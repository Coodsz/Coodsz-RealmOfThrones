using BannerlordCoopLauncher.Core.Profile;

namespace BannerlordCoopLauncher.Core.Modules;

/// <summary>
/// When the Server Tool's mod list can't change (Coodsz, redesign wave 3): while the server runs, while a mod test runs
/// its own server, and while a restart after a crash waits, since the server keeps its mod files open and the next start
/// would load a list nobody saved. A locked setup can't change either.
/// </summary>
public static class ModListBlock
{
    public const string RunningLine = "The server is running. Stop it to change the mod list.";
    public const string RunningTip = "Stop the server to change the mod list.";
    public const string LockedLine = "This setup is locked. The working mods cannot be changed.";
    /// <summary>A Start, Stop, Restart, Backup now or Save game is under way, and the server does not hold the mods.</summary>
    public const string BusyLine = "Wait for the current action to finish.";

    /// <summary>Whether the mod list counts as running: the server, a mod test, or a restart after a crash that waits.</summary>
    public static bool Running(bool serverRunning, bool modTestRunning, bool restartPending) =>
        serverRunning || modTestRunning || restartPending;

    /// <summary>Why the mod list can't change now, or null when it can. Running comes first: stopping is the one step.</summary>
    public static string? Reason(bool running, bool locked) =>
        running ? RunningLine : locked ? LockedLine : null;

    /// <summary>Where Move up (-1) or Move down (+1) puts the row at <paramref name="from"/>, or null at the end of the
    /// list or when the move would break the working Coop order (the same rule as dragging).</summary>
    public static int? MoveTarget(IReadOnlyList<string> ids, int from, int delta, IReadOnlyList<string> working)
    {
        if (delta == 0 || from < 0 || from >= ids.Count)
            return null;
        var to = from + delta;
        if (to < 0 || to >= ids.Count)
            return null;
        return WorkingLoadOrder.CanMoveKeepingCoop(ids, from, to, working) ? to : null;
    }

    /// <summary>The same move with a filter on: <paramref name="shown"/> holds the full list index of every row the
    /// page shows, in view order. The row takes the next shown row's place, so a hidden neighbour never makes the
    /// button seem to do nothing. Null at the end of the shown rows or when the move breaks the working Coop order.</summary>
    public static int? MoveTarget(IReadOnlyList<string> ids, IReadOnlyList<int> shown, int from, int delta, IReadOnlyList<string> working)
    {
        if (delta == 0 || from < 0 || from >= ids.Count)
            return null;
        var at = -1;
        for (var i = 0; i < shown.Count; i++)
        {
            if (shown[i] == from)
                at = i;
        }
        var next = at + Math.Sign(delta);
        if (at < 0 || next < 0 || next >= shown.Count)
            return null;
        var to = shown[next];
        return WorkingLoadOrder.CanMoveKeepingCoop(ids, from, to, working) ? to : null;
    }
}
