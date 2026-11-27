// The live map follows CoopKeep's map page (https://github.com/Aderimo/bannerlord-coopkeep,
// src/CoopKeep.App/ViewModels/MainViewModel.Map.cs and MainViewModel.MapLive.cs): settlements
// coloured by their kingdom, players highlighted, lords and armies, names that come in as you zoom,
// what a party is doing, kingdom names over their land and a refresh every few seconds. CoopKeep
// draws over a map picture it downloads; Coodsz Host downloads nothing and draws the land from the
// settlements themselves (soft territory around each one), so this part is new.
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

using System.Globalization;

namespace BannerlordCoopLauncher.Core.Keep;

/// <summary>A kingdom of <c>coodsz.map</c> with its two banner colours (ARGB).</summary>
public sealed record MapKingdom(string Id, string Name, uint Color, uint Color2);

/// <summary>A town, castle or village of <c>coodsz.map</c> with its map position (the game's own coordinates).</summary>
public sealed record MapSettlement(string Id, string Name, string Kind, double X, double Y, string OwnerClanId, string OwnerClanName,
    string KingdomId, uint Color, string BoundId, bool UnderSiege)
{
    public bool IsTown => Kind == "town";
    public bool IsCastle => Kind == "castle";
    public bool IsVillage => Kind == "village";
}

/// <summary>A player or lord party of <c>coodsz.map</c>. <see cref="Kind"/> is player or lord.</summary>
public sealed record MapParty(string PartyId, string HeroId, string Name, string ClanId, string ClanName, string KingdomId, string Kind,
    double X, double Y, int Troops, int Wounded, string SettlementId, string ArmyLeaderId, int ArmyTroops, bool InBattle, bool Besieging,
    string TargetId, uint Color)
{
    public bool IsPlayer => Kind == "player";

    /// <summary>Leads an army (the troops of the whole army are in <see cref="ArmyTroops"/>).</summary>
    public bool LeadsArmy => ArmyTroops > 0 && (ArmyLeaderId.Length == 0 || ArmyLeaderId == HeroId);

    /// <summary>Rides in someone else's army, so the army leader's mark stands for it.</summary>
    public bool InOtherArmy => ArmyLeaderId.Length > 0 && ArmyLeaderId != HeroId;

    public bool InSettlement => SettlementId.Length > 0;
}

/// <summary>What <c>coodsz.map</c> wrote: kingdoms, settlements and parties, with lookups by game id.</summary>
public sealed class MapData
{
    public static readonly MapData Empty = new(Array.Empty<MapKingdom>(), Array.Empty<MapSettlement>(), Array.Empty<MapParty>());

    public MapData(IReadOnlyList<MapKingdom> kingdoms, IReadOnlyList<MapSettlement> settlements, IReadOnlyList<MapParty> parties)
    {
        Kingdoms = kingdoms;
        Settlements = settlements;
        Parties = parties;
        KingdomById = ById(kingdoms, k => k.Id);
        SettlementById = ById(settlements, s => s.Id);
        PartyByHero = ById(parties, p => p.HeroId);
    }

    public IReadOnlyList<MapKingdom> Kingdoms { get; }
    public IReadOnlyList<MapSettlement> Settlements { get; }
    public IReadOnlyList<MapParty> Parties { get; }
    public IReadOnlyDictionary<string, MapKingdom> KingdomById { get; }
    public IReadOnlyDictionary<string, MapSettlement> SettlementById { get; }
    public IReadOnlyDictionary<string, MapParty> PartyByHero { get; }

    /// <summary>No settlements means nothing to draw (a parties only read needs an earlier full one).</summary>
    public bool IsEmpty => Settlements.Count == 0;

    /// <summary>The newer parties (and kingdoms, when sent) on the settlements already known.</summary>
    public MapData WithParties(MapData newer) =>
        new(newer.Kingdoms.Count > 0 ? newer.Kingdoms : Kingdoms, newer.Settlements.Count > 0 ? newer.Settlements : Settlements, newer.Parties);

    private static IReadOnlyDictionary<string, T> ById<T>(IEnumerable<T> list, Func<T, string> id)
    {
        var map = new Dictionary<string, T>(StringComparer.Ordinal);
        foreach (var item in list)
            map.TryAdd(id(item), item);
        return map;
    }
}

/// <summary>Reads <c>out\map.txt</c> of <c>coodsz.map</c>; lines it does not know are skipped.</summary>
public static class MapParser
{
    public static MapData Parse(IEnumerable<string> lines)
    {
        var kingdoms = new List<MapKingdom>();
        var settlements = new List<MapSettlement>();
        var parties = new List<MapParty>();
        foreach (var raw in lines)
        {
            var line = raw.Trim();
            if (line.Length == 0 || !line.Contains('|'))
                continue;
            var f = line.Split('|');
            switch (f[0])
            {
                case "MAPKINGDOM" when f.Length >= 5:
                    kingdoms.Add(new MapKingdom(f[1], f[2], Color(f[3]), Color(f[4])));
                    break;
                case "MAPSETTLEMENT" when f.Length >= 12 && Num(f[4], out var sx) && Num(f[5], out var sy):
                    settlements.Add(new MapSettlement(f[1], f[2], f[3], sx, sy, f[6], f[7], f[8], Color(f[9]), f[10], f[11] == "1"));
                    break;
                case "MAPPARTY" when f.Length >= 19 && Num(f[8], out var px) && Num(f[9], out var py):
                    parties.Add(new MapParty(f[1], f[2], f[3], f[4], f[5], f[6], f[7], px, py, Int(f[10]), Int(f[11]), f[12], f[13], Int(f[14]),
                        f[15] == "1", f[16] == "1", f[17], Color(f[18])));
                    break;
            }
        }

        return new MapData(kingdoms, settlements, parties);
    }

    private static bool Num(string s, out double value) =>
        double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out value) && double.IsFinite(value);

    private static int Int(string s) => Num(s, out var d) ? (int)Math.Round(d) : 0;

    private static uint Color(string s) =>
        uint.TryParse(s, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var c) ? c : 0;
}

/// <summary>The part of the world the map shows: a rectangle in game coordinates (y grows north).</summary>
public readonly record struct MapBounds(double MinX, double MinY, double MaxX, double MaxY)
{
    public double Width => MaxX - MinX;
    public double Height => MaxY - MinY;
    public double CenterX => (MinX + MaxX) / 2;
    public double CenterY => (MinY + MaxY) / 2;
}

/// <summary>
/// Where the map looks and how close: the game point at the middle of the view and the pixels per
/// game unit. Game y grows north, screen y grows down, so y is flipped.
/// </summary>
public sealed class MapCamera
{
    public const double MinScale = 0.2;
    public const double MaxScale = 60;

    public double CenterX { get; private set; }
    public double CenterY { get; private set; }
    public double Scale { get; private set; } = 1;
    public double ViewWidth { get; private set; } = 1;
    public double ViewHeight { get; private set; } = 1;

    /// <summary>How close the map is compared with the whole world fitted in the view (1 = all of it).</summary>
    public double Zoom { get; private set; } = 1;

    private double _fitScale = 1;

    public void Resize(double width, double height)
    {
        ViewWidth = Math.Max(1, width);
        ViewHeight = Math.Max(1, height);
    }

    /// <summary>The whole <paramref name="bounds"/> in the view.</summary>
    public void Fit(MapBounds bounds)
    {
        _fitScale = Math.Clamp(Math.Min(ViewWidth / Math.Max(1, bounds.Width), ViewHeight / Math.Max(1, bounds.Height)), MinScale, MaxScale);
        Scale = _fitScale;
        Zoom = 1;
        CenterX = bounds.CenterX;
        CenterY = bounds.CenterY;
    }

    public (double X, double Y) ToScreen(double x, double y) =>
        ((x - CenterX) * Scale + ViewWidth / 2, (CenterY - y) * Scale + ViewHeight / 2);

    public (double X, double Y) ToWorld(double screenX, double screenY) =>
        ((screenX - ViewWidth / 2) / Scale + CenterX, CenterY - (screenY - ViewHeight / 2) / Scale);

    /// <summary>Drags the map by screen pixels.</summary>
    public void Pan(double dx, double dy)
    {
        CenterX -= dx / Scale;
        CenterY += dy / Scale;
    }

    /// <summary>Zooms by <paramref name="factor"/> keeping the game point under the screen point where it is (the mouse wheel).</summary>
    public void ZoomAt(double screenX, double screenY, double factor)
    {
        var (wx, wy) = ToWorld(screenX, screenY);
        SetScale(Scale * factor);
        var (nx, ny) = ToWorld(screenX, screenY);
        CenterX += wx - nx;
        CenterY += wy - ny;
    }

    /// <summary>Puts a game point in the middle, closer in when <paramref name="zoom"/> is larger than now.</summary>
    public void CenterOn(double x, double y, double zoom = 0)
    {
        CenterX = x;
        CenterY = y;
        if (zoom > Zoom)
            SetScale(_fitScale * zoom);
    }

    private void SetScale(double scale)
    {
        Scale = Math.Clamp(scale, Math.Max(MinScale, _fitScale * 0.6), Math.Min(MaxScale, _fitScale * 14));
        Zoom = Scale / _fitScale;
    }
}

/// <summary>Which layers are drawn. Players are always drawn.</summary>
public sealed record MapLayers(bool Towns = true, bool Castles = true, bool Villages = true, bool Lords = true, bool Territory = true, bool Armies = true);

/// <summary>A row of the legend: a kingdom (or the clans outside any kingdom) and how many towns and castles it holds.</summary>
public sealed record MapLegendRow(string KingdomId, string Name, uint Color, int Fiefs);

/// <summary>A kingdom name drawn over its land, at the middle of its towns and castles.</summary>
public sealed record MapKingdomLabel(string KingdomId, string Name, double X, double Y, uint Color);

/// <summary>
/// The live map page: what it shows when the server is stopped or starting, what is drawn at each
/// zoom, the soft territory around settlements, the legend, what a party is doing in plain words,
/// and the player names (hidden in streamer mode). Read only.
/// </summary>
public static class LiveMap
{
    /// <summary>The map reads where the parties are this often while it is open and the server is serving.</summary>
    public static readonly TimeSpan RefreshEvery = TimeSpan.FromSeconds(4);

    /// <summary>Settlements (owners, sieges) are read again this often; in between only the parties.</summary>
    public static readonly TimeSpan SettlementsEvery = TimeSpan.FromSeconds(30);

    /// <summary>Colour of settlements and parties outside any kingdom.</summary>
    public const uint NeutralColor = 0xFFB0A080;

    public const string StoppedHeadline = "Start the server to see the map";
    public const string StoppedLine = "The map shows the towns, castles and villages of the running campaign, who owns them, and where the players and lords are. Start the server and it fills in by itself.";

    /// <summary>The same states as the Encyclopedia, in the map's own words.</summary>
    public static (EncState State, string Headline, string Line) Status(bool running, string blockReason, bool loading, string error, bool hasMap)
    {
        if (!running)
            return (EncState.Stopped, StoppedHeadline, StoppedLine);
        if (blockReason.Length > 0)
            return (EncState.Waiting, "The server is starting", blockReason);
        if (hasMap)
            return (EncState.Ready, "", "");
        if (loading)
            return (EncState.Loading, "Reading the map from the server...", "Big worlds with many mods take a few seconds.");
        if (error.Length > 0)
            return (EncState.Failed, "The map could not be read", error);
        return (EncState.Failed, "The map could not be read", KeepClient.NoAnswer);
    }

    /// <summary>A full read (settlements too) is due: nothing read yet, or the last one is old.</summary>
    public static bool NeedsSettlements(MapData map, DateTime? lastFull, DateTime now) =>
        map.IsEmpty || lastFull is not { } at || now - at >= SettlementsEvery;

    /// <summary>Every settlement and party, with a margin so nothing sits on the edge.</summary>
    public static MapBounds Bounds(MapData map)
    {
        var xs = map.Settlements.Select(s => s.X).Concat(map.Parties.Select(p => p.X)).ToList();
        var ys = map.Settlements.Select(s => s.Y).Concat(map.Parties.Select(p => p.Y)).ToList();
        if (xs.Count == 0)
            return new MapBounds(0, 0, 1000, 1000);
        var minX = xs.Min();
        var maxX = xs.Max();
        var minY = ys.Min();
        var maxY = ys.Max();
        var margin = Math.Max(20, Math.Max(maxX - minX, maxY - minY) * 0.04);
        return new MapBounds(minX - margin, minY - margin, maxX + margin, maxY + margin);
    }

    /// <summary>Is this settlement drawn with these layers?</summary>
    public static bool Shows(MapSettlement s, MapLayers layers) =>
        s.IsTown ? layers.Towns : s.IsCastle ? layers.Castles : s.IsVillage && layers.Villages;

    /// <summary>
    /// May the settlement's name be drawn at this zoom? Towns always, castles closer, villages only
    /// close up. The map still leaves out a name that would cover one already drawn.
    /// </summary>
    public static bool ShowsName(MapSettlement s, double zoom) =>
        s.IsTown || (s.IsCastle && zoom >= 1.8) || (s.IsVillage && zoom >= 5);

    /// <summary>Lord names come in only very close up (and on hover); player names are always drawn.</summary>
    public static bool ShowsLordName(double zoom) => zoom >= 6;

    /// <summary>
    /// The parties drawn on the map: every player (inside a settlement too, so they are easy to find),
    /// army leaders with the Armies layer and other lords out on the map with the Lords layer. A lord
    /// riding in someone else's army is drawn by the army leader's mark; lords inside a settlement are
    /// not drawn, like in the game.
    /// </summary>
    public static IReadOnlyList<MapParty> PartiesToDraw(MapData map, MapLayers layers) =>
        map.Parties
            .Where(p => p.IsPlayer || (!p.InSettlement && !p.InOtherArmy && (layers.Lords || (layers.Armies && p.LeadsArmy))))
            .OrderBy(p => p.IsPlayer ? 1 : 0)
            .ToList();

    /// <summary>"Player 1", "Player 2" in streamer mode (in a fixed order by hero id); the hero's name otherwise.</summary>
    public static string PlayerName(MapData map, MapParty party, bool streamer)
    {
        if (!streamer || !party.IsPlayer)
            return party.Name;
        var players = map.Parties.Where(p => p.IsPlayer).Select(p => p.HeroId).Distinct().OrderBy(id => id, StringComparer.Ordinal).ToList();
        return "Player " + (players.IndexOf(party.HeroId) + 1).ToString(CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// The name the map shows: in streamer mode a player's number from <see cref="StreamerNames"/>
    /// (shared with the other pages; players new to it are numbered in hero id order), otherwise the name.
    /// </summary>
    public static string ShownName(MapData map, MapParty party, bool streamer)
    {
        if (!streamer || !party.IsPlayer)
            return party.Name;
        StreamerNames.Learn(map.Parties.Where(p => p.IsPlayer).OrderBy(p => p.HeroId, StringComparer.Ordinal).Select(p => p.Name));
        return StreamerNames.Alias(party.Name);
    }

    /// <summary>
    /// What the party is doing, as an English sentence with the game name in it (the page translates
    /// it; the language files have "Staying in {0}" and so on): fighting, laying siege, in a town,
    /// in an army, on the way to a town, or near the closest one.
    /// </summary>
    public static string Activity(MapData map, MapParty party)
    {
        if (party.InBattle)
            return "Fighting in battle";
        if (party.Besieging)
            return "Laying siege";
        if (party.InSettlement)
            return "Staying in " + SettlementName(map, party.SettlementId);
        if (party.InOtherArmy)
            return "In the army of " + (map.PartyByHero.TryGetValue(party.ArmyLeaderId, out var leader) ? leader.Name : party.ArmyLeaderId);
        if (party.TargetId.Length > 0 && map.SettlementById.ContainsKey(party.TargetId))
            return "On the way to " + SettlementName(map, party.TargetId);
        return Nearest(map, party.X, party.Y, s => !s.IsVillage) is { } near ? "Near " + near.Name : "Travelling";
    }

    private static string SettlementName(MapData map, string id) =>
        map.SettlementById.TryGetValue(id, out var s) ? s.Name : id;

    /// <summary>The closest settlement to a game point that <paramref name="which"/> accepts.</summary>
    public static MapSettlement? Nearest(MapData map, double x, double y, Func<MapSettlement, bool>? which = null)
    {
        MapSettlement? best = null;
        var bestDistance = double.MaxValue;
        foreach (var s in map.Settlements)
        {
            if (which != null && !which(s))
                continue;
            var d = (s.X - x) * (s.X - x) + (s.Y - y) * (s.Y - y);
            if (d < bestDistance)
            {
                bestDistance = d;
                best = s;
            }
        }

        return best;
    }

    /// <summary>The kingdom's colour for a settlement or party, or the neutral colour outside any kingdom.</summary>
    public static uint ColorOf(MapData map, string kingdomId, uint fallback)
    {
        if (kingdomId.Length > 0 && map.KingdomById.TryGetValue(kingdomId, out var k) && k.Color != 0)
            return Readable(k.Color);
        return fallback != 0 ? Readable(fallback) : NeutralColor;
    }

    /// <summary>
    /// A banner colour that can be seen on the dark map: fully opaque and, when it is very dark
    /// (some kingdoms are near black), lightened towards white.
    /// </summary>
    public static uint Readable(uint argb)
    {
        double r = (argb >> 16) & 0xFF, g = (argb >> 8) & 0xFF, b = argb & 0xFF;
        var light = (0.299 * r + 0.587 * g + 0.114 * b) / 255;
        const double least = 0.33;
        if (light < least)
        {
            var t = (least - light) / (1 - light);
            r += (255 - r) * t;
            g += (255 - g) * t;
            b += (255 - b) * t;
        }

        return 0xFF000000u | ((uint)Math.Round(r) << 16) | ((uint)Math.Round(g) << 8) | (uint)Math.Round(b);
    }

    /// <summary>The legend: kingdoms by the towns and castles they hold, then "No kingdom" when some fiefs have none.</summary>
    public static IReadOnlyList<MapLegendRow> Legend(MapData map)
    {
        var fiefs = map.Settlements.Where(s => !s.IsVillage).ToList();
        var rows = map.Kingdoms
            .Select(k => new MapLegendRow(k.Id, k.Name, ColorOf(map, k.Id, 0), fiefs.Count(s => s.KingdomId == k.Id)))
            .Where(r => r.Fiefs > 0)
            .OrderByDescending(r => r.Fiefs)
            .ThenBy(r => r.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
        var outside = fiefs.Count(s => s.KingdomId.Length == 0 || !map.KingdomById.ContainsKey(s.KingdomId));
        if (outside > 0)
            rows.Add(new MapLegendRow("", "No kingdom", NeutralColor, outside));
        return rows;
    }

    /// <summary>Each kingdom's name at the middle of its towns and castles.</summary>
    public static IReadOnlyList<MapKingdomLabel> KingdomLabels(MapData map) =>
        map.Settlements
            .Where(s => !s.IsVillage && s.KingdomId.Length > 0 && map.KingdomById.ContainsKey(s.KingdomId))
            .GroupBy(s => s.KingdomId)
            .Select(g => new MapKingdomLabel(g.Key, map.KingdomById[g.Key].Name, g.Average(s => s.X), g.Average(s => s.Y), ColorOf(map, g.Key, 0)))
            .ToList();

    /// <summary>
    /// The soft territory drawn under the map: a grid over <paramref name="bounds"/> where each cell
    /// takes the colour of the closest settlement within <paramref name="reach"/> game units, 0
    /// where none is that close (sea and empty land). Row 0 is the north edge. The settlements are
    /// where the land is, so this draws the shape of Calradia (or a mod's map) without a picture.
    /// </summary>
    public static uint[] Territory(MapData map, MapBounds bounds, int columns, int rows, double reach)
    {
        var cells = new uint[columns * rows];
        if (map.Settlements.Count == 0 || columns <= 0 || rows <= 0)
            return cells;
        // Sorted by x, so each column only looks at the settlements within reach of it.
        var points = map.Settlements.Select(s => (s.X, s.Y, Color: ColorOf(map, s.KingdomId, s.Color))).OrderBy(p => p.X).ToArray();
        var xs = points.Select(p => p.X).ToArray();
        var reach2 = reach * reach;
        var cellW = bounds.Width / columns;
        var cellH = bounds.Height / rows;
        for (var col = 0; col < columns; col++)
        {
            var x = bounds.MinX + (col + 0.5) * cellW;
            var first = LowerBound(xs, x - reach);
            var last = LowerBound(xs, x + reach);
            if (first >= last)
                continue;
            for (var row = 0; row < rows; row++)
            {
                var y = bounds.MaxY - (row + 0.5) * cellH;
                var best = reach2;
                uint color = 0;
                for (var i = first; i < last; i++)
                {
                    var dy = points[i].Y - y;
                    if (dy * dy >= best)
                        continue;
                    var dx = points[i].X - x;
                    var d = dx * dx + dy * dy;
                    if (d < best)
                    {
                        best = d;
                        color = points[i].Color;
                    }
                }

                cells[row * columns + col] = color;
            }
        }

        return cells;
    }

    private static int LowerBound(double[] sorted, double value)
    {
        int lo = 0, hi = sorted.Length;
        while (lo < hi)
        {
            var mid = (lo + hi) / 2;
            if (sorted[mid] < value)
                lo = mid + 1;
            else
                hi = mid;
        }

        return lo;
    }

    /// <summary>How far a settlement's land reaches: a little more than the usual gap between neighbours.</summary>
    public static double TerritoryReach(MapData map)
    {
        var list = map.Settlements;
        if (list.Count < 2)
            return 40;
        // The median distance to the nearest neighbour over a sample keeps this quick on big maps.
        var step = Math.Max(1, list.Count / 200);
        var gaps = new List<double>();
        for (var i = 0; i < list.Count; i += step)
        {
            var best = double.MaxValue;
            for (var j = 0; j < list.Count; j++)
            {
                if (i == j)
                    continue;
                var d = (list[i].X - list[j].X) * (list[i].X - list[j].X) + (list[i].Y - list[j].Y) * (list[i].Y - list[j].Y);
                if (d > 0 && d < best)
                    best = d;
            }

            if (best < double.MaxValue)
                gaps.Add(Math.Sqrt(best));
        }

        if (gaps.Count == 0)
            return 40;
        gaps.Sort();
        return Math.Clamp(gaps[gaps.Count / 2] * 2.6, 10, 200);
    }

    /// <summary>"12 players, 140 lords" style line under the title: counts of what is drawn.</summary>
    public static (int Players, int Lords, int Armies) Counts(MapData map) =>
        (map.Parties.Count(p => p.IsPlayer), map.Parties.Count(p => !p.IsPlayer), map.Parties.Count(p => p.LeadsArmy));

    /// <summary>A player hero's party on the map, by game id or Coop's registry id (<c>Hero_lord_1_1</c>).</summary>
    public static MapParty? FindHero(MapData map, string heroId)
    {
        if (string.IsNullOrEmpty(heroId))
            return null;
        if (map.PartyByHero.TryGetValue(heroId, out var party))
            return party;
        if (heroId.StartsWith("Hero_", StringComparison.Ordinal) && map.PartyByHero.TryGetValue(heroId[5..], out party))
            return party;
        return map.Parties.FirstOrDefault(p => string.Equals(p.HeroId, heroId, StringComparison.OrdinalIgnoreCase));
    }
}
