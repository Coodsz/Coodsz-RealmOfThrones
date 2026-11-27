using System.Text.RegularExpressions;

namespace TaomCoop.Core;

public static class ClanInfluenceLookup
{
    public static string? HeroRegistryId(IEnumerable<string> lines, string playerName)
    {
        var wanted = playerName.Trim();
        string? found = null;
        foreach (var line in lines)
        {
            var match = Regex.Match(line, "ID: '([^']*)', Name: '([^']*)'");
            if (!match.Success)
                continue;
            if (!match.Groups[2].Value.Equals(wanted, StringComparison.OrdinalIgnoreCase))
                continue;
            var id = match.Groups[1].Value;
            if (found != null && !found.Equals(id, StringComparison.Ordinal))
                return null;
            found = id;
        }

        return string.IsNullOrWhiteSpace(found) ? null : found;
    }

    public static string? ClanNameFromHeroInfo(IEnumerable<string> lines)
    {
        foreach (var line in lines)
        {
            var match = Regex.Match(line.Trim(), @"^_clan = \((\d+)\) (.+)$");
            if (match.Success)
                return match.Groups[2].Value.Trim();
        }

        return null;
    }

    public static string? ClanIdForName(IEnumerable<string> lines, string clanName)
    {
        var wanted = clanName.Trim();
        string? found = null;
        foreach (var line in lines)
        {
            var match = Regex.Match(line, "ID: '([^']*)' Name: '([^']*)'");
            if (!match.Success)
                continue;
            if (!match.Groups[2].Value.Equals(wanted, StringComparison.OrdinalIgnoreCase))
                continue;
            var id = match.Groups[1].Value;
            if (found != null && !found.Equals(id, StringComparison.Ordinal))
                return null;
            found = id;
        }

        return string.IsNullOrWhiteSpace(found) ? null : found;
    }
}
