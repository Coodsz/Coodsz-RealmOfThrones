using BannerlordCoopLauncher.Core.Models;

namespace BannerlordCoopLauncher.Core.Profile;

public static class ServerModuleSet
{
    public static void Apply(ServerProfile profile, bool includeMoreNations = true)
    {
        profile.BaseModules = new List<string> { "Native", "SandBoxCore", "SandBox" };
        profile.Mods = new List<ProfileMod>
        {
            Mod("Harmony", "2859188632", "Bannerlord.Harmony"),
            Mod("ButterLib", "2859232415", "Bannerlord.ButterLib"),
            Mod("UIExtenderEx", "2859222409", "Bannerlord.UIExtenderEx"),
            Mod("Mod Configuration Menu v5", "0", "Bannerlord.MBOptionScreen"),
            Mod("Bannerlord Coop", "3770450698", "Coop", pinned: true),
            Mod("Xorberax's Legacy", "2875138158", "XorberaxLegacy"),
            Mod("Settlement Culture Drift", "3777470096", "SettlementCultureDrift"),
            Mod("CoopModPatch", "3786391685", "CoopModPatch"),
            Mod("Improved Garrisons", "2859265386", "ImprovedGarrisons", pinned: true),
            Mod("More Nations Remastered", "0", "MoreNationsRemastered"),
            Mod("MoreNationsRemastered COOP Fix", "0", "MoreNationsRemasteredFix"),
            Mod("RTS Camera", "3747725551", "RTSCamera"),
            Mod("RTS Camera Command System", "3747771970", "RTSCamera.CommandSystem"),
            Mod("Dismemberment (Plus)", "2875093027", "DismembermentPlus"),
            Mod("Banner Fix", "0", "BannerFix", pinned: true),
            Mod("Raise your Banner", "0", "RaiseYourBanner", pinned: true),
            Mod("Raise your Torch", "0", "RaiseYourTorch", pinned: true),
            Mod("Perfect Fire Arrows", "0", "PerfectFireArrows", pinned: true),
            Mod("Alive Scenes", "0", "AliveScenes", pinned: true),
            Mod("Immersive Battlefields", "0", "ImmersiveBattlefields", pinned: true),
            Mod("Agriculture Estate", "2880158010", "AgricultureEstate"),
            Mod("Player Settlement", "3720376888", "PlayerSettlement"),
            Mod("PSCoopBridge", "0", "PSCoopBridge", pinned: true)
        };
        profile.ModuleLoadOrder = new List<string>
        {
            "Bannerlord.Harmony",
            "Bannerlord.ButterLib",
            "Bannerlord.UIExtenderEx",
            "Bannerlord.MBOptionScreen",
            "Native",
            "SandBoxCore",
            "SandBox",
            "Coop",
            "XorberaxLegacy",
            "SettlementCultureDrift",
            "CoopModPatch",
            "ImprovedGarrisons",
            "MoreNationsRemastered",
            "MoreNationsRemasteredFix",
            "RTSCamera",
            "RTSCamera.CommandSystem",
            "DismembermentPlus",
            "BannerFix",
            "RaiseYourBanner",
            "RaiseYourTorch",
            "PerfectFireArrows",
            "AliveScenes",
            "ImmersiveBattlefields",
            "AgricultureEstate",
            "PlayerSettlement",
            "PSCoopBridge"
        };
        if (includeMoreNations)
            return;

        var mapMods = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "MoreNationsRemastered",
            "MoreNationsRemasteredFix"
        };
        profile.Mods.RemoveAll(mod => mapMods.Contains(mod.ModuleId));
        profile.ModuleLoadOrder.RemoveAll(mapMods.Contains);
    }

    public static IReadOnlyList<string> WorkingOrder(bool includeMoreNations)
    {
        var profile = new ServerProfile();
        Apply(profile, includeMoreNations);
        return profile.ModuleLoadOrder;
    }

    // The Plain Coop (no mods) setup: the game's own modules and Coop, nothing else. It matches a
    // Coodsz Host Plain Coop server, which loads only Native, SandBoxCore, Sandbox and Coop (and
    // its own DedicatedServer.* modules, which Coop leaves out of the join check).
    public static readonly IReadOnlyList<string> PlainOrder = ["Native", "SandBoxCore", "SandBox", "Coop"];

    public static void ApplyPlain(ServerProfile profile)
    {
        profile.BaseModules = new List<string> { "Native", "SandBoxCore", "SandBox" };
        profile.Mods = new List<ProfileMod> { Mod("Bannerlord Coop", "3770450698", "Coop", pinned: true) };
        profile.ModuleLoadOrder = PlainOrder.ToList();
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
