using BannerlordCoopLauncher.Core.IO;
using BannerlordCoopLauncher.Core.Parsing;
using BannerlordCoopLauncher.Core.Steam;

namespace TaomCoop.Core;

public static class BannerlordFinder
{
    public static string? FindGame()
        => FindGameWithReason().Root;

    public static GameSearch FindGameWithReason()
        => FindGameWithReason(SteamRegistry.TryGetSteamPath(), new PhysicalFileSystem());

    public static GameSearch FindGameWithReason(string? steamPath, IFileSystem files)
    {
        var steam = steamPath;
        if (steam == null)
            return new GameSearch(null, GameSearch.SteamMissingReason);

        var vdfPath = Path.Combine(steam, "steamapps", "libraryfolders.vdf");
        IReadOnlyList<string> libraries = new[] { steam };
        var libraryListFailed = false;
        // Steam can hold this file while it writes it; a click must never crash the tool over that.
        try
        {
            var vdf = files.ReadAllText(vdfPath);
            if (vdf != null)
                libraries = SteamInstallFinder.ReadLibraryPaths(steam, vdf);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or FormatException or VdfException)
        {
            libraryListFailed = true;
        }

        foreach (var library in libraries)
        {
            var root = Path.Combine(library, "steamapps", "common", "Mount & Blade II Bannerlord");
            if (GameRoot.IsRoot(root, files))
                return new GameSearch(OnDiskCasing(root), null);
        }

        // A game folder with another name: the install folder Steam's app manifest names.
        try
        {
            var found = SteamInstallFinder.FindGameRoot(steam, files, out var manifestUnreadable);
            return found == null
                ? new GameSearch(null, libraryListFailed || manifestUnreadable ? GameSearch.LibraryListUnreadableReason : GameSearch.GameMissingReason)
                : new GameSearch(OnDiskCasing(found), null);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or FormatException or VdfException)
        {
            return new GameSearch(null, GameSearch.LibraryListUnreadableReason);
        }
    }

    public static IReadOnlyList<string> SteamLibraries()
    {
        var steam = SteamRegistry.TryGetSteamPath();
        if (steam == null)
            return Array.Empty<string>();
        var vdfPath = Path.Combine(steam, "steamapps", "libraryfolders.vdf");
        if (!File.Exists(vdfPath))
            return new[] { steam };
        try
        {
            return SteamInstallFinder.ReadLibraryPaths(steam, File.ReadAllText(vdfPath));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or FormatException or VdfException)
        {
            // A corrupt or unreadable library list: the mod scans still look in the Steam folder itself.
            return new[] { steam };
        }
    }

    public static string OnDiskCasing(string path)
        => PathCasing.OnDisk(path);
}
