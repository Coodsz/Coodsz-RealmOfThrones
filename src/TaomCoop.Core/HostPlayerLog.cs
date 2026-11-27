using System.Text.Json;
using System.Text.RegularExpressions;

namespace TaomCoop.Core;

public static class PlayerBanList
{
    public static List<string> Read(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return [];

        try
        {
            var names = JsonSerializer.Deserialize<List<string>>(json) ?? [];
            return Distinct(names);
        }
        catch (JsonException)
        {
            return [];
        }
    }

    public static string Write(IEnumerable<string> names) =>
        JsonSerializer.Serialize(Distinct(names), new JsonSerializerOptions { WriteIndented = true });

    public static bool IsBanned(IEnumerable<string> names, string player)
    {
        var trimmed = player.Trim();
        return trimmed.Length > 0 && names.Any(name => name.Equals(trimmed, StringComparison.OrdinalIgnoreCase));
    }

    private static List<string> Distinct(IEnumerable<string> names)
    {
        var list = new List<string>();
        foreach (var name in names)
        {
            var trimmed = name.Trim();
            if (trimmed.Length == 0)
                continue;
            if (list.Any(item => item.Equals(trimmed, StringComparison.OrdinalIgnoreCase)))
                continue;
            list.Add(trimmed);
        }

        return list;
    }
}

public sealed record HostPlayer(int Id, string Name, string State, string Address);

public static class HostPlayerLog
{
    /// <summary>
    /// True while <paramref name="player"/> is still in the list with the same id and name, so a queued kick never hits
    /// whoever got that id after them.
    /// </summary>
    public static bool StillOn(IEnumerable<HostPlayer> online, HostPlayer player) =>
        online.Any(other => other.Id == player.Id && other.Name.Equals(player.Name, StringComparison.OrdinalIgnoreCase));

    // The server writes the players event at the start of its own line, after the log's time stamp (or bare on stdout):
    // "16:15:25.478  @DS@{"ev":"players",...}". The engine sometimes glues it onto the end of one of its own lines
    // (DsEventParser reads those too). Only the engine lines really seen in server logs count as glue, and only when the
    // event ends the line: a WARN line ("16:15:25.478   WARN@DS@{...}"), the 0Harmony pdb line from the TAOM test server
    // ("02:28:25.072  #0Harmony.pdb@{5D97B133...} @DS@{...}") and an RGL warning line. Anything else, a chat line or a
    // player name with the same text, must start its line, so it can't set or blank the list.
    private static readonly Regex EngineGlue = new(
        "^(\\s*WARN|#\\d*\\w+\\.pdb@\\{[0-9A-Fa-f]+\\}\\s*|rgl_post_warning_line: RGL WARNING - [^\"'@]*)$",
        RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
    private const string EventMarker = "@DS@{\"ev\":\"players\"";
    private static readonly Regex Stamp = new("^\\d{1,2}:\\d{2}:\\d{2}(\\.\\d+)?\\s+", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));

    /// <summary>Where the players event starts in <paramref name="line"/> (its "{"), or -1 when the line holds none the
    /// server wrote.</summary>
    internal static int EventStart(string line)
    {
        var at = line.IndexOf(EventMarker, StringComparison.Ordinal);
        if (at < 0)
            return -1;
        var before = line[..at];
        var stamp = Stamp.Match(before);
        if (stamp.Success)
            before = before[stamp.Length..];
        if (before.Length == 0)
            return at + 4;
        if (!EngineGlue.IsMatch(before) || !line.TrimEnd().EndsWith('}'))
            return -1;
        return at + 4;
    }

    /// <summary>
    /// The players of the last players event in <paramref name="text"/>. An event that does not read, or has no list,
    /// keeps <paramref name="last"/>, the last good list, instead of blanking it.
    /// </summary>
    public static IReadOnlyList<HostPlayer> Read(string text, IReadOnlyList<HostPlayer>? last = null)
    {
        var events = new List<(string Line, int Start)>();
        foreach (var raw in text.Replace("\r\n", "\n").Split('\n'))
        {
            if (!raw.Contains("\"ev\":\"players\"", StringComparison.Ordinal))
                continue;
            var at = EventStart(raw);
            if (at >= 0)
                events.Add((raw, at));
        }

        if (events.Count == 0)
            return last ?? [];

        // The newest event that reads wins. One that doesn't (cut in half, no list, text that can't be read) keeps the last
        // good list; with none yet (or an empty one), the event before it is tried, so a fresh window still finds the players.
        for (var i = events.Count - 1; i >= 0; i--)
        {
            if (TryPlayers(events[i].Line[events[i].Start..]) is { } players)
                return players;
            if (last is { Count: > 0 })
                return last;
        }

        return last ?? [];
    }

    private static List<HostPlayer>? TryPlayers(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("list", out var list) || list.ValueKind != JsonValueKind.Array)
                return null;

            var players = new List<HostPlayer>();
            foreach (var item in list.EnumerateArray())
            {
                var id = item.TryGetProperty("id", out var idValue) && idValue.TryGetInt32(out var parsed) ? parsed : -1;
                var name = item.TryGetProperty("name", out var nameValue) ? nameValue.GetString() ?? "" : "";
                var state = item.TryGetProperty("state", out var stateValue) ? stateValue.GetString() ?? "" : "";
                var address = item.TryGetProperty("addr", out var addressValue) ? addressValue.GetString() ?? "" : "";
                if (name.Length > 0)
                    players.Add(new HostPlayer(id, name, state, address));
            }

            return players;
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException)
        {
            return null;
        }
    }
}
