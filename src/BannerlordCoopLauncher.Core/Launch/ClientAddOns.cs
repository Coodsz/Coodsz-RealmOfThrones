using BannerlordCoopLauncher.Core.IO;
using BannerlordCoopLauncher.Core.Support;

namespace BannerlordCoopLauncher.Core.Launch;

/// <summary>
/// Small client modules a launcher ships in its ClientAddOns folder and copies into the game's Modules
/// folder on Connect. Their ids start with DedicatedServer., which Coop 0.1.6 leaves out of the join
/// check, so the server never needs them.
/// </summary>
public static class ClientAddOns
{
    public const string Folder = "ClientAddOns";

    // Fills an empty Coop connect password from COODSZ_COOP_PASSWORD (CoopPasswordPatch.VariableName)
    // without editing Coop.Core.dll. Source: compat\CoodszJoinPassword.
    public const string JoinPasswordId = "DedicatedServer.CoodszJoinPassword";
    public const string JoinPasswordName = "Coodsz Join Password";

    // The same module also carries Coodsz Menu Guard (compat\CoodszMenuGuard), which keeps a village or
    // town menu from freezing on a party Coop synced without its data. It rides in this module so the
    // id list stays the same: Bannerlord wipes its shader cache whenever the active mod list changes.

    public static string Payload(string moduleId, string? baseDirectory = null) =>
        baseDirectory == null ? Path.Combine(ToolPayload.Folder(Folder), moduleId) : Path.Combine(baseDirectory, Folder, moduleId);

    /// <summary>Installs an add-on and clears its download mark; false (with the reason) when it could not.
    /// log gets the antivirus line when a DLL the last Connect put there was gone.</summary>
    public static bool TryInstall(string? gameRoot, string moduleId, string name, out string? error, Action<string>? log = null)
    {
        error = null;
        if (string.IsNullOrEmpty(gameRoot))
            return false;
        try
        {
            if (Install(gameRoot, moduleId, name) is { } taken)
                log?.Invoke(taken);
            Support.DownloadMark.Clear(Path.Combine(gameRoot, "Modules", moduleId));
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            error = ex.Message;
            return false;
        }
    }

    /// <summary>Copies the shipped module over Modules\&lt;id&gt;. Throws when the launcher could not write it out.
    /// Returns the antivirus line when a DLL the last install put there was gone (it is back now), else null.</summary>
    public static string? Install(string gameRoot, string moduleId, string name, string? payloadRoot = null, AntivirusWatch? watch = null)
    {
        var shown = payloadRoot ?? ToolPayload.Expected(Folder, moduleId);
        payloadRoot ??= Payload(moduleId);
        if (!File.Exists(Path.Combine(payloadRoot, "SubModule.xml")))
            throw new InvalidOperationException(ToolPayload.MissingText(name, shown));
        var target = Path.Combine(gameRoot, "Modules", moduleId);
        watch ??= AntivirusWatch.ForTool();
        // Only DLLs this launcher version still ships, so one an update renamed or dropped is never named.
        // Antivirus takes DLLs, not a whole module: with the module folder or its SubModule.xml gone (a first install,
        // or a folder deleted or restored without it), nothing is blamed on antivirus.
        var installed = File.Exists(Path.Combine(target, "SubModule.xml"));
        var gone = installed
            ? AntivirusFiles.StillShipped(watch.Gone([target]), AntivirusFiles.RepairPayloadDlls(target, payloadRoot))
            : [];
        foreach (var source in Directory.EnumerateFiles(payloadRoot, "*", SearchOption.AllDirectories))
        {
            var destination = Path.Combine(target, Path.GetRelativePath(payloadRoot, source));
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            File.Copy(source, destination, overwrite: true);
        }

        watch.Remember([target], AntivirusFiles.ModuleDlls(target));
        var modules = Path.Combine(gameRoot, "Modules");
        return gone.Count == 0 ? null : AntivirusText.TakenPutBack(string.Join(", ", gone.Select(file => AntivirusFiles.Shown(file, modules))), "Connect");
    }
}
