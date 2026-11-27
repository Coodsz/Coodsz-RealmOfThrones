using BannerlordCoopLauncher.Core.Modules;
using BannerlordCoopLauncher.Core.Profile;
using BannerlordCoopLauncher.Core.Saves;
using BannerlordCoopLauncher.Core.Server;
using BannerlordCoopLauncher.Core.Support;
using TaomCoop.Core;

namespace RealmOfThrones.Core;

/// <summary>Where a module of the server list comes from.</summary>
public enum RotModuleSource
{
    /// <summary>Harmony, ButterLib, UIExtenderEx or MCM from the host's install.</summary>
    Library,
    /// <summary>One of Realm of Thrones' four modules from the host's install.</summary>
    RealmOfThrones,
    /// <summary>Realm of Thrones Coop Compat from the tool's payload.</summary>
    Compat,
    /// <summary>An extra of the setup, from where the setup's catalog found it.</summary>
    Extra
}

/// <summary>One module of the server list, with the folder it is copied from (null when this PC does not
/// have it), its version there (null when its SubModule.xml could not be read) and, for an extra, how its code
/// runs on the server.</summary>
public sealed record RotServerModule(string ModuleId, string Label, RotModuleSource Source, string? SourceDirectory, string? Version, ServerCode? Code);

/// <summary>What a complete copy of a module's source was made from: kept outside the module folders and
/// written only after the copy finished, so a copy cut off half way never counts as done.</summary>
public sealed record RotStageStamp(string Version, int Files, long Bytes, long Newest);

/// <summary>Where the core comes from on the host's PC.</summary>
/// <param name="GameRoot">The host's Bannerlord folder (its Modules folder can hold the libraries and Realm of Thrones).</param>
/// <param name="SteamLibraries">Steam libraries to find Workshop items in.</param>
/// <param name="CompatModule">The tool's payload copy of the RealmOfThrones.CoopCompat folder.</param>
public sealed record RotCoreSources(string? GameRoot, IReadOnlyList<string> SteamLibraries, string CompatModule);

/// <summary>What the server needs for a setup: the "modules" list of server-config.json and every module
/// folder that has to be under engine\Modules, in that order.</summary>
public sealed record RotServerPlan(IReadOnlyList<string> Modules, IReadOnlyList<RotServerModule> Stage, TaomSetup Setup);

/// <summary>
/// The Realm of Thrones server path on the Coop nightly dedicated server (NightlyServer). The setup's
/// RotLoadOrder.ServerOrder is the "modules" list. Each module of it is copied into the server's engine\Modules
/// under its module id (ROT_Map's folder on this PC is named ROT-Map): the four library mods and Realm of
/// Thrones' four modules from the host's install, unchanged, our compat module from the tool's payload, and the
/// extras from where the setup's catalog found them. Realm of Thrones' art folders stay behind (the server
/// draws nothing, and the live tested server ran without them), and ROT_Map's campaign map comes from
/// Stage-RotServerMap.ps1 (ApplyMap), because the server cannot read the full map. An extra with client code
/// gets the rule the TAOM server uses: that code is switched off in the server copy. NavalDLC is never listed
/// and never staged here; the server loads its own.
/// </summary>
public static class RotServerPath
{
    /// <summary>Realm of Thrones' art folders, left out of the server copies (as on the live tested server).</summary>
    public static readonly IReadOnlyList<string> ArtFolders = ["AssetPackages", "EmAssetPackages", "ModuleSounds", "Shaders", "music"];

    // The campaign map folder of ROT_Map. The server copy gets the staged one (ApplyMap) instead.
    private static readonly string MapScene = Path.Combine("SceneObj", "Main_map");

    public static string ModulesRoot(string serverRoot) => ServerModuleInstall.ModulesRoot(serverRoot);

    /// <summary>Everything the server loads for this setup, in order: what it always loads first, the
    /// "modules" list and DedicatedServer.Windows. Realm of Thrones always runs with War Sails on.</summary>
    public static IReadOnlyList<string> FullServerOrder(TaomSetup setup) =>
        NightlyServer.FullOrder(RotStack.CoopModuleId, RotLoadOrder.ServerOrder(setup), warSails: true);

    // ----- Finding the modules -----

    /// <summary>The folder of a module on the host's PC, or null. A library is looked for in its Workshop item
    /// first; Realm of Thrones' modules in the game's Modules folder first. Any folder whose SubModule.xml has
    /// this id counts, whatever its folder name.</summary>
    public static string? FindModule(RotCoreSources sources, string moduleId)
    {
        var library = RotStack.Libraries.FirstOrDefault(mod => mod.ModuleId.Equals(moduleId, StringComparison.OrdinalIgnoreCase));
        var workshop = sources.SteamLibraries.Select(root => Path.Combine(root, "steamapps", "workshop", "content", "261550")).ToList();
        var modules = string.IsNullOrWhiteSpace(sources.GameRoot) ? null : Path.Combine(sources.GameRoot, "Modules");
        IEnumerable<string> Candidates()
        {
            if (library != null)
            {
                foreach (var root in workshop)
                    yield return Path.Combine(root, library.WorkshopId);
            }

            if (modules != null)
            {
                yield return Path.Combine(modules, moduleId);
                if (moduleId.Equals(RotStack.MapModuleId, StringComparison.OrdinalIgnoreCase))
                    yield return Path.Combine(modules, "ROT-Map");
            }
        }

        // A folder at its expected place whose SubModule.xml cannot be read counts too, so Problems can say so.
        var direct = Candidates().FirstOrDefault(folder => (IdOf(folder) is { } id && id.Equals(moduleId, StringComparison.OrdinalIgnoreCase))
            || (File.Exists(Path.Combine(folder, "SubModule.xml")) && !CanRead(folder)));
        if (direct != null)
            return direct;
        // A folder named otherwise, in the game's Modules folder or in any Workshop item.
        var roots = (modules == null ? workshop : workshop.Prepend(modules)).Where(Directory.Exists);
        return roots.SelectMany(Children).SelectMany(folder => Children(folder).Prepend(folder))
            .FirstOrDefault(folder => IdOf(folder) is { } id && id.Equals(moduleId, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>What the server needs for this setup. onThisPc are the mods the setup's catalog found.</summary>
    public static RotServerPlan Plan(TaomSetup setup, RotCoreSources sources, IReadOnlyList<FoundMod> onThisPc)
    {
        var order = RotLoadOrder.ServerOrder(setup);
        var labels = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var extra in setup.Extras)
            labels.TryAdd(extra.ModuleId.Trim(), extra.Label);
        var stage = new List<RotServerModule>();
        foreach (var id in order)
        {
            if (id.Equals(RotStack.CompatModuleId, StringComparison.OrdinalIgnoreCase))
            {
                var compat = Directory.Exists(sources.CompatModule) ? sources.CompatModule : null;
                stage.Add(new RotServerModule(id, RotStack.CompatName, RotModuleSource.Compat, compat, VersionOf(compat), null));
            }
            else if (RotStack.IsLibrary(id) || RotStack.IsRotModule(id))
            {
                var folder = FindModule(sources, id);
                var label = RotStack.Libraries.FirstOrDefault(mod => mod.ModuleId.Equals(id, StringComparison.OrdinalIgnoreCase))?.Name ?? id;
                stage.Add(new RotServerModule(id, label, RotStack.IsLibrary(id) ? RotModuleSource.Library : RotModuleSource.RealmOfThrones, folder, VersionOf(folder), null));
            }
            else
            {
                var found = onThisPc.FirstOrDefault(mod => mod.ModuleId.Equals(id, StringComparison.OrdinalIgnoreCase) && Directory.Exists(mod.Directory));
                ServerCode? code = null;
                if (found != null)
                {
                    try
                    {
                        code = ServerModuleInstall.Classify(found.Directory);
                    }
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                    {
                        code = ServerCode.ClientCodeOnly;
                    }
                }

                stage.Add(new RotServerModule(id, labels.GetValueOrDefault(id) ?? found?.Name ?? id, RotModuleSource.Extra, found?.Directory, VersionOf(found?.Directory), code));
            }
        }

        return new RotServerPlan(order, stage, setup);
    }

    // ----- Checks before Start server -----

    /// <summary>Everything that stops Start server, one plain line each: a module of the list that is not on
    /// this PC or whose SubModule.xml cannot be read, a Realm of Thrones module that is not the version the
    /// server is made for (a missing leading v does not count), and an MCM older than the server loads
    /// (RotStack.McmServerMinimum; an old MCM crashes the server). Other library versions are the host's own. carried: the
    /// tool carries the RoT build's server (RotServerBuild), so Prepare server can put it in.</summary>
    public static IReadOnlyList<string> Problems(RotServerPlan plan, string? serverRoot = null, bool carried = false)
    {
        var problems = new List<string>();
        // The Coop build first: with another one no player can join, whatever else is right.
        if (serverRoot != null && (carried ? ServerBuildProblem(serverRoot) : CoopBuildProblem(CoopBuildId.FromServer(serverRoot))) is { } build)
            problems.Add(build);
        foreach (var module in plan.Stage)
        {
            if (module.SourceDirectory == null)
            {
                // The first problem is the whole status line, so the two common ones say what to do next.
                problems.Add(module.Source switch
                {
                    RotModuleSource.RealmOfThrones => module.Label + " is not on this PC, so the server cannot load it. Put Realm of Thrones "
                        + RotStack.RotVersion + " into Bannerlord's Modules folder, then click Prepare server.",
                    RotModuleSource.Library => module.Label + " is not on this PC, so the server cannot load it. Subscribe to it on the Steam Workshop, "
                        + "wait for Steam to download it, then click Prepare server.",
                    _ => module.Label + " is not on this PC, so the server cannot load it."
                });
                continue;
            }

            if (module.Version == null)
            {
                problems.Add(ReadProblem(module.Label));
                continue;
            }

            if (module.Source == RotModuleSource.RealmOfThrones && RotStack.ContentVersions.TryGetValue(module.ModuleId, out var wanted)
                && !Short(module.Version).Equals(Short(wanted), StringComparison.OrdinalIgnoreCase))
                problems.Add(module.Label + " on this PC is not " + wanted + ", the version the Realm of Thrones server is made for.");
            if (module.ModuleId.Equals(RotStack.Mcm.ModuleId, StringComparison.OrdinalIgnoreCase)
                && (ParseVersion(module.Version) is not { } mcm || mcm < ParseVersion(RotStack.McmServerMinimum)!))
                problems.Add("MCM " + module.Version.Trim() + " on this PC is too old for the server, which needs " + RotStack.McmServerMinimum + " or newer.");
        }

        return problems;
    }

    public const string NotRotBuild = "The server's Coop is not the RoT build this tool carries. Click Prepare server to put it in.";

    /// <summary>
    /// The check when the tool carries the RoT build's server: null only when both the ProductVersion of Common.dll in
    /// engine\Modules\Coop\bin\Win64_Shipping_Server and the "Paired Coop" line of release-info.txt are the build of
    /// RotStack.CoopBuild, otherwise NotRotBuild. The server checks its Coop against the pins of its own release, so a
    /// server whose two halves differ cannot start either. buildOf reads a Common.dll's ProductVersion (tests pass their own).
    /// </summary>
    public static string? ServerBuildProblem(string serverRoot, Func<string, string?>? buildOf = null)
    {
        buildOf ??= dll =>
        {
            try
            {
                return File.Exists(dll) ? System.Diagnostics.FileVersionInfo.GetVersionInfo(dll).ProductVersion?.Trim() : null;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or FileNotFoundException)
            {
                return null;
            }
        };
        var module = buildOf(Path.Combine(ModulesRoot(serverRoot), "Coop", ServerBin, CoopBuildId.AssemblyName));
        string? paired;
        try
        {
            var info = Path.Combine(serverRoot, "release-info.txt");
            paired = File.Exists(info) ? CoopBuildId.FromReleaseInfo(File.ReadAllText(info)) : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            paired = null;
        }

        return CoopBuildId.Same(module, RotStack.CoopBuild.Build) && CoopBuildId.Same(paired, RotStack.CoopBuild.Build) ? null : NotRotBuild;
    }

    /// <summary>The Coop server's build (CoopBuildId.FromServer, null when it cannot be read) against the one build
    /// Realm of Thrones runs on (RotStack.CoopBuild.Build), or null when they are the same. The lines of a tool built
    /// without the RoT build's server (ServerBuildProblem when it carries it).</summary>
    public static string? CoopBuildProblem(string? serverBuild)
    {
        if (serverBuild == null)
            return "The tool could not read the Coop build of this Coop server (engine\\Modules\\Coop or release-info.txt). Realm of Thrones in Coop runs on "
                + CoopBuildId.Short(RotStack.CoopBuild.Build) + ". Get the Coop server of that build, then click Choose Coop server and pick it.";
        if (CoopBuildId.Same(serverBuild, RotStack.CoopBuild.Build))
            return null;
        return "The Coop server is build " + CoopBuildId.Short(serverBuild) + ", but Realm of Thrones in Coop runs on " + CoopBuildId.Short(RotStack.CoopBuild.Build)
            + ". Players can only join the very same build. Get the Coop server of build " + CoopBuildId.Short(RotStack.CoopBuild.Build)
            + ", then click Choose Coop server and pick it.";
    }

    private static string ReadProblem(string label) =>
        "The tool could not read the version of " + label + " on this PC. Close anything that has its SubModule.xml open and try again.";

    internal static string Short(string version) => version.Trim().TrimStart('v', 'V');

    // v5.12.3 and v5.12.3.0 are the same version; null when it cannot be read. The client checks (RotJoinCheck) use it too.
    internal static Version? ParseVersion(string text)
    {
        var digits = new string(Short(text).TakeWhile(ch => char.IsDigit(ch) || ch == '.').ToArray()).Trim('.');
        if (!digits.Contains('.'))
            digits += ".0";
        return System.Version.TryParse(digits, out var version)
            ? new Version(version.Major, version.Minor, Math.Max(version.Build, 0), Math.Max(version.Revision, 0))
            : null;
    }

    /// <summary>Null when the save remembers the same mods as this setup's server (or remembers none);
    /// otherwise which mods differ. A world belongs to the setup it was made with.</summary>
    public static string? SaveProblem(string savePath, TaomSetup setup)
    {
        var info = CampaignSaveInfoReader.Read(savePath);
        return info == null ? null : CoopJoinRules.SaveProblem(info.Modules, FullServerOrder(setup));
    }

    /// <summary>True when the save's header can be read and it was not made with Realm of Thrones (no ROT-Core in
    /// its module list). A save whose header cannot be read is not called foreign; the compat module refuses one
    /// made without Realm of Thrones when it loads.</summary>
    public static bool IsForeignSave(string savePath) =>
        CampaignSaveInfoReader.Read(savePath) is { } info && info.Modules.Count > 0
        && !info.Modules.Any(id => id.Trim().Equals(RotStack.CoreModuleId, StringComparison.OrdinalIgnoreCase));

    /// <summary>Whether this setup only adds mods to the save (ask), leaves one out (warn), or matches. A
    /// save that cannot be read starts as it is.</summary>
    public static SaveModChange SaveChange(string savePath, TaomSetup setup)
    {
        var info = CampaignSaveInfoReader.Read(savePath);
        return SaveModChange.Check(info?.Modules ?? Array.Empty<string>(), FullServerOrder(setup));
    }

    // ----- Staging -----

    /// <summary>
    /// Copies the plan's modules into the server's engine\Modules, each under its module id. A module is up to
    /// date only when its stage record (engine\Modules\.coodsz-staged\&lt;id&gt;.json, written after a complete
    /// copy) matches its source: the version, the number of files, their size and the newest change. Otherwise
    /// the server copy is removed (RemoveStaged, guarded) and copied again, SubModule.xml last, so a copy cut off
    /// half way and the files of an older version never stay. A library or a Realm of Thrones module is copied
    /// unchanged (Realm of Thrones without its art folders and without ROT_Map's campaign map, which ApplyMap
    /// puts in); ROT-Core never keeps a bin\Win64_Shipping_Server folder. An extra goes through
    /// ServerModuleInstall.Install, and client code without a server build is switched off in its server copy.
    /// A module this PC does not have is skipped (Problems names it), and so is one whose SubModule.xml cannot
    /// be read. Returns one plain line per module.
    /// </summary>
    public static IReadOnlyList<string> Stage(string serverRoot, RotServerPlan plan)
    {
        var lines = new List<string>();
        var modulesRoot = ModulesRoot(serverRoot);
        foreach (var module in plan.Stage)
        {
            if (module.SourceDirectory == null)
                continue;
            if (!CanRead(module.SourceDirectory))
            {
                lines.Add(ReadProblem(module.Label));
                continue;
            }

            var destination = Path.Combine(modulesRoot, module.ModuleId);
            var skip = SkipFor(module);
            var stamp = SourceStamp(module.SourceDirectory, skip);
            var copy = !Directory.Exists(destination) || ReadStamp(serverRoot, module.ModuleId) != stamp;
            if (copy)
            {
                var record = StampPath(serverRoot, module.ModuleId);
                if (File.Exists(record))
                    File.Delete(record);
                RemoveStaged(serverRoot, plan, module.ModuleId);
                if (module.Source == RotModuleSource.Extra)
                    ServerModuleInstall.Install(module.SourceDirectory, serverRoot, module.ModuleId);
                else
                    CopyUnchanged(module.SourceDirectory, destination, skip);
                Directory.CreateDirectory(Path.GetDirectoryName(record)!);
                File.WriteAllText(record, System.Text.Json.JsonSerializer.Serialize(stamp));
            }

            if (module.ModuleId.Equals(RotStack.CoreModuleId, StringComparison.OrdinalIgnoreCase))
                RemoveStaged(serverRoot, plan, module.ModuleId, ServerBin);
            var line = copy ? "Copied " + module.Label + " to the server." : module.Label + " is already on the server.";
            if (module.Source == RotModuleSource.Extra)
            {
                TurnOffClientCode(destination, module.ModuleId);
                var unblocked = ModUnblock.UnblockDlls(destination);
                if (unblocked > 0)
                    lines.Add(ModUnblock.Line(module.Label, unblocked));
                line += " " + TaomServerPath.NoteFor(module.Code ?? ServerCode.ClientCodeOnly);
            }

            lines.Add(line);
        }

        return lines;
    }

    // ----- Removing a server copy -----

    /// <summary>The folder the stage records live in, outside every module folder.</summary>
    public static string StampFolder(string serverRoot) => Path.Combine(ModulesRoot(serverRoot), ".coodsz-staged");

    private static string StampPath(string serverRoot, string moduleId) => Path.Combine(StampFolder(serverRoot), moduleId + ".json");

    private static readonly string ServerBin = Path.Combine("bin", "Win64_Shipping_Server");

    /// <summary>
    /// Removes the server copy of a module (or one folder inside it, inside), and does nothing when it is not
    /// there. It refuses, before touching the disk, an id that is not in this plan's stage list, a game module,
    /// a Coop module, NavalDLC or a DedicatedServer.* module, a path that does not come out as exactly
    /// &lt;server&gt;\engine\Modules\&lt;id&gt;[\inside] after it is made whole, and a folder that is a link or a junction.
    /// </summary>
    public static void RemoveStaged(string serverRoot, RotServerPlan plan, string moduleId, string? inside = null)
    {
        var id = moduleId ?? "";
        var modulesRoot = Path.GetFullPath(ModulesRoot(serverRoot)).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var expected = modulesRoot + Path.DirectorySeparatorChar + id + (inside == null ? "" : Path.DirectorySeparatorChar + inside);
        var refused = new InvalidOperationException("The tool did not remove " + expected + ", because it is not a module folder this setup puts on the server.");
        if (id.Length == 0 || id != id.Trim() || id is "." or ".." || id.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0
            || !plan.Stage.Any(module => module.ModuleId.Equals(id, StringComparison.OrdinalIgnoreCase))
            || RotLoadOrder.OfficialModules.Contains(id) || id.Equals(RotStack.CoopModuleId, StringComparison.OrdinalIgnoreCase)
            || RotStack.OtherCoopModuleIds.Contains(id, StringComparer.OrdinalIgnoreCase)
            || id.StartsWith("DedicatedServer.", StringComparison.OrdinalIgnoreCase))
            throw refused;
        var module = Path.GetFullPath(Path.Combine(modulesRoot, id));
        var target = inside == null ? module : Path.GetFullPath(Path.Combine(module, inside));
        if (!target.Equals(expected, StringComparison.OrdinalIgnoreCase) || !module.Equals(modulesRoot + Path.DirectorySeparatorChar + id, StringComparison.OrdinalIgnoreCase))
            throw refused;
        if (File.Exists(target))
            throw refused;
        if (!Directory.Exists(target))
            return;
        if (IsLink(module) || IsLink(target))
            throw refused;
        Directory.Delete(target, recursive: true);
    }

    private static bool IsLink(string folder) =>
        Directory.Exists(folder) && (File.GetAttributes(folder) & FileAttributes.ReparsePoint) != 0;

    // What staging leaves out of a module's source: Realm of Thrones' art folders and ROT_Map's campaign map,
    // and ROT-Core's server bin folder.
    private static Func<string, bool> SkipFor(RotServerModule module)
    {
        if (module.Source != RotModuleSource.RealmOfThrones)
            return _ => false;
        var map = module.ModuleId.Equals(RotStack.MapModuleId, StringComparison.OrdinalIgnoreCase);
        var core = module.ModuleId.Equals(RotStack.CoreModuleId, StringComparison.OrdinalIgnoreCase);
        return relative => IsArt(relative) || (map && Under(relative, MapScene)) || (core && Under(relative, ServerBin));
    }

    private static RotStageStamp SourceStamp(string source, Func<string, bool> skip)
    {
        var from = Path.GetFullPath(source);
        var files = 0;
        long bytes = 0;
        long newest = 0;
        foreach (var file in Directory.EnumerateFiles(from, "*", SearchOption.AllDirectories))
        {
            if (skip(Path.GetRelativePath(from, file)))
                continue;
            var info = new FileInfo(file);
            files++;
            bytes += info.Length;
            newest = Math.Max(newest, info.LastWriteTimeUtc.Ticks);
        }

        return new RotStageStamp(VersionOf(source) ?? "", files, bytes, newest);
    }

    private static RotStageStamp? ReadStamp(string serverRoot, string moduleId)
    {
        try
        {
            var path = StampPath(serverRoot, moduleId);
            return File.Exists(path) ? System.Text.Json.JsonSerializer.Deserialize<RotStageStamp>(File.ReadAllText(path)) : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Text.Json.JsonException)
        {
            return null;
        }
    }

    // True when the folder has no SubModule.xml or one that can be read.
    private static bool CanRead(string folder)
    {
        var xml = Path.Combine(folder, "SubModule.xml");
        if (!File.Exists(xml))
            return true;
        try
        {
            using var stream = File.OpenRead(xml);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    // ----- The server map -----

    public const string MapScriptName = "Stage-RotServerMap.ps1";
    public const string MapDataFile = "rot_map.txt";

    /// <summary>The tool's folder for the staged map of this ROT_Map version: &lt;toolData&gt;\ServerMap\&lt;version&gt;.
    /// Stage-RotServerMap.ps1 needs it new or empty, so a folder left half written is cleared before the script
    /// runs again. A version that is empty, "." or "..", ends in a dot or a space, has a path character, or would
    /// lead outside ServerMap throws instead of giving a path.</summary>
    public static string MapStageFolder(string toolData, string mapVersion)
    {
        var version = mapVersion ?? "";
        var refused = new InvalidOperationException("The map version " + version + " cannot be used as a folder name.");
        if (version.Trim().Length == 0 || version is "." or ".." || version.EndsWith('.') || version.EndsWith(' ') || version.StartsWith(' ')
            || version.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || version.Contains('/') || version.Contains('\\'))
            throw refused;
        var root = Path.GetFullPath(Path.Combine(toolData, "ServerMap")).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var folder = Path.GetFullPath(Path.Combine(root, version));
        if (!folder.Equals(root + Path.DirectorySeparatorChar + version, StringComparison.OrdinalIgnoreCase))
            throw refused;
        return folder;
    }

    /// <summary>The PowerShell start of the compat module's Stage-RotServerMap.ps1, which reads the host's ROT_Map
    /// (rotMap) and writes the server's map into outDir. Nothing is written into rotMap.</summary>
    public static NightlyServerStart MapStageStart(string compatModule, string rotMap, string outDir)
    {
        var script = Path.Combine(compatModule, MapScriptName);
        var powerShell = Path.Combine(Environment.SystemDirectory, "WindowsPowerShell", "v1.0", "powershell.exe");
        return new NightlyServerStart(powerShell, compatModule,
            [.. "-NoLogo -NoProfile -ExecutionPolicy Bypass -File".Split(' '), script, "-RotMap", rotMap, "-OutDir", outDir],
            new Dictionary<string, string>(), NightlyServer.InputEncoding, []);
    }

    private static string StagedScene(string stageFolder) => Path.Combine(stageFolder, RotStack.MapModuleId, MapScene);

    private static string StagedMapData(string stageFolder) => Path.Combine(stageFolder, RotStack.CompatModuleId, "ModuleData", MapDataFile);

    /// <summary>Null when the stage folder has a whole server map; otherwise the line to show.</summary>
    public static string? MapProblem(string stageFolder) =>
        File.Exists(Path.Combine(StagedScene(stageFolder), "scene.xscene")) && File.Exists(StagedMapData(stageFolder))
            ? null
            : "The Realm of Thrones map for the server is not made yet.";

    /// <summary>Why the map cannot be made, after MapProblem, when the plan has no source for it.</summary>
    public const string MapSourceMissing = "The tool makes it from Realm of Thrones (ROT_Map) and Realm of Thrones Coop Compat, and one of them was not found.";

    /// <summary>The one line about the server's map once it is in the server: made now, made before, or
    /// already the same in the server.</summary>
    public static string MapLine(bool madeNow, bool copied) =>
        !copied ? "The Realm of Thrones map for the server is already in the server."
        : madeNow ? "Made the Realm of Thrones map for the server and put it into the server."
        : "Put the Realm of Thrones map for the server into the server. It was made before, so the tool used that copy.";

    /// <summary>Puts the staged map into the server copies: ROT_Map's campaign map folder (cleared first, so
    /// nothing of an older map stays) and the compat module's rot_map.txt (the map's borders and size). Run
    /// after Stage on every start. False when the server already has exactly this map (the same files with
    /// the same size and time, and the same rot_map.txt), and then nothing is copied.</summary>
    public static bool ApplyMap(string serverRoot, RotServerPlan plan, string stageFolder)
    {
        if (MapProblem(stageFolder) is { } problem)
            throw new InvalidOperationException(problem);
        var modulesRoot = ModulesRoot(serverRoot);
        var scene = Path.Combine(modulesRoot, RotStack.MapModuleId, MapScene);
        var data = Path.Combine(modulesRoot, RotStack.CompatModuleId, "ModuleData", MapDataFile);
        if (SameFiles(StagedScene(stageFolder), scene) && File.Exists(data)
            && File.ReadAllBytes(data).AsSpan().SequenceEqual(File.ReadAllBytes(StagedMapData(stageFolder))))
            return false;
        RemoveStaged(serverRoot, plan, RotStack.MapModuleId, MapScene);
        CopyUnchanged(StagedScene(stageFolder), scene, _ => false);
        Directory.CreateDirectory(Path.GetDirectoryName(data)!);
        File.Copy(StagedMapData(stageFolder), data, overwrite: true);
        return true;
    }

    // True when both folders hold the same files by name, size and time (a copy keeps the time).
    private static bool SameFiles(string source, string copy)
    {
        if (!Directory.Exists(copy))
            return false;
        static Dictionary<string, (long, DateTime)> Files(string folder) =>
            Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories).ToDictionary(
                file => Path.GetRelativePath(folder, file), file => (new FileInfo(file).Length, File.GetLastWriteTimeUtc(file)), StringComparer.OrdinalIgnoreCase);
        var from = Files(source);
        var to = Files(copy);
        return from.Count == to.Count && from.All(pair => to.TryGetValue(pair.Key, out var other) && other == pair.Value);
    }

    // ----- Helpers -----

    // The module's version, "" when it has none, null when its SubModule.xml cannot be read.
    internal static string? VersionOf(string? moduleDirectory)
    {
        if (moduleDirectory == null)
            return "";
        var xml = Path.Combine(moduleDirectory, "SubModule.xml");
        try
        {
            return File.Exists(xml) ? ModuleIdentityReader.Read(xml).Version : "";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static string? IdOf(string folder)
    {
        var xml = Path.Combine(folder, "SubModule.xml");
        try
        {
            return File.Exists(xml) ? ModuleIdentityReader.Read(xml).Id : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Xml.XmlException)
        {
            return null;
        }
    }

    private static IEnumerable<string> Children(string folder)
    {
        try
        {
            return Directory.EnumerateDirectories(folder).ToList();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return Array.Empty<string>();
        }
    }

    private static bool IsArt(string relative)
    {
        var first = relative.Split(Path.DirectorySeparatorChar, 2)[0];
        return ArtFolders.Contains(first, StringComparer.OrdinalIgnoreCase);
    }

    private static bool Under(string relative, string folder) =>
        relative.Equals(folder, StringComparison.OrdinalIgnoreCase)
        || relative.StartsWith(folder + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);

    // Copies every file byte for byte, the module's own SubModule.xml last, so a copy cut off half way has none.
    private static void CopyUnchanged(string source, string destination, Func<string, bool> skip)
    {
        var from = Path.GetFullPath(source);
        string? subModule = null;
        foreach (var file in Directory.EnumerateFiles(from, "*", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(from, file);
            if (skip(relative))
                continue;
            if (relative.Equals("SubModule.xml", StringComparison.OrdinalIgnoreCase))
            {
                subModule = file;
                continue;
            }

            var target = Path.Combine(destination, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(file, target, overwrite: true);
        }

        Directory.CreateDirectory(destination);
        if (subModule != null)
            File.Copy(subModule, Path.Combine(destination, "SubModule.xml"), overwrite: true);
    }

    // Switches client code off in an extra's server copy, the rule TaomServerPath uses.
    private static void TurnOffClientCode(string moduleDirectory, string moduleId)
    {
        var xmlPath = Path.Combine(moduleDirectory, "SubModule.xml");
        if (!File.Exists(xmlPath))
            return;
        var raw = File.ReadAllBytes(xmlPath);
        var bom = raw.Length >= 3 && raw[0] == 0xEF && raw[1] == 0xBB && raw[2] == 0xBF;
        var xml = new System.Text.UTF8Encoding(false).GetString(raw, bom ? 3 : 0, raw.Length - (bom ? 3 : 0));
        var dlls = ServerModuleInstall.ServerDlls(moduleDirectory);
        if (ServerModuleInstall.Classify(xml, dlls, moduleId) != ServerCode.ClientCodeOnly)
            return;
        var patched = ServerModuleInstall.TurnOffClientCode(xml, dlls, moduleId);
        if (patched != xml)
            File.WriteAllText(xmlPath, patched, new System.Text.UTF8Encoding(bom));
    }
}

public enum RotWorldState
{
    Waiting,
    /// <summary>The world is up: the tool may send "save".</summary>
    Ready,
    /// <summary>A line says the world must not be kept: stop and show it.</summary>
    Failed
}

/// <summary>
/// Reads the server's console lines until the world is ready to be saved. A new world is ready when the
/// server says SERVING and our module says "World check passed"; a loaded one when the server says SERVING
/// and our module says "Host: the loaded world is in the year". Only a failure line of our module
/// ("[RealmOfThrones.CoopCompat]", see FailureMarks) means stop and show it. Its other "!!!" lines and any
/// other mod's lines do not stop it. Once ready or failed, later lines change nothing.
/// </summary>
public sealed class RotWorldWatch(bool newWorld)
{
    public bool NewWorld { get; } = newWorld;
    public RotWorldState State { get; private set; } = RotWorldState.Waiting;
    public string? FailedLine { get; private set; }
    private bool _serving;
    private bool _world;

    // The module's lines that say a world must not be kept, as compat\RealmOfThronesCoopCompat writes them.
    private static readonly string[] FailureMarks =
    [
        // FreshCampaignRules.WorldCheckLine and RunWorldCheck: the new world's check failed or could not run.
        "!!! World check FAILED",
        // FreshCampaign, FreshCampaignCounts, FreshCampaignRules.Describe and DecideLoad, RotHostRules.WorldRefusalLine:
        // a save or new world refused, a new campaign that cannot start, a save loaded unchecked or below full.
        // A normal Realm of Thrones world or blank copy gets a "Fresh campaign:" line without "!!!".
        "!!! Fresh campaign:",
        // RotHost.LogMainHero from world ready on: no main hero (before that its line has no "!!!").
        "Host: !!! at world ready Hero.MainHero is",
        "Host: !!! at the first daily tick Hero.MainHero is",
        // RotHostRules.FailureLine: behaviors, models, guards or story hooks failed.
        "THE HOST IS NOT COMPLETE",
        // RotHostRules.WorldYearLine and RotHost's loaded world without Realm of Thrones' code.
        "DO NOT KEEP ITS SAVE"
    ];

    public RotWorldState Feed(string? line)
    {
        if (State != RotWorldState.Waiting || string.IsNullOrWhiteSpace(line))
            return State;
        if (line.Contains("[" + RotStack.CompatModuleId + "]", StringComparison.Ordinal)
            && FailureMarks.Any(mark => line.Contains(mark, StringComparison.Ordinal)))
        {
            FailedLine = line.Trim();
            return State = RotWorldState.Failed;
        }

        if (IsServing(line))
            _serving = true;
        if (line.Contains(NewWorld ? "World check passed" : "Host: the loaded world is in the year", StringComparison.Ordinal))
            _world = true;
        if (_serving && _world)
            State = RotWorldState.Ready;
        return State;
    }

    // The server's "[DedicatedServer] SERVING - coop server up". Its start help prints the bare words
    // "[DedicatedServer] SERVING" too, with nothing after them, so that one does not count.
    private static bool IsServing(string line)
    {
        const string marker = "[DedicatedServer] SERVING";
        var at = line.IndexOf(marker, StringComparison.Ordinal);
        return at >= 0 && line[(at + marker.Length)..].Trim().Length > 0 && !line.Contains("when you see", StringComparison.OrdinalIgnoreCase);
    }
}
