using System.Text;
using BannerlordCoopLauncher.Core.Steam;
using BannerlordCoopLauncher.Core.Support;

namespace BannerlordCoopLauncher.Core.Launch;

public sealed record ClientLogZipResult(int Included, IReadOnlyList<string> Skipped)
{
    public bool DumpIncluded { get; init; }
}

// Extra files only the TAOM client packs. Extra adds the TAOM server logs and stall reports
// found on this PC; ExtraNotes says so at the top of NOTES.txt.
public sealed record ClientLogZipTaom(string CompatLogs, string? PatchshieldFlag, Action<LogZipBuilder>? Extra = null, string? ExtraNotes = null)
{
    /// <summary>Above 0, BootPatches.log keeps only its last this many bytes (the Burning Empires pack).</summary>
    public long BootPatchesMaxBytes { get; init; }

    /// <summary>File name starts in the game's Logs folder that this pack leaves out, like
    /// taom_debug_ in the Burning Empires pack, so another overhaul's old logs do not fill the zip.</summary>
    public IReadOnlyList<string> LeaveOutGameLogs { get; init; } = Array.Empty<string>();

    public bool LeavesOut(string path) =>
        LeaveOutGameLogs.Any(start => Path.GetFileName(path).StartsWith(start, StringComparison.OrdinalIgnoreCase));
}

// The client log pack behind Send my logs and Send crash report: Coop_client.log, the launcher's
// action log, crash reports, engine logs, hard crashes and the game's crash
// dumps. Text files are cleaned (addresses, Steam ids, passwords) and cut to
// their last 20 MB. Saves and shader caches are never added.
public static class ClientLogZip
{
    public const long MaxFileBytes = LogZipBuilder.MaxTextBytes;

    public static ClientLogZipResult Create(
        string zipPath,
        string? gameRoot,
        string documentsRoot,
        string programDataRoot,
        string? windowsDumpRoot = null,
        string? activityLog = null,
        string? actionLogPath = null,
        ClientLogZipTaom? taom = null,
        string title = "Coodsz client logs",
        LogZipLimits? limits = null,
        string? toolFolder = null)
    {
        var zip = new LogZipBuilder(limits);
        var gameBin = GameBin(gameRoot);
        zip.AddFile(Existing(gameBin, "Coop_client.log"), "game/Coop_client.log");
        zip.AddNewest(CoopProcessLogs(gameBin), "game", CoopProcessLogCount);
        if (taom is { BootPatchesMaxBytes: > 0 })
            zip.AddFile(Existing(gameBin, "BootPatches.log"), "game/BootPatches.log", taom.BootPatchesMaxBytes);
        else
            zip.AddFile(Existing(gameBin, "BootPatches.log"), "game/BootPatches.log");
        zip.AddFile(Existing(gameBin, "NoHarmony.txt"), "game/NoHarmony.txt");
        zip.AddNewest(LogZipBuilder.Files(gameBin, "rgl_log_*.txt"), "game", 6);
        var gameLogs = gameBin == null ? null : Path.Combine(gameBin, "Logs");
        zip.AddNewest(LogZipBuilder.Files(gameLogs, "*.log").Where(path => taom?.LeavesOut(path) != true), "game-logs", 6);
        zip.AddNewest(LogZipBuilder.Files(gameLogs, "*.txt").Where(path => taom?.LeavesOut(path) != true), "game-logs", 6);

        var reports = CrashReports(documentsRoot);
        zip.AddNewest(LogZipBuilder.Files(reports, "*.txt"), "crash-reports", 10);
        zip.AddNewest(LogZipBuilder.Files(reports, "*.log"), "crash-reports", 10);
        if (taom != null)
            AddNewestArchive(zip, reports);
        AddReportFolders(zip, reports);

        if (taom == null)
        {
            zip.AddNewest(LogZipBuilder.Files(ProgramDataLogs(programDataRoot), "rgl_log_*.txt"), "engine-logs", 8);
            zip.AddNewest(LogZipBuilder.Files(ProgramDataLogs(programDataRoot), "watchdog_log_*.txt"), "engine-logs", 4);
        }
        else
        {
            foreach (var log in EngineSessionFiles(ProgramDataLogs(programDataRoot)))
                zip.AddFile(log, "engine-logs/" + Path.GetFileName(log));
        }
        var hardCrashes = ProgramDataCrashes(programDataRoot);
        zip.AddNewest(LogZipBuilder.Files(hardCrashes, "*.txt", recursive: true), "hard-crashes", 10);
        foreach (var dump in LogZipBuilder.Files(hardCrashes, "*.dmp", recursive: true).OrderByDescending(LogZipBuilder.SafeWriteTime).Take(2))
            zip.AddDump(dump, "hard-crashes/" + Path.GetFileName(dump));
        foreach (var dump in LogZipBuilder.Files(windowsDumpRoot, "Bannerlord.exe*.dmp").OrderByDescending(LogZipBuilder.SafeWriteTime).Take(2))
            zip.AddDump(dump, "windows-crash-dumps/" + Path.GetFileName(dump));

        if (taom != null)
        {
            zip.AddNewest(LogZipBuilder.Files(taom.CompatLogs, "coopcompat*.log*"), "compat", 8);
            if (taom.PatchshieldFlag != null && File.Exists(taom.PatchshieldFlag))
                zip.AddText("modules/" + Path.GetFileName(taom.PatchshieldFlag), "This marker file is present (it is empty). Fix low FPS is on.");
            taom.Extra?.Invoke(zip);
        }

        // The Join Password module's own log: the password fill and the map check guard's skipped parties
        // (compat\CoodszJoinPassword, JoinPasswordSubModule.Log). It never holds the password itself.
        zip.AddFile(Existing(ClientDiagnostics(documentsRoot), JoinPasswordLog), "client-diagnostics/" + JoinPasswordLog);
        zip.AddFile(actionLogPath, "launcher/client-action.log");
        zip.AddText("launcher/activity-log.txt", activityLog);
        zip.AddToolUpdateLogs(toolFolder, "launcher");
        var result = zip.Write(zipPath, Notes(title, gameRoot, documentsRoot, programDataRoot, taom?.ExtraNotes));
        return new ClientLogZipResult(result.Included, result.Skipped) { DumpIncluded = result.DumpIncluded };
    }

    public const int CoopProcessLogCount = 3;

    /// <summary>
    /// Coop 0.1.7's Coop_client_&lt;pid&gt;.log files in the client bin folder (ClientJoinLog), only real .log files:
    /// on Windows the *.log pattern also takes Coop_client_&lt;pid&gt;.log.lock.
    /// </summary>
    public static IEnumerable<string> CoopProcessLogs(string? gameBin) =>
        LogZipBuilder.Files(gameBin, "Coop_client_*.log")
            .Where(path => Path.GetExtension(path).Equals(".log", StringComparison.OrdinalIgnoreCase));

    public const int GameSessions = 4;
    public const int ServerSessions = 2;

    // The newest game sessions and the newest dedicated server sessions on this PC, each with
    // its full engine log, its errors log and its watchdog log (the watchdog names the build
    // and the module list). Picking the newest files by name alone let the errors logs push
    // the full logs out, and a server's full log was left behind.
    public static IReadOnlyList<string> EngineSessionFiles(string? engineLogRoot, int gameSessions = GameSessions, int serverSessions = ServerSessions)
    {
        var full = LogZipBuilder.Files(engineLogRoot, "rgl_log_*.txt")
            .Where(path => !Path.GetFileName(path).StartsWith("rgl_log_errors_", StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(LogZipBuilder.SafeWriteTime)
            .Take(40)
            .Select(path => (Path: path, Server: LogZipBuilder.IsServerEngineLog(path)))
            .ToList();
        var watchdogs = LogZipBuilder.Files(engineLogRoot, "watchdog_log_*.txt")
            .OrderByDescending(LogZipBuilder.SafeWriteTime)
            .Take(40)
            .Where(LogZipBuilder.IsServerWatchdog);
        var sessions = full.Where(log => !log.Server).Take(gameSessions).Select(log => SessionId(log.Path)).ToList();
        // A server whose full log is gone still left its watchdog log.
        sessions.AddRange(full.Where(log => log.Server).Select(log => log.Path)
            .Concat(watchdogs)
            .OrderByDescending(LogZipBuilder.SafeWriteTime)
            .Select(SessionId)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(serverSessions));
        var files = new List<string>();
        foreach (var session in sessions.Where(id => id.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            foreach (var name in new[] { "rgl_log_" + session + ".txt", "rgl_log_errors_" + session + ".txt", "watchdog_log_" + session + ".txt" })
            {
                var path = Existing(engineLogRoot, name);
                if (path != null)
                    files.Add(path);
            }
        }

        return files;
    }

    // rgl_log_1234.txt, rgl_log_errors_1234.txt and watchdog_log_1234.txt are session 1234.
    public static string SessionId(string path)
    {
        var name = Path.GetFileNameWithoutExtension(path);
        var cut = name.LastIndexOf('_');
        return cut < 0 ? "" : name[(cut + 1)..];
    }

    public static string CrashReports(string documentsRoot) =>
        Path.Combine(documentsRoot, "Coop Crash Reports");

    public const string JoinPasswordLog = "CoodszJoinPassword.log";

    public static string ClientDiagnostics(string documentsRoot) =>
        Path.Combine(documentsRoot, "Client Diagnostics");

    public static string ProgramDataLogs(string programDataRoot) =>
        Path.Combine(programDataRoot, "logs");

    public static string ProgramDataCrashes(string programDataRoot) =>
        Path.Combine(programDataRoot, "crashes");

    private static string? GameBin(string? gameRoot) =>
        string.IsNullOrWhiteSpace(gameRoot) ? null : Path.Combine(gameRoot, "bin", "Win64_Shipping_Client");

    private static string? Existing(string? directory, string name)
    {
        if (string.IsNullOrWhiteSpace(directory))
            return null;
        var path = Path.Combine(directory, name);
        return File.Exists(path) ? path : null;
    }

    private static void AddNewestArchive(LogZipBuilder zip, string reports)
    {
        var archive = LogZipBuilder.Files(reports, "*.*")
            .Where(path => path.EndsWith(".rar", StringComparison.OrdinalIgnoreCase) || path.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(LogZipBuilder.SafeWriteTime)
            .FirstOrDefault();
        if (archive == null)
            return;
        // A rar cannot be opened without a library, so it cannot be cleaned and is not packed.
        if (archive.EndsWith(".rar", StringComparison.OrdinalIgnoreCase))
        {
            zip.Skip(Path.GetFileName(archive) + " was left out because a rar file cannot be cleaned of passwords and addresses. It is in " + reports + " if you are asked for it.");
            return;
        }

        if (new FileInfo(archive).Length > MaxFileBytes)
        {
            zip.Skip(Path.GetFileName(archive) + " was left out because it is larger than 20 MB.");
            return;
        }

        // Unpacked so the text in it is cleaned like the other crash reports.
        zip.AddZipContents(archive, "crash-reports/" + Path.GetFileNameWithoutExtension(archive));
    }

    /// <summary>A crash report folder older than this is from another problem and is left out.</summary>
    public static readonly TimeSpan ReportFolderWindow = TimeSpan.FromDays(7);

    // The newest crash report folder from the last 7 days, as it is. Older folders are from earlier
    // runs and only repeat the same kind of logs. Text is cleaned, dumps and other binary files are
    // copied byte for byte, and the game's shareable.zip in it adds only what the folder lacks.
    private static void AddReportFolders(LogZipBuilder zip, string root)
    {
        if (!Directory.Exists(root))
            return;
        IEnumerable<string> folders;
        try
        {
            folders = Directory.GetDirectories(root)
                .Where(folder => Directory.GetLastWriteTimeUtc(folder) > DateTime.UtcNow - ReportFolderWindow)
                .OrderByDescending(Directory.GetLastWriteTimeUtc)
                .Take(1)
                .ToList();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return;
        }

        foreach (var folder in folders)
        {
            var name = Path.GetFileName(folder);
            foreach (var path in LogZipBuilder.Files(folder, "*", recursive: true))
            {
                var relative = Path.GetRelativePath(folder, path).Replace('\\', '/');
                var entry = "crash-reports/" + name + "/" + relative;
                var file = Path.GetFileName(path);
                if (file.Contains(".sav", StringComparison.OrdinalIgnoreCase)
                    || file.Contains(".sack", StringComparison.OrdinalIgnoreCase)
                    || file.EndsWith(".lock", StringComparison.OrdinalIgnoreCase))
                {
                    zip.Skip(file + " was left out.");
                    continue;
                }

                // Every text kind is cleaned (an .ini or .cfg used to be copied as it is), and a
                // zip in the folder is unpacked so the logs in it are cleaned too.
                if (LogZipBuilder.IsTextName(file))
                    zip.AddFile(path, entry);
                else if (file.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
                    zip.AddZipContents(path, entry);
                else
                    zip.AddDump(path, entry);
            }
        }
    }

    private static string Notes(string title, string? gameRoot, string documentsRoot, string programDataRoot, string? extra)
    {
        var builder = new StringBuilder();
        builder.AppendLine(title + ", made " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
        builder.AppendLine("This zip is the full pack: Coop_client.log, client-action.log, crash reports, engine logs, and hard crashes when present.");
        if (!string.IsNullOrWhiteSpace(extra))
            builder.AppendLine(extra.TrimEnd());
        builder.AppendLine("Saves are left out. Addresses, Steam ids and passwords are removed from the text files. A text file larger than 20 MB keeps only its end.");
        builder.AppendLine("Crash dumps (.dmp) are copied as they are. A dump is a snapshot of the game's memory, so only send this zip to someone you trust.");
        builder.AppendLine("Game: " + (string.IsNullOrWhiteSpace(gameRoot) ? "(not found)" : ProblemReport.Redact(gameRoot)));
        if (NonSteamCopy.Note(gameRoot) is { } nonSteam)
            builder.AppendLine(nonSteam);
        builder.AppendLine("Crash reports: " + CrashReports(documentsRoot));
        builder.AppendLine("Engine logs: " + ProgramDataLogs(programDataRoot));
        builder.AppendLine("Hard crashes: " + ProgramDataCrashes(programDataRoot));
        return builder.ToString();
    }
}
