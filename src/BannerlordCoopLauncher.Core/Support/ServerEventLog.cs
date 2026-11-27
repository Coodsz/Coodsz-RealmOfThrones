using System.Globalization;
using System.Text;

namespace BannerlordCoopLauncher.Core.Support;

/// <summary>One entry of a Windows event log (Application unless Log says another), as the crash report keeps it.</summary>
public sealed record EventLogEntryInfo(DateTime Time, string Source, long EventId, string Level, string Message, string Log = "Application");

/// <summary>
/// The Windows Application event log entries about the server process around a crash, for the
/// server crash report. A server that ends with no error (exit code 1) leaves nothing in its own
/// logs, but Windows Error Reporting, Application Error and the .NET Runtime often write why.
/// The report only keeps entries that name the server's process, from 2 minutes before to 1
/// minute after the exit. The zip cleans the text like every other file.
/// </summary>
public static class ServerEventLog
{
    public static readonly TimeSpan Before = TimeSpan.FromMinutes(2);
    public static readonly TimeSpan After = TimeSpan.FromMinutes(1);

    /// <summary>The processes a Coop dedicated server runs as.</summary>
    public static readonly IReadOnlyList<string> ProcessNames = ["dotnet.exe", "BannerlordCoopServer.exe", "TaleWorlds.Starter.DotNetCore.exe"];

    public const string EntryName = "host/windows-event-log.txt";
    public const string EngineTailName = "engine-logs/rgl-log-last-200-lines.txt";
    public const int EngineTailLines = 200;

    public static bool InWindow(DateTime time, DateTime exitAt) =>
        time >= exitAt - Before && time <= exitAt + After;

    public static bool NamesServer(string? message) =>
        !string.IsNullOrEmpty(message) && ProcessNames.Any(name => message.Contains(name, StringComparison.OrdinalIgnoreCase));

    /// <summary>The entries in the time window that name a server process, oldest first.</summary>
    public static IReadOnlyList<EventLogEntryInfo> Select(IEnumerable<EventLogEntryInfo> entries, DateTime exitAt) =>
        entries.Where(entry => InWindow(entry.Time, exitAt) && NamesServer(entry.Message))
            .OrderBy(entry => entry.Time)
            .ToList();

    /// <summary>The text file for the zip, or null when no entry matched.</summary>
    public static string? Format(IReadOnlyList<EventLogEntryInfo> entries, DateTime exitAt)
    {
        if (entries.Count == 0)
            return null;
        var text = new StringBuilder();
        text.AppendLine("Windows Application event log entries that name the server process, from "
            + (exitAt - Before).ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture) + " to "
            + (exitAt + After).ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture) + ". The server exited at "
            + exitAt.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture) + ".");
        foreach (var entry in entries)
        {
            text.AppendLine();
            text.AppendLine(entry.Time.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture) + "  " + entry.Level + "  " + entry.Source + "  event " + entry.EventId);
            text.AppendLine(entry.Message.Trim());
        }

        return text.ToString();
    }

    /// <summary>The last lines of a log text.</summary>
    public static string? Tail(string? text, int lines = EngineTailLines)
    {
        if (string.IsNullOrWhiteSpace(text))
            return null;
        var all = text.Replace("\r", "").TrimEnd('\n').Split('\n');
        return string.Join(Environment.NewLine, all.Skip(Math.Max(0, all.Length - lines)));
    }
}
