using BannerlordCoopLauncher.Core.Models;
using BannerlordCoopLauncher.Core.Parsing;
using BannerlordCoopLauncher.Core.Steam;

namespace BannerlordCoopLauncher.Core.Profile;

public sealed record FoundMod(string Name, string ModuleId, string WorkshopId, string Directory, string Source);

public static class CustomModCatalog
{
    private static readonly HashSet<string> BuiltInIds = new(StringComparer.OrdinalIgnoreCase)
    {
        "Native", "SandBoxCore", "SandBox", "StoryMode", "CustomBattle",
        "Bannerlord.Harmony", "Bannerlord.ButterLib", "Bannerlord.UIExtenderEx", "Bannerlord.MBOptionScreen",
        "Coop", "CoopNightly",
        "BirthAndDeath", "Multiplayer", "NavalDLC", "DedicatedCustomServer", "DedicatedServer.Windows",
        "CoopMarriage", "FamilyAppearanceEditor"
    };

    /// <summary>War Sails as the Add mods list names it. Only the Coop and Friends client offers it, and only with the
    /// Coop nightly, the one Coop that joins a War Sails server.</summary>
    public const string NavalName = "War Sails (NavalDLC)";

    // NavalDLC is a built in module, taken as a mod only with navalDlc (the Coop nightly in the Coop and Friends client).
    private static bool Skipped(string moduleId, bool navalDlc) =>
        BuiltInIds.Contains(moduleId) && !(navalDlc && moduleId.Equals(Server.WarSailsCheck.ModuleId, StringComparison.OrdinalIgnoreCase));

    public static void Apply(ServerProfile profile, IEnumerable<CustomMod> mods, bool locked, bool navalDlc = false)
    {
        if (locked)
            return;

        foreach (var mod in mods)
        {
            if (string.IsNullOrWhiteSpace(mod.ModuleId) || Skipped(mod.ModuleId, navalDlc))
                continue;
            if (profile.Mods.Any(item => item.ModuleId.Equals(mod.ModuleId, StringComparison.OrdinalIgnoreCase)))
                continue;

            var workshopId = string.IsNullOrWhiteSpace(mod.WorkshopId) ? "0" : mod.WorkshopId;
            profile.Mods.Add(new ProfileMod
            {
                Name = string.IsNullOrWhiteSpace(mod.Name) ? mod.ModuleId : mod.Name,
                ModuleId = mod.ModuleId,
                WorkshopId = workshopId,
                Enabled = true,
                Pinned = workshopId == "0"
            });
            if (!profile.ModuleLoadOrder.Contains(mod.ModuleId, StringComparer.OrdinalIgnoreCase))
                profile.ModuleLoadOrder.Add(mod.ModuleId);
        }
    }

    /// <param name="navalDlc">The Coop and Friends client with the Coop nightly: War Sails (NavalDLC) in the game's Modules
    /// folder is listed too. Every other tool leaves it out.</param>
    public static IReadOnlyList<FoundMod> Scan(string launcherDirectory, IReadOnlyList<WorkshopLibrary> workshops, string? gameModulesDirectory = null, string? bundledDirectory = null, bool navalDlc = false)
    {
        var found = new List<FoundMod>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var library in workshops)
            ScanTree(library.ContentDirectory, "Workshop", found, seen);
        if (!string.IsNullOrWhiteSpace(gameModulesDirectory) && !SameDirectory(gameModulesDirectory, launcherDirectory))
        {
            ScanTree(gameModulesDirectory, "From Nexus or ModDB  —  auto detected in your module folder", found, seen);
            var naval = Path.Combine(gameModulesDirectory, Server.WarSailsCheck.ModuleId);
            if (navalDlc && File.Exists(Path.Combine(naval, "SubModule.xml")) && seen.Add(Server.WarSailsCheck.ModuleId))
                found.Add(new FoundMod(NavalName, Server.WarSailsCheck.ModuleId, "0", naval, "War Sails"));
        }
        // A bundled mod an older version left beside the launcher is the same one the launcher carries (bundledDirectory), so it is listed once.
        ScanTree(launcherDirectory, "From Nexus or ModDB", found, seen, bundledDirectory);
        if (!string.IsNullOrWhiteSpace(bundledDirectory))
            ScanTree(bundledDirectory, "From Nexus or ModDB", found, seen);
        return Matching(found, "");
    }

    public static IReadOnlyList<FoundMod> Matching(IEnumerable<FoundMod> mods, string? query)
    {
        var ordered = mods
            .OrderBy(mod => mod.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(mod => mod.ModuleId, StringComparer.OrdinalIgnoreCase);
        var text = query?.Trim();
        if (string.IsNullOrEmpty(text))
            return ordered.ToList();

        var parts = text.Split([' ', '\t'], StringSplitOptions.RemoveEmptyEntries);
        return ordered.Where(mod =>
        {
            var haystack = mod.Name + " " + mod.ModuleId + " " + mod.Source;
            return parts.All(part => haystack.Contains(part, StringComparison.OrdinalIgnoreCase));
        }).ToList();
    }

    private static bool SameDirectory(string? left, string? right)
    {
        if (string.IsNullOrWhiteSpace(left) || string.IsNullOrWhiteSpace(right))
            return false;
        return Path.GetFullPath(left).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            .Equals(Path.GetFullPath(right).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar), StringComparison.OrdinalIgnoreCase);
    }

    private static void ScanTree(string root, string source, List<FoundMod> found, HashSet<string> seen, string? skipCopiesOf = null)
    {
        if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root))
            return;

        var workshopItem = source == "Workshop";
        foreach (var directory in Directory.EnumerateDirectories(root))
        {
            if (Launch.BundledModules.IsCopyOf(directory, skipCopiesOf))
                continue;
            var folderName = Path.GetFileName(directory);
            var workshopId = workshopItem && folderName.All(char.IsDigit) ? folderName : "0";
            foreach (var moduleDirectory in FindModules(directory, 0))
            {
                var xmlPath = Path.Combine(moduleDirectory, "SubModule.xml");
                if (!SubModuleXml.TryRead(File.ReadAllText(xmlPath), out var info, out _) || info == null)
                    continue;
                if (BuiltInIds.Contains(info.Id) || !seen.Add(info.Id))
                    continue;
                found.Add(new FoundMod(
                    string.IsNullOrWhiteSpace(info.Name) ? info.Id : info.Name,
                    info.Id,
                    workshopId,
                    moduleDirectory,
                    source));
            }
        }
    }

    private static IEnumerable<string> FindModules(string directory, int depth)
    {
        if (depth > 3 || !Directory.Exists(directory))
            yield break;
        if (File.Exists(Path.Combine(directory, "SubModule.xml")))
        {
            yield return directory;
            yield break;
        }

        foreach (var child in Directory.EnumerateDirectories(directory))
        {
            foreach (var nested in FindModules(child, depth + 1))
                yield return nested;
        }
    }
}
