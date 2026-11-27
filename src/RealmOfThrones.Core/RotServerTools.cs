using BannerlordCoopLauncher.Core.IO;
using BannerlordCoopLauncher.Core.Server;

namespace RealmOfThrones.Core;

/// <summary>
/// Coodsz Server Tools on the Realm of Thrones server, for the host rules (pause on join, offline XP freeze, kingdom
/// protection, unstuck) and the mod settings for players, the same module and defaults as the Coop Server Tool. The
/// Server Tool carries it in its ServerAddOns payload next to the compat module, copies it into engine\Modules on every
/// Prepare and Start, and lists it after the compat module, so it loads after Realm of Thrones and the RoT build of
/// Coop. It is a DedicatedServer.* module: Coop's join check, the shared list and the save checks leave it out, so
/// players need nothing for it. The client half of the settings sync rides in the join password module the Client
/// Launcher already puts in on every Connect.
/// The start names the server's data folder in <see cref="DataDirVariable"/>, so the module reads its rules from
/// coodsz-server-tools.json there and never the Coop Server Tool's file in Documents. MCM's settings provider does not
/// run on this server (its library DLLs load, its SubModule does not), so the sync sends players the server's own copy
/// of the MCM files, which takes the host's Documents files it is missing at every Prepare and Start, and the Settings
/// page edits that copy (<see cref="ServerModSettings"/>). Looks mods go into recipes.json, so players keep their own.
/// </summary>
public static class RotServerTools
{
    public const string Payload = "ServerAddOns";

    /// <summary>Set by this Server Tool's start and the TAOM one's (ServerToolsModule.DataDirVariable).</summary>
    public const string DataDirVariable = ServerToolsModule.DataDirVariable;

    /// <summary>The extra start variables of the server: its data folder for the module.</summary>
    public static IReadOnlyDictionary<string, string> StartVariables(string dataRoot) =>
        new Dictionary<string, string> { [DataDirVariable] = dataRoot };

    /// <summary>The settings file the module reads, in the server's data folder.</summary>
    public static string RulesPath(string dataRoot) => ClanCapFile.PathIn(dataRoot);

    /// <summary>The "modules" list of server-config.json: the setup's list with the tools module last, once.</summary>
    public static IReadOnlyList<string> ServerModules(IReadOnlyList<string> modules) => ServerToolsModule.WithTools(modules);

    /// <summary>
    /// Copies the payload copy into the server when a file is missing or different. Returns a line for the log when
    /// something was copied or the payload is missing, or null.
    /// </summary>
    public static string? Install(string serverRoot, string payloadRoot)
    {
        if (!File.Exists(Path.Combine(payloadRoot, "SubModule.xml")))
            return ToolPayload.MissingText(ServerToolsModule.Name, payloadRoot);
        return ServerToolsModule.CopyInto(payloadRoot, serverRoot) == 0
            ? null
            : "Added " + ServerToolsModule.Name + " to the server (engine\\Modules\\" + ServerToolsModule.ModuleId
              + ") for the server rules and the mod settings for players. Players do not need it.";
    }
}
