using BannerlordCoopLauncher.Core.IO;
using BannerlordCoopLauncher.Core.Parsing;
using BannerlordCoopLauncher.Core.Steam;

namespace BannerlordCoopLauncher.Core.Modules;

/// <summary>One Bannerlord Coop module on this PC: its folder and the version in its SubModule.xml.</summary>
public sealed record CoopCopy(string Directory, string Version, bool InModules);

/// <summary>
/// Where Bannerlord Coop is on this PC. Coop 0.1.7 came out on Coop's GitHub release and ModDB before the
/// Steam Workshop, so players unzip it into the game's Modules folder by hand (Modules\Coop, id "Coop").
/// The "CoopNightly" id is skipped unless a caller opts in with nightly: the Coop and Friends tools take the Coop nightly
/// (built for Bannerlord 1.5.7) as their Coop, while TAOM and Burning Empires keep skipping it.
/// </summary>
public static class CoopInstall
{
    public const string WorkshopId = SteamWorkshopAcf.CoopWorkshopId;
    public const string ReleaseUrl = "https://github.com/Bannerlord-Coop-Team/BannerlordCoop/releases/latest";

    /// <summary>
    /// True while Coop 0.1.7 is only on Coop's GitHub release and ModDB. Every "not on Steam yet" text keys off this
    /// (ClientGetLineNow, ClientMissingLine, ServerGetLineNow, CoodszHost's DedicatedServerFinder.MissingMessage), so
    /// setting it to false once the Workshop item has Coop 0.1.7 drops them together.
    /// </summary>
    public const bool NotOnSteamYet = false;

    // Shown where Coop is missing while Coop 0.1.7 is only on GitHub and ModDB (NotOnSteamYet).
    public const string ClientGetLine =
        "Coop v0.1.7 is not on Steam yet. Get BannerlordCoop-v0.1.7.zip from Coop's GitHub release and unzip it into the Bannerlord Modules folder, so it ends up in Modules\\Coop.";

    // The Coop and Friends client's Coop row when no Coop is found: Install mods still gets the Workshop Coop.
    // One text, not two joined, so the window translates it whole.
    public const string ClientMissingLine = !NotOnSteamYet
        ? "Click Install mods, then subscribe in Steam."
        : "Click Install mods, then subscribe in Steam. Coop v0.1.7 is not on Steam yet. Get BannerlordCoop-v0.1.7.zip from Coop's GitHub release and unzip it into the Bannerlord Modules folder, so it ends up in Modules\\Coop.";

    public static string? GitHubClientInstallLine(string? gameRoot, IFileSystem? files = null) =>
        NewerGame.Line(NewerGame.NativeVersion(gameRoot, files), coopTools: true);

    // A short folder, since the server engine crashes from a deep path (0xC0000409 at Module Initialize).
    public static string ServerGetLine(string button) =>
        "For the server, click Download Coop server under Advanced: server folder on the Settings page to get the Coop v0.1.7 dedicated server download (about 4.5 GB), linked at the bottom of Coop's release notes, not under Assets. Unzip it to a short folder like C:\\CoopServer, then click " + button + ".";

    /// <summary>ClientGetLine while Coop 0.1.7 is not on Steam yet, else null.</summary>
    public static string? ClientGetLineNow => NotOnSteamYet ? ClientGetLine : null;

    /// <summary>ServerGetLine while Coop 0.1.7 is not on Steam yet, else null.</summary>
    public static string? ServerGetLineNow(string button) => NotOnSteamYet ? ServerGetLine(button) : null;

    /// <summary>
    /// A message with a get-it line under it, on a line of its own, or the message alone when line is null. The
    /// window's translator takes each line as a whole text, so both keep their translations; joined into one
    /// sentence chain neither would match a key.
    /// </summary>
    public static string AddLine(string message, string? line) => string.IsNullOrEmpty(line) ? message : message + "\n" + line;

    /// <summary>The first Coop version that is the nightly built for Bannerlord 1.5.7 (Id CoopNightly, v0.1.8).</summary>
    public const string NightlyVersion = "v0.1.8";

    /// <summary>True for the Coop nightly: Id CoopNightly, or a Coop of v0.1.8 or newer.</summary>
    public static bool IsNightly(string? id, string? version) =>
        string.Equals(id, BannerlordCoopModules.CoopNightly, StringComparison.OrdinalIgnoreCase)
        || (BannerlordCoopModules.IsEither(id) && Compare(version, NightlyVersion) >= 0);

    /// <summary>The Bannerlord version the Coop nightly in this folder was made for (its Native DependentVersion), "" when
    /// that can't be read, or null when the folder holds no Coop nightly. NewerGame takes it.</summary>
    public static string? NightlyFor(string? directory)
    {
        if (string.IsNullOrWhiteSpace(directory))
            return null;
        var manifest = Parsing.ModuleManifestReader.Read(directory);
        return manifest != null && IsNightly(manifest.Id, manifest.Version) ? manifest.NativeVersion ?? "" : null;
    }

    /// <summary>True when the Coop in this folder is a nightly build (Id CoopNightly, or Nightly in its name), not Coop's
    /// official 0.1.8 or newer (Id Coop, "Bannerlord Co-op"). Only the lines that name the Coop say "nightly"; every rule
    /// goes by IsNightly, which both pass.</summary>
    public static bool IsNightlyBuild(string? directory)
    {
        var manifest = string.IsNullOrWhiteSpace(directory) ? null : Parsing.ModuleManifestReader.Read(directory);
        return manifest != null && (string.Equals(manifest.Id, BannerlordCoopModules.CoopNightly, StringComparison.OrdinalIgnoreCase)
            || (BannerlordCoopModules.IsEither(manifest.Id) && (manifest.Name ?? "").Contains("Nightly", StringComparison.OrdinalIgnoreCase)));
    }

    /// <summary>
    /// The one rule for a Coop nightly in the game's Modules folder next to a Workshop Coop: it wins when the setup names
    /// CoopNightly, or when it was made for the installed game (its Native DependentVersion is the game's Native version)
    /// and is not older than the Workshop Coop. ModuleResolver picks the Coop the client loads by it, and LoadsNightly
    /// offers War Sails by it, so the two always agree.
    /// </summary>
    public static bool NightlyBeatsWorkshop(bool picked, string? nightlyMadeFor, string? nightlyVersion, string? gameNative, string? workshopVersion) =>
        picked || (Compare(nightlyVersion, workshopVersion) >= 0 && Steam.NewerGame.SameGame(nightlyMadeFor, gameNative));

    // "nightly v0.1.8", so the Active Coop and Check mods lines name the nightly.
    private static string Label(string version, bool nightly) => nightly ? "nightly " + version : version;

    /// <summary>The Check mods line that names the Coop the game loads and where it comes from.</summary>
    public static string LoadedLine(string version, bool inModules, bool nightly = false) => inModules
        ? "Bannerlord Coop " + Label(version, nightly) + " from the game's Modules folder is the one the game loads."
        : "Bannerlord Coop " + Label(version, nightly) + " from the Steam Workshop is the one the game loads.";

    /// <summary>The Coop client's Active Coop line on Play: the Coop the game loads and where it comes from.</summary>
    public static string ActiveClientLine(string version, bool inModules, bool nightly = false) => inModules
        ? "Active Coop: " + Label(version, nightly) + ", from the game's Modules folder."
        : "Active Coop: " + Label(version, nightly) + ", from the Steam Workshop.";

    /// <summary>The Active Coop line on Play when the game has no Bannerlord Coop yet.</summary>
    public const string ActiveClientMissing = "Active Coop: not installed.";

    /// <summary>The Server Tool's Active Coop line, with where its server comes from: the Steam Workshop item, the
    /// game's Modules\Bannerlord Coop folder, or a server folder the host chose (Set server folder).</summary>
    public static string ActiveServerLine(string coop, string? serverRoot)
    {
        var path = (serverRoot ?? "").Replace('/', '\\') + "\\";
        return path.Contains(@"\workshop\content\261550\" + WorkshopId + @"\", StringComparison.OrdinalIgnoreCase)
            ? "Active Coop: " + coop + ", from the Steam Workshop server."
            : path.Contains(@"\Modules\Bannerlord Coop\", StringComparison.OrdinalIgnoreCase)
            ? "Active Coop: " + coop + ", from the game's Modules folder."
            : "Active Coop: " + coop + ", from the server folder you chose.";
    }

    /// <summary>True when the server folder is one the host chose (Set server folder): not the Workshop item's server
    /// and not the game's Modules\Bannerlord Coop one. The same split as ActiveServerLine.</summary>
    public static bool IsChosenServer(string? serverRoot)
    {
        if (string.IsNullOrWhiteSpace(serverRoot))
            return false;
        var path = serverRoot.Replace('/', '\\') + "\\";
        return !path.Contains(@"\workshop\content\261550\" + WorkshopId + @"\", StringComparison.OrdinalIgnoreCase)
            && !path.Contains(@"\Modules\Bannerlord Coop\", StringComparison.OrdinalIgnoreCase);
    }

    public const string UseSteamServerButton = "Use the Steam server";

    /// <summary>
    /// The Server Tool's line under Active Coop when the server folder the host chose has an older Coop than the Workshop
    /// item's DedicatedServer, or null. After Steam updated Coop, a host kept the ModDB server he had picked and could not
    /// see how to go back (Vanquish). Use the Steam server then points the tool at the Workshop server. Nothing when no
    /// folder was chosen, there is no Workshop server, or a version can't be read. With <paramref name="ad1259"/> also
    /// nothing while the Workshop's Coop is newer than the Coop AD1259 pins (AD1259Stack), so the button never moves an
    /// AD1259 host off his pinned Coop. With <paramref name="ad1259"/> a picked Coop nightly server (AD1259Stack.IsNightlyCoop)
    /// gets the line too, since the AD1259 tool refuses to start it and Use the Steam server is the way back.
    /// <paramref name="nightly"/> is the Server Tool's own nightly answer (AD1259Server.IsNightlyServer), for a nightly that
    /// only shows in the server's Common.dll build or as a lone CoopNightly module (no Coop version then).
    /// </summary>
    public static string? ChosenServerOlderLine(string? serverRoot, string? serverCoop, string? workshopCoop, bool ad1259 = false, bool nightly = false)
    {
        if (!IsChosenServer(serverRoot)
            || (ad1259 && AD1259.AD1259Stack.IsNewerThanPinned(AD1259.AD1259Stack.Find(BannerlordCoopModules.Coop)!, workshopCoop)))
            return null;
        if (KnownNewer(workshopCoop, serverCoop))
            return "Your picked server folder has Coop " + serverCoop + ". Steam has Coop " + workshopCoop + ".";
        return ad1259 && !string.IsNullOrWhiteSpace(workshopCoop) && (nightly || AD1259.AD1259Stack.IsNightlyCoop(serverCoop))
            ? "Your picked server folder has the Coop nightly. Steam has Coop " + workshopCoop + "."
            : null;
    }

    /// <summary>
    /// Why Use the Steam server leaves the server folder as it is, or null. The Server Tool reads the Workshop server's Coop
    /// again right before it saves, since Steam can update it after the line was shown. With <paramref name="ad1259"/> a Coop
    /// newer than the one AD1259 pins, or one that can't be read, is refused, so the AD1259 tool never saves a server it won't start.
    /// </summary>
    public static string? UseSteamServerProblem(string? workshopCoop, bool ad1259)
    {
        if (!ad1259)
            return null;
        if (string.IsNullOrWhiteSpace(workshopCoop))
            return "The Coop version of the Steam Workshop server could not be read, so the server folder stays as it is. Click Use the Steam server again in a moment.";
        var pinned = AD1259.AD1259Stack.Find(BannerlordCoopModules.Coop)!;
        return AD1259.AD1259Stack.IsNewerThanPinned(pinned, workshopCoop)
            ? "The Steam Workshop server now has Coop " + workshopCoop + ", newer than the Coop " + pinned.Version + " AD1259 needs, so the server folder stays as it is."
            : null;
    }

    /// <summary>
    /// The Server Tool's line under Active Coop when the game's Modules\Coop is newer than the server's Coop, or
    /// null. A host who unzipped client Coop 0.1.7 into Modules still has the Workshop's 0.1.6 server, and Coop refuses
    /// players whose Coop differs from the server's, so the server needs Coop's own server download (Download Coop server, then Set server folder).
    /// An unreadable version on either side gives nothing.
    /// </summary>
    public static string? ServerBehindLine(string? gameCoop, string? serverCoop) => KnownNewer(gameCoop, serverCoop)
        ? "The server runs Coop " + serverCoop + ", older than Coop " + gameCoop + " in the game's Modules folder. Click Download Coop server for the matching Coop dedicated server download, linked at the bottom of Coop's release notes, not under Assets. Unzip it to a short folder, then click Set server folder."
        : null;

    /// <summary>The Coop row's detail in the Coop and Friends launcher: the version and where it comes from.</summary>
    public static string PinnedDetail(string version, bool inModules) => string.IsNullOrWhiteSpace(version)
        ? "The launcher skips Steam's version check for Coop. Your Coop version still needs to match the server."
        : inModules
        ? "Coop " + version + " from the game's Modules folder. The launcher skips Steam's version check for Coop. Your Coop version still needs to match the server."
        : "Coop " + version + " from the Steam Workshop. The launcher skips Steam's version check for Coop. Your Coop version still needs to match the server.";

    /// <summary>True when the folder is inside the game's Modules folder (the copy the game loads first).</summary>
    public static bool IsInModules(string? directory, string? gameRoot)
    {
        if (string.IsNullOrWhiteSpace(directory) || string.IsNullOrWhiteSpace(gameRoot))
            return false;
        try
        {
            var modules = Path.GetFullPath(Path.Combine(gameRoot, "Modules")).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            return Path.GetFullPath(directory).StartsWith(modules, StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return false;
        }
    }

    // ----- Coop unzipped by hand: the Mark of the Web, one folder too deep, a wrong folder name, loose files -----

    public const string NestedLine =
        "Coop is one folder too deep. Move the inner Coop folder up so SubModule.xml sits directly in Modules\\Coop.";

    /// <summary>A Coop in a folder with another name (the ModDB 7z has no Coop folder, so "Extract to Coop.10\" gives one).</summary>
    public static string MisnamedLine(string folder) =>
        "Coop is in Modules\\" + folder + ". Rename that folder to Coop.";

    /// <summary>A Coop wrapped in another folder while Modules\Coop holds another mod.</summary>
    public static string WrappedTakenLine(string folder) =>
        "Modules\\Coop holds another mod. Move that mod out of Modules first, then move Modules\\" + folder + "\\Coop into Modules.";

    /// <summary>A Coop inside a folder with another name (Modules\BannerlordCoop-v0.1.7\Coop).</summary>
    public static string WrappedLine(string folder) =>
        "Coop is in Modules\\" + folder + "\\Coop. Move that Coop folder into Modules, so it becomes Modules\\Coop.";

    /// <summary>A Coop in a folder with another name while Modules\Coop holds another mod.</summary>
    public static string TakenLine(string folder) =>
        "Modules\\Coop holds another mod. Move that mod out of Modules first, then rename Modules\\" + folder + " to Coop.";

    /// <summary>A second Coop beside the real Modules\Coop.</summary>
    public static string DuplicateLine(string folder) =>
        "Modules\\" + folder + " is a second copy of Coop. Move it out of the Modules folder, so only Modules\\Coop is left.";

    /// <summary>The ModDB 7z extracted straight into Modules ("Extract here").</summary>
    public const string LooseLine =
        "Coop's files were extracted straight into Modules. Make a folder named Coop in Modules and move SubModule.xml, bin, GUI, ModuleData and the other Coop files into it.";

    /// <summary>The log line after clearing download marks; null when nothing was marked.</summary>
    public static string? UnblockedLine(int count) => count <= 0
        ? null
        : count == 1
            ? "Unblocked 1 Coop file that Windows had marked as downloaded from the internet."
            : "Unblocked " + count + " Coop files that Windows had marked as downloaded from the internet.";

    /// <summary>
    /// Clears the Windows download mark (the Zone.Identifier stream) under the game's Modules\Coop folder, when it exists,
    /// and under a Coop that sits in the wrong place (MisplacedCoopFolders: a folder with another name, a second copy, a
    /// Coop wrapped in another folder), so it runs even before the player renames it. Players unzip Coop 0.1.7 themselves,
    /// and the game's .NET Framework client will not load a DLL Windows marked as downloaded (ModUnblock). The paths are
    /// built on every call, so a folder renamed to Coop is swept next time. Loose files straight in Modules are never
    /// swept, and Modules itself never is. A folder that can't be read gives 0. With <paramref name="nightly"/> a
    /// CoopNightly counts as Coop (the Coop and Friends tools).
    /// </summary>
    public static int ClearModulesCoopMarks(string? gameRoot, bool nightly = false)
    {
        if (string.IsNullOrWhiteSpace(gameRoot))
            return 0;
        var count = ClearMarks(Path.Combine(gameRoot, "Modules", BannerlordCoopModules.Coop));
        foreach (var folder in MisplacedCoopFolders(gameRoot, nightly))
            count += ClearMarks(folder);
        return count;
    }

    /// <summary>
    /// The folders under Modules that hold Coop by their SubModule.xml Id but are not Modules\Coop: Modules\Coop.10 and the
    /// like (also a second copy beside Modules\Coop) and Modules\&lt;name&gt;\Coop. Never Modules itself and never a broad folder.
    /// </summary>
    public static IReadOnlyList<string> MisplacedCoopFolders(string? gameRoot, bool nightly = false)
    {
        var found = new List<string>();
        if (string.IsNullOrWhiteSpace(gameRoot))
            return found;
        try
        {
            var modules = Path.Combine(gameRoot, "Modules");
            if (!Directory.Exists(modules))
                return found;
            foreach (var folder in Directory.GetDirectories(modules))
            {
                if (Path.GetFileName(folder).Equals(BannerlordCoopModules.Coop, StringComparison.OrdinalIgnoreCase))
                    continue;
                var candidate = Read(folder, inModules: true, nightly) != null ? folder
                    : Read(Path.Combine(folder, BannerlordCoopModules.Coop), inModules: true, nightly) != null ? Path.Combine(folder, BannerlordCoopModules.Coop)
                    : null;
                if (candidate == null || IsBroadFolder(candidate))
                    continue;
                // A junction or link under Modules is not followed, the top folder and an inner Coop alike, so the sweep
                // never reaches what it points at. (A junctioned Modules\Coop itself is still swept: that is the Coop that loads.)
                if (IsLink(folder) || IsLink(candidate))
                    continue;
                found.Add(candidate);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
        }

        return found;
    }

    /// <summary>The same for a Coop server folder, recursive. Only a real server folder (IsServerFolder) is swept.</summary>
    public static int ClearServerMarks(string? serverFolder) =>
        IsServerFolder(serverFolder) ? ClearMarks(serverFolder!) : 0;

    // A bad saved path or a locked file never escapes into a window's click handler; DownloadMark keeps going past one
    // locked mark and returns the count it cleared.
    private static int ClearMarks(string directory)
    {
        try
        {
            if (!Directory.Exists(directory))
                return 0;
            return Support.DownloadMark.Clear(directory);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or ArgumentException or NotSupportedException)
        {
            return 0;
        }
    }

    public const string ServerExeName = "BannerlordCoopServer.exe";

    /// <summary>The line when a picked folder holds no Coop dedicated server (the Burning Empires and Coop Server Tools).</summary>
    public const string NotServerFolder =
        "That folder has no Coop dedicated server. Pick the folder you unzipped BannerlordCoop-DedicatedServer-Win64-v0.1.7.7z to, the one with BannerlordCoopServer.exe and the engine folder in it.";

    /// <summary>
    /// True for a folder no server folder can be: a drive root, the user's profile folder or a folder above it, and the
    /// Desktop, Documents and Downloads folders themselves. The tools sweep a server folder recursively, so these are refused.
    /// It compares paths only and doesn't resolve junctions; that is harmless, since a sweep also needs the exe and the
    /// engine folder in the folder itself (IsServerFolder).
    /// </summary>
    public static bool IsBroadFolder(string? folder)
    {
        if (string.IsNullOrWhiteSpace(folder))
            return true;
        try
        {
            var full = Trim(Path.GetFullPath(folder));
            var root = Path.GetPathRoot(full);
            if (root == null || full.Equals(Trim(root), StringComparison.OrdinalIgnoreCase))
                return true;
            var profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            if (profile.Length > 0)
            {
                var home = Trim(Path.GetFullPath(profile));
                if (home.Equals(full, StringComparison.OrdinalIgnoreCase) || home.StartsWith(full + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                    return true;
                if (full.Equals(Trim(Path.Combine(home, "Downloads")), StringComparison.OrdinalIgnoreCase))
                    return true;
            }

            foreach (var special in new[] { Environment.SpecialFolder.Desktop, Environment.SpecialFolder.DesktopDirectory, Environment.SpecialFolder.MyDocuments })
            {
                var path = Environment.GetFolderPath(special);
                if (path.Length > 0 && full.Equals(Trim(Path.GetFullPath(path)), StringComparison.OrdinalIgnoreCase))
                    return true;
            }

            return false;
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException or System.Security.SecurityException)
        {
            return true;
        }

        static string Trim(string path) => path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
    }

    /// <summary>A Coop dedicated server folder: BannerlordCoopServer.exe and the engine folder in it, and not a broad folder.</summary>
    public static bool IsServerFolder(string? folder)
    {
        try
        {
            return !IsBroadFolder(folder) && File.Exists(Path.Combine(folder!, ServerExeName)) && Directory.Exists(Path.Combine(folder!, "engine"));
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException)
        {
            return false;
        }
    }

    /// <summary>
    /// The server folder (IsServerFolder) in a folder the host picked: the folder itself, its DedicatedServer folder, or,
    /// for a package unzipped one folder too deep (C:\CoopServer\BannerlordCoop-DedicatedServer-Win64-v0.1.7), the one
    /// folder inside it that holds the server. Null when none does, or when several folders inside do.
    /// </summary>
    public static string? ServerFolderIn(string? folder)
    {
        if (string.IsNullOrWhiteSpace(folder))
            return null;
        try
        {
            if (!Directory.Exists(folder))
                return null;
            foreach (var candidate in new[] { folder, Path.Combine(folder, "DedicatedServer") })
            {
                if (IsServerFolder(candidate))
                    return candidate;
            }

            if (IsBroadFolder(folder))
                return null;
            var inside = Directory.GetDirectories(folder)
                .SelectMany(child => new[] { child, Path.Combine(child, "DedicatedServer") })
                .Where(IsServerFolder)
                .ToList();
            return inside.Count == 1 ? inside[0] : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            return null;
        }
    }

    /// <summary>True when Modules\Coop has no SubModule.xml but a folder one or two levels inside it holds Coop (the zip
    /// unzipped one folder too deep: Modules\Coop\Coop or Modules\Coop\BannerlordCoop-v0.1.7\Coop).</summary>
    public static bool IsNested(string? gameRoot, bool nightly = false)
    {
        if (string.IsNullOrWhiteSpace(gameRoot))
            return false;
        try
        {
            var coop = Path.Combine(gameRoot, "Modules", BannerlordCoopModules.Coop);
            if (!Directory.Exists(coop) || File.Exists(Path.Combine(coop, "SubModule.xml")))
                return false;
            return NestedCopies(coop, nightly).Any();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            return false;
        }
    }

    // The Coops one or two levels inside Modules\Coop.
    private static IEnumerable<CoopCopy> NestedCopies(string coop, bool nightly) =>
        Directory.GetDirectories(coop)
            .Select(child => Read(child, inModules: true, nightly) ?? Read(Path.Combine(child, BannerlordCoopModules.Coop), inModules: true, nightly))
            .OfType<CoopCopy>();

    /// <summary>
    /// What is wrong with how Coop sits in the game's Modules folder, read from each SubModule.xml's Id, not folder names:
    /// one folder too deep, a folder with another name (or a second copy beside Modules\Coop), a Coop wrapped in another
    /// folder, and loose files straight in Modules. Nothing is moved or renamed; each problem gets one line.
    /// With <paramref name="onlyVersion"/> (TAOM's pinned Coop), a misplaced Coop of another version gets no line that
    /// would make it Modules\Coop: that would load the wrong Coop. A second copy beside Modules\Coop still gets its line.
    /// With <paramref name="nightly"/> a CoopNightly is Coop too, so Modules\Coop holding the nightly is no other mod.
    /// </summary>
    public static IReadOnlyList<string> LayoutProblems(string? gameRoot, string? onlyVersion = null, bool nightly = false)
    {
        // By number, so "0.1.6" and "v0.1.6.0" count as TAOM's "v0.1.6".
        bool Wanted(CoopCopy? copy) => copy != null && (onlyVersion == null || Compare(copy.Version, onlyVersion) == 0);
        var lines = new List<string>();
        if (string.IsNullOrWhiteSpace(gameRoot))
            return lines;
        try
        {
            var modules = Path.Combine(gameRoot, "Modules");
            if (!Directory.Exists(modules))
                return lines;
            var real = Read(Path.Combine(modules, BannerlordCoopModules.Coop), inModules: true, nightly) != null;
            // Coop and CoopNightly are two ids the game can hold side by side (the launcher names one), so only a copy with
            // the same id as Modules\Coop is a second copy.
            var realIsCoop = Read(Path.Combine(modules, BannerlordCoopModules.Coop), inModules: true) != null;
            // Modules\Coop holds another mod (its SubModule.xml reads, with another Id): renaming onto it is not possible yet.
            // A SubModule.xml there that can't be read is neither, so no rename or move line is given then.
            var coopXml = Path.Combine(modules, BannerlordCoopModules.Coop, "SubModule.xml");
            var otherId = OtherId(coopXml, nightly);
            var taken = !real && otherId == true;
            var unknown = !real && otherId == null && File.Exists(coopXml);
            if (IsNested(gameRoot, nightly) && (onlyVersion == null || NestedCopies(Path.Combine(modules, BannerlordCoopModules.Coop), nightly).Any(Wanted)))
                lines.Add(NestedLine);
            // Several misplaced copies of one id and no Modules\Coop (Coop.10, Coop.11, a wrapped one): only the newest gets
            // the rename or move line, every other one the second copy line, so the player keeps one Coop.
            var misplaced = new List<(string Name, CoopCopy Copy, bool Wrapped)>();
            foreach (var folder in Directory.GetDirectories(modules))
            {
                var name = Path.GetFileName(folder);
                if (name.Equals(BannerlordCoopModules.Coop, StringComparison.OrdinalIgnoreCase))
                    continue;
                if (unknown)
                    continue;
                var copy = Read(folder, inModules: true, nightly);
                if (copy != null)
                {
                    if (real)
                    {
                        if ((Read(folder, inModules: true) != null) == realIsCoop)
                            lines.Add(DuplicateLine(name));
                    }
                    else if (Wanted(copy))
                        misplaced.Add((name, copy, false));
                }
                else if (!real && Read(Path.Combine(folder, BannerlordCoopModules.Coop), inModules: true, nightly) is { } wrapped && Wanted(wrapped))
                    misplaced.Add((name, wrapped, true));
            }

            foreach (var group in misplaced.GroupBy(item => Read(item.Copy.Directory, inModules: true) != null))
            {
                var keep = group.OrderByDescending(item => item.Copy.Version, Comparer<string?>.Create(Compare))
                    .ThenBy(item => item.Wrapped).ThenBy(item => item.Name, StringComparer.OrdinalIgnoreCase).First();
                foreach (var item in group)
                {
                    lines.Add(item != keep ? DuplicateLine(item.Name)
                        : item.Wrapped ? (taken ? WrappedTakenLine(item.Name) : WrappedLine(item.Name))
                        : taken ? TakenLine(item.Name) : MisnamedLine(item.Name));
                }
            }

            var loose = Read(modules, inModules: true, nightly);
            var looseDll = File.Exists(Path.Combine(modules, "bin", "Win64_Shipping_Client", "Coop.dll"))
                || File.Exists(Path.Combine(modules, "bin", "Win64_Shipping_Client", "Coop.Core.dll"));
            // Loose DLLs with no readable SubModule.xml are a Coop of unknown version: no line when only one version is wanted.
            if (loose != null ? Wanted(loose) : looseDll && onlyVersion == null)
                lines.Add(LooseLine);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
        }

        return lines;
    }

    // True when a SubModule.xml reads with an Id other than Coop, false when it reads as Coop, null when it is missing or unreadable.
    private static bool? OtherId(string xml, bool nightly)
    {
        try
        {
            if (!File.Exists(xml) || !SubModuleXml.TryRead(File.ReadAllText(xml), out var info, out _) || info == null)
                return null;
            return !IsCoopId(info.Id, nightly);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static bool IsLink(string directory)
    {
        try
        {
            return Directory.Exists(directory) && new DirectoryInfo(directory).Attributes.HasFlag(FileAttributes.ReparsePoint);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return true;
        }
    }

    /// <summary>What every client launcher does on Check mods and before it starts the game: clear the download marks in
    /// Modules\Coop and name a Coop that sits in the wrong place (LayoutProblems, <paramref name="onlyVersion"/> for TAOM).
    /// The lines to log, none when all is well.</summary>
    public static IReadOnlyList<string> CheckModulesCoop(string? gameRoot, string? onlyVersion = null, bool nightly = false)
    {
        var lines = new List<string>();
        if (UnblockedLine(ClearModulesCoopMarks(gameRoot, nightly)) is { } unblocked)
            lines.Add(unblocked);
        lines.AddRange(LayoutProblems(gameRoot, onlyVersion, nightly));
        return lines;
    }

    /// <summary>
    /// The Coop in the game's Modules folder (any folder whose SubModule.xml has id Coop, the one named Coop first),
    /// or null. The game loads a Modules copy of a module before the Workshop copy, so with both this is what runs.
    /// With <paramref name="nightly"/> a CoopNightly counts too.
    /// </summary>
    public static CoopCopy? InModules(string? gameRoot, bool nightly = false)
    {
        if (string.IsNullOrWhiteSpace(gameRoot))
            return null;
        var modules = Path.Combine(gameRoot, "Modules");
        if (!Directory.Exists(modules))
            return null;
        string[] folders;
        try
        {
            folders = Directory.GetDirectories(modules);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }

        return folders
            .OrderBy(folder => Path.GetFileName(folder).Equals(BannerlordCoopModules.Coop, StringComparison.OrdinalIgnoreCase) ? 0 : 1)
            .ThenBy(folder => Path.GetFileName(folder), StringComparer.OrdinalIgnoreCase)
            .Select(folder => Read(folder, inModules: true, nightly))
            .FirstOrDefault(copy => copy != null);
    }

    /// <summary>The Coop in this folder, or null when its SubModule.xml is missing, unreadable or another id. Only the
    /// Coop and Friends tools pass <paramref name="nightly"/>, so TAOM and Burning Empires still skip a CoopNightly.</summary>
    public static CoopCopy? Read(string directory, bool inModules, bool nightly = false)
    {
        try
        {
            var xml = Path.Combine(directory, "SubModule.xml");
            if (!File.Exists(xml) || !SubModuleXml.TryRead(File.ReadAllText(xml), out var info, out _) || info == null
                || !IsCoopId(info.Id, nightly))
                return null;
            return new CoopCopy(directory, info.Version ?? "", inModules);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static bool IsCoopId(string id, bool nightly) =>
        nightly ? BannerlordCoopModules.IsEither(id) : id.Equals(BannerlordCoopModules.Coop, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Compares two Coop versions by number, ignoring a leading v ("v0.1.7" is newer than "0.1.6" and the same
    /// as "0.1.7.0"). A version that is not numbers (empty, garbled) sorts before every real one.
    /// </summary>
    public static int Compare(string? left, string? right)
    {
        var a = Parts(left);
        var b = Parts(right);
        if (a == null || b == null)
            return (a == null ? 0 : 1) - (b == null ? 0 : 1);
        for (var index = 0; index < Math.Max(a.Count, b.Count); index++)
        {
            var x = index < a.Count ? a[index] : 0;
            var y = index < b.Count ? b[index] : 0;
            if (x != y)
                return x.CompareTo(y);
        }

        return 0;
    }

    public static bool IsNewer(string? version, string? than) => Compare(version, than) > 0;

    /// <summary>True when both versions are numbers and the first is newer. An unreadable version is unknown, so
    /// nothing is called older or newer than it (a Coop whose SubModule.xml could not be read is never replaced
    /// as if it were old).</summary>
    public static bool KnownNewer(string? version, string? than) =>
        Parts(version) != null && Parts(than) != null && Compare(version, than) > 0;

    private static List<long>? Parts(string? version)
    {
        var text = (version ?? "").Trim().TrimStart('v', 'V');
        if (text.Length == 0)
            return null;
        var parts = new List<long>();
        foreach (var part in text.Split('.'))
        {
            if (!long.TryParse(part, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var value))
                return null;
            parts.Add(value);
        }

        return parts;
    }
}
