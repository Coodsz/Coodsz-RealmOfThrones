using System.Text.Json;
using BannerlordCoopLauncher.Core.Launch;
using BannerlordCoopLauncher.Core.Support;

namespace TaomCoop.Core;

public sealed record ClientLogPackResult(int Included, IReadOnlyList<string> Skipped)
{
    public bool DumpIncluded { get; init; }
}

public sealed record LogPick(IReadOnlyList<(string Path, string Entry)> Files, IReadOnlyList<string> Skipped);

// The TAOM client's log zip: the shared client pack plus the TAOM.CoopCompat
// logs, the Fix low FPS marker, TAOM's own stall reports (taom_crash zips) and,
// when a TAOM server runs on the same PC, that server's logs.
public static class ClientLogPack
{
    public const long MaxFileBytes = ClientLogZip.MaxFileBytes;

    public const int StallReportCount = 3;
    public const int StallReportDays = 3;
    public const long StallReportBytes = 25_000_000;
    public const long StallReportTotalBytes = 60_000_000;
    public const long ServerLogTotalBytes = 60_000_000;

    public const string ExtraNotes =
        "TAOM: the stall reports TAOM wrote in the last 3 days (taom_crash zips, unpacked and cleaned) and, when a TAOM server is on this PC, its Coop_server.log, coop-server and host-console logs and taom_debug logs are in stall-reports and server.";

    public static ClientLogPackResult Create(
        string zipPath,
        string? gameRoot,
        string documentsRoot,
        string programDataRoot,
        string? windowsDumpRoot = null,
        string? activityLog = null,
        string? actionLogPath = null,
        IReadOnlyList<TaomServerFolder>? servers = null,
        DateTime? nowUtc = null,
        LogZipLimits? limits = null)
    {
        var stalls = StallReports(gameRoot, nowUtc ?? DateTime.UtcNow);
        var all = servers ?? Array.Empty<TaomServerFolder>();
        limits = WithServerPasswords(limits, all);
        var serverLogs = ServerLogs(all);
        void Extra(LogZipBuilder zip)
        {
            foreach (var stall in stalls.Files)
                zip.AddZipContents(stall.Path, stall.Entry);
            foreach (var log in serverLogs.Files)
                zip.AddFile(log.Path, log.Entry);
            // Coop 0.1.6 writes Coop_server.log into each server folder's engine folder, apart from the logs
            // above, so these are named by the server folder: two data folders can share one.
            var roots = all.Select(server => server.ServerRoot).Where(root => !string.IsNullOrWhiteSpace(root))
                .Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            foreach (var (root, label) in roots.Zip(UniqueLabels(roots)))
                zip.AddCoopServerLogs(root, label + "coop-logs");
            foreach (var line in stalls.Skipped.Concat(serverLogs.Skipped))
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
            new ClientLogZipTaom(CompatLogs(documentsRoot), Patchshield.FlagPath(gameRoot), Extra, ExtraNotes),
            "TAOM client logs",
            limits);
        return new ClientLogPackResult(result.Included, result.Skipped) { DumpIncluded = result.DumpIncluded };
    }

    // The server logs can repeat the join password, so the password of every TAOM server found on
    // this PC is taken out of the text files along with the ones the caller knows.
    public static LogZipLimits WithServerPasswords(LogZipLimits? limits, IReadOnlyList<TaomServerFolder> servers)
    {
        var current = limits ?? LogZipLimits.Full;
        var passwords = ServerPasswords(servers);
        if (passwords.Count == 0)
            return current;
        return current with { Secrets = current.Secrets.Concat(passwords).Distinct(StringComparer.Ordinal).ToList() };
    }

    /// <summary>
    /// The join password of every TAOM server on this PC. The launcher adds them to the report's secrets too,
    /// so a note that repeats one is cleaned like the logs.
    /// </summary>
    public static IReadOnlyList<string> ServerPasswords(IReadOnlyList<TaomServerFolder> servers) =>
        servers
            .Where(server => !string.IsNullOrWhiteSpace(server.DataRoot))
            .Select(server => ConfigPassword(Path.Combine(server.DataRoot!, "server-config.json")))
            .OfType<string>()
            .Where(password => password.Length > 0)
            .Distinct(StringComparer.Ordinal)
            .ToList();

    /// <summary>The "password" in a server's server-config.json, or null when there is none to read.</summary>
    public static string? ConfigPassword(string path)
    {
        try
        {
            if (!File.Exists(path))
                return null;
            using var document = JsonDocument.Parse(File.ReadAllText(path), new JsonDocumentOptions { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip });
            if (document.RootElement.ValueKind != JsonValueKind.Object)
                return null;
            foreach (var property in document.RootElement.EnumerateObject())
            {
                if (property.Name.Equals("password", StringComparison.OrdinalIgnoreCase) && property.Value.ValueKind == JsonValueKind.String)
                    return property.Value.GetString();
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
        }

        return null;
    }

    public static string CompatLogs(string documentsRoot) =>
        Path.Combine(documentsRoot, "Logs", "TAOM.CoopCompat");

    public static string? GameLogs(string? gameRoot) =>
        string.IsNullOrWhiteSpace(gameRoot) ? null : Path.Combine(gameRoot, "bin", "Win64_Shipping_Client", "Logs");

    // TAOM writes taom_crash_<time>.zip in the game's Logs folder when a battle or the map
    // stalls. The newest few from the last days, each and all together under a size cap.
    public static LogPick StallReports(string? gameRoot, DateTime nowUtc)
    {
        var files = new List<(string Path, string Entry)>();
        var skipped = new List<string>();
        long total = 0;
        var recent = LogZipBuilder.Files(GameLogs(gameRoot), "taom_crash_*.zip")
            .Where(path => LogZipBuilder.SafeWriteTime(path) >= nowUtc.AddDays(-StallReportDays))
            .OrderByDescending(LogZipBuilder.SafeWriteTime)
            .Take(StallReportCount);
        foreach (var path in recent)
        {
            var name = Path.GetFileName(path);
            var length = Length(path);
            if (length > StallReportBytes)
            {
                skipped.Add(name + " was left out because it is larger than 25 MB.");
                continue;
            }

            if (total + length > StallReportTotalBytes)
            {
                skipped.Add(name + " was left out so the zip stays small enough to send.");
                continue;
            }

            total += length;
            files.Add((path, "stall-reports/" + Path.GetFileNameWithoutExtension(path)));
        }

        return new LogPick(files, skipped);
    }

    // The logs of a TAOM server on this PC, which the client folders do not have when the
    // player hosts and plays on one PC. Text files keep their last 20 MB, and all of them
    // together stay under ServerLogTotalBytes.
    public static LogPick ServerLogs(IReadOnlyList<TaomServerFolder> servers, long totalBytes = ServerLogTotalBytes)
    {
        var files = new List<(string Path, string Entry)>();
        var skipped = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        long total = 0;
        foreach (var (server, label) in servers.Zip(ServerLabels(servers)))
        {
            var logs = HostLogPack.ServerLogs(server.DataRoot);
            var picks = Newest(LogZipBuilder.Files(logs == null ? null : Path.Combine(logs, "TAOM.CoopCompat"), "Coop_server*.log"), 3, label + "coop-compat/")
                .Concat(Newest(LogZipBuilder.Files(logs, "coop-server-*.log"), 3, label + "logs/"))
                .Concat(Newest(LogZipBuilder.Files(logs, ConsoleLogFile.Pattern), 2, label + "logs/"))
                .Concat(Newest(LogZipBuilder.Files(HostLogPack.TaomDebugLogs(server.ServerRoot), "taom_debug_*.log"), 2, label + "taom-debug/"));
            foreach (var pick in picks)
            {
                if (!seen.Add(Path.GetFullPath(pick.Path)))
                    continue;
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
        }

        return new LogPick(files, skipped);
    }

    // The folder in the zip for each server's logs, with its own name when there are more, and -2, -3
    // after a name another server already has, so two folders both named Data keep their logs apart.
    private static IReadOnlyList<string> ServerLabels(IReadOnlyList<TaomServerFolder> servers) =>
        UniqueLabels(servers.Select(server => server.DataRoot ?? server.ServerRoot).ToList());

    private static IReadOnlyList<string> UniqueLabels(IReadOnlyList<string?> folders)
    {
        if (folders.Count <= 1)
            return folders.Select(_ => "server/").ToList();
        var taken = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var labels = new List<string>();
        foreach (var folder in folders)
        {
            var name = FolderName(folder);
            var unique = name;
            for (var n = 2; !taken.Add(unique); n++)
                unique = name + "-" + n;
            labels.Add("server/" + unique + "/");
        }

        return labels;
    }

    private static IEnumerable<(string Path, string Entry)> Newest(IEnumerable<string> paths, int count, string folder) =>
        paths.OrderByDescending(LogZipBuilder.SafeWriteTime).Take(count).Select(path => (path, folder + Path.GetFileName(path)));

    // C:\TAOM-Coop-Host\Data-v0325 is "C-Data-v0325", so two drives keep their own folders.
    private static string FolderName(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return "server";
        var drive = (Path.GetPathRoot(path) ?? "").TrimEnd(':', '\\', '/');
        var name = Path.GetFileName(path.TrimEnd('\\', '/'));
        return drive.Length > 0 && drive.Length <= 2 ? drive + "-" + name : name;
    }

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
