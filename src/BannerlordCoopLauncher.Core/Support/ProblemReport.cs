using System.Globalization;
using System.Text.RegularExpressions;

namespace BannerlordCoopLauncher.Core.Support;

// Takes the server password, addresses, Steam ids and the Windows user name out
// of log text before the logs and log zips write it.
public static class ProblemReport
{
    // Four numbers with dots, but not part of a longer dotted number like 1.2.3.4.5. ASCII digits only,
    // as \d also takes other scripts' digits.
    private static readonly Regex Ipv4 = new(@"\b(?<![0-9]\.)([0-9]{1,3})\.([0-9]{1,3})\.([0-9]{1,3})\.([0-9]{1,3})\b(?!\.[0-9])",
        RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
    // Text right before a version number: Version=3.0.3.0, FileVersion: 1.2.3.4, runtime 2.4.2.248, v 1.0.0.0.
    // Whole words only, so "conversion 10.0.0.5" is still an address.
    private static readonly Regex VersionBefore = new(@"(?i)(?:\b(?:file|product|assembly)?version|\bruntime|\bbuild|\bv)[ \t]*[=:]?[ \t]*$",
        RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));

    public static string Redact(string? text)
    {
        if (string.IsNullOrEmpty(text))
            return "";
        var cleaned = Ipv4.Replace(text, match => IsVersion(text, match) ? match.Value : "[address]");
        cleaned = Regex.Replace(cleaned, @"\b7656\d{10,}\b", "[steam id]", RegexOptions.None, TimeSpan.FromSeconds(1));
        // A quoted value (COODSZ_COOP_PASSWORD = "two words") goes as a whole, and the value
        // is only looked for on the same line.
        cleaned = Regex.Replace(
            cleaned,
            @"(?i)(password[ \t]*[:=][ \t]*)(""[^""\r\n]*""|'[^'\r\n]*'|\S+)",
            match => match.Groups[1].Value + "[password]",
            RegexOptions.None,
            TimeSpan.FromSeconds(1));
        cleaned = Regex.Replace(cleaned, @"(?i)Users\\[^\\]+", @"Users\[user]", RegexOptions.None, TimeSpan.FromSeconds(1));
        return cleaned;
    }

    // A dotted quad stays when a part is over 255 (no address has that) or when version text comes
    // right before it, unless a :port follows, which only an address has.
    private static bool IsVersion(string text, Match match)
    {
        var end = match.Index + match.Length;
        if (end + 1 < text.Length && text[end] == ':' && char.IsAsciiDigit(text[end + 1]))
            return false;
        if (Enumerable.Range(1, 4).Any(group =>
                int.TryParse(match.Groups[group].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var part) && part > 255))
            return true;
        var start = Math.Max(0, match.Index - 40);
        return VersionBefore.IsMatch(text.Substring(start, match.Index - start));
    }
}
