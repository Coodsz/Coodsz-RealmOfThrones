using System.Diagnostics;

namespace TaomCoop.Core;

public enum KitCoopServerCheck
{
    /// <summary>Every Coop DLL the host kit pins (TaomStack.CoopServerPins) is the released Coop 0.1.6 file.</summary>
    Pinned,
    /// <summary>Coop.Core.dll was edited for a password and its .pre-password.bak is the released Coop 0.1.6 file; the other pins match.</summary>
    PasswordEdited,
    /// <summary>The Coop SubModule.xml names another Coop version (0.1.7 on the Workshop since 2026-10-30).</summary>
    OtherVersion,
    /// <summary>A pinned Coop DLL is missing, or the SubModule.xml says 0.1.6 (or nothing) but a file is another build.</summary>
    DifferentBuild,
    /// <summary>No BannerlordCoopServer.exe.</summary>
    NoServer
}

/// <summary>Where Prepare server copies from and Start server repairs from. A pinned Coop 0.1.6 Workshop server stays first, as before.
/// Steam's Workshop has Coop 0.1.7 since 2026-10-30, so a host can choose a Coop 0.1.6 dedicated server folder downloaded from Coop's
/// GitHub release or Nexus Mods. The tool only reads that folder, it never writes into it.</summary>
public sealed record KitCoopServerPick(string? Server, bool Chosen, string? Problem);

public static class KitCoopServer
{
    public const string ExeName = "BannerlordCoopServer.exe";
    public const string ChooseButton = "Choose Coop 0.1.6 server";
    public const string GitHubRelease = "https://github.com/Bannerlord-Coop-Team/BannerlordCoop/releases/tag/v0.1.6";
    public const string NexusFiles = "https://www.nexusmods.com/mountandblade2bannerlord/mods/11879?tab=files&category=archived";

    // ModDB is left out: its server file was replaced by Coop 0.1.7 on 2026-10-30.
    public const string WhereToGet =
        "Get the Coop 0.1.6 dedicated server from Coop's GitHub release (Windows dedicated server, about 4.4 GB) or from the archived files on Nexus Mods (needs a Nexus login). Unpack it with 7-Zip, then click Choose Coop 0.1.6 server on the Saves page and pick that folder.";

    public const string NotAServer =
        "BannerlordCoopServer.exe was not found in this folder or in a DedicatedServer folder inside it. Choose the folder you unpacked the Coop 0.1.6 dedicated server into.";

    // A host who unzipped the 12 MB client (BannerlordCoop-v0.1.6.zip) got NotAServer with the same advice under it as before, and
    // read it as nothing happened (TAOM Server Tool report, 2026-11-07).
    public const string ClientNotServer =
        "This folder has the Coop 0.1.6 client (BannerlordCoop-v0.1.6.zip), not the dedicated server. The server tool needs the Windows dedicated server download, about 4.4 GB.";

    /// <summary>True when a picked folder, its Coop folder or its only subfolder is a Coop client module (SubModule.xml with Id Coop and
    /// bin\Win64_Shipping_Client) with no server program, so Problem can say it is the client instead of NotAServer.</summary>
    internal static bool IsCoopClient(string? folder)
    {
        if (string.IsNullOrWhiteSpace(folder) || !Directory.Exists(folder))
            return false;
        static bool Client(string dir) =>
            File.Exists(Path.Combine(dir, "SubModule.xml"))
            && Directory.Exists(Path.Combine(dir, "bin", "Win64_Shipping_Client"))
            && ModuleIdentityReader.Read(Path.Combine(dir, "SubModule.xml")).Id.Equals(TaomStack.CoopModuleId, StringComparison.OrdinalIgnoreCase);
        if (Client(folder) || Client(Path.Combine(folder, TaomStack.CoopModuleId)))
            return true;
        var inner = Directory.GetDirectories(folder);
        return inner.Length == 1 && (Client(inner[0]) || Client(Path.Combine(inner[0], TaomStack.CoopModuleId)));
    }

    public const string DifferentBuild =
        "The Coop files in this folder are missing or a different build than the official Coop 0.1.6 server. TAOM.CoopCompat 0.3.25 needs the official Coop 0.1.6 dedicated server.";

    public const string PasswordEdited =
        "The Coop.Core.dll in this folder was changed by a password edit. The tool does not change your folder, so unpack the Coop 0.1.6 dedicated server again into a new folder and choose that one.";

    public static string OtherVersion(string version) =>
        "This folder has Coop " + version.TrimStart('v') + ". TAOM.CoopCompat " + TaomStack.CompatVersion.TrimStart('v') + " needs the official Coop " + TaomStack.CoopVersion.TrimStart('v') + " dedicated server.";

    public static string Unreadable(string reason) => "The tool could not read the Coop 0.1.6 server folder. " + reason;

    public static string Gone(string folder) => "The Coop 0.1.6 server folder you chose is gone: " + folder;

    public static string Chosen(string server) =>
        "Prepare server and Start server use the Coop 0.1.6 server in " + server + " while the Steam Workshop has no Coop 0.1.6 server. The tool only copies from that folder.";

    public static string YourServer(string server) =>
        "Your Coop 0.1.6 server is in " + server + ". Prepare server and Start server use it unless the Steam Workshop already has Coop 0.1.6.";

    /// <summary>The line under Choose Coop 0.1.6 server when the tool opens, or null when nothing is chosen. It only looks whether
    /// the folder is there: which server Prepare server and Start server use needs the hashes, which are read off the window's thread.</summary>
    public static (string Text, bool Failed)? StartupLine(string? server) =>
        string.IsNullOrWhiteSpace(server) ? null
        : Directory.Exists(server) ? (YourServer(server), false)
        : (Gone(server), true);

    public static string ChosenAsBackup(string server) =>
        "The Steam Workshop has the Coop 0.1.6 server, so Prepare server and Start server use it first. Your Coop 0.1.6 server in " + server + " is the backup for when the Workshop has a newer Coop.";

    public static string LinkOverlap(string server, string linkBase) =>
        "Your Coop 0.1.6 server folder " + server + " and the tool's link folder " + linkBase + " are inside one another. Unpack the Coop 0.1.6 server into another folder and choose that one.";

    public static string PercentInPath(string folder) =>
        "The Coop 0.1.6 server folder " + folder + " has a % in its path. Move or rename it so the path has no %, then choose it again.";

    public static string PercentInLinkPath(string link) =>
        "The tool's link folder " + link + " has a % in its path, so the Coop 0.1.6 server can't be linked from there.";

    public static string LinkNotRemoved(string link) =>
        "The link to the Coop 0.1.6 server at " + link + " could not be removed. The tool removes it next time.";

    public static string ChoseHostFolder(string server, string folder) =>
        "Your Coop 0.1.6 server folder " + server + " and the TAOM host folder " + folder + " are inside one another. Choose the Coop 0.1.6 server you downloaded, not the server this tool prepared.";

    public static string Overlap(string server, string folder) =>
        "Your Coop 0.1.6 server folder " + server + " and the TAOM host folder " + folder + " are inside one another, so Prepare server would copy into your download. Unpack the Coop 0.1.6 server into another folder and choose that one.";

    /// <summary>A problem with the download links under it, one line each, so the status line and the log show each link on its own line.</summary>
    public static string WithWhereToGet(string problem) => problem + "\n" + WhereToGet + "\n" + GitHubRelease + "\n" + NexusFiles;

    /// <summary>What Choose Coop 0.1.6 server shows after a pick, under the button and under Start server: the problem with where
    /// to get the server (Failed), or which server Prepare server and Start server use now.</summary>
    public static (string Text, bool Failed) ChooseResult(string? problem, string? server, bool workshopFirst) =>
        problem != null ? (WithWhereToGet(problem), true)
        : server == null ? (WithWhereToGet(NotAServer), true)
        : (workshopFirst ? ChosenAsBackup(server) : Chosen(server), false);

    /// <summary>The server folder in a picked folder: the folder itself when it holds BannerlordCoopServer.exe (the GitHub 7z), its
    /// DedicatedServer folder (a Nexus file), or its only subfolder when 7-Zip unpacked into one more folder. Null when none has it.</summary>
    public static string? Resolve(string? folder)
    {
        if (string.IsNullOrWhiteSpace(folder) || !Directory.Exists(folder))
            return null;
        if (ResolveHere(folder) is { } here)
            return here;
        try
        {
            var inner = Directory.GetDirectories(folder);
            return inner.Length == 1 ? ResolveHere(inner[0]) : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static string? ResolveHere(string folder)
    {
        if (File.Exists(Path.Combine(folder, ExeName)))
            return folder;
        var dedicated = Path.Combine(folder, "DedicatedServer");
        return File.Exists(Path.Combine(dedicated, ExeName)) ? dedicated : null;
    }

    public static string CoopModule(string server) => Path.Combine(server, "engine", "Modules", "Coop");

    public static string CoopBin(string server) => Path.Combine(CoopModule(server), "bin", "Win64_Shipping_Server");

    public static string CoopCoreDll(string server) => Path.Combine(CoopBin(server), "Coop.Core.dll");

    public static KitCoopServerCheck Check(string? server) => Check(server, TaomStack.CoopServerPins);

    // The pins are a parameter so the tests can stand in files of their own for the released Coop 0.1.6 DLLs.
    // Reading a file can throw (locked, no access); the callers show that reason.
    internal static KitCoopServerCheck Check(string? server, IReadOnlyDictionary<string, string> pins)
    {
        if (server == null || !File.Exists(Path.Combine(server, ExeName)))
            return KitCoopServerCheck.NoServer;
        // The version goes first: Steam's update to Coop 0.1.7 keeps an old .bak next to the new DLL, and a folder whose
        // SubModule.xml names another Coop is not the kit's Coop even when its DLLs match.
        if (CoopStaging.IsOtherCoopVersion(CoopModule(server)))
            return KitCoopServerCheck.OtherVersion;
        var bin = CoopBin(server);
        var others = pins.Where(pin => !pin.Key.Equals("Coop.Core.dll", StringComparison.OrdinalIgnoreCase)).All(pin => Matches(Path.Combine(bin, pin.Key), pin.Value));
        var corePin = pins["Coop.Core.dll"];
        var dll = CoopCoreDll(server);
        if (others && Matches(dll, corePin))
            return KitCoopServerCheck.Pinned;
        return others && File.Exists(dll) && Matches(dll + ".pre-password.bak", corePin) ? KitCoopServerCheck.PasswordEdited : KitCoopServerCheck.DifferentBuild;
    }

    /// <summary>Why a chosen folder can't be used, or null when it holds the official Coop 0.1.6 server.</summary>
    public static string? Problem(string? folder) => Problem(folder, TaomStack.CoopServerPins);

    internal static string? Problem(string? folder, IReadOnlyDictionary<string, string> pins)
    {
        if (folder != null && folder.Contains('%'))
            return PercentInPath(folder);
        try
        {
            var server = Resolve(folder);
            return Check(server, pins) switch
            {
                KitCoopServerCheck.Pinned => null,
                KitCoopServerCheck.NoServer => !string.IsNullOrWhiteSpace(folder) && !Directory.Exists(folder) ? Gone(folder)
                    : IsCoopClient(folder) ? ClientNotServer : NotAServer,
                KitCoopServerCheck.OtherVersion => OtherVersion(ModuleIdentityReader.Read(Path.Combine(CoopModule(server!), "SubModule.xml")).Version),
                KitCoopServerCheck.PasswordEdited => PasswordEdited,
                _ => DifferentBuild
            };
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return Unreadable(ex.Message);
        }
    }

    /// <summary>The server Prepare server copies from. A Workshop server with the released Coop 0.1.6 (a password edit Prepare
    /// puts back counts) stays the source, as before. Otherwise a chosen folder with the official Coop 0.1.6 server. A Workshop
    /// that is newer with nothing chosen is refused with where to get Coop 0.1.6. No Workshop server and nothing chosen gives
    /// no server and no problem, so Prepare says why the Workshop server is missing, as before.</summary>
    public static KitCoopServerPick Pick(string? workshopServer, string? chosenFolder) => Pick(workshopServer, chosenFolder, TaomStack.CoopServerPins);

    internal static KitCoopServerPick Pick(string? workshopServer, string? chosenFolder, IReadOnlyDictionary<string, string> pins)
    {
        if (WorkshopFirst(workshopServer, pins, allowPasswordEdit: true))
            return new KitCoopServerPick(workshopServer, false, null);
        if (!string.IsNullOrWhiteSpace(chosenFolder))
        {
            return Problem(chosenFolder, pins) is { } problem
                ? new KitCoopServerPick(null, true, WithWhereToGet(problem))
                : new KitCoopServerPick(Resolve(chosenFolder), true, null);
        }

        if (workshopServer != null && CoopStaging.NewerCoopProblem(CoopModule(workshopServer), server: true) is { } newer)
            return new KitCoopServerPick(null, false, WithWhereToGet(newer));
        // An older or broken Workshop server gets the messages Prepare gave before.
        return new KitCoopServerPick(workshopServer, false, null);
    }

    /// <summary>True when the Workshop server holds the released Coop 0.1.6 server, so it stays first over a chosen folder.</summary>
    public static bool WorkshopHasKitCoop(string? workshopServer) => WorkshopHasKitCoop(workshopServer, TaomStack.CoopServerPins);

    // The same rule as Pick, so Choose never says the chosen folder is used while Prepare uses a password edited Workshop 0.1.6.
    internal static bool WorkshopHasKitCoop(string? workshopServer, IReadOnlyDictionary<string, string> pins) =>
        WorkshopFirst(workshopServer, pins, allowPasswordEdit: true);

    private static bool WorkshopFirst(string? workshopServer, IReadOnlyDictionary<string, string> pins, bool allowPasswordEdit)
    {
        try
        {
            var check = Check(workshopServer, pins);
            return check == KitCoopServerCheck.Pinned || (allowPasswordEdit && check == KitCoopServerCheck.PasswordEdited);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    /// <summary>The server Start server repairs the server's Coop bin from: a pinned Workshop Coop 0.1.6 server first, like Pick,
    /// then the chosen Coop 0.1.6 server when it is set and pinned, else the Workshop server, as before. A file that can't be read
    /// leaves the Workshop server. EnsurePinnedServerBin and ServerProgramRepair copy only files with the released hashes, so a
    /// Workshop Coop 0.1.7 never lands in a Coop 0.1.6 server.</summary>
    public static string? RepairServer(string? workshopServer, string? chosenFolder) =>
        RepairServer(workshopServer, chosenFolder, TaomStack.CoopServerPins);

    internal static string? RepairServer(string? workshopServer, string? chosenFolder, IReadOnlyDictionary<string, string> pins)
    {
        if (WorkshopFirst(workshopServer, pins, allowPasswordEdit: false))
            return workshopServer;
        try
        {
            return Resolve(chosenFolder) is { } chosen && Check(chosen, pins) == KitCoopServerCheck.Pinned ? chosen : workshopServer;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return workshopServer;
        }
    }

    /// <summary>The kit only checks that its target is apart from the junction path, so a TAOM server or Data folder that is the chosen
    /// server, inside it or holds it would make Prepare copy into the download, maybe into itself. Null when every folder is apart.</summary>
    public static string? OverlapProblem(string server, params string?[] folders) => OverlapWithLink(server, null, folders);

    /// <summary>OverlapProblem with the folder the junction lives in too (linkBase): a chosen server inside it, or holding it,
    /// would put the junction inside the folder it points to.</summary>
    public static string? OverlapWithLink(string server, string? linkBase, params string?[] folders)
    {
        foreach (var folder in folders)
        {
            if (Overlaps(server, folder))
                return Overlap(server, folder!);
        }

        return Overlaps(server, linkBase) ? LinkOverlap(server, linkBase!) : null;
    }

    // Compared as written and where each path really leads, so a junction or symbolic link (on the folder or a parent) to the
    // other folder is caught too. A path whose links can't be followed counts as an overlap: refusing is safe, copying is not.
    internal static bool Overlaps(string server, string? folder)
    {
        if (string.IsNullOrWhiteSpace(folder))
            return false;
        try
        {
            return Nested(Prefix(server), Prefix(folder)) || Nested(Prefix(RealPath(server)), Prefix(RealPath(folder)));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            return true;
        }
    }

    private static bool Nested(string a, string b) =>
        a.StartsWith(b, StringComparison.OrdinalIgnoreCase) || b.StartsWith(a, StringComparison.OrdinalIgnoreCase);

    private static string Prefix(string path) => Path.TrimEndingDirectorySeparator(Path.GetFullPath(path)) + Path.DirectorySeparatorChar;

    /// <summary>Where a path really is: each part that exists and is a junction or symbolic link is replaced by its final target,
    /// from the drive down. Parts that don't exist yet (a server folder Prepare makes) are kept as written.</summary>
    internal static string RealPath(string path)
    {
        var full = Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
        // These throws are never shown: Overlaps turns them into an overlap, so they carry only the path.
        var current = Path.GetPathRoot(full) ?? throw new ArgumentException(path);
        var parts = full[current.Length..].Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries);
        for (var i = 0; i < parts.Length; i++)
        {
            current = Path.Combine(current, parts[i]);
            var info = new DirectoryInfo(current);
            if (!info.Exists)
                return Path.Combine(new[] { current }.Concat(parts.Skip(i + 1)).ToArray());
            if (info.LinkTarget == null)
                continue;
            var target = info.ResolveLinkTarget(returnFinalTarget: true) ?? throw new IOException(current);
            current = Path.TrimEndingDirectorySeparator(StripLongPrefix(target.FullName));
        }

        return current;
    }

    private static string StripLongPrefix(string path) =>
        path.StartsWith(@"\\?\", StringComparison.Ordinal) ? path[4..] : path;

    /// <summary>Why Choose Coop 0.1.6 server refuses a folder, or null. Besides Problem, the folder can't be the TAOM server or Data
    /// folder this tool prepares, inside one or holding one, or Start would repair the server from itself.</summary>
    public static string? ChooseProblem(string? folder, string? serverRoot, string? dataRoot) =>
        ChooseProblem(folder, serverRoot, dataRoot, TaomStack.CoopServerPins);

    internal static string? ChooseProblem(string? folder, string? serverRoot, string? dataRoot, IReadOnlyDictionary<string, string> pins)
    {
        if (Problem(folder, pins) is { } problem)
            return problem;
        var server = Resolve(folder)!;
        foreach (var host in new[] { serverRoot, dataRoot })
        {
            if (Overlaps(server, host))
                return ChoseHostFolder(server, host!);
        }

        return null;
    }

    /// <summary>Prepare-Host.ps1 in the host kit takes only a path ending in steamapps\workshop\content\261550\3770450698\DedicatedServer
    /// and checks the exact Coop 0.1.6 DLLs there. A chosen server reaches it through a junction at this path under baseFolder, so the
    /// kit's own checks still run on the files and the kit script stays as its author made it.</summary>
    public static string KitLinkPath(string baseFolder) =>
        Path.Combine(baseFolder, "steamapps", "workshop", "content", "261550", TaomStack.CoopWorkshopId, "DedicatedServer");

    /// <summary>Runs prepare with the kit's path to a chosen server: refuses folders that overlap it, makes the junction, and removes
    /// the junction afterwards, also when prepare throws.</summary>
    public static void PrepareFromChosen(string server, string linkBase, string serverRoot, string dataRoot, Action<string> prepare, Action<string>? log = null) =>
        PrepareFromChosen(server, linkBase, serverRoot, dataRoot, prepare, log, RemoveLink);

    // removeLink is a parameter so the tests can make the cleanup fail. A cleanup that fails never hides how prepare went: it only
    // writes a line, and the next LinkForKit makes the link again over it.
    internal static void PrepareFromChosen(string server, string linkBase, string serverRoot, string dataRoot, Action<string> prepare, Action<string>? log, Action<string> removeLink)
    {
        if (OverlapWithLink(server, linkBase, serverRoot, dataRoot) is { } overlap)
            throw new InvalidOperationException(overlap);
        var link = LinkForKit(server, linkBase);
        try
        {
            prepare(link);
        }
        finally
        {
            try
            {
                removeLink(link);
                try
                {
                    RemoveEmptyParents(link, linkBase);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    // Only empty folders are left; the next Prepare tries again, so nothing is said.
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                log?.Invoke(LinkNotRemoved(link));
            }
        }
    }

    /// <summary>Deletes the empty folders the link sat in, from its parent up to linkBase\steamapps. Never a folder with anything
    /// in it, never linkBase itself or anything above it.</summary>
    public static void RemoveEmptyParents(string link, string linkBase)
    {
        var stop = Path.TrimEndingDirectorySeparator(Path.GetFullPath(Path.Combine(linkBase, "steamapps")));
        var folder = Path.GetDirectoryName(Path.GetFullPath(link));
        while (folder != null && Prefix(folder).StartsWith(Prefix(stop), StringComparison.OrdinalIgnoreCase))
        {
            if (!Directory.Exists(folder) || Directory.EnumerateFileSystemEntries(folder).Any())
                return;
            Directory.Delete(folder, recursive: false);
            if (folder.Equals(stop, StringComparison.OrdinalIgnoreCase))
                return;
            folder = Path.GetDirectoryName(folder);
        }
    }

    /// <summary>Makes the junction at KitLinkPath(baseFolder) to server and returns its path. A junction needs no admin rights. An old
    /// junction there, to this folder or another, is made again.</summary>
    public static string LinkForKit(string server, string baseFolder)
    {
        // cmd expands %NAME% even inside quotes, so a path with a % could link to another folder.
        if (server.Contains('%'))
            throw new InvalidOperationException(PercentInPath(server));
        var link = KitLinkPath(baseFolder);
        if (link.Contains('%'))
            throw new InvalidOperationException(PercentInLinkPath(link));
        RemoveLink(link);
        if (Directory.Exists(link))
            throw new InvalidOperationException("A folder is in the way of the Coop 0.1.6 server link: " + link);
        Directory.CreateDirectory(Path.GetDirectoryName(link)!);
        var target = Path.GetFullPath(server);
        if (Path.GetPathRoot(target) != target)
            target = Path.TrimEndingDirectorySeparator(target);
        var start = new ProcessStartInfo
        {
            FileName = Path.Combine(Environment.SystemDirectory, "cmd.exe"),
            Arguments = "/d /c mklink /J \"" + link + "\" \"" + target + "\"",
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        string said;
        using (var process = Process.Start(start) ?? throw new InvalidOperationException("cmd.exe did not start."))
        {
            var output = process.StandardOutput.ReadToEndAsync();
            var error = process.StandardError.ReadToEnd();
            process.WaitForExit();
            said = (error + " " + output.Result).Trim();
        }

        if (!File.Exists(Path.Combine(link, ExeName)))
        {
            RemoveLink(link);
            // mklink's own words (in the Windows language) go on their own line, so the host sees why.
            throw new InvalidOperationException(NoLink(link) + (said.Length > 0 ? "\n" + said : ""));
        }

        return link;
    }

    public static string NoLink(string link) => "The link to the Coop 0.1.6 server could not be made: " + link;

    /// <summary>Removes the junction only. Deleting a junction never touches the folder it points to.</summary>
    public static void RemoveLink(string link)
    {
        FileAttributes attributes;
        try
        {
            // Read without following the link, so a junction to a folder that is gone is removed too.
            attributes = File.GetAttributes(link);
        }
        catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException)
        {
            return;
        }

        if (attributes.HasFlag(FileAttributes.ReparsePoint) && attributes.HasFlag(FileAttributes.Directory))
            Directory.Delete(link, recursive: false);
    }

    /// <summary>True when a file has the released hash of the pinned Coop DLL with its name. Start's repair from a chosen folder copies only these.</summary>
    public static bool IsPinnedFile(string path) => IsPinnedFile(path, TaomStack.CoopServerPins);

    internal static bool IsPinnedFile(string path, IReadOnlyDictionary<string, string> pins) =>
        pins.TryGetValue(Path.GetFileName(path), out var sha256) && Matches(path, sha256);

    private static bool Matches(string path, string sha256) =>
        File.Exists(path) && Sha256Files.Hash(path).Equals(sha256, StringComparison.OrdinalIgnoreCase);
}
