using BannerlordCoopLauncher.Core.IO;
using BannerlordCoopLauncher.Core.Parsing;

namespace BannerlordCoopLauncher.Core.Steam;

public static class SteamInstallFinder
{
    public const int BannerlordAppId = 261550;

    public static IReadOnlyList<string> ReadLibraryPaths(string steamPath, string libraryFoldersVdf)
    {
        var paths = new List<string> { steamPath };
        var root = Vdf.Parse(libraryFoldersVdf);
        CollectPaths(root, paths);
        return Distinct(paths);
    }

    public static string? ReadInstallDir(string appManifestVdf)
    {
        var root = Vdf.Parse(appManifestVdf);
        return root.FindFirst("installdir")?.Value;
    }

    public static Dictionary<string, long> ReadInstalledTimes(string appWorkshopVdf)
    {
        var root = Vdf.Parse(appWorkshopVdf);
        var items = root.Child("WorkshopItemsInstalled") ?? FindChildRecursive(root, "WorkshopItemsInstalled");
        var times = new Dictionary<string, long>(StringComparer.Ordinal);
        if (items == null)
            return times;

        foreach (var item in items.Children)
        {
            if (!item.Key.All(char.IsDigit))
                continue;
            var raw = item.Child("timeupdated")?.Value ?? item.FindFirst("timeupdated")?.Value;
            times[item.Key] = long.TryParse(raw, out var time) ? time : 0;
        }

        return times;
    }

    // The content manifest Steam installed for each item. It matches the Workshop
    // page's hcontent_file when the files are current, even after the author only
    // edited the page text (that moves time_updated but downloads nothing).
    public static Dictionary<string, string> ReadInstalledManifests(string appWorkshopVdf)
    {
        var root = Vdf.Parse(appWorkshopVdf);
        var items = root.Child("WorkshopItemsInstalled") ?? FindChildRecursive(root, "WorkshopItemsInstalled");
        var manifests = new Dictionary<string, string>(StringComparer.Ordinal);
        if (items == null)
            return manifests;

        foreach (var item in items.Children)
        {
            if (!item.Key.All(char.IsDigit))
                continue;
            var raw = item.Child("manifest")?.Value;
            if (!string.IsNullOrWhiteSpace(raw))
                manifests[item.Key] = raw.Trim();
        }

        return manifests;
    }

    // The newest content manifest the Steam client itself knows of for each item
    // (WorkshopItemDetails latest_manifest). When it equals the installed manifest,
    // Steam calls the copy current and DownloadItem does nothing, even if the
    // Workshop page already shows a newer upload.
    public static Dictionary<string, string> ReadLatestManifests(string appWorkshopVdf)
    {
        var root = Vdf.Parse(appWorkshopVdf);
        var items = root.Child("WorkshopItemDetails") ?? FindChildRecursive(root, "WorkshopItemDetails");
        var manifests = new Dictionary<string, string>(StringComparer.Ordinal);
        if (items == null)
            return manifests;

        foreach (var item in items.Children)
        {
            if (!item.Key.All(char.IsDigit))
                continue;
            var raw = item.Child("latest_manifest")?.Value;
            if (!string.IsNullOrWhiteSpace(raw))
                manifests[item.Key] = raw.Trim();
        }

        return manifests;
    }

    public static string? FindGameRoot(string steamPath, IFileSystem files)
        => FindGameRoot(steamPath, files, out _);

    public static string? FindGameRoot(string steamPath, IFileSystem files, out bool manifestUnreadable)
    {
        manifestUnreadable = false;
        var libraries = new List<string> { steamPath };
        var vdf = files.ReadAllText(Path.Combine(steamPath, "steamapps", "libraryfolders.vdf"));
        // A corrupt library list still leaves the Steam folder itself to search.
        if (vdf != null)
        {
            try
            {
                libraries.AddRange(ReadLibraryPaths(steamPath, vdf));
            }
            catch (VdfException)
            {
                // Counts for the reason a search failed, like an unreadable manifest.
                manifestUnreadable = true;
            }
        }

        foreach (var library in Distinct(libraries))
        {
            var manifest = files.ReadAllText(Path.Combine(library, "steamapps", $"appmanifest_{BannerlordAppId}.acf"));
            if (manifest == null)
                continue;

            string? installDir;
            try
            {
                installDir = ReadInstallDir(manifest);
            }
            catch (VdfException)
            {
                manifestUnreadable = true;
                continue;
            }

            if (string.IsNullOrWhiteSpace(installDir))
                continue;

            var root = Path.Combine(library, "steamapps", "common", installDir);
            if (GameRoot.IsRoot(root, files))
                return root;
        }

        return null;
    }

    public static IReadOnlyList<WorkshopLibrary> ReadWorkshopLibraries(IEnumerable<string> libraryPaths, IFileSystem files)
    {
        var libraries = new List<WorkshopLibrary>();
        foreach (var library in Distinct(libraryPaths))
        {
            var content = Path.Combine(library, "steamapps", "workshop", "content", BannerlordAppId.ToString());
            var manifestPath = Path.Combine(library, "steamapps", "workshop", $"appworkshop_{BannerlordAppId}.acf");
            if (!files.DirectoryExists(content) && !files.FileExists(manifestPath))
                continue;

            var text = files.ReadAllText(manifestPath);
            if (text == null)
            {
                libraries.Add(new WorkshopLibrary(content, new Dictionary<string, long>(), ManifestParsed: true));
                continue;
            }

            try
            {
                libraries.Add(new WorkshopLibrary(content, ReadInstalledTimes(text), ManifestParsed: true, ReadInstalledManifests(text), ReadLatestManifests(text)));
            }
            catch (VdfException)
            {
                libraries.Add(new WorkshopLibrary(content, new Dictionary<string, long>(), ManifestParsed: false));
            }
        }

        return libraries;
    }

    private static void CollectPaths(VdfNode node, List<string> paths)
    {
        if (string.Equals(node.Key, "path", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(node.Value))
            paths.Add(node.Value);

        if (node.Key.All(char.IsDigit) && !string.IsNullOrWhiteSpace(node.Value) && LooksLikePath(node.Value))
            paths.Add(node.Value);

        foreach (var child in node.Children)
            CollectPaths(child, paths);
    }

    private static bool LooksLikePath(string value) =>
        value.Contains('\\') || value.Contains('/') || value.Contains(':');

    private static VdfNode? FindChildRecursive(VdfNode node, string key)
    {
        foreach (var child in node.Children)
        {
            if (string.Equals(child.Key, key, StringComparison.OrdinalIgnoreCase))
                return child;
            var nested = FindChildRecursive(child, key);
            if (nested != null)
                return nested;
        }

        return null;
    }

    public static IReadOnlyList<string> Distinct(IEnumerable<string> paths)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var result = new List<string>();
        foreach (var path in paths)
        {
            if (string.IsNullOrWhiteSpace(path))
                continue;
            var normalized = path.Replace('/', '\\').Trim().TrimEnd('\\');
            if (seen.Add(normalized))
                result.Add(normalized);
        }

        return result;
    }
}

public sealed record WorkshopLibrary(
    string ContentDirectory,
    IReadOnlyDictionary<string, long> InstalledTimeUpdated,
    bool ManifestParsed,
    IReadOnlyDictionary<string, string>? InstalledManifests = null,
    IReadOnlyDictionary<string, string>? LatestManifests = null);
