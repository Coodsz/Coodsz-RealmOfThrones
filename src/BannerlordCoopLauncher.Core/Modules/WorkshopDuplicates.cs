using BannerlordCoopLauncher.Core.Parsing;
using BannerlordCoopLauncher.Core.Profile;

namespace BannerlordCoopLauncher.Core.Modules;

// The game loads every folder in Modules and every subscribed Workshop item.
// When the same mod id is in both, it loads two copies and can crash. Older
// launchers also copied Workshop mods into Modules. This turns off the copy in
// the game's Modules folder when the setup takes that mod from the Workshop and
// the Workshop copy is there. The folder stays; its SubModule.xml is renamed so
// the game skips it, and renaming it back undoes this.
public static class WorkshopDuplicates
{
    public const string ParkedFile = "SubModule.xml.coodsz-off";

    // Problems (no write access, a locked file) are added to `problems` and the
    // launch goes on; a copy that could not be turned off is only reported.
    public static IReadOnlyList<string> ParkGameCopies(string gameRoot, ModuleResolution resolution, IReadOnlyList<string> workshopContentDirectories) =>
        ParkGameCopies(gameRoot, resolution, workshopContentDirectories, new List<string>());

    public static IReadOnlyList<string> ParkGameCopies(string gameRoot, ModuleResolution resolution, IReadOnlyList<string> workshopContentDirectories, List<string> problems)
    {
        var parked = new List<string>();
        var modulesRoot = Path.Combine(gameRoot, "Modules");
        if (!Directory.Exists(modulesRoot))
            return parked;
        string[] gameFolders;
        try
        {
            gameFolders = Directory.GetDirectories(modulesRoot);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            problems.Add("Could not read the game's Modules folder to look for extra mod copies. " + ex.Message);
            return parked;
        }

        foreach (var module in resolution.Modules)
        {
            if (!module.Enabled || string.IsNullOrWhiteSpace(module.WorkshopId) || module.WorkshopId == "0")
                continue;
            // Coop is managed by "Use Coop 0.1.5" in the TAOM launcher. Leave it alone.
            if (BannerlordCoopModules.IsEither(module.ModuleId))
                continue;
            if (!IsUnder(module.Directory, workshopContentDirectories))
                continue;

            foreach (var folder in gameFolders)
            {
                var xml = Path.Combine(folder, "SubModule.xml");
                if (!File.Exists(xml) || !HasId(xml, module.ModuleId))
                    continue;
                var off = Path.Combine(folder, ParkedFile);
                // Keep an older parked file: the new one gets a dated name instead.
                if (File.Exists(off))
                    off = Path.Combine(folder, ParkedFile + "-" + DateTime.Now.ToString("yyyyMMdd-HHmmss"));
                try
                {
                    File.Move(xml, off);
                    parked.Add(module.Name + " (" + Path.GetFileName(folder) + ")");
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    problems.Add("Could not turn off the extra copy of " + module.Name + " in " + folder + ". " + ex.Message
                        + " The game may load two copies. Move that folder out of the Modules folder yourself.");
                }
            }
        }

        return parked;
    }

    // The other way round, for a removed Workshop item whose game folder copy wins (RetiredWorkshopItems.LocalCopyWins, MCM): the setup
    // uses the copy in the game's Modules folder (the one from Nexus the host runs), and Steam can no longer
    // update the old download, so that download is the copy turned off. Same rename, so it is undone the
    // same way, and RestoreRetiredWorkshopCopies undoes it once the game folder copy is gone.
    public static IReadOnlyList<string> ParkRetiredWorkshopCopies(ModuleResolution resolution, IReadOnlyList<string> workshopContentDirectories, List<string> problems)
    {
        var parked = new List<string>();
        foreach (var module in resolution.Modules)
        {
            var retired = RetiredWorkshopItems.LocalCopyWins(module.ModuleId) ? RetiredWorkshopItems.For(module.ModuleId) : null;
            if (!module.Enabled || retired == null || string.IsNullOrWhiteSpace(module.Directory) || IsUnder(module.Directory, workshopContentDirectories))
                continue;
            foreach (var folder in RetiredFolders(module.ModuleId, retired, workshopContentDirectories))
                ParkRetired(folder, module.Name, parked, problems);
        }

        return parked;
    }

    // The same for a launcher that names its mods by id and takes a mod from the game's Modules folder whenever it is
    // there (the Burning Empires client, see its FindModuleXml): that copy wins exactly when it exists. Returns the module ids.
    public static IReadOnlyList<string> ParkRetiredWorkshopCopies(string? gameRoot, IEnumerable<string> moduleIds, IReadOnlyList<string> workshopContentDirectories, List<string> problems) =>
        ParkRetiredWorkshopCopies(gameRoot, moduleIds, workshopContentDirectories, problems, Directory.GetDirectories);

    internal static IReadOnlyList<string> ParkRetiredWorkshopCopies(string? gameRoot, IEnumerable<string> moduleIds, IReadOnlyList<string> workshopContentDirectories, List<string> problems, Func<string, string[]> listDirectories)
    {
        var parked = new List<string>();
        foreach (var (moduleId, folder) in RetiredWorkshopCopies(gameRoot, moduleIds, workshopContentDirectories, listDirectories))
            ParkRetired(folder, moduleId, parked, problems);
        return parked.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    // What the overload above would turn off: the removed Workshop item's download of a mod in moduleIds whose game folder
    // copy wins (LocalCopyWins) and is in the game's Modules folder. An unreadable Modules folder turns nothing off.
    public static IReadOnlyList<(string ModuleId, string Folder)> RetiredWorkshopCopies(string? gameRoot, IEnumerable<string> moduleIds, IReadOnlyList<string> workshopContentDirectories) =>
        RetiredWorkshopCopies(gameRoot, moduleIds, workshopContentDirectories, Directory.GetDirectories);

    internal static IReadOnlyList<(string ModuleId, string Folder)> RetiredWorkshopCopies(string? gameRoot, IEnumerable<string> moduleIds, IReadOnlyList<string> workshopContentDirectories, Func<string, string[]> listDirectories)
    {
        var ids = new HashSet<string>(moduleIds, StringComparer.OrdinalIgnoreCase);
        return RetiredWorkshopItems.LocalWins
            .Where(item => ids.Contains(item.ModuleId) && GameHasModule(gameRoot, item.ModuleId, whenUnreadable: false, listDirectories))
            .SelectMany(item => RetiredFolders(item.ModuleId, item.WorkshopId, workshopContentDirectories).Select(folder => (item.ModuleId, folder)))
            .ToList();
    }

    private static List<string> RetiredFolders(string moduleId, string workshopId, IReadOnlyList<string> workshopContentDirectories) =>
        workshopContentDirectories
            .SelectMany(root => ModuleFolders(Path.Combine(root, workshopId), "SubModule.xml"))
            .Where(folder => HasId(Path.Combine(folder, "SubModule.xml"), moduleId))
            .ToList();

    private static void ParkRetired(string folder, string name, List<string> parked, List<string> problems)
    {
        var xml = Path.Combine(folder, "SubModule.xml");
        var off = Path.Combine(folder, ParkedFile);
        if (File.Exists(off))
            off = Path.Combine(folder, ParkedFile + "-" + DateTime.Now.ToString("yyyyMMdd-HHmmss"));
        try
        {
            File.Move(xml, off);
            parked.Add(name);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            problems.Add("Could not turn off the old Workshop copy of " + name + " in " + folder + ". " + ex.Message
                + " The game may load two copies. Unsubscribe from it in Steam.");
        }
    }

    // Puts back an old Workshop copy turned off above when the game's Modules folder no longer has that mod,
    // so the old download is used again instead of the mod going missing. Run before the mods are checked.
    public static IReadOnlyList<string> RestoreRetiredWorkshopCopies(string? gameRoot, IReadOnlyList<string> workshopContentDirectories) =>
        RestoreRetiredWorkshopCopies(gameRoot, workshopContentDirectories, new List<string>());

    // The same, with a rename that failed (a locked file) added to `problems` instead of dropped.
    public static IReadOnlyList<string> RestoreRetiredWorkshopCopies(string? gameRoot, IReadOnlyList<string> workshopContentDirectories, List<string> problems) =>
        RestoreRetiredWorkshopCopies(gameRoot, workshopContentDirectories, problems, Directory.GetDirectories);

    internal static IReadOnlyList<string> RestoreRetiredWorkshopCopies(string? gameRoot, IReadOnlyList<string> workshopContentDirectories, List<string> problems, Func<string, string[]> listDirectories)
    {
        var restored = new List<string>();
        foreach (var (moduleId, workshopId) in RetiredWorkshopItems.LocalWins)
        {
            if (GameHasModule(gameRoot, moduleId, whenUnreadable: true, listDirectories))
                continue;
            foreach (var root in workshopContentDirectories)
            {
                foreach (var folder in ModuleFolders(Path.Combine(root, workshopId), ParkedFile))
                {
                    var xml = Path.Combine(folder, "SubModule.xml");
                    var off = Path.Combine(folder, ParkedFile);
                    if (File.Exists(xml) || !HasId(off, moduleId))
                        continue;
                    try
                    {
                        File.Move(off, xml);
                        restored.Add(moduleId);
                    }
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                    {
                        problems.Add("Could not turn the old Workshop copy of " + moduleId + " back on in " + folder + ". " + ex.Message);
                    }
                }
            }
        }

        return restored;
    }

    // When the game's Modules folder cannot be read, whenUnreadable decides: true for a restore, so nothing is turned
    // back on, and false for a park, so nothing is turned off. It matches by module id only: a folder named like the id
    // whose SubModule.xml has another id is no game copy, which is safe, as nothing is parked for it.
    private static bool GameHasModule(string? gameRoot, string moduleId, bool whenUnreadable, Func<string, string[]> listDirectories)
    {
        var modulesRoot = string.IsNullOrWhiteSpace(gameRoot) ? null : Path.Combine(gameRoot, "Modules");
        if (modulesRoot == null || !Directory.Exists(modulesRoot))
            return false;
        try
        {
            return listDirectories(modulesRoot).Any(folder =>
                File.Exists(Path.Combine(folder, "SubModule.xml")) && HasId(Path.Combine(folder, "SubModule.xml"), moduleId));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return whenUnreadable;
        }
    }

    // A Workshop item holds its module at the top or a few folders down. Links are not followed.
    private static IEnumerable<string> ModuleFolders(string root, string marker)
    {
        if (!Directory.Exists(root))
            return [];
        try
        {
            return Directory.EnumerateFiles(root, marker, new EnumerationOptions { RecurseSubdirectories = true, MaxRecursionDepth = 3, AttributesToSkip = FileAttributes.ReparsePoint })
                .Select(path => Path.GetDirectoryName(path)!)
                .ToList();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }

    private static bool HasId(string xmlPath, string moduleId)
    {
        try
        {
            return SubModuleXml.TryRead(File.ReadAllText(xmlPath), out var info, out _)
                && info != null
                && string.Equals(info.Id, moduleId, StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    private static bool IsUnder(string? directory, IReadOnlyList<string> roots)
    {
        if (string.IsNullOrWhiteSpace(directory))
            return false;
        var full = Path.GetFullPath(directory);
        return roots.Any(root =>
        {
            var prefix = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
            return full.StartsWith(prefix, StringComparison.OrdinalIgnoreCase);
        });
    }
}
