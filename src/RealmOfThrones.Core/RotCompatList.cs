using System.Text.Json;
using System.Text.Json.Serialization;
using TaomCoop.Core;

namespace RealmOfThrones.Core;

/// <summary>
/// The Realm of Thrones compatibility list: rot-mod-compat.json in the public Realm of Thrones tools
/// repository (compat/rot-mod-compat.json on its main branch). A copy ships inside RealmOfThrones.Core
/// (Compat\rot-mod-compat.json) and is used when the online file cannot be read or is older; the last
/// good download is kept next to the tool.
///
/// Format: the TAOM list's schema 2 with kind "CoodszRotCompat" and "rot" (the Realm of Thrones version)
/// instead of "taom": { "kind", "schemaVersion": 2, "rot", "game", "coop", "updated", "mods": [ ... ] }.
/// A mod is matched by module id, then by Workshop id, then by its name.
/// </summary>
public sealed class RotCompatList
{
    public const string Kind = "CoodszRotCompat";
    public const string FileName = "rot-mod-compat.json";
    public const int CurrentSchema = 2;
    // Written out in full (RotRelease.Repo is the same repo), so the address test can pin the exact files.
    public const string OnlineUrl = "https://raw.githubusercontent.com/Coodsz/Coodsz-RealmOfThrones/main/compat/rot-mod-compat.json";
    /// <summary>The readable page of the whole list with the player counts (MODS.md, written by the tally there).</summary>
    public const string ModsPageUrl = "https://github.com/Coodsz/Coodsz-RealmOfThrones/blob/main/MODS.md";

    [JsonPropertyName("kind")]
    public string FileKind { get; set; } = Kind;
    public int SchemaVersion { get; set; } = CurrentSchema;
    public string Rot { get; set; } = "";
    public string Game { get; set; } = "";
    public string Coop { get; set; } = "";
    public string Updated { get; set; } = "";
    public List<CompatEntry> Mods { get; set; } = new();

    /// <summary>Reported mods that are not on the hand list, with the player counts the tally gives them
    /// ("community" next to "mods"). They never get a list badge, only the count chips.</summary>
    public List<RotModCounts> Community { get; set; } = new();

    /// <summary>The player counts the tally writes on the entries of "mods" (works, broken), read from the same
    /// file. CompatEntry is the TAOM list's entry and skips those fields.</summary>
    [JsonIgnore]
    public List<RotModCounts> ModCounts { get; private set; } = new();

    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };

    public static RotCompatList Parse(string json)
    {
        RotCompatList? list;
        CountsFile? counts;
        try
        {
            list = JsonSerializer.Deserialize<RotCompatList>(json, Options);
            // The count fields are not on CompatEntry, so a bad one only shows here.
            counts = JsonSerializer.Deserialize<CountsFile>(json, Options);
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException("The Realm of Thrones mod list is not valid. " + ex.Message);
        }

        if (list == null || !Kind.Equals(list.FileKind, StringComparison.Ordinal))
            throw new InvalidOperationException("That file is not a Realm of Thrones mod list.");
        if (list.SchemaVersion < 1)
            throw new InvalidOperationException("The Realm of Thrones mod list is not valid.");
        list.Mods = (list.Mods ?? new List<CompatEntry>()).Where(entry => entry != null && (entry.Id.Length > 0 || entry.WorkshopId.Length > 0 || entry.Name.Length > 0)).ToList();
        foreach (var entry in list.Mods)
        {
            entry.Id ??= "";
            entry.Name ??= "";
            entry.WorkshopId ??= "";
            entry.NexusId ??= "";
            entry.Tag ??= "";
            entry.Class ??= "";
            entry.Note ??= "";
            entry.Source ??= "";
            entry.Checked ??= "";
        }

        list.Rot ??= "";
        list.Updated ??= "";
        list.Community = (list.Community ?? new List<RotModCounts>()).Where(counts => counts != null).Select(RotModCounts.Clean).ToList();
        list.ModCounts = (counts?.Mods ?? new List<RotModCounts>()).Where(entry => entry != null).Select(RotModCounts.Clean).ToList();
        return list;
    }

    // The "mods" array again, for the count fields only.
    private sealed class CountsFile
    {
        public List<RotModCounts>? Mods { get; set; }
    }

    public static RotCompatList BuiltIn()
    {
        using var stream = typeof(RotCompatList).Assembly.GetManifestResourceStream("RealmOfThrones.Compat." + FileName)
            ?? throw new InvalidOperationException("The built in Realm of Thrones mod list is missing.");
        using var reader = new StreamReader(stream);
        return Parse(reader.ReadToEnd());
    }

    /// <summary>True when this list says it is older than the other one (both have an updated date).
    /// A list without a date is never called older.</summary>
    public bool OlderThan(RotCompatList other) =>
        Updated.Length > 0 && other.Updated.Length > 0 && string.CompareOrdinal(Updated.Trim(), other.Updated.Trim()) < 0;

    /// <summary>The saved download next to the tool when it is valid and not older than the built in
    /// list (a tool update brings a newer list), else the built in list.</summary>
    public static RotCompatList Load(string cachePath)
    {
        var builtIn = BuiltIn();
        try
        {
            if (File.Exists(cachePath))
            {
                var saved = Parse(File.ReadAllText(cachePath));
                if (!saved.OlderThan(builtIn))
                    return saved;
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
        }

        return builtIn;
    }

    /// <summary>Downloads the online list (or the given url). A valid list that is not older than the
    /// built in one is saved to cachePath and returned; on any problem null is returned and the list
    /// in use stays.</summary>
    public static async Task<RotCompatList?> DownloadAsync(HttpClient http, string cachePath, CancellationToken cancellationToken, string? url = null)
    {
        var builtIn = BuiltIn();
        try
        {
            var json = await http.GetStringAsync(url ?? OnlineUrl, cancellationToken);
            var list = Parse(json);
            if (list.OlderThan(builtIn))
                return null;
            File.WriteAllText(cachePath, json);
            return list;
        }
        catch (Exception ex) when (ex is HttpRequestException or InvalidOperationException or IOException or UnauthorizedAccessException)
        {
        }
        catch (TaskCanceledException)
        {
            // A timeout or Stop; the list in use stays.
        }

        return null;
    }

    public CompatEntry? Find(string? moduleId, string? workshopId = null, string? name = null)
    {
        if (!string.IsNullOrWhiteSpace(moduleId))
        {
            var byId = Mods.FirstOrDefault(entry => entry.Id.Length > 0 && entry.Id.Equals(moduleId.Trim(), StringComparison.OrdinalIgnoreCase));
            if (byId != null)
                return byId;
        }

        if (!string.IsNullOrWhiteSpace(workshopId) && workshopId.Trim() != "0")
        {
            var byWorkshop = Mods.FirstOrDefault(entry => entry.WorkshopId.Length > 0 && entry.WorkshopId.Equals(workshopId.Trim(), StringComparison.Ordinal));
            if (byWorkshop != null)
                return byWorkshop;
        }

        if (!string.IsNullOrWhiteSpace(name))
            return Mods.FirstOrDefault(entry => entry.Id.Length == 0 && entry.Name.Equals(name.Trim(), StringComparison.OrdinalIgnoreCase));
        return null;
    }

    /// <summary>What players reported for a mod through the Realm of Thrones tools: the counts on its list
    /// entry (matched like Find), else on its "community" entry by module id. Null when nobody reported it.</summary>
    public RotModCounts? CountsFor(string? moduleId, string? workshopId = null, string? name = null)
    {
        var id = (moduleId ?? "").Trim();
        RotModCounts? counts = null;
        if (id.Length > 0)
            counts = ModCounts.FirstOrDefault(entry => entry.Id.Length > 0 && entry.Id.Equals(id, StringComparison.OrdinalIgnoreCase));
        if (counts == null && !string.IsNullOrWhiteSpace(workshopId) && workshopId.Trim() != "0")
            counts = ModCounts.FirstOrDefault(entry => entry.WorkshopId.Length > 0 && entry.WorkshopId.Equals(workshopId.Trim(), StringComparison.Ordinal));
        if (counts == null && !string.IsNullOrWhiteSpace(name))
            counts = ModCounts.FirstOrDefault(entry => entry.Id.Length == 0 && entry.Name.Equals(name.Trim(), StringComparison.OrdinalIgnoreCase));
        if (counts == null && id.Length > 0)
            counts = Community.FirstOrDefault(entry => entry.Id.Equals(id, StringComparison.OrdinalIgnoreCase));
        return counts is { Works: > 0 } or { Broken: > 0 } ? counts : null;
    }

    /// <summary>The short badge text for a tag, or null when nothing is known.</summary>
    public static string? BadgeText(CompatTag tag) => tag switch
    {
        CompatTag.Safe => "Tested safe",
        CompatTag.Everyone => "Everyone needs it",
        CompatTag.Risky => "Risky",
        CompatTag.Broken => "Known broken",
        CompatTag.Reported => "Works in Realm of Thrones, not tested in Coop",
        CompatTag.Loads => "Loads on the server",
        _ => null
    };

    /// <summary>Green, yellow, red, or blue for what is reported or only load tested.</summary>
    public static string BadgeColor(CompatTag tag) => TaomCompatList.BadgeColor(tag);

    /// <summary>Where the entry comes from and when it was checked, as one line. The Coop test and load
    /// test sources are the TAOM list's words; anything else counts as Coodsz notes.</summary>
    public static string SourceLine(CompatEntry entry)
    {
        var source = entry.Source.Trim().ToLowerInvariant() switch
        {
            CompatSource.CoodszCoop => "Tested in Coop by Coodsz",
            CompatSource.ServerLoads => "Passed the automatic server load test",
            _ => "Coodsz notes"
        };
        return entry.Checked.Length > 0 ? source + ", checked " + entry.Checked + "." : source + ".";
    }

    /// <summary>The hover text of a badge: the note, then where it comes from and when it was checked.
    /// translate turns the source line into the tool language (the note is list data and stays English).</summary>
    public static string Hover(CompatEntry entry, Func<string, string>? translate = null)
    {
        var from = (translate ?? (text => text))(SourceLine(entry));
        return entry.Note.Length > 0 ? entry.Note + "\n\n" + from : from;
    }

    /// <summary>The warning to show before a known broken mod is added.</summary>
    public static string BrokenWarning(string label, CompatEntry entry) =>
        label + " is known to be broken with Realm of Thrones. " + (entry.Note.Length > 0 ? entry.Note + " " : "") + "Leave it off unless you are testing it.";
}
