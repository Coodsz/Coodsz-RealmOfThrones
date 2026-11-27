using System.Text.RegularExpressions;
using BannerlordCoopLauncher.Core.Modules;

namespace BannerlordCoopLauncher.Core.AD1259;

public enum AD1259ModState
{
    Ready,
    Missing,
    WrongVersion,
    Duplicate
}

public sealed record AD1259ModStatus(
    AD1259Mod Mod,
    AD1259ModState State,
    string? FoundVersion,
    IReadOnlyList<string> Folders,
    IReadOnlyList<string> Directories,
    bool Nightly = false)
{
    public bool IsReady => State == AD1259ModState.Ready;

    // A Coop nightly (AD1259Stack.IsNightlyCoop) or only a CoopNightly module, which the AD1259 tools never take.
    public const string NightlyInModules = "The game's Modules folder has the Coop nightly. Move that Coop folder out of Modules. If Bannerlord Coop is missing after that, subscribe to it on the Steam Workshop.";
    public const string NightlyInWorkshop = "The Steam Workshop Coop has Coop nightly files. In Steam, unsubscribe from Bannerlord Coop and subscribe again, so it downloads Coop 0.1.7.";

    /// <summary>What to tell the player, or null when the mod is ready.</summary>
    public string? Problem => State switch
    {
        // Two lines, each a whole translated text: what AD1259 needs, then how to go back. Only a CoopNightly module is a
        // missing Coop, so that one keeps the get-it line.
        AD1259ModState.WrongVersion when Nightly => CoopInstall.AddLine(AD1259Stack.NightlyNeeds, FoundSource == "Modules" ? NightlyInModules : NightlyInWorkshop),
        AD1259ModState.Missing when Nightly => CoopInstall.AddLine(AD1259Stack.NightlyNeeds, Mod.Name + " is missing. Get " + Mod.Version + " from " + Mod.Source + "."),
        AD1259ModState.Missing => Mod.BaseGame
            ? Mod.Name + " is missing. Verify the game files in Steam."
            // The get-it line goes on a line of its own, so each line stays a whole translated text.
            : CoopInstall.AddLine(Mod.Name + " is missing. Get " + Mod.Version + " from " + Mod.Source + ".",
                IsCoop ? CoopInstall.ClientGetLineNow : OpenSourceSet.IsOpenSource(Mod.Id) ? OpenSourceSet.OlderToo : null),
        // An older Coop needs no GitHub line: the Workshop Coop (0.1.7 since 2026-10-30) fixes it. An Open Source mod
        // also names the 1.x set, which is on Nexus Mods only.
        AD1259ModState.WrongVersion => CoopInstall.AddLine(WrongVersionProblem(), OpenSourceSet.IsOpenSource(Mod.Id) ? OpenSourceSet.OlderToo : null),
        // Two Coop copies where one is the nightly: which one to keep is the Workshop one, so the nightly line follows.
        AD1259ModState.Duplicate => CoopInstall.AddLine(Mod.Name + " is installed " + Folders.Count + " times (" + string.Join(", ", Folders)
            + "). Keep one copy; two copies of a mod crash the game.", Nightly ? AD1259Stack.NightlyNeeds : null),
        _ => null
    };

    public string RowDetail => State switch
    {
        AD1259ModState.Duplicate => "Keep one: " + string.Join(", ", Folders),
        _ when Nightly =>
            "Coop nightly (found " + (string.IsNullOrEmpty(FoundVersion) ? "no version" : FoundVersion) + ", needs " + Mod.Version + ") · " + FoundSource,
        // A newer game gets the plain row below: no tool update fixes it, so Problem says how to go back (NewerGame).
        AD1259ModState.WrongVersion when !Mod.BaseGame && AD1259Stack.IsNewerThanPinned(Mod, FoundVersion) =>
            "Newer than this tool supports (found " + FoundVersion + ", needs " + Mod.Version + ") \u00b7 " + Mod.Source + ". Update the tool first.",
        AD1259ModState.WrongVersion => "Needs " + Mod.Version + ", found " + (string.IsNullOrEmpty(FoundVersion) ? "no version" : FoundVersion)
            + " \u00b7 " + FoundSource,
        // The found version and where it is, so an accepted older one (Coop 0.1.6 beside the set's 0.1.7) or a copy in
        // the game's Modules folder (Coop 0.1.7 unzipped by hand) reads as what this PC has.
        _ => (string.IsNullOrEmpty(FoundVersion) ? Mod.Version : FoundVersion) + " \u00b7 " + FoundSource
    };

    // Where a ready mod is: "Modules" for a copy in the game's Modules folder, else where players get it. The base game
    // modules are always in Modules, so they keep "Bannerlord".
    private string FoundSource => !Mod.BaseGame && Folders.Count == 1 && Folders[0].StartsWith("Modules\\", StringComparison.OrdinalIgnoreCase)
        ? "Modules"
        : Mod.Source;

    // A missing Coop says where to get it (the Workshop has Coop 0.1.7 since 2026-10-30; it was on Coop's GitHub release first).
    private bool IsCoop => Mod.Id.Equals(BannerlordCoopModules.Coop, StringComparison.OrdinalIgnoreCase);

    private string WrongVersionProblem()
    {
        if (!AD1259Stack.IsNewerThanPinned(Mod, FoundVersion))
            return Mod.Name + " is " + (string.IsNullOrEmpty(FoundVersion) ? "missing a version" : FoundVersion)
                + ". The server needs " + Mod.Version + ".";

        var found = FoundVersion!;
        if (Mod.BaseGame && Steam.NewerGame.Line(found) is { } newerGame)
            return newerGame;
        var advice = Mod.BaseGame
            ? "Until then, go back to " + Mod.Version + " and verify the game files in Steam."
            : "Until then, go back to " + Mod.Version + " from " + Mod.Source + ".";
        return Mod.Name + " is " + found + ", which is newer than this tool supports. "
            + AD1259Stack.NewerAdvice + " " + advice;
    }
}

/// <summary>Finds each mod of the AD1259 set in the game's Modules folder and the Steam Workshop folders.</summary>
public static class AD1259ModScan
{
    private const string ModulesLabel = "Modules\\";

    public static IReadOnlyList<AD1259ModStatus> Check(string gameModulesDirectory, IEnumerable<string> workshopContentRoots)
    {
        var found = Index(gameModulesDirectory, workshopContentRoots);
        return AD1259Stack.ClientMods.Select(mod => Status(mod, found)).ToList();
    }

    public static AD1259ModStatus Status(AD1259Mod mod, IReadOnlyDictionary<string, List<(string Folder, string Directory, string Version)>> found)
    {
        var isCoop = mod.Id.Equals(BannerlordCoopModules.Coop, StringComparison.OrdinalIgnoreCase);
        if (!found.TryGetValue(mod.Id, out var copies) || copies.Count == 0)
        {
            // Only the Coop nightly under its own id (CoopNightly): Connect would load no Coop, so the row says why.
            if (isCoop && found.TryGetValue(BannerlordCoopModules.CoopNightly, out var nightly) && nightly.Count > 0)
                return new AD1259ModStatus(mod, AD1259ModState.Missing, nightly[0].Version, nightly.Select(copy => copy.Folder).ToList(),
                    nightly.Select(copy => copy.Directory).ToList(), Nightly: true);
            return new AD1259ModStatus(mod, AD1259ModState.Missing, null, [], []);
        }
        var folders = copies.Select(copy => copy.Folder).ToList();
        var directories = copies.Select(copy => copy.Directory).ToList();
        // Bannerlord 1.4.8's TaleWorlds.ModuleManager.ModuleHelper lists GetPhysicalModules (the Modules folder) before
        // GetPlatformModules (the Workshop) and picks each loaded id with list.Find on Id, so a copy in Modules loads and a
        // Workshop copy with the same id is not loaded at all. For the ModulesCopyWins mods that copy is judged and the
        // Workshop one is not a second copy; two copies in Modules are still two copies. AD1259Server.GoodCopy gives the
        // server that Modules copy too.
        if (ModulesCopyWins(mod.Id)
            && copies.Where(copy => copy.Folder.StartsWith(ModulesLabel, StringComparison.OrdinalIgnoreCase)).ToList() is { Count: > 0 } inModules
            && inModules.Count < copies.Count)
        {
            copies = inModules;
            folders = copies.Select(copy => copy.Folder).ToList();
            directories = copies.Select(copy => copy.Directory).ToList();
        }

        if (copies.Count > 1)
            return new AD1259ModStatus(mod, AD1259ModState.Duplicate, copies[0].Version, folders, directories,
                Nightly: isCoop && copies.Any(copy => AD1259Stack.IsNightlyCoop(copy.Version, CoopBuildId.FromModule(copy.Directory))));
        var version = copies[0].Version;
        // Nightly files over a Workshop Coop keep its SubModule.xml, so the row names the build's version then.
        var build = isCoop ? CoopBuildId.FromModule(copies[0].Directory) : null;
        if (isCoop && AD1259Stack.IsNightlyCoop(version, build))
            return new AD1259ModStatus(mod, AD1259ModState.WrongVersion, AD1259Stack.IsNewerThanPinned(mod, version) ? version : CoopBuildId.Version(build),
                folders, directories, Nightly: true);
        return new AD1259ModStatus(
            mod,
            AD1259Stack.VersionMatches(mod, version) ? AD1259ModState.Ready : AD1259ModState.WrongVersion,
            version,
            folders,
            directories);
    }

    /// <summary>
    /// The mods whose copy in the game's Modules folder is the one judged when the Workshop has one too: Coop, the Open
    /// Source mods (a 1.x set from Nexus next to the 2.0 Workshop items) and MCM (Nexus 5.12.3 next to the Workshop's 5.13.1).
    /// </summary>
    public static bool ModulesCopyWins(string id) =>
        id.Equals(BannerlordCoopModules.Coop, StringComparison.OrdinalIgnoreCase) || OpenSourceSet.IsOpenSource(id)
        || id.Equals("Bannerlord.MBOptionScreen", StringComparison.OrdinalIgnoreCase);

    /// <summary>Every SubModule.xml by module id. A folder is shown as "Modules\name" or "Workshop 123".</summary>
    public static Dictionary<string, List<(string Folder, string Directory, string Version)>> Index(string gameModulesDirectory, IEnumerable<string> workshopContentRoots)
    {
        var index = new Dictionary<string, List<(string Folder, string Directory, string Version)>>(StringComparer.OrdinalIgnoreCase);
        Add(index, gameModulesDirectory, directory => ModulesLabel + Path.GetFileName(directory));
        foreach (var root in workshopContentRoots.Distinct(StringComparer.OrdinalIgnoreCase))
            Add(index, root, directory => "Workshop " + Path.GetFileName(directory));
        return index;
    }

    /// <summary>
    /// The removed Workshop item's old MCM download that the Coop and Friends launcher turned off at Connect
    /// (WorkshopDuplicates.ParkRetiredWorkshopCopies): its SubModule.xml is renamed, so Index does not see it.
    /// The AD1259 host still takes it as a source for the pack's exact MCM version.
    /// </summary>
    public static Dictionary<string, List<(string Folder, string Directory, string Version)>> ParkedRetired(IEnumerable<string> workshopContentRoots)
    {
        var index = new Dictionary<string, List<(string Folder, string Directory, string Version)>>(StringComparer.OrdinalIgnoreCase);
        foreach (var root in workshopContentRoots.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            foreach (var (moduleId, workshopId) in BannerlordCoopLauncher.Core.Profile.RetiredWorkshopItems.LocalWins)
            {
                var directory = Path.Combine(root, workshopId);
                var parked = Path.Combine(directory, BannerlordCoopLauncher.Core.Modules.WorkshopDuplicates.ParkedFile);
                if (File.Exists(Path.Combine(directory, "SubModule.xml")) || !File.Exists(parked))
                    continue;
                string text;
                try
                {
                    text = File.ReadAllText(parked);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    continue;
                }

                if (!Value(text, "Id").Equals(moduleId, StringComparison.OrdinalIgnoreCase))
                    continue;
                if (!index.TryGetValue(moduleId, out var list))
                    index[moduleId] = list = [];
                list.Add(("Workshop " + workshopId, directory, Value(text, "Version")));
            }
        }

        return index;
    }

    /// <summary>The Expanded Armoury folder has the crash fix DLL. Null when Expanded Armoury is not installed.</summary>
    public static bool? HasArmouryFix(AD1259ModStatus armoury)
    {
        if (armoury.Directories.Count == 0)
            return null;
        var bin = Path.Combine(armoury.Directories[0], "bin", "Win64_Shipping_Client");
        return File.Exists(Path.Combine(bin, AD1259Stack.ArmouryFixDll));
    }

    private static void Add(Dictionary<string, List<(string Folder, string Directory, string Version)>> index, string root, Func<string, string> label)
    {
        if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root))
            return;
        foreach (var directory in Directory.EnumerateDirectories(root))
        {
            var xml = Path.Combine(directory, "SubModule.xml");
            if (!File.Exists(xml))
                continue;
            string text;
            try
            {
                text = File.ReadAllText(xml);
            }
            catch (IOException)
            {
                continue;
            }
            catch (UnauthorizedAccessException)
            {
                continue;
            }

            var id = Value(text, "Id");
            if (id.Length == 0)
                continue;
            if (!index.TryGetValue(id, out var list))
                index[id] = list = [];
            list.Add((label(directory), directory, Value(text, "Version")));
        }
    }

    private static string Value(string xml, string element)
    {
        var match = Regex.Match(xml, "<" + element + "\\s+value\\s*=\\s*\"([^\"]*)\"", RegexOptions.IgnoreCase);
        return match.Success ? match.Groups[1].Value.Trim() : "";
    }
}
