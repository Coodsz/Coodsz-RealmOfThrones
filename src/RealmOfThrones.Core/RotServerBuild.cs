// The War Sails staging here follows the steps of Bannerlord Coop's own dedicated server staging (war-sails-staging.ps1),
// re-expressed in C#. It ships no Coop or game files; it copies them from the host's own Workshop Coop and game.
using System.Globalization;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml;
using BannerlordCoopLauncher.Core.Support;
using Mono.Cecil;

namespace RealmOfThrones.Core;

/// <summary>The done record of a server this tool made (RotServerBuild.RecordFileName in its folder): the Coop build and
/// the overlay zip it was made with, and whether the last Prepare server finished.</summary>
public sealed record RotServerRecord(string Build, string OverlaySha256, bool Done);

/// <summary>
/// Prepare server makes the Realm of Thrones server itself when the Server Tool carries the RoT build of Coop's server
/// overlay (RotStack.CoopBuild.ServerZip, src\RotCoopBuild.targets). The overlay holds only Coop's own files, so it goes
/// over a copy of the host's Workshop Coop server (item 3770450698, its DedicatedServer folder) in a short folder the
/// tool owns (DefaultRoot, C:\RotServer: the engine crashes from deep folders). In this order: a whitelist copy of the
/// Workshop server (the engine, the official modules, DedicatedServer.Windows and Coop, never the mods other tools put
/// there), then War Sails from the host's own game the way Coop's runner stages it (windows\run-windows.ps1 and
/// scripts\war-sails-staging.ps1 of the dedicated server repo): NavalDLC's data, its DLLs and StoryMode.dll, its
/// Main_map without the tiles and with the stripped scene and the naval map points, Parameters\Version.xml and the
/// virtual Version.xml in TaleWorlds.Library.dll. Then the overlay on top, file by file, so the game view and Steam DLLs
/// the Workshop Coop keeps in engine\Modules\Coop stay. None of the game's files ship with the tool. The Workshop folder
/// and the game are only read. The first build goes to RotServer.coodsz-new and becomes the server by one rename once
/// its done record says done; later ones update the server in place with the record set to not done first, so a
/// Prepare cut off half way is never taken for a finished server and the next one finishes it.
/// </summary>
public static class RotServerBuild
{
    public const string FolderName = "RotServer";
    public const string RecordFileName = ".coodsz-rot-server.json";
    public const string NewFolderSuffix = ".coodsz-new";
    public const string PrepareButton = "Prepare server";

    /// <summary>The embedded server overlay (src\RotCoopBuild.targets names it Coodsz.RotCoop.&lt;zip name&gt;).</summary>
    public static string ResourceName => "Coodsz.RotCoop." + RotStack.CoopBuild.ServerZip;

    /// <summary>
    /// Tester switches of the compat module the Server Tool never passes to the server, so one left in the user's
    /// environment can't fire a story event, host another ROT-Core version, keep a failed world or run less than all
    /// of Realm of Thrones on a world someone keeps. Testers who need them start BannerlordCoopServer.exe themselves.
    /// The test loopback switch (TestLoopback in the module) still passes: our live tests set it on the tool on
    /// purpose, and it only narrows who can join. No tool names it (RealmOfThronesCompatTests checks).
    /// </summary>
    public static bool TesterOnlyVariable(string name) =>
        name.StartsWith("COODSZ_ROT_STORY_TEST", StringComparison.OrdinalIgnoreCase)
        || name.Equals("COODSZ_ROT_ANYVERSION", StringComparison.OrdinalIgnoreCase)
        || name.Equals("COODSZ_ROT_WORLDCHECK", StringComparison.OrdinalIgnoreCase)
        || name.StartsWith("COODSZ_ROT_HOST", StringComparison.OrdinalIgnoreCase);

    /// <summary>The server folder: RotServer at the root of the Windows drive, C:\RotServer on nearly every PC.</summary>
    public static string DefaultRoot => Path.Combine(Path.GetPathRoot(Environment.SystemDirectory) is { Length: > 0 } drive ? drive : "C:\\", FolderName);

    // ----- What comes from where -----

    /// <summary>Files from the top of the Workshop server folder (the thin Workshop server has these four).</summary>
    public static readonly IReadOnlyList<string> TopFiles = ["BannerlordCoopServer.exe", "EULA.md", "LICENSE.txt", "release-info.txt"];

    /// <summary>Whole folders under the Workshop server's engine folder.</summary>
    public static readonly IReadOnlyList<string> EngineFolders = ["bin", "dotnet", "Parameters", "XmlSchemas"];

    /// <summary>Modules under the Workshop server's engine\Modules: the official ones, the server host module and Coop.</summary>
    public static readonly IReadOnlyList<string> BaseModules = ["Native", "SandBoxCore", "SandBox", "DedicatedServer.Windows", "Coop"];

    /// <summary>NavalDLC's DLLs the server needs from the game (Copy-NavalDlcAssemblies): the module and its view assemblies,
    /// which Coop's naval patches name. Its custom battle and generated UI DLLs stay out.</summary>
    public static readonly IReadOnlyList<string> NavalDlls = ["NavalDLC.dll", "NavalDLC.View.dll", "NavalDLC.ViewModelCollection.dll", "NavalDLC.GauntletUI.dll", "NavalDLC.GauntletUI.Widgets.dll"];

    /// <summary>Folders of NavalDLC's Main_map the server copy leaves out, at any depth.</summary>
    public static readonly IReadOnlyList<string> MapLeftOut = ["ShaderCache", "TileSets"];

    private const string ClientBin = "Win64_Shipping_Client";
    private const string ServerBin = "Win64_Shipping_Server";
    private const string Naval = "NavalDLC";
    private const string MapPointsFile = "naval_map_points.txt";
    private const string WarSailsBlank = "default_new_game_warsails.sav";
    private static readonly string LibraryFile = Path.Combine("engine", "bin", ServerBin, "TaleWorlds.Library.dll");
    private static readonly string VersionFile = Path.Combine("engine", "Parameters", "Version.xml");

    /// <summary>The game files War Sails on the server needs, in the order they are checked.</summary>
    public static IReadOnlyList<string> GameFiles(string gameRoot)
    {
        var naval = Path.Combine(gameRoot, "Modules", Naval);
        return
        [
            Path.Combine(naval, "SubModule.xml"),
            .. NavalDlls.Select(dll => Path.Combine(naval, "bin", ClientBin, dll)),
            Path.Combine(naval, "ModuleData"),
            Path.Combine(naval, "SceneObj", "Main_map", "scene.xscene"),
            Path.Combine(gameRoot, "Modules", "StoryMode", "bin", ClientBin, "StoryMode.dll"),
            Path.Combine(gameRoot, "bin", ClientBin, "Version.xml")
        ];
    }

    // ----- Lines -----

    private static string Label => RotStack.CoopBuild.Label;

    public const string NotCarried = "This Server Tool was built without the RoT build of Coop, so Prepare server uses the Coop server you chose.";

    public const string Damaged = "The Coop server inside this tool is damaged. Download the Server Tool again.";

    public const string NoWarSails =
        "War Sails is not on this PC, so the tool cannot make the server. Realm of Thrones needs the War Sails DLC. Install it in Steam, then click Prepare server again.";

    public static string GameFileMissing(string file) =>
        "The server needs " + file + " from your game, and it is missing. In Steam, verify the integrity of Bannerlord's game files, then click Prepare server again.";

    // The Burning Empires Server Tool's line (BeServerPrepare), so it is one key in the language files.
    public const string InsideWorkshop = "Pick a server folder outside the Steam Workshop folder. The Workshop copy stays as Steam made it.";

    public static string Taken(string folder) =>
        folder + " holds other files, so the tool left it as it is and made no server there. Move that folder somewhere else, then click Prepare server again.";

    public static string PartsMissing(string parts) =>
        "Making the server left out a War Sails part (" + parts + "). Click Prepare server again.";

    public static string Making(string folder) =>
        "Making the Realm of Thrones server in " + folder + ". The first time copies a few GB and takes a few minutes. Please wait and keep the tool open.";

    public const string Unfinished = "The last Prepare server did not finish. Click Prepare server again.";

    public static string NoSpace(string drive, long gigabytes) =>
        "There is not enough free space on drive " + drive + ". The server needs about " + gigabytes + " GB free there. Free up some space, then click Prepare server again.";

    public static string CopyingLine(string folder) =>
        "Copying the Workshop Coop server to " + folder + ". The first time copies a few GB and takes a few minutes.";

    public const string PuttingNaval = "Putting War Sails from your game into the server.";

    public static string PuttingOverlay => "Putting Coop " + Label + " into the server.";

    public static string RemovedLine(string file) => "Removed the old file " + file + " from the server's Coop.";

    public static string CopiedLine(int copied, int same, string folder) => copied == 0
        ? "The server in " + folder + " already has the Workshop Coop server files."
        : "Copied " + copied + " files from the Workshop Coop server to " + folder + ". " + same + " were already there.";

    public const string NavalLine = "Put War Sails from your game into the server.";

    public static string OverlayLine => "Put Coop " + Label + " into the server.";

    public static string LeftOld(string server, string old) =>
        "The server now runs from " + server + ". Your earlier server folder " + old + " stays as it was.";

    // ----- Checks -----

    /// <summary>Null when the game has every file War Sails on the server needs; otherwise the line to show.</summary>
    public static string? GameProblem(string gameRoot)
    {
        if (!Directory.Exists(Path.Combine(gameRoot, "Modules", Naval)))
            return NoWarSails;
        var missing = GameFiles(gameRoot).FirstOrDefault(path => !File.Exists(path) && !Directory.Exists(path));
        return missing == null ? null : GameFileMissing(AntivirusFiles.Shown(missing, gameRoot));
    }

    /// <summary>The done record in a server folder, or null when there is none or it cannot be read.</summary>
    public static RotServerRecord? ReadRecord(string serverRoot)
    {
        try
        {
            var path = Path.Combine(serverRoot, RecordFileName);
            return File.Exists(path) ? JsonSerializer.Deserialize<RotServerRecord>(File.ReadAllText(path)) : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or ArgumentException)
        {
            return null;
        }
    }

    /// <summary>True when this tool made the folder and its last Prepare server did not finish.</summary>
    public static bool IsUnfinished(string? serverRoot) =>
        !string.IsNullOrWhiteSpace(serverRoot) && ReadRecord(serverRoot) is { Done: false };

    /// <summary>What stops a server folder running War Sails, as Coop's own launcher checks it before the engine starts
    /// (WarSailsStagingPreflight): the NavalDLC module, its map points, the virtual Version.xml in TaleWorlds.Library.dll and
    /// the War Sails blank made with NavalDLC. Empty when nothing does.</summary>
    public static IReadOnlyList<string> WarSailsGaps(string serverRoot)
    {
        var gaps = new List<string>();
        var naval = Path.Combine(serverRoot, "engine", "Modules", Naval);
        if (!File.Exists(Path.Combine(naval, "SubModule.xml")) || !File.Exists(Path.Combine(naval, "bin", ServerBin, "NavalDLC.dll")))
            gaps.Add("NavalDLC module");
        if (!File.Exists(Path.Combine(naval, "SceneObj", "Main_map", MapPointsFile)))
            gaps.Add("NavalDLC Main_map");
        var library = Path.Combine(serverRoot, LibraryFile);
        if (!File.Exists(library) || !HasVirtualVersionXml(File.ReadAllBytes(library)))
            gaps.Add("TaleWorlds.Library.dll");
        if (!ListsNaval(Path.Combine(serverRoot, "engine", "bin", ServerBin, WarSailsBlank)))
            gaps.Add("War Sails blank");
        return gaps;
    }

    // ----- Making the server -----

    /// <summary>
    /// Makes or updates the server in serverRoot from the Workshop Coop server folder (workshopServer), the host's game
    /// (gameRoot) and the overlay zip, whose SHA256 must be overlaySha256 before anything is written (InvalidDataException).
    /// Throws InvalidOperationException with the line to show when the Workshop server or War Sails is missing, when
    /// serverRoot is in the Workshop folder or holds other files, when its drive has too little free space (freeSpace, the
    /// bytes free on the drive of a path, stands in for the real drive in the tests), or when a War Sails part is still
    /// missing at the end. progress gets a line as each step starts. Nothing in the Workshop folder or the game is written.
    /// Returns the lines for the log.
    /// </summary>
    public static IReadOnlyList<string> Build(string workshopServer, string gameRoot, Stream overlay, string overlaySha256, string serverRoot,
        Action<string>? progress = null, Func<string, long>? freeSpace = null)
    {
        using var zip = new MemoryStream();
        overlay.CopyTo(zip);
        zip.Position = 0;
        var hash = Convert.ToHexString(SHA256.HashData(zip));
        if (!hash.Equals(overlaySha256.Trim(), StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException(Damaged);
        if (!File.Exists(Path.Combine(workshopServer, TopFiles[0])) || !Directory.Exists(Path.Combine(workshopServer, "engine")))
            throw new InvalidOperationException(AntivirusText.NoWorkshopCoop(PrepareButton));
        if (GameProblem(gameRoot) is { } game)
            throw new InvalidOperationException(game);
        var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(serverRoot));
        var workshop = Path.TrimEndingDirectorySeparator(Path.GetFullPath(workshopServer));
        if (SamePath(root, workshop) || Inside(root, workshop) || Inside(workshop, root))
            throw new InvalidOperationException(InsideWorkshop);

        // The first build goes to a work folder next to the server folder; an update works in the server folder.
        var work = root;
        if (Directory.Exists(root))
        {
            if (IsLink(root) || (ReadRecord(root) == null && Directory.EnumerateFileSystemEntries(root).Any()))
                throw new InvalidOperationException(Taken(root));
        }
        else
        {
            // A work folder left from a cut off Prepare is ours only when its record says so, the same rule as the server folder.
            work = root + NewFolderSuffix;
            if (IsLink(work) || File.Exists(work) || (Directory.Exists(work) && ReadRecord(work) == null && Directory.EnumerateFileSystemEntries(work).Any()))
                throw new InvalidOperationException(Taken(work));
        }

        zip.Position = 0;
        using var archive = new ZipArchive(zip, ZipArchiveMode.Read, leaveOpen: true);
        // The overlay's files, the stamped library and the game's Version.xml are never copied from the Workshop, or every
        // Prepare would put the Workshop copy back first. Neither are the mod DLLs an old modded server script put in the
        // Workshop server's engine bin: the Workshop never ships them, and Coop's own module carries Harmony.
        var ours = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { LibraryFile, VersionFile };
        foreach (var dll in new[] { "0Harmony.dll", "MCMv5.dll" })
            ours.Add(Path.Combine("engine", "bin", ServerBin, dll));
        var entries = archive.Entries.Where(entry => entry.Name.Length > 0).ToList();
        foreach (var entry in entries)
            ours.Add(entry.FullName.Replace('/', Path.DirectorySeparatorChar));
        var baseFiles = WorkshopFiles(workshop, ours);
        var navalFiles = NavalFiles(gameRoot);

        // The space still to copy (a file already there only needs what it grows by) plus a margin, before anything is written.
        var naval = Path.Combine(work, "engine", "Modules", Naval);
        var needed = baseFiles.Sum(relative => Growth(new FileInfo(Path.Combine(workshop, relative)).Length, Path.Combine(work, relative)))
            + navalFiles.Sum(file => Growth(new FileInfo(file.Source).Length, Path.Combine(naval, file.Relative)))
            + Growth(new FileInfo(Path.Combine(workshop, LibraryFile)) is { Exists: true } library ? library.Length : 0, Path.Combine(work, LibraryFile))
            + entries.Sum(entry => Growth(entry.Length, Path.Combine(work, entry.FullName.Replace('/', Path.DirectorySeparatorChar))))
            + SpaceMargin;
        if ((freeSpace ?? FreeSpace)(root) < needed)
            throw new InvalidOperationException(NoSpace(DriveName(root), (long)Math.Ceiling(needed / (double)(1L << 30))));

        Directory.CreateDirectory(work);
        var record = new RotServerRecord(RotStack.CoopBuild.Build, hash, Done: false);
        WriteRecord(work, record);

        progress?.Invoke(CopyingLine(root));
        var copied = 0;
        foreach (var relative in baseFiles)
        {
            if (CopyIfChanged(Path.Combine(workshop, relative), Path.Combine(work, relative)))
                copied++;
        }

        var lines = new List<string> { CopiedLine(copied, baseFiles.Count - copied, root) };

        progress?.Invoke(PuttingNaval);
        StageNaval(gameRoot, work, navalFiles);
        StageVersion(gameRoot, workshop, work);
        lines.Add(NavalLine);
        progress?.Invoke(PuttingOverlay);
        LayOverlay(archive, work);
        lines.Add(OverlayLine);
        lines.AddRange(RemoveStrayCoopFiles(work, new HashSet<string>(ours.Concat(baseFiles), StringComparer.OrdinalIgnoreCase)).Select(RemovedLine));

        if (WarSailsGaps(work) is { Count: > 0 } gaps)
            throw new InvalidOperationException(PartsMissing(string.Join(", ", gaps)));
        WriteRecord(work, record with { Done = true });
        if (!SamePath(work, root))
            Directory.Move(work, root);
        return lines;
    }

    /// <summary>The free space a Prepare keeps on top of what it copies (the server's logs and saves need room too).</summary>
    public const long SpaceMargin = 1L << 30;

    // The Workshop whitelist: the relative path of every file copied, without ours and the leftovers of Skipped. A link in
    // the Workshop folder could point anywhere; only real folders are copied.
    private static List<string> WorkshopFiles(string workshop, HashSet<string> ours)
    {
        var files = new List<string>();
        void Add(string relative)
        {
            var source = Path.Combine(workshop, relative);
            if (File.Exists(source) && !ours.Contains(relative) && !Skipped(source))
                files.Add(relative);
        }

        void Tree(string relative)
        {
            var source = Path.Combine(workshop, relative);
            if (!Directory.Exists(source) || IsLink(source))
                return;
            foreach (var file in Directory.EnumerateFiles(source))
                Add(Path.Combine(relative, Path.GetFileName(file)));
            foreach (var folder in Directory.EnumerateDirectories(source))
                Tree(Path.Combine(relative, Path.GetFileName(folder)));
        }

        foreach (var name in TopFiles)
            Add(name);
        foreach (var folder in EngineFolders)
            Tree(Path.Combine("engine", folder));
        foreach (var module in BaseModules)
            Tree(Path.Combine("engine", "Modules", module));
        return files;
    }

    // A .bak, .coodsz-old or .coodsz-password file other tools left in the Workshop folder (the TAOM tools park Coop.Core.dll
    // as .coodsz-password) never goes into the server.
    private static bool Skipped(string path)
    {
        var name = Path.GetFileName(path);
        return name.EndsWith(".bak", StringComparison.OrdinalIgnoreCase) || name.Contains(".bak.", StringComparison.OrdinalIgnoreCase)
            || name.EndsWith(".coodsz-old", StringComparison.OrdinalIgnoreCase) || name.EndsWith(".coodsz-password", StringComparison.OrdinalIgnoreCase);
    }

    // What writing length bytes over destination takes from the drive: all of it for a new file, only the growth otherwise.
    private static long Growth(long length, string destination) =>
        new FileInfo(destination) is { Exists: true } there ? Math.Max(0, length - there.Length) : length;

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

    // C for C:\RotServer.
    private static string DriveName(string path) => (Path.GetPathRoot(path) ?? path).TrimEnd('\\', '/').TrimEnd(':');

    // engine\Modules\Coop holds only the overlay's files and the Workshop Coop's (keep): any other file there, a DLL an older
    // overlay had or a Workshop leftover an earlier Prepare copied, is removed. Only real files in that folder of the
    // tool's own server are removed; a link is never followed. Returns the removed files, relative to the server.
    private static List<string> RemoveStrayCoopFiles(string work, HashSet<string> keep)
    {
        var removed = new List<string>();
        var coop = Path.Combine(work, "engine", "Modules", "Coop");
        if (!Directory.Exists(coop) || IsLink(coop))
            return removed;
        var own = new EnumerationOptions { RecurseSubdirectories = true, AttributesToSkip = FileAttributes.ReparsePoint };
        foreach (var file in Directory.EnumerateFiles(coop, "*", own).ToList())
        {
            var relative = Path.GetRelativePath(work, file);
            if (keep.Contains(relative) || !Inside(file, coop))
                continue;
            Writable(file);
            File.Delete(file);
            removed.Add(relative);
        }

        return removed;
    }

    // ----- War Sails from the host's game -----

    /// <summary>
    /// engine\Modules\NavalDLC from the game, as Stage-NavalDlc in Coop's runner makes it: ModuleData, the DLLs of NavalDlls
    /// and StoryMode.dll in bin\Win64_Shipping_Server, Main_map without its scene and the MapLeftOut folders, the stripped
    /// scene with the naval map points (WriteNavalScene) and SubModule.xml with only the NavalDLC submodule (WriteNavalSubModule).
    /// The folder is the tool's own, so any other file in it (an older game's, the tiles of an earlier stage) is removed.
    /// </summary>
    private static void StageNaval(string gameRoot, string work, IReadOnlyList<(string Source, string Relative)> files)
    {
        var source = Path.Combine(gameRoot, "Modules", Naval);
        var modules = Path.Combine(work, "engine", "Modules");
        var target = Path.Combine(modules, Naval);
        if (IsLink(target))
            throw new InvalidOperationException(Taken(target));
        var keep = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (from, relative) in files)
        {
            CopyIfChanged(from, Path.Combine(target, relative));
            keep.Add(relative);
        }

        var map = Path.Combine("SceneObj", "Main_map");
        WriteNavalScene(Path.Combine(source, map, "scene.xscene"), Path.Combine(target, map));
        keep.Add(Path.Combine(map, "scene.xscene"));
        keep.Add(Path.Combine(map, MapPointsFile));
        WriteNavalSubModule(Path.Combine(source, "SubModule.xml"), Path.Combine(target, "SubModule.xml"));
        keep.Add("SubModule.xml");

        // A link inside is never followed, so nothing outside the folder is ever removed.
        var own = new EnumerationOptions { RecurseSubdirectories = true, AttributesToSkip = FileAttributes.ReparsePoint };
        foreach (var file in Directory.EnumerateFiles(target, "*", own).ToList())
        {
            if (keep.Contains(Path.GetRelativePath(target, file)))
                continue;
            Writable(file);
            File.Delete(file);
        }

        foreach (var folder in Directory.EnumerateDirectories(target, "*", own).OrderByDescending(folder => folder.Length).ToList())
        {
            if (!Directory.EnumerateFileSystemEntries(folder).Any())
                Directory.Delete(folder);
        }
    }

    // The game files StageNaval copies, each with its path in the server's NavalDLC folder: ModuleData, the DLLs and
    // StoryMode.dll, and Main_map without its scene and the MapLeftOut folders.
    private static List<(string Source, string Relative)> NavalFiles(string gameRoot)
    {
        var source = Path.Combine(gameRoot, "Modules", Naval);
        var files = new List<(string, string)>();
        void Copy(string from, string relative) => files.Add((from, relative));
        CopyFolder(Path.Combine(source, "ModuleData"), "ModuleData", _ => false, Copy);
        foreach (var dll in NavalDlls)
            Copy(Path.Combine(source, "bin", ClientBin, dll), Path.Combine("bin", ServerBin, dll));
        Copy(Path.Combine(gameRoot, "Modules", "StoryMode", "bin", ClientBin, "StoryMode.dll"), Path.Combine("bin", ServerBin, "StoryMode.dll"));
        var map = Path.Combine("SceneObj", "Main_map");
        CopyFolder(Path.Combine(source, map), map,
            relative => relative.Equals("scene.xscene", StringComparison.OrdinalIgnoreCase)
                || relative.Split(Path.DirectorySeparatorChar).SkipLast(1).Any(part => MapLeftOut.Contains(part, StringComparer.OrdinalIgnoreCase)),
            Copy);
        return files;
    }

    // Every file under a game folder whose relative path skip does not leave out. A link inside could point anywhere and
    // is never followed.
    private static void CopyFolder(string source, string relative, Func<string, bool> skip, Action<string, string> copy)
    {
        foreach (var file in Directory.EnumerateFiles(source))
        {
            var inside = Path.GetFileName(file);
            if (!skip(inside))
                copy(file, Path.Combine(relative, inside));
        }

        foreach (var folder in Directory.EnumerateDirectories(source).Where(folder => !IsLink(folder)))
        {
            var name = Path.GetFileName(folder);
            CopyFolder(folder, Path.Combine(relative, name), path => skip(Path.Combine(name, path)), copy);
        }
    }

    /// <summary>
    /// NavalDLC's SubModule.xml for the server (Copy-SubModuleXml with -StripDepends): without ModuleCategory and
    /// DependedModules, and only the NavalDLC submodule, without its Tags.
    /// </summary>
    internal static void WriteNavalSubModule(string source, string target)
    {
        var xml = Load(source, "NavalDLC module");
        var module = xml.SelectSingleNode("Module") ?? throw new InvalidOperationException(PartsMissing("NavalDLC module"));
        foreach (var name in new[] { "ModuleCategory", "DependedModules" })
        {
            if (module.SelectSingleNode(name) is { } node)
                module.RemoveChild(node);
        }

        if (module.SelectSingleNode("SubModules") is { } subModules)
        {
            foreach (var node in subModules.SelectNodes("SubModule")!.Cast<XmlNode>().ToList())
            {
                if ((node.SelectSingleNode("Name") as XmlElement)?.GetAttribute("value") != Naval)
                {
                    subModules.RemoveChild(node);
                    continue;
                }

                if (node.SelectSingleNode("Tags") is { } tags)
                    node.RemoveChild(tags);
            }
        }

        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        Writable(target);
        xml.Save(target);
    }

    /// <summary>
    /// NavalDLC's Main_map scene for the server (Write-StrippedNavalScene): first the pirate spawn points and the village
    /// drop offs the server reads from naval_map_points.txt, with their positions on the map, then the scene without its
    /// entities and its terrain node, which the dedicated server cannot use.
    /// </summary>
    internal static void WriteNavalScene(string sourceScene, string mapFolder)
    {
        var xml = Load(sourceScene, "NavalDLC Main_map");
        var scene = xml.SelectSingleNode("scene") ?? throw new InvalidOperationException(PartsMissing("NavalDLC Main_map"));
        var entities = scene.SelectSingleNode("entities") ?? throw new InvalidOperationException(PartsMissing("NavalDLC Main_map"));
        var lines = NavalMapPoints(entities);
        Directory.CreateDirectory(mapFolder);
        Writable(Path.Combine(mapFolder, MapPointsFile));
        File.WriteAllLines(Path.Combine(mapFolder, MapPointsFile), lines);
        entities.RemoveAll();
        if (scene.SelectSingleNode("terrain") is { } terrain)
            scene.RemoveChild(terrain);
        Writable(Path.Combine(mapFolder, "scene.xscene"));
        xml.Save(Path.Combine(mapFolder, "scene.xscene"));
    }

    // The lines of naval_map_points.txt in Coop's format: every PirateSpawnPoint ("pirate_spawn clan x y radius") and then
    // every main_map_village_dropoff ("village_dropoff village x y", named by its parent village), in the scene's order.
    internal static IReadOnlyList<string> NavalMapPoints(XmlNode entities)
    {
        var lines = new List<string> { "# GENERATED by War Sails staging from NavalDLC SceneObj/Main_map/scene.xscene (global positions)" };
        var spawns = entities.SelectNodes(".//game_entity[scripts/script[@name='PirateSpawnPoint']]")!.Cast<XmlElement>().ToList();
        foreach (var entity in spawns)
        {
            var script = entity.SelectSingleNode("scripts/script[@name='PirateSpawnPoint']")!;
            var clan = (script.SelectSingleNode("variables/variable[@name='ClanStringId']") as XmlElement)?.GetAttribute("value") ?? "";
            // PirateSpawnPoint.Radius is 10 when the scene does not set it.
            var radius = (script.SelectSingleNode("variables/variable[@name='Radius']") as XmlElement)?.GetAttribute("value") is { Length: > 0 } set ? Number(set) : 10;
            var origin = Frame(entity).Origin;
            lines.Add("pirate_spawn " + clan + " " + Format(origin[0]) + " " + Format(origin[1]) + " " + Format(radius));
        }

        var dropOffs = entities.SelectNodes(".//game_entity[tags/tag[@name='main_map_village_dropoff']]")!.Cast<XmlElement>().ToList();
        foreach (var entity in dropOffs)
        {
            var village = ParentEntity(entity) ?? throw new InvalidOperationException(PartsMissing("NavalDLC Main_map"));
            var origin = Frame(entity).Origin;
            lines.Add("village_dropoff " + village.GetAttribute("name") + " " + Format(origin[0]) + " " + Format(origin[1]));
        }

        if (spawns.Count == 0 || dropOffs.Count == 0)
            throw new InvalidOperationException(PartsMissing("NavalDLC Main_map"));
        return lines;
    }

    private sealed record EntityFrame(double[] Origin, double[] Side, double[] Forward, double[] Up);

    // An entity's frame on the map: its own position, rotation and scale, then each parent entity's in turn. The rotation
    // turns about the up axis, then the side axis, then the forward axis, each time about the axes turned so far, as
    // TaleWorlds' Mat3.ApplyEulerAngles does.
    private static EntityFrame Frame(XmlElement entity)
    {
        var transform = entity.SelectSingleNode("transform") as XmlElement;
        double[] Read(string name, double fallback) =>
            transform?.GetAttribute(name) is not { Length: > 0 } text ? [fallback, fallback, fallback]
            : text.Split(',').Select(Number).ToArray() is { Length: 3 } values ? values
            : throw new InvalidOperationException(PartsMissing("NavalDLC Main_map"));
        var position = Read("position", 0);
        var euler = Read("rotation_euler", 0);
        var scale = Read("scale", 1);
        double[] side = [1, 0, 0], forward = [0, 1, 0], up = [0, 0, 1];
        (side, forward) = Turn(side, forward, euler[2]);
        (forward, up) = Turn(forward, up, euler[0]);
        (up, side) = Turn(up, side, euler[1]);
        var local = new EntityFrame(position, Times(side, scale[0]), Times(forward, scale[1]), Times(up, scale[2]));
        if (ParentEntity(entity) is not { } parent)
            return local;
        var outer = Frame(parent);
        double[] ToParent(double[] v) => Sum(Sum(Times(outer.Side, v[0]), Times(outer.Forward, v[1])), Times(outer.Up, v[2]));
        return new EntityFrame(Sum(outer.Origin, ToParent(local.Origin)), ToParent(local.Side), ToParent(local.Forward), ToParent(local.Up));
    }

    // Turns the pair (a, b) by angle within their plane: a toward b.
    private static (double[], double[]) Turn(double[] a, double[] b, double angle)
    {
        var (cos, sin) = (Math.Cos(angle), Math.Sin(angle));
        return (Sum(Times(a, cos), Times(b, sin)), Sum(Times(b, cos), Times(a, -sin)));
    }

    private static double[] Times(double[] v, double k) => [v[0] * k, v[1] * k, v[2] * k];

    private static double[] Sum(double[] a, double[] b) => [a[0] + b[0], a[1] + b[1], a[2] + b[2]];

    // The game_entity an entity is a child of (game_entity/children/game_entity), or null at the top.
    private static XmlElement? ParentEntity(XmlElement entity) =>
        entity.ParentNode is { Name: "children" } children ? children.ParentNode as XmlElement : null;

    // A number of the Main_map scene; one the game would not write means the scene is damaged.
    private static double Number(string text) =>
        double.TryParse(text.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var number)
            ? number
            : throw new InvalidOperationException(PartsMissing("NavalDLC Main_map"));

    private static string Format(double value) => value.ToString("0.######", CultureInfo.InvariantCulture);

    // A game XML file; a damaged one is the War Sails part it makes, as a plain line.
    private static XmlDocument Load(string path, string part)
    {
        try
        {
            var xml = new XmlDocument { XmlResolver = null };
            using var reader = XmlReader.Create(path, new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null });
            xml.Load(reader);
            return xml;
        }
        catch (XmlException)
        {
            throw new InvalidOperationException(PartsMissing(part));
        }
    }

    /// <summary>
    /// The game's Version.xml goes into engine\Parameters, and into TaleWorlds.Library.dll as a virtual file
    /// (Add-VirtualVersionXml): the server reads Parameters only from the files compiled into that DLL, so without it
    /// NavalDLC's base version check stops the server. The Workshop server's library is the source on every Prepare, so
    /// it is stamped once, and a library that already has a virtual Version.xml is copied as it is.
    /// </summary>
    private static void StageVersion(string gameRoot, string workshop, string work)
    {
        var version = Path.Combine(gameRoot, "bin", ClientBin, "Version.xml");
        WriteIfChanged(Path.Combine(work, VersionFile), File.ReadAllBytes(version));
        var library = Path.Combine(workshop, LibraryFile);
        if (!File.Exists(library))
            throw new InvalidOperationException(PartsMissing("TaleWorlds.Library.dll"));
        var bytes = File.ReadAllBytes(library);
        WriteIfChanged(Path.Combine(work, LibraryFile), HasVirtualVersionXml(bytes) ? bytes : AddVirtualVersionXml(bytes, File.ReadAllText(version)));
    }

    /// <summary>TaleWorlds.Library.dll with a Version field in VirtualFolders' Parameters folder carrying
    /// VirtualFile("Version.xml", versionXml), made the way the folder's other files are.</summary>
    public static byte[] AddVirtualVersionXml(byte[] library, string versionXml)
    {
        var shape = new InvalidOperationException(PartsMissing("TaleWorlds.Library.dll"));
        try
        {
            using var module = ModuleDefinition.ReadModule(new MemoryStream(library), new ReaderParameters { InMemory = true });
            var folders = module.Types.FirstOrDefault(type => type.FullName == "TaleWorlds.Library.VirtualFolders") ?? throw shape;
            var pending = new Queue<TypeDefinition>(folders.NestedTypes);
            TypeDefinition? parameters = null;
            while (pending.Count > 0 && parameters == null)
            {
                var type = pending.Dequeue();
                if (type.Name == "Parameters")
                    parameters = type;
                foreach (var nested in type.NestedTypes)
                    pending.Enqueue(nested);
            }

            var file = parameters?.Fields.SelectMany(field => field.CustomAttributes).FirstOrDefault(attribute => attribute.AttributeType.Name == "VirtualFileAttribute") ?? throw shape;
            var added = new FieldDefinition("Version", Mono.Cecil.FieldAttributes.Public, module.TypeSystem.String);
            var attribute = new CustomAttribute(file.Constructor);
            attribute.ConstructorArguments.Add(new CustomAttributeArgument(module.TypeSystem.String, "Version.xml"));
            attribute.ConstructorArguments.Add(new CustomAttributeArgument(module.TypeSystem.String, versionXml));
            added.CustomAttributes.Add(attribute);
            parameters!.Fields.Add(added);
            using var output = new MemoryStream();
            module.Write(output);
            return output.ToArray();
        }
        // Cecil throws many kinds of exception on a damaged or unexpected DLL; each is the same plain line.
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            throw shape;
        }
    }

    // The blob Cecil writes for VirtualFile("Version.xml", ...): the prolog 01 00, the name's length and the name. Coop's
    // launcher looks for these bytes too.
    private static readonly byte[] VirtualVersionXml = [0x01, 0x00, 0x0B, .. Encoding.ASCII.GetBytes("Version.xml")];

    internal static bool HasVirtualVersionXml(byte[] library) => library.AsSpan().IndexOf(VirtualVersionXml) >= 0;

    // True when a save's header lists NavalDLC in its "Modules" (a War Sails world must), as Coop's runner reads it.
    private static bool ListsNaval(string save)
    {
        if (!File.Exists(save))
            return false;
        using var stream = File.OpenRead(save);
        var buffer = new byte[Math.Min(stream.Length, 65536)];
        stream.ReadExactly(buffer);
        var match = Regex.Match(Encoding.UTF8.GetString(buffer), "\"Modules\":\"([^\"]*)\"");
        return match.Success && match.Groups[1].Value.Split(';').Contains(Naval);
    }

    // ----- The overlay -----

    // Every file of the overlay over the server, written only when its bytes differ. An entry that would land outside the
    // server folder is refused.
    private static void LayOverlay(ZipArchive archive, string work)
    {
        var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(work)) + Path.DirectorySeparatorChar;
        foreach (var entry in archive.Entries.Where(entry => entry.Name.Length > 0))
        {
            var target = Path.GetFullPath(Path.Combine(work, entry.FullName));
            if (!target.StartsWith(root, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException(Damaged);
            using var stream = entry.Open();
            using var bytes = new MemoryStream();
            stream.CopyTo(bytes);
            WriteIfChanged(target, bytes.ToArray());
        }
    }

    // ----- Helpers -----

    private static void WriteRecord(string folder, RotServerRecord record) =>
        File.WriteAllText(Path.Combine(folder, RecordFileName), JsonSerializer.Serialize(record));

    // Copies when the copy is missing or its size or time differ, and keeps the time. True when it copied. A read-only
    // copy (or a read-only game file, whose copy would be read-only too) never stops the next Prepare.
    private static bool CopyIfChanged(string source, string destination)
    {
        var from = new FileInfo(source);
        var to = new FileInfo(destination);
        if (to.Exists && to.Length == from.Length && to.LastWriteTimeUtc == from.LastWriteTimeUtc)
            return false;
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        Writable(destination);
        File.Copy(source, destination, overwrite: true);
        Writable(destination);
        File.SetLastWriteTimeUtc(destination, from.LastWriteTimeUtc);
        return true;
    }

    // Writes the bytes when they differ, through a .tmp file and one move, so a Prepare cut off half way never leaves a
    // half written file (TaleWorlds.Library.dll above all).
    private static void WriteIfChanged(string destination, byte[] bytes)
    {
        if (File.Exists(destination) && new FileInfo(destination).Length == bytes.Length && File.ReadAllBytes(destination).AsSpan().SequenceEqual(bytes))
            return;
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        var temporary = destination + ".tmp";
        Writable(temporary);
        File.WriteAllBytes(temporary, bytes);
        Writable(destination);
        File.Move(temporary, destination, overwrite: true);
    }

    private static void Writable(string file)
    {
        if (File.Exists(file) && File.GetAttributes(file) is var attributes && (attributes & FileAttributes.ReadOnly) != 0)
            File.SetAttributes(file, attributes & ~FileAttributes.ReadOnly);
    }

    private static bool IsLink(string folder) =>
        Directory.Exists(folder) && (File.GetAttributes(folder) & FileAttributes.ReparsePoint) != 0;

    private static bool SamePath(string a, string b) =>
        Path.GetFullPath(a).TrimEnd('\\', '/').Equals(Path.GetFullPath(b).TrimEnd('\\', '/'), StringComparison.OrdinalIgnoreCase);

    private static bool Inside(string path, string root) =>
        Path.GetFullPath(path).StartsWith(Path.GetFullPath(root).TrimEnd('\\', '/') + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
}
