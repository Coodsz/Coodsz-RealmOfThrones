using System.Text.Json;
using System.Text.Json.Serialization;

namespace TaomCoop.Core;

/// <summary>What is known about a mod with TAOM and Coop.</summary>
public enum CompatTag
{
    Unknown,
    /// <summary>Tested safe.</summary>
    Safe,
    /// <summary>Works, but the host and every player need the same version.</summary>
    Everyone,
    /// <summary>Can work; it changes campaign logic or battles, or is not tested with TAOM yet.</summary>
    Risky,
    /// <summary>Known broken with TAOM.</summary>
    Broken,
    /// <summary>Reported working in TAOM (singleplayer), not tested in Coop.</summary>
    Reported,
    /// <summary>Passed the automatic server load test of the TAOM server tool. Joining is not tested.</summary>
    Loads
}

/// <summary>Where what the list says about a mod comes from (the "source" field).</summary>
public static class CompatSource
{
    /// <summary>Tested in Coop by Coodsz: a TAOM server and a client joining it.</summary>
    public const string CoodszCoop = "coodsz-coop";
    /// <summary>Passed the automatic server load test (TAOM core plus that one mod reached serving).</summary>
    public const string ServerLoads = "server-loads";
    /// <summary>Reported in TAOM singleplayer on the TAOM Discord or a TAOM community list, not tested in Coop.</summary>
    public const string TaomDiscord = "taom-discord";
    /// <summary>Said by the TAOM developer Theboyys.</summary>
    public const string TaomDev = "taom-dev";
    /// <summary>Coodsz's own notes (known from Coop and Friends, not tested with TAOM).</summary>
    public const string CoodszNotes = "coodsz-notes";

    /// <summary>The plain label for a source. An unknown or empty source counts as Coodsz notes.</summary>
    public static string Label(string? source) => (source ?? "").Trim().ToLowerInvariant() switch
    {
        CoodszCoop => "Tested in Coop by Coodsz",
        ServerLoads => "Passed the automatic server load test",
        TaomDiscord => "Reported on the TAOM Discord in TAOM singleplayer, not tested in Coop",
        TaomDev => "Said by TAOM developer Theboyys",
        _ => "Coodsz notes"
    };
}

public sealed class CompatEntry
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string WorkshopId { get; set; } = "";
    // Nexus Mods page id, for mods that are not on the Workshop.
    public string NexusId { get; set; } = "";
    public string Tag { get; set; } = "";
    public string Class { get; set; } = "";
    public bool ClientOnlyOnServer { get; set; }
    public bool HasServerBuild { get; set; }
    public string Note { get; set; } = "";
    // Where the entry comes from, see CompatSource. Schema 2; older lists have none.
    public string Source { get; set; } = "";
    // The day it was checked, YYYY-MM-DD. Schema 2; older lists have none.
    public string Checked { get; set; } = "";

    [JsonIgnore]
    public CompatTag Kind => Tag.Trim().ToLowerInvariant() switch
    {
        "safe" => CompatTag.Safe,
        "everyone" => CompatTag.Everyone,
        "risky" => CompatTag.Risky,
        "broken" => CompatTag.Broken,
        "reported" => CompatTag.Reported,
        "loads" => CompatTag.Loads,
        _ => CompatTag.Unknown
    };

    [JsonIgnore]
    public string Label => Name.Length > 0 ? Name : Id.Length > 0 ? Id : WorkshopId;
}

/// <summary>
/// The TAOM compatibility list: taom-mod-compat.json in the public Coodsz/Coodsz-TAOM repository
/// (compat/taom-mod-compat.json on its main branch, see docs/taom-modlist-publish.md), with the old
/// place next to the Coop and Friends profile (https://bannerlord.eu/download/) as the fallback. A
/// copy ships inside TaomCoop.Core (Compat\taom-mod-compat.json) and is used when the online file
/// cannot be read or is older; the last good download is kept next to the tool.
///
/// Format: { "kind": "CoodszTaomCompat", "schemaVersion": 2, "taom", "game", "coop", "updated",
///   "mods": [ { "id", "name", "workshopId", "nexusId", "tag": "safe|everyone|risky|broken|reported|loads",
///   "class": "A|B|C|D", "clientOnlyOnServer", "hasServerBuild", "source", "checked", "note" } ] }.
/// Schema 2 added source, checked and the reported and loads tags. Older tools read a schema 2
/// file too: they skip the fields they do not know and show no badge for a tag they do not know.
/// The copy in the Coodsz-TAOM repository also carries player report counts written by its tally
/// (works, broken, clientOnly, lastReport on an entry, and a "community" array of reported mods
/// that are not on the list); the tools skip them too, so they stay schema 2.
/// A mod is matched by module id, then by Workshop id, then by its name.
/// </summary>
public sealed class TaomCompatList
{
    public const string Kind = "CoodszTaomCompat";
    public const string FileName = "taom-mod-compat.json";
    public const int CurrentSchema = 2;
    public const string Repository = "Coodsz/Coodsz-TAOM";
    public const string RepositoryPath = "compat/" + FileName;
    public const string OnlineUrl = "https://raw.githubusercontent.com/" + Repository + "/main/" + RepositoryPath;
    public const string FallbackUrl = "https://bannerlord.eu/download/" + FileName;
    /// <summary>The readable page of the whole list with the player counts (MODS.md, written by the tally there).</summary>
    public const string ModsPageUrl = "https://github.com/" + Repository + "/blob/main/MODS.md";

    [JsonPropertyName("kind")]
    public string FileKind { get; set; } = Kind;
    public int SchemaVersion { get; set; } = CurrentSchema;
    public string Taom { get; set; } = "";
    public string Game { get; set; } = "";
    public string Coop { get; set; } = "";
    public string Updated { get; set; } = "";
    public List<CompatEntry> Mods { get; set; } = new();

    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };

    public static TaomCompatList Parse(string json)
    {
        TaomCompatList? list;
        try
        {
            list = JsonSerializer.Deserialize<TaomCompatList>(json, Options);
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException("The TAOM mod list is not valid. " + ex.Message);
        }

        if (list == null || !Kind.Equals(list.FileKind, StringComparison.Ordinal))
            throw new InvalidOperationException("That file is not a TAOM mod list.");
        if (list.SchemaVersion < 1)
            throw new InvalidOperationException("The TAOM mod list is not valid.");
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

        list.Updated ??= "";
        return list;
    }

    public static TaomCompatList BuiltIn()
    {
        using var stream = typeof(TaomCompatList).Assembly.GetManifestResourceStream("TaomCoop.Compat." + FileName)
            ?? throw new InvalidOperationException("The built in TAOM mod list is missing.");
        using var reader = new StreamReader(stream);
        return Parse(reader.ReadToEnd());
    }

    /// <summary>True when this list says it is older than the other one (both have an updated date).
    /// A list without a date is never called older.</summary>
    public bool OlderThan(TaomCompatList other) =>
        Updated.Length > 0 && other.Updated.Length > 0 && string.CompareOrdinal(Updated.Trim(), other.Updated.Trim()) < 0;

    /// <summary>The saved download next to the tool when it is valid and not older than the built in
    /// list (a tool update brings a newer list), else the built in list.</summary>
    public static TaomCompatList Load(string cachePath)
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

    /// <summary>Downloads the online list: the Coodsz-TAOM repository first, then the bannerlord.eu
    /// copy (or only the given url). A valid list that is not older than the built in one is saved to
    /// cachePath and returned; on any problem null is returned and the list in use stays.</summary>
    public static async Task<TaomCompatList?> DownloadAsync(HttpClient http, string cachePath, CancellationToken cancellationToken, string? url = null)
    {
        var builtIn = BuiltIn();
        foreach (var address in url == null ? new[] { OnlineUrl, FallbackUrl } : new[] { url })
        {
            try
            {
                var json = await http.GetStringAsync(address, cancellationToken);
                var list = Parse(json);
                if (list.OlderThan(builtIn))
                    continue;
                File.WriteAllText(cachePath, json);
                return list;
            }
            catch (Exception ex) when (ex is HttpRequestException or InvalidOperationException or IOException or UnauthorizedAccessException)
            {
            }
            catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                // A timeout; try the next place.
            }
            catch (TaskCanceledException)
            {
                return null;
            }
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

    /// <summary>The short badge text for a tag, or null when nothing is known.</summary>
    /// <summary>True for the tags that say a mod works in Coop (Tested safe, Everyone needs it): the Works group of the
    /// side Mods card. Reported and Loads on the server are not tested with players, so they stay out.</summary>
    public static bool Works(CompatTag? tag) => tag is CompatTag.Safe or CompatTag.Everyone;

    public static string? BadgeText(CompatTag tag) => tag switch
    {
        CompatTag.Safe => "Tested safe",
        CompatTag.Everyone => "Everyone needs it",
        CompatTag.Risky => "Risky",
        CompatTag.Broken => "Known broken",
        CompatTag.Reported => "Works in TAOM, not tested in Coop",
        CompatTag.Loads => "Loads on the server",
        _ => null
    };

    /// <summary>Green, yellow, red, or blue for what is reported or only load tested.</summary>
    public static string BadgeColor(CompatTag tag) => tag switch
    {
        CompatTag.Safe => "green",
        CompatTag.Broken => "red",
        CompatTag.Reported or CompatTag.Loads => "blue",
        CompatTag.Unknown => "none",
        _ => "yellow"
    };

    /// <summary>Where the entry comes from and when it was checked, as one line.</summary>
    public static string SourceLine(CompatEntry entry)
    {
        var source = CompatSource.Label(entry.Source);
        return entry.Checked.Length > 0 ? source + ", checked " + entry.Checked + "." : source + ".";
    }

    /// <summary>The hover text of a badge: the note, then where it comes from and when it was checked.
    /// translate turns the source line into the tool language (the note is list data and stays English).</summary>
    public static string Hover(CompatEntry entry, Func<string, string>? translate = null)
    {
        var from = (translate ?? (text => text))(SourceLine(entry));
        return entry.Note.Length > 0 ? entry.Note + "\n\n" + from : from;
    }

    /// <summary>The warning to show before a known broken mod is added or launched.</summary>
    public static string BrokenWarning(string label, CompatEntry entry) =>
        label + " is known to be broken with TAOM. " + (entry.Note.Length > 0 ? entry.Note + " " : "") + "Leave it off unless you are testing it.";
}
