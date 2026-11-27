namespace BannerlordCoopLauncher.Core.Modules;

/// <summary>
/// The modules Coodsz wrote (compat\*\SubModule.xml and the Server Tool's own modules), which get the Coodsz module
/// chip on a mod row, and the ones bundled from another author with permission, which get Bundled. TAOM.CoopCompat is
/// not Coodsz's, and Coodsz War Sync rides inside DedicatedServer.Windows, so neither is a row of its own here.
/// </summary>
public static class CoodszModuleIds
{
    public static readonly IReadOnlySet<string> Coodsz = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "DedicatedServer.CoodszJoinPassword",
        "DedicatedServer.CoodszMenuGuard",
        Keep.ToolsCommand.ModuleId,
        Support.AutoRescue.ModuleId,
        Support.AutoRescue.LegacyModuleId,
        "DedicatedServer.IGCoopCompat",
        "Diplomacy.CoopCompat",
        "Limitless.CoopCompat",
        "StuckBattleFix",
        "BurningEmpires.CoopCompat",
        "CoodszImageExporter",
    };

    // SkyApolo's AD1259 modules, shipped with the AD1259 tools with his permission (AD1259Stack).
    public static readonly IReadOnlySet<string> Bundled = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        AD1259.AD1259Stack.CompatModuleId,
        AD1259.AD1259Stack.ClientFixModuleId,
    };

    public static bool IsCoodsz(string? moduleId) => moduleId != null && Coodsz.Contains(moduleId.Trim());

    public static bool IsBundled(string? moduleId) => moduleId != null && Bundled.Contains(moduleId.Trim());
}
