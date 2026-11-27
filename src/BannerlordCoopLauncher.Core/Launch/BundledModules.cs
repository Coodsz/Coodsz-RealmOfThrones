using BannerlordCoopLauncher.Core.Nexus;
using BannerlordCoopLauncher.Core.Parsing;

namespace BannerlordCoopLauncher.Core.Launch;

public static class BundledModules
{
    // Only mods that are not on the Steam Workshop. Coop and Improved Garrisons come from
    // their Workshop items. COOP Server Settings Lock left the setup in v1.3.4 and is not
    // bundled either. A second copy of a Workshop mod in the game's Modules folder made the game load two copies of the same mod
    // and crash on the locked More Nations setup.
    public static readonly string[] ModuleIds =
    [
        "PSCoopBridge",
        "ImmersiveBattlefields",
        "BannerFix",
        "RaiseYourBanner",
        "RaiseYourTorch",
        "PerfectFireArrows",
        "AliveScenes"
    ];

    /// <summary>
    /// True when the launcher can copy this mod into Modules: it is one of <see cref="ModuleIds"/> and
    /// its folder is really in the bundled mods folder (ToolPayload.BundledMods, which a dev build does
    /// not carry). Without the folder only the player can get it.
    /// </summary>
    public static bool CanCopy(string launcherDirectory, string moduleId) =>
        !string.IsNullOrWhiteSpace(launcherDirectory)
        && ModuleIds.Contains(moduleId, StringComparer.OrdinalIgnoreCase)
        && File.Exists(Path.Combine(launcherDirectory, moduleId, "SubModule.xml"));

    public static IReadOnlyList<string> InstallMissing(string launcherDirectory, string gameRoot)
    {
        var installed = new List<string>();
        if (string.IsNullOrWhiteSpace(launcherDirectory) || string.IsNullOrWhiteSpace(gameRoot))
            return installed;

        foreach (var moduleId in ModuleIds)
        {
            var source = Path.Combine(launcherDirectory, moduleId);
            if (!File.Exists(Path.Combine(source, "SubModule.xml")))
                continue;
            if (AlreadyPresent(gameRoot, moduleId))
                continue;

            ModuleZipInstaller.InstallDirectory(source, gameRoot, moduleId);
            installed.Add(moduleId);
        }

        return installed;
    }

    private static bool AlreadyPresent(string gameRoot, string moduleId)
    {
        var modulesRoot = Path.Combine(gameRoot, "Modules");
        if (File.Exists(Path.Combine(modulesRoot, moduleId, "SubModule.xml")))
            return true;
        if (!Directory.Exists(modulesRoot))
            return false;

        foreach (var child in Directory.EnumerateDirectories(modulesRoot))
        {
            var xmlPath = Path.Combine(child, "SubModule.xml");
            if (!File.Exists(xmlPath))
                continue;
            if (SubModuleXml.TryRead(File.ReadAllText(xmlPath), out var info, out _)
                && info != null
                && string.Equals(info.Id, moduleId, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    /// <summary>
    /// True when a mod folder is the same as the one of that name in the bundled mods folder: the same files
    /// with the same sizes and the same SubModule.xml. An older version shipped the bundled mods beside the exe.
    /// </summary>
    public static bool IsCopyOf(string folder, string? bundledDirectory)
    {
        if (string.IsNullOrWhiteSpace(bundledDirectory))
            return false;
        var twin = Path.Combine(bundledDirectory, Path.GetFileName(folder.TrimEnd('\\', '/')));
        try
        {
            if (!File.Exists(Path.Combine(folder, "SubModule.xml")) || !File.Exists(Path.Combine(twin, "SubModule.xml")))
                return false;
            if (IO.FileHash.Sha256(Path.Combine(folder, "SubModule.xml")) != IO.FileHash.Sha256(Path.Combine(twin, "SubModule.xml")))
                return false;
            return Listing(folder).SequenceEqual(Listing(twin));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    private static IEnumerable<string> Listing(string folder) =>
        Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories)
            .Select(file => Path.GetRelativePath(folder, file).Replace('\\', '/').ToUpperInvariant() + "|" + new FileInfo(file).Length)
            .Order(StringComparer.Ordinal);
}
