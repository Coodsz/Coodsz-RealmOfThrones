namespace BannerlordCoopLauncher.Core.Profile;

/// <summary>
/// Workshop items the setups used to download that are now hidden or removed on the Steam
/// Workshop (Steam result 9, checked 2026-10-17). Steam can not download them any more, so the
/// setups list these mods as local mods ("0"), like the online profile does, and the player gets
/// them from Nexus Mods or ModDB. A copy Steam downloaded before is still found and used.
/// </summary>
public static class RetiredWorkshopItems
{
    private static readonly Dictionary<string, string> ByModule = new(StringComparer.OrdinalIgnoreCase)
    {
        ["ServerSettingsLock"] = "3794802607",
        ["MoreNationsRemastered"] = "3806450944",
        ["MoreNationsRemasteredFix"] = "3806450713",
        // The official BUTR MCM item was removed (checked 2026-10-26). The Workshop only has re-uploads, some of
        // them old builds that crash the dedicated server, so MCM comes from Nexus Mods (mod 612).
        ["Bannerlord.MBOptionScreen"] = "2859238197"
    };

    /// <summary>The old Workshop id of this mod, or null when it never had one that was retired.</summary>
    public static string? For(string? moduleId) =>
        moduleId != null && ByModule.TryGetValue(moduleId, out var id) ? id : null;

    /// <summary>
    /// Mods whose copy in the game's Modules folder wins over the old Workshop download, which Connect then turns
    /// off: MCM comes from Nexus Mods and the host runs that copy (CR-J5AUW). For the other retired items the
    /// Workshop download is still the copy players use, so it keeps winning.
    /// </summary>
    public static bool LocalCopyWins(string? moduleId) =>
        string.Equals(moduleId, "Bannerlord.MBOptionScreen", StringComparison.OrdinalIgnoreCase);

    /// <summary>The retired items whose game folder copy wins (LocalCopyWins): the mod id and its old Workshop id.</summary>
    public static IEnumerable<(string ModuleId, string WorkshopId)> LocalWins =>
        ByModule.Where(pair => LocalCopyWins(pair.Key)).Select(pair => (pair.Key, pair.Value));

    public static bool IsRetired(string? workshopId) =>
        !string.IsNullOrWhiteSpace(workshopId) && ByModule.ContainsValue(workshopId.Trim());
}
