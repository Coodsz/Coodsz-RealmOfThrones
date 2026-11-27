using System.Globalization;

namespace BannerlordCoopLauncher.Core.Server;

/// <summary>
/// "Automatic restart" in server-config.json (autoRestart, autoRestartHours). The Coop dedicated server ignores
/// both keys, so the host tool does the restart itself: save, stop and start the same save once the server has
/// run for the set hours while the tool was open.
/// </summary>
public static class ScheduledRestart
{
    /// <summary>Fewer hours would restart a big world again before players had time to play.</summary>
    public const double MinHours = 1;

    /// <summary>One week.</summary>
    public const double MaxHours = 168;

    /// <summary>
    /// The time between restarts, or null when automatic restart is off, the hours are empty or not a number,
    /// or zero or less. Hours below <see cref="MinHours"/> or above <see cref="MaxHours"/> are clamped.
    /// </summary>
    public static TimeSpan? Interval(bool enabled, string? hours)
    {
        if (!enabled || string.IsNullOrWhiteSpace(hours))
            return null;
        if (!double.TryParse(hours.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var value)
            || double.IsNaN(value) || double.IsInfinity(value) || value <= 0)
            return null;
        return TimeSpan.FromHours(Math.Clamp(value, MinHours, MaxHours));
    }

    /// <summary>
    /// True when the server has run for the whole interval, counted from its start or from when the tool opened,
    /// whichever is later. Opening the tool on a server that has run for days never restarts it at once.
    /// </summary>
    public static bool IsDue(DateTime? startedAt, DateTime watchingSince, DateTime now, TimeSpan? interval) =>
        startedAt is { } started && interval is { } every && now - (started > watchingSince ? started : watchingSince) >= every;

    /// <summary>
    /// True when a restart would load the save that runs now. Restart starts the save picked in the tool,
    /// and the host may have picked another one since the start.
    /// </summary>
    public static bool KeepsRunningSave(string? runningSave, string? pickedSave) =>
        !string.IsNullOrWhiteSpace(runningSave) && string.Equals(runningSave.Trim(), pickedSave?.Trim(), StringComparison.OrdinalIgnoreCase);

    /// <summary>The log line for an automatic restart.</summary>
    public static string Line(TimeSpan interval)
    {
        var hours = interval.TotalHours.ToString("0.##", CultureInfo.InvariantCulture);
        return hours == "1"
            ? "Automatic restart: the server has run for 1 hour, so it saves and starts again now."
            : "Automatic restart: the server has run for " + hours + " hours, so it saves and starts again now.";
    }

    public const string OtherSaveLine = "Automatic restart skipped: another save is picked than the one running. Pick the running save again, or restart by hand.";
    public const string BrokenWorldLine = "Automatic restart skipped: the world looks broken, so the server is left running. The lines above say what to do.";
    public const string FailedLoadLine = "Automatic restart skipped because the campaign didn't load. Nothing was saved over your save.";
    public const string TermsLine = "Automatic restart skipped: the Coop server terms changed. Press Start server to read them.";
}
