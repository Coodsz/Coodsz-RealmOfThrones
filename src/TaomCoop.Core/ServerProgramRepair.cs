using System.Text.Json;
using System.Text.RegularExpressions;
using BannerlordCoopLauncher.Core.IO;
using BannerlordCoopLauncher.Core.Server;

namespace TaomCoop.Core;

public enum ServerProgramState
{
    Ready,
    NotPrepared,
    ServerRunning,
    NeedsRepair
}

public sealed record ServerProgramRepairResult(IReadOnlyList<string> Restored, IReadOnlyList<string> StillMissing)
{
    public bool Repaired => StillMissing.Count == 0;
}

/// <summary>
/// A prepared server folder can lose BannerlordCoopServer.exe (antivirus quarantine is the usual
/// suspect). Prepare server refuses a folder that is not empty, so Start server puts back only the
/// program files Stage-TaomCompatServer.ps1 copied from the host kit's TAOM Coop wrapper, each checked
/// against the hash in WRAPPER-ARTIFACT.json, the engine bin DLLs the staging moved there, the module
/// DLLs it copied from the game's Modules folder and the TAOM.CoopCompat DLLs it copied from the host kit,
/// each checked against a pinned hash. It never overwrites
/// a file, except a TAOM.CoopCompat file that is not the pinned one, which it replaces from the host kit, and it
/// never writes into the Data folder, so the campaign, saves, configs and every file already in
/// the TAOM modules stay as they are. The TAOM.CoopCompat v2 of the new TAOM download (NewTaomCompat) is never
/// touched. The Workshop server's own
/// BannerlordCoopServer.exe is a different program and is never used here.
/// </summary>
public static class ServerProgramRepair
{
    public const string ExeName = "BannerlordCoopServer.exe";
    public const string ManifestName = "WRAPPER-ARTIFACT.json";

    // Wrapper payload path (as written in WRAPPER-ARTIFACT.json) and where Stage-TaomCompatServer.ps1
    // puts it in the server folder. DedicatedServer.Core.dll goes to two places.
    public static readonly (string Payload, string Target)[] Files =
    {
        (ExeName, ExeName),
        ("EULA.txt", "EULA.txt"),
        ("LICENSE.txt", "LICENSE.txt"),
        ("Modules/DedicatedServer.Windows/SubModule.xml", Path.Combine("engine", "Modules", "DedicatedServer.Windows", "SubModule.xml")),
        ("Modules/DedicatedServer.Windows/bin/Win64_Shipping_Server/DedicatedServer.Windows.dll", Path.Combine("engine", "Modules", "DedicatedServer.Windows", "bin", "Win64_Shipping_Server", "DedicatedServer.Windows.dll")),
        ("Modules/DedicatedServer.Windows/bin/Win64_Shipping_Server/DedicatedServer.Core.dll", Path.Combine("engine", "Modules", "DedicatedServer.Windows", "bin", "Win64_Shipping_Server", "DedicatedServer.Core.dll")),
        ("Modules/DedicatedServer.Windows/bin/Win64_Shipping_Server/DedicatedServer.Core.dll", Path.Combine("engine", "bin", "Win64_Shipping_Server", "DedicatedServer.Core.dll"))
    };

    public const string HarmonyName = "0Harmony.dll";

    public static readonly string EngineBin = Path.Combine("engine", "bin", "Win64_Shipping_Server");

    // Stage-TaomCompatServer.ps1 (section 5) moves 0Harmony.dll and the view and GauntletUI DLLs out of
    // the Coop module bin into the main engine bin, so after Prepare they live only there. Without them
    // the server dies 2 seconds in while UIExtenderEx loads (player report CR-WRTH2). The list is what
    // the Coop 0.1.6 Workshop server's Coop bin holds that its main bin does not. 0Harmony.dll is the
    // staging script's own reviewed pin; the others are the Workshop 0.1.6 copies the staging moved.
    public static readonly (string Name, long Length, string Sha256)[] EngineBinFiles =
    {
        ("0Harmony.dll", 2333696, "12EED32DE73312B376F616EE8659BA5D3766D96859181728437FA85C6B5CE309"),
        ("TaleWorlds.GauntletUI.dll", 333784, "3FE02171A2A8B455C0922689A24669AE91ABE7914895B21F269897197433DB1E"),
        ("TaleWorlds.GauntletUI.PrefabSystem.dll", 61912, "78422B519733EF27DDC4ADE743381A286E51975997511C83A77055B634E015ED"),
        ("TaleWorlds.GauntletUI.Data.dll", 46040, "FCD480210708F45850EE794CF59EC139C0E6AEF77669C7370914D3B038C97D8C"),
        ("TaleWorlds.GauntletUI.ExtraWidgets.dll", 75736, "8CA50050F4F67F7506588B26C16C7CBCE83D9165A1E5DB70EFB4320422AC235E"),
        ("TaleWorlds.Engine.GauntletUI.dll", 41944, "8F96A2C31F04F294194F4C59867935D80008723758350997404710B220E290CF"),
        ("TaleWorlds.MountAndBlade.GauntletUI.dll", 213976, "75B070E0B1347C3F6C7E4526EDC5370BBA18B4D4B4747C115669E23477AD44E2"),
        ("TaleWorlds.MountAndBlade.GauntletUI.Widgets.dll", 677336, "3828067E5BC61521D399BB237CAC6488C7C999B045C2436E86906E1232BD9DBE"),
        ("TaleWorlds.MountAndBlade.View.dll", 405464, "FFAAC5ED867CD299F8C14F58FFEE64F86E50D8B197DB5F966759524C3E574BA1"),
        ("TaleWorlds.MountAndBlade.ViewModelCollection.dll", 499672, "C7052D125863B9DBC898F6F95762F1B7754F25133A5BC3AAF3B87E8E85FD7ACB"),
        ("TaleWorlds.CampaignSystem.ViewModelCollection.dll", 1534936, "5D817A46456D07B5E4382A870795FD9F8D6F11B4707A91D8A8B5D5CD50E762A5"),
        ("TaleWorlds.Core.ViewModelCollection.dll", 75736, "5A48A04FF42B1B448FB8DEF314608A402FD4F872174C3A4CD858D5E866F44B23"),
        ("TaleWorlds.TwoDimension.dll", 103896, "228A7534D395D5337887A438EFDC9DF533A1AFD46D888AF9FEFAED5A4705998E"),
        ("SandBox.View.dll", 418264, "C7360E71DA06799A0CB6C00C4A0CFAB5877621197BA25101433AC28654D169E2"),
        ("SandBox.GauntletUI.dll", 247768, "69F2A87C3C3883D04ECE46200C5CDDF39C188F0EE7C20E26C47FCF64C0F97ED3"),
        ("SandBox.ViewModelCollection.dll", 283096, "8C4B2CA448A3BC8789A6F8D7B4427E7A9832A31C45DD79E9034173535EBAEBF4")
    };

    // Stage-TaomCompatServer.ps1 copies these from the game's Modules folder: StoryMode and CustomBattle
    // from their client bin (section 2), the TAOM modules' bins (section 1), and it puts MCMv5.dll and
    // DryIoc.dll from those bins into the main engine bin (section 5). Every prepared server has them, and
    // without them TAOM's types fail to load 3 seconds in, before any save is read (player report
    // CR-KFRG6). Source is relative to the game's Modules folder. The pins are Bannerlord v1.4.8 and TAOM
    // v2.0.29.5, the versions Prepare-Host.ps1 requires; TAOM.dll and the server TAOM.Dependencies.dll
    // are its own pins.
    public static readonly (string Target, string Source, long Length, string Sha256)[] ModuleFiles =
    {
        (Path.Combine("engine", "Modules", "StoryMode", "bin", "Win64_Shipping_Server", "StoryMode.dll"),
            Path.Combine("StoryMode", "bin", "Win64_Shipping_Client", "StoryMode.dll"),
            535000, "CC205186BCA26EA04197C543F06BFD14EC7CA8A94B559A3A2753921C4BB27EF0"),
        (Path.Combine("engine", "Modules", "CustomBattle", "bin", "Win64_Shipping_Server", "TaleWorlds.MountAndBlade.CustomBattle.dll"),
            Path.Combine("CustomBattle", "bin", "Win64_Shipping_Client", "TaleWorlds.MountAndBlade.CustomBattle.dll"),
            141784, "8073B0AE60CAA77A7E3BE5FE892BDCC427E11337C7F1338743F857B9B30EBB25"),
        (Path.Combine("engine", "Modules", "TAOM.Dependencies", "bin", "Win64_Shipping_Server", "TAOM.Dependencies.dll"),
            Path.Combine("TAOM.Dependencies", "bin", "Win64_Shipping_Server", "TAOM.Dependencies.dll"),
            119808, "21F792903622417872A04D9DFA2819C824A3647C1716DF7B9055048C54D7EC7B"),
        (Path.Combine("engine", "Modules", "TAOM.Dependencies", "bin", "Win64_Shipping_Server", "Bannerlord.UIExtenderEx.dll"),
            Path.Combine("TAOM.Dependencies", "bin", "Win64_Shipping_Server", "Bannerlord.UIExtenderEx.dll"),
            268288, "489BCF6CE0963CD57BE019B3FA7C12ECB7EB786A689B211BD4C23FF1B85D85B0"),
        (Path.Combine("engine", "Modules", "TAOM.Dependencies", "bin", "Win64_Shipping_Server", "MCMv5.dll"),
            Path.Combine("TAOM.Dependencies", "bin", "Win64_Shipping_Server", "MCMv5.dll"),
            495104, "BE8FDE024957C97F0CF08ED813D2EF0A069F1D3E3F4C54F4557DA4083CBA3B15"),
        (Path.Combine("engine", "Modules", "TAOM", "bin", "Win64_Shipping_Server", "TAOM.dll"),
            Path.Combine("TAOM", "bin", "Win64_Shipping_Client", "TAOM.dll"),
            3230720, "AEEC07AA61C84F45E9E8662C294CFF68B000F041D8FCA453CF30058CB9AD14D8"),
        (Path.Combine("engine", "Modules", "TAOM", "bin", "Win64_Shipping_Server", "DryIoc.dll"),
            Path.Combine("TAOM", "bin", "Win64_Shipping_Client", "DryIoc.dll"),
            579584, "EC66E544EECC57CA75DF1E3761F0F86EF76583849F45347E9FB553CAF4B9D5D2"),
        (Path.Combine("engine", "bin", "Win64_Shipping_Server", "MCMv5.dll"),
            Path.Combine("TAOM.Dependencies", "bin", "Win64_Shipping_Server", "MCMv5.dll"),
            495104, "BE8FDE024957C97F0CF08ED813D2EF0A069F1D3E3F4C54F4557DA4083CBA3B15"),
        (Path.Combine("engine", "bin", "Win64_Shipping_Server", "DryIoc.dll"),
            Path.Combine("TAOM", "bin", "Win64_Shipping_Client", "DryIoc.dll"),
            579584, "EC66E544EECC57CA75DF1E3761F0F86EF76583849F45347E9FB553CAF4B9D5D2")
    };

    // Stage-TaomCompatServer.ps1 ("staging exact TAOM.CoopCompat artifact") copies the host kit's
    // TAOM.CoopCompat module whole. With one of these gone, its preflight stops every start with "required
    // TAOM.CoopCompat feature DLL is missing" (player report CR-HZYX5). Source is relative to the kit's
    // TAOM.CoopCompat module (TaomServerFiles.KitCompatModule). The pins are the kit 0.3.25 copies, the
    // hashes its ARTIFACT.json and the wrapper pin. A changed DLL stops it with "feature DLL hash ... does not
    // match the wrapper pin", a missing SubModule.xml with "required TAOM.CoopCompat module descriptor is
    // missing" (player report CR-XXP67).
    public static readonly (string Target, string Source, long Length, string Sha256)[] CompatFiles =
    {
        (Path.Combine("engine", "Modules", TaomStack.CompatModuleId, "SubModule.xml"), "SubModule.xml",
            2108, "EB46CE58436D7FC61BAFEEA15FED14C6712960B439F3FFF3AB2246EA39814E08"),
        (Path.Combine("engine", "Modules", TaomStack.CompatModuleId, "bin", "Win64_Shipping_Server", "TAOM.CoopCompat.dll"),
            Path.Combine("bin", "Win64_Shipping_Server", "TAOM.CoopCompat.dll"),
            1227776, "E0B27271032D062DE45A0E29A31C52A278EF9680A80AC11EBD1311FE6D84B21A"),
        (Path.Combine("engine", "Modules", TaomStack.CompatModuleId, "bin", "Win64_Shipping_Server", "TAOM.CoopCompat.Bootstrap.dll"),
            Path.Combine("bin", "Win64_Shipping_Server", "TAOM.CoopCompat.Bootstrap.dll"),
            18432, "B105EAC2EE225BEF5AC02C2BBD98F5C7D5D1D5DE546558C98F825B9CB4800C0B")
    };

    private static readonly Regex MissingAssembly = new(
        @"Could not load file or assembly '(?<path>[^']*\\engine\\bin\\Win64_Shipping_Server\\(?<name>[^'\\]+))'",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    // workshopServer is the Workshop Coop DedicatedServer folder. A missing engine bin DLL needs repair
    // only when it is 0Harmony.dll or the Workshop server has its pinned copy (BlockingEngineBinFiles).
    // gameModules is the game's Modules folder. A missing module DLL needs repair only when the game has
    // its source (BlockingModuleFiles). compatByHash also counts a TAOM.CoopCompat file that is not the pinned one.
    public static ServerProgramState Check(string? serverRoot, bool serverRunning, string? workshopServer = null, string? gameModules = null,
        bool compatByHash = false)
    {
        if (string.IsNullOrWhiteSpace(serverRoot) || !Directory.Exists(serverRoot) || !Directory.EnumerateFileSystemEntries(serverRoot).Any())
            return ServerProgramState.NotPrepared;
        if (serverRunning)
            return ServerProgramState.ServerRunning;
        return Files.Any(file => !File.Exists(Path.Combine(serverRoot, file.Target))) || BlockingEngineBinFiles(serverRoot, workshopServer).Count > 0
                || BlockingModuleFiles(serverRoot, gameModules).Count > 0 || MissingCompatFiles(serverRoot, byHash: compatByHash).Count > 0
            ? ServerProgramState.NeedsRepair
            : ServerProgramState.Ready;
    }

    public static readonly string CompatFolder = Path.Combine("engine", "Modules", TaomStack.CompatModuleId);

    /// <summary>
    /// The CompatFiles targets that are not in the server folder, and with byHash also the ones that are there but
    /// not the pinned file. Every prepared server has them and the server never starts without them, so each one
    /// stops the start when Repair cannot put it back. None for the TAOM.CoopCompat v2 of the new TAOM download
    /// (NewTaomCompat), which Repair leaves alone.
    /// </summary>
    public static IReadOnlyList<string> MissingCompatFiles(string? serverRoot,
        IReadOnlyList<(string Target, string Source, long Length, string Sha256)>? pins = null, bool byHash = false) =>
        string.IsNullOrWhiteSpace(serverRoot) || NewTaomCompat.Problem(Path.Combine(serverRoot, CompatFolder)) != null
            ? Array.Empty<string>()
            : (pins ?? CompatFiles).Where(file => !File.Exists(Path.Combine(serverRoot, file.Target))
                    || byHash && !Matches(Path.Combine(serverRoot, file.Target), (file.Sha256, file.Length)))
                .Select(file => file.Target).ToList();

    /// <summary>The ModuleFiles targets that are not in the server folder.</summary>
    public static IReadOnlyList<string> MissingModuleFiles(string? serverRoot,
        IReadOnlyList<(string Target, string Source, long Length, string Sha256)>? pins = null) =>
        string.IsNullOrWhiteSpace(serverRoot)
            ? Array.Empty<string>()
            : (pins ?? ModuleFiles).Where(file => !File.Exists(Path.Combine(serverRoot, file.Target))).Select(file => file.Target).ToList();

    /// <summary>
    /// The missing ModuleFiles targets whose source is in the game's Modules folder, so a healthy server
    /// for this game and TAOM version has them. These stop the start when Repair cannot put them back, and
    /// a run that dies with one gone never loaded the save. A server staged by an older host kit or another
    /// TAOM version can lack a name the game's Modules folder lacks too; that one never blocks.
    /// </summary>
    public static IReadOnlyList<string> BlockingModuleFiles(string? serverRoot, string? gameModules,
        IReadOnlyList<(string Target, string Source, long Length, string Sha256)>? pins = null) =>
        string.IsNullOrWhiteSpace(serverRoot) || string.IsNullOrWhiteSpace(gameModules)
            ? Array.Empty<string>()
            : (pins ?? ModuleFiles).Where(file => !File.Exists(Path.Combine(serverRoot, file.Target)) && File.Exists(Path.Combine(gameModules, file.Source)))
                .Select(file => file.Target).ToList();

    /// <summary>
    /// What a server exit says about missing server files. Only a crash before serving that this tool did
    /// not ask for counts. EngineBin holds the EngineBinFiles names the log says could not load (MissingInLog),
    /// Modules the BlockingModuleFiles targets on disk. With either one, the save never loaded, so there is no
    /// load mode retry and no Coop Save Rescue. With none, the exit goes on to the load crash path.
    /// </summary>
    public static (IReadOnlyList<string> EngineBin, IReadOnlyList<string> Modules) MissingAtExit(ServerExitInfo exit, bool servedThisRun,
        string? log, string? serverRoot, string? gameModules,
        IReadOnlyList<(string Target, string Source, long Length, string Sha256)>? modulePins = null)
    {
        if (!exit.IsCrash || servedThisRun)
            return (Array.Empty<string>(), Array.Empty<string>());
        return (MissingInLog(log), BlockingModuleFiles(serverRoot, gameModules, modulePins));
    }

    public static IReadOnlyList<string> MissingEngineBinFiles(string serverRoot) =>
        EngineBinFiles.Where(file => !File.Exists(Path.Combine(serverRoot, EngineBin, file.Name))).Select(file => file.Name).ToList();

    // The staging moves only the names the main bin lacks, so a healthy server can lack one of these.
    // Start stops only for 0Harmony.dll (the staging refuses to stage without it) or for a name the
    // Workshop server has a pinned copy of, which Repair puts back. Any other missing name is noted
    // and the server starts; a real failure shows up in the exit check (MissingInLog).
    public static IReadOnlyList<string> BlockingEngineBinFiles(string serverRoot, string? workshopServer,
        IReadOnlyList<(string Name, long Length, string Sha256)>? pins = null) =>
        (pins ?? EngineBinFiles).Where(file => !File.Exists(Path.Combine(serverRoot, EngineBin, file.Name))
                && (file.Name == HarmonyName || FindBinSource(workshopServer, file) != null))
            .Select(file => file.Name).ToList();

    public static bool IsWrapperFile(string target) => Files.Any(file => file.Target == target);

    /// <summary>A file Repair takes from the host kit: a wrapper file or a TAOM.CoopCompat DLL.</summary>
    public static bool IsKitFile(string target) => IsWrapperFile(target) || CompatFiles.Any(file => file.Target == target);

    /// <summary>
    /// The EngineBinFiles DLLs a run's log says the engine could not load from
    /// engine\bin\Win64_Shipping_Server and that are not there now. A server that dies on those never
    /// loaded the save, so the exit is no load crash and Coop Save Rescue cannot help. Other names are
    /// left out, since UIExtenderEx probing can log a file that never existed.
    /// </summary>
    public static IReadOnlyList<string> MissingInLog(string? log)
    {
        if (string.IsNullOrEmpty(log))
            return Array.Empty<string>();
        var names = new List<string>();
        foreach (Match match in MissingAssembly.Matches(log))
        {
            var name = match.Groups["name"].Value;
            if (!EngineBinFiles.Any(file => file.Name.Equals(name, StringComparison.OrdinalIgnoreCase))
                || names.Contains(name, StringComparer.OrdinalIgnoreCase))
                continue;
            try
            {
                if (File.Exists(match.Groups["path"].Value))
                    continue;
            }
            catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
            {
            }

            names.Add(name);
        }

        return names;
    }

    // Same rule as Prepare-Host.ps1's Resolve-SingleArtifact: exactly one wrapper folder with its manifest.
    public static string? FindWrapperArtifact(string kitRoot)
    {
        var parent = Path.Combine(kitRoot, "Artifacts", "TAOM-Coop-Wrapper");
        if (!Directory.Exists(parent))
            return null;
        var folders = Directory.GetDirectories(parent);
        if (folders.Length != 1 || !File.Exists(Path.Combine(folders[0], ManifestName)))
            return null;
        return folders[0];
    }

    // workshopServer is the Workshop Coop DedicatedServer folder. The engine bin DLLs come back only from
    // there and only with the pinned length and hash, so a client built Harmony is never used. gameModules
    // is the game's Modules folder: the ModuleFiles come back only from there, also with their pins. Tests
    // pass their own pins. compatModule is the host kit's TAOM.CoopCompat module (TaomServerFiles.KitCompatModule):
    // the CompatFiles come back only from there, with their pins. With it, a CompatFiles file that is not the pinned
    // one is replaced too, so a file is only ever swapped for the kit copy. log gets a line when that swap fails.
    public static ServerProgramRepairResult Repair(string serverRoot, string? dataRoot, string? wrapperArtifact, string? workshopServer = null,
        IReadOnlyList<(string Name, long Length, string Sha256)>? pins = null, string? gameModules = null,
        IReadOnlyList<(string Target, string Source, long Length, string Sha256)>? modulePins = null, string? compatModule = null,
        IReadOnlyList<(string Target, string Source, long Length, string Sha256)>? compatPins = null, Action<string>? log = null)
    {
        var restored = new List<string>();
        var missing = Files.Where(file => !File.Exists(Path.Combine(serverRoot, file.Target))).ToList();
        var missingBin = (pins ?? EngineBinFiles).Where(file => !File.Exists(Path.Combine(serverRoot, EngineBin, file.Name))).ToList();
        var missingModule = (modulePins ?? ModuleFiles).Where(file => !File.Exists(Path.Combine(serverRoot, file.Target))).ToList();
        var wrongCompat = MissingCompatFiles(serverRoot, compatPins, byHash: compatModule != null);
        var missingCompat = (compatPins ?? CompatFiles).Where(file => wrongCompat.Contains(file.Target)).ToList();
        if (missing.Count == 0 && missingBin.Count == 0 && missingModule.Count == 0 && missingCompat.Count == 0)
            return new ServerProgramRepairResult(restored, Array.Empty<string>());

        // A Data folder inside the server folder (or the other way round) is never written to.
        var stillMissing = new List<string>();
        if (Overlaps(dataRoot, serverRoot))
            return new ServerProgramRepairResult(restored, missing.Select(file => file.Target).Concat(missingBin.Select(file => Path.Combine(EngineBin, file.Name)))
                .Concat(missingModule.Select(file => file.Target)).Concat(missingCompat.Select(file => file.Target)).ToList());

        var hashes = wrapperArtifact == null ? null : ReadManifest(Path.Combine(wrapperArtifact, ManifestName));
        foreach (var (payload, target) in missing)
        {
            var source = wrapperArtifact == null ? null : Path.Combine(wrapperArtifact, payload.Replace('/', Path.DirectorySeparatorChar));
            if (source != null && hashes != null && hashes.TryGetValue(payload, out var expected) && Matches(source, expected)
                && CopyChecked(source, Path.Combine(serverRoot, target), expected))
                restored.Add(target);
            else
                stillMissing.Add(target);
        }

        // The staging takes each one from the Workshop server's main bin, or moves it out of its Coop bin.
        foreach (var (name, length, sha256) in missingBin)
        {
            var target = Path.Combine(EngineBin, name);
            var source = FindBinSource(workshopServer, (name, length, sha256));
            if (source != null && CopyChecked(source, Path.Combine(serverRoot, target), (sha256, length)))
                restored.Add(target);
            else
                stillMissing.Add(target);
        }

        // The same file the staging copied, taken from the game's Modules folder only when it is the pinned one.
        foreach (var (target, relative, length, sha256) in missingModule)
        {
            var source = gameModules == null ? null : Path.Combine(gameModules, relative);
            if (source != null && Matches(source, (sha256, length)) && CopyChecked(source, Path.Combine(serverRoot, target), (sha256, length)))
                restored.Add(target);
            else
                stillMissing.Add(target);
        }

        // The same TAOM.CoopCompat file the staging copied from the host kit, only when it is the pinned one.
        foreach (var (target, relative, length, sha256) in missingCompat)
        {
            var source = compatModule == null ? null : Path.Combine(compatModule, relative);
            var destination = Path.Combine(serverRoot, target);
            if (source != null && Matches(source, (sha256, length))
                && (File.Exists(destination) ? ReplaceChecked(source, destination, (sha256, length), log) : CopyChecked(source, destination, (sha256, length))))
                restored.Add(target);
            else
                stillMissing.Add(target);
        }

        return new ServerProgramRepairResult(restored, stillMissing);
    }

    // The pinned copy goes in beside the changed file first, so the changed one stays when the copy fails.
    private static bool ReplaceChecked(string source, string destination, (string Sha256, long Length) expected, Action<string>? log)
    {
        var fresh = destination + ".coodsz-repair";
        TryDelete(fresh);
        if (!CopyChecked(source, fresh, expected))
            return false;
        try
        {
            File.Move(fresh, destination, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            log?.Invoke("Could not replace " + destination + ". " + ex.Message);
            TryDelete(fresh);
            return false;
        }

        return Matches(destination, expected);
    }

    private static string? FindBinSource(string? workshopServer, (string Name, long Length, string Sha256) file) =>
        workshopServer == null
            ? null
            : new[] { Path.Combine(workshopServer, EngineBin, file.Name), Path.Combine(workshopServer, "engine", "Modules", "Coop", "bin", "Win64_Shipping_Server", file.Name) }
                .FirstOrDefault(path => Matches(path, (file.Sha256, file.Length)));

    private static bool CopyChecked(string source, string destination, (string Sha256, long Length) expected)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            File.Copy(source, destination, overwrite: false);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // A copy that stopped part way (full disk, antivirus) leaves a file that only looks present;
            // it was missing before and overwrite is off, so the half file is this repair's own.
            TryDelete(destination);
            return false;
        }

        // Antivirus can take the copy away again at once. A copy that does not match is removed,
        // so the next Start tries again instead of running it.
        if (Matches(destination, expected))
            return true;
        TryDelete(destination);
        return false;
    }

    private static Dictionary<string, (string Sha256, long Length)>? ReadManifest(string path)
    {
        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(path));
            if (!document.RootElement.TryGetProperty("files", out var files) || files.ValueKind != JsonValueKind.Array)
                return null;
            var hashes = new Dictionary<string, (string, long)>(StringComparer.Ordinal);
            foreach (var entry in files.EnumerateArray())
            {
                if (entry.TryGetProperty("path", out var relative) && relative.ValueKind == JsonValueKind.String
                    && entry.TryGetProperty("sha256", out var sha) && sha.ValueKind == JsonValueKind.String
                    && entry.TryGetProperty("length", out var length) && length.TryGetInt64(out var size))
                    hashes[relative.GetString()!] = (sha.GetString()!, size);
            }

            return hashes;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            return null;
        }
    }

    // A Data folder path that cannot be read (bad characters in the settings) counts as overlapping, so nothing is written.
    private static bool Overlaps(string? dataRoot, string serverRoot)
    {
        try
        {
            return DedicatedServerWatch.IsUnder(dataRoot, serverRoot) || DedicatedServerWatch.IsUnder(serverRoot, dataRoot);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return true;
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }

    private static bool Matches(string path, (string Sha256, long Length) expected)
    {
        try
        {
            return File.Exists(path) && new FileInfo(path).Length == expected.Length
                && string.Equals(FileHash.TrySha256(path), expected.Sha256, StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }
}
