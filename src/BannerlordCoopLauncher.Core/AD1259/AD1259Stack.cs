using System.Numerics;
using System.Text.RegularExpressions;

namespace BannerlordCoopLauncher.Core.AD1259;

/// <summary>One mod of the AD1259 set: where players get it and the exact version every PC must have.
/// AlsoAccepted: older versions still taken while Steam moves players over. Both show Ready, but Coop itself refuses a
/// join when the server and the player run different Coop versions, so a 0.1.6 host and a 0.1.7 player still can't play.
/// The Open Source mods also take their 1.x set (Nexus files "FOR v1.2.x"), which AD1259 1.4.6 was made for (OpenSourceSet).</summary>
public sealed record AD1259Mod(string Id, string Name, string Version, string? WorkshopId = null, int? NexusId = null, bool BaseGame = false,
    IReadOnlyList<string>? AlsoAccepted = null)
{
    public string? Link => WorkshopId != null
        ? "https://steamcommunity.com/sharedfiles/filedetails/?id=" + WorkshopId
        : NexusId != null
        ? "https://www.nexusmods.com/mountandblade2bannerlord/mods/" + NexusId
        : null;

    public string Source => BaseGame ? "Bannerlord" : WorkshopId != null ? "Steam Workshop" : "Nexus Mods";
}

/// <summary>
/// The locked AD1259 set, proven on a Coop 0.1.6 dedicated server on 2026-10-13: these mods,
/// these versions, in this order. The server and every player must match.
/// </summary>
public static class AD1259Stack
{
    public const string GameVersion = "v1.4.8";
    public const string AD1259Version = "1.4.6";
    public const string CoopWorkshopId = Steam.SteamWorkshopAcf.CoopWorkshopId;
    public const string SetText = "Bannerlord 1.4.8, AD1259 1.4.6, Coop 0.1.7, RBM 4.5.4";
    public const string NewerAdvice = "Update this tool first. Install update shows at the top when there is one, or get the newest release.";

    // The AD1259 tools never take the Coop nightly (it moved to Bannerlord 1.5.7), so no tool update fixes one and the
    // refusals say how to go back instead of "newer than this tool supports".
    public const string NightlyNeeds = "AD1259 needs Bannerlord 1.4.8 and Coop 0.1.7 from the Steam Workshop, never the Coop nightly.";

    /// <summary>
    /// The readable AD1259 mod list with the player counts: MODS.md in the Coodsz-AD1259 repository,
    /// written by its tally from compat/ad1259-mod-compat.json (seeded from <see cref="ClientMods"/>).
    /// </summary>
    public const string ModsPageUrl = "https://github.com/" + Updates.ReleaseNotice.AD1259Repo + "/blob/main/MODS.md";

    // SkyApolo's server-only compatibility module, bundled with permission. Coop leaves
    // every DedicatedServer.* module out of the join check, so players never need it.
    public const string CompatModuleId = "DedicatedServer.AD1259CoopCompat";
    public const string CompatName = "AD1259 - Coop Compatibility";
    public const string CompatVersion = "v1.1.1";
    public const string CompatCredit = "AD1259 - Coop Compatibility is by SkyApolo, bundled with permission.";

    // SkyApolo's client module, bundled with permission: stops a map-camera crash when AD1259's terrain
    // IDs go past the game's 26 ambient sounds. Loaded after Coop; the server does not need it.
    public const string ClientFixModuleId = "DedicatedServer.AD1259CoopClientFix";
    public const string ClientFixName = "AD1259 - Coop Client Stability";
    public const string ClientFixVersion = "v1.0.1";
    public const string ClientFixCredit = "AD1259 - Coop Client Stability is by SkyApolo, bundled with permission.";

    // The Expanded Armoury fix (Nexus 11487, file "FIX") adds this client DLL; without it the
    // armour tableau can crash the game. The server does not need it.
    public const string ArmouryFixDll = "EBA.TableauCrashFix.dll";
    public const int ArmouryNexusId = 11487;

    /// <summary>Client load order, exactly as the accepted test join loaded it.</summary>
    public static readonly AD1259Mod[] ClientMods =
    [
        new("Bannerlord.Harmony", "Harmony", "v2.4.2.248", WorkshopId: "2859188632"),
        new("Bannerlord.ButterLib", "ButterLib", "v2.12.0", WorkshopId: "2859232415"),
        new("Bannerlord.UIExtenderEx", "UIExtenderEx", "v3.0.3", WorkshopId: "2859222409"),
        // The Workshop MCM updated itself to 5.13.1 on 2026-11-04; the Nexus 5.12.3 still counts. The server gets the
        // copy the host's game loads (AD1259Server.GoodCopy), since Coop refuses a join on another MCM version.
        new("Bannerlord.MBOptionScreen", "Mod Configuration Menu v5", "v5.13.1", NexusId: 612, AlsoAccepted: ["v5.12.3"]),
        new("Native", "Native", GameVersion, BaseGame: true),
        new("SandBoxCore", "SandBox Core", GameVersion, BaseGame: true),
        new("Sandbox", "SandBox", GameVersion, BaseGame: true),
        new("StoryMode", "Story Mode", GameVersion, BaseGame: true),
        new("CustomBattle", "Custom Battle", GameVersion, BaseGame: true),
        new("RBM", "Realistic Battle Mod", "v4.5.4", WorkshopId: "2859251492"),
        new("ExpandedArmouryBL", "Expanded Armoury", "e1.5.2", NexusId: ArmouryNexusId),
        new("swadian armoury", "Swadian Armoury", "v1.0.0", WorkshopId: "2875090166"),
        new("OpenSourceArmory", "Open Source Armory", "v2.0.0", WorkshopId: "3011479883", AlsoAccepted: ["v1.18.0"]),
        new("OpenSourceSaddlery", "Open Source Saddlery", "v2.0.0", WorkshopId: "3010990914", AlsoAccepted: ["v1.8.0"]),
        new("OpenSourceWeaponry", "Open Source Weaponry", "v2.0.1", WorkshopId: "3010984416", AlsoAccepted: ["v1.8.0"]),
        new("Templar Armor", "Templar Armor", "v1.0.3", NexusId: 4736),
        new("Templar Equipment", "Templar Equipment", "v1.0.3", NexusId: 4736),
        new("SettlementIconRedesign", "Settlement Icons - Redesign", "v1.1.4", NexusId: 2717),
        new("AD1259", "Anno Domini 1259", "v1.4.6", NexusId: 6723),
        new("Coop", "Bannerlord Coop", "v0.1.7", WorkshopId: CoopWorkshopId, AlsoAccepted: ["v0.1.6"])
    ];

    public static IReadOnlyList<string> ClientModules => ClientMods.Select(mod => mod.Id).ToList();

    /// <summary>What Connect loads: the player's mods, then each bundled client module that is installed.</summary>
    public static IReadOnlyList<string> LaunchModules(bool withClientFix, bool withJoinPassword = false)
    {
        var modules = ClientModules.ToList();
        if (withClientFix)
            modules.Add(ClientFixModuleId);
        if (withJoinPassword)
            modules.Add(Launch.ClientAddOns.JoinPasswordId);
        return modules;
    }

    /// <summary>
    /// Server load order as tested: the engine module after Native, Coop right after the base game,
    /// no StoryMode or CustomBattle, and the compatibility module last (after AD1259).
    /// </summary>
    public static readonly string[] ServerModules =
    [
        "Bannerlord.Harmony",
        "Bannerlord.ButterLib",
        "Bannerlord.UIExtenderEx",
        "Bannerlord.MBOptionScreen",
        "Native",
        "DedicatedServer.Windows",
        "SandBoxCore",
        "Sandbox",
        "Coop",
        "RBM",
        "ExpandedArmouryBL",
        "swadian armoury",
        "OpenSourceArmory",
        "OpenSourceSaddlery",
        "OpenSourceWeaponry",
        "Templar Armor",
        "Templar Equipment",
        "SettlementIconRedesign",
        "AD1259",
        CompatModuleId
    ];

    // Modules whose raw Assets folder crashes the headless server (0xC0000005 right after
    // "Loading packages $BASE/Modules/swadian armoury/Assets"). Players keep theirs.
    public static readonly string[] ServerHiddenAssets = ["AD1259", "swadian armoury"];

    public static AD1259Mod? Find(string id) =>
        ClientMods.FirstOrDefault(mod => mod.Id.Equals(id, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// True when a SubModule.xml version is the one the set needs. The base game modules only need
    /// the same major.minor.patch (the build number differs between the client and the server);
    /// every other mod must match exactly.
    /// </summary>
    public static bool VersionMatches(AD1259Mod mod, string? found)
    {
        var have = (found ?? "").Trim();
        if (mod.BaseGame)
            return Normalize(have).StartsWith(Normalize(mod.Version), StringComparison.OrdinalIgnoreCase);
        return Normalize(have).Equals(Normalize(mod.Version), StringComparison.OrdinalIgnoreCase)
            || (mod.AlsoAccepted ?? []).Any(version => Normalize(have).Equals(Normalize(version), StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>True when the found version is numerically newer than the version this tool supports.</summary>
    public static bool IsNewerThanPinned(AD1259Mod mod, string? found)
    {
        if (VersionMatches(mod, found) || !TryVersionParts(found, out var foundParts)
            || !TryVersionParts(mod.Version, out var pinnedParts))
            return false;

        var partCount = mod.BaseGame ? 3 : Math.Max(foundParts.Length, pinnedParts.Length);
        for (var index = 0; index < partCount; index++)
        {
            var foundPart = index < foundParts.Length ? foundParts[index] : 0;
            var pinnedPart = index < pinnedParts.Length ? pinnedParts[index] : 0;
            if (foundPart != pinnedPart)
                return foundPart > pinnedPart;
        }

        return false;
    }

    /// <summary>
    /// True for a Coop nightly: a Coop SubModule.xml version newer than the pinned Coop (every nightly says v0.1.8), or a
    /// Coop build (CoopBuildId, Common.dll's ProductVersion, 0.1.7+commit for the Workshop Coop) newer than it, which also
    /// catches nightly files copied over a Workshop Coop that kept its SubModule.xml.
    /// </summary>
    public static bool IsNightlyCoop(string? version, string? build = null)
    {
        var coop = Find(Modules.BannerlordCoopModules.Coop)!;
        return IsNewerThanPinned(coop, version) || IsNewerThanPinned(coop, Modules.CoopBuildId.Version(build));
    }

    private static bool TryVersionParts(string? version, out BigInteger[] parts)
    {
        parts = [];
        if (string.IsNullOrWhiteSpace(version))
            return false;
        var match = Regex.Match(version.Trim(), @"^([a-zA-Z]?)(\d+(?:\.\d+)*)$");
        if (!match.Success)
            return false;
        var values = new List<BigInteger>();
        foreach (var value in match.Groups[2].Value.Split('.'))
        {
            if (!BigInteger.TryParse(value, out var part))
                return false;
            values.Add(part);
        }
        parts = values.ToArray();
        return true;
    }

    /// <summary>True when two versions are the same once read like VersionMatches does ("v1.18.0", "1.18", "v1.18.0.0").</summary>
    public static bool SameVersion(string? a, string? b) =>
        Normalize((a ?? "").Trim()).Equals(Normalize((b ?? "").Trim()), StringComparison.OrdinalIgnoreCase);

    // "v2.12.0.0", "v2.12.0", "v2.12" and "2.12.0" are the same version: Bannerlord pads to four parts, and the Nexus
    // Open Source 1.x files may write theirs without the v. Another letter ("e1.5.2") stays part of the version.
    private static string Normalize(string version)
    {
        var match = Regex.Match(version, @"^([a-zA-Z]?)(\d+(?:\.\d+)*)");
        if (!match.Success)
            return version;
        var parts = match.Groups[2].Value.Split('.').ToList();
        while (parts.Count > 3 && parts[^1] == "0")
            parts.RemoveAt(parts.Count - 1);
        while (parts.Count < 3)
            parts.Add("0");
        var prefix = match.Groups[1].Value.ToLowerInvariant();
        return (prefix == "v" ? "" : prefix) + string.Join(".", parts);
    }
}
