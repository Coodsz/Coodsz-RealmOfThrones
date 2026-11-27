using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace BannerlordCoopLauncher.Core.Support;

/// <summary>
/// What the log pack keeps after the server ended with exit code 1 and no stop was asked for
/// (crash repo issue #12): the Windows Application, System and Defender event log entries around
/// the exit, whether Coodsz Host ended the process itself, and the last minute of the engine log.
/// An outside kill (antivirus, an Insider build) and the process ending itself look the same in
/// the server's own logs, so this is what tells them apart. The zip cleans the text like every
/// other file, so user names, PC names, Steam ids and addresses never leave the PC.
/// </summary>
public static class ServerExitEvidence
{
    public const string EntryName = "host/exit-evidence.txt";
    public static readonly TimeSpan EngineMinute = TimeSpan.FromMinutes(1);
    public const int MaxEngineLines = 400;

    public const string ApplicationLog = "Application";
    public const string SystemLog = "System";
    public const string DefenderLog = "Microsoft-Windows-Windows Defender/Operational";
    public static readonly IReadOnlyList<string> Logs = [ApplicationLog, SystemLog, DefenderLog];

    // Defender found, removed, quarantined or blocked something (malware, a suspicious behaviour,
    // Controlled folder access). Any of these near the exit counts, whatever it names.
    private static readonly HashSet<long> DefenderIds = [1006, 1007, 1008, 1015, 1116, 1117, 1118, 1119, 1123, 1124];

    // Windows ran low on memory or the page file and may have closed the biggest process.
    private static readonly string[] ResourceSources = ["Microsoft-Windows-Resource-Exhaustion-Detector", "Resource-Exhaustion-Detector"];

    private static readonly Regex EngineTime = new(@"^\[(\d{2}):(\d{2}):(\d{2})\.(\d{3})\]", RegexOptions.Compiled);

    /// <summary>The same time window as the crash report's event log: 2 minutes before to 1 minute after.</summary>
    public static bool InWindow(DateTime time, DateTime exitAt) => ServerEventLog.InWindow(time, exitAt);

    /// <summary>
    /// An entry worth keeping: anything that names a server process (Application Error, Windows
    /// Error Reporting, Application Hang, .NET Runtime, Application Popup), a Defender detection or
    /// remediation, or a low memory warning. Other programs' crashes are left out.
    /// </summary>
    public static bool Relevant(EventLogEntryInfo entry)
    {
        if (string.Equals(entry.Log, DefenderLog, StringComparison.OrdinalIgnoreCase))
            return DefenderIds.Contains(entry.EventId);
        if (ResourceSources.Any(source => string.Equals(entry.Source, source, StringComparison.OrdinalIgnoreCase)))
            return true;
        return ServerEventLog.NamesServer(entry.Message);
    }

    /// <summary>The kept entries in the time window, oldest first.</summary>
    public static IReadOnlyList<EventLogEntryInfo> Select(IEnumerable<EventLogEntryInfo> entries, DateTime exitAt) =>
        entries.Where(entry => InWindow(entry.Time, exitAt) && Relevant(entry))
            .OrderBy(entry => entry.Time)
            .ToList();

    /// <summary>
    /// The engine log lines from the minute before its last time stamp, oldest first. A line with
    /// no time stamp (a stack trace line) goes with the line above it. The engine log only has the
    /// time of day, so a log that runs past midnight is read as one day later.
    /// </summary>
    public static string? EngineLastMinute(string? log)
    {
        if (string.IsNullOrWhiteSpace(log))
            return null;
        var lines = log.Replace("\r", "").TrimEnd('\n').Split('\n');
        var times = new TimeSpan?[lines.Length];
        var day = TimeSpan.Zero;
        TimeSpan? previous = null;
        for (var i = 0; i < lines.Length; i++)
        {
            var match = EngineTime.Match(lines[i]);
            if (!match.Success)
                continue;
            var time = new TimeSpan(0, int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture), int.Parse(match.Groups[2].Value, CultureInfo.InvariantCulture),
                int.Parse(match.Groups[3].Value, CultureInfo.InvariantCulture), int.Parse(match.Groups[4].Value, CultureInfo.InvariantCulture)) + day;
            if (previous is { } last && time < last - TimeSpan.FromHours(12))
            {
                day += TimeSpan.FromDays(1);
                time += TimeSpan.FromDays(1);
            }

            times[i] = previous = time;
        }

        if (previous is not { } end)
            return ServerEventLog.Tail(log, 50);
        var from = end - EngineMinute;
        var first = lines.Length;
        for (var i = lines.Length - 1; i >= 0; i--)
        {
            if (times[i] is { } time && time < from)
                break;
            first = i;
        }

        var kept = lines.Skip(Math.Max(first, lines.Length - MaxEngineLines));
        return string.Join(Environment.NewLine, kept);
    }

    /// <summary>The text file for the zip.</summary>
    public static string Format(DateTime exitAt, string hostLine, IReadOnlyList<EventLogEntryInfo>? entries, IReadOnlyList<string> unreadLogs, string? engineMinute, string? engineErrors = null)
    {
        var text = new StringBuilder();
        text.AppendLine("The server process ended with exit code 1 at " + Stamp(exitAt) + " and no stop was asked for.");
        text.AppendLine(hostLine);
        text.AppendLine();
        text.AppendLine("Windows event log entries from " + Stamp(exitAt - ServerEventLog.Before) + " to " + Stamp(exitAt + ServerEventLog.After)
            + " (" + string.Join(", ", Logs) + "): Application Error, Windows Error Reporting, Application Hang and .NET Runtime entries that name the server process, Defender detections and low memory warnings.");
        foreach (var log in unreadLogs)
            text.AppendLine("The " + log + " log could not be read.");
        if (entries == null)
            text.AppendLine("The event logs could not be read.");
        else if (entries.Count == 0)
            text.AppendLine("None. Nothing in Windows says it closed the server, so it most likely ended itself.");
        foreach (var entry in entries ?? [])
        {
            text.AppendLine();
            text.AppendLine(Stamp(entry.Time) + "  " + entry.Log + "  " + entry.Level + "  " + entry.Source + "  event " + entry.EventId);
            text.AppendLine(entry.Message.Trim());
        }

        text.AppendLine();
        if (engineMinute == null)
        {
            text.AppendLine("The engine log could not be found.");
        }
        else
        {
            text.AppendLine("The last minute of the engine log:");
            text.AppendLine(engineMinute);
        }

        if (engineErrors != null)
        {
            text.AppendLine();
            text.AppendLine("The end of the engine error log (rgl_log_errors):");
            text.AppendLine(engineErrors);
        }

        return text.ToString();
    }

    private static string Stamp(DateTime time) => time.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
}
