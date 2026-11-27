using System.Text;
using BannerlordCoopLauncher.Core.Launch;
using BannerlordCoopLauncher.Core.Support;

namespace TaomCoop.Core;

// The TAOM host's log pack for Send my logs and Send server crash report: server logs, what the window
// captured from the server console, the TAOM debug log, engine logs from the
// dedicated server, server crash reports, and the settings without the
// password. Crash dumps only when the host agreed.
public static class HostLogPack
{
    public const long MaxFileBytes = LogZipBuilder.MaxTextBytes;
    public const int DumpCount = 2;

    public static ClientLogPackResult Create(
        string zipPath,
        string? serverRoot,
        string? dataRoot,
        string programDataRoot,
        string? windowLog = null,
        bool includeDumps = false,
        string? windowsDumpRoot = null,
        LogZipLimits? limits = null,
        string? modLogsRoot = null,
        string? toolFolder = null,
        IReadOnlyList<string>? steamLibraries = null)
    {
        var zip = new LogZipBuilder(limits);
        var logs = ServerLogs(dataRoot);
        zip.AddNewest(LogZipBuilder.Files(logs, ConsoleLogFile.Pattern), "host-console", 3);
        zip.AddFile(logs == null ? null : Path.Combine(logs, "host-action.log"), "host/host-action.log");
        zip.AddNewest(LogZipBuilder.Files(logs, "*.log", recursive: true)
            .Where(path => !Path.GetFileName(path).StartsWith("host-console-", StringComparison.OrdinalIgnoreCase)
                && !Path.GetFileName(path).Equals("host-action.log", StringComparison.OrdinalIgnoreCase)), "server-logs", 12);
        zip.AddNewest(LogZipBuilder.Files(logs, "*.txt", recursive: true), "server-logs", 8);
        zip.AddNewest(LogZipBuilder.Files(TaomDebugLogs(serverRoot), "taom_debug_*.log", recursive: true), "taom-debug", 2);
        // Coop 0.1.6 writes Coop_server.log into the engine folder the server runs from, apart from the
        // TAOM.CoopCompat copies the logs folder above already has.
        zip.AddCoopServerLogs(serverRoot, "server-folder/coop-logs");
        var engineLogs = ProgramDataLogs(programDataRoot);
        var engine = LogZipBuilder.ServerEngineLogs(engineLogs, 6);
        foreach (var log in engine)
        {
            zip.AddFile(log, "engine-logs/" + Path.GetFileName(log));
            // The watchdog log of the same session names the build and the module list.
            var watchdog = "watchdog_log_" + ClientLogZip.SessionId(log) + ".txt";
            zip.AddFile(Path.Combine(engineLogs, watchdog), "engine-logs/" + watchdog);
        }

        if (engine.Count > 0 && !engine.Any(IsServerEngineLog))
            zip.Skip("No engine log was from the dedicated server, so the newest engine logs were included.");

        zip.AddFile(SmallText(serverRoot, "TAOM-QUALIFICATION.json"), "host/TAOM-QUALIFICATION.json");
        var dependencies = DependencyFolder(serverRoot);
        zip.AddFile(SmallText(dependencies, "diag.log"), "taom-dependencies/diag.log");
        zip.AddFile(SmallText(dependencies, "coop-modules.txt"), "taom-dependencies/coop-modules.txt");
        if (SmallText(dependencies, Patchshield.FileName) != null)
            zip.AddText("taom-dependencies/" + Patchshield.FileName, "This marker file is present (it is empty).");
        zip.AddFile(dataRoot == null ? null : Path.Combine(dataRoot, "server-config.json"), "host/server-config.redacted.json");
        // CoopModPatch (TAOM+ with Improved Garrisons): its logs and the settings in server-data.
        zip.AddCoopModPatch(modLogsRoot ?? LogZipBuilder.ModLogsRoot(), new[]
        {
            string.IsNullOrWhiteSpace(serverRoot) ? null : Path.Combine(serverRoot, "server-data"),
            string.IsNullOrWhiteSpace(dataRoot) ? null : Path.Combine(dataRoot, "server-data")
        });

        var crashFolders = LogZipBuilder.ServerCrashFolders(Path.Combine(programDataRoot, "crashes"));
        zip.AddCrashReports(crashFolders.Take(3));
        if (includeDumps)
            zip.AddDumps(LogZipBuilder.ServerDumps(crashFolders, windowsDumpRoot, DumpCount));
        zip.AddText("host/server-window.txt", windowLog);
        zip.AddToolUpdateLogs(toolFolder, "host");
        // What this PC has of the Workshop Coop server, so a "not found" report shows which case it is (CR-P75R3).
        zip.AddText(WorkshopCoopFacts.Entry, WorkshopCoopFacts.ForPack(steamLibraries, BannerlordFinder.SteamLibraries));

        var result = zip.Write(zipPath, Notes(includeDumps));
        return new ClientLogPackResult(result.Included, result.Skipped) { DumpIncluded = result.DumpIncluded };
    }

    public static IReadOnlyList<string> Dumps(string programDataRoot, string? windowsDumpRoot) =>
        LogZipBuilder.ServerDumps(LogZipBuilder.ServerCrashFolders(Path.Combine(programDataRoot, "crashes")), windowsDumpRoot, DumpCount);

    public static string? ServerLogs(string? dataRoot) =>
        string.IsNullOrWhiteSpace(dataRoot) ? null : Path.Combine(dataRoot, "logs");

    public static string? TaomDebugLogs(string? serverRoot) =>
        string.IsNullOrWhiteSpace(serverRoot) ? null : Path.Combine(serverRoot, "engine", "bin", "Win64_Shipping_Server", "Logs");

    public static string ProgramDataLogs(string programDataRoot) =>
        Path.Combine(programDataRoot, "logs");

    private static bool IsServerEngineLog(string path)
    {
        try
        {
            using var reader = new StreamReader(new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite));
            var buffer = new char[16000];
            var count = reader.Read(buffer, 0, buffer.Length);
            var text = new string(buffer, 0, count);
            return text.Contains("DedicatedServer", StringComparison.OrdinalIgnoreCase)
                || text.Contains("Win64_Shipping_Server", StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    private static string? DependencyFolder(string? serverRoot) =>
        string.IsNullOrWhiteSpace(serverRoot) ? null : Path.Combine(serverRoot, "engine", "Modules", "TAOM.Dependencies");

    private static string? SmallText(string? directory, string name)
    {
        if (string.IsNullOrWhiteSpace(directory))
            return null;
        var path = Path.Combine(directory, name);
        return File.Exists(path) ? path : null;
    }

    private static string Notes(bool includeDumps)
    {
        var builder = new StringBuilder();
        builder.AppendLine("TAOM host log pack, made " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
        builder.AppendLine("Saves and shader caches are left out. Addresses, Steam ids and passwords are removed from the text files.");
        builder.AppendLine(includeDumps
            ? "Crash dumps (.dmp) are copied as they are. A dump is a snapshot of the server's memory and can hold the password and player addresses, so only send this zip to someone you trust."
            : "Crash dumps were left out.");
        return builder.ToString();
    }
}
