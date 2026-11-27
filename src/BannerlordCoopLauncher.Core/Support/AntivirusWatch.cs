using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using BannerlordCoopLauncher.Core.IO;
using BannerlordCoopLauncher.Core.Launch;
using BannerlordCoopLauncher.Core.Steam;

namespace BannerlordCoopLauncher.Core.Support;

/// <summary>
/// Remembers the files a tool put in place (after a good Prepare, Install or Start) in antivirus-watch.json
/// in the tool's settings folder. A remembered file that is gone later was taken by something on this PC,
/// most often antivirus (crash report CR-88BP7, the Workshop BannerlordCoopServer.exe ran at 02:09 and was
/// gone soon after), so the tool can word its own problem that way instead of a plain "not found". The
/// watch never stops anything by itself. A file that never existed is never remembered.
/// </summary>
public sealed class AntivirusWatch
{
    public const string FileName = "antivirus-watch.json";

    private readonly string? _path;

    /// <summary>A watch kept in this json file. Tests pass a scratch path.</summary>
    public AntivirusWatch(string? path) => _path = path;

    /// <summary>The running tool's watch, or one that remembers nothing before ToolData.Start (a test run).</summary>
    public static AntivirusWatch ForTool() => new(ToolData.Current == null ? null : ToolData.File(FileName));

    /// <summary>
    /// The list a good Prepare, Install or Start leaves. Every remembered file inside these folders is
    /// replaced by the files of this list that exist now, so a renamed, dropped or deliberately removed file
    /// is forgotten. Files in other folders stay as they are. Never throws.
    /// </summary>
    public void Remember(IEnumerable<string?> folders, IEnumerable<string> files)
    {
        if (_path == null)
            return;
        try
        {
            var roots = Roots(folders);
            var existing = files.Where(file => !string.IsNullOrWhiteSpace(file)).Select(Full).Where(File.Exists).ToList();
            Locked(() =>
            {
                var known = Read(out var mainReadable) ?? new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                known.RemoveWhere(file => Inside(file, roots));
                foreach (var file in existing)
                    known.Add(file);
                Write(known, mainReadable);
            });
        }
        catch (Exception ex) when (Harmless(ex))
        {
            // Best effort. Without the list the tool gives its usual messages.
        }
    }

    /// <summary>
    /// The remembered files inside these folders that are gone now. A file whose own folder is gone too is
    /// left out, since a deleted or moved folder is the host's doing, not antivirus. A file that is back
    /// (restored from quarantine or put back by the tool) is not gone. Never throws.
    /// </summary>
    public IReadOnlyList<string> Gone(IEnumerable<string?> folders)
    {
        try
        {
            var roots = Roots(folders);
            return (Read(out _) ?? [])
                .Where(file => Inside(file, roots))
                .Where(file => !File.Exists(file) && Directory.Exists(Path.GetDirectoryName(file)))
                .OrderBy(file => file, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }
        catch (Exception ex) when (Harmless(ex))
        {
            return [];
        }
    }

    /// <summary>
    /// Gone, but a Workshop Coop file is left out while Steam is updating the item (the update deletes and
    /// replaces DedicatedServer\BannerlordCoopServer.exe), and is looked at again after the delay, so a
    /// file Steam was just swapping isn't called antivirus.
    /// </summary>
    public IReadOnlyList<string> GoneSettled(IEnumerable<string?> folders, string? workshopCoop, TimeSpan recheck)
    {
        var gone = Gone(folders);
        if (workshopCoop == null || gone.Count == 0)
            return gone;
        var workshop = Roots([workshopCoop]);
        bool InWorkshop(string file) => Inside(file, workshop);
        if (!gone.Any(InWorkshop))
            return gone;
        if (AntivirusFiles.SteamUpdating(workshopCoop))
            return gone.Where(file => !InWorkshop(file)).ToList();
        if (recheck > TimeSpan.Zero)
            Thread.Sleep(recheck);
        // An update Steam started during the wait leaves the Workshop files out too.
        if (recheck > TimeSpan.Zero && AntivirusFiles.SteamUpdating(workshopCoop))
            return gone.Where(file => !InWorkshop(file)).ToList();
        return gone.Where(file => !InWorkshop(file) || !File.Exists(file)).ToList();
    }

    // Two tools of a product, or a tool started twice, share one file, so writes take a named mutex.
    private void Locked(Action work)
    {
        using var mutex = new Mutex(false, @"Local\CoodszAntivirusWatch" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(_path!.ToUpperInvariant())))[..16]);
        bool owned;
        try
        {
            owned = mutex.WaitOne(TimeSpan.FromSeconds(3));
        }
        catch (AbandonedMutexException)
        {
            owned = true;
        }

        if (!owned)
            return;
        try
        {
            work();
        }
        finally
        {
            mutex.ReleaseMutex();
        }
    }

    private string Backup => _path + ".bak";

    // The list, or the backup of the last good one when the file is broken or locked. Null when neither reads.
    private HashSet<string>? Read(out bool mainReadable)
    {
        mainReadable = false;
        if (_path == null)
            return null;
        if (!File.Exists(_path))
        {
            mainReadable = true;
            return ReadFile(Backup);
        }

        var main = ReadFile(_path);
        mainReadable = main != null;
        return main ?? ReadFile(Backup);
    }

    private static HashSet<string>? ReadFile(string path)
    {
        try
        {
            if (!File.Exists(path))
                return null;
            var list = JsonSerializer.Deserialize<List<string>>(File.ReadAllText(path));
            return list == null ? null : new HashSet<string>(list, StringComparer.OrdinalIgnoreCase);
        }
        catch (Exception ex) when (ex is JsonException || Harmless(ex))
        {
            return null;
        }
    }

    // A whole new file moved in place, so a reader never sees half a list. The last readable list is kept
    // as .bak, and a broken one is never kept as the backup.
    private void Write(HashSet<string> known, bool mainReadable)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_path!)!);
        var temp = _path + "." + Environment.ProcessId + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(known.OrderBy(file => file, StringComparer.OrdinalIgnoreCase).ToList(), new JsonSerializerOptions { WriteIndented = true }));
        try
        {
            if (mainReadable && File.Exists(_path))
                File.Replace(temp, _path!, Backup);
            else
                File.Move(temp, _path!, overwrite: true);
        }
        finally
        {
            if (File.Exists(temp))
                File.Delete(temp);
        }
    }

    private static List<string> Roots(IEnumerable<string?> folders) =>
        folders.Where(folder => !string.IsNullOrWhiteSpace(folder)).Select(folder => Full(folder!).TrimEnd('\\', '/') + Path.DirectorySeparatorChar).ToList();

    private static bool Inside(string file, List<string> roots) =>
        roots.Any(root => file.StartsWith(root, StringComparison.OrdinalIgnoreCase));

    internal static bool Harmless(Exception ex) =>
        ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException
            or System.Security.SecurityException or WaitHandleCannotBeOpenedException;

    internal static string Full(string path)
    {
        try
        {
            return Path.GetFullPath(path);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return path;
        }
    }
}

/// <summary>
/// The files each server tool watches, from the places that put them there (Prepare server, Install or
/// repair, Start server, ServerFolderRepair, CoopServerBin, ServerProgramRepair and the Workshop Coop).
/// </summary>
public static class AntivirusFiles
{
    public const string CoopWorkshopId = SteamWorkshopAcf.CoopWorkshopId;
    public const string ServerExeName = "BannerlordCoopServer.exe";

    /// <summary>The Workshop Coop folder (steamapps\workshop\content\261550\3770450698) in one of these libraries, or null.</summary>
    public static string? WorkshopCoop(IEnumerable<string> steamLibraries)
    {
        foreach (var library in steamLibraries)
        {
            var path = Path.Combine(library, "steamapps", "workshop", "content", "261550", CoopWorkshopId);
            if (File.Exists(Path.Combine(path, "SubModule.xml")))
                return path;
        }

        return null;
    }

    /// <summary>True when a server tool's program is the Workshop Coop's: no folder was set, or the set one is
    /// in the Workshop Coop item (steamapps\workshop\content\261550\3770450698), also when that folder is gone
    /// now. Only that program can be missing because Steam is still downloading it.</summary>
    public static bool UsesWorkshopServer(string? serverExecutable) =>
        string.IsNullOrWhiteSpace(serverExecutable)
        || serverExecutable.Replace('/', '\\').Contains(@"\workshop\content\261550\" + CoopWorkshopId + @"\", StringComparison.OrdinalIgnoreCase);

    /// <summary>The Workshop Coop's DedicatedServer\BannerlordCoopServer.exe.</summary>
    public static string WorkshopServerExe(string workshopCoop) => Path.Combine(workshopCoop, "DedicatedServer", ServerExeName);

    /// <summary>
    /// True while Steam downloads or updates the Workshop Coop itself (SteamWorkshopAcf.CoopPending: Coop's
    /// download folder, or its own appworkshop_261550.acf entries). Another mod's pending update doesn't count.
    /// The swap deletes files for a moment.
    /// </summary>
    public static bool SteamUpdating(string workshopCoop)
    {
        try
        {
            return SteamWorkshopAcf.CoopPending(Path.GetFullPath(Path.Combine(workshopCoop, "..", "..", "..")));
        }
        catch (Exception ex) when (AntivirusWatch.Harmless(ex))
        {
            return false;
        }
    }

    /// <summary>
    /// True when Steam is downloading the Workshop Coop in one of these libraries before its content folder
    /// is there (SteamWorkshopAcf.CoopPending on each library's workshop folder). Another item's update alone
    /// does not count.
    /// </summary>
    public static bool CoopDownloading(IEnumerable<string> steamLibraries) =>
        steamLibraries.Any(library => SteamWorkshopAcf.CoopPending(Path.Combine(library, "steamapps", "workshop")));

    /// <summary>The engine files every Coop server folder starts with, engine\dotnet\dotnet.exe and the starter (DedicatedEngineLaunch).</summary>
    public static IReadOnlyList<string> Engine(string serverRoot) =>
        [DedicatedEngineLaunch.Dotnet(serverRoot), DedicatedEngineLaunch.Starter(serverRoot)];

    /// <summary>The DLLs a payload folder puts in a target folder, like ServerFolderRepair's DedicatedServerRepair payload in a server folder or a compat module in Modules.</summary>
    public static IReadOnlyList<string> RepairPayloadDlls(string serverRoot, string? payloadRoot)
    {
        if (string.IsNullOrWhiteSpace(payloadRoot) || !Directory.Exists(payloadRoot))
            return [];
        return Directory.EnumerateFiles(payloadRoot, "*.dll", SearchOption.AllDirectories)
            .Select(source => Path.Combine(serverRoot, Path.GetRelativePath(payloadRoot, source)))
            .ToList();
    }

    /// <summary>The DLLs now in a module's bin folders, for a compat module the tool copied in.</summary>
    public static IReadOnlyList<string> ModuleDlls(string moduleFolder)
    {
        var bin = Path.Combine(moduleFolder, "bin");
        if (!Directory.Exists(bin))
            return [];
        try
        {
            return Directory.EnumerateFiles(bin, "*.dll", SearchOption.AllDirectories).ToList();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }

    /// <summary>
    /// Why the Workshop Coop dedicated server can't be used, or null when it can. No Workshop Coop at all
    /// says subscribe. A Workshop Coop Steam is still downloading (Steam writes SubModule.xml first) says
    /// let Steam finish. A server file the watch saw before and is gone now says antivirus (CR-88BP7), and
    /// any other missing file says the download is incomplete. Both of those end with the resubscribe step.
    /// gone is AntivirusWatch.Gone for the Workshop Coop folder.
    /// </summary>
    public static string? WorkshopServerProblem(string? workshopCoop, string button, IReadOnlyCollection<string>? gone = null, IEnumerable<string>? steamLibraries = null)
    {
        // Right after a subscribe Steam has not made the content folder yet (CR-P75R3, a minute after a
        // resubscribe), so its download folder or its own list still says downloading, not subscribe.
        if (workshopCoop == null)
            return steamLibraries != null && CoopDownloading(steamLibraries) ? AntivirusText.WorkshopDownloading(button) : AntivirusText.NoWorkshopCoop(button);
        var server = Path.Combine(workshopCoop, "DedicatedServer");
        var missing = new[] { Path.Combine(server, ServerExeName), DedicatedEngineLaunch.Dotnet(server), DedicatedEngineLaunch.Starter(server) }
            .FirstOrDefault(file => !File.Exists(file));
        // Steam swaps files during an update, so a Coop it is still updating is never copied from, even with
        // every server file there.
        if (SteamUpdating(workshopCoop))
            return AntivirusText.WorkshopDownloading(button);
        if (missing == null)
            return null;
        var full = AntivirusWatch.Full(missing);
        var shown = Shown(full, workshopCoop);
        return gone != null && gone.Contains(full, StringComparer.OrdinalIgnoreCase)
            ? AntivirusText.WorkshopTaken(shown)
            : AntivirusText.WorkshopIncomplete(shown, button);
    }

    /// <summary>
    /// The gone files the running tool version still puts there. A file a tool update renamed or dropped
    /// (and cleaned up) is left out, so it is never called antivirus.
    /// </summary>
    public static IReadOnlyList<string> StillShipped(IEnumerable<string> gone, IEnumerable<string> expected)
    {
        var now = expected.Select(AntivirusWatch.Full).ToHashSet(StringComparer.OrdinalIgnoreCase);
        return gone.Where(now.Contains).ToList();
    }

    /// <summary>A short name for a message, the path under the folder it sits in, or the file name.</summary>
    public static string Shown(string file, string? folder)
    {
        if (!string.IsNullOrWhiteSpace(folder))
        {
            var root = AntivirusWatch.Full(folder).TrimEnd('\\', '/') + Path.DirectorySeparatorChar;
            if (file.StartsWith(root, StringComparison.OrdinalIgnoreCase))
                return file[root.Length..];
        }

        return Path.GetFileName(file);
    }
}

/// <summary>
/// How a tool words a problem it has anyway when the file behind it was there before. These only pick
/// the words. Whether the tool stops is the tool's own check, the same as without the watch.
/// </summary>
public static class AntivirusWording
{
    /// <summary>
    /// The tool's usual message for a missing file, or the antivirus one when that file is in gone.
    /// removedYourself is the way back for a host who deleted it on purpose, like Set server folder.
    /// </summary>
    public static string Reword(string usual, string? missingFile, IReadOnlyCollection<string> gone, string? root, string? workshopCoop, string button, string? removedYourself = null)
    {
        if (string.IsNullOrWhiteSpace(missingFile))
            return usual;
        var full = AntivirusWatch.Full(missingFile);
        if (!gone.Contains(full, StringComparer.OrdinalIgnoreCase))
            return usual;
        var text = InWorkshop(full, workshopCoop)
            ? AntivirusText.WorkshopTaken(AntivirusFiles.Shown(full, workshopCoop))
            : AntivirusText.Taken(AntivirusFiles.Shown(full, root), button);
        return removedYourself == null ? text : text + "\n" + removedYourself;
    }

    /// <summary>One log line for every gone file at once, or null when none is gone.</summary>
    public static string? Note(IEnumerable<string> gone, string? root, string? workshopCoop)
    {
        var names = gone.Select(file => AntivirusFiles.Shown(file, InWorkshop(file, workshopCoop) ? workshopCoop : root)).ToList();
        return names.Count == 0 ? null : AntivirusText.Noticed(string.Join(", ", names));
    }

    private static bool InWorkshop(string file, string? workshopCoop) =>
        workshopCoop != null && file.StartsWith(AntivirusWatch.Full(workshopCoop).TrimEnd('\\', '/') + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
}

/// <summary>The antivirus lines every tool shows. The window they point to is Fix antivirus.</summary>
public static class AntivirusText
{
    public const string WindowButtonTip = "Shows the folders your antivirus should skip on this PC and opens Windows Security. It changes no setting itself.";

    // Where each tool has the Fix antivirus button, the last sentence of every antivirus line.
    public const string PlaceServer = "Fix antivirus is next to Unblock DLLs.";
    public const string PlaceLogsPage = "Fix antivirus is on the Logs and help page.";
    public const string PlaceLogsMenu = "Fix antivirus is in the ▾ menu next to Send my logs.";

    /// <summary>Where the running tool has Fix antivirus. The server tools are the default.</summary>
    public static string Place => ToolData.CurrentPlan?.Key switch
    {
        "taom-client" or "be-client" => PlaceLogsPage,
        "coop-client" or "ad1259-client" or "more-nations-client" => PlaceLogsMenu,
        _ => PlaceServer
    };

    /// <summary>A file the tool put there is gone. button is the one that puts it back, like Prepare server.</summary>
    public static string Taken(string file, string button) =>
        "Your antivirus most likely removed " + file + ". Restore it from your antivirus (its quarantine list), add the folders listed in Fix antivirus as folders your antivirus should skip, then click " + button + " again. " + Place;

    /// <summary>A file the tool put there is gone and this click puts it back.</summary>
    public static string TakenPutBack(string file, string button) =>
        "Your antivirus most likely removed " + file + ". " + button + " puts it back now. Add the folders listed in Fix antivirus as folders your antivirus should skip so it stays. " + Place;

    /// <summary>A file in the Bannerlord Coop Workshop folder is gone. Steam puts it back on a resubscribe.</summary>
    public static string WorkshopTaken(string file) =>
        "Your antivirus most likely removed " + file + " from the Bannerlord Coop Workshop folder. Restore it from your antivirus (its quarantine list) and add the folders listed in Fix antivirus as folders your antivirus should skip. Then unsubscribe from Bannerlord Coop in Steam and subscribe again. " + Place;

    /// <summary>The one log line for files gone since the last good start, when the start goes on anyway.</summary>
    public static string Noticed(string files) =>
        "Your antivirus most likely removed " + files + " since the last start that worked. Add the folders listed in Fix antivirus as folders your antivirus should skip. " + Place;

    /// <summary>The picked server exe is gone and the tool found another one to use.</summary>
    public static string OtherServer(string file, string folder) =>
        "Your antivirus most likely removed " + file + ", so this tool now uses the server it found in " + folder + ". Add the folders listed in Fix antivirus as folders your antivirus should skip. " + Place;

    public const string RemovedYourselfSetFolder = "If you removed it yourself, click Set server folder.";

    /// <summary>No Workshop Coop at all.</summary>
    public static string NoWorkshopCoop(string button) =>
        "Bannerlord Coop from the Steam Workshop was not found. Subscribe to Bannerlord Coop, let Steam finish the download, then click " + button + " again.";

    /// <summary>Steam is still downloading or updating the Workshop Coop (Coodsz's wording, 2026-10-29).</summary>
    public static string WorkshopDownloading(string button) =>
        "Steam is still downloading Bannerlord Coop. Wait until it's done in Steam's Downloads page, then click " + button + " again.";

    /// <summary>A Workshop Coop server file is missing that the tool never saw there.</summary>
    public static string WorkshopIncomplete(string file, string button) =>
        "The Bannerlord Coop Workshop download is incomplete. " + file + " is missing. In Steam, unsubscribe from Bannerlord Coop and subscribe again, let the download finish, then click " + button + " again.";

    public const string WindowTitle = "Fix antivirus";
    public const string WindowIntro = "Antivirus apps sometimes remove game or server files they don't know. Then the tool says a file was not found. These steps keep the files safe. This window changes nothing on your PC by itself.";
    public const string FoldersTitle = "Folders your antivirus should skip on this PC";
    public const string Step1 = "Click Open Windows Security below. Windows Security opens. Use its Virus and threat protection page.";
    public const string Step2 = "Click Protection history. If a file from one of these folders is there, open it and click Restore. Skip this if nothing is listed.";
    public const string Step3 = "Go back, click Manage settings under Virus and threat protection settings, then Add or remove exclusions.";
    public const string Step4 = "Click Add an exclusion, pick Folder, and paste each folder below. Use the Copy buttons.";
    public const string Step5 = "Back in this tool, click the button the message named again.";
    public const string OtherApps = "Other antivirus apps have a similar list of folders to skip in their settings.";
    public const string OpenSecurity = "Open Windows Security";
    public const string OpenSecurityTip = "Opens the Virus and threat protection page of Windows Security. Nothing is changed.";
    public const string CouldNotOpen = "Windows Security could not be opened. Open the Start menu, type Windows Security and open it.";
    public const string NoFolders = "No folders were found on this PC yet. Start the game once, then open this window again.";

    // The one page the window opens. It only shows Windows Security; it never changes a setting.
    public const string SecurityPage = "windowsdefender://threat";

    public const string ToolFolder = "This tool";
    public const string ToolDataFolder = "This tool's files";
    public const string ServerFolder = "Server folder";
    public const string ModulesFolder = "Game Modules folder";
    public const string WorkshopFolder = "Bannerlord Coop Workshop folder";
}

/// <summary>One folder the Fix antivirus window lists.</summary>
public sealed record AntivirusFolder(string Label, string Path);

public static class AntivirusFolders
{
    /// <summary>This PC's real folders to exclude, the ones that exist, each once.</summary>
    public static IReadOnlyList<AntivirusFolder> For(string? toolFolder, string? toolDataFolder, string? serverFolder, string? gameRoot, string? workshopCoop)
    {
        var list = new List<AntivirusFolder>();
        void Add(string label, string? path)
        {
            if (string.IsNullOrWhiteSpace(path))
                return;
            var trimmed = path.TrimEnd('\\', '/');
            if (!Directory.Exists(trimmed) || list.Any(folder => folder.Path.Equals(trimmed, StringComparison.OrdinalIgnoreCase)))
                return;
            list.Add(new AntivirusFolder(label, trimmed));
        }

        Add(AntivirusText.ToolFolder, toolFolder);
        Add(AntivirusText.ToolDataFolder, toolDataFolder);
        Add(AntivirusText.ServerFolder, serverFolder);
        Add(AntivirusText.ModulesFolder, string.IsNullOrWhiteSpace(gameRoot) ? null : System.IO.Path.Combine(gameRoot, "Modules"));
        Add(AntivirusText.WorkshopFolder, workshopCoop);
        return list;
    }
}
