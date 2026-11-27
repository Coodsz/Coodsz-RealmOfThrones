using System.Text.RegularExpressions;

namespace BannerlordCoopLauncher.Core.Support;

// What LogZipBuilder.Clean takes out of every text file before it goes into a zip, on top of
// ProblemReport.Redact (IPv4 addresses, SteamID64s, "password = value" and Users\name):
//   - the passwords the tool knows (the join or server password typed in it), wherever they are
//   - Users/name and Users\\name (forward slashes, and JSON's doubled backslashes)
//   - this PC's Windows user name and PC name as whole words
//   - the values on the engine log's "Computer: x User: y" line and after User:, User=, UserName=,
//     ComputerName= and MachineName=, and this PC's user name after Users\ even when it is a
//     common word like Admin
//   - IPv6 addresses
//   - SteamID3 ([U:1:123]), SteamID2 (STEAM_0:1:123) and steamcommunity.com/id/name links
//   - email addresses
//   - a password or token after a command line switch (/cooppassword x, -password "x y",
//     /dedicatedcustomserverauthtoken x)
//   - ServerPassword x and AdminPassword x lines of a dedicated server config
//   - token, secret and api key values (key: value, key=value, "key": "value"), but not
//     .NET's PublicKeyToken, which only names an assembly
//   - <Password>x</Password> and <Token>x</Token> in XML
// A rule that takes too long leaves the whole text out instead of packing it half cleaned.
public static class LogScrubber
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);

    // Names too common to take out as words: they are not anyone's identity and would cut the logs up.
    private static readonly HashSet<string> CommonNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "admin", "administrator", "user", "users", "owner", "guest", "default", "public", "test", "pc", "desktop",
        "laptop", "home", "game", "games", "steam", "server", "client", "windows", "system", "local", "coop", "host",
        "player", "gamer", "bannerlord", "taom", "mod", "mods", "root", "dev", "main", "work", "office", "computer"
    };

    private static Regex Rx(string pattern, RegexOptions options = RegexOptions.None) =>
        new(pattern, options | RegexOptions.CultureInvariant | RegexOptions.Compiled, Timeout);

    private static readonly Regex UsersPath = Rx(@"(?i)(Users(?:\\\\|/)+)(?!\[user\])([^\\/\r\n""'<>|:*?]+)");
    private static readonly Regex Ipv6Full = Rx(@"(?<![\w:.])(?:[0-9A-Fa-f]{1,4}:){7}[0-9A-Fa-f]{1,4}(?![\w:])");
    private static readonly Regex Ipv6Short = Rx(@"(?<![\w:.])(?:[0-9A-Fa-f]{1,4}(?::[0-9A-Fa-f]{1,4}){0,6})?::(?:[0-9A-Fa-f]{1,4}(?::[0-9A-Fa-f]{1,4}){0,6})?(?:%[\w.]+)?(?![\w:])");
    private static readonly Regex SteamId3 = Rx(@"(?i)\[U:[01]:\d+\]");
    private static readonly Regex SteamId2 = Rx(@"\bSTEAM_[0-5]:[01]:\d+\b");
    private static readonly Regex SteamProfile = Rx(@"(?i)(steamcommunity\.com/(?:id|profiles)/)[^/\s""'<>]+");
    private static readonly Regex Email = Rx(@"(?<![\w.+-])[\w.+-]+@[\w-]+(?:\.[\w-]+)*\.[A-Za-z]{2,}\b");
    private static readonly Regex SwitchSecret = Rx(@"(?i)((?<![\w])[/+-][a-z_]*(?:password|passwd|authtoken|token)\s+)(""[^""\r\n]*""|[^\s""]+)");
    private static readonly Regex ConfigPassword = Rx(@"(?im)^([ \t]*(?:Server|Admin|Game)Password[ \t]+)[^\r\n]+");
    private static readonly Regex KeyedToken = Rx(@"(?i)\b((?!publickeytoken)[a-z_.]*(?:token|secret|apikey|api_key))((?:""|')?[ \t]*[:=][ \t]*)(""(?:\\.|[^""\\\r\n])*""|'[^'\r\n]*'|[^\s,;""'<>]+)");
    // The engine log's third line, "Computer: DESKTOP-1 User: Admin": both values go, whatever the
    // names are, even a common word like Admin that the whole word rule leaves alone.
    private static readonly Regex RglComputerLine = Rx(@"(?im)^([^\r\n]*?\bComputer:[ \t]*)(?:[^\r\n]*?)([ \t]+User:[ \t]*)[^\r\n]*?([ \t]*)(?=\r?$)");
    // User: x, User=x, UserName=x, "userName": "x", USERNAME=x, ComputerName=x and MachineName=x.
    private static readonly Regex KeyedUser = Rx(@"(?i)(?<![\w.\[])((?:user|user_?name|computer_?name|machine_?name)(?:""|')?[ \t]*[:=][ \t]*)(""[^""\r\n]*""|'[^'\r\n]*'|[^\s,;""'<>]+)");
    private static readonly Regex XmlSecret =Rx(@"(?i)(<(\w*(?:password|token))\b[^>]*>)([^<]*)(</\2>)");

    /// <summary>This PC's Windows user name and PC name. Tests set their own.</summary>
    public static IReadOnlyList<string> PrivateNames { get; set; } = DefaultNames();

    public static IReadOnlyList<string> DefaultNames()
    {
        var names = new List<string>();
        try
        {
            names.Add(Environment.UserName);
            names.Add(Environment.MachineName);
        }
        catch (InvalidOperationException)
        {
        }

        return names;
    }

    /// <summary>The names worth taking out: three letters or more, and not a common word like Admin or Steam.</summary>
    public static IEnumerable<string> UsableNames(IEnumerable<string?> names) =>
        names.Select(name => (name ?? "").Trim())
            .Where(name => name.Length >= 3 && !CommonNames.Contains(name) && name.Any(char.IsLetterOrDigit))
            .Distinct(StringComparer.OrdinalIgnoreCase);

    /// <summary>The extra rules above. ProblemReport.Redact and the password rules of LogZipBuilder.Clean run around it.</summary>
    public static string Scrub(string text, IEnumerable<string>? secrets = null)
    {
        try
        {
            return ScrubOrThrow(text, secrets);
        }
        catch (RegexMatchTimeoutException)
        {
            return TimedOutText;
        }
    }

    /// <summary>What a text becomes when a rule takes too long.</summary>
    public const string TimedOutText = "[This text could not be cleaned in time, so it was left out.]";

    /// <summary>Scrub, but a rule that takes too long throws RegexMatchTimeoutException, so the caller can leave the file out.</summary>
    public static string ScrubOrThrow(string text, IEnumerable<string>? secrets = null)
    {
        if (string.IsNullOrEmpty(text))
            return text ?? "";
        {
            var cleaned = text;
            foreach (var secret in (secrets ?? Array.Empty<string>())
                         .Where(secret => !string.IsNullOrEmpty(secret) && secret.Length >= LogZipBuilder.MinSecretLength)
                         .Distinct(StringComparer.Ordinal)
                         .OrderByDescending(secret => secret.Length))
                cleaned = IsWordLike(secret)
                    // A short plain word ("lord") is not taken out inside a longer Latin word: inside a mod id
                    // (Bannerlord.Harmony) it mangled the id and showed the password from what was left.
                    // Digits, underscores, symbols and other scripts next to it still count.
                    ? Regex.Replace(cleaned, "(?<![A-Za-z])" + Regex.Escape(secret) + "(?![A-Za-z])", "[password]", RegexOptions.CultureInvariant, Timeout)
                    // Anything else goes wherever it is, as always.
                    : cleaned.Replace(secret, "[password]", StringComparison.Ordinal);

            cleaned = UsersPath.Replace(cleaned, match => match.Groups[1].Value + "[user]");
            // This PC's user name inside a path is always taken out, even a common one like Admin.
            foreach (var name in PrivateNames.Select(name => (name ?? "").Trim()).Where(name => name.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase))
                cleaned = Regex.Replace(cleaned, @"(Users(?:\\+|/+))" + Regex.Escape(name) + @"(?![\p{L}\p{N}_])", "$1[user]",
                    RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, Timeout);
            cleaned = RglComputerLine.Replace(cleaned, match => match.Groups[1].Value + "[user]" + match.Groups[2].Value + "[user]" + match.Groups[3].Value);
            cleaned = KeyedUser.Replace(cleaned, match =>
            {
                var value = match.Groups[2].Value;
                var quote = value.Length > 1 && (value[0] == '"' || value[0] == '\'') ? value[0].ToString() : "";
                var inner = quote.Length > 0 ? value[1..^1] : value;
                return inner.Length == 0 || inner == "[user]" ? match.Value : match.Groups[1].Value + quote + "[user]" + quote;
            });
            foreach (var name in UsableNames(PrivateNames))
                cleaned = Regex.Replace(cleaned, @"(?<![\p{L}\p{N}_])" + Regex.Escape(name) + @"(?![\p{L}\p{N}_])", "[user]",
                    RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, Timeout);
            cleaned = Ipv6Full.Replace(cleaned, "[address]");
            cleaned = Ipv6Short.Replace(cleaned, match => HexGroups(match.Value) >= 2 ? "[address]" : match.Value);
            cleaned = SteamId3.Replace(cleaned, "[steam id]");
            cleaned = SteamId2.Replace(cleaned, "[steam id]");
            cleaned = SteamProfile.Replace(cleaned, match => match.Groups[1].Value + "[steam id]");
            cleaned = Email.Replace(cleaned, "[email]");
            cleaned = SwitchSecret.Replace(cleaned, match => match.Groups[1].Value + "[password]");
            cleaned = ConfigPassword.Replace(cleaned, match => match.Groups[1].Value + "[password]");
            cleaned = KeyedToken.Replace(cleaned, match =>
            {
                var value = match.Groups[3].Value;
                var quote = value.Length > 1 && (value[0] == '"' || value[0] == '\'') ? value[0].ToString() : "";
                var inner = quote.Length > 0 ? value[1..^1] : value;
                return inner.Length == 0 ? match.Value : match.Groups[1].Value + match.Groups[2].Value + quote + "[token]" + quote;
            });
            cleaned = XmlSecret.Replace(cleaned, match => match.Groups[1].Value + (match.Groups[3].Value.Length == 0 ? "" : "[password]") + match.Groups[4].Value);
            return cleaned;
        }
    }

    // A password that could be part of a mod id or a word: up to 8 Latin letters, nothing else.
    private static bool IsWordLike(string secret) =>
        secret.Length <= 8 && secret.All(ch => ch is >= 'A' and <= 'Z' or >= 'a' and <= 'z');

    private static int HexGroups(string value) =>
        value.Split('%')[0].Split(':', StringSplitOptions.RemoveEmptyEntries).Length;
}
