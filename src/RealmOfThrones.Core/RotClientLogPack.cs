using BannerlordCoopLauncher.Core.Launch;
using BannerlordCoopLauncher.Core.Support;
using TaomCoop.Core;

namespace RealmOfThrones.Core;

// The Realm of Thrones client's log zip, built like the Burning Empires one (BeClientLogPack): the shared client
// pack (ClientLogZip, with the Join Password module's log) plus Realm of Thrones Coop Compat's own log in compat,
// and, when the Realm of Thrones server tool runs on the same PC, that server's Coop_server.log (from its data
// logs and its server folder), console and Coop Compat logs in server. Its join password comes out of every text file.
public static class RotClientLogPack
{
    public const long MaxFileBytes = ClientLogZip.MaxFileBytes;
    public const long ServerLogTotalBytes = 60_000_000;
    public const string Title = "Realm of Thrones client logs";

    // The compat module's folder under Logs (CompatLogFile.FolderName in the module).
    public const string CompatLogFolder = RotStack.CompatModuleId;
    public const string CompatLogPattern = "coopcompat*.log";

    // Other overhauls' debug logs in the game's Logs folder say nothing about Realm of Thrones.
    public static readonly IReadOnlyList<string> LeftOutGameLogs = ["taom_debug_"];

    public const string ExtraNotes =
        "Realm of Thrones: Realm of Thrones Coop Compat's own log is in compat. When the Realm of Thrones server tool is on this PC, its Coop_server.log, coop-server, host-console and Coop Compat logs are in server, and the Coop_server.log its server folder holds is in server/coop-logs.";

    /// <summary>serverDataRoot: the data folder of the Realm of Thrones server tool on this PC (its rot-server.json
    /// DataRoot), or null. serverRoot: that tool's server folder (C:\RotServer or the one the host chose), where
    /// Coop writes Coop_server.log, or null.</summary>
    public static ClientLogPackResult Create(
        string zipPath,
        string? gameRoot,
        string documentsRoot,
        string programDataRoot,
        string? windowsDumpRoot = null,
        string? activityLog = null,
        string? actionLogPath = null,
        string? serverDataRoot = null,
        string? serverRoot = null,
        LogZipLimits? limits = null)
    {
        limits = WithServerPassword(limits, serverDataRoot);
        var serverLogs = ServerLogs(serverDataRoot);
        void Extra(LogZipBuilder zip)
        {
            foreach (var log in serverLogs.Files)
                zip.AddFile(log.Path, log.Entry);
            // Coop writes Coop_server.log into the server folder's engine folder, apart from the logs above.
            zip.AddCoopServerLogs(serverRoot, "server/coop-logs");
            foreach (var line in serverLogs.Skipped)
                zip.Skip(line);
        }

        var result = ClientLogZip.Create(
            zipPath,
            gameRoot,
            documentsRoot,
            programDataRoot,
            windowsDumpRoot,
            activityLog,
            actionLogPath,
            new ClientLogZipTaom(CompatLogs(documentsRoot), null, Extra, ExtraNotes) { LeaveOutGameLogs = LeftOutGameLogs },
            Title,
            limits);
        return new ClientLogPackResult(result.Included, result.Skipped) { DumpIncluded = result.DumpIncluded };
    }

    // The server logs can repeat the join password, so the one in the server's server-config.json is taken out
    // of the text files along with the ones the caller knows.
    public static LogZipLimits WithServerPassword(LogZipLimits? limits, string? serverDataRoot)
    {
        var current = limits ?? LogZipLimits.Full;
        var password = ServerPassword(serverDataRoot);
        if (string.IsNullOrEmpty(password) || current.Secrets.Contains(password, StringComparer.Ordinal))
            return current;
        return current with { Secrets = current.Secrets.Append(password).ToList() };
    }

    /// <summary>The password in the server's server-config.json, where one is set.</summary>
    public static string? ServerPassword(string? serverDataRoot) =>
        string.IsNullOrWhiteSpace(serverDataRoot) ? null : ClientLogPack.ConfigPassword(RotHostLogPack.ServerConfig(serverDataRoot));

    // Where the compat module writes coopcompat.log on a game client (CompatLogFile.Folder in the module).
    public static string CompatLogs(string documentsRoot) => Path.Combine(documentsRoot, "Logs", CompatLogFolder);

    // The same on the dedicated server: its logs folder, from the BANNERLORD_USER_DIR the tool sets to the data folder.
    public static string? ServerCompatLogs(string? serverLogs) =>
        string.IsNullOrWhiteSpace(serverLogs) ? null : Path.Combine(serverLogs, CompatLogFolder);

    // The logs of the Realm of Thrones server on this PC, which the client folders do not have when the player
    // hosts and plays on one PC. Each keeps its last MaxFileBytes, and all of them stay under totalBytes.
    public static LogPick ServerLogs(string? serverDataRoot, long totalBytes = ServerLogTotalBytes)
    {
        var files = new List<(string Path, string Entry)>();
        var skipped = new List<string>();
        var logs = RotHostLogPack.ServerLogs(serverDataRoot);
        var picks = Newest(LogZipBuilder.Files(logs, RotHostLogPack.CoopServerLogPattern), 3, "server/logs/")
            .Concat(Newest(LogZipBuilder.Files(logs, "coop-server-*.log"), 3, "server/logs/"))
            .Concat(Newest(LogZipBuilder.Files(logs, ConsoleLogFile.Pattern), 2, "server/logs/"))
            .Concat(Newest(LogZipBuilder.Files(ServerCompatLogs(logs), CompatLogPattern), 2, "server/logs/compat/"));
        long total = 0;
        foreach (var pick in picks)
        {
            var length = Math.Min(Length(pick.Path), MaxFileBytes);
            if (length == 0)
                continue;
            if (total + length > totalBytes)
            {
                skipped.Add(Path.GetFileName(pick.Path) + " was left out so the zip stays small enough to send.");
                continue;
            }

            total += length;
            files.Add(pick);
        }

        return new LogPick(files, skipped);
    }

    private static IEnumerable<(string Path, string Entry)> Newest(IEnumerable<string> paths, int count, string folder) =>
        paths.OrderByDescending(LogZipBuilder.SafeWriteTime).Take(count).Select(path => (path, folder + Path.GetFileName(path)));

    private static long Length(string path)
    {
        try
        {
            return new FileInfo(path).Length;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return 0;
        }
    }
}
