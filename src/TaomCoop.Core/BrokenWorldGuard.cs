using BannerlordCoopLauncher.Core.Server;

namespace TaomCoop.Core;

public static class BrokenWorldGuard
{
    public const string Marker = "no recoverable player party";
    public const string OnMap = "\"state\":\"on map\"";
    public const string RefusalText = "This world was not saved. Your hero has no party to rejoin, so a save would keep that break. Load an older save instead.";
    public const string AutosaveHeldText = "Autosave is off because your hero has no party to rejoin. It turns back on after a hero is on the map and you start the server again.";
    public const string AutosaveStoppedText = "The server's autosave was stopped. Your hero has no party to rejoin, so the campaign was not written.";
    public const string FailedLoadStopText = "The campaign didn't load, so the tool stopped the server without saving. Your save is untouched.";
    public const string FailedLoadSaveText = "The campaign didn't load, so there is nothing to save. Your save is untouched.";
    public const string FailedLoadBackupText = "The server saved a campaign that didn't load. The tool didn't back it up and kept your older backups.";

    public static string? Refusal(string logText) =>
        BrokenTime(logText) == null ? null : RefusalText;

    // A server that cannot load its campaign writes @DS@{"ev":"state","phase":"fatal",...} (TAOM crash #31,
    // a clan with an empty banner). Save, or stop which saves on its way down, would then write that
    // half loaded world over the player's save. The fatal event counts until a later boot, loading or
    // serving one (a new run). The "stopping" the server writes after it does not clear it.
    public static bool LoadFailed(string? logText)
    {
        var failed = false;
        if (string.IsNullOrEmpty(logText))
            return false;
        var at = logText.IndexOf(StateHint, StringComparison.Ordinal);
        while (at >= 0)
        {
            var lineStart = logText.LastIndexOf('\n', at) + 1;
            var lineEnd = logText.IndexOf('\n', at);
            failed = LoadFailedAfter(failed, logText[lineStart..(lineEnd < 0 ? logText.Length : lineEnd)]);
            if (lineEnd < 0)
                break;
            at = logText.IndexOf(StateHint, lineEnd, StringComparison.Ordinal);
        }

        return failed;
    }

    public static bool LoadFailed(ServerState? runFatal, string? logText) =>
        runFatal is { Fatal: true } || LoadFailed(logText);

    // The same rule one line at a time, for a host that follows the server line by line.
    public static bool LoadFailedAfter(bool failed, string? line)
    {
        if (string.IsNullOrEmpty(line) || !line.Contains(StateHint, StringComparison.Ordinal) || ServerStateEvent.Last(line) is not { } state)
            return failed;
        if (state.Fatal)
            return true;
        var phase = ServerPhaseExtensions.ParsePhase(state.Phase);
        return failed && !(phase.IsStartingUp() || phase == ServerPhase.Serving);
    }

    private const string StateHint = "\"ev\":\"state\"";

    public static bool HoldAutosave(DateTime? saveCreated, DateTime? brokenAt) =>
        brokenAt != null && (saveCreated == null || saveCreated.Value >= brokenAt.Value);

    public static int AutosaveMinutes(int chosen, DateTime? saveCreated, DateTime? brokenAt) =>
        HoldAutosave(saveCreated, brokenAt) ? 0 : chosen;

    public static bool StopOwnedServer(bool ownsProcess, bool alreadyHeld, bool spareUntilOnMap, string logText) =>
        ownsProcess && !alreadyHeld && !spareUntilOnMap && BrokenTime(logText) != null;

    public static TimeSpan? BrokenTime(string logText)
    {
        var brokenAt = LastStamp(logText, Marker);
        if (brokenAt == null)
            return null;
        var onMapAt = LastStamp(logText, OnMap);
        if (onMapAt != null && IsLater(onMapAt.Value, brokenAt.Value))
            return null;
        return brokenAt;
    }

    private static TimeSpan? LastStamp(string text, string marker)
    {
        if (string.IsNullOrEmpty(text))
            return null;
        var index = 0;
        TimeSpan? last = null;
        while ((index = text.IndexOf(marker, index, StringComparison.OrdinalIgnoreCase)) >= 0)
        {
            var stamp = StampOnLine(text, index);
            if (stamp != null && (last == null || IsLater(stamp.Value, last.Value)))
                last = stamp;
            index += marker.Length;
        }

        return last;
    }

    private static TimeSpan? StampOnLine(string text, int markerIndex)
    {
        var lineStart = text.LastIndexOf('\n', markerIndex);
        if (lineStart < 0)
            lineStart = 0;
        else
            lineStart++;
        var length = Math.Min(16, text.Length - lineStart);
        if (length < 8)
            return null;
        var head = text.Substring(lineStart, length).TrimStart('[', ' ');
        return head.Length >= 8 && TimeSpan.TryParse(head[..8], out var stamp) ? stamp : null;
    }

    private static bool IsLater(TimeSpan candidate, TimeSpan baseline)
    {
        var delta = candidate - baseline;
        if (delta < TimeSpan.Zero)
            delta += TimeSpan.FromDays(1);
        return delta > TimeSpan.Zero && delta <= TimeSpan.FromHours(18);
    }
}
