using BannerlordCoopLauncher.Core.Profile;

namespace TaomCoop.Core;

/// <summary>What one row of the Test mods window shows as its last test or live state.</summary>
public enum ModTestRowState
{
    NotTested,
    Waiting,
    Testing,
    Passed,
    Failed
}

/// <summary>
/// The text of the "Test mods on the server" window, kept out of the window so it can be tested:
/// the mod list badge of a row, the last saved result of a mod, the pill text, the selected count,
/// the progress line, the elapsed clock and the passed and failed summary.
/// </summary>
public static class ModTestView
{
    /// <summary>The short mod list badge of a mod (Tested in Coop, Loads on the server, Works in TAOM,
    /// Risky or Broken) and the entry it comes from, or null when the list does not know the mod.</summary>
    public static (string Text, CompatEntry Entry)? ListBadge(TaomCompatList list, ModTestCandidate mod)
    {
        var entry = list.Find(mod.ModuleId, mod.WorkshopId, mod.Name);
        if (entry == null)
            return null;
        var text = entry.Kind switch
        {
            CompatTag.Risky => "Risky",
            CompatTag.Broken => "Broken",
            _ => TaomModList.GroupOf(entry) switch
            {
                CompatGroup.TestedInCoop => "Tested in Coop",
                CompatGroup.LoadsOnServer => "Loads on the server",
                _ => "Works in TAOM"
            }
        };
        return (text, entry);
    }

    /// <summary>The newest saved result of the mod: the same version first, else its newest result
    /// with another version. Results are newest last, as the results file keeps them.</summary>
    public static ModTestResult? LastResult(IEnumerable<ModTestResult> results, ModTestCandidate mod)
    {
        var mine = results.Where(result => result.Id.Equals(mod.ModuleId, StringComparison.OrdinalIgnoreCase)).ToList();
        return mine.LastOrDefault(result => result.Version == mod.Version) ?? mine.LastOrDefault();
    }

    /// <summary>The newest saved result of each mod that is not in the window's list, newest first.</summary>
    public static IReadOnlyList<ModTestResult> OtherResults(IEnumerable<ModTestResult> results, IEnumerable<ModTestCandidate> mods)
    {
        var listed = new HashSet<string>(mods.Select(mod => mod.ModuleId), StringComparer.OrdinalIgnoreCase);
        return results.Select((result, index) => (Result: result, Index: index))
            .Where(pair => !listed.Contains(pair.Result.Id))
            .GroupBy(pair => pair.Result.Id, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.Last())
            .OrderByDescending(pair => pair.Index)
            .Select(pair => pair.Result)
            .ToList();
    }

    public static ModTestRowState StateOf(ModTestResult? result) =>
        result == null ? ModTestRowState.NotTested : result.Passed ? ModTestRowState.Passed : ModTestRowState.Failed;

    public static string PillText(ModTestRowState state) => state switch
    {
        ModTestRowState.Waiting => "Waiting",
        ModTestRowState.Testing => "Testing",
        ModTestRowState.Passed => "Passed",
        ModTestRowState.Failed => "Failed",
        _ => "Not tested"
    };

    public static string SelectedLine(int picked, int total) => picked + " of " + total + " selected";

    public static string ProgressLine(ModTestProgress step) => "Testing " + step.Index + " of " + step.Count + ", " + step.Mod.Label;

    /// <summary>Elapsed time as m:ss, or h:mm:ss from an hour on.</summary>
    public static string Clock(TimeSpan time)
    {
        if (time < TimeSpan.Zero)
            time = TimeSpan.Zero;
        var culture = System.Globalization.CultureInfo.InvariantCulture;
        return time.TotalHours >= 1
            ? ((int)time.TotalHours).ToString(culture) + ":" + time.Minutes.ToString("00", culture) + ":" + time.Seconds.ToString("00", culture)
            : ((int)time.TotalMinutes).ToString(culture) + ":" + time.Seconds.ToString("00", culture);
    }

    /// <summary>The compact line under the list: how many listed mods passed, failed or are not tested.</summary>
    public static string Summary(IReadOnlyList<ModTestCandidate> mods, IEnumerable<ModTestResult> results)
    {
        var saved = results.ToList();
        var passed = 0;
        var failed = 0;
        foreach (var mod in mods)
        {
            switch (StateOf(LastResult(saved, mod)))
            {
                case ModTestRowState.Passed:
                    passed++;
                    break;
                case ModTestRowState.Failed:
                    failed++;
                    break;
            }
        }

        if (passed + failed == 0)
            return "No mod in this list was tested yet.";
        var untested = mods.Count - passed - failed;
        return untested > 0
            ? passed + " passed, " + failed + " failed, " + untested + " not tested."
            : passed + " passed, " + failed + " failed.";
    }
}
