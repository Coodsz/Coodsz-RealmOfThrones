using BannerlordCoopLauncher.Core.Models;

namespace BannerlordCoopLauncher.Core.Profile;

public static class ClientModToggles
{
    private static readonly HashSet<string> Locked = new(StringComparer.OrdinalIgnoreCase)
    {
        "Bannerlord.Harmony",
        "Bannerlord.ButterLib",
        "Bannerlord.UIExtenderEx",
        "Bannerlord.MBOptionScreen",
        "Coop",
        "CoopModPatch",
        "Native",
        "SandBoxCore",
        "SandBox",
        "StoryMode",
        "CustomBattle"
    };

    private static readonly HashSet<string> MapMods = new(StringComparer.OrdinalIgnoreCase)
    {
        "MoreNationsRemastered",
        "MoreNationsRemasteredFix"
    };

    private static readonly HashSet<string> SettlementTail = new(StringComparer.OrdinalIgnoreCase)
    {
        "AgricultureEstate",
        "PSCoopBridge"
    };

    public static bool CanToggle(string moduleId) =>
        !Locked.Contains(moduleId) && !MapMods.Contains(moduleId) && !SettlementTail.Contains(moduleId);

    public static bool FollowsSettlement(string moduleId) => SettlementTail.Contains(moduleId);

    public static void Apply(ServerProfile profile, IEnumerable<string> disabledIds)
    {
        var disabled = new HashSet<string>(disabledIds.Where(CanToggle), StringComparer.OrdinalIgnoreCase);
        var settlementOff = disabled.Contains("PlayerSettlement");

        foreach (var mod in profile.Mods)
        {
            if (Locked.Contains(mod.ModuleId) || MapMods.Contains(mod.ModuleId))
                continue;

            if (SettlementTail.Contains(mod.ModuleId))
            {
                mod.Enabled = !settlementOff;
                continue;
            }

            mod.Enabled = !disabled.Contains(mod.ModuleId);
        }

        profile.ModuleLoadOrder.RemoveAll(id =>
            disabled.Contains(id) || (settlementOff && SettlementTail.Contains(id)));
    }
}
