using System.Xml;
using BannerlordCoopLauncher.Core.Joining;
using BannerlordCoopLauncher.Core.Modules;

namespace RealmOfThrones.Core;

/// <summary>
/// The checks the Realm of Thrones Client Launcher runs at Connect, in the Burning Empires order: after it got
/// the server's mods, first the exact Coop build (CoopBuildProblem), then the Realm of Thrones core on this PC
/// (CoreProblems), then the host's imported mod list against this PC (HostProblems). Each problem is one plain line,
/// and any line keeps the game from starting, since the Coop nightly turns a join down on any difference in module
/// ids or versions, and on any other Coop build.
/// </summary>
public static class RotJoinCheck
{
    public const string IncompletePlayerMap = "Could not read the full Realm of Thrones campaign map. Install the four mod folders from the Nexus download for Bannerlord 1.4.8 into the game's Modules folder, then click Check mods.";

    // Server staging keeps the same SubModule.xml version as the player map, but strips
    // the border entities and terrain from the scene. Use the resolved module directory:
    // both ROT-Map and ROT_Map are in use, and the folder name need not match the module id.
    public static string? PlayerMapProblem(string moduleDirectory)
    {
        var scene = Path.Combine(moduleDirectory, "SceneObj", "Main_map", "scene.xscene");
        try
        {
            using var file = File.OpenRead(scene);
            using var reader = XmlReader.Create(file);
            var border = false;
            var terrain = false;
            while (reader.Read())
            {
                if (reader.NodeType != XmlNodeType.Element)
                    continue;
                if (reader.Name == "game_entity" && reader.GetAttribute("name") == "border_min")
                    border = true;
                if (reader.Depth == 1 && reader.Name == "terrain")
                    terrain = true;
            }
            return border && terrain ? null : IncompletePlayerMap;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or XmlException)
        {
            return IncompletePlayerMap;
        }
    }

    /// <summary>
    /// The player's Coop build (CoopBuildId, null when it cannot be read) against the host's: the build on the Coop
    /// entry of the host's imported list, or the RoT build this launcher carries (RotStack.CoopBuild) when no list or an
    /// old list without it is there (the Server Tool only starts that build). Null when they are the same. A host on
    /// another build than the one carried needs a tool update on one side; any other difference is this PC's Coop,
    /// which the launcher replaces with the carried build at Connect. A launcher built without the zip (carriesCoop
    /// false, a Debug build) puts nothing in, so it keeps the lines of before that ask for the host's build.
    /// </summary>
    public static string? CoopBuildProblem(string? thisPc, IReadOnlyList<SharedModEntry> hostList, bool carriesCoop = true)
    {
        var carried = RotStack.CoopBuild;
        var host = HostCoopBuild(hostList);
        if (CoopBuildId.Same(thisPc, host ?? carried.Build))
            return null;
        if (!carriesCoop)
            return thisPc == null
                ? "The launcher could not read your Coop build (Common.dll in the Coop folder of the game's Modules folder). "
                    + "The host runs " + CoopBuildId.Short(host ?? carried.Build) + ". Get the same Coop build as the host, then click Connect again."
                : host != null
                    ? "Your Coop build is " + CoopBuildId.Short(thisPc) + " and the host's is " + CoopBuildId.Short(host)
                        + ". Coop only lets you join on the very same build. Get the same Coop build as the host, then click Connect again."
                    : "Your Coop build is " + CoopBuildId.Short(thisPc) + ", but Realm of Thrones in Coop runs on " + CoopBuildId.Short(carried.Build)
                        + ". Coop only lets you join on the very same build. Get the same Coop build as the host, then click Connect again.";
        if (host != null && !CoopBuildId.Same(host, carried.Build))
            return "The host runs Coop " + CoopBuildId.Short(host) + " and this launcher has Coop " + carried.Label
                + ". Update the Client Launcher, or ask the host to update the Server Tool.";
        return thisPc == null
            ? "The launcher could not read the Coop build on this PC (Common.dll in Modules\\Coop). This launcher carries Coop " + carried.Label
                + " and puts it in when you click Connect."
            : "The Coop on this PC is " + CoopBuildId.Short(thisPc) + ", not Coop " + carried.Label
                + " that this launcher carries. The launcher puts its own in when you click Connect.";
    }

    /// <summary>The log line when this PC has the Coop build the host's list names, or null.</summary>
    public static string? SameCoopBuild(string? thisPc, IReadOnlyList<SharedModEntry> hostList) =>
        HostCoopBuild(hostList) is { } host && CoopBuildId.Same(thisPc, host)
            ? "Coop is " + (CoopBuildId.Same(host, RotStack.CoopBuild.Build) ? RotStack.CoopBuild.Label : CoopBuildId.Short(host)) + ", the same as the host."
            : null;

    // The build on the Coop entry of the host's list: CoopBuild, else a Version that is a build (RotSharedList writes it
    // in both). Null for an old list that only says v0.1.8.
    private static string? HostCoopBuild(IReadOnlyList<SharedModEntry> hostList)
    {
        var entry = hostList.FirstOrDefault(item => (item.Id ?? "").Trim().Equals(RotStack.CoopModuleId, StringComparison.OrdinalIgnoreCase));
        if (!string.IsNullOrWhiteSpace(entry?.CoopBuild))
            return entry.CoopBuild.Trim();
        return CoopBuildId.Commit(entry?.Version) != null ? entry!.Version.Trim() : null;
    }

    /// <summary>The line under Connect after a difference from the host's list.</summary>
    public const string SameVersions =
        "The host and every player need the same mods at the same versions, or the server turns the join down. Install the host's versions of the mods named in the log, or ask the host for a new mod list from Share mod list and click Import mod list on the Mods page.";

    /// <summary>The line when MCM is not on this PC. Its Workshop item was removed, so it comes from Nexus Mods.</summary>
    public static string McmMissing =>
        "MCM is not on this PC. Get the MCM version the host uses from Nexus Mods and put it in the game's Modules folder. MCM is on Nexus Mods at " + RotStack.McmNexusPage;

    /// <summary>The name a core module is shown with.</summary>
    public static string Name(string moduleId)
    {
        if (moduleId.Equals(RotStack.CoopModuleId, StringComparison.OrdinalIgnoreCase))
            return "Bannerlord Coop";
        if (moduleId.Equals(RotStack.CompatModuleId, StringComparison.OrdinalIgnoreCase))
            return RotStack.CompatName;
        if (moduleId.Equals(RotStack.NavalModuleId, StringComparison.OrdinalIgnoreCase))
            return "War Sails (NavalDLC)";
        if (RotStack.IsRotModule(moduleId))
            return RotStack.Name + " (" + moduleId + ")";
        return RotStack.Libraries.FirstOrDefault(mod => mod.ModuleId.Equals(moduleId, StringComparison.OrdinalIgnoreCase))?.Name ?? moduleId;
    }

    /// <summary>MCM is got by hand from Nexus Mods; the other library mods come from the Steam Workshop.</summary>
    public static bool FromNexus(RotLibrary mod) => mod.ModuleId.Equals(RotStack.Mcm.ModuleId, StringComparison.OrdinalIgnoreCase);

    /// <summary>A missing library mod for the Connect prompt: Steam gets a Workshop one, MCM is got by hand from Nexus Mods.</summary>
    public static ServerModNeed LibraryNeed(RotLibrary mod) => FromNexus(mod)
        ? new ServerModNeed(mod.ModuleId, mod.Name, ServerModProblem.Missing, ServerModFetch.ByHand, RotStack.McmNexusPage)
        : new ServerModNeed(mod.ModuleId, mod.Name, ServerModProblem.Missing, ServerModFetch.Workshop, ServerModNeeds.WorkshopPage(mod.WorkshopId));

    /// <summary>
    /// Problems with the Realm of Thrones core on this PC, one line each, in load order: a module that is missing
    /// (a Realm of Thrones module, Coop, the compat module, War Sails, a library mod), a Realm of Thrones module
    /// that is not v8.1.8 (ROT_Map v8.1.8.2) or a compat module of another version (a leading v does not count),
    /// and an MCM older than RotStack.McmServerMinimum. versionOf gives a module's version on this PC, "" when its
    /// SubModule.xml names none, or null when the module is not there. withWorkshopLibraries false leaves out a
    /// missing Workshop library mod, since Connect gets those from Steam first. carriesCoop false (a launcher built
    /// without the zip) asks for the host's Coop build instead of saying Connect puts it in.
    /// </summary>
    public static IReadOnlyList<string> CoreProblems(Func<string, string?> versionOf, bool withWorkshopLibraries = true, bool carriesCoop = true) =>
        RotStack.ClientModules.Select(id => ModuleProblem(id, versionOf(id), withWorkshopLibraries, carriesCoop)).OfType<string>().ToList();

    /// <summary>The CoreProblems line of one core module, or null when it is fine. version is null when the module is not on this PC.</summary>
    public static string? ModuleProblem(string id, string? version, bool withWorkshopLibraries = true, bool carriesCoop = true)
    {
        var library = RotStack.Libraries.FirstOrDefault(mod => mod.ModuleId.Equals(id, StringComparison.OrdinalIgnoreCase));
        if (version == null)
            return library == null || FromNexus(library) || withWorkshopLibraries ? MissingLine(id, library, carriesCoop) : null;

        if (RotStack.ContentVersions.TryGetValue(id, out var wanted)
            && !RotServerPath.Short(version).Equals(RotServerPath.Short(wanted), StringComparison.OrdinalIgnoreCase))
            return version.Trim().Length == 0
                ? "The launcher could not read the version of " + Name(id) + " on this PC. Realm of Thrones in Coop needs " + wanted + "."
                : Name(id) + " on this PC is " + version.Trim() + ". Realm of Thrones in Coop needs " + wanted + ".";

        if (library != null && FromNexus(library)
            && (RotServerPath.ParseVersion(version) is not { } mcm || mcm < RotServerPath.ParseVersion(RotStack.McmServerMinimum)!))
            return version.Trim().Length == 0
                ? "The launcher could not read the version of MCM on this PC. Realm of Thrones in Coop needs MCM " + RotStack.McmServerMinimum
                    + " or newer, the same version as the host. MCM is on Nexus Mods at " + RotStack.McmNexusPage
                : "MCM " + version.Trim() + " on this PC is too old. Realm of Thrones in Coop needs MCM " + RotStack.McmServerMinimum
                    + " or newer, the same version as the host. MCM is on Nexus Mods at " + RotStack.McmNexusPage;
        return null;
    }

    // The Realm of Thrones modules are put in by hand (Module folder on Play opens the folder); Coop comes with the launcher.

    private static string MissingLine(string id, RotLibrary? library, bool carriesCoop)
    {
        if (library != null)
            return FromNexus(library)
                ? McmMissing
                : library.Name + " is not on this PC. Subscribe to it on the Steam Workshop, wait for Steam to download it, then click Connect again.";
        if (RotStack.IsRotModule(id))
            return Name(id) + " is not on this PC. Download Realm of Thrones " + RotStack.RotVersion + " from its mod page and unpack its four folders into the game's Modules folder (click Module folder), then click Check mods.";
        if (id.Equals(RotStack.NavalModuleId, StringComparison.OrdinalIgnoreCase))
            return "War Sails (NavalDLC) is not on this PC. Realm of Thrones needs the War Sails DLC. Install it in Steam, then click Check mods.";
        if (id.Equals(RotStack.CoopModuleId, StringComparison.OrdinalIgnoreCase))
            return carriesCoop
                ? Name(id) + " (" + id + ") is not on this PC. This launcher carries Coop " + RotStack.CoopBuild.Label + " and puts it in when you click Connect."
                : Name(id) + " (" + id + ") is not on this PC. Ask the host for the Coop build they use and unpack it into the game's Modules folder (click Module folder), then click Check mods.";
        if (id.Equals(RotStack.CompatModuleId, StringComparison.OrdinalIgnoreCase))
            return RotStack.CompatName + " " + RotStack.CompatVersion + " is not on this PC. It comes with this launcher, so download the launcher again, then click Check mods.";
        return id + " is not in the game's Modules folder. Check the game files of Bannerlord in Steam.";
    }

    /// <summary>
    /// The host's imported list against this PC (RotSharedList.VersionProblems and NotOnHost): a module of the list
    /// missing here or at another version, then a module this PC loads that the list does not have. Coop leaves the
    /// official game modules and the client add ons out of its join check (CoopJoinRules), so they are left out here
    /// too. No list imported (empty) gives no problems. installed maps each module id on this PC to its version,
    /// active is the launch order, name gives a module's name when it is known.
    /// </summary>
    public static IReadOnlyList<RotVersionProblem> HostProblems(IReadOnlyList<SharedModEntry> host, IReadOnlyDictionary<string, string> installed,
        IEnumerable<string> active, Func<string, string?>? name = null)
    {
        if (host.Count == 0)
            return [];
        return RotSharedList.VersionProblems(host, installed)
            .Concat(RotSharedList.NotOnHost(host, active, name))
            .Where(problem => !CoopJoinRules.IsIgnored(problem.ModuleId))
            .ToList();
    }

    /// <summary>The line under Connect for the first difference, or null when there is none.</summary>
    public static string? Blocking(IReadOnlyList<RotVersionProblem> problems) =>
        problems.Count == 0 ? null : problems[0].Text + " " + SameVersions;

    // ----- The server's own mods. The Coop server answers a ping without its module list, so a launcher only
    // knows them from the Server Tool's server-config.json on this PC or from the mods Coop named when it
    // turned the last join to that server down. -----

    /// <summary>The mods a server loads that this launch leaves out. The core has checks of its own, and Coop leaves
    /// the official modules and the client add ons out of its join check (CoopJoinRules).</summary>
    public static IReadOnlyList<string> NotLoadedHere(IEnumerable<string> server, IEnumerable<string> launch)
    {
        var here = new HashSet<string>(launch.Select(id => id.Trim()), StringComparer.OrdinalIgnoreCase);
        return server.Select(id => id.Trim())
            .Where(id => id.Length > 0 && !here.Contains(id) && !CoopJoinRules.IsIgnored(id) && !RotLoadOrder.IsCore(id)
                && !id.Equals(RotStack.CoopModuleId, StringComparison.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static readonly System.Text.RegularExpressions.Regex Required = new(
        @"To join the server the module '(?<mod>[^']+)'(?: \((?<id>[^)]+)\))?[^\r\n]*?is required",
        System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.Compiled);

    /// <summary>The module ids Coop names in a game log when the server turns a join down for a mod the player
    /// does not load ("To join the server the module 'Name' (Id) with version 'v1.0' is required"). Only these are
    /// kept for the next Connect. namesOnly gives the names of the lines without an id instead: a display name can't
    /// be matched with a module id, so those are only logged.</summary>
    public static IReadOnlyList<string> RequiredByServer(string? log, bool namesOnly = false) =>
        string.IsNullOrEmpty(log)
            ? []
            : Required.Matches(log)
                .Where(match => match.Groups["id"].Success != namesOnly)
                .Select(match => (namesOnly ? match.Groups["mod"] : match.Groups["id"]).Value.Trim())
                .Where(id => id.Length > 0)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

    /// <summary>True when one of the running BannerlordCoopServer command lines has the Realm of Thrones Server Tool's
    /// data folder (rot-server.json DataRoot) as its --data-dir. A TAOM, Burning Empires or other server on this PC
    /// is never read as the Realm of Thrones one.</summary>
    public static bool IsRotServer(IEnumerable<string?> commandLines, string? dataRoot)
    {
        static string Folder(string path) => path.Trim().Replace('/', '\\').TrimEnd('\\');
        if (string.IsNullOrWhiteSpace(dataRoot))
            return false;
        return commandLines.Select(LocalServer.DataDirFromCommandLine).OfType<string>()
            .Any(dir => Folder(dir).Equals(Folder(dataRoot), StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>The line for mods the server loads and this setup does not. before is true when Connect stopped
    /// before the game started, false after Coop turned the join down.</summary>
    public static string ServerModsLine(IReadOnlyList<string> names, bool before) =>
        "The server needs mods this setup does not load: " + string.Join(", ", names) + "."
        + (before ? " The game was not started." : "")
        + " Ask the host for their mod list (Share mod list), click Import mod list on the Mods page, then click Connect again.";
}
