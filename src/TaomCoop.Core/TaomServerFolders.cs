using System.Text.Json;
using BannerlordCoopLauncher.Core.IO;

namespace TaomCoop.Core;

public sealed record TaomServerFolder(string? ServerRoot, string? DataRoot);

// Where a TAOM server tool on this PC keeps its server and Data folders, so the client
// launcher's log zip can carry the server's logs when one PC hosts and plays.
public static class TaomServerFolders
{
    public const string SettingsFile = "taom-server.json";

    // The default folders on every fixed drive, and the folders named in the server tool's taom-server.json:
    // its own in %LOCALAPPDATA%\Coodsz\TAOM\Server, and the old one next to this launcher or in a folder
    // beside it (both tools unzipped in one place).
    public static IReadOnlyList<TaomServerFolder> OnThisPc(string launcherFolder)
    {
        var drives = new List<string>();
        try
        {
            drives.AddRange(DriveInfo.GetDrives().Where(drive => drive.DriveType == DriveType.Fixed && drive.IsReady).Select(drive => drive.RootDirectory.FullName));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }

        return Find(drives, SettingsFiles(launcherFolder));
    }

    public static IReadOnlyList<TaomServerFolder> Find(IEnumerable<string> driveRoots, IEnumerable<string> settingsFiles)
    {
        var found = new List<TaomServerFolder>();
        foreach (var file in settingsFiles)
        {
            var folder = Read(file);
            if (folder != null)
                found.Add(folder);
        }

        found.AddRange(driveRoots.Select(root => new TaomServerFolder(
            Path.Combine(root, "TAOM-Coop-Host", HostFolders.ServerFolderName),
            Path.Combine(root, "TAOM-Coop-Host", HostFolders.DataFolderName))));
        return found
            .Where(folder => Exists(folder.ServerRoot) || Exists(folder.DataRoot))
            .DistinctBy(folder => Key(folder.DataRoot) + "|" + Key(folder.ServerRoot))
            .ToList();
    }

    // Only ServerRoot and DataRoot are read. The file also holds the server password,
    // which never leaves it.
    public static TaomServerFolder? Read(string settingsFile)
    {
        try
        {
            if (!File.Exists(settingsFile))
                return null;
            using var document = JsonDocument.Parse(File.ReadAllText(settingsFile));
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
                return null;
            var server = Text(root, "ServerRoot");
            var data = Text(root, "DataRoot");
            return server == null && data == null ? null : new TaomServerFolder(server, data);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            return null;
        }
    }

    private static IEnumerable<string> SettingsFiles(string launcherFolder)
    {
        var files = new List<string> { Path.Combine(ToolData.FolderOf(ToolData.TaomServer), SettingsFile), Path.Combine(launcherFolder, SettingsFile) };
        try
        {
            var parent = Directory.GetParent(launcherFolder.TrimEnd('\\', '/'));
            if (parent != null)
                files.AddRange(Directory.EnumerateDirectories(parent.FullName).Take(200).Select(folder => Path.Combine(folder, SettingsFile)));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }

        return files.Where(File.Exists);
    }

    private static string? Text(JsonElement root, string name) =>
        root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(value.GetString())
            ? value.GetString()
            : null;

    private static bool Exists(string? path) => !string.IsNullOrWhiteSpace(path) && Directory.Exists(path);

    private static string Key(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return "";
        try
        {
            return Path.GetFullPath(path).TrimEnd('\\', '/').ToUpperInvariant();
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return path.ToUpperInvariant();
        }
    }
}
