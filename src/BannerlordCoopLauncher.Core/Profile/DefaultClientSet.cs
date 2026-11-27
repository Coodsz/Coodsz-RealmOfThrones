using BannerlordCoopLauncher.Core.Models;

namespace BannerlordCoopLauncher.Core.Profile;

public static class DefaultClientSet
{
    public static readonly string[] Order =
    [
        "Bannerlord.Harmony",
        "Bannerlord.ButterLib",
        "Bannerlord.UIExtenderEx",
        "Bannerlord.MBOptionScreen",
        "Native",
        "SandBoxCore",
        "SandBox",
        "StoryMode",
        "CustomBattle",
        "Coop",
        "CoopModPatch",
        "PSCoopBridge"
    ];

    private static readonly string[] BaseGameModules = ["Native", "SandBoxCore", "SandBox", "StoryMode", "CustomBattle"];

    public static readonly string[] Fixed =
    [
        "Bannerlord.Harmony",
        "Bannerlord.ButterLib",
        "Bannerlord.UIExtenderEx",
        "Bannerlord.MBOptionScreen",
        "Native",
        "SandBoxCore",
        "SandBox",
        "StoryMode",
        "CustomBattle"
    ];

    public static bool IsDefault(string? name) =>
        name != null && name.Equals("Default", StringComparison.OrdinalIgnoreCase);

    public static bool IsFixed(string moduleId) =>
        Fixed.Contains(moduleId, StringComparer.OrdinalIgnoreCase);

    public static bool Contains(string moduleId) =>
        Order.Contains(moduleId, StringComparer.OrdinalIgnoreCase);

    public static readonly string[] NotOnNewSetup = ["CoopModPatch", "PSCoopBridge"];

    public static bool CanToggle(string moduleId) =>
        !BaseGameModules.Contains(moduleId, StringComparer.OrdinalIgnoreCase);

    public static void Apply(ServerProfile profile)
    {
        profile.BaseModules = BaseGameModules.ToList();
        profile.Mods = new List<ProfileMod>
        {
            Mod("Harmony", "2859188632", "Bannerlord.Harmony"),
            Mod("ButterLib", "2859232415", "Bannerlord.ButterLib"),
            Mod("UIExtenderEx", "2859222409", "Bannerlord.UIExtenderEx"),
            Mod("Mod Configuration Menu v5", "0", "Bannerlord.MBOptionScreen"),
            Mod("Bannerlord Coop", "3770450698", "Coop", pinned: true),
            Mod("CoopModPatch", "3786391685", "CoopModPatch"),
            Mod("PSCoopBridge", "0", "PSCoopBridge", pinned: true)
        };
        profile.ModuleLoadOrder = Order.ToList();
    }

    public static void ApplyToggles(ServerProfile profile, IEnumerable<string> disabledIds)
    {
        var disabled = new HashSet<string>(disabledIds.Where(CanToggle), StringComparer.OrdinalIgnoreCase);
        foreach (var mod in profile.Mods)
            mod.Enabled = !disabled.Contains(mod.ModuleId);

        // Keep library positions so switching them back on restores their working order.
        profile.ModuleLoadOrder.RemoveAll(id => disabled.Contains(id) && !IsFixed(id));
    }

    private static ProfileMod Mod(string name, string workshopId, string moduleId, bool pinned = false) => new()
    {
        Name = name,
        WorkshopId = workshopId,
        ModuleId = moduleId,
        Enabled = true,
        Pinned = pinned
    };
}
