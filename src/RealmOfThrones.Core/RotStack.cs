using BannerlordCoopLauncher.Core.Modules;

namespace RealmOfThrones.Core;

/// <summary>One library mod Realm of Thrones needs, with the version the join tests ran. The tools do
/// not pin these: the host's installed version is the truth and players match it.</summary>
public sealed record RotLibrary(string ModuleId, string Name, string WorkshopId, string TestedVersion);

/// <summary>The RoT build of Coop: its build string (Common.dll's ProductVersion), the date in its name and the
/// client and server zips with their SHA-256.</summary>
public sealed record RotCoopBuild(string Build, string LabelDate, string ClientZip, string ClientZipSha256, string ServerZip, string ServerZipSha256)
{
    /// <summary>The build as players read it, for example RoT build 2026-11-14 (01846e1).</summary>
    public string Label => "RoT build " + LabelDate + " (" + (CoopBuildId.Commit(Build) is { } commit ? commit[..Math.Min(7, commit.Length)] : Build) + ")";
}

/// <summary>
/// The locked Realm of Thrones Coop set. The core is the four library mods, the game modules with
/// War Sails (NavalDLC), Coop, the four Realm of Thrones modules and Realm of Thrones Coop Compat last,
/// in the order the join tests on the Coop nightly used.
/// </summary>
public static class RotStack
{
    public const string GameVersion = "v1.4.8";
    public const string Name = "Realm of Thrones";

    // The four Realm of Thrones modules are pinned. ROT_Map's folder is named ROT-Map, its id is ROT_Map.
    public const string CoreModuleId = "ROT-Core";
    public const string ContentModuleId = "ROT-Content";
    public const string MapModuleId = "ROT_Map";
    public const string DragonModuleId = "ROT-Dragon";
    public const string RotVersion = "v8.1.8";
    public const string MapVersion = "v8.1.8.2";

    public const string CompatModuleId = "RealmOfThrones.CoopCompat";
    public const string CompatName = "Realm of Thrones Coop Compat";
    public const string CompatVersion = "v0.1.1";

    // The one place for the Coop module. Today Realm of Thrones runs on the Coop nightly (CoopNightly v0.1.8,
    // War Sails required). The public Coop release may use another module id and version: change them here.
    // The nightly is not on the Workshop, so its shared list entry has no Workshop id.
    public const string CoopModuleId = "CoopNightly";
    public const string CoopVersion = "v0.1.8";
    // The one Coop build Realm of Thrones runs on, the RoT build of Coop the tools carry (tools\Build-RotCoop.ps1).
    // Coop only lets a player join a server of the very same build (CoopBuildId: Common.dll's ProductVersion, the
    // version and the commit), and every nightly says v0.1.8 above. The zips go into the exes (src\RotCoopBuild.targets)
    // with these names and hashes. They are not release files. A new build changes all of it.
    // The server zip is pinned as built: obfuscation makes its bytes differ on every rebuild, so it is never rebuilt.
    public static readonly RotCoopBuild CoopBuild = new(
        "0.1.8+01846e136cf38cf64c79d9d4014ddb2e01907de3",
        "2026-11-14",
        "Coop-RoT-Client-2026-11-14-01846e1.zip",
        "FBC78E7B8594B2F2E2644FA6EB35ED28972B1CE934898D27CBB18E28DB1E908F",
        "Coop-RoT-Server-2026-11-14-01846e1.zip",
        "5AAC1C6E91EFB34A8E53D96248DD0E5C119753FA5BD1337315B59B0B16C605EA");
    public const string CoopWorkshopId = "0";
    public const string DirectPort = "4200";

    /// <summary>The Coop module ids that are not CoopModuleId. Loading one next to it is two Coop copies.</summary>
    public static readonly IReadOnlyList<string> OtherCoopModuleIds =
        new[] { "Coop", "CoopNightly" }.Where(id => !id.Equals(CoopModuleId, StringComparison.OrdinalIgnoreCase)).ToArray();

    public const string NavalModuleId = "NavalDLC";

    public static readonly RotLibrary Harmony = new("Bannerlord.Harmony", "Harmony", "2859188632", "v2.4.2.248");
    public static readonly RotLibrary ButterLib = new("Bannerlord.ButterLib", "ButterLib", "2859232415", "v2.12.0");
    public static readonly RotLibrary UIExtenderEx = new("Bannerlord.UIExtenderEx", "UIExtenderEx", "2859222409", "v3.0.3");
    public static readonly RotLibrary Mcm = new("Bannerlord.MBOptionScreen", "MCM", "2859238197", "v5.13.1");

    /// <summary>The oldest MCM the dedicated server loads (BeStack.McmServerMinimum); an older one crashes it.</summary>
    public const string McmServerMinimum = "v5.12.0";

    // The official MCM Workshop item 2859238197 was removed, so the client launcher sends players to MCM on
    // Nexus Mods (mod 612), the same page as the Burning Empires tools (BeStack.Mcm).
    public const string McmNexusPage = "https://www.nexusmods.com/mountandblade2bannerlord/mods/612";

    /// <summary>The library mods in load order.</summary>
    public static readonly IReadOnlyList<RotLibrary> Libraries = [Harmony, ButterLib, UIExtenderEx, Mcm];

    /// <summary>The pinned versions of the Realm of Thrones modules and our compat module.</summary>
    public static readonly IReadOnlyDictionary<string, string> ContentVersions =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            [CoreModuleId] = RotVersion,
            [ContentModuleId] = RotVersion,
            [MapModuleId] = MapVersion,
            [DragonModuleId] = RotVersion,
            [CompatModuleId] = CompatVersion
        };

    /// <summary>The four Realm of Thrones modules in load order.</summary>
    public static readonly IReadOnlyList<string> RotModules = [CoreModuleId, ContentModuleId, MapModuleId, DragonModuleId];

    // The client order of the join tests on the Coop nightly (rot-wave8 client logs try1 and try2, the
    // Command Args line of rgl_log), the compat module last.
    public static readonly string[] ClientModules =
    [
        "Bannerlord.Harmony",
        "Bannerlord.ButterLib",
        "Bannerlord.UIExtenderEx",
        "Bannerlord.MBOptionScreen",
        "Native",
        "SandBoxCore",
        "Sandbox",
        "CustomBattle",
        "StoryMode",
        NavalModuleId,
        CoopModuleId,
        CoreModuleId,
        ContentModuleId,
        MapModuleId,
        DragonModuleId,
        CompatModuleId
    ];

    /// <summary>What the dedicated server always loads before the "modules" list of its server-config.json.
    /// NavalDLC is one of them and never goes into that list.</summary>
    public static readonly IReadOnlyList<string> ServerLoadsFirst = ["Native", "SandBoxCore", "Sandbox", CoopModuleId, NavalModuleId];

    /// <summary>What the dedicated server always loads after that list.</summary>
    public const string ServerLoadsLast = "DedicatedServer.Windows";

    /// <summary>The "modules" list of server-config.json for the locked setup: the library mods, the Realm of
    /// Thrones modules and the compat module last.</summary>
    public static readonly string[] ServerModules =
    [
        "Bannerlord.Harmony",
        "Bannerlord.ButterLib",
        "Bannerlord.UIExtenderEx",
        "Bannerlord.MBOptionScreen",
        CoreModuleId,
        ContentModuleId,
        MapModuleId,
        DragonModuleId,
        CompatModuleId
    ];

    public static bool IsLibrary(string? moduleId) =>
        moduleId != null && Libraries.Any(mod => mod.ModuleId.Equals(moduleId.Trim(), StringComparison.OrdinalIgnoreCase));

    public static bool IsRotModule(string? moduleId) =>
        moduleId != null && RotModules.Contains(moduleId.Trim(), StringComparer.OrdinalIgnoreCase);
}
