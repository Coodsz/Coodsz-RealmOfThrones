using BannerlordCoopLauncher.Core.IO;
using BannerlordCoopLauncher.Core.Server;

namespace TaomCoop.Core;

/// <summary>
/// Coodsz Server Tools on the TAOM server, for the host rules (pause on join, offline XP freeze, kingdom protection,
/// unstuck) and the mod settings for players, the same module and defaults as the Coop and Realm of Thrones Server
/// Tools. The TAOM host only loads its sealed module list, so the module's DLL rides in DedicatedServer.Windows as an
/// extra SubModule, like Coop Save Rescue and Coodsz War Sync: the module list and the saves stay the same and Coop's
/// join check never sees it, so players need nothing for it. The client half of the settings sync rides in the join
/// password module the Client Launcher already puts in. Coodsz War Sync steps aside by itself when this module is
/// loaded (WarSyncRules.ShouldRun), and this module's settings sync sends the War of the Ring state instead, so one
/// sync runs. Both use the same CoodszSettingsSync.Coop.dll (the tests keep the two copies the same).
/// The start names the server's data folder in <see cref="ServerToolsModule.DataDirVariable"/>, so the module reads its
/// rules from coodsz-server-tools.json there and the sync sends players the server's own copy of the MCM files
/// (<see cref="ServerModSettings"/>), never the Coop Server Tool's files in Documents. The TAOM server's log shows MCMv5.dll
/// load as a library and no MCM SubModule (TAOM.Dependencies lists none for it), so the sync sends those files as they are;
/// were MCM's provider running there, the sync would put the same files into it. Looks mods go into recipes.json in
/// DedicatedServer.Windows, the folder the module runs from.
/// </summary>
public static class TaomServerTools
{
    public const string Payload = "ServerTools";
    public const string SubModuleName = ServerToolsModule.Name;
    public const string DllName = ServerToolsModule.ModuleId + ".dll";
    public const string SyncDllName = WarSyncSubModule.SyncDllName;
    public const string ClassType = "CoodszServerTools.ToolsSubModule";

    /// <summary>The extra start variables of the server: its data folder for the module.</summary>
    public static IReadOnlyDictionary<string, string> StartVariables(string dataRoot) =>
        new Dictionary<string, string> { [ServerToolsModule.DataDirVariable] = dataRoot };

    /// <summary>The settings file the module reads, in the server's data folder.</summary>
    public static string RulesPath(string dataRoot) => ClanCapFile.PathIn(dataRoot);

    /// <summary>The folder the module runs from on the server, where the settings sync reads recipes.json.</summary>
    public static string ModuleFolder(string serverRoot) => ServerWindowsSubModules.ModuleDirectory(serverRoot);

    public static bool IsInstalled(string serverRoot) => ServerWindowsSubModules.IsInstalled(serverRoot, SubModuleName, DllName, SyncDllName);

    /// <summary>Copies both DLLs when they differ and adds the SubModule entry once.</summary>
    public static void Install(string serverRoot, string payloadDirectory)
    {
        // The bridge DLL first, as for War Sync: a module already there still finds a bridge it can start.
        var sources = new[] { Path.Combine(payloadDirectory, SyncDllName), Path.Combine(payloadDirectory, DllName) };
        if (!sources.All(File.Exists))
            throw new InvalidOperationException(ToolPayload.MissingText(SubModuleName, payloadDirectory));
        ServerWindowsSubModules.CopyDlls(serverRoot, sources);
        ServerWindowsSubModules.EnsureEntry(serverRoot, SubModuleName, DllName, ClassType);
    }

    /// <summary>The server log line when Start could not add or update it. The start goes on either way.</summary>
    public static string InstallFailed(bool copyThere, string reason) => copyThere
        ? "Could not update Coodsz Server Tools on the server, so this start uses the copy already there. " + reason
        : "Could not add Coodsz Server Tools to the server, so the server rules and the mod settings for players are off on this start. " + reason;
}
