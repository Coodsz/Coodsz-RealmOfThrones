// Ported from CoopKeep (https://github.com/Aderimo/bannerlord-coopkeep),
// Installation/ModCompatDatabase.cs, translated from Turkish and changed for the Coodsz tools.

using System.Text.Json;
using System.Text.Json.Serialization;

namespace BannerlordCoopLauncher.Core.Modules;

/// <summary>The compatibility the community reported.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<CommunityStatus>))]
public enum CommunityStatus
{
    Unknown,

    /// <summary>Works together with Coop.</summary>
    Works,

    /// <summary>Gives errors with Coop or stops players from joining.</summary>
    Broken,

    /// <summary>Only runs on the players' PCs (looks, interface); the server keeps it with its code off.</summary>
    ClientOnly,
}

/// <summary>One mod in the list.</summary>
public sealed record ModCompatEntry
{
    [JsonPropertyName("id")] public required string Id { get; init; }
    [JsonPropertyName("status")] public CommunityStatus Status { get; init; }

    /// <summary>The Coop version a hand written verdict was checked with (for example <c>v0.1.6</c>).</summary>
    [JsonPropertyName("coop")] public string? CoopVersion { get; init; }

    [JsonPropertyName("note")] public string? Note { get; init; }

    /// <summary>The dedicated server crashes on the mod's raw Assets folder, so the server copy hides it (<see cref="ServerRawAssets"/>).</summary>
    [JsonPropertyName("serverHideAssets")] public bool ServerHideAssets { get; init; }

    [JsonPropertyName("name")] public string? Name { get; init; }
    [JsonPropertyName("workshop")] public string? Workshop { get; init; }
    [JsonPropertyName("nexus")] public JsonElement? Nexus { get; init; }
    [JsonPropertyName("category")] public string? Category { get; init; }
    [JsonPropertyName("works")] public int Works { get; init; }
    [JsonPropertyName("broken")] public int Broken { get; init; }

    /// <summary>Mods most often in the mod lists of the players who said it works (from the anonymous reports).</summary>
    [JsonPropertyName("testedWith")] public List<string>? TestedWith { get; init; }

    /// <summary>Mods that were in the broken reports and never in a works report: broken together with them.</summary>
    [JsonPropertyName("conflicts")] public List<string>? Conflicts { get; init; }

    /// <summary>Mods players said it works only together with.</summary>
    [JsonPropertyName("needs")] public List<string>? Needs { get; init; }

    /// <summary>The real Module Id is not known; <see cref="Id"/> is the display name.</summary>
    [JsonPropertyName("idGuess")] public bool IdGuess { get; init; }

    public string DisplayName => string.IsNullOrWhiteSpace(Name) ? Id : Name!;

    /// <summary>The Steam Workshop id (digits only), or <see langword="null"/>.</summary>
    public string? WorkshopId => Workshop?.Trim() is { Length: > 0 and <= 20 } id && id.All(char.IsAsciiDigit) ? id : null;

    public string? WorkshopUrl => WorkshopId is { } id ? "https://steamcommunity.com/sharedfiles/filedetails/?id=" + id : null;

    public string? NexusUrl =>
        Nexus is { } n && n.ValueKind is JsonValueKind.Number or JsonValueKind.String && n.ToString() is { Length: > 0 } id
            ? "https://www.nexusmods.com/mountandblade2bannerlord/mods/" + id
            : null;

    /// <summary>The mod's page: Steam Workshop, otherwise Nexus.</summary>
    public string? Url => WorkshopUrl ?? NexusUrl;
}

/// <summary>
/// The community compatibility list: <c>compat-list/mods.json</c> in the public Coodsz Coop and Friends repository.
/// </summary>
/// <remarks>
/// <para>
/// The tools download the list and keep a copy; offline, the last downloaded copy is used,
/// and the copy built into the tools when there is none. Entries are added through GitHub
/// issues people open, counted by the repository's workflow; nothing is collected
/// automatically.
/// </para>
/// </remarks>
public sealed class ModCompatDatabase
{
    /// <summary>The repository the list, the issue form and the tally workflow live in.</summary>
    public const string Repository = "Coodsz/Coodsz-Coop-and-Friends";

    /// <summary>The list on the repository's default branch (main). docs/compat-list-publish.md lists the files that go there.</summary>
    public const string DefaultUrl = "https://raw.githubusercontent.com/" + Repository + "/main/compat-list/mods.json";

    /// <summary>The readable page of the whole list with the player counts (MODS.md, written by the tally there).</summary>
    public const string ModsPageUrl = "https://github.com/" + Repository + "/blob/main/MODS.md";

    private const string EmbeddedName = "Coodsz.CompatList.mods.json";

    private static readonly JsonSerializerOptions Options = new()
    {
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        PropertyNameCaseInsensitive = true,
    };

    private readonly Dictionary<string, ModCompatEntry> _byId;

    /// <summary>Name matches for mods whose real id is not known (<see cref="ModCompatEntry.IdGuess"/>).</summary>
    private readonly Dictionary<string, ModCompatEntry> _byName;

    /// <summary>Workshop item id matches, for mods whose list id is a guess that differs from the real module id.</summary>
    private readonly Dictionary<string, ModCompatEntry> _byWorkshop;

    private ModCompatDatabase(IEnumerable<ModCompatEntry> entries)
    {
        _byId = entries
            .Where(e => !string.IsNullOrWhiteSpace(e.Id))
            .GroupBy(e => Normalize(e.Id))
            .ToDictionary(g => g.Key, g => g.Last());

        _byName = _byId.Values
            .GroupBy(e => Normalize(e.DisplayName))
            .Where(g => g.Key.Length > 0)
            .ToDictionary(g => g.Key, g => g.Last());

        _byWorkshop = _byId.Values
            .Where(e => e.WorkshopId != null)
            .GroupBy(e => e.WorkshopId!)
            .ToDictionary(g => g.Key, g => g.Last());
    }

    public static ModCompatDatabase Empty { get; } = new([]);

    public int Count => _byId.Count;

    /// <summary>Every mod in the list (installed or not).</summary>
    public IEnumerable<ModCompatEntry> All => _byId.Values;

    public ModCompatEntry? Find(string moduleId) =>
        _byId.TryGetValue(Normalize(moduleId), out var e) ? e
        : _byName.TryGetValue(Normalize(moduleId), out var n) && n.IdGuess ? n
        : null;

    /// <summary>The entry for a module id, else for its Steam Workshop item id.</summary>
    public ModCompatEntry? Find(string moduleId, string? workshopId) =>
        Find(moduleId)
        ?? (workshopId?.Trim() is { Length: > 0 } id && id != "0" && _byWorkshop.TryGetValue(id, out var w) ? w : null);

    /// <summary>Reads the list text; <see langword="null"/> when it is damaged.</summary>
    public static ModCompatDatabase? Parse(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json, new JsonDocumentOptions
            {
                CommentHandling = JsonCommentHandling.Skip,
                AllowTrailingCommas = true,
            });

            var array = doc.RootElement.ValueKind == JsonValueKind.Array
                ? doc.RootElement
                : doc.RootElement.TryGetProperty("mods", out var mods) ? mods : default;

            if (array.ValueKind != JsonValueKind.Array) return null;

            var entries = array.Deserialize<List<ModCompatEntry>>(Options) ?? [];
            return new ModCompatDatabase(entries);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>The copy built into the tools.</summary>
    public static ModCompatDatabase Embedded()
    {
        using var stream = typeof(ModCompatDatabase).Assembly.GetManifestResourceStream(EmbeddedName);
        if (stream == null) return Empty;
        using var reader = new StreamReader(stream);
        return Parse(reader.ReadToEnd()) ?? Empty;
    }

    /// <summary>Reads the kept copy; the built in copy when there is none or it is damaged.</summary>
    public static ModCompatDatabase LoadCached(string cachePath) =>
        File.Exists(cachePath) && Parse(SafeRead(cachePath)) is { Count: > 0 } db ? db : Embedded();

    /// <summary>
    /// Downloads the list and keeps a copy. When that fails, the kept copy (or the built in one) is returned.
    /// </summary>
    public static async Task<ModCompatDatabase> FetchAsync(HttpClient http, string cachePath, string url = DefaultUrl,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var json = await http.GetStringAsync(url, cancellationToken).ConfigureAwait(false);
            if (Parse(json) is { Count: > 0 } db)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(cachePath)!);
                var temp = cachePath + ".tmp";
                await File.WriteAllTextAsync(temp, json, cancellationToken).ConfigureAwait(false);
                File.Move(temp, cachePath, overwrite: true);
                return db;
            }
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException or IOException or UnauthorizedAccessException)
        {
            // Offline, or GitHub cannot be reached: fall back to the kept copy.
        }

        return LoadCached(cachePath);
    }

    /// <summary>Ids compare without regard to case, dots or spaces.</summary>
    public static string Normalize(string id) =>
        new(id.Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant).ToArray());

    private static string SafeRead(string path)
    {
        try { return File.ReadAllText(path); }
        catch (IOException) { return ""; }
        catch (UnauthorizedAccessException) { return ""; }
    }
}
