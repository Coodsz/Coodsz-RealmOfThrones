using System.Text.RegularExpressions;
using BannerlordCoopLauncher.Core.AD1259;

namespace BannerlordCoopLauncher.Core.Modules;

/// <summary>
/// Raw Assets folders the headless dedicated server crashes on (0xC0000005 right after
/// "Loading packages $BASE/Modules/X/Assets"). Every Coop server start renames them to
/// Assets.off in the server's own module copy. Players keep theirs, and the game folder and
/// the Workshop copies are never touched. The list is the built in mods plus every entry of
/// the community list with "serverHideAssets": true or a note that says to remove the Assets
/// folder from the server copy.
/// </summary>
public static class ServerRawAssets
{
    public const string HiddenName = "Assets.off";

    /// <summary>
    /// The words in a compat-list note that put a mod on this list. Lists from before the
    /// serverHideAssets field only have these words, so the tools keep reading them.
    /// </summary>
    public const string NoteMarker = "Server copy: remove the mod's Assets folder";

    // Comes with the Coodsz tools, so it is hidden even when the community list cannot be read.
    public static readonly IReadOnlyList<string> Builtin =
        ["BannerFix", "RaiseYourBanner", "RaiseYourTorch", "ArtemCore", .. AD1259Stack.ServerHiddenAssets];

    // The official modules load DsAssetPackages, never a raw Assets folder; they are never renamed.
    private static readonly string[] Official =
        ["Native", "SandBox", "SandBoxCore", "StoryMode", "CustomBattle", "Multiplayer", "DedicatedServer.Windows", "NavalDLC", "BirthAndDeath", "FastMode"];

    /// <summary>The built in mods and the compat-list entries whose field or note asks for it.</summary>
    public static IReadOnlyList<string> Ids(ModCompatDatabase? list = null)
    {
        var ids = new List<string>(Builtin);
        if (list != null)
        {
            foreach (var entry in list.All)
            {
                if (!entry.ServerHideAssets && (entry.Note == null || !entry.Note.Contains(NoteMarker, StringComparison.OrdinalIgnoreCase)))
                    continue;
                if (!ids.Contains(entry.Id, StringComparer.OrdinalIgnoreCase))
                    ids.Add(entry.Id);
            }
        }

        return ids;
    }

    /// <summary>
    /// Renames the raw Assets folder of these mods to Assets.off in the server modules folder.
    /// A mod is found by its folder name or its SubModule.xml id. Returns the folders it renamed.
    /// </summary>
    public static IReadOnlyList<string> Hide(string serverModulesRoot, IEnumerable<string> moduleIds)
    {
        var hidden = new List<string>();
        if (string.IsNullOrWhiteSpace(serverModulesRoot) || !Directory.Exists(serverModulesRoot))
            return hidden;
        // A Modules folder (or engine folder) linked to the game's own Modules would rename the player's copy.
        if (IsLinkedFolder(serverModulesRoot) || IsLinkedFolder(Path.GetDirectoryName(Path.GetFullPath(serverModulesRoot))))
            return hidden;
        var wanted = new HashSet<string>(moduleIds.Where(id => !IsOfficial(id)), StringComparer.OrdinalIgnoreCase);
        if (wanted.Count == 0)
            return hidden;
        foreach (var module in Directory.EnumerateDirectories(serverModulesRoot))
        {
            var folder = Path.GetFileName(module);
            if (IsOfficial(folder) || !Directory.Exists(Path.Combine(module, "Assets")) || IsLink(module))
                continue;
            if (!wanted.Contains(folder) && !(ReadId(module) is { } id && wanted.Contains(id) && !IsOfficial(id)))
                continue;
            try
            {
                HideFolder(module);
                hidden.Add(folder);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // A file in use (the server or an editor still has it open); the next start tries again.
            }
        }

        return hidden;
    }

    /// <summary>The log line for renamed folders, or null when nothing was renamed.</summary>
    public static string? Note(IReadOnlyList<string> hidden) =>
        hidden.Count == 0
            ? null
            : "Hid the raw Assets folder of " + string.Join(", ", hidden) + " in the server copy (renamed to " + HiddenName
              + "), because the dedicated server crashes loading it. Players keep theirs.";

    public static bool IsOfficial(string? moduleId) =>
        moduleId != null && Official.Contains(moduleId, StringComparer.OrdinalIgnoreCase);

    // A module folder linked to the player's copy (a junction made by hand) is the player's
    // folder, so it is left alone.
    private static bool IsLink(string module)
    {
        try
        {
            var info = new DirectoryInfo(module);
            return info.LinkTarget != null || info.Attributes.HasFlag(FileAttributes.ReparsePoint)
                || new DirectoryInfo(Path.Combine(module, "Assets")).Attributes.HasFlag(FileAttributes.ReparsePoint);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return true;
        }
    }

    private static bool IsLinkedFolder(string? path)
    {
        if (string.IsNullOrEmpty(path) || !Directory.Exists(path))
            return false;
        try
        {
            var info = new DirectoryInfo(path);
            return info.LinkTarget != null || info.Attributes.HasFlag(FileAttributes.ReparsePoint);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return true;
        }
    }

    private static void HideFolder(string module)
    {
        var assets = Path.Combine(module, "Assets");
        var off = Path.Combine(module, HiddenName);
        // A mod update brings a fresh Assets folder back; the old hidden copy is stale.
        if (Directory.Exists(off))
            Directory.Delete(off, recursive: true);
        Directory.Move(assets, off);
    }

    private static string? ReadId(string module)
    {
        try
        {
            var xml = Path.Combine(module, "SubModule.xml");
            if (!File.Exists(xml))
                return null;
            var match = Regex.Match(File.ReadAllText(xml), "<Id\\s+value\\s*=\\s*\"([^\"]+)\"", RegexOptions.IgnoreCase);
            return match.Success ? match.Groups[1].Value.Trim() : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }
}
