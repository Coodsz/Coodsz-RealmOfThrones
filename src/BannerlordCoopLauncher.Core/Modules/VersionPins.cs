using System.Numerics;
using System.Text.RegularExpressions;

namespace BannerlordCoopLauncher.Core.Modules;

/// <summary>A Made for row against what this PC or server has: Match green, Missing amber, Mismatch red.</summary>
public enum PinState { Match, Missing, Mismatch }

/// <summary>The picture in front of a Made for row.</summary>
public enum PinIcon { Game, Mod, Coop }

/// <summary>
/// One row of the Made for card (redesign D1): the name, the version the tool pins and the version found. A null
/// Pinned means the tool takes any version (the Coop tools follow the Workshop Coop), so a found one is a match.
/// BaseGame rows only need the same numbers as the pin, since the build number after them differs between the client
/// and the server (AD1259Stack.VersionMatches does the same).
/// </summary>
public sealed record VersionPin(string Name, PinIcon Icon, string? Pinned, string? Found, bool BaseGame = false, IReadOnlyList<string>? AlsoAccepted = null)
{
    public PinState State => VersionPins.Check(Pinned, Found, BaseGame, AlsoAccepted);

    /// <summary>The version the row shows: the one found, else the pin without its v.</summary>
    public string Shown => VersionPins.Plain(string.IsNullOrWhiteSpace(Found) ? Pinned : Found);
}

public static class VersionPins
{
    public const string MatchTip = "Matches the version this tool is made for.";
    public const string FoundTip = "Found. This tool works with any version.";
    public const string MissingTip = "Not found yet.";
    public const string MismatchTip = "Not the version this tool is made for.";

    public static PinState Check(string? pinned, string? found, bool baseGame = false, IReadOnlyList<string>? alsoAccepted = null)
    {
        if (string.IsNullOrWhiteSpace(found))
            return PinState.Missing;
        if (string.IsNullOrWhiteSpace(pinned))
            return PinState.Match;
        if (Same(pinned, found, baseGame))
            return PinState.Match;
        return (alsoAccepted ?? []).Any(version => Same(version, found, false)) ? PinState.Match : PinState.Mismatch;
    }

    /// <summary>The hover line of a row's mark.</summary>
    public static string Tip(VersionPin pin) => pin.State switch
    {
        PinState.Match => string.IsNullOrWhiteSpace(pin.Pinned) ? FoundTip : MatchTip,
        PinState.Missing => MissingTip,
        _ => MismatchTip,
    };

    /// <summary>"v1.4.8" as "1.4.8".</summary>
    public static string Plain(string? version)
    {
        var text = (version ?? "").Trim();
        return text.Length > 1 && (text[0] is 'v' or 'V' or 'e' or 'E') && char.IsDigit(text[1]) ? text[1..] : text;
    }

    private static bool Same(string pinned, string found, bool baseGame)
    {
        if (!TryParts(pinned, out var want) || !TryParts(found, out var have))
            return Plain(pinned).Equals(Plain(found), StringComparison.OrdinalIgnoreCase);
        if (baseGame)
            return have.Length >= want.Length && want.Select((part, index) => part == have[index]).All(same => same);
        // 1.1.0 and 1.1 are the same version.
        var length = Math.Max(want.Length, have.Length);
        for (var index = 0; index < length; index++)
            if ((index < want.Length ? want[index] : 0) != (index < have.Length ? have[index] : 0))
                return false;
        return true;
    }

    private static bool TryParts(string version, out BigInteger[] parts)
    {
        parts = [];
        var match = Regex.Match(version.Trim(), @"^[a-zA-Z]?(\d+(?:\.\d+)*)$");
        if (!match.Success)
            return false;
        parts = match.Groups[1].Value.Split('.').Select(BigInteger.Parse).ToArray();
        return true;
    }
}
