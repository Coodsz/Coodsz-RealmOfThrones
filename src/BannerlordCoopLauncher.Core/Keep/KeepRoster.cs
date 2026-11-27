// The save companion reader follows CoopKeep (https://github.com/Aderimo/bannerlord-coopkeep),
// src/CoopKeep.Core/Saves/CoopSession.cs and SaveRepository.ReadPlayers.
//
// Copyright (c) 2026 aderimo
//
// Permission is hereby granted, free of charge, to any person obtaining a copy
// of this software and associated documentation files (the "Software"), to deal
// in the Software without restriction, including without limitation the rights
// to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
// copies of the Software, and to permit persons to whom the Software is
// furnished to do so, subject to the following conditions:
//
// The above copyright notice and this permission notice shall be included in all
// copies or substantial portions of the Software.
//
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
// IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
// FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
// AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
// LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
// OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
// SOFTWARE.

using System.Text.Json;

namespace BannerlordCoopLauncher.Core.Keep;

/// <summary>A player saved in the campaign's .json companion: SteamID64, the hero ids and the
/// Steam name Coop saw last (<c>PlatformName</c>).</summary>
public sealed record SessionPlayer(string ControllerId, string HeroId, string ClanId, string MobilePartyId, string PlatformName = "");

/// <summary>A connected player from the host's live list.</summary>
public sealed record LivePlayerInfo(string Name, string SteamId);

/// <summary>A row of the Cheats page Who? panel and the player profile header.</summary>
/// <remarks><see cref="Name"/> is the hero's name when it is known, else the Steam name, else the hero id.</remarks>
public sealed record KeepPlayer(string Name, string HeroId, string SteamId, bool Online, HeroSummary? Hero, string SteamName = "")
{
    public string Initial => Name.Length > 0 ? char.ToUpperInvariant(Name[0]).ToString() : "?";

    /// <summary>
    /// The small line under the name: the Steam name when it is not the name already, and the hero id
    /// ("RendeGar · Player2863"). Streamer mode leaves the Steam name out.
    /// </summary>
    public string Detail(bool streamer)
    {
        var parts = new List<string>();
        if (!streamer && SteamName.Length > 0 && !string.Equals(SteamName, Name, StringComparison.OrdinalIgnoreCase))
            parts.Add(SteamName);
        if (HeroId.Length > 0 && !string.Equals(HeroId, Name, StringComparison.Ordinal))
            parts.Add(HeroId);
        return string.Join(" · ", parts);
    }
}

/// <summary>
/// Puts together who can be picked on the Cheats page: the heroes the server says look like
/// players, the players saved in the campaign's .json (which carries the SteamID64), and the
/// players connected now (matched by hero name).
/// </summary>
public static class KeepRoster
{
    private static readonly JsonSerializerOptions Options = new() { PropertyNameCaseInsensitive = true };

    private sealed class Session
    {
        public List<SessionRow> Players { get; set; } = new();
    }

    private sealed class SessionRow
    {
        public string? ControllerId { get; set; }
        public string? HeroId { get; set; }
        public string? ClanId { get; set; }
        public string? MobilePartyId { get; set; }
        public string? PlatformName { get; set; }
    }

    /// <summary>The players of <c>&lt;save&gt;.json</c> next to the .sav; none when it is missing or unreadable.</summary>
    public static IReadOnlyList<SessionPlayer> ReadSession(string companionPath)
    {
        try
        {
            if (!File.Exists(companionPath))
                return Array.Empty<SessionPlayer>();
            using var stream = new FileStream(companionPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            var session = JsonSerializer.Deserialize<Session>(stream, Options);
            return session?.Players
                .Where(p => !string.IsNullOrWhiteSpace(p.HeroId))
                .Select(p => new SessionPlayer(p.ControllerId ?? "", p.HeroId!, p.ClanId ?? "", p.MobilePartyId ?? "", (p.PlatformName ?? "").Trim()))
                .ToList() ?? (IReadOnlyList<SessionPlayer>)Array.Empty<SessionPlayer>();
        }
        catch (Exception e) when (e is IOException or JsonException or UnauthorizedAccessException)
        {
            return Array.Empty<SessionPlayer>();
        }
    }

    /// <summary>Coop's registry id to the game's id: <c>Hero_Player2863</c> is <c>Player2863</c>.</summary>
    public static string GameId(string heroId) =>
        heroId.StartsWith("Hero_", StringComparison.Ordinal) && heroId.Length > 5 ? heroId[5..] : heroId;

    /// <summary>
    /// One row per hero: online players first, then by name. A connected player whose hero is not
    /// known yet stays in the list without a hero id, so the page can say why nothing can be done.
    /// A row is named after the hero (from the server, or <paramref name="knownNames"/> remembered
    /// from an earlier run while the server is stopped), then the Steam name in the save's .json,
    /// and only then the hero id, so the list does not show just "Player2863" when a name is known.
    /// </summary>
    public static IReadOnlyList<KeepPlayer> Merge(IEnumerable<LivePlayerInfo> live, IEnumerable<SessionPlayer> session, IEnumerable<HeroSummary> heroes,
        IReadOnlyDictionary<string, string>? knownNames = null)
    {
        var heroList = heroes.GroupBy(h => h.Id, StringComparer.Ordinal).Select(g => g.First()).ToList();
        var byId = heroList.ToDictionary(h => h.Id, StringComparer.Ordinal);
        var rows = new Dictionary<string, KeepPlayer>(StringComparer.Ordinal);
        var savedByHero = new Dictionary<string, SessionPlayer>(StringComparer.Ordinal);
        foreach (var player in session)
            savedByHero[GameId(player.HeroId)] = player;

        string NameFor(string id, string steamName) =>
            byId.TryGetValue(id, out var h) && !string.IsNullOrWhiteSpace(h.Name) ? h.Name
            : knownNames != null && knownNames.TryGetValue(id, out var known) && !string.IsNullOrWhiteSpace(known) ? known
            : steamName.Length > 0 ? steamName
            : id;

        foreach (var hero in heroList.Where(h => h.IsPlayer || savedByHero.ContainsKey(h.Id)))
        {
            var saved = savedByHero.GetValueOrDefault(hero.Id);
            rows[hero.Id] = new KeepPlayer(NameFor(hero.Id, saved?.PlatformName ?? ""), hero.Id, saved?.ControllerId ?? "", false, hero, saved?.PlatformName ?? "");
        }

        foreach (var player in session)
        {
            var id = GameId(player.HeroId);
            if (!rows.ContainsKey(id))
                rows[id] = new KeepPlayer(NameFor(id, player.PlatformName), id, player.ControllerId, false, byId.GetValueOrDefault(id), player.PlatformName);
        }

        var loose = new List<KeepPlayer>();
        foreach (var player in live)
        {
            if (string.IsNullOrWhiteSpace(player.Name))
                continue;
            var match = rows.Values.FirstOrDefault(r => string.Equals(r.Name, player.Name, StringComparison.OrdinalIgnoreCase))
                        ?? rows.Values.FirstOrDefault(r => r.SteamName.Length > 0 && string.Equals(r.SteamName, player.Name, StringComparison.OrdinalIgnoreCase))
                        ?? (player.SteamId.Length > 0 ? rows.Values.FirstOrDefault(r => r.SteamId == player.SteamId) : null);
            if (match == null)
            {
                var hero = heroList.FirstOrDefault(h => string.Equals(h.Name, player.Name, StringComparison.OrdinalIgnoreCase));
                if (hero != null)
                    rows[hero.Id] = new KeepPlayer(hero.Name, hero.Id, player.SteamId, true, hero);
                else if (loose.All(l => !string.Equals(l.Name, player.Name, StringComparison.OrdinalIgnoreCase)))
                    loose.Add(new KeepPlayer(player.Name, "", player.SteamId, true, null));
                continue;
            }

            rows[match.HeroId] = match with { Online = true, SteamId = match.SteamId.Length > 0 ? match.SteamId : player.SteamId };
        }

        return rows.Values.Concat(loose)
            .OrderByDescending(r => r.Online)
            .ThenBy(r => r.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    /// <summary>"Steam ••••1234", or "" when there is no SteamID64.</summary>
    public static string MaskedSteamId(string? steamId) =>
        string.IsNullOrWhiteSpace(steamId) || steamId.Length < 4 ? "" : "Steam ••••" + steamId[^4..];

    /// <summary>The live players whose hero is not known yet; their names are looked up on the server one by one.</summary>
    public static IReadOnlyList<string> Unmatched(IEnumerable<KeepPlayer> rows) =>
        rows.Where(r => r.HeroId.Length == 0).Select(r => r.Name).ToList();
}
