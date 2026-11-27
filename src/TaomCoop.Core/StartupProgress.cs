using System.Text.RegularExpressions;

namespace TaomCoop.Core;

/// <summary>One reading of the start for the loading bar.</summary>
/// <param name="Fraction">How far along the start is, 0 to 1, or null while no number is known yet.</param>
/// <param name="Stage">boot, loading, settlements, neighbors or serving.</param>
public sealed record StartupStep(double? Fraction, string Stage);

/// <summary>
/// What a starting server is busy with before SERVING. On a new TAOM campaign the server rejects
/// TAOM_Map's distance cache (navigation CRC mismatch) and builds its own: about 1002 settlement steps
/// (about one a second), then 221 neighbour-cache steps (about 10 seconds each), so close to an hour.
/// Without this hosts think the server is stuck and restart it, which starts the build over.
/// </summary>
public static class StartupProgress
{
    private static readonly Regex Neighbor = new(
        @"Neighbor cache progress for navigation \S+, current index: (\d+)\s*-\s*total count: (\d+)", RegexOptions.Compiled);

    private static readonly Regex Settlement = new(
        @"Settlement to settlement cache creation index (\d+),\s*total count: (\d+)", RegexOptions.Compiled);

    public const string Generating = "generating native distance cache";

    /// <summary>A short status for the server log text, or null when the server is not building the map cache.</summary>
    public static string? Describe(string log)
    {
        if (string.IsNullOrEmpty(log))
            return null;
        if (LastStep(Neighbor, log) is (int index, int total))
        {
            var done = Math.Min(index + 1, total);
            return "Building the map cache: " + done + " of " + total + " (" + done * 100 / total + "%). "
                + "First start of a new campaign, it can take up to an hour. Do not stop the server, or it starts over.";
        }

        if (LastStep(Settlement, log) is (int settlementIndex, int settlementTotal))
        {
            var done = Math.Min(settlementIndex + 1, settlementTotal);
            return "Building the distances between settlements: " + done + " of " + settlementTotal + " (" + done * 100 / settlementTotal + "%). "
                + "First start of a new campaign, it can take up to an hour. Do not stop the server, or it starts over.";
        }

        return log.Contains(Generating, StringComparison.Ordinal)
            ? "Building the map's travel distances. First start of a new campaign, it can take up to an hour. Do not stop the server, or it starts over."
            : null;
    }

    /// <summary>
    /// Where the start is for the loading bar. Boot and loading are small fixed steps, the settlement
    /// distances and the neighbour cache fill most of the bar, serving is full. Null after a fatal state
    /// or for an empty log. Fraction is null while the server runs but no step is known yet.
    /// </summary>
    public static StartupStep? Measure(string? log)
    {
        if (string.IsNullOrEmpty(log))
            return null;
        var state = ServerStateEvent.Last(log);
        if (state is { Fatal: true })
            return null;
        if (state is { Serving: true } || ServerStateEvent.HasServingLine(log))
            return new StartupStep(1, "serving");
        if (LastStep(Neighbor, log) is (int index, int total))
            return new StartupStep(0.40 + 0.55 * Math.Min(index + 1, total) / total, "neighbors");
        if (LastStep(Settlement, log) is (int settlementIndex, int settlementTotal))
            return new StartupStep(0.10 + 0.30 * Math.Min(settlementIndex + 1, settlementTotal) / settlementTotal, "settlements");
        if (log.Contains(Generating, StringComparison.Ordinal) || log.Contains("distance cache rejected", StringComparison.Ordinal))
            return new StartupStep(0.10, "settlements");
        return state?.Phase.ToLowerInvariant() switch
        {
            "boot" => new StartupStep(0.03, "boot"),
            "loading" => new StartupStep(0.08, "loading"),
            _ => new StartupStep(null, "boot")
        };
    }

    private static (int Index, int Total)? LastStep(Regex pattern, string log)
    {
        var matches = pattern.Matches(log);
        if (matches.Count == 0)
            return null;
        var last = matches[^1];
        var index = int.Parse(last.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture);
        var total = int.Parse(last.Groups[2].Value, System.Globalization.CultureInfo.InvariantCulture);
        return total > 0 ? (index, total) : null;
    }
}
