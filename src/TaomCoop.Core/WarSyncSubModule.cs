using BannerlordCoopLauncher.Core.IO;

namespace TaomCoop.Core;

/// <summary>
/// Coodsz War Sync (compat\CoodszWarSync) for the TAOM server, on every start: the server part of the War of
/// the Ring sync, which sends TAOM's war phase and momentum to players with the Coodsz client add on so TAOM's
/// map bar shows and moves for them. Nothing else: no settings sync, no server rules, no commands. Like Coop
/// Save Rescue it rides in DedicatedServer.Windows, so the sealed module list and the saves do not change. It
/// stays off by itself when Coodsz Server Tools is loaded in the same server, which the TAOM Server Tool now adds
/// next to it (TaomServerTools); the module's own settings sync then sends the War of the Ring state.
/// </summary>
public static class WarSyncSubModule
{
    public const string SubModuleName = "Coodsz War Sync";
    public const string DllName = "CoodszWarSync.dll";
    public const string SyncDllName = "CoodszSettingsSync.Coop.dll";
    public const string ClassType = "CoodszWarSync.WarSyncSubModule";
    public const string PayloadFolder = "WarSync";

    public static string PayloadDirectory(string? baseDirectory = null) =>
        baseDirectory == null ? ToolPayload.Folder(PayloadFolder) : Path.Combine(baseDirectory, PayloadFolder);

    public static bool IsInstalled(string serverRoot) => ServerWindowsSubModules.IsInstalled(serverRoot, SubModuleName, DllName, SyncDllName);

    /// <summary>Copies both DLLs when they differ and adds the SubModule entry once.</summary>
    public static void Install(string serverRoot, string payloadDirectory)
    {
        // The bridge DLL first: if the second copy fails, the module already there still finds a bridge it can start.
        var sources = new[] { Path.Combine(payloadDirectory, SyncDllName), Path.Combine(payloadDirectory, DllName) };
        if (!sources.All(File.Exists))
            throw new InvalidOperationException(ToolPayload.MissingText(SubModuleName, payloadDirectory));
        ServerWindowsSubModules.CopyDlls(serverRoot, sources);
        ServerWindowsSubModules.EnsureEntry(serverRoot, SubModuleName, DllName, ClassType);
    }

    /// <summary>The server log line when Start could not add or update it. The start goes on either way.</summary>
    public static string InstallFailed(bool copyThere, string reason) => copyThere
        ? "Could not update Coodsz War Sync on the server, so this start uses the copy already there. " + reason
        : "Could not add Coodsz War Sync to the server, so TAOM's War of the Ring bar does not update for players on this start. " + reason;
}
