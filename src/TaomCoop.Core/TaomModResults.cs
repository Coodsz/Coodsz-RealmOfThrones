using BannerlordCoopLauncher.Core.Modules;

namespace TaomCoop.Core;

/// <summary>
/// The TAOM tools' side of the per PC results (ModResults): which rows get the Unknown, not tested
/// badge, and the results the server load test and Find the crashing mod write.
/// </summary>
public static class TaomModResults
{
    /// <summary>True for a mod that should show a badge: not an official module, Coop, a
    /// DedicatedServer.* module, or a module of the TAOM core.</summary>
    public static bool IsExtraMod(string? moduleId)
    {
        var id = (moduleId ?? "").Trim();
        return UnknownModBadge.Applies(id)
            && !TaomStack.ClientModules.Contains(id, StringComparer.OrdinalIgnoreCase)
            && !TaomStack.ServerModules.Contains(id, StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>True when the TAOM list has nothing to say about the mod, so the grey
    /// Unknown, not tested badge shows.</summary>
    public static bool ShowsUnknown(TaomCompatList list, string? moduleId, string? workshopId, string? name)
    {
        if (!IsExtraMod(moduleId))
            return false;
        var entry = list.Find(moduleId, workshopId, name);
        return entry == null || TaomCompatList.BadgeText(entry.Kind) == null;
    }

    /// <summary>A "Test mods on the server" result as this PC's result: passed or failed on your server.</summary>
    public static ModResult FromServerTest(ModTestResult result, string? date = null) => new()
    {
        Id = result.Id,
        Version = result.Version,
        Result = result.Passed ? ModResult.Passed : ModResult.Failed,
        Where = ModResult.YourServer,
        Date = date ?? result.Date,
        Reason = result.Passed
            ? "Passed the server load test. Joining was not tested."
            : result.Reason.Trim().Length > 0 ? "Failed the server load test. " + result.Reason.Trim() : "Failed the server load test."
    };

    /// <summary>The mods turned on in a Find the crashing mod round that reached the main menu.</summary>
    public static IReadOnlyList<ModResult> CrashRoundPassed(IEnumerable<(string Id, string? Version)> turnedOn, string? date = null) =>
        turnedOn.Where(mod => IsExtraMod(mod.Id)).Select(mod => new ModResult
        {
            Id = mod.Id,
            Version = mod.Version ?? "",
            Result = ModResult.Worked,
            Where = ModResult.ThisPc,
            Date = date ?? ModResults.Now(),
            Reason = "Reached the main menu in a Find the crashing mod round."
        }).ToList();

    /// <summary>The mod Find the crashing mod blamed.</summary>
    public static ModResult CrashFound(string id, string? version, string? date = null) => new()
    {
        Id = id,
        Version = version ?? "",
        Result = ModResult.Crashed,
        Where = ModResult.ThisPc,
        Date = date ?? ModResults.Now(),
        Reason = "Find the crashing mod found that the game crashes before the main menu with this mod."
    };
}
