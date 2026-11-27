using BannerlordCoopLauncher.Core.IO;

namespace BannerlordCoopLauncher.Core.Steam;

public enum GameFolderPickResult { Ok, NotBannerlord, Epic }

public sealed record GameFolderCheck(GameFolderPickResult Result, string? Root, string Message);

public static class GameFolderPick
{
    public const string EpicMessage = "This looks like the Epic Games version of Bannerlord. The Coodsz tools need the Steam version. Install Bannerlord from Steam to use them.";
    public const string NotBannerlordMessage = "This folder is not Bannerlord. Pick the folder named Mount & Blade II Bannerlord. It is usually in Steam, then steamapps, then common.";
    public const string NotFoundText = "Bannerlord was not found. Press Find Bannerlord and pick its folder.";
    public const string RememberedText = "Found. The tool remembers this folder.";
    public const string DefaultSteamFolder = @"C:\Program Files (x86)\Steam";

    // Checks the picked folder and hands only a good Bannerlord root to use, so an Epic copy or a
    // wrong folder never gets saved.
    public static GameFolderCheck Use(string? picked, Action<string> use, IFileSystem? files = null)
    {
        var check = files == null ? Check(picked) : Check(picked, files);
        if (check.Result == GameFolderPickResult.Ok && check.Root != null)
            use(check.Root);
        return check;
    }

    // Where the folder dialog opens: the saved game folder, else Steam's steamapps\common, else
    // the usual Steam folder, else wherever Windows likes.
    public static string DialogStartFolder(string? saved) =>
        DialogStartFolder(saved, SteamRegistry.TryGetSteamPath(), new PhysicalFileSystem());

    public static string DialogStartFolder(string? saved, string? steamPath, IFileSystem files)
    {
        if (!string.IsNullOrWhiteSpace(saved) && files.DirectoryExists(saved))
            return saved;
        if (!string.IsNullOrWhiteSpace(steamPath))
        {
            var common = Path.Combine(steamPath, "steamapps", "common");
            if (files.DirectoryExists(common))
                return common;
        }

        return files.DirectoryExists(DefaultSteamFolder) ? DefaultSteamFolder : "";
    }

    public static string? ResolveSavedPath(string? saved, Func<string?> search, IFileSystem? files = null)
    {
        // A saved pick is ignored in the no game test mode, so the button shows on every start.
        if (SteamRegistry.TestNoGame)
            return search();
        var checkedPath = files == null ? Check(saved) : Check(saved, files);
        return checkedPath.Result == GameFolderPickResult.Ok ? checkedPath.Root : search();
    }

    /// <summary>Check for a path saved by an earlier pick; it never counts in the no game test mode.</summary>
    public static GameFolderCheck CheckSaved(string? saved) =>
        SteamRegistry.TestNoGame ? new GameFolderCheck(GameFolderPickResult.NotBannerlord, null, NotBannerlordMessage) : Check(saved);

    public static GameFolderCheck Check(string? picked) => Check(picked, new PhysicalFileSystem());

    public static GameFolderCheck Check(string? picked, IFileSystem files)
    {
        if (string.IsNullOrWhiteSpace(picked))
            return new GameFolderCheck(GameFolderPickResult.NotBannerlord, null, NotBannerlordMessage);

        string path;
        try
        {
            path = Path.GetFullPath(picked.Trim().Trim('"').Trim());
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException or System.Security.SecurityException)
        {
            return new GameFolderCheck(GameFolderPickResult.NotBannerlord, null, NotBannerlordMessage);
        }

        if (!string.Equals(path, Path.GetPathRoot(path), StringComparison.OrdinalIgnoreCase))
            path = Path.TrimEndingDirectorySeparator(path);

        if (files.FileExists(path))
            path = Path.GetDirectoryName(path) ?? path;

        var candidate = path;
        for (var depth = 0; depth <= 3 && !string.IsNullOrEmpty(candidate); depth++)
        {
            if (GameRoot.IsRoot(candidate, files))
                return CheckRoot(candidate, files);
            candidate = Path.GetDirectoryName(candidate);
        }

        return new GameFolderCheck(GameFolderPickResult.NotBannerlord, null, NotBannerlordMessage);
    }

    private static GameFolderCheck CheckRoot(string root, IFileSystem files)
    {
        var segments = root.Split(new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar }, StringSplitOptions.RemoveEmptyEntries);
        var steamInstall = segments.Any(segment => string.Equals(segment, "steamapps", StringComparison.OrdinalIgnoreCase));
        var shipping = Path.Combine(root, "bin", "Win64_Shipping_Client");
        if (steamInstall || files.FileExists(Path.Combine(root, "steam_appid.txt")) || files.FileExists(Path.Combine(shipping, "steam_appid.txt")))
            return new GameFolderCheck(GameFolderPickResult.Ok, PathCasing.OnDisk(root), "");

        var bin = Path.Combine(root, "bin");
        var rootMarker = Path.Combine(root, ".egstore");
        var binMarker = Path.Combine(bin, ".egstore");
        var epicPath = segments.Any(segment => string.Equals(segment, "Epic Games", StringComparison.OrdinalIgnoreCase));
        if (epicPath || files.FileExists(rootMarker) || files.DirectoryExists(rootMarker) || files.FileExists(binMarker) || files.DirectoryExists(binMarker))
            return new GameFolderCheck(GameFolderPickResult.Epic, null, EpicMessage);

        return new GameFolderCheck(GameFolderPickResult.Ok, PathCasing.OnDisk(root), "");
    }
}
