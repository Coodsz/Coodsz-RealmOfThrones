using System.Text;
using BannerlordCoopLauncher.Core.Launch;
using BannerlordCoopLauncher.Core.Support;
using TaomCoop.Core;

namespace RealmOfThrones.Core;

// The Realm of Thrones server tool's log pack for Send my logs and Send server crash report, built like the
// Burning Empires one (BeHostLogPack): Coop_server.log, the console the window captured, Realm of Thrones Coop
// Compat's own log, the rest of the server's logs, engine logs and crash reports from the dedicated server,
// server-config.json without the password. Crash dumps only when the host agreed.
public static class RotHostLogPack
{
    public const int DumpCount = 2;
    public const string Title = "Realm of Thrones host log pack";

    /// <summary>serverRoot: the Coop nightly server folder. dataRoot: its data folder (BANNERLORD_USER_DIR), the
    /// one with server-config.json and logs.</summary>
    public static ClientLogPackResult Create(
        string zipPath,
        string? serverRoot,
        string? dataRoot,
        string programDataRoot,
        string? windowLog = null,
        bool includeDumps = false,
        string? windowsDumpRoot = null,
        LogZipLimits? limits = null,
        string? toolFolder = null)
    {
        var zip = new LogZipBuilder(limits);
        var logs = ServerLogs(dataRoot);
        // Coop_server.log first, so the newest 12 below can never push it out.
        zip.AddNewest(LogZipBuilder.Files(logs, CoopServerLogPattern), "server-logs", 3);
        zip.AddNewest(LogZipBuilder.Files(logs, ConsoleLogFile.Pattern), "host-console", 3);
        zip.AddFile(logs == null ? null : Path.Combine(logs, "host-action.log"), "host/host-action.log");
        var compatLogs = RotClientLogPack.ServerCompatLogs(logs);
        zip.AddNewest(LogZipBuilder.Files(compatLogs, RotClientLogPack.CompatLogPattern), "compat", 2);
        zip.AddNewest(LogZipBuilder.Files(logs, "*.log", recursive: true)
            .Where(path => !Path.GetFileName(path).StartsWith("Coop_server", StringComparison.OrdinalIgnoreCase)
                && !Path.GetFileName(path).StartsWith("host-console-", StringComparison.OrdinalIgnoreCase)
                && !Path.GetFileName(path).Equals("host-action.log", StringComparison.OrdinalIgnoreCase)
                && !string.Equals(Path.GetDirectoryName(path), compatLogs, StringComparison.OrdinalIgnoreCase)), "server-logs", 12);
        zip.AddNewest(LogZipBuilder.Files(logs, "*.txt", recursive: true), "server-logs", 8);
        // A Coop_server.log the server wrote next to its exe, when it ran without the data folder.
        zip.AddCoopServerLogs(serverRoot, "server-folder/coop-logs");
        var engineLogs = HostLogPack.ProgramDataLogs(programDataRoot);
        var engine = LogZipBuilder.ServerEngineLogs(engineLogs, 6);
        foreach (var log in engine)
        {
            zip.AddFile(log, "engine-logs/" + Path.GetFileName(log));
            // The watchdog log of the same session names the build and the module list.
            var watchdog = "watchdog_log_" + ClientLogZip.SessionId(log) + ".txt";
            zip.AddFile(Path.Combine(engineLogs, watchdog), "engine-logs/" + watchdog);
        }

        if (engine.Count > 0 && !engine.Any(LogZipBuilder.IsServerEngineLog))
            zip.Skip("No engine log was from the dedicated server, so the newest engine logs were included.");

        zip.AddFile(dataRoot == null ? null : ServerConfig(dataRoot), "host/server-config.redacted.json");
        var crashFolders = LogZipBuilder.ServerCrashFolders(Path.Combine(programDataRoot, "crashes"));
        zip.AddCrashReports(crashFolders.Take(3));
        if (includeDumps)
            zip.AddDumps(LogZipBuilder.ServerDumps(crashFolders, windowsDumpRoot, DumpCount));
        zip.AddText("host/server-window.txt", windowLog);
        zip.AddToolUpdateLogs(toolFolder, "host");

        var result = zip.Write(zipPath, Notes(includeDumps));
        return new ClientLogPackResult(result.Included, result.Skipped) { DumpIncluded = result.DumpIncluded };
    }

    public const string CoopServerLogPattern = "Coop_server*.log";

    /// <summary>The one dump a server crash report or Send my logs may carry, the newest between sinceUtc and
    /// untilUtc, the same way as the Burning Empires server tool (BeHostLogPack.LatestDump).</summary>
    public static string? LatestDump(string programDataRoot, string? windowsDumpRoot, DateTime sinceUtc, DateTime untilUtc) =>
        CrashDumps.LatestServerDump(Path.Combine(programDataRoot, "crashes"), windowsDumpRoot, sinceUtc, untilUtc, HostLogPack.ProgramDataLogs(programDataRoot));

    public static string? ServerLogs(string? dataRoot) =>
        string.IsNullOrWhiteSpace(dataRoot) ? null : Path.Combine(dataRoot, "logs");

    public static string ServerConfig(string dataRoot) => Path.Combine(dataRoot, "server-config.json");

    private static string Notes(bool includeDumps)
    {
        var builder = new StringBuilder();
        builder.AppendLine(Title + ", made " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
        builder.AppendLine("Saves and shader caches are left out. Addresses, Steam ids and passwords are removed from the text files.");
        builder.AppendLine(includeDumps
            ? "Crash dumps (.dmp) are copied as they are. A dump is a snapshot of the server's memory and can hold the password and player addresses, so only send this zip to someone you trust."
            : "Crash dumps were left out.");
        return builder.ToString();
    }
}
