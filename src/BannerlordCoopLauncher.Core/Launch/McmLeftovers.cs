namespace BannerlordCoopLauncher.Core.Launch;

public static class McmLeftovers
{
    public static string Root =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            "Mount and Blade II Bannerlord",
            "Configs",
            "ModSettings");

    public static (int Removed, IReadOnlyList<string> Names) ClearUnused(IEnumerable<string> keepModuleIds) =>
        ClearUnused(Root, keepModuleIds);

    public static (int Removed, IReadOnlyList<string> Names) ClearUnused(string root, IEnumerable<string> keepModuleIds)
    {
        var keep = new HashSet<string>(
            keepModuleIds.Where(id => !string.IsNullOrWhiteSpace(id)),
            StringComparer.OrdinalIgnoreCase);
        if (!Directory.Exists(root))
            return (0, Array.Empty<string>());

        var removed = new List<string>();
        foreach (var path in Directory.EnumerateFileSystemEntries(root))
        {
            var name = Path.GetFileNameWithoutExtension(path);
            if (string.IsNullOrWhiteSpace(name) || keep.Contains(name))
                continue;
            if (!OptionalLeftover.Contains(name))
                continue;

            try
            {
                if (Directory.Exists(path))
                    Directory.Delete(path, recursive: true);
                else
                    File.Delete(path);
                removed.Add(name);
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }

        return (removed.Count, removed);
    }

    private static readonly HashSet<string> OptionalLeftover = new(StringComparer.OrdinalIgnoreCase)
    {
        "PlayerSettlement",
        "PSCoopBridge",
        "AgricultureEstate",
        "ImprovedGarrisons",
        "ServerSettingsLock",
        "XorberaxLegacy",
        "SettlementCultureDrift",
        "CoopModPatch",
        "RTSCamera",
        "RTSCamera.CommandSystem",
        "DismembermentPlus",
        "BannerFix",
        "RaiseYourBanner",
        "RaiseYourTorch",
        "PerfectFireArrows",
        "AliveScenes",
        "ImmersiveBattlefields",
        "MoreNationsRemastered",
        "MoreNationsRemasteredFix",
        "RBM",
        "RBMCombat",
        "RBMTournament",
        "Bannerlord.Diplomacy"
    };
}
