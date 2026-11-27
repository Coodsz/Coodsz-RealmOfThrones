using System.Text.RegularExpressions;

namespace BannerlordCoopLauncher.Core.Support;

/// <summary>What the server's own log says about its Steam lobby.</summary>
public enum SteamLobbyState { Unknown, Up, Renewing, Closed, Off, Failed }

/// <summary>
/// Reads the Steam lobby lines of a Coop 0.1.6 dedicated server. Only these lines count as
/// proof; the Steam advertise setting alone does not mean friends can find the lobby yet.
/// </summary>
public static class SteamLobbyLine
{
    private const RegexOptions Options = RegexOptions.CultureInvariant;

    // [Coop] Steam lobby "109775245183549596" created  (right after SERVING, and again after a lease renewal)
    private static readonly Regex Created = new Regex(@"\[Coop\] Steam lobby ""?\d+""? created", Options);
    // [Coop] Steam lobby lease renewal failed repeatedly; recreating the advertisement
    private static readonly Regex Recreating = new Regex(@"Steam lobby .*recreating the advertisement", Options);
    // [DedicatedServer] Steam lobby advertisement withdrawn  (on stop)
    private static readonly Regex Withdrawn = new Regex(@"Steam lobby advertisement withdrawn", Options);
    // [DedicatedServer] Steam discoverability disabled (steam=false in server-config.json)
    private static readonly Regex Disabled = new Regex(@"Steam discoverability disabled", Options);
    // [Coop] Server Steam integration inactive; initialization did not reach anonymous logon.
    // [DedicatedServer] coop Steam services did not start, direct connect only
    // [DedicatedServer] Steam unavailable (client not running or SteamAPI.Init failed) — direct connect only
    private static readonly Regex Inactive = new Regex(@"Steam integration inactive|Steam services did not start|Steam unavailable \(client not running", Options);

    /// <summary>The lobby state a line reports, or null for a line that says nothing about it.</summary>
    public static SteamLobbyState? Read(string? line)
    {
        if (string.IsNullOrEmpty(line) || !line.Contains("Steam", StringComparison.Ordinal))
            return null;
        if (Created.IsMatch(line))
            return SteamLobbyState.Up;
        if (Recreating.IsMatch(line))
            return SteamLobbyState.Renewing;
        if (Withdrawn.IsMatch(line))
            return SteamLobbyState.Closed;
        if (Disabled.IsMatch(line))
            return SteamLobbyState.Off;
        if (Inactive.IsMatch(line))
            return SteamLobbyState.Failed;
        return null;
    }

    /// <summary>The Home line for a running server. <paramref name="steamName"/> is empty in streamer mode.</summary>
    public static string Describe(SteamLobbyState state, string steamName) => state switch
    {
        SteamLobbyState.Up => steamName.Length == 0
            ? "Steam lobby is up. Friends can find it now."
            : "Steam lobby is up as " + steamName + ".",
        SteamLobbyState.Renewing => "Steam lobby is not up right now. The server puts it back up by itself.",
        SteamLobbyState.Closed => "Steam lobby is closed. The server is stopping.",
        SteamLobbyState.Off => "Steam lobby is off for this run. Restart the server to turn it on.",
        SteamLobbyState.Failed => "Steam lobby did not start because Steam was not ready on this PC. Check that Steam is open and logged in, then press Restart. Until then friends use the address and UDP port.",
        _ => "Steam lobby is not up yet. It comes up a few seconds after the status says Server online.",
    };
}
