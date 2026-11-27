using BannerlordCoopLauncher.Core.Launch;
using BannerlordCoopLauncher.Core.Modules;
using BannerlordCoopLauncher.Core.Profile;
using BannerlordCoopLauncher.Core.Saves;
using BannerlordCoopLauncher.Core.Support;

namespace TaomCoop.Core;

/// <summary>One extra mod of the setup as the server will get it.</summary>
public sealed record TaomServerExtra(TaomExtraMod Mod, string SourceDirectory, ServerCode Code);

public sealed record TaomServerPlan(
    IReadOnlyList<string> ServerModules,
    IReadOnlyList<string> ClientModules,
    IReadOnlyList<TaomServerExtra> Extras,
    IReadOnlyList<TaomExtraMod> Missing,
    string? JoinProblem,
    TaomSetup Setup);

/// <summary>
/// The Coodsz TAOM server path. Plain TAOM keeps the sealed TAOM-Coop server
/// (BannerlordCoopServer.exe --data-dir, with its fixed module list). A setup with extra mods
/// starts the same prepared server folder's engine directly, the way Coop's Start-ModdedServer.ps1
/// does, with TaomLoadOrder.ServerOrder as the module list: the TAOM core in its sealed order, the
/// setup's extras in their slots, DedicatedServer.Windows last. The TAOM-Coop DedicatedServer.Core
/// (with its TAOM.CoopCompat guards and Coop pin checks) is the one in that server folder and is
/// not changed. Extras are copied into the server's engine\Modules. A mod without a server build
/// stays in the list (so Coop's join check matches the players), but its client code is switched
/// off in the server copy, the way such mods mark it themselves (DedicatedServerType none).
/// </summary>
public static class TaomServerPath
{
    public const string Explain =
        "Plain TAOM starts the sealed TAOM server as before. A setup with extra mods starts the Coodsz TAOM server. It loads the same TAOM core plus the extra mods, from the same server folder and the same Data folder.";

    public const string NotTaomCampaign = "This save is not a TAOM campaign. It was made without TAOM, so the server can't load it with TAOM. Make a new campaign in singleplayer with TAOM on, save it, then pick that save here.";

    // An empty list (a header that can't be read) counts as TAOM on purpose, like SaveChange: a reader
    // hiccup must never lock out a real TAOM campaign.
    public static bool IsTaomCampaign(IReadOnlyList<string> saveModules) =>
        saveModules.Count == 0 || saveModules.Any(module => module.Equals("TAOM", StringComparison.OrdinalIgnoreCase));

    public static bool IsTaomCampaign(string savePath) =>
        IsTaomCampaign(CampaignSaveInfoReader.Read(savePath)?.Modules ?? Array.Empty<string>());

    public static bool Needed(TaomSetup? setup) => setup != null && TaomLoadOrder.ClientOrder(setup).Count > TaomStack.ClientModules.Length;

    /// <summary>What the server needs for this setup. onThisPc are the mods found on the host's PC
    /// (Steam Workshop and the game's Modules folder).</summary>
    public static TaomServerPlan Plan(TaomSetup setup, IReadOnlyList<FoundMod> onThisPc)
    {
        // The ids the mods on this PC really have (a mod is also matched by its Workshop id).
        setup = TaomModIds.Resolve(setup, onThisPc);
        var extras = new List<TaomServerExtra>();
        var missing = new List<TaomExtraMod>();
        foreach (var mod in setup.EnabledExtras.Where(mod => TaomLoadOrder.RefusalReason(mod.ModuleId) == null))
        {
            var found = onThisPc.FirstOrDefault(item => item.ModuleId.Equals(mod.ModuleId, StringComparison.OrdinalIgnoreCase));
            if (found == null)
            {
                missing.Add(mod);
                continue;
            }

            ServerCode code;
            try
            {
                code = ServerModuleInstall.Classify(found.Directory);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                code = ServerCode.ClientCodeOnly;
            }

            if (code == ServerCode.ClientCodeSkipped && RunsForCoopModPatch(setup, mod.ModuleId))
                code = ServerCode.ServerBuild;
            extras.Add(new TaomServerExtra(mod, found.Directory, code));
        }

        var server = TaomLoadOrder.ServerOrder(setup);
        var client = TaomLoadOrder.LaunchModules(setup, joinPassword: true);
        return new TaomServerPlan(server, client, extras, missing, CoopJoinRules.JoinProblem(client, server), setup);
    }

    /// <summary>
    /// Copies the extras into the server's engine\Modules: a mod that is not there yet, or whose
    /// version changed, is copied again. Client code without a server build is switched off in the
    /// server copy. Returns one plain line per mod.
    /// </summary>
    public static IReadOnlyList<string> Stage(string serverRoot, TaomServerPlan plan)
    {
        var lines = new List<string>();
        var modulesRoot = ServerModuleInstall.ModulesRoot(serverRoot);
        foreach (var extra in plan.Extras)
        {
            var id = extra.Mod.ModuleId.Trim();
            var destination = Path.Combine(modulesRoot, id);
            var copied = false;
            if (!Directory.Exists(destination) || VersionOf(destination) != VersionOf(extra.SourceDirectory)
                || MissesServerBuild(destination, extra.SourceDirectory, RunsForCoopModPatch(plan.Setup, id)))
            {
                ServerModuleInstall.Install(extra.SourceDirectory, serverRoot, id);
                copied = true;
            }

            // A mod from a Nexus or ModDB zip keeps the Windows download mark; the server cannot load
            // a marked DLL. Done on every start, since an update brings the mark back.
            var unblocked = ModUnblock.UnblockDlls(destination);
            if (unblocked > 0)
                lines.Add(ModUnblock.Line(extra.Mod.Label, unblocked));
            if (copied)
                DownloadMark.Clear(destination);

            if (ServerModuleInstall.CoopModPatchServerCode.Contains(id))
            {
                var needed = RunsForCoopModPatch(plan.Setup, id);
                if (needed)
                    ServerModuleInstall.AddServerBuildFromClient(destination);
                SetCoopModPatchServerCode(destination, extra.SourceDirectory, needed);
            }
            TurnOffClientCode(destination, id);
            // The server copy is the server's own, so the TAOM dependency fix just stays on it.
            var fix = TaomDependencyFix.Apply(destination, extra.Mod.Label);
            if (fix != null)
                lines.Add(fix);
            var label = extra.Mod.Label;
            lines.Add((copied ? "Copied " + label + " to the server." : label + " is already on the server.") + " " + NoteFor(extra.Code));
        }

        return lines;
    }

    /// <summary>True when the setup has CoopModPatch and this mod is one its server adapter needs
    /// running on the server (Improved Garrisons).</summary>
    public static bool RunsForCoopModPatch(TaomSetup setup, string moduleId) =>
        ServerModuleInstall.CoopModPatchServerCode.Contains(moduleId)
        && TaomLoadOrder.ServerOrder(setup).Contains(ServerModuleInstall.CoopModPatchId, StringComparer.OrdinalIgnoreCase);

    // Coodsz Host force loads these mods on its server (forceHeadlessModules); the TAOM server has
    // no such loader, so without this the engine skips the DLL (DedicatedServerType none) and the
    // CoopModPatch adapter fails with "Could not load file or assembly". The server copy's
    // SubModule.xml is made from the mod's own each start: the skip mark comes off when CoopModPatch
    // is in the setup, and dropping CoopModPatch puts the mod's own mark back. A copy that already
    // is what it should be (also after the TAOM dependency fix) is not written again.
    private static void SetCoopModPatchServerCode(string moduleDirectory, string sourceDirectory, bool on)
    {
        var xmlPath = Path.Combine(moduleDirectory, "SubModule.xml");
        var sourcePath = Path.Combine(sourceDirectory, "SubModule.xml");
        if (!File.Exists(xmlPath))
            return;
        var samePlace = Path.GetFullPath(sourcePath).Equals(Path.GetFullPath(xmlPath), StringComparison.OrdinalIgnoreCase);
        var basePath = File.Exists(sourcePath) && !samePlace ? sourcePath : xmlPath;
        var source = ReadXml(basePath, out var bom);
        var wanted = on ? ServerModuleInstall.TurnOnServerCode(source, ServerModuleInstall.ServerDlls(moduleDirectory)) : source;
        var current = ReadXml(xmlPath, out _);
        if (current == wanted || current == (TaomDependencyFix.Strip(wanted) ?? wanted))
            return;
        File.WriteAllText(xmlPath, wanted, new System.Text.UTF8Encoding(bom));
    }

    // A same-version server copy without the server DLL keeps its skip mark, so CoopModPatch's adapter can't load it.
    private static bool MissesServerBuild(string destination, string sourceDirectory, bool needed) =>
        needed && ServerModuleInstall.ServerDlls(sourceDirectory).Except(ServerModuleInstall.ServerDlls(destination), StringComparer.OrdinalIgnoreCase).Any();

    private static string ReadXml(string path, out bool bom)
    {
        var raw = File.ReadAllBytes(path);
        bom = raw.Length >= 3 && raw[0] == 0xEF && raw[1] == 0xBB && raw[2] == 0xBF;
        return new System.Text.UTF8Encoding(false).GetString(raw, bom ? 3 : 0, raw.Length - (bom ? 3 : 0));
    }

    // Switches client code off in the server copy. True when the copy has code that is now off.
    private static bool TurnOffClientCode(string moduleDirectory, string moduleId)
    {
        var xmlPath = Path.Combine(moduleDirectory, "SubModule.xml");
        if (!File.Exists(xmlPath))
            return false;
        var raw = File.ReadAllBytes(xmlPath);
        var bom = raw.Length >= 3 && raw[0] == 0xEF && raw[1] == 0xBB && raw[2] == 0xBF;
        var xml = new System.Text.UTF8Encoding(false).GetString(raw, bom ? 3 : 0, raw.Length - (bom ? 3 : 0));
        var dlls = ServerModuleInstall.ServerDlls(moduleDirectory);
        if (ServerModuleInstall.Classify(xml, dlls, moduleId) != ServerCode.ClientCodeOnly)
            return false;
        var patched = ServerModuleInstall.TurnOffClientCode(xml, dlls, moduleId);
        if (patched != xml)
            File.WriteAllText(xmlPath, patched, new System.Text.UTF8Encoding(bom));
        return true;
    }

    /// <summary>Extras that are not on the Steam Workshop (and do not come with the tools): every player
    /// has to put them in the game's Modules folder by hand, or Coop turns the join down.</summary>
    public static IReadOnlyList<string> NexusOnlyNotes(TaomServerPlan plan) =>
        plan.Extras
            .Where(extra => !SharedModList.IsWorkshopId(extra.Mod.WorkshopId) && !TaomPlus.BundledIds.Contains(extra.Mod.ModuleId, StringComparer.OrdinalIgnoreCase))
            .Select(extra => extra.Mod.Label + " is not on the Steam Workshop. Every player has to get it from Nexus Mods or ModDB and put it in the game's Modules folder, or the server turns the join down.")
            .ToList();

    public static string NoteFor(ServerCode code) => code switch
    {
        ServerCode.DataOnly => "The server loads it. It has no code.",
        ServerCode.ServerBuild => "The server loads it with its server build.",
        ServerCode.ClientCodeSkipped => "The server loads its data. The mod keeps its own code off the server.",
        _ => "The server loads its data, and its client code is switched off on the server so it cannot crash it. Players still run that code."
    };

    public static string RowNote(ServerCode code) => code switch
    {
        ServerCode.DataOnly => "Loads on the server",
        ServerCode.ServerBuild => "Server build",
        _ => "Data only on the server"
    };

    /// <summary>The engine start for the Coodsz TAOM server: the prepared TAOM server folder, the
    /// TAOM Data folder, the setup's server order.</summary>
    public static EngineStart Launch(string serverRoot, string dataRoot, string coopDataRoot, TaomSetup setup) =>
        DedicatedEngineLaunch.Build(serverRoot, TaomLoadOrder.ServerOrder(setup), dataRoot, coopDataRoot);

    /// <summary>Null when the save remembers the same mods as the setup (or remembers none).</summary>
    public static string? SaveProblem(string savePath, TaomSetup setup)
    {
        var info = CampaignSaveInfoReader.Read(savePath);
        return info == null ? null : CoopJoinRules.SaveProblem(info.Modules, TaomLoadOrder.ServerOrder(setup));
    }

    /// <summary>Whether the setup only adds mods to the save (ask), leaves one out (warn), or
    /// matches. A save that cannot be read starts as it is.</summary>
    public static SaveModChange SaveChange(string savePath, TaomSetup setup)
    {
        var info = CampaignSaveInfoReader.Read(savePath);
        return SaveModChange.Check(info?.Modules ?? Array.Empty<string>(), TaomLoadOrder.ServerOrder(setup));
    }

    private static string VersionOf(string moduleDirectory)
    {
        var xml = Path.Combine(moduleDirectory, "SubModule.xml");
        try
        {
            return File.Exists(xml) ? ModuleIdentityReader.Read(xml).Version : "";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return "";
        }
    }
}
