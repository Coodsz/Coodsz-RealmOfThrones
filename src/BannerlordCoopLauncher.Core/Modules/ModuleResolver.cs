using BannerlordCoopLauncher.Core.IO;
using BannerlordCoopLauncher.Core.Models;
using BannerlordCoopLauncher.Core.Parsing;
using BannerlordCoopLauncher.Core.Profile;
using BannerlordCoopLauncher.Core.Steam;

namespace BannerlordCoopLauncher.Core.Modules;

public enum ModuleState
{
    Ready,
    Missing,
    UpdatePending,
    IdMismatch,
    Ambiguous,
    Unavailable,
    WrongGame,
    VersionMismatch,
    InvalidDefinition
}

public sealed record ResolvedModule(
    string Name,
    string ModuleId,
    string? WorkshopId,
    bool Enabled,
    bool IsBaseModule,
    ModuleState State,
    string Detail,
    string? Directory,
    string? LaunchId = null,
    IReadOnlyList<string>? ExtraCopies = null);

public sealed record ModuleResolution(
    IReadOnlyList<ResolvedModule> BaseModules,
    IReadOnlyList<ResolvedModule> Modules,
    bool IncludesCoop)
{
    public IEnumerable<ResolvedModule> All => BaseModules.Concat(Modules.Where(module => module.Enabled));
}

public static class ModuleResolver
{
    public static ModuleResolution Resolve(
        ServerProfile profile,
        string? gameRoot,
        IReadOnlyList<WorkshopLibrary> workshops,
        IReadOnlyDictionary<string, PublishedFile>? remote,
        IFileSystem files,
        IReadOnlyCollection<string>? keepLocalCopy = null)
    {
        var bases = profile.BaseModules
            .Select(id => ResolveBase(id, gameRoot, files))
            .ToList();

        var mods = profile.Mods
            .Select(mod => ResolveMod(profile, mod, gameRoot, workshops, remote, files, keepLocalCopy))
            .ToList();

        return new ModuleResolution(bases, mods, ProfileJson.IncludesCoopModule(profile));
    }

    // After Install mods: Workshop mods Steam reported installed and ready that
    // still look older than the Workshop page. Steam will not download more for
    // them, so they are kept local for the session instead of asking again. A
    // brief download flag on a no-op DownloadItem does not change this.
    public static IReadOnlyList<ResolvedModule> SteamWillNotUpdate(IEnumerable<ResolvedModule> modules, IEnumerable<string> steamReady)
    {
        var ready = new HashSet<string>(steamReady, StringComparer.Ordinal);
        return modules
            .Where(module => module.Enabled
                && module.State == ModuleState.UpdatePending
                && module.WorkshopId != null
                && ready.Contains(module.WorkshopId))
            .ToList();
    }

    public static bool RequiresSteamMetadata(ServerProfile profile) =>
        profile.CheckModVersions && profile.EnabledMods.Any(mod => !mod.Pinned);

    public static string? ConnectBlockReason(ModuleResolution resolution)
    {
        if (!resolution.IncludesCoop)
            return "The enabled module list must include Coop or CoopNightly.";

        if (resolution.All.Any(module => module.State != ModuleState.Ready))
            return "Connect is locked. Resolve the marked modules in Steam, then click Check mods.";

        return null;
    }

    public static IReadOnlyList<string> ConnectProblemLines(ModuleResolution resolution)
    {
        var lines = new List<string>();
        if (!resolution.IncludesCoop)
            lines.Add("Bannerlord Coop is missing. This setup needs Coop or CoopNightly.");
        foreach (var module in resolution.All)
        {
            if (!module.Enabled || module.State == ModuleState.Ready)
                continue;
            lines.Add(module.State switch
            {
                ModuleState.Missing => module.Name + " is missing.",
                ModuleState.VersionMismatch => module.Name + " is the wrong version. " + module.Detail,
                ModuleState.IdMismatch => module.Name + " is not compatible. " + module.Detail,
                ModuleState.UpdatePending => module.Name + " is older than the Workshop copy. Steam has a newer one than yours.",
                ModuleState.Ambiguous => module.Name + " has extra copies. " + module.Detail,
                _ => module.Name + " needs attention. " + module.Detail
            });
        }

        return lines;
    }

    /// <summary>
    /// The version in the SubModule.xml of an installed module, also when the setup has it off (a
    /// mod that is off resolves without a folder): its Workshop folder first, then the game's
    /// Modules folder. Empty when no copy with that id is found.
    /// </summary>
    public static string InstalledVersion(string moduleId, string? workshopId, string? gameRoot, IReadOnlyList<WorkshopLibrary> workshops, IFileSystem files)
    {
        if (string.IsNullOrWhiteSpace(moduleId))
            return "";
        var mod = new ProfileMod { ModuleId = moduleId, WorkshopId = string.IsNullOrWhiteSpace(workshopId) ? "0" : workshopId };
        var candidates = FindCandidates(mod, gameRoot, workshops, files);
        // The game loads a Coop in its Modules folder before the Workshop Coop, so that copy's version is the one that runs.
        if (moduleId.Equals(BannerlordCoopModules.Coop, StringComparison.OrdinalIgnoreCase))
            candidates = candidates.OrderBy(directory => IsGameModule(directory, gameRoot) ? 0 : 1).ToList();
        foreach (var directory in candidates)
        {
            var xml = files.ReadAllText(Path.Combine(directory, "SubModule.xml"));
            if (xml != null && SubModuleXml.TryRead(xml, out var info, out _) && info != null
                && info.Id.Equals(moduleId, StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(info.Version))
                return info.Version!;
        }

        return "";
    }

    /// <summary>
    /// True when the Coop this setup loads is the Coop nightly (CoopInstall.IsNightly), resolved exactly as Resolve does for
    /// the whole setup: the setup's own Coop row and picked id, every Workshop library and the Modules folder. The client
    /// offers War Sails (NavalDLC) by it, so the offer always matches the Coop that loads.
    /// </summary>
    public static bool LoadsNightly(ServerProfile profile, string? gameRoot, IReadOnlyList<WorkshopLibrary> workshops, IFileSystem files)
    {
        var coop = profile.Mods.FirstOrDefault(mod => mod.Enabled && IsBannerlordCoop(mod.ModuleId));
        if (coop == null)
            return false;
        var resolved = ResolveMod(profile, coop, gameRoot, workshops, null, files, null);
        var xml = resolved.Directory == null ? null : files.ReadAllText(Path.Combine(resolved.Directory, "SubModule.xml"));
        return xml != null && SubModuleXml.TryRead(xml, out var info, out _) && info != null && CoopInstall.IsNightly(info.Id, info.Version);
    }

    internal static bool IsBannerlordCoop(string? moduleId) =>
        moduleId != null && (moduleId.Equals("Coop", StringComparison.OrdinalIgnoreCase)
            || moduleId.Equals("CoopNightly", StringComparison.OrdinalIgnoreCase));

    private static ResolvedModule ResolveBase(string moduleId, string? gameRoot, IFileSystem files)
    {
        if (string.IsNullOrWhiteSpace(gameRoot))
        {
            return new ResolvedModule(moduleId, moduleId, null, true, true, ModuleState.Missing,
                "Select your Bannerlord installation folder.", null);
        }

        var directory = Path.Combine(gameRoot, "Modules", moduleId);
        var xmlPath = Path.Combine(directory, "SubModule.xml");
        var xml = files.ReadAllText(xmlPath);
        if (xml == null)
        {
            return new ResolvedModule(moduleId, moduleId, null, true, true, ModuleState.Missing,
                "Not found in the Bannerlord Modules folder.", null);
        }

        if (!SubModuleXml.TryRead(xml, out var info, out var error) || info == null)
        {
            return new ResolvedModule(moduleId, moduleId, null, true, true, ModuleState.IdMismatch, error, directory);
        }

        if (!string.Equals(info.Id, moduleId, StringComparison.OrdinalIgnoreCase))
        {
            return new ResolvedModule(moduleId, moduleId, null, true, true, ModuleState.IdMismatch,
                $"Module ID mismatch: {info.Id}", directory);
        }

        return new ResolvedModule(moduleId, moduleId, null, true, true, ModuleState.Ready, "Installed.", directory);
    }

    private static ResolvedModule ResolveMod(
        ServerProfile profile,
        ProfileMod mod,
        string? gameRoot,
        IReadOnlyList<WorkshopLibrary> workshops,
        IReadOnlyDictionary<string, PublishedFile>? remote,
        IFileSystem files,
        IReadOnlyCollection<string>? keepLocalCopy)
    {
        if (!ProfileJson.IsValidMod(mod))
        {
            var detail = string.IsNullOrWhiteSpace(mod.Name)
                ? "This mod has no name."
                : !ProfileJson.IsSafeModuleId(mod.ModuleId)
                    ? "This mod id has a character that can't be in a folder name."
                    : "This mod needs a numeric Workshop id. Mods that are not on Steam use 0.";
            return new ResolvedModule(mod.Name, mod.ModuleId, mod.WorkshopId, mod.Enabled, false,
                ModuleState.InvalidDefinition, detail, null);
        }

        if (!mod.Enabled)
        {
            return new ResolvedModule(mod.Name, mod.ModuleId, mod.WorkshopId, false, false,
                ModuleState.Ready, "Off for you. It stays installed.", null);
        }

        var candidates = FindCandidates(mod, gameRoot, workshops, files).ToList();
        var readable = new List<(string Directory, SubModuleInfo Info)>();
        string? readError = null;
        foreach (var directory in candidates)
        {
            var xml = files.ReadAllText(Path.Combine(directory, "SubModule.xml"));
            if (xml == null)
                continue;
            if (!SubModuleXml.TryRead(xml, out var info, out var error) || info == null)
            {
                readError = error;
                continue;
            }

            readable.Add((directory, info));
        }

        var matching = readable
            .Where(item => IdsMatch(item.Info.Id, mod.ModuleId))
            .ToList();

        string? skippedCopies = null;
        List<string>? extraCopies = null;
        if (matching.Count > 1)
        {
            var chosen = ChooseCopy(matching, mod, gameRoot, files);
            if (chosen == null)
            {
                var folders = string.Join(", ", matching.Select(item => Path.GetFileName(item.Directory)));
                return new ResolvedModule(mod.Name, mod.ModuleId, mod.WorkshopId, true, false,
                    ModuleState.Ambiguous,
                    "More than one folder is this mod. Keep one copy and move the others out of the Bannerlord Modules folder, then click Check mods. Folders: " + folders + ".",
                    matching[0].Directory);
            }

            // Only a copy with the very same id and a different version is worth a
            // warning. Coop and CoopNightly are told apart on the command line, and an
            // identical copy (same id, same version) loads the same mod either way.
            var others = matching
                .Where(item => !string.Equals(item.Directory, chosen.Value.Directory, StringComparison.OrdinalIgnoreCase)
                    && string.Equals(item.Info.Id, chosen.Value.Info.Id, StringComparison.OrdinalIgnoreCase)
                    && !string.Equals(item.Info.Version, chosen.Value.Info.Version, StringComparison.OrdinalIgnoreCase)
                    && !IsSkippedWorkshopCoop(item, chosen.Value, mod, gameRoot))
                .ToList();
            if (others.Count > 0)
            {
                skippedCopies = " Extra copies were skipped: " + string.Join(", ", others.Select(item => Path.GetFileName(item.Directory))) + ".";
                extraCopies = others.Select(item => item.Directory).ToList();
            }
            matching = new List<(string Directory, SubModuleInfo Info)> { chosen.Value };
        }

        if (matching.Count == 0)
        {
            var named = readable
                .Where(item => IsNamedForMod(item.Directory, mod))
                .ToList();
            if (named.Count > 0)
            {
                return new ResolvedModule(mod.Name, mod.ModuleId, mod.WorkshopId, true, false,
                    ModuleState.IdMismatch,
                    $"Module ID mismatch: {named[0].Info.Id}", named[0].Directory);
            }

            if (readError != null)
            {
                return new ResolvedModule(mod.Name, mod.ModuleId, mod.WorkshopId, true, false,
                    ModuleState.IdMismatch, readError, null);
            }

            return new ResolvedModule(mod.Name, mod.ModuleId, mod.WorkshopId, true, false,
                ModuleState.Missing,
                IsBannerlordCoop(mod.ModuleId)
                    ? CoopInstall.GitHubClientInstallLine(gameRoot, files) ?? CoopInstall.ClientMissingLine
                    : mod.WorkshopId.Length > 1 && mod.WorkshopId.All(char.IsDigit)
                    ? "Click Install mods, then subscribe in Steam."
                    : "This mod is not in the game's Modules folder.",
                null);
        }

        var installed = matching[0];
        var launchId = CoopLaunchId(mod.ModuleId, installed.Info.Id);
        if (!string.IsNullOrWhiteSpace(mod.RequiredVersion) &&
            !string.Equals(installed.Info.Version, mod.RequiredVersion, StringComparison.OrdinalIgnoreCase))
        {
            return WithCopies(new ResolvedModule(mod.Name, mod.ModuleId, mod.WorkshopId, true, false,
                ModuleState.VersionMismatch,
                $"Installed {installed.Info.Version ?? "unknown"}, required {mod.RequiredVersion}.",
                installed.Directory, launchId), extraCopies);
        }

        if (!profile.CheckModVersions || mod.Pinned)
        {
            var detail = (!mod.Pinned ? "Installed."
                // The Coop row names the Coop the game loads and where it comes from (a Coop unzipped into Modules by hand).
                : IsBannerlordCoop(mod.ModuleId) ? CoopInstall.PinnedDetail(installed.Info.Version ?? "", IsGameModule(installed.Directory, gameRoot))
                : "Pinned. Steam version check skipped.") + skippedCopies;
            return WithCopies(new ResolvedModule(mod.Name, mod.ModuleId, mod.WorkshopId, true, false,
                ModuleState.Ready, detail, installed.Directory, launchId), extraCopies);
        }

        // A local mod (Nexus, ModDB, the launcher folder) has no Workshop page to check.
        if (mod.WorkshopId == "0")
        {
            return WithCopies(new ResolvedModule(mod.Name, mod.ModuleId, mod.WorkshopId, true, false,
                ModuleState.Ready, "Installed." + skippedCopies, installed.Directory, launchId), extraCopies);
        }

        if (remote == null || !remote.TryGetValue(mod.WorkshopId, out var published))
        {
            // Folder is already installed. A flaky Steam reply used to mark this Unavailable and
            // lock Connect even though the files were fine (seen with ServerSettingsLock).
            return WithCopies(new ResolvedModule(mod.Name, mod.ModuleId, mod.WorkshopId, true, false,
                ModuleState.Ready,
                "Installed. Steam could not verify this Workshop item. Open Steam and click Check mods if you want a version check.",
                installed.Directory, launchId), extraCopies);
        }

        if (published.Result != 1)
        {
            // Steam's public lookup answers "not found" for items that were made
            // hidden, friends-only or unlisted (both More Nations items). The files
            // Steam already installed are still the setup's mod, so they are used.
            return WithCopies(new ResolvedModule(mod.Name, mod.ModuleId, mod.WorkshopId, true, false,
                ModuleState.Ready,
                (published.Result is 9 or 15
                    ? "Installed. Steam does not show this Workshop item publicly any more (hidden or removed), so its version was not checked. The copy on this PC is used."
                    : "Installed. Steam could not verify this Workshop item right now (Steam code " + published.Result + "), so its version was not checked. The copy on this PC is used.") + skippedCopies,
                installed.Directory, launchId), extraCopies);
        }

        if (published.ConsumerAppId != 0 && published.ConsumerAppId != SteamInstallFinder.BannerlordAppId)
        {
            return WithCopies(new ResolvedModule(mod.Name, mod.ModuleId, mod.WorkshopId, true, false,
                ModuleState.WrongGame, "Other game's Workshop item rejected", installed.Directory, launchId), extraCopies);
        }

        // A copy put in the game's Modules folder by hand (Nexus, ModDB) is not a
        // Steam download, so Steam's update time says nothing about it. Marking it
        // Update made Install mods try Steam again and again.
        if (!IsWorkshopItem(installed.Directory, mod.WorkshopId))
        {
            return WithCopies(new ResolvedModule(mod.Name, mod.ModuleId, mod.WorkshopId, true, false,
                ModuleState.Ready,
                "Installed in the game's Modules folder, not from Steam, so its Workshop version was not checked." + skippedCopies,
                installed.Directory, launchId), extraCopies);
        }

        var localTime = LocalTime(workshops, mod.WorkshopId);
        var manifestOk = workshops.Count == 0 || workshops.All(library => library.ManifestParsed);
        if (!manifestOk)
        {
            return WithCopies(new ResolvedModule(mod.Name, mod.ModuleId, mod.WorkshopId, true, false,
                ModuleState.Unavailable, "Unknown Steam manifest format.", installed.Directory, launchId), extraCopies);
        }

        var localManifest = LocalManifest(workshops, mod.WorkshopId);
        var sameFiles = !string.IsNullOrWhiteSpace(published.ContentHandle)
            && string.Equals(localManifest, published.ContentHandle, StringComparison.Ordinal);
        var steamLatest = LatestManifest(workshops, mod.WorkshopId);
        if (!sameFiles && localManifest != null && steamLatest != null
            && string.Equals(localManifest, steamLatest, StringComparison.Ordinal))
        {
            // Steam itself says this is the newest copy it has (latest_manifest is the
            // installed one), so DownloadItem does nothing. Calling it Update only made
            // Install mods loop. If the host has a newer copy, the join says so.
            return WithCopies(new ResolvedModule(mod.Name, mod.ModuleId, mod.WorkshopId, true, false,
                ModuleState.Ready,
                "Installed. Steam says this is the newest copy it has. The Workshop page shows a newer upload that Steam has not offered yet." + skippedCopies,
                installed.Directory, launchId), extraCopies);
        }

        var steamKnowsNewer = !sameFiles && localManifest != null && steamLatest != null;
        if (!sameFiles && (steamKnowsNewer || localTime == null || published.TimeUpdated > localTime.Value))
        {
            if (keepLocalCopy != null && keepLocalCopy.Contains(mod.WorkshopId))
            {
                // Install mods already asked Steam, and Steam said this copy is
                // current. Locking Connect again only made the button loop.
                return WithCopies(new ResolvedModule(mod.Name, mod.ModuleId, mod.WorkshopId, true, false,
                    ModuleState.Ready,
                    "Installed. Steam would not download the newest Workshop update yet, so the copy on this PC is used." + skippedCopies,
                    installed.Directory, launchId), extraCopies);
            }

            return WithCopies(new ResolvedModule(mod.Name, mod.ModuleId, mod.WorkshopId, true, false,
                ModuleState.UpdatePending, "Steam reports a pending update.", installed.Directory, launchId), extraCopies);
        }

        return WithCopies(new ResolvedModule(mod.Name, mod.ModuleId, mod.WorkshopId, true, false,
            ModuleState.Ready, "Latest Workshop release verified." + skippedCopies, installed.Directory, launchId), extraCopies);
    }

    private static ResolvedModule WithCopies(ResolvedModule module, List<string>? extraCopies) =>
        extraCopies == null ? module : module with { ExtraCopies = extraCopies };

    private static bool IdsMatch(string? installedId, string expectedId)
    {
        if (string.Equals(installedId, expectedId, StringComparison.Ordinal))
            return true;
        return IsBannerlordCoop(installedId) && IsBannerlordCoop(expectedId);
    }

    private static string? CoopLaunchId(string expectedId, string? installedId)
    {
        if (!IsBannerlordCoop(expectedId) || !IsBannerlordCoop(installedId))
            return null;
        return string.Equals(expectedId, installedId, StringComparison.OrdinalIgnoreCase) ? null : installedId;
    }

    private static (string Directory, SubModuleInfo Info)? ChooseCopy(
        List<(string Directory, SubModuleInfo Info)> matching,
        ProfileMod mod,
        string? gameRoot,
        IFileSystem files)
    {
        // The removed official MCM item never wins over a copy in the game's Modules folder: that copy is the
        // one from Nexus the host runs too. A setup saved before v1.2.76 still names MCM by its old Workshop id,
        // and the old v5.12.3 download made the join refuse (CR-J5AUW).
        var workshop = RetiredWorkshopItems.IsRetired(mod.WorkshopId) && RetiredWorkshopItems.LocalCopyWins(mod.ModuleId)
            ? []
            : matching.Where(item => IsWorkshopItem(item.Directory, mod.WorkshopId)).ToList();
        // A Coop unzipped into the game's Modules folder by hand (Coop 0.1.7 from GitHub or ModDB) has the
        // Workshop Coop's id, and the game loads the Modules copy first, so that copy is the one that runs.
        if (ModulesCoop(matching, workshop, mod, gameRoot, files) is { } manual)
            return manual;
        if (workshop.Count > 0)
            return workshop.OrderBy(item => item.Directory.Length).First();

        var inGame = matching.Where(item => IsGameModule(item.Directory, gameRoot)).ToList();
        var pool = inGame.Count > 0 ? inGame : matching;
        var named = pool.Where(item => FolderName(item.Directory).Equals(mod.ModuleId, StringComparison.OrdinalIgnoreCase)).ToList();
        if (named.Count == 1)
            return named[0];

        var plain = pool.Where(item => !IsSpareCopy(item.Directory)).ToList();
        if (plain.Count == 1)
            return plain[0];
        if (plain.Count > 1 && plain.All(item => IsBannerlordCoop(item.Info.Id)))
        {
            var release = plain.FirstOrDefault(item => item.Info.Id.Equals(BannerlordCoopModules.Coop, StringComparison.OrdinalIgnoreCase));
            if (!string.IsNullOrEmpty(release.Directory))
                return release;
            return plain[0];
        }

        return null;
    }

    // The Modules folder copy with id Coop, the folder named Coop first, when the Workshop copies have that
    // id too. A Workshop CoopNightly is another id the launcher names on the command line, so it stays.
    // A Coop nightly in the Modules folder (CoopNightly) wins too, after a Modules copy with id Coop, and the launcher
    // names CoopNightly on the command line (CoopLaunchId), but only when the setup names CoopNightly, or the nightly was
    // made for this game (NightlyFitsGame). A stale nightly would keep a player off a Workshop Coop server.
    private static (string Directory, SubModuleInfo Info)? ModulesCoop(
        List<(string Directory, SubModuleInfo Info)> matching,
        List<(string Directory, SubModuleInfo Info)> workshop,
        ProfileMod mod,
        string? gameRoot,
        IFileSystem files)
    {
        if (workshop.Count == 0 || !IsBannerlordCoop(mod.ModuleId)
            || !workshop.All(item => item.Info.Id.Equals(BannerlordCoopModules.Coop, StringComparison.OrdinalIgnoreCase)))
            return null;
        var picked = mod.ModuleId.Equals(BannerlordCoopModules.CoopNightly, StringComparison.OrdinalIgnoreCase);
        static bool IsCoopId((string Directory, SubModuleInfo Info) item) => item.Info.Id.Equals(BannerlordCoopModules.Coop, StringComparison.OrdinalIgnoreCase);
        // The nightly is held against the newest Coop there is, a Modules copy with id Coop (Modules\Coop.10) included.
        // The game loads either id only when the launcher names it, so the launcher's pick decides which one runs.
        var newest = workshop.Concat(matching.Where(item => IsGameModule(item.Directory, gameRoot) && IsCoopId(item)))
            .Select(item => item.Info.Version).OrderByDescending(version => version, Comparer<string?>.Create(CoopInstall.Compare)).First();
        bool Fits((string Directory, SubModuleInfo Info) item) =>
            item.Info.Id.Equals(BannerlordCoopModules.CoopNightly, StringComparison.OrdinalIgnoreCase)
            && (picked || NightlyFitsGame(item, newest, gameRoot, files));
        var inGame = matching
            .Where(item => IsGameModule(item.Directory, gameRoot) && (IsCoopId(item) || Fits(item)))
            // A fitting nightly first, as NightlyBeatsWorkshop and LoadsNightly have it, then a Modules copy with id Coop.
            .OrderBy(item => Fits(item) ? 0 : 1)
            .ThenBy(item => FolderName(item.Directory).Equals(BannerlordCoopModules.Coop, StringComparison.OrdinalIgnoreCase) ? 0 : 1)
            .ThenBy(item => IsSpareCopy(item.Directory) ? 1 : 0)
            .ToList();
        return inGame.Count > 0 ? inGame[0] : null;
    }

    // The nightly was made for the installed game (its Native DependentVersion is the game's Native version) and is not
    // older than the Workshop Coop. On 1.5.7 the Workshop's 0.1.7 can't run anyway.
    private static bool NightlyFitsGame((string Directory, SubModuleInfo Info) nightly, string? workshopVersion, string? gameRoot, IFileSystem files)
    {
        if (string.IsNullOrWhiteSpace(gameRoot))
            return false;
        var xml = files.ReadAllText(Path.Combine(nightly.Directory, "SubModule.xml"));
        var native = files.ReadAllText(Path.Combine(gameRoot, "Modules", "Native", "SubModule.xml"));
        return xml != null && native != null && SubModuleXml.TryRead(native, out var game, out _) && game != null
            && CoopInstall.NightlyBeatsWorkshop(false, ModuleManifestReader.Parse(xml)?.NativeVersion, nightly.Info.Version, game.Version, workshopVersion);
    }

    // With a Modules Coop in use, the Workshop Coop is the copy the game skips, not a second copy to move.
    private static bool IsSkippedWorkshopCoop((string Directory, SubModuleInfo Info) item, (string Directory, SubModuleInfo Info) chosen, ProfileMod mod, string? gameRoot) =>
        IsBannerlordCoop(mod.ModuleId) && IsGameModule(chosen.Directory, gameRoot) && IsWorkshopItem(item.Directory, mod.WorkshopId);

    private static bool IsWorkshopItem(string directory, string? workshopId)
    {
        if (string.IsNullOrWhiteSpace(workshopId) || workshopId == "0")
            return false;
        var full = Path.GetFullPath(directory);
        var marker = Path.DirectorySeparatorChar + workshopId;
        return full.EndsWith(marker, StringComparison.OrdinalIgnoreCase)
            || full.Contains(marker + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsSpareCopy(string directory)
    {
        var name = FolderName(directory);
        return name.Contains("playtest", StringComparison.OrdinalIgnoreCase)
            || name.Contains("kept", StringComparison.OrdinalIgnoreCase)
            || name.Contains("backup", StringComparison.OrdinalIgnoreCase)
            || name.Contains("-old", StringComparison.OrdinalIgnoreCase);
    }

    private static string FolderName(string directory) =>
        Path.GetFileName(directory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));

    private static long? LocalTime(IReadOnlyList<WorkshopLibrary> workshops, string workshopId)
    {
        // With two Steam libraries an old entry can stay behind in one of them.
        // The newest time is the copy Steam last installed.
        long? newest = null;
        foreach (var library in workshops)
        {
            if (library.InstalledTimeUpdated.TryGetValue(workshopId, out var time) && (newest == null || time > newest))
                newest = time;
        }

        return newest ?? (workshops.Count == 0 ? 0 : null);
    }

    private static string? LocalManifest(IReadOnlyList<WorkshopLibrary> workshops, string workshopId)
    {
        foreach (var library in workshops)
        {
            if (library.InstalledManifests != null && library.InstalledManifests.TryGetValue(workshopId, out var manifest))
                return manifest;
        }

        return null;
    }

    private static string? LatestManifest(IReadOnlyList<WorkshopLibrary> workshops, string workshopId)
    {
        foreach (var library in workshops)
        {
            if (library.InstalledManifests != null && library.InstalledManifests.ContainsKey(workshopId)
                && library.LatestManifests != null && library.LatestManifests.TryGetValue(workshopId, out var manifest))
                return manifest;
        }

        return null;
    }

    private static bool IsGameModule(string directory, string? gameRoot)
    {
        if (string.IsNullOrWhiteSpace(gameRoot))
            return false;
        var modulesRoot = Path.GetFullPath(Path.Combine(gameRoot, "Modules"));
        var full = Path.GetFullPath(directory);
        return full.StartsWith(modulesRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsNamedForMod(string directory, ProfileMod mod)
    {
        var name = Path.GetFileName(directory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        if (string.Equals(name, mod.ModuleId, StringComparison.OrdinalIgnoreCase))
            return true;
        if (string.IsNullOrWhiteSpace(mod.WorkshopId))
            return false;
        return string.Equals(name, mod.WorkshopId, StringComparison.OrdinalIgnoreCase)
            || directory.Contains(Path.DirectorySeparatorChar + mod.WorkshopId + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    }

    private static IEnumerable<string> FindCandidates(
        ProfileMod mod,
        string? gameRoot,
        IReadOnlyList<WorkshopLibrary> workshops,
        IFileSystem files)
    {
        // A mod whose Workshop item was hidden or removed is a local mod now, but the
        // copy Steam downloaded before is still on this PC and still works.
        var retired = mod.WorkshopId == "0" ? RetiredWorkshopItems.For(mod.ModuleId) : null;
        foreach (var library in workshops)
        {
            var root = Path.Combine(library.ContentDirectory, mod.WorkshopId);
            foreach (var directory in FindModuleDirectories(root, files))
                yield return directory;
            if (retired == null)
                continue;
            foreach (var directory in FindModuleDirectories(Path.Combine(library.ContentDirectory, retired), files))
                yield return directory;
        }

        if (!string.IsNullOrWhiteSpace(gameRoot))
        {
            var modulesRoot = Path.Combine(gameRoot, "Modules");
            if (files.DirectoryExists(modulesRoot))
            {
                var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var child in files.EnumerateDirectories(modulesRoot))
                {
                    if (!seen.Add(Path.GetFullPath(child)))
                        continue;
                    if (files.FileExists(Path.Combine(child, "SubModule.xml")))
                        yield return child;
                }
            }
        }
    }

    private static IEnumerable<string> FindModuleDirectories(string root, IFileSystem files)
    {
        return Walk(root, files, 0);
    }

    private static IEnumerable<string> Walk(string directory, IFileSystem files, int depth)
    {
        if (depth > 3 || !files.DirectoryExists(directory))
            yield break;

        if (files.FileExists(Path.Combine(directory, "SubModule.xml")))
            yield return directory;

        foreach (var child in files.EnumerateDirectories(directory))
        {
            foreach (var nested in Walk(child, files, depth + 1))
                yield return nested;
        }
    }
}
