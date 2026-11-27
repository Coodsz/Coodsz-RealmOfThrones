using System.Globalization;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace BannerlordCoopLauncher.Core.Modules;

/// <summary>One mod that passed a server load test: the server reached serving with it.</summary>
public sealed record ServerLoadPass(string Id, string Name, string WorkshopId, string Version, string Date);

/// <summary>
/// Writes server load test passes into a copy of the community mod list (compat-list/mods.json),
/// for Coodsz to check and publish. A passed mod gets "source": "server-loads", the test date in
/// "checked", the tested mod version in "version" and a note that joining was not tested. Its
/// status and the community counts stay as they are: a mod that loads on the server can still
/// break joining or a campaign, so only people's reports (or Coodsz by hand) make it Works.
/// Entries with a hand kept verdict ("coop") are left alone. A mod the list does not have is added
/// as Unknown. The text keeps the comment block on top and is written the way the tally workflow
/// writes it (.github/scripts/tally-mod-compat.mjs): one entry per line, sorted by name.
/// </summary>
public static class CompatListServerLoads
{
    public const string Source = "server-loads";

    private const string EmbeddedName = "Coodsz.CompatList.mods.json";

    // The tally workflow's key order; other keys follow in the order the entry has them.
    private static readonly string[] KeyOrder = ["id", "name", "status", "coop", "note", "serverHideAssets", "workshop", "nexus", "category", "works", "broken", "idGuess"];

    private static readonly JsonSerializerOptions ValueOptions = new() { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    /// <summary>The list text to start from: the copy the tools downloaded last, else the built in one.</summary>
    public static string CurrentText(string? cachePath = null)
    {
        cachePath ??= CommunityCompat.CachePath;
        try
        {
            if (File.Exists(cachePath))
            {
                var text = File.ReadAllText(cachePath);
                if (ModCompatDatabase.Parse(text) is { Count: > 0 })
                    return text;
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }

        using var stream = typeof(ModCompatDatabase).Assembly.GetManifestResourceStream(EmbeddedName);
        if (stream == null)
            return "{\n  \"mods\": [\n  ]\n}\n";
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    /// <summary>The sentence a pass adds to the note.</summary>
    public static string NoteFor(ServerLoadPass pass) =>
        "Passed the automatic server load test on " + pass.Date + (pass.Version.Length > 0 ? " with version " + pass.Version : "") + ". Joining was not tested.";

    /// <summary>The list text with the passes written in. The newest pass of a mod counts.</summary>
    public static string Merge(string listText, IEnumerable<ServerLoadPass> passes)
    {
        var head = Regex.Match(listText, "^([\\s\\S]*?\"mods\"\\s*:\\s*\\[)", RegexOptions.Multiline);
        if (!head.Success)
            throw new InvalidOperationException("The mod list has no \"mods\" list.");
        var root = JsonNode.Parse(listText, documentOptions: new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
        var mods = (root as JsonObject)?["mods"] as JsonArray ?? throw new InvalidOperationException("The mod list has no \"mods\" list.");
        var entries = mods.OfType<JsonObject>().Select(entry => (JsonObject)entry.DeepClone()).ToList();

        var newest = passes.Where(pass => pass.Id.Trim().Length > 0)
            .GroupBy(pass => ModCompatDatabase.Normalize(pass.Id))
            .Select(group => group.Last());
        foreach (var pass in newest)
        {
            var entry = Find(entries, pass);
            if (entry == null)
            {
                entry = new JsonObject
                {
                    ["id"] = pass.Id.Trim(),
                    ["name"] = pass.Name.Trim().Length > 0 ? pass.Name.Trim() : pass.Id.Trim(),
                    ["status"] = "Unknown",
                    ["note"] = NoteFor(pass)
                };
                if (IsWorkshopId(pass.WorkshopId))
                    entry["workshop"] = pass.WorkshopId;
                entry["works"] = 0;
                entry["broken"] = 0;
                Mark(entry, pass);
                entries.Add(entry);
                continue;
            }

            if (entry["coop"] is JsonValue)
                continue;
            if (entry["idGuess"] is JsonValue guess && guess.TryGetValue<bool>(out var guessed) && guessed)
            {
                // The real id is known now (the same fix a report with the id makes).
                entry["id"] = pass.Id.Trim();
                entry.Remove("idGuess");
            }

            if (entry["workshop"] is null && IsWorkshopId(pass.WorkshopId))
                entry["workshop"] = pass.WorkshopId;
            var oldNote = Regex.Replace(Text(entry, "note"), @"\s*Passed the automatic server load test on .*?Joining was not tested\.", "").Trim();
            entry["note"] = oldNote.Length > 0 ? oldNote + " " + NoteFor(pass) : NoteFor(pass);
            Mark(entry, pass);
        }

        var compare = CultureInfo.InvariantCulture.CompareInfo;
        entries.Sort((a, b) =>
        {
            var byName = compare.Compare(Label(a), Label(b), CompareOptions.IgnoreCase | CompareOptions.IgnoreNonSpace);
            return byName != 0 ? byName : string.CompareOrdinal(Text(a, "id"), Text(b, "id"));
        });

        var newline = listText.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
        var text = new StringBuilder(head.Groups[1].Value).Append(newline);
        text.Append(string.Join("," + newline, entries.Select(entry => "    " + Line(entry))));
        text.Append(newline).Append("  ]").Append(newline).Append('}').Append(newline);
        return text.ToString();
    }

    private static void Mark(JsonObject entry, ServerLoadPass pass)
    {
        entry["source"] = Source;
        entry["checked"] = pass.Date;
        if (pass.Version.Trim().Length > 0)
            entry["version"] = pass.Version.Trim();
        else
            entry.Remove("version");
    }

    private static JsonObject? Find(List<JsonObject> entries, ServerLoadPass pass)
    {
        var id = ModCompatDatabase.Normalize(pass.Id);
        var byId = entries.FirstOrDefault(entry => ModCompatDatabase.Normalize(Text(entry, "id")) == id);
        if (byId != null)
            return byId;
        var name = ModCompatDatabase.Normalize(pass.Name);
        if (name.Length > 0)
        {
            var byName = entries.FirstOrDefault(entry => entry["idGuess"] is JsonValue guess && guess.TryGetValue<bool>(out var guessed) && guessed
                && (ModCompatDatabase.Normalize(Text(entry, "name")) == name || ModCompatDatabase.Normalize(Text(entry, "id")) == name));
            if (byName != null)
                return byName;
        }

        return IsWorkshopId(pass.WorkshopId)
            ? entries.FirstOrDefault(entry => entry["idGuess"] is JsonValue && Text(entry, "workshop") == pass.WorkshopId)
            : null;
    }

    private static string Line(JsonObject entry)
    {
        var keys = KeyOrder.Where(entry.ContainsKey).Concat(entry.Select(pair => pair.Key).Where(key => !KeyOrder.Contains(key)));
        var parts = keys.Where(key => entry[key] is not null)
            .Select(key => JsonSerializer.Serialize(key, ValueOptions) + ": " + entry[key]!.ToJsonString(ValueOptions));
        return "{ " + string.Join(", ", parts) + " }";
    }

    private static string Label(JsonObject entry) => Text(entry, "name") is { Length: > 0 } name ? name : Text(entry, "id");

    private static string Text(JsonObject entry, string key) =>
        entry[key] is JsonValue value && value.TryGetValue<string>(out var text) ? text : "";

    private static bool IsWorkshopId(string? id) => id is { Length: > 0 and <= 20 } && id.All(char.IsAsciiDigit);
}
