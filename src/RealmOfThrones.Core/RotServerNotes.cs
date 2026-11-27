using TaomCoop.Core;

namespace RealmOfThrones.Core;

/// <summary>Lines the Realm of Thrones server tool shows around a start and a stop, as the Burning Empires server
/// tool has them (BeServerStop, BeFirewallHint, BeModNotes), plus the world watch lines.</summary>
public static class RotServerNotes
{
    /// <summary>Stop and Restart wait for a save only on a server that reached SERVING in this run. One still
    /// loading has no campaign running to save.</summary>
    public static bool WaitsForSave(bool reachedServing) => reachedServing;

    public const string StoppedWhileLoading = "Stopped right away. The server was still loading, so no save was needed.";

    /// <summary>The first start from a server folder makes Windows ask whether the server may use the network.</summary>
    public const string FirewallHint = "Windows may ask to allow .NET Host. Click Allow so players can join.";

    /// <summary>True until a server from this folder once reached SERVING, unless the firewall already lets the
    /// server in (the sixth test showed the hint while Windows Firewall already allowed UDP 4200 and no prompt came).</summary>
    public static bool FirewallHintShows(string? doneForServerRoot, string? serverRoot, bool firewallAllows = false) =>
        !firewallAllows && !string.IsNullOrWhiteSpace(serverRoot)
        && !string.Equals(Normalize(doneForServerRoot), Normalize(serverRoot), StringComparison.OrdinalIgnoreCase);

    private static string Normalize(string? path) => (path ?? "").Trim().TrimEnd('\\', '/');

    /// <summary>The time the uptime counts from: what DedicatedServerWatch.TryStarted saw, but never older than this
    /// window's own Start click (runStartedUtc), so a lock file left by an earlier run cannot add its time.</summary>
    public static DateTime? UptimeFrom(DateTime? found, DateTime? runStartedUtc)
    {
        if (found == null)
            return null;
        if (runStartedUtc is { } run && run.ToLocalTime() > found.Value)
            return run.ToLocalTime();
        return found;
    }

    public const string NewWorldReady = "The new Realm of Thrones world passed its check. The tool saves it now.";

    public const string LoadedWorldReady = "The Realm of Thrones world is loaded. The tool saves it now.";

    /// <summary>The line when the world watch stops on a line that says the world must not be kept.</summary>
    public static string WorldFailed(string? line) =>
        "The world is not right, so the tool did not save it. The server said " + (line ?? "").Trim().TrimEnd('.') + ".";

    // BattleSizeResized replaces the game's battle size on each player's game, so the battle size the
    // server tool writes to Coop's mod config (modOptions.battleSize) does not show up in battles.
    public const string BattleSizeResized =
        "BattleSizeResized is in this setup. It overrides the server's battle size on each player's game, so the \"Battle size\" set in the server tool may not apply. Remove it, or turn on \"Override Native Battlesize\" in its MCM settings and set the size there on every player's game.";

    /// <summary>Notes for extra mods that change what the tool's settings do.</summary>
    public static IReadOnlyList<string> ModNotes(TaomSetup? setup)
    {
        if (setup == null)
            return [];
        return setup.EnabledExtras.Any(mod => mod.ModuleId.Equals(TaomModNotes.BattleSizeResizedId, StringComparison.OrdinalIgnoreCase))
            ? [BattleSizeResized]
            : [];
    }
}

/// <summary>
/// The Logs window skips a server log chunk too big to show without freezing (MostShown lines). The Realm of
/// Thrones server writes hundreds of lines at a time while it boots, so the skip line came again and again: now it
/// comes at most once a minute, with every line skipped since the last one counted in.
/// </summary>
public sealed class RotSkippedLines
{
    public const int MostShown = 120;

    public static readonly TimeSpan Quiet = TimeSpan.FromMinutes(1);

    private int _held;
    private DateTime? _said;

    /// <summary>Counts a chunk of lines (0 for one that was shown) and gives the skip line when one is due, else null.</summary>
    public string? Note(int lines, DateTime nowUtc)
    {
        if (lines > MostShown)
            _held += lines;
        if (_held == 0 || (_said is { } said && nowUtc - said < Quiet))
            return null;
        _said = nowUtc;
        var count = _held;
        _held = 0;
        return "The server wrote " + count + " log lines in bursts. They stay in the log file. This window skipped them so it would not freeze, and says so at most once a minute.";
    }
}
