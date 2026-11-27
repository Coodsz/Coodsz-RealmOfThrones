namespace BannerlordCoopLauncher.Core.Modules;

/// <summary>
/// The total overhaul modules the add mods windows warn about: the modules TAOM itself installs.
/// Community mods whose id or name starts with TAOM (the TAOM developer's Nexus mods such as
/// TAOM_PlayerVoices, TAOM_CheatPanel or the TAOM_Standalone_Compat_* patches) are not overhauls.
/// </summary>
public static class TotalOverhaul
{
    private static readonly HashSet<string> TaomModules = new(StringComparer.OrdinalIgnoreCase)
    {
        "TAOM",
        "TAOM_Map",
        "TAOM.Dependencies",
        "LOTRLOME_Armory",
        "TAOM.CoopCompat"
    };

    public static bool Is(string? moduleId) =>
        !string.IsNullOrWhiteSpace(moduleId) && TaomModules.Contains(moduleId.Trim());
}
