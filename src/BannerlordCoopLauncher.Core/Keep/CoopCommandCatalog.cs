using System.Globalization;
using System.Reflection;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace BannerlordCoopLauncher.Core.Keep;

/// <summary>One parameter of a Coop command, from its usage line: &lt;name&gt; or [&lt;name&gt;].</summary>
public sealed record CoopParameter(string Name, bool Optional);

/// <summary>
/// One coop.debug command as bannerlordcoop.com/cheats lists it: the name, what it does, the usage
/// line, the category, where it runs (SERVER, CLIENT or BOTH) and its type (GAMEPLAY, INSPECT, AUDIT).
/// </summary>
public sealed record CoopCommandInfo(string Name, string Description, string Usage, string Category, string Side, string Type)
{
    [JsonIgnore]
    public IReadOnlyList<CoopParameter> Parameters => CoopCommandCatalog.ParametersOf(Usage);

    /// <summary>A CLIENT command runs in a player's own game, so the server console cannot send it.</summary>
    [JsonIgnore]
    public bool IsClientOnly => string.Equals(Side, "CLIENT", StringComparison.OrdinalIgnoreCase);

    /// <summary>"Hero development" for HERO DEVELOPMENT.</summary>
    [JsonIgnore]
    public string CategoryName => CoopCommandCatalog.Pretty(Category);

    [JsonIgnore]
    public string SideName => CoopCommandCatalog.Pretty(Side);
}

/// <summary>
/// The Coop command list shipped with the host (an embedded copy of bannerlordcoop.com/cheats?tab=all),
/// the parser that makes it from the page text, and the builder that turns typed values into a command line.
/// </summary>
public static class CoopCommandCatalog
{
    public const string ResourceName = "Coodsz.CoopCommands.json";

    private static readonly Lazy<IReadOnlyList<CoopCommandInfo>> Embedded = new(LoadEmbedded);
    private static readonly Regex NameLine = new(@"^coop\.[a-z0-9_]+(\.[a-z0-9_]+)*$", RegexOptions.CultureInvariant);
    private static readonly Regex ParameterToken = new(@"\[[^\]]*\]|<[^>]+>", RegexOptions.CultureInvariant);
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true, Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    /// <summary>Every command of the shipped list.</summary>
    public static IReadOnlyList<CoopCommandInfo> All => Embedded.Value;

    public static CoopCommandInfo? Find(string? name)
    {
        var clean = (name ?? "").Trim();
        return All.FirstOrDefault(c => string.Equals(c.Name, clean, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Reads the text of the cheats page. Each command is a block of lines: the name, the description,
    /// the usage line, maybe "Parameters", then the category, the side, the type, "COPY LINK" and "COPY".
    /// </summary>
    public static IReadOnlyList<CoopCommandInfo> ParsePageText(string text)
    {
        var lines = (text ?? "").Replace("\r", "").Split('\n').Select(l => l.Trim()).ToList();
        var result = new List<CoopCommandInfo>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        for (var i = 0; i < lines.Count; i++)
        {
            if (!NameLine.IsMatch(lines[i]))
                continue;
            var end = lines.FindIndex(i + 1, l => l == "COPY LINK");
            if (end < 0)
                break;
            var block = lines.Skip(i + 1).Take(end - i - 1).Where(l => l.Length > 0).ToList();
            var name = lines[i];
            var usageAt = block.FindIndex(l => l == name || l.StartsWith(name + " ", StringComparison.Ordinal));
            if (usageAt < 0 || block.Count - usageAt < 4)
            {
                i = end;
                continue;
            }

            var tail = block.Skip(usageAt + 1).Where(l => l != "Parameters").ToList();
            if (tail.Count < 3)
            {
                i = end;
                continue;
            }

            var info = new CoopCommandInfo(name, string.Join(" ", block.Take(usageAt)), block[usageAt],
                tail[^3], tail[^2], tail[^1]);
            if (seen.Add(name))
                result.Add(info);
            i = end + 1 < lines.Count && lines[end + 1] == "COPY" ? end + 1 : end;
        }

        return result;
    }

    /// <summary>The parameters of a usage line; everything inside [...] is optional.</summary>
    public static IReadOnlyList<CoopParameter> ParametersOf(string usage)
    {
        var list = new List<CoopParameter>();
        foreach (Match m in ParameterToken.Matches(usage ?? ""))
        {
            var optional = m.Value.StartsWith('[');
            var inner = optional ? m.Value[1..^1] : m.Value;
            foreach (Match p in Regex.Matches(inner, "<([^>]+)>"))
                list.Add(new CoopParameter(p.Groups[1].Value.Trim(), optional));
            if (optional && !inner.Contains('<') && inner.Trim().Length > 0)
                list.Add(new CoopParameter(inner.Trim(), true));
        }

        return list;
    }

    public static string ToJson(IReadOnlyList<CoopCommandInfo> commands) =>
        JsonSerializer.Serialize(commands, Options).Replace("\r\n", "\n") + "\n";

    public static IReadOnlyList<CoopCommandInfo> FromJson(string json) =>
        JsonSerializer.Deserialize<List<CoopCommandInfo>>(json) ?? new List<CoopCommandInfo>();

    private static IReadOnlyList<CoopCommandInfo> LoadEmbedded()
    {
        try
        {
            using var stream = typeof(CoopCommandCatalog).Assembly.GetManifestResourceStream(ResourceName);
            if (stream == null)
                return Array.Empty<CoopCommandInfo>();
            using var reader = new StreamReader(stream, Encoding.UTF8);
            return FromJson(reader.ReadToEnd());
        }
        catch (JsonException)
        {
            return Array.Empty<CoopCommandInfo>();
        }
    }

    /// <summary>The categories in list order, each once.</summary>
    public static IReadOnlyList<string> Categories(IEnumerable<CoopCommandInfo> commands) =>
        commands.Select(c => c.Category).Distinct(StringComparer.Ordinal).OrderBy(c => c, StringComparer.Ordinal).ToList();

    /// <summary>The commands whose name or description has every word of the search, in one category or all.</summary>
    public static IReadOnlyList<CoopCommandInfo> Search(IEnumerable<CoopCommandInfo> commands, string? query, string? category = null)
    {
        var words = (query ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return commands
            .Where(c => string.IsNullOrEmpty(category) || c.Category == category)
            .Where(c => words.All(w => c.Name.Contains(w, StringComparison.OrdinalIgnoreCase)
                                        || c.Description.Contains(w, StringComparison.OrdinalIgnoreCase)))
            .ToList();
    }

    /// <summary>"Hero development" for HERO DEVELOPMENT.</summary>
    public static string Pretty(string upper)
    {
        var lower = (upper ?? "").ToLowerInvariant();
        return lower.Length == 0 ? "" : char.ToUpperInvariant(lower[0]) + lower[1..];
    }

    /// <summary>
    /// Why these values cannot make a command yet: a required box left empty (with the usage line).
    /// Null when the command can be built.
    /// </summary>
    public static string? Problem(CoopCommandInfo command, IReadOnlyList<string?> values)
    {
        var parameters = command.Parameters;
        for (var i = 0; i < parameters.Count; i++)
        {
            var value = i < values.Count ? (values[i] ?? "").Trim() : "";
            if (!parameters[i].Optional && value.Length == 0)
                return "Fill in " + parameters[i].Name + ". Usage " + command.Usage;
            if (value.Contains('\n') || value.Contains('\r'))
                return "A box has more than one line. Usage " + command.Usage;
        }

        // An optional value after an empty optional one would land in the wrong place.
        var lastFilled = -1;
        for (var i = 0; i < parameters.Count; i++)
            if (i < values.Count && !string.IsNullOrWhiteSpace(values[i]))
                lastFilled = i;
        for (var i = 0; i < lastFilled; i++)
            if (parameters[i].Optional && (i >= values.Count || string.IsNullOrWhiteSpace(values[i])))
                return "Fill in " + parameters[i].Name + " too, or leave the boxes after it empty. Usage " + command.Usage;
        return null;
    }

    /// <summary>The command line: the name and each filled value, quoted when it has a space.</summary>
    public static string Build(CoopCommandInfo command, IReadOnlyList<string?> values)
    {
        var parts = new List<string> { command.Name };
        var count = Math.Min(values.Count, command.Parameters.Count);
        for (var i = 0; i < count; i++)
        {
            var value = (values[i] ?? "").Trim();
            if (value.Length == 0)
                break;
            parts.Add(CoopArg.Quote(value));
        }

        return string.Join(" ", parts);
    }

    /// <summary>
    /// A typed command line checked against the list: a CLIENT command, or one with fewer values than
    /// it needs, gives the line to show (with the usage). Null when it looks fine or is not in the list.
    /// </summary>
    public static string? CheckLine(string? line, IReadOnlyList<CoopCommandInfo>? commands = null)
    {
        var tokens = CoopArg.Split(line);
        if (tokens.Count == 0)
            return null;
        var list = commands ?? All;
        var command = list.FirstOrDefault(c => string.Equals(c.Name, tokens[0], StringComparison.OrdinalIgnoreCase));
        if (command == null)
            return tokens[0].StartsWith("coop.", StringComparison.OrdinalIgnoreCase)
                ? "There is no " + tokens[0] + " in the Coop command list. Check the name in the list below."
                : null;
        if (command.IsClientOnly)
            return ClientOnlyNote;
        var required = command.Parameters.Count(p => !p.Optional);
        var given = tokens.Count - 1;
        if (given < required)
            return "This command needs " + required.ToString(CultureInfo.InvariantCulture) + (required == 1 ? " value" : " values") + ". Usage " + command.Usage;
        if (given > command.Parameters.Count && command.Parameters.Count > 0)
            return "This command has more values than it takes. Put quotes around a name with spaces. Usage " + command.Usage;
        return null;
    }

    public const string ClientOnlyNote =
        "This is a CLIENT command, so it is not sent from here. The player types it in their own game. They open the console with Alt and the backtick key.";
}

/// <summary>Values on a Coop command line: Coop splits on spaces and reads "quoted values" as one.</summary>
public static class CoopArg
{
    /// <summary>The value as one argument: quoted when it has a space or a quote, with \ and " escaped.</summary>
    public static string Quote(string? value)
    {
        var clean = string.Join(' ', (value ?? "").Replace('\r', ' ').Replace('\n', ' ').Replace('\t', ' ')
            .Split(' ', StringSplitOptions.RemoveEmptyEntries));
        if (clean.Length > 0 && !clean.Any(c => c is ' ' or '"' or '\\'))
            return clean;
        return "\"" + clean.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";
    }

    /// <summary>Splits a command line the way Coop does: spaces, with "quoted values" kept whole.</summary>
    public static IReadOnlyList<string> Split(string? line)
    {
        var result = new List<string>();
        var current = new StringBuilder();
        var quoted = false;
        var any = false;
        var text = line ?? "";
        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            if (c == '\\' && i + 1 < text.Length && text[i + 1] is '"' or '\\')
            {
                current.Append(text[++i]);
                any = true;
                continue;
            }

            if (c == '"')
            {
                quoted = !quoted;
                any = true;
                continue;
            }

            if (char.IsWhiteSpace(c) && !quoted)
            {
                if (any)
                    result.Add(current.ToString());
                current.Clear();
                any = false;
                continue;
            }

            current.Append(c);
            any = true;
        }

        if (any)
            result.Add(current.ToString());
        return result;
    }
}
