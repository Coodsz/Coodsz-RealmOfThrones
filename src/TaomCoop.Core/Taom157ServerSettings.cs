using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace TaomCoop.Core;

/// <summary>One setting in TAOM's taom-server-settings.defaults.json: its server default as JSON text, its kind, its range for a
/// number, and its options for a dropdown.</summary>
public sealed record Taom157SettingDefault(string Json, string Kind, double Min = 0, double Max = 0, IReadOnlyList<string>? Options = null);

/// <summary>What <see cref="Taom157ServerSettings.Build"/> puts in the file: key to JSON text, and how many it left out.</summary>
public sealed record Taom157SettingsBuild(IReadOnlyDictionary<string, string> Values, int Skipped, bool DropdownsWait);

/// <summary>
/// The host's TAOM Mod Options on the Coop server for Bannerlord 1.5.7. A dedicated server does not load MCM, so TAOM reads its
/// settings once per server start from taom-server-settings.json in the server's user folder (our Data folder), and every key the
/// file leaves out runs at its server default (TAOM docs\features\server-settings.md). TAOM.CoopCompat compares each player's
/// settings with the server's, group by group, so a server on TAOM's defaults tells every player their settings differ. On
/// every 1.5.7 start this writes the host's values from Documents\...\ModSettings\Global\TAOM\TAOM.json that differ from
/// TAOM's own list (taom-server-settings.defaults.json, which TAOM writes on every server boot), dropdowns as their option text.
/// Keys of the file that TAOM.json doesn't have (set by hand) stay. TAOM.json is only read.
/// </summary>
public static class Taom157ServerSettings
{
    public const string FileName = "taom-server-settings.json";
    public const string DefaultsFileName = "taom-server-settings.defaults.json";

    /// <summary>This tool's record in the Data folder: the SHA-256 of the file as it last wrote it, then the server's TAOM version
    /// and the SHA-256 of the defaults file it read then.</summary>
    public const string RecordFileName = "coodsz-taom-server-settings.txt";

    public const string NoMcmFile = "TAOM's Mod Options file was not found on this PC, so the server doesn't get your TAOM Mod Options.";

    public const string UnparsedFile =
        "taom-server-settings.json in the Data folder can't be read as settings, so the tool left it as it is and the server doesn't get your TAOM Mod Options. Fix or delete that file, then start the server again.";

    public static string Applied(int count) => "Put " + count + " of your TAOM Mod Options on the server.";

    public static string LeftOut(int count) => count + " of your TAOM Mod Options were left out, because the server's TAOM can't take them.";

    public const string DropdownsWait =
        "The server's TAOM hasn't listed its settings yet, so the dropdown settings of your TAOM Mod Options wait for the next start.";

    public static string McmUnreadable(string reason) => "TAOM's Mod Options file can't be read now, so the server doesn't get your TAOM Mod Options this time. " + reason;

    public static string NotWritten(string reason) =>
        "Could not put your TAOM Mod Options on the server. " + reason + " The server starts with the settings it had.";

    public static string BackedUp(string name) =>
        "Kept a copy of your own taom-server-settings.json as " + name + ". Settings there that TAOM's Mod Options doesn't have stay in the file.";

    /// <summary>The host's TAOM.json under the Documents ModSettings folder (ServerModSettings.DocumentsRoot).</summary>
    public static string McmPath(string documentsModSettings) => Path.Combine(documentsModSettings, "Global", "TAOM", "TAOM.json");

    private static readonly JsonDocumentOptions Lenient = new() { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true };

    // A key line of the defaults file: "Name": value, /* kind, range or options, group */
    private static readonly Regex DefaultLine = new(@"^\s*""(?<key>[A-Za-z0-9_]+)""\s*:\s*(?<value>.+?)\s*,?\s*/\*(?<meta>.*)\*/\s*$", RegexOptions.CultureInvariant);
    private static readonly Regex Range = new(@"^\s*(?<kind>int|float)\s*,\s*(?<min>-?[0-9.Ee+-]+)\s+to\s+(?<max>-?[0-9.Ee+-]+)\s*,", RegexOptions.CultureInvariant);
    private static readonly Regex JsonString = new(@"\G""(?:[^""\\]|\\.)*""", RegexOptions.CultureInvariant);

    /// <summary>
    /// The settings of TAOM's defaults file, by key. A line it can't read is left out (that key is then written as the host has
    /// it, unchecked). A dropdown's options are read as JSON strings; one whose options hold "*/" is left out, since its
    /// comment can't be told apart.
    /// </summary>
    public static IReadOnlyDictionary<string, Taom157SettingDefault> ReadDefaults(string text)
    {
        var defaults = new Dictionary<string, Taom157SettingDefault>(StringComparer.Ordinal);
        foreach (var line in text.Split('\n'))
        {
            var match = DefaultLine.Match(line.TrimEnd('\r'));
            if (!match.Success)
                continue;
            var key = match.Groups["key"].Value;
            var value = match.Groups["value"].Value;
            var meta = match.Groups["meta"].Value.Trim();
            if (meta.StartsWith("bool,", StringComparison.Ordinal))
                defaults[key] = new Taom157SettingDefault(value, "bool");
            else if (meta.StartsWith("text,", StringComparison.Ordinal))
                defaults[key] = new Taom157SettingDefault(value, "text");
            else if (Range.Match(meta) is { Success: true } range
                     && double.TryParse(range.Groups["min"].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var min)
                     && double.TryParse(range.Groups["max"].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var max))
                defaults[key] = new Taom157SettingDefault(value, range.Groups["kind"].Value, min, max);
            else if (meta.StartsWith("one of ", StringComparison.Ordinal) && Options(meta["one of ".Length..]) is { } options
                     && !options.Any(option => option.Contains("*/", StringComparison.Ordinal)))
                defaults[key] = new Taom157SettingDefault(value, "dropdown", Options: options);
        }

        return defaults;
    }

    // "A" | "B" | "C", group: the strings as JSON tokens, or null when they don't read.
    private static List<string>? Options(string text)
    {
        var options = new List<string>();
        var at = 0;
        while (true)
        {
            var token = JsonString.Match(text, at);
            if (!token.Success)
                return null;
            try
            {
                options.Add(JsonSerializer.Deserialize<string>(token.Value)!);
            }
            catch (JsonException)
            {
                return null;
            }

            at = token.Index + token.Length;
            if (text.AsSpan(at).StartsWith(" | "))
                at += 3;
            // Then the group after a comma, or the end of the comment when the setting has no group.
            else
                return options.Count > 0 && text.AsSpan(at).TrimStart() is { } rest && (rest.IsEmpty || rest.StartsWith(",")) ? options : null;
        }
    }

    /// <summary>
    /// The keys to write from the host's TAOM.json. With defaults that fit this server's TAOM (trusted), a key is written only when
    /// it differs from its server default; it is checked against its kind and its range after narrowing a float as TAOM does; a
    /// dropdown's index becomes its option text; a key TAOM doesn't list is left out. With defaults from another TAOM, every key
    /// is written unchecked except the dropdowns, whose options may have changed. With no defaults yet, every key is written
    /// unchecked: TAOM keeps its default for a dropdown index, with a warning. Either way the dropdowns wait for the next start.
    /// </summary>
    public static Taom157SettingsBuild Build(JsonElement mcm, IReadOnlyDictionary<string, Taom157SettingDefault>? defaults, bool trusted)
    {
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        var skipped = 0;
        foreach (var property in mcm.EnumerateObject())
        {
            var value = property.Value;
            var setting = defaults != null && defaults.TryGetValue(property.Name, out var found) ? found : null;
            if (!trusted)
            {
                if (setting?.Kind == "dropdown")
                    continue;
                if (value.ValueKind is JsonValueKind.True or JsonValueKind.False or JsonValueKind.String or JsonValueKind.Number)
                    values[property.Name] = value.GetRawText();
                else
                    skipped++;
                continue;
            }

            var written = setting == null ? null : Value(value, setting);
            if (written == null)
                skipped++;
            else if (!Same(written, setting!))
                values[property.Name] = written;
        }

        return new Taom157SettingsBuild(values, skipped, DropdownsWait: !trusted);
    }

    private static bool IsWhole(JsonElement value) => value.GetRawText().IndexOfAny(['.', 'e', 'E']) < 0;

    // The value as TAOM's file takes it, or null when it doesn't fit the setting.
    private static string? Value(JsonElement value, Taom157SettingDefault setting)
    {
        switch (setting.Kind)
        {
            case "bool":
                return value.ValueKind is JsonValueKind.True or JsonValueKind.False ? value.GetRawText() : null;
            case "text":
                return value.ValueKind == JsonValueKind.String && value.GetString()!.Length <= 256 ? value.GetRawText() : null;
            case "int":
                return value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out var whole) && IsWhole(value) && whole >= setting.Min && whole <= setting.Max
                    ? value.GetRawText()
                    : null;
            case "float":
                if (value.ValueKind != JsonValueKind.Number || !value.TryGetDouble(out var number))
                    return null;
                var narrowed = (float)number;
                return float.IsFinite(narrowed) && narrowed >= (float)setting.Min && narrowed <= (float)setting.Max ? value.GetRawText() : null;
            case "dropdown":
                return value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var index) && index >= 0 && index < setting.Options!.Count
                    ? JsonSerializer.Serialize(setting.Options[index])
                    : null;
            default:
                return null;
        }
    }

    // Equal to the server default: a float after narrowing both, as TAOM compares what it runs.
    private static bool Same(string written, Taom157SettingDefault setting)
    {
        try
        {
            using var a = JsonDocument.Parse(written);
            using var b = JsonDocument.Parse(setting.Json);
            var x = a.RootElement;
            var y = b.RootElement;
            if (setting.Kind == "float" && x.ValueKind == JsonValueKind.Number && y.ValueKind == JsonValueKind.Number)
                return (float)x.GetDouble() == (float)y.GetDouble();
            if (x.ValueKind == JsonValueKind.Number && y.ValueKind == JsonValueKind.Number)
                return x.GetDouble() == y.GetDouble();
            if (x.ValueKind == JsonValueKind.String && y.ValueKind == JsonValueKind.String)
                return x.GetString() == y.GetString();
            return x.ValueKind == y.ValueKind && x.ValueKind is JsonValueKind.True or JsonValueKind.False;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    /// <summary>
    /// Writes dataRoot\taom-server-settings.json from the host's TAOM.json (mcmFile) and returns the lines for the start's log. Keys
    /// already in the file that TAOM.json doesn't have stay. A file that changed since this tool last wrote it is copied first to
    /// taom-server-settings.json.before-tool-&lt;date&gt;, never over an older copy. A file that doesn't parse is left as it is.
    /// The defaults are trusted only when the server's TAOM is the one that wrote them (the record's version and hash); otherwise
    /// this start leaves the dropdowns out and the next start, after TAOM wrote its list again, puts them in. now names the copy.
    /// </summary>
    public static IReadOnlyList<string> Apply(string dataRoot, string mcmFile, string? serverTaomVersion, DateTime now)
    {
        var lines = new List<string>();
        if (!File.Exists(mcmFile))
            return [NoMcmFile];
        JsonElement mcm;
        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(mcmFile), Lenient);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
                return [McmUnreadable("It is not one list of settings.")];
            mcm = document.RootElement.Clone();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            return [McmUnreadable(ex.Message)];
        }

        var path = Path.Combine(dataRoot, FileName);
        var record = ReadRecord(dataRoot);
        var existingText = File.Exists(path) ? File.ReadAllText(path) : null;
        var kept = new Dictionary<string, string>(StringComparer.Ordinal);
        if (existingText != null)
        {
            try
            {
                using var existing = JsonDocument.Parse(existingText, Lenient);
                if (existing.RootElement.ValueKind != JsonValueKind.Object)
                    return [UnparsedFile];
                var mcmKeys = mcm.EnumerateObject().Select(property => property.Name).ToHashSet(StringComparer.Ordinal);
                foreach (var property in existing.RootElement.EnumerateObject())
                {
                    if (!mcmKeys.Contains(property.Name))
                        kept[property.Name] = property.Value.GetRawText();
                }
            }
            catch (JsonException)
            {
                return [UnparsedFile];
            }
        }

        var defaultsPath = Path.Combine(dataRoot, DefaultsFileName);
        string? defaultsHash = null;
        IReadOnlyDictionary<string, Taom157SettingDefault>? defaults = null;
        var trusted = false;
        if (File.Exists(defaultsPath))
        {
            var defaultsText = File.ReadAllText(defaultsPath);
            defaultsHash = Hash(defaultsText);
            defaults = ReadDefaults(defaultsText);
            // The same defaults file as last time, read then on another TAOM: TAOM has not written it again since the update.
            trusted = !(record is { } last && last.DefaultsHash == defaultsHash && !string.Equals(last.TaomVersion, serverTaomVersion ?? "", StringComparison.Ordinal));
        }

        var build = Build(mcm, defaults, trusted);
        var hostValues = build.Values;
        var text = Render(hostValues, kept);
        if (existingText != text)
        {
            // Changed since this tool last wrote it, and not TAOM's empty template: the host's own edit, kept once as a copy.
            var handEdited = existingText != null && record?.FileHash != Hash(existingText) && !IsEmptyObject(existingText);
            if (handEdited)
            {
                var copy = BackupPath(path, now);
                File.Copy(path, copy);
                lines.Add(BackedUp(Path.GetFileName(copy)));
            }

            Directory.CreateDirectory(dataRoot);
            File.WriteAllText(path, text, new UTF8Encoding(false));
        }

        WriteRecord(dataRoot, Hash(text), serverTaomVersion ?? "", defaultsHash ?? "");
        lines.Insert(0, Applied(hostValues.Count));
        if (build.Skipped > 0)
            lines.Add(LeftOut(build.Skipped));
        if (build.DropdownsWait)
            lines.Add(DropdownsWait);
        return lines;
    }

    private static bool IsEmptyObject(string text)
    {
        try
        {
            using var document = JsonDocument.Parse(text, Lenient);
            return document.RootElement.ValueKind == JsonValueKind.Object && !document.RootElement.EnumerateObject().Any();
        }
        catch (JsonException)
        {
            return false;
        }
    }

    // taom-server-settings.json.before-tool-20261122-1530, with -2, -3 after it when that name is taken.
    private static string BackupPath(string path, DateTime now)
    {
        var first = path + ".before-tool-" + now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);
        var copy = first;
        for (var n = 2; File.Exists(copy); n++)
            copy = first + "-" + n;
        return copy;
    }

    private static string Render(IReadOnlyDictionary<string, string> host, IReadOnlyDictionary<string, string> kept)
    {
        var text = new StringBuilder();
        // The file's own header, in English like TAOM's template: a file, not screen text.
        text.Append(string.Join("\r\n",
        [
            "// TAOM server settings, written by the TAOM Server Tool on every start of the Coop server for Bannerlord 1.5.7.",
            "// The keys from your TAOM Mod Options are set again from them each time. Keys you add here that Mod Options",
            "// doesn't have stay. Every key with its default and range is in taom-server-settings.defaults.json beside this file.",
            "{",
            ""
        ]));
        var all = host.Concat(kept).ToList();
        for (var i = 0; i < all.Count; i++)
            text.Append("  ").Append(JsonSerializer.Serialize(all[i].Key)).Append(": ").Append(all[i].Value).Append(i < all.Count - 1 ? ",\r\n" : "\r\n");
        text.Append("}\r\n");
        return text.ToString();
    }

    private sealed record Record(string FileHash, string TaomVersion, string DefaultsHash);

    private static Record? ReadRecord(string dataRoot)
    {
        try
        {
            var lines = File.ReadAllLines(Path.Combine(dataRoot, RecordFileName));
            return lines.Length >= 3 ? new Record(lines[0], lines[1], lines[2]) : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static void WriteRecord(string dataRoot, string fileHash, string taomVersion, string defaultsHash) =>
        File.WriteAllText(Path.Combine(dataRoot, RecordFileName), fileHash + "\r\n" + taomVersion + "\r\n" + defaultsHash + "\r\n");

    private static string Hash(string text) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text)));
}
