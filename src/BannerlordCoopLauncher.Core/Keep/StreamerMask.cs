using System.Text.RegularExpressions;

namespace BannerlordCoopLauncher.Core.Keep;

/// <summary>
/// Streamer mode, after CoopKeep's privacy switch: what Coodsz Host shows on screen while it is on.
/// IP addresses, SteamID64s, invite and reward codes and the join password become dots. Copy
/// buttons still copy the real value; only the screen is hidden.
/// </summary>
public static class StreamerMask
{
    /// <summary>What a hidden value shows.</summary>
    public const string Hidden = "••••••";

    private static readonly Regex Ipv4 = new(@"(?<![\d.])(?:\d{1,3}\.){3}\d{1,3}(?::\d{1,5})?(?![\d.])", RegexOptions.Compiled);
    private static readonly Regex Ipv6 = new(@"\[[0-9A-Fa-f:]{2,}\](?::\d{1,5})?", RegexOptions.Compiled);
    private static readonly Regex SteamId = new(@"(?<!\d)7656119\d{10}(?!\d)", RegexOptions.Compiled);

    /// <summary>The value itself, or <see cref="Hidden"/> while streamer mode is on (an empty value stays empty).</summary>
    public static string Value(string? value, bool on) =>
        string.IsNullOrEmpty(value) ? "" : on ? Hidden : value;

    /// <summary>
    /// Free text (an address note, a log line) with every IP address and SteamID64 hidden, and each
    /// of <paramref name="secrets"/> (the password, the Steam name) too. Off: the text as it is.
    /// </summary>
    public static string Text(string? text, bool on, params string?[] secrets)
    {
        if (string.IsNullOrEmpty(text) || !on)
            return text ?? "";
        var masked = Ipv4.Replace(text, Hidden);
        masked = Ipv6.Replace(masked, Hidden);
        masked = SteamId.Replace(masked, Hidden);
        foreach (var secret in secrets.Where(s => !string.IsNullOrWhiteSpace(s) && s!.Trim().Length >= 3).Select(s => s!.Trim()).OrderByDescending(s => s.Length))
            masked = masked.Replace(secret, Hidden, StringComparison.OrdinalIgnoreCase);
        return masked;
    }

    /// <summary>True when the text holds an IP address or a SteamID64.</summary>
    public static bool HasPrivate(string? text) =>
        !string.IsNullOrEmpty(text) && (Ipv4.IsMatch(text) || Ipv6.IsMatch(text) || SteamId.IsMatch(text));

    /// <summary>"Steam ••••1234" normally, "Steam ••••••" in streamer mode, "" with no SteamID64.</summary>
    public static string SteamLabel(string? steamId, bool on)
    {
        var label = KeepRoster.MaskedSteamId(steamId);
        return on && label.Length > 0 ? "Steam " + Hidden : label;
    }

    /// <summary>A SteamID64 written out (the ban list): the id, or <see cref="Hidden"/> in streamer mode.</summary>
    public static string SteamId64(string? steamId, bool on) => Value(steamId, on);

    /// <summary>The player box line: "Name  76561199..." normally, the name alone in streamer mode.</summary>
    public static string PlayerLine(string name, string? steamId, bool on) =>
        on || string.IsNullOrWhiteSpace(steamId) ? name : name + "  " + steamId;
}

/// <summary>
/// Player names in streamer mode: "Player 1", "Player 2". A name keeps the number it got the first
/// time any page showed it, for as long as the app runs, so the Map, Players, Home, Cheats and the
/// player profile all call one player the same. The pages translate "Player {0}".
/// </summary>
public static class StreamerNames
{
    private static readonly Dictionary<string, int> Numbers = new(StringComparer.OrdinalIgnoreCase);
    private static readonly object Gate = new();

    /// <summary>"Player N" for this name.</summary>
    public static string Alias(string name)
    {
        var key = name.Trim();
        lock (Gate)
        {
            if (!Numbers.TryGetValue(key, out var number))
                Numbers[key] = number = Numbers.Count + 1;
            return "Player " + number.ToString(System.Globalization.CultureInfo.InvariantCulture);
        }
    }

    /// <summary>The name itself, or its "Player N" while streamer mode is on (an empty name stays empty).</summary>
    public static string Name(string? name, bool on) =>
        string.IsNullOrWhiteSpace(name) ? name ?? "" : on ? Alias(name) : name;

    /// <summary>Gives numbers to names that have none yet, in this order.</summary>
    public static void Learn(IEnumerable<string> names)
    {
        foreach (var name in names)
            if (!string.IsNullOrWhiteSpace(name))
                Alias(name);
    }
}
