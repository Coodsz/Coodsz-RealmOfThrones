using System.Globalization;

namespace TaomCoop.Core;

/// <summary>
/// The TAOM and Burning Empires Server Tools host the save picked in the save box, so a single player copy
/// made with Name server copy, or another server copy picked by mistake, became the hosted save without a word
/// (TAOM report #44). These questions come up when the picked save is earlier in its campaign than the save the
/// server last ran (LastRan). At Start the same name never asks; for a copy only the same file never
/// asks. A later or equal day, or a day that cannot be read, never asks. Days are the raw header text
/// (CampaignSaveInfo.Day). The tools never ask on their own starts (load retry, Restart, Last good save).
/// </summary>
public static class SaveSwitchWarning
{
    public const string CopyTitle = "Older save picked";
    public const string CopyButton = "Make the copy";
    public const string StartTitle = "Host an older save?";
    public const string StartButton = "Host it";

    /// <summary>True when the picked save is a different save and earlier in the campaign than the hosted one.</summary>
    public static bool IsEarlier(string? hostedName, string? hostedDay, string? pickedName, string? pickedDay) =>
        !string.IsNullOrWhiteSpace(hostedName) && !string.IsNullOrWhiteSpace(pickedName)
        && !hostedName.Trim().Equals(pickedName.Trim(), StringComparison.OrdinalIgnoreCase)
        && Day(hostedDay) is { } hosted && Day(pickedDay) is { } picked
        && Math.Floor(picked) < Math.Floor(hosted);

    /// <summary>
    /// True when the picked file is another file than the hosted one and earlier in the campaign. Name server copy
    /// compares files, not names: a single player save001 copied while the server hosts its own save001 is another
    /// save with the same name.
    /// </summary>
    public static bool IsEarlierFile(string? hostedPath, string? hostedDay, string? pickedPath, string? pickedDay) =>
        !string.IsNullOrWhiteSpace(hostedPath) && !string.IsNullOrWhiteSpace(pickedPath)
        && !SamePath(hostedPath, pickedPath)
        && Day(hostedDay) is { } hosted && Day(pickedDay) is { } picked
        && Math.Floor(picked) < Math.Floor(hosted);

    /// <summary>The Name server copy question for two save files, or null when there is nothing to ask.</summary>
    public static string? CopyQuestion(string? hostedPath, string? hostedDay, string pickedPath, string? pickedDay) =>
        IsEarlierFile(hostedPath, hostedDay, pickedPath, pickedDay)
            ? Path.GetFileNameWithoutExtension(pickedPath) + " is on campaign day " + DayText(pickedDay) + ". The save the server last ran, "
              + Path.GetFileNameWithoutExtension(hostedPath) + ", is on day " + DayText(hostedDay) + ". Make the copy anyway?"
            : null;

    private static bool SamePath(string left, string right)
    {
        try
        {
            return string.Equals(Path.GetFullPath(left.Trim()), Path.GetFullPath(right.Trim()), StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return string.Equals(left.Trim(), right.Trim(), StringComparison.OrdinalIgnoreCase);
        }
    }

    /// <summary>The Start server question, or null when there is nothing to ask.</summary>
    public static string? StartQuestion(string? hostedName, string? hostedDay, string pickedName, string? pickedDay) =>
        IsEarlier(hostedName, hostedDay, pickedName, pickedDay)
            ? "Host " + pickedName + " (day " + DayText(pickedDay) + ") instead of " + hostedName!.Trim() + " (day " + DayText(hostedDay)
              + ")? The server last ran " + hostedName.Trim() + "."
            : null;

    /// <summary>
    /// The save the server last ran, which the Start question compares against. Save settings writes the box pick
    /// into server-config.json while the server is stopped, so the name kept when a Start's server ran comes first.
    /// The config name is only for a tool that kept none yet, or when the kept save is gone.
    /// </summary>
    public static string LastRan(string? keptName, bool keptExists, string? configuredName) =>
        !string.IsNullOrWhiteSpace(keptName) && keptExists ? keptName.Trim() : configuredName?.Trim() ?? "";

    /// <summary>The line when the host answered no at Start.</summary>
    public static string NotStarted(string hostedName) =>
        "The server was not started. Pick " + hostedName + " in the save box to host it again.";

    /// <summary>The line after Name server copy: the box points at the new copy and Start hosts it.</summary>
    public static string NowHosts(string name) =>
        "The save box now points at " + name + ", and Start server hosts it.";

    /// <summary>The small text under a save in the save box, or empty when the header has no day.</summary>
    public static string BoxDay(string? day) =>
        Day(day) == null ? "" : "Campaign day " + DayText(day);

    private static string DayText(string? day) =>
        Day(day) is { } value ? Math.Max(1, Math.Floor(value)).ToString(CultureInfo.InvariantCulture) : "?";

    private static double? Day(string? day)
    {
        if (string.IsNullOrWhiteSpace(day))
            return null;
        if (!double.TryParse(day, NumberStyles.Float, CultureInfo.InvariantCulture, out var value)
            && !double.TryParse(day, NumberStyles.Float, CultureInfo.CurrentCulture, out value))
            return null;
        return double.IsFinite(value) && value >= 0 ? value : null;
    }
}
