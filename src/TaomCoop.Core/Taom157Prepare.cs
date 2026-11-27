using BannerlordCoopLauncher.Core.Modules;

namespace TaomCoop.Core;

/// <summary>One module Prepare server copies on Bannerlord 1.5.x: its id (also its folder name in both Modules folders) and the
/// game's folder it comes from. SubModuleOnly is a TAOM helper stub, of which only SubModule.xml is copied.</summary>
public sealed record Taom157PrepareModule(string ModuleId, string Source, bool SubModuleOnly);

/// <summary>What a server copy was made from: the version, the number of files, their size and the newest change.</summary>
public sealed record Taom157StageStamp(string Version, int Files, long Bytes, long Newest);

/// <summary>What the folder pick of Prepare server found: the server folder to save, or the line why it can't be used.</summary>
public sealed record Taom157ServerChoice(string? Server, string? Problem);

/// <summary>
/// Prepare server on Bannerlord 1.5.x: copies the host's own TAOM, whatever its version, from the game's Modules folder into
/// the Coop server for Bannerlord 1.5.7 (Coop v0.1.8), so the server always has the TAOM the players have. TAOM.Dependencies,
/// TAOM's helper stubs, LOTRLOME_Armory, TAOM_Map, TAOM, and Dual Wield and TAOM.CoopCompat when the game has them. The art
/// folders the server never loads stay behind. Each module gets a stamp of its source after a whole copy, so a second run
/// copies only what changed. Start server does the rest (Taom157ServerSetup, Taom157ServerMap). The RoT stager
/// (RotServerPath.Stage) is the pattern, written again here because TaomCoop.Core can't use RealmOfThrones.Core.
/// </summary>
public static class Taom157Prepare
{
    /// <summary>Top folders of a module the server never loads (it reads DsAssetPackages, never AssetPackages), compared
    /// without letter case. In lower case so tools\ExtractUiText doesn't take a lone capitalized word for screen text.</summary>
    public static readonly IReadOnlyList<string> ArtFolders = ["assetpackages", "emassetpackages", "modulesounds", "shaders", "music", "sceneeditdata", "videos"];

    /// <summary>Files TAOM.Dependencies writes on the player's PC, which no server needs.</summary>
    public static readonly IReadOnlyList<string> PlayerFiles = ["diag.log", "last-good-modlist.txt", "coop-modules.txt"];

    private const string DependenciesId = "TAOM.Dependencies";
    private const string SubModuleFile = "SubModule.xml";

    /// <summary>Kept free on top of the copy, as on the Realm of Thrones server.</summary>
    public const long SpaceMargin = 1L << 30;

    public const string PickTitle = "Pick the folder you unpacked the Coop server for Bannerlord 1.5.7 into";

    public const string Done =
        "TAOM is on the server now. Next pick your campaign on the Saves page and click Name server copy, then press Start server.";

    public static string NotA157Server(string folder) =>
        folder + " is not the Coop server for Bannerlord 1.5.7. Pick the folder you unpacked the Coop v0.1.8 server download into. It has coop-pin.json and BannerlordCoopServer.exe in it.";

    public static string SavedFolderNotServer(string? folder) =>
        "The server folder " + folder + " is not a whole Coop server for Bannerlord 1.5.7, so Prepare server asks for the folder you unpacked the Coop server download into.";

    public static string InWorkshop(string folder) =>
        folder + " is in Steam's Workshop folder, and Steam may change it at any time. Unpack the Coop server download into a folder of its own, then pick that one.";

    public static string IsCoop016Server(string folder) =>
        folder + " is the Coop 0.1.6 server you chose for Bannerlord 1.4.8. Pick the folder of the Coop server for Bannerlord 1.5.7 instead.";

    public static string OverlapsHostFolder(string server, string folder) =>
        "The Coop server folder " + server + " overlaps " + folder + ". Unpack the Coop v0.1.8 dedicated server in a separate folder outside your game and server data. Pick the folder with BannerlordCoopServer.exe and coop-pin.json.";

    public static string ServerRunningThere(string folder) =>
        "A server is running from " + folder + ". Stop it, then press Prepare server again.";

    public static string Chosen(string server) => "The server folder for Bannerlord 1.5.7 is now " + server + ".";

    public static string NotSaved(string reason) => "The server folder could not be saved in the settings. " + reason;

    public static string NoSpace(string drive, long gigabytes) =>
        "There is not enough free space on drive " + drive + " to copy TAOM into the server. It needs about " + gigabytes + " GB free there. Free up some space, then press Prepare server again.";

    public static string SourceLink(string id) =>
        id + " in the game's Modules folder is a link to another folder, so Prepare server copied nothing. Put the real folder there, then press Prepare server again.";

    public static string ServerLink(string id) =>
        id + " in the server's engine\\Modules folder is a link to another folder, so Prepare server copied nothing. Remove that link, then press Prepare server again.";

    public const string StampFolderRefused = "Prepare server stopped because the server's .coodsz-staged folder is a link. Use a real folder there.";
    public const string ModulesRootRefused =
        "The server's engine\\Modules folder is a link, sits inside one, or overlaps the game's Modules folder, so Prepare server copied nothing. Unpack the Coop server download into a folder of its own, then press Prepare server again and pick that folder.";

    public static string TookOff(string id) =>
        "Took " + id + " off the server, because Prepare server no longer copies it from your game's Modules folder.";

    public const string FirstRunTip =
        "Copies your TAOM into the Coop server for Bannerlord 1.5.7. The first time it asks for the folder you unpacked the server into.";

    public const string NotPrepared =
        "TAOM is not on this server yet. Click Prepare server on the Saves page first, then press Start server.";

    /// <summary>Start server's line for a Coop server for Bannerlord 1.5.7 that Prepare server has not filled yet (TAOM.Dependencies or
    /// TAOM missing from its engine\Modules folder), or null. It comes before Taom157ServerSetup.Problem, whose line for that
    /// case doesn't say what to click.</summary>
    public static string? NotPreparedLine(string serverRoot) =>
        new[] { DependenciesId, TaomModules157.TaomModuleId }.Any(id => !File.Exists(Path.Combine(ModulesRoot(serverRoot), id, SubModuleFile)))
            ? NotPrepared
            : null;

    public static string Stopped(string reason) =>
        "Prepare server stopped before TAOM was all on the server. " + reason + " Close programs that may have the server or game folder open, like an antivirus scan, then press Prepare server again.";

    /// <summary>Every id this class may put on a server, and so the only ids <see cref="RemoveStaged"/> removes.</summary>
    public static IReadOnlyList<string> ModuleIds { get; } =
        [DependenciesId, .. Taom157Start.HelperFolders, "LOTRLOME_Armory", "TAOM_Map", TaomModules157.TaomModuleId, Taom157Start.DualWieldModuleId, TaomStack.CompatModuleId];

    /// <summary>
    /// What Prepare server copies from gameModules, in the server's load order: the four TAOM modules, each helper folder that
    /// is TAOM's stub (<see cref="IsTaomStub"/>), and Dual Wield and TAOM.CoopCompat when the game has them (Taom157Start.Modules
    /// lists both after TAOM). A real BUTR module with a helper's id
    /// is left out, and Start server's helper folder line says so. A missing TAOM module throws, with TaomModules157's line.
    /// </summary>
    public static IReadOnlyList<Taom157PrepareModule> Plan(string gameModules, string? bundledCompat = null, bool includeDualWield = false)
    {
        var modules = new List<Taom157PrepareModule>();
        void Content(string id)
        {
            var source = Path.Combine(gameModules, id);
            if (!File.Exists(Path.Combine(source, SubModuleFile)))
                throw new InvalidOperationException(id + " is missing. Install TAOM again from your TAOM download.");
            modules.Add(new Taom157PrepareModule(id, source, SubModuleOnly: false));
        }

        Content(DependenciesId);
        foreach (var id in Taom157Start.HelperFolders)
        {
            var source = Path.Combine(gameModules, id);
            if (IsTaomStub(source))
                modules.Add(new Taom157PrepareModule(id, source, SubModuleOnly: true));
        }

        Content("LOTRLOME_Armory");
        Content("TAOM_Map");
        Content(TaomModules157.TaomModuleId);
        // Both the host setup and the selected save can require Dual Wield.
        if (includeDualWield)
            Content(Taom157Start.DualWieldModuleId);
        if (bundledCompat != null)
            modules.Add(new Taom157PrepareModule(TaomStack.CompatModuleId, bundledCompat, SubModuleOnly: false));
        else if (File.Exists(Path.Combine(gameModules, TaomStack.CompatModuleId, SubModuleFile)))
            modules.Add(new Taom157PrepareModule(TaomStack.CompatModuleId, Path.Combine(gameModules, TaomStack.CompatModuleId), SubModuleOnly: false));
        return modules;
    }

    /// <summary>True for TAOM.Dependencies' alias stub: a SubModule.xml that says it is one (its first comment), and no DLL in the folder. A file
    /// that can't be read counts as no stub, so it is left out rather than guessed.</summary>
    public static bool IsTaomStub(string folder)
    {
        try
        {
            var xml = Path.Combine(folder, SubModuleFile);
            return File.Exists(xml) && File.ReadAllText(xml).Contains("TAOM.Dependencies passive alias stub", StringComparison.Ordinal)
                && !Directory.EnumerateFiles(folder, "*.dll", SearchOption.AllDirectories).Any();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    /// <summary>The bytes still to copy: the files the copy takes (after the art filter) of each module whose server copy is
    /// missing or has another stamp.</summary>
    public static long BytesToCopy(string gameModules, string serverRoot, string? bundledCompat = null, bool includeDualWield = false) =>
        Plan(gameModules, bundledCompat, includeDualWield).Select(module => (module, stamp: SourceStamp(module)))
            .Where(item => NeedsCopy(serverRoot, item.module.ModuleId, item.stamp))
            .Sum(item => item.stamp.Bytes);

    /// <summary>The line when the server's drive has less free space than the copy still needs plus <see cref="SpaceMargin"/>,
    /// or null. freeSpace is for the tests; a drive Windows can't read is not checked.</summary>
    public static string? SpaceProblem(string gameModules, string serverRoot, Func<string, long>? freeSpace = null, string? bundledCompat = null, bool includeDualWield = false)
    {
        var bytes = BytesToCopy(gameModules, serverRoot, bundledCompat, includeDualWield);
        if (bytes == 0)
            return null;
        var needed = bytes + SpaceMargin;
        // The drive the files really land on, also when the folder or a parent is a link to another drive.
        var full = Path.GetFullPath(serverRoot);
        try
        {
            full = KitCoopServer.RealPath(full);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
        }

        if ((freeSpace ?? FreeSpace)(full) >= needed)
            return null;
        // C: for C:\TAOM-Coop-Host\Server.
        var drive = (Path.GetPathRoot(full) ?? full).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return NoSpace(drive, (long)Math.Ceiling(needed / (double)(1L << 30)));
    }

    /// <summary>
    /// Copies <see cref="Plan"/> into the server's engine\Modules, each under its id. Every source and server module folder is
    /// checked for links first, so a refused run changes nothing. A module whose server copy has the same stamp is left as it
    /// is. Otherwise its stamp goes, the server copy is removed (RemoveStaged, guarded), the files are copied with SubModule.xml
    /// last, and the new stamp is written, so a copy cut off half way is done again next time. Real copies only: Start server
    /// moves DLLs out of the server copies. A module this class put there before (it has a stamp) that the plan no longer has
    /// is taken off, so Start server doesn't list a module the players don't have. Returns one line per module.
    /// </summary>
    public static IReadOnlyList<string> Stage(string gameModules, string serverRoot, string? bundledCompat = null, bool includeDualWield = false)
    {
        var plan = Plan(gameModules, bundledCompat, includeDualWield);
        var modulesRoot = ModulesRoot(serverRoot);
        // A server engine or engine\Modules folder that leads elsewhere, say into the game's Modules folder, would make the
        // removal below delete the player's own TAOM.
        if (!IsRealFolder(modulesRoot) || KitCoopServer.Overlaps(modulesRoot, gameModules))
            throw new InvalidOperationException(ModulesRootRefused);
        if (!IsRealFolder(StampFolder(serverRoot)))
            throw new InvalidOperationException(StampFolderRefused);
        foreach (var module in plan)
        {
            if (IsLink(module.Source))
                throw new InvalidOperationException(SourceLink(module.ModuleId));
            if (IsLink(Path.Combine(modulesRoot, module.ModuleId)))
                throw new InvalidOperationException(ServerLink(module.ModuleId));
            if (module.ModuleId == TaomStack.CompatModuleId
                && TaomCompat157.IsNewer(Path.Combine(modulesRoot, module.ModuleId), module.Source))
                throw new InvalidOperationException(TaomCompat157.MatchingBuildNeeded);
        }

        var lines = new List<string>();
        foreach (var id in ModuleIds.Where(id => !plan.Any(module => module.ModuleId.Equals(id, StringComparison.OrdinalIgnoreCase))))
        {
            // Only a module with this class's stamp: a folder the host put there himself stays.
            var record = StampPath(serverRoot, id);
            if (!File.Exists(record))
                continue;
            RemoveStaged(serverRoot, id);
            File.Delete(record);
            lines.Add(TookOff(id));
        }

        foreach (var module in plan)
        {
            var stamp = SourceStamp(module);
            if (!NeedsCopy(serverRoot, module.ModuleId, stamp))
            {
                lines.Add(module.ModuleId + " is already on the server.");
                continue;
            }

            var record = StampPath(serverRoot, module.ModuleId);
            if (File.Exists(record))
                File.Delete(record);
            RemoveStaged(serverRoot, module.ModuleId);
            CopyFiles(module.Source, Path.Combine(modulesRoot, module.ModuleId), Skip(module));
            Directory.CreateDirectory(Path.GetDirectoryName(record)!);
            File.WriteAllText(record, System.Text.Json.JsonSerializer.Serialize(stamp));
            lines.Add("Copied " + module.ModuleId + " to the server.");
        }

        return lines;
    }

    /// <summary>
    /// Removes the server copy of a module, and does nothing when it is not there. It refuses, before touching the disk, an id
    /// that is not one of <see cref="ModuleIds"/>, a game module, Coop, a DedicatedServer.* module, a path that does not come out
    /// as exactly &lt;server&gt;\engine\Modules\&lt;id&gt;, a folder that is a link or a junction, and an engine\Modules folder
    /// that is a link or inside one.
    /// </summary>
    public static void RemoveStaged(string serverRoot, string moduleId)
    {
        var id = moduleId ?? "";
        var modulesRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(ModulesRoot(serverRoot)));
        var expected = modulesRoot + Path.DirectorySeparatorChar + id;
        var refused = new InvalidOperationException("The tool did not remove " + expected + ", because it is not a module folder this setup puts on the server.");
        if (id.Length == 0 || id != id.Trim() || id is "." or ".." || id.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0
            || !ModuleIds.Contains(id, StringComparer.OrdinalIgnoreCase)
            || CoopJoinRules.OfficialModules.Contains(id) || id.Equals("SandBox", StringComparison.OrdinalIgnoreCase)
            || id.Equals(TaomStack.CoopModuleId, StringComparison.OrdinalIgnoreCase) || id.Equals(TaomStack.LegacyCoopModuleId, StringComparison.OrdinalIgnoreCase)
            || id.StartsWith("DedicatedServer.", StringComparison.OrdinalIgnoreCase))
            throw refused;
        var target = Path.GetFullPath(Path.Combine(modulesRoot, id));
        if (!target.Equals(expected, StringComparison.OrdinalIgnoreCase) || File.Exists(target) || !IsRealFolder(modulesRoot))
            throw refused;
        if (!Directory.Exists(target))
            return;
        if (IsLink(target))
            throw refused;
        Directory.Delete(target, recursive: true);
    }

    /// <summary>
    /// The folder pick of Prepare server: the server folder inside the picked one (KitCoopServer.Resolve), or why it can't be
    /// used (<see cref="Problem"/>). running is for the tests.
    /// </summary>
    public static Taom157ServerChoice Choose(string folder, string? gameRoot, string? dataRoot, string? coop016Server, Func<string, bool>? running = null)
    {
        var server = KitCoopServer.Resolve(folder);
        if (server == null)
            return new Taom157ServerChoice(null, NotA157Server(folder));
        return Problem(server, gameRoot, dataRoot, coop016Server, running) is { } problem
            ? new Taom157ServerChoice(null, problem)
            : new Taom157ServerChoice(server, null);
    }

    /// <summary>
    /// Why a server folder can't be prepared, or null, for a picked folder and the saved one alike. It must be the Coop server
    /// for Bannerlord 1.5.7 (Taom157Start.Pick is Line157), not in Steam's Workshop folder, not the Coop 0.1.6 server chosen for
    /// 1.4.8, not inside the game or Data folder or holding one, with an engine\Modules folder that is no link and not inside
    /// one, and no server may run from it. running is for the tests.
    /// </summary>
    public static string? Problem(string server, string? gameRoot, string? dataRoot, string? coop016Server, Func<string, bool>? running = null)
    {
        if (InWorkshopFolder(server))
            return InWorkshop(server);
        if (!string.IsNullOrWhiteSpace(coop016Server) && KitCoopServer.Overlaps(server, coop016Server))
            return IsCoop016Server(server);
        if (Taom157Start.Pick(server).Path != Taom157Path.Line157)
            return NotA157Server(server);
        foreach (var host in new[] { gameRoot, dataRoot })
        {
            if (KitCoopServer.Overlaps(server, host))
                return OverlapsHostFolder(server, host!);
        }

        if (!IsRealFolder(ModulesRoot(server)))
            return ModulesRootRefused;
        if (!IsRealFolder(StampFolder(server)))
            return StampFolderRefused;
        if ((running ?? DedicatedServerWatch.IsRunning)(server))
            return ServerRunningThere(server);
        return null;
    }

    // As written and where its links lead. A path whose links can't be followed counts as in it: refusing is safe.
    private static bool InWorkshopFolder(string folder)
    {
        static bool Has(string path) =>
            (Path.TrimEndingDirectorySeparator(Path.GetFullPath(path)) + Path.DirectorySeparatorChar)
                .Contains(@"\steamapps\workshop\", StringComparison.OrdinalIgnoreCase);
        try
        {
            return Has(folder) || Has(KitCoopServer.RealPath(folder));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            return true;
        }
    }

    // ----- Helpers -----

    private static string ModulesRoot(string serverRoot) => Path.Combine(serverRoot, "engine", "Modules");

    /// <summary>The folder the stamps live in, outside every module folder (the same one the Realm of Thrones server uses).</summary>
    public static string StampFolder(string serverRoot) => Path.Combine(ModulesRoot(serverRoot), ".coodsz-staged");

    private static string StampPath(string serverRoot, string moduleId) => Path.Combine(StampFolder(serverRoot), moduleId + ".json");

    private static bool NeedsCopy(string serverRoot, string moduleId, Taom157StageStamp stamp)
    {
        if (ReadStamp(serverRoot, moduleId) != stamp)
            return true;

        // The record describes the source. Check the server manifest too in case
        // the server folder was restored or overwritten independently of its record.
        var path = Path.Combine(ModulesRoot(serverRoot), moduleId, SubModuleFile);
        var staged = ModuleIdentityReader.TryRead(path);
        return staged == null
            || !staged.Id.Equals(moduleId, StringComparison.OrdinalIgnoreCase)
            || !staged.Version.Equals(stamp.Version, StringComparison.OrdinalIgnoreCase);
    }

    private static Taom157StageStamp? ReadStamp(string serverRoot, string moduleId)
    {
        try
        {
            var path = StampPath(serverRoot, moduleId);
            return File.Exists(path) ? System.Text.Json.JsonSerializer.Deserialize<Taom157StageStamp>(File.ReadAllText(path)) : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Text.Json.JsonException)
        {
            return null;
        }
    }

    // What the copy leaves out of a module, by its path inside the module: a stub keeps only SubModule.xml; every other module
    // leaves out the art folders, and TAOM.Dependencies its player files too. Dual Wield is copied whole (about 4 MB): its
    // animations are in its AssetPackages, and missing Dual Wield animations stopped a client on the loading screen before.
    private static Func<string, bool> Skip(Taom157PrepareModule module)
    {
        if (module.SubModuleOnly)
            return relative => !relative.Equals(SubModuleFile, StringComparison.OrdinalIgnoreCase);
        if (module.ModuleId.Equals(Taom157Start.DualWieldModuleId, StringComparison.OrdinalIgnoreCase))
            return _ => false;
        var dependencies = module.ModuleId.Equals(DependenciesId, StringComparison.OrdinalIgnoreCase);
        return relative =>
        {
            var parts = relative.Split(Path.DirectorySeparatorChar, 2);
            if (parts.Length == 2 && ArtFolders.Contains(parts[0], StringComparer.OrdinalIgnoreCase))
                return true;
            return dependencies && parts.Length == 1 && PlayerFiles.Contains(parts[0], StringComparer.OrdinalIgnoreCase);
        };
    }

    private static Taom157StageStamp SourceStamp(Taom157PrepareModule module)
    {
        var from = Path.GetFullPath(module.Source);
        var skip = Skip(module);
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

        var xml = Path.Combine(from, SubModuleFile);
        var version = File.Exists(xml) ? ModuleIdentityReader.Read(xml).Version : "";
        return new Taom157StageStamp(version, files, bytes, newest);
    }

    // Copies every file byte for byte, the module's own SubModule.xml last, so a copy cut off half way has none.
    private static void CopyFiles(string source, string destination, Func<string, bool> skip)
    {
        var from = Path.GetFullPath(source);
        string? subModule = null;
        foreach (var file in Directory.EnumerateFiles(from, "*", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(from, file);
            if (skip(relative))
                continue;
            if (relative.Equals(SubModuleFile, StringComparison.OrdinalIgnoreCase))
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
            File.Copy(subModule, Path.Combine(destination, SubModuleFile), overwrite: true);
    }

    // True when the folder, written out in full, is where it really is: neither it nor a parent is a link or a junction.
    private static bool IsRealFolder(string folder)
    {
        try
        {
            var full = Path.TrimEndingDirectorySeparator(Path.GetFullPath(folder));
            return KitCoopServer.RealPath(full).Equals(full, StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            return false;
        }
    }

    private static bool IsLink(string folder) =>
        Directory.Exists(folder) && (File.GetAttributes(folder) & FileAttributes.ReparsePoint) != 0;

    // The bytes free on the drive of path; a path whose drive Windows cannot read (a network share, say) is not checked.
    private static long FreeSpace(string path)
    {
        try
        {
            return new DriveInfo(Path.GetPathRoot(path)!).AvailableFreeSpace;
        }
        catch (Exception ex) when (ex is ArgumentException or IOException or UnauthorizedAccessException)
        {
            return long.MaxValue;
        }
    }
}
