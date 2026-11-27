using BannerlordCoopLauncher.Core.IO;
using BannerlordCoopLauncher.Core.Support;

namespace TaomCoop.Core;

/// <summary>
/// Coop Save Rescue for the TAOM server, on every start. The TAOM host only loads its sealed module
/// list, so the Coop Save Rescue code rides in DedicatedServer.Windows (always loaded) as an extra
/// SubModule, the same way the plain Coop rescue zip does it. The module list stays the same, so the
/// save loads without a module mismatch and players can join. How much it may change comes from the
/// start (AutoRescue.EnvironmentVariable): auto on every start, load once after a crash while the save
/// loaded, full for the manual Run Rescue save.
/// </summary>
public static class RescueSubModule
{
    public const string SubModuleName = "Coop Save Rescue";
    public const string DllName = "CoopSaveRescue.dll";
    public const string ReportTag = AutoRescue.ReportTag;

    public const string Steps = RescueSaveText.Steps;

    public static string ModuleDirectory(string serverRoot) => ServerWindowsSubModules.ModuleDirectory(serverRoot);

    public static bool IsInstalled(string serverRoot) => ServerWindowsSubModules.IsInstalled(serverRoot, SubModuleName, DllName);

    /// <summary>
    /// Copies the DLL next to DedicatedServer.Windows (when it differs) and adds its SubModule entry
    /// once. A CoopSaveRescue.mode file left by the plain Coop rescue zip is removed, since it would
    /// turn on every fix on every start.
    /// </summary>
    public static void Install(string serverRoot, string dllSource)
    {
        if (!File.Exists(dllSource))
            throw new InvalidOperationException(ToolPayload.MissingText("Coop Save Rescue", dllSource));
        ServerWindowsSubModules.CopyDlls(serverRoot, new[] { dllSource });
        var modeFile = Path.Combine(ServerWindowsSubModules.BinDirectory(serverRoot), AutoRescue.ModeFile);
        if (File.Exists(modeFile))
            File.Delete(modeFile);
        ServerWindowsSubModules.EnsureEntry(serverRoot, SubModuleName, DllName, "CoopSaveRescue.SubModule");
    }

    public static string ReportText(string line) => AutoRescue.ReportText(line);
}
