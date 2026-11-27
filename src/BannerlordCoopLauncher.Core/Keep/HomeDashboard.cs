using System.Globalization;

namespace BannerlordCoopLauncher.Core.Keep;

/// <summary>
/// What the Dashboard knows about the server at one moment. Crashed: the last run ended in a crash and
/// nobody started it since. Restarting: Restart runs, or a crash restart waits for its turn.
/// </summary>
public sealed record HomeFacts(
    bool Running,
    bool Serving,
    bool Exited,
    bool Stopping,
    string SaveName,
    TimeSpan? Uptime,
    int Players,
    int AutosaveMinutes,
    DateTime? LastSave,
    DateTime? LastBackup,
    bool Crashed = false,
    bool Restarting = false);

/// <summary>The kind of state the Dashboard shows: the pill and the one big server button.</summary>
public enum HomeState
{
    NoWorld,
    Ready,
    Starting,
    Online,
    Stopping,
    Exited,
    Crashed,
    Restarting,
}

/// <summary>The smaller buttons beside the big one, by state (ux-plan 1.3).</summary>
[Flags]
public enum DashboardActions
{
    None = 0,
    // Stop while the server is still starting.
    StopStarting = 1,
    SaveGame = 2,
    Restart = 4,
    BackupNow = 8,
    LastGoodSave = 16,
    StopAutoRestart = 32,
}

/// <summary>The big server button: its words, whether it stops the server (red) and whether it can be pressed.</summary>
public sealed record PrimaryAction(string Label, bool Stops, bool Enabled);

/// <summary>
/// The words of the Dashboard (once the Home page), after CoopKeep's Home page ("Your server is ready", the stat strip).
/// Pure, so it can be tested; the page only shows them.
/// </summary>
public static class HomeDashboard
{
    public static HomeState State(HomeFacts facts)
    {
        if (facts.Restarting)
            return HomeState.Restarting;
        if (facts.Running && facts.Exited)
            return HomeState.Exited;
        if (facts.Running && facts.Stopping)
            return HomeState.Stopping;
        if (facts.Running)
            return facts.Serving ? HomeState.Online : HomeState.Starting;
        if (facts.Crashed)
            return HomeState.Crashed;
        return string.IsNullOrWhiteSpace(facts.SaveName) ? HomeState.NoWorld : HomeState.Ready;
    }

    public static string Headline(HomeFacts facts) => State(facts) switch
    {
        HomeState.NoWorld => "Create your first world",
        HomeState.Starting => "Your server is starting",
        HomeState.Online => "Your server is online",
        HomeState.Stopping => "Your server is stopping",
        HomeState.Exited => "The game engine stopped",
        HomeState.Crashed => "Your server crashed",
        HomeState.Restarting => "Your server is restarting",
        _ => "Your server is ready",
    };

    public static string Detail(HomeFacts facts) => State(facts) switch
    {
        HomeState.NoWorld => "Create a world, or pick a save on the Saves page.",
        HomeState.Starting => "Loading " + facts.SaveName + ". Friends can join once it says online.",
        HomeState.Online => "Friends can join " + facts.SaveName + " now.",
        HomeState.Stopping => "Saving and closing. Wait until it says stopped.",
        HomeState.Exited => "Click Stop server to clear it, then read the Logs page.",
        HomeState.Crashed => "Read the end of the Logs page, then press Start server.",
        HomeState.Restarting => "Wait until it says online.",
        _ => "World " + facts.SaveName + " is selected. Press Start server. It is up in about a minute.",
    };

    /// <summary>"Online", "Starting", "Stopped"... for the stat strip.</summary>
    public static string StatusWord(HomeFacts facts) => State(facts) switch
    {
        HomeState.Starting => "Starting",
        HomeState.Online => "Online",
        HomeState.Stopping => "Stopping",
        HomeState.Exited => "Exited",
        HomeState.Crashed => "Crashed",
        HomeState.Restarting => "Restarting",
        _ => "Stopped",
    };

    /// <summary>
    /// The one big button (ux-plan 1.3): Start server, Starting..., Stop server, Saving and stopping...,
    /// Restarting..., and Start server again after a crash. The pending states can't be pressed, so a second
    /// click never starts or stops twice.
    /// </summary>
    public static PrimaryAction Primary(HomeState state) => state switch
    {
        HomeState.Starting => new("Starting...", false, false),
        HomeState.Online or HomeState.Exited => new("Stop server", true, true),
        HomeState.Stopping => new("Saving and stopping...", true, false),
        HomeState.Restarting => new("Restarting...", false, false),
        _ => new("Start server", false, true),
    };

    /// <summary>The smaller buttons beside the big one. A crash restart that waits offers Stop auto restart.</summary>
    public static DashboardActions Secondary(HomeState state, bool crashRestartWaits) => state switch
    {
        HomeState.Ready => DashboardActions.BackupNow,
        HomeState.Starting => DashboardActions.StopStarting,
        HomeState.Online => DashboardActions.SaveGame | DashboardActions.Restart | DashboardActions.BackupNow | DashboardActions.LastGoodSave,
        HomeState.Exited => DashboardActions.LastGoodSave,
        HomeState.Crashed => DashboardActions.LastGoodSave | DashboardActions.BackupNow,
        HomeState.Restarting => crashRestartWaits ? DashboardActions.StopAutoRestart : DashboardActions.None,
        _ => DashboardActions.None,
    };

    public static string Uptime(TimeSpan? uptime)
    {
        if (uptime is not { } span)
            return "-";
        if (span < TimeSpan.Zero)
            span = TimeSpan.Zero;
        return (int)span.TotalHours + span.ToString(@"\:mm\:ss", CultureInfo.InvariantCulture);
    }

    public static string Autosave(int minutes) =>
        minutes <= 0 ? "Off" : "Saves every " + minutes.ToString(CultureInfo.InvariantCulture) + " min";

    /// <summary>"-", "just now", "12 min ago", "3 h ago", or the date for anything older than a day.</summary>
    public static string Ago(DateTime? when, DateTime now)
    {
        if (when is not { } time)
            return "-";
        var age = now - time;
        if (age < TimeSpan.FromMinutes(1))
            return "just now";
        if (age < TimeSpan.FromHours(1))
            return ((int)age.TotalMinutes).ToString(CultureInfo.InvariantCulture) + " min ago";
        if (age < TimeSpan.FromDays(1))
            return ((int)age.TotalHours).ToString(CultureInfo.InvariantCulture) + " hr ago";
        return time.ToString("d MMM yyyy", CultureInfo.InvariantCulture);
    }
}
