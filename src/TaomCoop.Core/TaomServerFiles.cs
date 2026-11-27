using BannerlordCoopLauncher.Core.Support;

namespace TaomCoop.Core;

/// <summary>
/// The files the TAOM server tool's antivirus watch remembers after Prepare server and a start that
/// worked. They are what Stage-TaomCompatServer.ps1 and Start server put in the server folder
/// (ServerProgramRepair's lists, the engine, Coop Save Rescue, Coodsz War Sync and TAOM.CoopCompat),
/// plus the Workshop server Prepare copies from.
/// </summary>
public static class TaomServerFiles
{
    public static List<string> Watched(string root, string? workshopCoop, string? compatPayload = null)
    {
        var files = ServerProgramRepair.Files.Select(file => file.Target)
            .Where(target => target.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) || target.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
            .Select(target => Path.Combine(root, target)).ToList();
        files.AddRange(AntivirusFiles.Engine(root));
        files.AddRange(ServerProgramRepair.EngineBinFiles.Select(file => Path.Combine(root, ServerProgramRepair.EngineBin, file.Name)));
        files.AddRange(ServerProgramRepair.ModuleFiles.Select(file => Path.Combine(root, file.Target)));
        var bin = ServerWindowsSubModules.BinDirectory(root);
        files.AddRange(new[] { RescueSubModule.DllName, WarSyncSubModule.DllName, WarSyncSubModule.SyncDllName }.Select(dll => Path.Combine(bin, dll)));
        // From the kit's TAOM.CoopCompat, so a DLL antivirus removed from the server is still listed. Without
        // the kit only what is on disk now.
        var compat = Path.Combine(root, "engine", "Modules", TaomStack.CompatModuleId);
        files.AddRange(compatPayload != null ? AntivirusFiles.RepairPayloadDlls(compat, compatPayload) : AntivirusFiles.ModuleDlls(compat));
        if (workshopCoop != null)
            files.Add(AntivirusFiles.WorkshopServerExe(workshopCoop));
        return files;
    }

    /// <summary>
    /// The host kit's Artifacts\TAOM.CoopCompat\&lt;version&gt;\Modules\TAOM.CoopCompat, the folder Prepare-Host.ps1 hands
    /// Stage-TaomCompatServer.ps1, or null without exactly one artifact folder (the same rule as Resolve-SingleArtifact).
    /// </summary>
    public static string? KitCompatModule(string? kitRoot)
    {
        if (string.IsNullOrWhiteSpace(kitRoot))
            return null;
        var parent = Path.Combine(kitRoot, "Artifacts", TaomStack.CompatModuleId);
        if (!Directory.Exists(parent))
            return null;
        var folders = Directory.GetDirectories(parent);
        if (folders.Length != 1)
            return null;
        var module = Path.Combine(folders[0], "Modules", TaomStack.CompatModuleId);
        return File.Exists(Path.Combine(module, "SubModule.xml")) ? module : null;
    }
}
