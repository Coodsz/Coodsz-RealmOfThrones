namespace TaomCoop.Core;

public static class CoopStaging
{
    public static string? FindWorkshopModule(IEnumerable<string> steamLibraries)
    {
        foreach (var library in steamLibraries)
        {
            var path = Path.Combine(library, "steamapps", "workshop", "content", "261550", TaomStack.CoopWorkshopId);
            if (File.Exists(Path.Combine(path, "SubModule.xml")))
                return path;
        }

        return null;
    }

    public static string? FindDedicatedServer(IEnumerable<string> steamLibraries)
    {
        var module = FindWorkshopModule(steamLibraries);
        if (module == null)
            return null;
        var server = Path.Combine(module, "DedicatedServer");
        return File.Exists(Path.Combine(server, "BannerlordCoopServer.exe")) ? server : null;
    }

    // TAOM-Coop 0.3.25 uses the released Workshop Coop 0.1.6 (module id "Coop").
    // The game loads it from the Workshop folder. A local Modules copy is only
    // kept when it is that same 0.1.6 build; older copies (the 0.1.5 CoopNightly
    // playtest, other Coop versions) are moved out of Modules so they cannot win.
    // Steam has Coop 0.1.7 since 2026-10-30, so these two don't promise 0.1.6 from the Workshop.
    public const string NoPlaytestCoop =
        "Bannerlord Coop from the Steam Workshop is needed. Subscribe to Workshop item 3770450698, let Steam finish the download, then try again.";

    public const string NoPlaytestServerCoop =
        "The dedicated server needs the Coop files from Workshop item 3770450698. In Steam, unsubscribe from it and subscribe again so it downloads again, then prepare the server again.";

    /// <summary>
    /// Why the Workshop Coop can't be used. A newer Coop than the kit was made for (Steam already updated, as with
    /// Coop 0.1.7 on 2026-10-30) can't run TAOM.CoopCompat 0.3.25, which checks the exact Coop 0.1.6 DLLs, so waiting
    /// for Steam would not help. A player unzips the Coop 0.1.6 client from Coop's GitHub v0.1.6 release into
    /// Modules\Coop, which loads before the Workshop copy. A host (server) gets no client advice, since the server
    /// callers add KitCoopServer.WhereToGet for the Coop 0.1.6 dedicated server. An older one does need Steam.
    /// </summary>
    // Each message is one expression, so tools/ExtractUiText finds the three whole texts the languages translate.
    public static string WrongWorkshopCoop(string id, string version, bool server = false) =>
        id.Equals(TaomStack.CoopModuleId, StringComparison.OrdinalIgnoreCase) && IsNewerCoop(version)
            ? server
                ? "The Workshop Coop is " + id + " " + version + ". TAOM.CoopCompat " + TaomStack.CompatVersion.TrimStart('v')
                    + " needs Coop " + TaomStack.CoopVersion.TrimStart('v') + ", and Steam now has that newer Coop."
                : "The Workshop Coop is " + id + " " + version + ". TAOM.CoopCompat " + TaomStack.CompatVersion.TrimStart('v')
                    + " needs Coop " + TaomStack.CoopVersion.TrimStart('v') + ". The Coop 0.1.6 client from Coop's GitHub v0.1.6 release works. Get BannerlordCoop-v0.1.6.zip there and unzip it into the game's Modules folder, so SubModule.xml sits in Modules\\Coop. If a Modules\\Coop folder is already there, move it out first. Then try again."
            : "The Workshop Coop is " + id + " " + version + ". TAOM.CoopCompat " + TaomStack.CompatVersion.TrimStart('v')
                + " needs Coop " + TaomStack.CoopVersion.TrimStart('v') + ". Wait for Steam to update the Workshop item, or in Steam unsubscribe from Workshop item 3770450698 and subscribe again.";

    /// <summary>
    /// A Coop of another version in the game's Modules folder, whatever its folder is called (Modules\Coop.10 from the
    /// ModDB 7z too). It loads before the Workshop Coop, and Use Workshop Coop 0.1.6 moves it out.
    /// </summary>
    public static string ModulesCoopNotPinned(string version, string folder) =>
        "Coop " + version + " in Modules\\" + folder + " is not the Coop " + TaomStack.CoopVersion.TrimStart('v') + " TAOM needs. Open Settings and click Use Workshop Coop 0.1.6. It moves that folder out of Modules, kept, not deleted, so the Workshop Coop loads.";

    /// <summary>True when a Coop version is numerically newer than the one the TAOM kit was made for.</summary>
    public static bool IsNewerCoop(string? version) =>
        Version.TryParse((version ?? "").Trim().TrimStart('v', 'V'), out var found)
        && Version.TryParse(TaomStack.CoopVersion.TrimStart('v', 'V'), out var pinned)
        && found > pinned;

    /// <summary>With a setAsideMarker (the client launcher), other Coop copies are set aside through CoopParking and go
    /// back into Modules when the launcher closes; without one they are moved out for good, as before.</summary>
    public static void UsePlaytestCoop(string workshopModule, string gameRoot, IProgress<string>? progress, string? setAsideMarker = null)
    {
        var workshopXml = Path.Combine(workshopModule, "SubModule.xml");
        if (!File.Exists(workshopXml))
            throw new InvalidOperationException(NoPlaytestCoop);
        var identity = ModuleIdentityReader.Read(workshopXml);
        // A newer Workshop Coop next to a Coop 0.1.6 of the player's own in Modules (the GitHub v0.1.6 zip): that copy
        // already loads first, so the zip advice to move it out would break a working setup. Other Coop copies in Modules
        // (a 0.1.7 in Modules\Coop next to a 0.1.6 in another folder) still go aside the usual way, keeping the 0.1.6 (Codex).
        if (identity.Id.Equals(TaomStack.CoopModuleId, StringComparison.OrdinalIgnoreCase) && IsNewerCoop(identity.Version) && HasLocalKitCoop(gameRoot))
        {
            var others = setAsideMarker == null ? ParkExtraCoop(gameRoot) : CoopParking.SetAside(gameRoot, setAsideMarker);
            foreach (var path in others)
                progress?.Report(setAsideMarker == null
                    ? "Moved " + Path.GetFileName(path) + " out of Modules. The game now uses the Coop 0.1.6 in Modules."
                    : CoopParking.SetAsideLine(path));
            progress?.Report(others.Count == 0
                ? "Coop 0.1.6 is already in the game's Modules folder. It loads before the Workshop's Coop, so nothing needs to change."
                : "Coop 0.1.6 in the game's Modules folder is ready. It loads before the Workshop's Coop.");
            return;
        }
        if (!identity.Id.Equals(TaomStack.CoopModuleId, StringComparison.OrdinalIgnoreCase)
            || !identity.Version.Equals(TaomStack.CoopVersion, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(WrongWorkshopCoop(identity.Id, identity.Version));
        }

        var sourceDll = Path.Combine(workshopModule, "bin", "Win64_Shipping_Client", "Coop.Core.dll");
        if (!IsPinnedCoop(sourceDll))
            throw new InvalidOperationException("The Workshop Coop.Core.dll is not the released Coop 0.1.6 file. In Steam, unsubscribe from Workshop item 3770450698 and subscribe again so it downloads again.");

        var moved = setAsideMarker == null ? ParkExtraCoop(gameRoot) : CoopParking.SetAside(gameRoot, setAsideMarker);
        foreach (var path in moved)
            progress?.Report(setAsideMarker == null
                ? "Moved " + Path.GetFileName(path) + " out of Modules. The game now uses Coop 0.1.6 from the Workshop."
                : CoopParking.SetAsideLine(path));
        progress?.Report(moved.Count == 0
            ? "Bannerlord Coop 0.1.6 from the Workshop is ready. No other Coop copy is in Modules."
            : "Bannerlord Coop 0.1.6 from the Workshop is ready.");
    }

    private static bool IsPlaytestVersion(string moduleDirectory)
    {
        var xml = Path.Combine(moduleDirectory, "SubModule.xml");
        if (!File.Exists(xml))
            return false;
        var identity = ModuleIdentityReader.Read(xml);
        return identity.Id.Equals(TaomStack.CoopModuleId, StringComparison.OrdinalIgnoreCase)
            && identity.Version.Equals(TaomStack.CoopVersion, StringComparison.OrdinalIgnoreCase);
    }

    private static string? ReadVersion(string moduleDirectory)
    {
        var xml = Path.Combine(moduleDirectory, "SubModule.xml");
        if (!File.Exists(xml))
            return null;
        return ModuleIdentityReader.Read(xml).Version;
    }

    /// <summary>True when the game's Modules folder holds a Coop 0.1.6 of the player's own, the copy ExtraCoop keeps.
    /// TAOM can use it while the Workshop is on a newer Coop.</summary>
    public static bool HasLocalKitCoop(string? gameRoot) =>
        gameRoot != null && ReadCoopFolders(Path.Combine(gameRoot, "Modules")).Any(folder => IsPlaytestVersion(folder.Directory));

    /// <summary>The Coop folders ParkExtraCoop would move out of Modules, without moving them (CoopParking asks first).</summary>
    public static IReadOnlyList<CoopFolder> ExtraCoop(string gameRoot) =>
        PlanExtraCopies(Path.Combine(gameRoot, "Modules"), parkLoneCoopId: true);

    public static IReadOnlyList<string> ParkExtraCoop(string gameRoot) =>
        ParkExtraCopies(Path.Combine(gameRoot, "Modules"), gameRoot, parkLoneCoopId: true);

    public static IReadOnlyList<string> ParkDuplicateCoop(string modulesDirectory, string parkRoot) =>
        ParkExtraCopies(modulesDirectory, parkRoot, parkLoneCoopId: false);

    public const string NotInModules = "Bannerlord Coop was not found in this Modules folder.";

    public static string DescribeCopies(string modulesDirectory)
    {
        var found = ReadCoopFolders(modulesDirectory);
        if (found.Count == 0)
            return NotInModules;
        var ids = found.Select(folder => folder.Id).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        if (found.Count == 1)
            return "Bannerlord Coop is in this Modules folder once. Its id is " + found[0].Id + ".";
        if (ids.Count == 1)
            return "Bannerlord Coop is in this Modules folder " + found.Count + " times. Each copy uses id " + ids[0] + ".";
        return "Bannerlord Coop and the old CoopNightly playtest copy are both in this Modules folder.";
    }

    public static string EnsurePinnedServerBin(string serverCoopDirectory, string? workshopCoopDirectory, bool chosenServer = false) =>
        EnsurePinnedServerBin(serverCoopDirectory, workshopCoopDirectory, chosenServer, TaomStack.CoopServerPins);

    // The pins are a parameter so the tests can stand in files of their own for the released Coop 0.1.6 DLLs. A chosen Coop 0.1.6
    // server gives only the DLLs the host kit pins, each with its released hash, so another DLL in that folder never reaches the server.
    internal static string EnsurePinnedServerBin(string serverCoopDirectory, string? workshopCoopDirectory, bool chosenServer, IReadOnlyDictionary<string, string> pins)
    {
        var corePin = pins["Coop.Core.dll"];
        bool IsPinnedCoop(string path) => File.Exists(path) && Sha256Files.Hash(path).Equals(corePin, StringComparison.OrdinalIgnoreCase);
        var serverBin = Path.Combine(serverCoopDirectory, "bin", "Win64_Shipping_Server");
        var dll = Path.Combine(serverBin, "Coop.Core.dll");
        if (IsPinnedCoop(dll))
            return "Bannerlord Coop is already on this server, so the game's client copy was not used.";

        var backup = dll + ".pre-password.bak";
        if (File.Exists(dll) && File.Exists(backup) && IsPinnedCoop(backup) && RestoreQualifiedDll(dll, corePin))
            return "Coop.Core.dll was put back to the released Coop 0.1.6 file. A password edit crashes this server.";

        var sourceBin = workshopCoopDirectory == null
            ? null
            : Path.Combine(workshopCoopDirectory, "bin", "Win64_Shipping_Server");
        var sourceDll = sourceBin == null ? null : Path.Combine(sourceBin, "Coop.Core.dll");
        if (sourceDll == null || !IsPinnedCoop(sourceDll))
        {
            // A newer Workshop Coop (Steam already has Coop 0.1.7) is no download to verify, so the host gets the Coop 0.1.6 server advice.
            if (NewerCoopProblem(workshopCoopDirectory, server: true) is { } newer)
                throw new InvalidOperationException(KitCoopServer.WithWhereToGet(newer));
            if (!File.Exists(dll))
                throw new InvalidOperationException("Coop.Core.dll is missing: " + dll + ". " + NoPlaytestServerCoop);
            throw new InvalidOperationException("The server Coop.Core.dll is not the released Coop 0.1.6 file. " + NoPlaytestServerCoop);
        }

        // A Workshop Steam is part way through updating can still have 0.1.6's Coop.Core.dll next to newer files, so a source whose
        // SubModule.xml names another Coop is refused before anything is copied.
        if (IsOtherCoopVersion(workshopCoopDirectory))
        {
            var identity = ModuleIdentityReader.Read(Path.Combine(workshopCoopDirectory!, "SubModule.xml"));
            throw new InvalidOperationException(KitCoopServer.WithWhereToGet(WrongWorkshopCoop(identity.Id, identity.Version, server: true)));
        }

        // A DLL the host kit pins is copied only with its released hash, from either source. Other DLLs come only from the Workshop, as before.
        Directory.CreateDirectory(serverBin);
        foreach (var file in Directory.EnumerateFiles(sourceBin!, "*.dll"))
        {
            var copy = pins.ContainsKey(Path.GetFileName(file)) ? KitCoopServer.IsPinnedFile(file, pins) : !chosenServer;
            if (copy)
                File.Copy(file, Path.Combine(serverBin, Path.GetFileName(file)), overwrite: true);
        }
        if (!IsPinnedCoop(dll))
            throw new InvalidOperationException(chosenServer
                ? "The Coop.Core.dll from your Coop 0.1.6 server folder did not stay the released Coop 0.1.6 file."
                : "The Workshop server Coop.Core.dll did not stay the released Coop 0.1.6 file.");
        return chosenServer
            ? "The dedicated server Coop files were put back from your Coop 0.1.6 server folder. The game's client files were not used."
            : "The dedicated server Coop files were put back from the Workshop server copy. The game's client files were not used.";
    }

    private static IReadOnlyList<string> ParkExtraCopies(string modulesDirectory, string parkRoot, bool parkLoneCoopId) =>
        PlanExtraCopies(modulesDirectory, parkLoneCoopId).Select(folder => MoveAside(folder, parkRoot)).ToList();

    private static List<CoopFolder> PlanExtraCopies(string modulesDirectory, bool parkLoneCoopId)
    {
        if (!Directory.Exists(modulesDirectory))
            return [];

        var found = ReadCoopFolders(modulesDirectory);
        var keep = found
            .Where(folder => IsPlaytestVersion(folder.Directory))
            .OrderBy(folder => folder.Name.Equals("Coop", StringComparison.OrdinalIgnoreCase) ? 0 : 1)
            .ThenBy(folder => folder.Name, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault();
        if (!parkLoneCoopId && found.Count <= 1)
            return [];
        if (keep == null && !parkLoneCoopId)
        {
            keep = found
                .OrderBy(folder => folder.Name.Equals("Coop", StringComparison.OrdinalIgnoreCase) ? 0 : 1)
                .ThenBy(folder => folder.Name, StringComparer.OrdinalIgnoreCase)
                .FirstOrDefault();
        }

        return found
            .Where(folder => keep == null || !folder.Directory.Equals(keep.Directory, StringComparison.OrdinalIgnoreCase))
            .ToList();
    }

    private static List<CoopFolder> ReadCoopFolders(string modulesDirectory)
    {
        var found = new List<CoopFolder>();
        if (!Directory.Exists(modulesDirectory))
            return found;
        foreach (var directory in Directory.EnumerateDirectories(modulesDirectory))
        {
            var xml = Path.Combine(directory, "SubModule.xml");
            if (!File.Exists(xml))
                continue;
            var identity = ModuleIdentityReader.Read(xml);
            if (!identity.Id.Equals("Coop", StringComparison.OrdinalIgnoreCase)
                && !identity.Id.Equals("CoopNightly", StringComparison.OrdinalIgnoreCase))
                continue;
            found.Add(new CoopFolder(directory, new DirectoryInfo(directory).Name, identity.Id, identity.Version));
        }

        return found;
    }

    private static string MoveAside(CoopFolder folder, string parkRoot)
    {
        var kept = KeptPath(folder, parkRoot);
        Directory.Move(folder.Directory, kept);
        return kept;
    }

    /// <summary>Where MoveAside puts a folder: Name-kept-version in parkRoot, or Name-kept-date when that one is taken.</summary>
    public static string KeptPath(CoopFolder folder, string parkRoot)
    {
        var version = string.IsNullOrWhiteSpace(folder.Version) ? "" : folder.Version.TrimStart('v', 'V');
        var kept = Path.Combine(parkRoot, folder.Name + "-kept-" + (version.Length == 0 ? "old" : version));
        if (Directory.Exists(kept))
            kept = Path.Combine(parkRoot, folder.Name + "-kept-" + DateTime.Now.ToString("yyyyMMdd-HHmmss"));
        return kept;
    }

    /// <summary>WrongWorkshopCoop for a Coop module folder (its SubModule.xml) newer than the kit's Coop, else null. Verifying
    /// Bannerlord would only download that newer Coop again. A server caller (server: true) adds KitCoopServer.WhereToGet itself.</summary>
    public static string? NewerCoopProblem(string? coopModuleDirectory, bool server = false)
    {
        var xml = coopModuleDirectory == null ? null : Path.Combine(coopModuleDirectory, "SubModule.xml");
        if (xml == null || !File.Exists(xml))
            return null;
        // One that can't be read now (locked) gives no advice: moving a folder another program holds would fail anyway.
        if (ModuleIdentityReader.TryRead(xml) is not { } identity)
            return null;
        return identity.Id.Equals(TaomStack.CoopModuleId, StringComparison.OrdinalIgnoreCase) && IsNewerCoop(identity.Version)
            ? WrongWorkshopCoop(identity.Id, identity.Version, server)
            : null;
    }

    private static bool IsPinnedCoop(string path) =>
        File.Exists(path) && Sha256Files.Hash(path).Equals(TaomStack.CoopCoreSha256, StringComparison.OrdinalIgnoreCase);

    public sealed record CoopFolder(string Directory, string Name, string Id, string Version);

    /// <summary>True when a module folder's SubModule.xml names a Coop version other than the kit's (0.1.7 since
    /// 2026-10-30). A folder without a SubModule.xml has no version to go by and returns false.</summary>
    public static bool IsOtherCoopVersion(string? moduleDirectory) =>
        moduleDirectory != null && ReadVersion(moduleDirectory) is { } version
        && !version.Equals(TaomStack.CoopVersion, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Puts the released Coop 0.1.6 Coop.Core.dll back from its .pre-password.bak. Coop and Friends before 1.2.28 left
    /// that .bak in the Workshop folder and Steam's update to Coop 0.1.7 keeps it, so a module whose SubModule.xml names
    /// another Coop version is refused before anything is copied: 0.1.6's DLL would break that Coop. Only the server tool gets
    /// that refusal (the client launcher skips a Coop of another version), so it says where to get the Coop 0.1.6 server.
    /// </summary>
    public static bool RestoreQualifiedDll(string dllPath) => RestoreQualifiedDll(dllPath, TaomStack.CoopCoreSha256);

    // The pin is a parameter so the tests can stand in a file of their own for the released Coop 0.1.6 DLL.
    internal static bool RestoreQualifiedDll(string dllPath, string pinnedSha256)
    {
        if (!File.Exists(dllPath))
            throw new InvalidOperationException("Coop.Core.dll is missing: " + dllPath);
        if (Sha256Files.Hash(dllPath).Equals(pinnedSha256, StringComparison.OrdinalIgnoreCase))
            return false;

        var module = Path.GetDirectoryName(Path.GetDirectoryName(Path.GetDirectoryName(dllPath)));
        if (IsOtherCoopVersion(module))
        {
            var identity = ModuleIdentityReader.Read(Path.Combine(module!, "SubModule.xml"));
            throw new InvalidOperationException(KitCoopServer.WithWhereToGet(WrongWorkshopCoop(identity.Id, identity.Version, server: true)));
        }

        var backup = dllPath + ".pre-password.bak";
        if (!File.Exists(backup) || !Sha256Files.Hash(backup).Equals(pinnedSha256, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Coop.Core.dll is not the released Coop 0.1.6 file and no original copy was found. In Steam, unsubscribe from Workshop item 3770450698 and subscribe again so it downloads again.");

        var parked = dllPath + ".coodsz-password";
        if (!File.Exists(parked))
            File.Copy(dllPath, parked, overwrite: false);
        File.Copy(backup, dllPath, overwrite: true);
        return true;
    }

    public static void RestoreKeptCoop(string gameRoot, IProgress<string>? progress)
    {
        var modules = Path.Combine(gameRoot, "Modules");
        var kept = FindKeptCoop(gameRoot);
        if (kept == null)
            throw new InvalidOperationException("No saved Coop folder was found.");

        var current = Path.Combine(modules, "Coop");
        if (Directory.Exists(current))
        {
            var parked = Path.Combine(gameRoot, "Coop-kept-" + (ReadVersion(current) ?? "old").TrimStart('v'));
            if (Directory.Exists(parked))
                parked = Path.Combine(gameRoot, "Coop-kept-" + DateTime.Now.ToString("yyyyMMdd-HHmmss"));
            Directory.Move(current, parked);
        }

        var version = ReadVersion(kept);
        Directory.Move(kept, current);
        progress?.Report(version == null
            ? "Modules\\Coop is now the saved Coop folder."
            : "Modules\\Coop is now Coop " + version.TrimStart('v') + ".");
    }

    private static string? FindKeptCoop(string gameRoot)
    {
        var found = new List<string>();
        if (Directory.Exists(gameRoot))
            found.AddRange(Directory.EnumerateDirectories(gameRoot, "Coop-kept-*"));
        var modules = Path.Combine(gameRoot, "Modules");
        if (Directory.Exists(modules))
            found.AddRange(Directory.EnumerateDirectories(modules, "Coop-kept-*"));
        return found.OrderByDescending(path => path).FirstOrDefault();
    }
}
