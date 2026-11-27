using BannerlordCoopLauncher.Core.Parsing;
using BannerlordCoopLauncher.Core.Profile;

namespace TaomCoop.Core;

/// <summary>
/// "TAOM plus": a built in, locked setup with the safe starter set from the 2026-10-17 research
/// for Bannerlord 1.4.8, TAOM 2.0.29.5 and Coop 0.1.6. Class A looks only mods plus the proven
/// Coop ones, in the order Coodsz Coop and Friends uses (ServerModuleSet.WorkingOrder). Mods that
/// are not on the Workshop (Workshop id "0") come with the tool folder like in Coop and Friends;
/// the rest come through Install mods. It has extra mods, so the host needs the Coodsz TAOM server
/// (the TAOM server tool starts it when the host picks TAOM plus).
/// Left out on purpose: Player Settlement, PSCoopBridge and AgricultureEstate (not tested with
/// TAOM yet), Perfect Fire Arrows (changes battles), Performance Optimizer (test it alone first).
/// </summary>
public static class TaomPlus
{
    public const string Name = "TAOM plus";

    // CoopModPatch (with its Coop adapter for Improved Garrisons) and Improved Garrisons are campaign
    // logic mods that only go together. Set this to false to drop both from TAOM plus at once if the
    // live test shows problems; everything else stays.
    public const bool IncludeCoopLogicPair = true;

    public static readonly IReadOnlyList<string> CoopLogicPair = ["CoopModPatch", "ImprovedGarrisons"];

    // Id, name and Workshop id, in load order, the Coop and Friends order: Xorberax, CoopModPatch,
    // Improved Garrisons, Dismemberment, Banner Fix before Raise your Banner, Raise your Torch. Better
    // Exception Window, CC's Banners and Immersive Battlefields are looks only. RTS Camera and its
    // Command System go last: TAOM players found RTS Camera works at the very bottom of the load order.
    private static readonly (string Id, string Name, string WorkshopId)[] AllMods =
        ("BetterExceptionWindow|Better Exception Window|2875478172;Bannerlord.CCsBanners|CC's Banners|2877445271;"
            + "XorberaxLegacy|Xorberax's Legacy|2875138158;CoopModPatch|CoopModPatch|3786391685;ImprovedGarrisons|Improved Garrisons|2859265386;"
            + "DismembermentPlus|Dismemberment Plus|2875093027;BannerFix|Banner Fix|0;RaiseYourBanner|Raise your Banner|0;"
            + "RaiseYourTorch|Raise your Torch|0;ImmersiveBattlefields|Immersive Battlefields|0;"
            + "RTSCamera|RTS Camera|3747725551;RTSCamera.CommandSystem|RTS Camera Command System|3747771970")
        .Split(';')
        .Select(item => item.Split('|'))
        .Select(parts => (parts[0], parts[1], parts[2]))
        .ToArray();

    private static IEnumerable<(string Id, string Name, string WorkshopId)> Mods =>
        AllMods.Where(mod => IncludeCoopLogicPair || !CoopLogicPair.Contains(mod.Id));

    public static IReadOnlyList<string> ModuleIds => Mods.Select(mod => mod.Id).ToList();

    /// <summary>Mods that ship next to the tools instead of on the Workshop.</summary>
    public static IReadOnlyList<string> BundledIds => Mods.Where(mod => mod.WorkshopId == "0").Select(mod => mod.Id).ToList();

    public static List<TaomExtraMod> Extras() =>
        Mods.Select(mod => new TaomExtraMod { Name = mod.Name, ModuleId = mod.Id, WorkshopId = mod.WorkshopId, Enabled = true, Slot = ExtraSlot.AfterTaom }).ToList();

    /// <summary>The bundled TAOM plus mods in the tool's bundled mods folder that the found mods do not have yet.</summary>
    public static IReadOnlyList<FoundMod> BundledNextTo(string folder, IEnumerable<FoundMod> found)
    {
        var have = new HashSet<string>(found.Select(mod => mod.ModuleId), StringComparer.OrdinalIgnoreCase);
        var list = new List<FoundMod>();
        foreach (var id in BundledIds.Where(id => !have.Contains(id)))
        {
            var directory = Path.Combine(folder, id);
            var manifest = ModuleManifestReader.Read(directory);
            if (manifest != null && manifest.Id.Equals(id, StringComparison.OrdinalIgnoreCase))
                list.Add(new FoundMod(manifest.Name, manifest.Id, "0", directory, "From Nexus or ModDB"));
        }

        return list;
    }
}
