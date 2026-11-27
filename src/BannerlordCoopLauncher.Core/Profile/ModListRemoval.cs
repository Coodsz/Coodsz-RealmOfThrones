using BannerlordCoopLauncher.Core.Modules;

namespace BannerlordCoopLauncher.Core.Profile;

public static class ModListRemoval
{
    private static readonly HashSet<string> Core = new(StringComparer.OrdinalIgnoreCase)
    {
        "Bannerlord.Harmony",
        "Bannerlord.ButterLib",
        "Bannerlord.UIExtenderEx",
        "Bannerlord.MBOptionScreen",
        "Native",
        "SandBoxCore",
        "SandBox",
        "StoryMode",
        "CustomBattle"
    };

    public static bool CanRemove(string? moduleId, bool locked, bool defaultSetup, bool includeMoreNations)
    {
        if (locked || string.IsNullOrWhiteSpace(moduleId) || BannerlordCoopModules.IsEither(moduleId) || Core.Contains(moduleId))
            return false;
        if (defaultSetup)
            return !DefaultClientSet.IsFixed(moduleId);
        return !ServerModuleSet.WorkingOrder(includeMoreNations).Contains(moduleId, StringComparer.OrdinalIgnoreCase);
    }
}
