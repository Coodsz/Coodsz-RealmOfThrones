using System.Text.Json;

namespace BannerlordCoopLauncher.Core.Server;

/// <summary>One connected player in server-data\live-players.json (written by Launcher Cheats).</summary>
public sealed record LiveRosterEntry(string ControllerId, string HeroName);

/// <summary>
/// Notices two players who joined with one Steam ID. Non Steam copies of the game share a fake
/// Steam ID, and Coop gives every connection with that ID the same character.
/// </summary>
/// <remarks>
/// The server prints no Steam ID when someone joins. What it does show is this: the @DS@ players
/// event lists every connection with the hero the server gave it, and Coop finds that hero by the
/// Steam ID, so two connections with one ID show the same hero. Two different players can still
/// name their heroes the same, so live-players.json has the last word: it lists one row per
/// connected Steam ID, and only when that hero name has one row there is it one shared ID.
/// Both connections must be on the map, past the 60 second timeout of a player who crashed and
/// came back. Nothing is ever blocked; the host only gets one line per pair per server run.
/// </remarks>
public sealed class SharedSteamIdWatch
{
    private readonly HashSet<string> _warned = new(StringComparer.Ordinal);

    /// <summary>A new server run: every pair may be noted again.</summary>
    public void Reset() => _warned.Clear();

    /// <summary>The lines to show for this player list, only for pairs not noted yet.</summary>
    public IReadOnlyList<string> Check(IEnumerable<ConnectedPlayer> connections, IReadOnlyList<LiveRosterEntry>? roster)
    {
        var lines = new List<string>();
        if (roster == null || roster.Count == 0)
            return lines;

        foreach (var group in SharedHeroes(connections))
        {
            var ids = roster
                .Where(entry => entry.HeroName.Trim().Equals(group.Key, StringComparison.OrdinalIgnoreCase))
                .Select(entry => entry.ControllerId.Trim())
                .Where(id => id.Length > 0)
                .Distinct(StringComparer.Ordinal)
                .ToList();
            if (ids.Count != 1)
                continue;

            var peers = group.Select(player => player.Id).Distinct().OrderBy(id => id).ToList();
            var isNew = false;
            for (var i = 0; i < peers.Count; i++)
                for (var j = i + 1; j < peers.Count; j++)
                    isNew |= _warned.Add(ids[0] + "\n" + peers[i] + "\n" + peers[j]);
            if (isNew)
                lines.Add(Line(group.Key, peers.Count));
        }

        return lines;
    }

    /// <summary>True when two connections on the map have the same hero, the cheap check before reading files.</summary>
    public static bool HasSharedHero(IEnumerable<ConnectedPlayer> connections) => SharedHeroes(connections).Any();

    private static IEnumerable<IGrouping<string, ConnectedPlayer>> SharedHeroes(IEnumerable<ConnectedPlayer> connections) =>
        connections
            .Where(player => !player.IsJoining && player.IsOnMap)
            .GroupBy(player => player.Name.Trim(), StringComparer.OrdinalIgnoreCase)
            .Where(group => group.Select(player => player.Id).Distinct().Count() > 1);

    public static string Line(string heroName, int count) =>
        (count == 2
            ? "Two players joined with the same Steam ID, both as "
            : count.ToString(System.Globalization.CultureInfo.InvariantCulture) + " players joined with the same Steam ID, all as ")
        + heroName
        + ". This happens with non Steam copies of the game, and they will share one character.";

    /// <summary>
    /// The rows of live-players.json: <c>{"UpdatedUtc":"...","Players":[{"ControllerId":"...","HeroName":"..."}]}</c>.
    /// A missing or broken file gives an empty list.
    /// </summary>
    public static IReadOnlyList<LiveRosterEntry> ReadRoster(string? json)
    {
        var rows = new List<LiveRosterEntry>();
        if (string.IsNullOrWhiteSpace(json))
            return rows;
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind != JsonValueKind.Object)
                return rows;
            JsonElement players = default;
            foreach (var property in doc.RootElement.EnumerateObject())
                if (property.Name.Equals("Players", StringComparison.OrdinalIgnoreCase))
                    players = property.Value;
            if (players.ValueKind != JsonValueKind.Array)
                return rows;
            foreach (var item in players.EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.Object)
                    continue;
                var id = Text(item, "ControllerId");
                var name = Text(item, "HeroName");
                if (id.Length > 0 && name.Length > 0)
                    rows.Add(new LiveRosterEntry(id, name));
            }
        }
        catch (JsonException)
        {
        }

        return rows;
    }

    /// <summary>The newest live-players.json in these server-data folders, read, or an empty list.</summary>
    public static IReadOnlyList<LiveRosterEntry> ReadRosterFile(IEnumerable<string> serverDataRoots, DateTime? notBeforeUtc = null)
    {
        try
        {
            var path = serverDataRoots
                .Select(root => Path.Combine(root, "live-players.json"))
                .Where(File.Exists)
                .OrderByDescending(File.GetLastWriteTimeUtc)
                .FirstOrDefault();
            // A file older than this server start belongs to an earlier run.
            if (path == null || (notBeforeUtc is DateTime start && File.GetLastWriteTimeUtc(path) < start.AddSeconds(-5)))
                return Array.Empty<LiveRosterEntry>();
            return ReadRoster(File.ReadAllText(path));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return Array.Empty<LiveRosterEntry>();
        }
    }

    private static string Text(JsonElement item, string name)
    {
        foreach (var property in item.EnumerateObject())
            if (property.Name.Equals(name, StringComparison.OrdinalIgnoreCase) && property.Value.ValueKind == JsonValueKind.String)
                return property.Value.GetString()?.Trim() ?? "";
        return "";
    }
}
