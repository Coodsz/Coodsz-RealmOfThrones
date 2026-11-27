using System.Globalization;

namespace BannerlordCoopLauncher.Core.Keep;

/// <summary>A kingdom of <c>coodsz.world</c>.</summary>
public sealed record WorldKingdom(string Id, string Name, string CultureId, string CultureName, string RulerId, string RulerName,
    IReadOnlyList<string> WarIds, string Text);

/// <summary>A clan of <c>coodsz.world</c>.</summary>
public sealed record WorldClan(string Id, string Name, string CultureId, string CultureName, string KingdomId, string KingdomName,
    string LeaderId, string LeaderName, int Tier, int Renown, bool Minor, bool Bandit, bool Player, string Text);

/// <summary>A living lord, wanderer or player hero of <c>coodsz.world</c>. <see cref="Kind"/> is player, lord or wanderer.</summary>
public sealed record WorldHero(string Id, string Name, string ClanId, string ClanName, string KingdomId, string KingdomName,
    string CultureId, string CultureName, int Age, bool Female, int Level, int Gold, int HitPoints, int MaxHitPoints,
    string PlaceId, string PlaceName, string State, bool Player, string SpouseId, string FatherId, string MotherId, string Kind, string Text);

/// <summary>A settlement of <c>coodsz.world</c>. <see cref="Kind"/> is town, castle, village or hideout.</summary>
public sealed record WorldSettlement(string Id, string Name, string Kind, string CultureId, string CultureName, string OwnerClanId,
    string OwnerClanName, string KingdomId, string KingdomName, int Prosperity, int Hearth, int Loyalty, int Security, int Food,
    int Garrison, int Militia, string BoundId, string Text);

/// <summary>A troop kind with what the encyclopedia shows: level, upgrades, skills and its first battle gear.</summary>
public sealed record WorldTroop(string Id, string Name, string CultureId, string CultureName, int Tier, int Level, string FormationClass,
    bool Mounted, bool Ranged, IReadOnlyList<string> UpgradeIds, IReadOnlyList<(string Id, int Value)> Skills, IReadOnlyList<string> ItemIds)
{
    public TroopClass Class => TroopCatalog.ClassOf(FormationClass, Mounted, Ranged);
}

/// <summary>
/// The running campaign's world for the Encyclopedia page: what <c>coodsz.world</c> wrote, plus the
/// item list of <c>coodsz.items</c>. Lookups by game id.
/// </summary>
public sealed class WorldData
{
    public static readonly WorldData Empty = new(Array.Empty<WorldKingdom>(), Array.Empty<WorldClan>(), Array.Empty<WorldHero>(),
        Array.Empty<WorldSettlement>(), Array.Empty<WorldTroop>(), Array.Empty<GameItem>());

    public WorldData(IReadOnlyList<WorldKingdom> kingdoms, IReadOnlyList<WorldClan> clans, IReadOnlyList<WorldHero> heroes,
        IReadOnlyList<WorldSettlement> settlements, IReadOnlyList<WorldTroop> troops, IReadOnlyList<GameItem> items)
    {
        Kingdoms = kingdoms;
        Clans = clans;
        Heroes = heroes;
        Settlements = settlements;
        Troops = troops;
        Items = items;
        KingdomById = ById(kingdoms, k => k.Id);
        ClanById = ById(clans, c => c.Id);
        HeroById = ById(heroes, h => h.Id);
        SettlementById = ById(settlements, s => s.Id);
        TroopById = ById(troops, t => t.Id);
        ItemById = ById(items, i => i.Id);
    }

    public IReadOnlyList<WorldKingdom> Kingdoms { get; }
    public IReadOnlyList<WorldClan> Clans { get; }
    public IReadOnlyList<WorldHero> Heroes { get; }
    public IReadOnlyList<WorldSettlement> Settlements { get; }
    public IReadOnlyList<WorldTroop> Troops { get; }
    public IReadOnlyList<GameItem> Items { get; }

    public IReadOnlyDictionary<string, WorldKingdom> KingdomById { get; }
    public IReadOnlyDictionary<string, WorldClan> ClanById { get; }
    public IReadOnlyDictionary<string, WorldHero> HeroById { get; }
    public IReadOnlyDictionary<string, WorldSettlement> SettlementById { get; }
    public IReadOnlyDictionary<string, WorldTroop> TroopById { get; }
    public IReadOnlyDictionary<string, GameItem> ItemById { get; }

    public bool IsEmpty => Kingdoms.Count + Clans.Count + Heroes.Count + Settlements.Count + Troops.Count + Items.Count == 0;

    public WorldData WithItems(IReadOnlyList<GameItem> items) => new(Kingdoms, Clans, Heroes, Settlements, Troops, items);

    // A mod can give two objects one id; the first one wins, like the game's own lookup.
    private static IReadOnlyDictionary<string, T> ById<T>(IEnumerable<T> list, Func<T, string> id)
    {
        var map = new Dictionary<string, T>(StringComparer.Ordinal);
        foreach (var item in list)
            map.TryAdd(id(item), item);
        return map;
    }
}

/// <summary>Reads <c>out\world.txt</c> of <c>coodsz.world</c>; lines it does not know are skipped.</summary>
public static class WorldParser
{
    public static WorldData Parse(IEnumerable<string> lines)
    {
        var kingdoms = new List<WorldKingdom>();
        var clans = new List<WorldClan>();
        var heroes = new List<WorldHero>();
        var settlements = new List<WorldSettlement>();
        var troops = new List<WorldTroop>();
        foreach (var raw in lines)
        {
            var line = raw.Trim();
            if (line.Length == 0 || !line.Contains('|'))
                continue;
            var f = line.Split('|');
            switch (f[0])
            {
                case "KINGDOM" when f.Length >= 9:
                    kingdoms.Add(new WorldKingdom(f[1], f[2], f[3], f[4], f[5], f[6], List(f[7]), f[8]));
                    break;
                case "CLAN" when f.Length >= 15:
                    clans.Add(new WorldClan(f[1], f[2], f[3], f[4], f[5], f[6], f[7], f[8], Int(f[9]), Int(f[10]), f[11] == "1", f[12] == "1",
                        f[13] == "1", f[14]));
                    break;
                case "LORD" when f.Length >= 24:
                    heroes.Add(new WorldHero(f[1], f[2], f[3], f[4], f[5], f[6], f[7], f[8], Int(f[9]), f[10] == "1", Int(f[11]), Int(f[12]),
                        Int(f[13]), Int(f[14]), f[15], f[16], f[17], f[18] == "1", f[19], f[20], f[21], f[22], f[23]));
                    break;
                case "SETTLEMENT" when f.Length >= 19:
                    settlements.Add(new WorldSettlement(f[1], f[2], f[3], f[4], f[5], f[6], f[7], f[8], f[9], Int(f[10]), Int(f[11]), Int(f[12]),
                        Int(f[13]), Int(f[14]), Int(f[15]), Int(f[16]), f[17], f[18]));
                    break;
                case "TROOP" when f.Length >= 13:
                    troops.Add(new WorldTroop(f[1], f[2], f[3], f[4], Int(f[5]), Int(f[6]), f[7], f[8] == "1", f[9] == "1", List(f[10]),
                        List(f[11]).Select(Skill).Where(s => s.Id.Length > 0).ToList(), List(f[12])));
                    break;
            }
        }

        return new WorldData(kingdoms, clans, heroes, settlements, troops, Array.Empty<GameItem>());
    }

    private static IReadOnlyList<string> List(string field) =>
        field.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    private static (string Id, int Value) Skill(string pair)
    {
        var at = pair.IndexOf('=');
        return at <= 0 ? ("", 0) : (pair[..at], Int(pair[(at + 1)..]));
    }

    private static int Int(string s) =>
        double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var d) ? (int)Math.Round(d) : 0;
}
