using System.Globalization;
using System.Text.RegularExpressions;
using BannerlordCoopLauncher.Core.Server;

namespace BannerlordCoopLauncher.Core.Support;

/// <summary>How much Coop Save Rescue may change (compat\CoopSaveRescue reads it from <see cref="AutoRescue.EnvironmentVariable"/>).</summary>
public enum RescueMode
{
    /// <summary>Every start: only the fixes for data that is already broken.</summary>
    Auto,

    /// <summary>The one start after a crash while the save loaded: Auto, plus a hero that fails to load is skipped.</summary>
    Load,

    /// <summary>The manual Run Rescue save: every fix, the empty lord party reset too.</summary>
    Full,
}

/// <summary>
/// Coop Save Rescue on every start, the same in Coodsz Host, the AD1259 server tool and the TAOM
/// server tool. The module id starts with DedicatedServer., so Coop leaves it out of the join check
/// and players can join while it is loaded; it stores nothing in the save, so a save made with it
/// loads without it. It prints a FIXED line for every fix and one Report line about a minute after
/// the campaign is up. New text needs tools\ExtractUiText and translations.
/// </summary>
public static class AutoRescue
{
    public const string ModuleId = "DedicatedServer.CoopSaveRescue";

    /// <summary>The id the module had before it loaded on every start. Saves made during an old Rescue save still list it.</summary>
    public const string LegacyModuleId = "CoopSaveRescue";

    public const string EnvironmentVariable = "COODSZ_SAVE_RESCUE";

    /// <summary>Written by the plain Coop rescue zip (full mode); the tools remove a stray one.</summary>
    public const string ModeFile = "CoopSaveRescue.mode";

    public const string LogTag = "[CoopSaveRescue]";
    public const string FixedTag = "[CoopSaveRescue] FIXED:";
    public const string ReportTag = "[CoopSaveRescue] Report:";

    public static string ModeValue(RescueMode mode) => mode switch
    {
        RescueMode.Load => "load",
        RescueMode.Full => "full",
        _ => "auto",
    };

    public static bool IsModule(string? moduleId) =>
        moduleId != null && (moduleId.Equals(ModuleId, StringComparison.OrdinalIgnoreCase) || moduleId.Equals(LegacyModuleId, StringComparison.OrdinalIgnoreCase));

    /// <summary>What one FIXED line says, or null for any other line.</summary>
    public static string? FixedText(string? line)
    {
        if (line == null)
            return null;
        var at = line.IndexOf(FixedTag, StringComparison.Ordinal);
        if (at < 0)
            return null;
        var text = line[(at + FixedTag.Length)..].Trim();
        return text.Length == 0 ? null : text;
    }

    public static bool IsReport(string? line) => line != null && line.Contains(ReportTag, StringComparison.Ordinal);

    public static bool FoundNothing(string reportLine) =>
        reportLine.Contains("nothing to fix", StringComparison.OrdinalIgnoreCase);

    public static string ReportText(string line)
    {
        var at = line.IndexOf(ReportTag, StringComparison.Ordinal);
        return at < 0 ? line.Trim() : line[(at + ReportTag.Length)..].Trim();
    }

    // ---- The sticky notice in the tools -------------------------------------------------

    public static string FixedTitle(int count) =>
        count == 1 ? "Coop Save Rescue fixed a problem in this save." : "Coop Save Rescue fixed " + count + " problems in this save.";

    public const string FixedSaved = "The game was saved with the fixes. A backup from before them is kept, and Load backup puts it back.";
    public const string FixedNotSaved = "The next save keeps the fixes. A backup from before them is kept, and Load backup puts it back.";
    public const string ProblemTitle = "This save has a problem. Start fixes it next time.";
    public const string RetryTitle = "The server crashed while loading the save. It starts once more with the deeper save fixes.";
    public const string RetryAfterCrash = "The server had crashed while loading the save, so it was started once more with the deeper save fixes.";
    public const string RetryFailedTitle = "The server crashed again while loading the save, also with the deeper save fixes.";
    public const string RetryFailedBody = "It was not started again. Click Send my logs and post the code in Discord. Load backup puts back an earlier save.";

    public static string BackupKept(string saveName) => "Kept a backup of " + saveName + " from before the save fixes. Load backup puts it back.";

    public static string InstallFailed(bool copyThere, string reason) => copyThere
        ? "Could not update Coop Save Rescue on the server, so this start uses the copy already there. " + reason
        : "Could not update Coop Save Rescue on the server, so this start runs without the save fixes. " + reason;

    // ---- Stop: the warning signs in the server log of the run that just ended -------------

    // One warning sign per line, or null. The needles are Coop's and the fixer's own log text.
    private static string? WarningSign(string line, bool battlesClosed)
    {
        // "Releasing N parties from ended map event" is not one: Coop's load cleaner and coop.unstuck
        // print it after they freed the parties, so it is already fixed (see PartiesFreedByCoop).
        if (line.Contains("half-finalized loaded player map event", StringComparison.OrdinalIgnoreCase))
            return "A battle in the save was left half finished.";
        if (line.Contains("Failed to auto-finalize concluded map event", StringComparison.OrdinalIgnoreCase))
            return "A battle could not finish.";
        // The fixer saw a battle fail its update but did not close it (it closes one after 3 failures in a row).
        if (!battlesClosed && line.Contains(LogTag, StringComparison.Ordinal) && line.Contains("failed its update", StringComparison.Ordinal))
            return "A battle failed to update, so the map can freeze.";
        return null;
    }

    /// <summary>
    /// The known problems Coop Save Rescue handles that the log of the run shows, each once.
    /// Lines the fixer already fixed in this run (its FIXED lines) are not counted again.
    /// </summary>
    public static IReadOnlyList<string> StopScan(string? log)
    {
        var found = new List<string>();
        if (string.IsNullOrEmpty(log))
            return found;
        var fixedBattles = log.Contains(FixedTag + " Closed broken battle", StringComparison.Ordinal)
            || log.Contains(FixedTag + " Force-closed broken battle", StringComparison.Ordinal);
        foreach (var raw in log.Split('\n'))
        {
            var line = raw.TrimEnd('\r');
            if (line.Contains(FixedTag, StringComparison.Ordinal))
                continue;
            if (WarningSign(line, fixedBattles) is { } problem && !found.Contains(problem))
                found.Add(problem);
        }

        return found;
    }

    // Coop's load cleaner (MapEventLoadCleaner) and coop.unstuck: Releasing 3 parties from ended map event "created_257585".
    private static readonly Regex ReleasedParties = new(
        @"Releasing (\d+) parties from ended map event ""?([^""\s]*)",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    /// <summary>
    /// How many parties Coop itself freed from ended battles in this run. Coop prints the line after
    /// it fixed them, so Stop shows it as a note, not as a problem. A battle that is in both the
    /// server log and the console log is counted once.
    /// </summary>
    public static int PartiesFreedByCoop(string? log)
    {
        if (string.IsNullOrEmpty(log))
            return 0;
        var perBattle = new Dictionary<string, int>(StringComparer.Ordinal);
        var unnamed = 0;
        foreach (Match match in ReleasedParties.Matches(log))
        {
            if (!int.TryParse(match.Groups[1].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var count))
                continue;
            var battle = match.Groups[2].Value;
            if (battle.Length == 0)
                unnamed += count;
            else if (!perBattle.TryGetValue(battle, out var seen) || count > seen)
                perBattle[battle] = count;
        }

        return perBattle.Values.Sum() + unnamed;
    }

    public static string FreedByCoopTitle(int count) =>
        count == 1 ? "Coop freed 1 stuck party this run." : "Coop freed " + count + " stuck parties this run.";

    public const string FreedByCoopBody = "They were left in a battle that had already ended. Coop fixed this itself, so nothing needs doing.";
}

/// <summary>What Coop Save Rescue said during one server run: its FIXED lines and its Report.</summary>
public sealed class RescueRun
{
    public enum Seen { Nothing, Fixed, Report }

    private readonly List<string> _fixes = new();

    public RescueMode Mode { get; private set; }

    public IReadOnlyList<string> Fixes => _fixes;

    public string? Report { get; private set; }

    public bool Reported => Report != null;

    /// <summary>The server crashed while loading and this is the one start with the deeper fixes.</summary>
    public bool AfterLoadCrash => Mode == RescueMode.Load;

    public void Begin(RescueMode mode)
    {
        Mode = mode;
        _fixes.Clear();
        Report = null;
    }

    public Seen Observe(string? line)
    {
        if (AutoRescue.FixedText(line) is { } text)
        {
            if (!_fixes.Contains(text))
                _fixes.Add(text);
            return Seen.Fixed;
        }

        if (Report == null && AutoRescue.IsReport(line))
        {
            Report = AutoRescue.ReportText(line!);
            return Seen.Report;
        }

        return Seen.Nothing;
    }
}

public enum LoadCrashStep
{
    /// <summary>Not a crash while the save loaded: the crash restart decides.</summary>
    NotALoadCrash,

    /// <summary>Start once more in load mode.</summary>
    RetryInLoadMode,

    /// <summary>The load mode start crashed too: say so and start nothing.</summary>
    GaveUp,

    /// <summary>A load crash with no retry left (or no save loaded): the crash restart decides.</summary>
    LeftToCrashRestart,
}

/// <summary>
/// After a crash while the save loads, the tools start the server once more with the deeper fixes
/// (<see cref="RescueMode.Load"/>). Never a loop: one retry per start the host made by hand.
/// </summary>
public sealed class LoadCrashRetry
{
    private bool _used;

    /// <summary>Start server, Restart, Rescue save or Last good save pressed by the host.</summary>
    public void ManualStart() => _used = false;

    public bool Used => _used;

    /// <summary>True once, when this exit is a crash before the server was serving and a retry can help.</summary>
    public bool Take(ServerExitInfo exit, bool servedThisRun, RescueMode crashedMode)
    {
        if (_used || !IsLoadCrash(exit, servedThisRun) || crashedMode != RescueMode.Auto)
            return false;
        _used = true;
        return true;
    }

    /// <summary>
    /// What the tools do with one server exit. A crash of the load mode start itself is the end:
    /// the host is told, and the crash restart does not start it again. A run that made a new
    /// campaign had no save to fix, so its crash goes to the crash restart like any other.
    /// </summary>
    public LoadCrashStep Decide(ServerExitInfo exit, bool servedThisRun, RescueMode crashedMode, bool loadedASave = true)
    {
        if (!IsLoadCrash(exit, servedThisRun))
            return LoadCrashStep.NotALoadCrash;
        if (crashedMode == RescueMode.Load)
            return LoadCrashStep.GaveUp;
        if (!loadedASave)
            return LoadCrashStep.LeftToCrashRestart;
        return Take(exit, servedThisRun, crashedMode) ? LoadCrashStep.RetryInLoadMode : LoadCrashStep.LeftToCrashRestart;
    }

    /// <summary>
    /// Whether the exit shows the Send server crash report bar. Not when the load mode retry starts:
    /// a report sent while that run still loads misses whether it worked (TAOM #42), and its own
    /// exit shows the bar if it crashes too.
    /// </summary>
    public static bool ShowsCrashBar(LoadCrashStep step) => step != LoadCrashStep.RetryInLoadMode;

    /// <summary>
    /// A crash before the server was serving. A changed Coop module (code 4), the server terms
    /// (code 6), a module that could not be staged or is missing (7, 8) and War Sails (9, 10) are
    /// not the save, so they are left out. A save that did not start loading (11) stays in, like 2.
    /// </summary>
    public static bool IsLoadCrash(ServerExitInfo exit, bool servedThisRun) =>
        exit.IsCrash && !servedThisRun && !ServerExit.IsSetupProblem(exit.Kind);
}
