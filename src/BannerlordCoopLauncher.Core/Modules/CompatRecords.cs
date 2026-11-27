using BannerlordCoopLauncher.Core.IO;
// The compat record format is ModderLords' by Ryvius (@PlueRyvius), used with permission
// (https://github.com/PlueRyvius/ModderLords, docs/COMPAT-RECORD-FORMAT.md). This file reads his
// curated compat-db.json; nothing of his code is in it.
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace BannerlordCoopLauncher.Core.Modules;

/// <summary>A ModderLords verdict. NeedsRecipe runs only with the settings in the record.</summary>
public enum CompatVerdict
{
    Unknown,
    Works,
    NeedsRecipe,
    Broken,
}

/// <summary>
/// One ModderLords compat record, as far as the Coodsz tools use it. DefaultRole, ServerAuthoritative,
/// ClientSideBehaviors, EnsureLines and the other launcher fields are read past and not used yet.
/// </summary>
public sealed record CompatRecord
{
    public required string Id { get; init; }
    public CompatVerdict Verdict { get; init; }
    public IReadOnlyList<string> TestedVersions { get; init; } = [];
    public string? TestedCoopVersion { get; init; }
    public string? Notes { get; init; }
    public string? Url { get; init; }
    public DateTime? UpdatedAt { get; init; }

    /// <summary>Settings id to setting name to value as text, for the host's game.</summary>
    public IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> DefaultSettings { get; init; } =
        new Dictionary<string, IReadOnlyDictionary<string, string>>();
}

/// <summary>The small verdict chip next to the count chips. Kind is Works, NeedsRecipe or Broken.</summary>
public sealed record CompatTestedChip(string Kind, string Text, string Tip, bool OlderVersion)
{
    public CompatTestedChip Translated() => this with
    {
        Text = Localization.Translator.T(Text),
        Tip = string.Join("\n", Tip.Split('\n').Select(Localization.Translator.T))
    };
}

/// <summary>A read compat-db.json: the records by module id, without regard to case.</summary>
public sealed class CompatRecordDatabase
{
    private readonly Dictionary<string, CompatRecord> _byId;

    public CompatRecordDatabase(IEnumerable<CompatRecord> records)
    {
        _byId = new Dictionary<string, CompatRecord>(StringComparer.OrdinalIgnoreCase);
        foreach (var record in records)
            _byId.TryAdd(record.Id, record);
    }

    public static CompatRecordDatabase Empty { get; } = new([]);

    public int Count => _byId.Count;

    public IEnumerable<CompatRecord> All => _byId.Values;

    public CompatRecord? Find(string? moduleId) =>
        moduleId?.Trim() is { Length: > 0 } id && _byId.TryGetValue(id, out var record) ? record : null;
}

/// <summary>
/// ModderLords' curated compat records (compat-db.json by Ryvius): downloaded once per tool run next to the
/// community list, kept in AppData, and read leniently as his format page says. The file is untrusted data:
/// a record or field that breaks the limits or holds control or invisible characters is dropped, and a file
/// with a SchemaVersion newer than 1 is refused as a whole.
/// </summary>
public static class CompatRecords
{
    public const string DefaultUrl = "https://raw.githubusercontent.com/PlueRyvius/ModderLords/main/src/ModderLords.Core/compat-db.json";
    public const int SchemaVersion = 1;
    public const string Credit = "From ModderLords by Ryvius.";

    // Limits from the format page, and a cap for the whole file.
    public const int MaxFileChars = 2_000_000;
    public const int MaxRecords = 5000;
    public const int MaxTestedVersions = 20;
    public const int MaxVersionLength = 40;
    public const int MaxNotesLength = 8000;
    public const int MaxUrlLength = 300;
    public const int MaxSettingsObjects = 20;
    public const int MaxSettingsPerObject = 100;
    public const int MaxSettingNameLength = 200;
    public const int MaxSettingValueLength = 500;

    // The tooltip shows at most this much of the notes, in lines of about this width.
    private const int TipNotesLength = 600;
    private const int TipLineWidth = 90;

    private static readonly Regex IdPattern = new("^(?=.{1,64}$)[A-Za-z0-9][A-Za-z0-9._-]*(?: [A-Za-z0-9._-]+)*$", RegexOptions.CultureInvariant);

    public static string CachePath { get; set; } =
        Path.Combine(ToolData.RoamingRoot, "compat-records.json");

    private static readonly object Gate = new();
    private static CompatRecordDatabase? _current;
    private static Task<bool>? _refresh;

    public static CompatRecordDatabase Current
    {
        get
        {
            lock (Gate)
                return _current ??= LoadCached(CachePath);
        }
    }

    /// <summary>Downloads the records once per run; true when a downloaded copy replaced the kept one.</summary>
    public static Task<bool> RefreshAsync(string userAgent)
    {
        lock (Gate)
            return _refresh ??= Task.Run(async () =>
            {
                try
                {
                    using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(8) };
                    http.DefaultRequestHeaders.UserAgent.ParseAdd(userAgent);
                    var db = await FetchAsync(http, CachePath).ConfigureAwait(false);
                    if (db == null)
                        return false;
                    lock (Gate)
                        _current = db;
                    return true;
                }
                catch (Exception)
                {
                    return false;
                }
            });
    }

    /// <summary>Downloads and keeps a copy. Null when it failed; the kept copy then stays in use.</summary>
    public static async Task<CompatRecordDatabase?> FetchAsync(HttpClient http, string cachePath, string url = DefaultUrl,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var json = await http.GetStringAsync(url, cancellationToken).ConfigureAwait(false);
            if (Parse(json) is not { } db)
                return null;
            Directory.CreateDirectory(Path.GetDirectoryName(cachePath)!);
            var temp = cachePath + ".tmp";
            await File.WriteAllTextAsync(temp, json, cancellationToken).ConfigureAwait(false);
            File.Move(temp, cachePath, overwrite: true);
            return db;
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException or IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>The kept copy, or no records when there is none or it cannot be read.</summary>
    public static CompatRecordDatabase LoadCached(string cachePath)
    {
        try
        {
            return File.Exists(cachePath) && Parse(File.ReadAllText(cachePath)) is { } db ? db : CompatRecordDatabase.Empty;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return CompatRecordDatabase.Empty;
        }
    }

    /// <summary>
    /// The lenient reader: names without regard to case, enums by name or number, unknown fields ignored.
    /// Null for a damaged file, one that is too big, or one with a SchemaVersion newer than 1.
    /// </summary>
    public static CompatRecordDatabase? Parse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json) || json.Length > MaxFileChars)
            return null;
        try
        {
            using var doc = JsonDocument.Parse(json, new JsonDocumentOptions { MaxDepth = 16 });
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
                return null;
            if (Get(root, "SchemaVersion") is { } version)
            {
                if (version.ValueKind != JsonValueKind.Number || !version.TryGetInt32(out var number) || number > SchemaVersion)
                    return null;
            }
            if (Get(root, "Records") is not { ValueKind: JsonValueKind.Array } list)
                return null;
            var records = new List<CompatRecord>();
            foreach (var item in list.EnumerateArray())
            {
                if (records.Count >= MaxRecords)
                    break;
                if (ReadRecord(item) is { } record)
                    records.Add(record);
            }
            return new CompatRecordDatabase(records);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static CompatRecord? ReadRecord(JsonElement item)
    {
        if (item.ValueKind != JsonValueKind.Object)
            return null;
        var id = Text(Get(item, "Id"), 64);
        if (id == null || !IdPattern.IsMatch(id))
            return null;
        return new CompatRecord
        {
            Id = id,
            Verdict = ReadVerdict(Get(item, "Verdict")),
            TestedVersions = ReadVersions(Get(item, "TestedVersions")),
            TestedCoopVersion = Text(Get(item, "TestedCoopVersion"), MaxVersionLength),
            Notes = Text(Get(item, "Notes"), MaxNotesLength),
            Url = Text(Get(item, "Url"), MaxUrlLength) is { } url && IsWebUrl(url) ? url : null,
            UpdatedAt = Get(item, "UpdatedAt") is { ValueKind: JsonValueKind.String } at
                && DateTime.TryParse(at.GetString(), CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var when)
                    ? when
                    : null,
            DefaultSettings = ReadSettings(Get(item, "DefaultSettings")),
        };
    }

    private static CompatVerdict ReadVerdict(JsonElement? value)
    {
        if (value is not { } v)
            return CompatVerdict.Unknown;
        if (v.ValueKind == JsonValueKind.Number)
            return v.TryGetInt32(out var n) && Enum.IsDefined(typeof(CompatVerdict), n) ? (CompatVerdict)n : CompatVerdict.Unknown;
        if (v.ValueKind == JsonValueKind.String)
            foreach (var verdict in Enum.GetValues<CompatVerdict>())
                if (verdict.ToString().Equals((v.GetString() ?? "").Trim(), StringComparison.OrdinalIgnoreCase))
                    return verdict;
        return CompatVerdict.Unknown;
    }

    private static IReadOnlyList<string> ReadVersions(JsonElement? value)
    {
        if (value is not { ValueKind: JsonValueKind.Array } list)
            return [];
        return list.EnumerateArray()
            .Select(v => Text(v, MaxVersionLength))
            .Where(v => v != null)
            .Select(v => v!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(MaxTestedVersions)
            .ToList();
    }

    private static IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> ReadSettings(JsonElement? value)
    {
        var result = new Dictionary<string, IReadOnlyDictionary<string, string>>(StringComparer.Ordinal);
        if (value is not { ValueKind: JsonValueKind.Object } objects)
            return result;
        foreach (var settings in objects.EnumerateObject())
        {
            if (result.Count >= MaxSettingsObjects)
                break;
            var settingsId = Clean(settings.Name, MaxSettingNameLength);
            if (settingsId == null || settings.Value.ValueKind != JsonValueKind.Object || result.ContainsKey(settingsId))
                continue;
            var values = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var setting in settings.Value.EnumerateObject())
            {
                if (values.Count >= MaxSettingsPerObject)
                    break;
                var name = Clean(setting.Name, MaxSettingNameLength);
                // Every value is text; a reader also takes a plain true, false or number.
                var text = setting.Value.ValueKind switch
                {
                    JsonValueKind.String => setting.Value.GetString(),
                    JsonValueKind.True => "true",
                    JsonValueKind.False => "false",
                    JsonValueKind.Number => setting.Value.GetRawText(),
                    _ => null
                };
                if (name == null || text == null || text.Length > MaxSettingValueLength || HasBadCharacters(text))
                    continue;
                values.TryAdd(name, text);
            }
            if (values.Count > 0)
                result[settingsId] = values;
        }
        return result;
    }

    // A property by name without regard to case (the first one when a file has it twice).
    private static JsonElement? Get(JsonElement obj, string name)
    {
        foreach (var property in obj.EnumerateObject())
            if (property.Name.Equals(name, StringComparison.OrdinalIgnoreCase))
                return property.Value.ValueKind == JsonValueKind.Null ? null : property.Value;
        return null;
    }

    // A string value within the limit and free of control and invisible characters, trimmed; else null.
    private static string? Text(JsonElement? value, int max) =>
        value is { ValueKind: JsonValueKind.String } v ? Clean(v.GetString(), max) : null;

    private static string? Clean(string? text, int max)
    {
        var trimmed = text?.Trim();
        return string.IsNullOrEmpty(trimmed) || trimmed.Length > max || HasBadCharacters(trimmed) ? null : trimmed;
    }

    /// <summary>Control characters (line breaks too) and the invisible direction and formatting characters.</summary>
    public static bool HasBadCharacters(string text)
    {
        foreach (var c in text)
        {
            if (char.IsControl(c))
                return true;
            var category = CharUnicodeInfo.GetUnicodeCategory(c);
            if (category is UnicodeCategory.Format or UnicodeCategory.LineSeparator or UnicodeCategory.ParagraphSeparator
                or UnicodeCategory.PrivateUse or UnicodeCategory.OtherNotAssigned)
                return true;
        }
        return false;
    }

    private static bool IsWebUrl(string url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var uri) && (uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp);

    // ----- The chip -----

    /// <summary>True when the installed version is one of the tested ones (without regard to case or a leading v or e).</summary>
    public static bool IsTestedVersion(CompatRecord record, string? installedVersion)
    {
        var mine = VersionKey(installedVersion);
        return mine.Length > 0 && record.TestedVersions.Any(tested => VersionKey(tested) == mine);
    }

    private static string VersionKey(string? version)
    {
        var v = (version ?? "").Trim().ToLowerInvariant();
        return v.Length > 1 && v[0] is 'v' or 'e' && char.IsAsciiDigit(v[1]) ? v[1..] : v;
    }

    /// <summary>
    /// The verdict chip for a mod: Tested: works, Works with settings or Tested: broken, or Tested on an older
    /// version when the record names versions and the installed one is not among them. Null with no verdict.
    /// </summary>
    public static CompatTestedChip? Chip(CompatRecord? record, string? installedVersion)
    {
        if (record == null)
            return null;
        var (kind, text) = record.Verdict switch
        {
            CompatVerdict.Works => ("Works", "Tested: works"),
            CompatVerdict.NeedsRecipe => ("NeedsRecipe", "Works with settings"),
            CompatVerdict.Broken => ("Broken", "Tested: broken"),
            _ => ("", "")
        };
        if (kind.Length == 0)
            return null;
        var older = record.TestedVersions.Count > 0 && !string.IsNullOrWhiteSpace(installedVersion) && !IsTestedVersion(record, installedVersion);
        var lines = new List<string> { text };
        if (record.Verdict == CompatVerdict.NeedsRecipe && record.DefaultSettings.Count > 0)
            lines.Add("It needs some settings changed. Coodsz Coop Server Tool sets them when you host it.");
        else if (record.Verdict == CompatVerdict.NeedsRecipe)
            lines.Add("It needs some settings changed. The notes say which.");
        foreach (var line in Wrap(Shorten(record.Notes)))
            lines.Add(line);
        if (record.TestedVersions.Count > 0)
            lines.Add("Tested on mod version " + string.Join(", ", record.TestedVersions) + ".");
        if (older)
            lines.Add("You have " + installedVersion!.Trim() + ", which was not tested.");
        if (!string.IsNullOrWhiteSpace(record.TestedCoopVersion))
            lines.Add("Tested with Coop " + record.TestedCoopVersion + ".");
        lines.Add(Credit);
        return new CompatTestedChip(kind, older ? "Tested on an older version" : text, string.Join("\n", lines), older);
    }

    private static string? Shorten(string? notes)
    {
        if (string.IsNullOrWhiteSpace(notes))
            return null;
        var text = notes.Trim();
        if (text.Length <= TipNotesLength)
            return text;
        var cut = text.LastIndexOf(' ', TipNotesLength);
        return text[..(cut > TipNotesLength / 2 ? cut : TipNotesLength)].TrimEnd() + " ...";
    }

    // Notes in lines of about TipLineWidth, so a tooltip never runs off the screen.
    private static IEnumerable<string> Wrap(string? text)
    {
        if (text == null)
            yield break;
        var line = new StringBuilder();
        foreach (var word in text.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            if (line.Length > 0 && line.Length + 1 + word.Length > TipLineWidth)
            {
                yield return line.ToString();
                line.Clear();
            }
            if (line.Length > 0)
                line.Append(' ');
            line.Append(word);
        }
        if (line.Length > 0)
            yield return line.ToString();
    }
}
