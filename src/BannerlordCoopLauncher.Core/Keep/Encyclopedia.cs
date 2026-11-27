// The Encyclopedia page's entries, facts and links follow CoopKeep's encyclopedia
// (https://github.com/Aderimo/bannerlord-coopkeep, src/CoopKeep.App/ViewModels/MainViewModel.Encyclopedia.cs),
// changed for Coodsz Host: the world comes from the running server (coodsz.world), not the game files.

using System.Globalization;

namespace BannerlordCoopLauncher.Core.Keep;

public enum EncSection { Lords, Clans, Kingdoms, Settlements, Troops, Items }

public enum EncState { Stopped, Waiting, Loading, Failed, Ready }

/// <summary>A row of the list: the name, a line of game names under it and a fixed English tag (translated on screen).</summary>
public sealed record EncRow(EncSection Section, string Id, string Title, string Subtitle, string Tag);

/// <summary>One fact tile. The label is fixed English; the value is a number or game name unless <see cref="TranslateValue"/>.</summary>
public sealed record EncFact(string Label, string Value, bool TranslateValue = false);

/// <summary>A link to another entry; <see cref="Sub"/> is a game name.</summary>
public sealed record EncLink(EncSection Section, string Id, string Title, string Sub);

/// <summary>Links under a fixed English heading.</summary>
public sealed record EncLinkGroup(string Title, IReadOnlyList<EncLink> Links);

/// <summary>A filter chip. Fixed labels are English words; the others are game names (kingdoms, cultures).</summary>
public sealed record EncFilter(string Key, string Label, bool Fixed);

/// <summary>One entry's card: picture kind and id, text, fact tiles and link groups.</summary>
public sealed record EncEntry(EncSection Section, string Id, string Title, string Subtitle, string Tag, string Text,
    GameImageKind ImageKind, IReadOnlyList<EncFact> Facts, IReadOnlyList<EncLinkGroup> Groups, bool IsPlayer)
{
    public string Initial => Title.Length > 0 ? char.ToUpperInvariant(Title[0]).ToString() : "?";
}

/// <summary>
/// The Encyclopedia page: the sections, their filter chips, the search and each entry's card.
/// Read only; everything comes from <see cref="WorldData"/>.
/// </summary>
public static class Encyclopedia
{
    public static readonly IReadOnlyList<EncSection> Sections = Enum.GetValues<EncSection>();

    public const string All = "";

    public const string StoppedHeadline = "Start the server to browse the world";
    public const string StoppedLine = "The Encyclopedia reads the kingdoms, clans, lords, towns, troops and items of the running campaign. Start the server and it fills in by itself.";

    /// <summary>
    /// What the page shows instead of the lists: the server is stopped, still loading (the reason
    /// why commands wait), reading the world, could not read it, or nothing (ready).
    /// </summary>
    public static (EncState State, string Headline, string Line) Status(bool running, string blockReason, bool loading, string error, bool hasWorld)
    {
        if (!running)
            return (EncState.Stopped, StoppedHeadline, StoppedLine);
        if (blockReason.Length > 0)
            return (EncState.Waiting, "The server is starting", blockReason);
        if (loading)
            return (EncState.Loading, "Reading the world from the server...", "Big worlds with many mods take a few seconds.");
        if (error.Length > 0)
            return (EncState.Failed, "The world could not be read", error);
        return hasWorld
            ? (EncState.Ready, "", "")
            : (EncState.Failed, "The world could not be read", KeepClient.NoAnswer);
    }

    public static string SectionName(EncSection section) => section switch
    {
        EncSection.Lords => "Heroes and lords",
        EncSection.Clans => "Clans",
        EncSection.Kingdoms => "Kingdoms",
        EncSection.Settlements => "Settlements",
        EncSection.Troops => "Troops",
        _ => "Items",
    };

    public static int Count(EncSection section, WorldData world) => section switch
    {
        EncSection.Lords => world.Heroes.Count,
        EncSection.Clans => world.Clans.Count,
        EncSection.Kingdoms => world.Kingdoms.Count,
        EncSection.Settlements => world.Settlements.Count,
        EncSection.Troops => world.Troops.Count,
        _ => world.Items.Count,
    };

    /// <summary>"397 entries".</summary>
    public static string CountText(int count) => count == 1 ? "1 entry" : count + " entries";

    /// <summary>The chips of one section: All, the fixed kinds, then kingdoms or cultures by name.</summary>
    public static IReadOnlyList<EncFilter> Filters(EncSection section, WorldData world)
    {
        var list = new List<EncFilter> { new(All, "All", true) };
        switch (section)
        {
            case EncSection.Lords:
                list.Add(new("kind:player", "Players", true));
                list.Add(new("kind:lord", "Lords", true));
                list.Add(new("kind:wanderer", "Wanderers", true));
                list.AddRange(KingdomFilters(world));
                break;
            case EncSection.Clans:
                list.Add(new("clan:player", "Players", true));
                list.Add(new("clan:kingdom", "In a kingdom", true));
                list.Add(new("clan:minor", "Minor factions", true));
                list.Add(new("clan:bandit", "Bandits", true));
                list.AddRange(KingdomFilters(world));
                break;
            case EncSection.Kingdoms:
                list.Add(new("kingdom:war", "At war", true));
                list.Add(new("kingdom:peace", "At peace", true));
                break;
            case EncSection.Settlements:
                list.Add(new("kind:town", "Towns", true));
                list.Add(new("kind:castle", "Castles", true));
                list.Add(new("kind:village", "Villages", true));
                list.Add(new("kind:hideout", "Hideouts", true));
                list.AddRange(KingdomFilters(world));
                break;
            case EncSection.Troops:
                foreach (var troopClass in Enum.GetValues<TroopClass>())
                    list.Add(new("class:" + troopClass, ClassPlural(troopClass), true));
                list.AddRange(world.Troops.Where(t => t.CultureId.Length > 0)
                    .GroupBy(t => t.CultureId, StringComparer.Ordinal)
                    .Select(g => new EncFilter("culture:" + g.Key, Name(g.First().CultureName, g.Key), false))
                    .OrderBy(f => f.Label, StringComparer.CurrentCultureIgnoreCase));
                break;
            default:
                foreach (var (group, _) in ItemCatalog.Counts(world.Items))
                    list.Add(new("group:" + group, ItemCatalog.GroupName(group), true));
                break;
        }

        return list;
    }

    private static string ClassPlural(TroopClass troopClass) => troopClass switch
    {
        TroopClass.Archer => "Archers",
        TroopClass.Cavalry => "Cavalry",
        TroopClass.HorseArcher => "Horse archers",
        _ => "Infantry",
    };

    private static IEnumerable<EncFilter> KingdomFilters(WorldData world) =>
        world.Kingdoms.Select(k => new EncFilter("kingdom:" + k.Id, Name(k.Name, k.Id), false))
            .OrderBy(f => f.Label, StringComparer.CurrentCultureIgnoreCase);

    /// <summary>The rows of one section that match the chip and the search (name or the line under it), by name.</summary>
    public static IReadOnlyList<EncRow> Search(EncSection section, WorldData world, string? query, string? filter)
    {
        var key = filter ?? All;
        var q = (query ?? "").Trim();
        var rows = section switch
        {
            EncSection.Lords => world.Heroes.Where(h => LordMatches(h, key)).Select(h => Row(h)),
            EncSection.Clans => world.Clans.Where(c => ClanMatches(c, key)).Select(c => Row(c)),
            EncSection.Kingdoms => world.Kingdoms.Where(k => KingdomMatches(k, key)).Select(k => Row(k)),
            EncSection.Settlements => world.Settlements.Where(s => SettlementMatches(s, key)).Select(s => Row(s)),
            EncSection.Troops => world.Troops.Where(t => TroopMatches(t, key)).Select(t => Row(t)),
            _ => world.Items.Where(i => key.Length == 0 || key == "group:" + i.Group).Select(i => Row(i)),
        };

        // A name that starts with the search comes before one that only contains it.
        var ordered = q.Length > 0
            ? rows.Where(r => r.Title.Contains(q, StringComparison.CurrentCultureIgnoreCase) || r.Subtitle.Contains(q, StringComparison.CurrentCultureIgnoreCase))
                .OrderBy(r => r.Title.StartsWith(q, StringComparison.CurrentCultureIgnoreCase) ? 0 : 1)
            : rows.OrderBy(_ => 0);
        return ordered.ThenBy(r => r.Title, StringComparer.CurrentCultureIgnoreCase).ToList();
    }

    private static bool LordMatches(WorldHero h, string key) => key switch
    {
        "" => true,
        "kind:player" => h.Player,
        "kind:lord" => h.Kind == "lord" && !h.Player,
        "kind:wanderer" => h.Kind == "wanderer" && !h.Player,
        _ => key == "kingdom:" + h.KingdomId,
    };

    private static bool ClanMatches(WorldClan c, string key) => key switch
    {
        "" => true,
        "clan:player" => c.Player,
        "clan:kingdom" => c.KingdomId.Length > 0,
        "clan:minor" => c.Minor && !c.Bandit,
        "clan:bandit" => c.Bandit,
        _ => key == "kingdom:" + c.KingdomId,
    };

    private static bool KingdomMatches(WorldKingdom k, string key) => key switch
    {
        "kingdom:war" => k.WarIds.Count > 0,
        "kingdom:peace" => k.WarIds.Count == 0,
        _ => true,
    };

    private static bool SettlementMatches(WorldSettlement s, string key) =>
        key.Length == 0 || key == "kind:" + s.Kind || key == "kingdom:" + s.KingdomId;

    private static bool TroopMatches(WorldTroop t, string key) =>
        key.Length == 0 || key == "class:" + t.Class || key == "culture:" + t.CultureId;

    // Rows

    private static EncRow Row(WorldHero h) =>
        new(EncSection.Lords, h.Id, Name(h.Name, h.Id), Join(h.ClanName, h.KingdomName), HeroTag(h));

    private static EncRow Row(WorldClan c) => new(EncSection.Clans, c.Id, Name(c.Name, c.Id), c.KingdomName.Length > 0 ? c.KingdomName : c.CultureName, ClanTag(c));

    private static EncRow Row(WorldKingdom k) => new(EncSection.Kingdoms, k.Id, Name(k.Name, k.Id), Join(k.RulerName, k.CultureName), k.WarIds.Count > 0 ? "At war" : "");

    private static EncRow Row(WorldSettlement s) => new(EncSection.Settlements, s.Id, Name(s.Name, s.Id), Join(s.OwnerClanName, s.KingdomName), KindName(s.Kind));

    private static EncRow Row(WorldTroop t) => new(EncSection.Troops, t.Id, Name(t.Name, t.Id), t.CultureName, TroopCatalog.Stars(t.Tier));

    private static EncRow Row(GameItem i) => new(EncSection.Items, i.Id, Name(i.Name, i.Id), i.CultureName, ItemCatalog.GroupName(i.Group));

    private static string HeroTag(WorldHero h) => h.Player ? "Player" : h.Kind == "wanderer" ? "Wanderer" : "Lord";

    private static string ClanTag(WorldClan c) => c.Player ? "Player clan" : c.Bandit ? "Bandits" : c.Minor ? "Minor faction" : "";

    public static string KindName(string kind) => kind switch
    {
        "town" => "Town",
        "castle" => "Castle",
        "village" => "Village",
        "hideout" => "Hideout",
        _ => "Other",
    };

    /// <summary>The game's hero state in plain words.</summary>
    public static string StateName(string state) => state switch
    {
        "Active" => "Active",
        "Prisoner" => "Prisoner",
        "Fugitive" => "On the run",
        "Released" => "Just released",
        "Traveling" => "Traveling",
        "Disabled" => "Away",
        "NotSpawned" => "Not on the map yet",
        "Dead" => "Dead",
        _ => "Unknown",
    };

    // Cards

    /// <summary>One entry's card, or null when the world no longer has that id.</summary>
    public static EncEntry? Entry(WorldData world, EncSection section, string id) => section switch
    {
        EncSection.Lords => world.HeroById.TryGetValue(id, out var h) ? Lord(world, h) : null,
        EncSection.Clans => world.ClanById.TryGetValue(id, out var c) ? Clan(world, c) : null,
        EncSection.Kingdoms => world.KingdomById.TryGetValue(id, out var k) ? Kingdom(world, k) : null,
        EncSection.Settlements => world.SettlementById.TryGetValue(id, out var s) ? Settlement(world, s) : null,
        EncSection.Troops => world.TroopById.TryGetValue(id, out var t) ? Troop(world, t) : null,
        _ => world.ItemById.TryGetValue(id, out var i) ? Item(world, i) : null,
    };

    private static EncEntry Lord(WorldData world, WorldHero h)
    {
        var facts = new List<EncFact>();
        Fact(facts, "Clan", h.ClanName);
        Fact(facts, "Kingdom", h.KingdomName);
        Fact(facts, "Culture", h.CultureName);
        facts.Add(new("Age", N(h.Age)));
        facts.Add(new("Level", N(h.Level)));
        facts.Add(new("Gold", N(h.Gold)));
        facts.Add(new("Health", N(h.HitPoints) + " / " + N(h.MaxHitPoints)));
        Fact(facts, "Where", h.PlaceName);
        facts.Add(new("State", StateName(h.State), true));

        var groups = new List<EncLinkGroup>();
        Group(groups, "Clan", ClanLink(world, h.ClanId));
        Group(groups, "Kingdom", KingdomLink(world, h.KingdomId));
        Group(groups, "Where", SettlementLink(world, h.PlaceId));
        Group(groups, "Spouse", HeroLink(world, h.SpouseId));
        Group(groups, "Parents", HeroLink(world, h.FatherId), HeroLink(world, h.MotherId));
        Group(groups, "Children", world.Heroes.Where(c => c.Id != h.Id && (c.FatherId == h.Id || c.MotherId == h.Id)).Select(c => HeroLink(world, c.Id)));
        return new EncEntry(EncSection.Lords, h.Id, Name(h.Name, h.Id), Join(h.ClanName, h.KingdomName), HeroTag(h), h.Text,
            GameImageKind.Lord, facts, groups, h.Player);
    }

    private static EncEntry Clan(WorldData world, WorldClan c)
    {
        var members = world.Heroes.Where(h => h.ClanId == c.Id).OrderBy(h => h.Id == c.LeaderId ? 0 : 1).ThenBy(h => h.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
        var fiefs = world.Settlements.Where(s => s.OwnerClanId == c.Id && s.Kind is "town" or "castle").OrderBy(s => s.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
        var facts = new List<EncFact>();
        Fact(facts, "Kingdom", c.KingdomName);
        Fact(facts, "Culture", c.CultureName);
        Fact(facts, "Leader", c.LeaderName);
        facts.Add(new("Tier", N(c.Tier)));
        facts.Add(new("Renown", N(c.Renown)));
        facts.Add(new("Heroes", N(members.Count)));
        facts.Add(new("Towns and castles", N(fiefs.Count)));

        var groups = new List<EncLinkGroup>();
        Group(groups, "Leader", HeroLink(world, c.LeaderId));
        Group(groups, "Kingdom", KingdomLink(world, c.KingdomId));
        Group(groups, "Heroes", members.Select(h => HeroLink(world, h.Id)));
        Group(groups, "Towns and castles", fiefs.Select(s => SettlementLink(world, s.Id)));
        return new EncEntry(EncSection.Clans, c.Id, Name(c.Name, c.Id), c.KingdomName.Length > 0 ? c.KingdomName : c.CultureName, ClanTag(c), c.Text,
            GameImageKind.Banner, facts, groups, c.Player);
    }

    private static EncEntry Kingdom(WorldData world, WorldKingdom k)
    {
        var clans = world.Clans.Where(c => c.KingdomId == k.Id).OrderBy(c => c.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
        var places = world.Settlements.Where(s => s.KingdomId == k.Id).ToList();
        var facts = new List<EncFact>();
        Fact(facts, "Ruler", k.RulerName);
        Fact(facts, "Culture", k.CultureName);
        facts.Add(new("Clans", N(clans.Count)));
        facts.Add(new("Heroes", N(world.Heroes.Count(h => h.KingdomId == k.Id))));
        facts.Add(new("Towns", N(places.Count(s => s.Kind == "town"))));
        facts.Add(new("Castles", N(places.Count(s => s.Kind == "castle"))));
        facts.Add(new("Villages", N(places.Count(s => s.Kind == "village"))));

        var groups = new List<EncLinkGroup>();
        Group(groups, "Ruler", HeroLink(world, k.RulerId));
        Group(groups, "At war with", k.WarIds.Select(id => KingdomLink(world, id)));
        Group(groups, "Clans", clans.Select(c => ClanLink(world, c.Id)));
        Group(groups, "Towns and castles", places.Where(s => s.Kind is "town" or "castle")
            .OrderBy(s => s.Kind == "town" ? 0 : 1).ThenBy(s => s.Name, StringComparer.CurrentCultureIgnoreCase).Select(s => SettlementLink(world, s.Id)));
        return new EncEntry(EncSection.Kingdoms, k.Id, Name(k.Name, k.Id), Join(k.RulerName, k.CultureName), k.WarIds.Count > 0 ? "At war" : "", k.Text,
            GameImageKind.Banner, facts, groups, false);
    }

    private static EncEntry Settlement(WorldData world, WorldSettlement s)
    {
        var fortified = s.Kind is "town" or "castle";
        var facts = new List<EncFact> { new("Kind", KindName(s.Kind), true) };
        Fact(facts, "Kingdom", s.KingdomName);
        Fact(facts, "Owner", s.OwnerClanName);
        Fact(facts, "Culture", s.CultureName);
        if (fortified)
        {
            facts.Add(new("Prosperity", N(s.Prosperity)));
            facts.Add(new("Loyalty", N(s.Loyalty)));
            facts.Add(new("Security", N(s.Security)));
            facts.Add(new("Food", N(s.Food)));
            facts.Add(new("Garrison", N(s.Garrison)));
        }

        if (s.Kind == "village")
            facts.Add(new("Hearth", N(s.Hearth)));
        if (s.Kind != "hideout")
            facts.Add(new("Militia", N(s.Militia)));

        var groups = new List<EncLinkGroup>();
        Group(groups, "Owner", ClanLink(world, s.OwnerClanId));
        Group(groups, "Kingdom", KingdomLink(world, s.KingdomId));
        Group(groups, "Bound to", SettlementLink(world, s.BoundId));
        Group(groups, "Villages", world.Settlements.Where(v => v.BoundId == s.Id).OrderBy(v => v.Name, StringComparer.CurrentCultureIgnoreCase).Select(v => SettlementLink(world, v.Id)));
        Group(groups, "Heroes here", world.Heroes.Where(h => h.PlaceId == s.Id).OrderBy(h => h.Name, StringComparer.CurrentCultureIgnoreCase).Select(h => HeroLink(world, h.Id)));
        return new EncEntry(EncSection.Settlements, s.Id, Name(s.Name, s.Id), Join(s.OwnerClanName, s.KingdomName), KindName(s.Kind), s.Text,
            GameImageKind.Banner, facts, groups, false);
    }

    private static EncEntry Troop(WorldData world, WorldTroop t)
    {
        var facts = new List<EncFact>();
        Fact(facts, "Culture", t.CultureName);
        facts.Add(new("Tier", N(t.Tier)));
        facts.Add(new("Level", N(t.Level)));
        facts.Add(new("Class", TroopCatalog.ClassName(t.Class), true));
        foreach (var (skill, value) in t.Skills.OrderByDescending(s => s.Value))
            facts.Add(new(SkillLayout.SkillName(skill), N(value)));

        var groups = new List<EncLinkGroup>();
        Group(groups, "Upgrades to", t.UpgradeIds.Select(id => TroopLink(world, id)));
        Group(groups, "Upgrades from", world.Troops.Where(o => o.UpgradeIds.Contains(t.Id)).Select(o => TroopLink(world, o.Id)));
        Group(groups, "Equipment", t.ItemIds.Select(id => ItemLink(world, id)));
        return new EncEntry(EncSection.Troops, t.Id, Name(t.Name, t.Id), t.CultureName, TroopCatalog.Stars(t.Tier), "",
            GameImageKind.Troop, facts, groups, false);
    }

    private static EncEntry Item(WorldData world, GameItem i)
    {
        var facts = new List<EncFact>
        {
            new("Category", ItemCatalog.GroupName(i.Group), true),
            new("Culture", ItemCatalog.CultureLabel(i), i.CultureName.Length == 0),
            new("Value", N(i.Value)),
            new("Tier", N(i.Tier)),
            new("Weight", i.Weight.ToString("0.##", CultureInfo.CurrentCulture)),
            new("Civilian", i.Civilian ? "Yes" : "No", true),
        };
        var groups = new List<EncLinkGroup>();
        Group(groups, "Troops that carry it", world.Troops.Where(t => t.ItemIds.Contains(i.Id)).Select(t => TroopLink(world, t.Id)));
        return new EncEntry(EncSection.Items, i.Id, Name(i.Name, i.Id), i.CultureName, ItemCatalog.GroupName(i.Group), "",
            GameImageKind.Item, facts, groups, false);
    }

    // Links: an id the world does not know gives no link.

    private static EncLink? HeroLink(WorldData world, string id) =>
        world.HeroById.TryGetValue(id, out var h) ? new EncLink(EncSection.Lords, h.Id, Name(h.Name, h.Id), h.ClanName) : null;

    private static EncLink? ClanLink(WorldData world, string id) =>
        world.ClanById.TryGetValue(id, out var c) ? new EncLink(EncSection.Clans, c.Id, Name(c.Name, c.Id), c.KingdomName) : null;

    private static EncLink? KingdomLink(WorldData world, string id) =>
        world.KingdomById.TryGetValue(id, out var k) ? new EncLink(EncSection.Kingdoms, k.Id, Name(k.Name, k.Id), k.RulerName) : null;

    private static EncLink? SettlementLink(WorldData world, string id) =>
        world.SettlementById.TryGetValue(id, out var s) ? new EncLink(EncSection.Settlements, s.Id, Name(s.Name, s.Id), s.OwnerClanName) : null;

    private static EncLink? TroopLink(WorldData world, string id) =>
        world.TroopById.TryGetValue(id, out var t) ? new EncLink(EncSection.Troops, t.Id, Name(t.Name, t.Id), TroopCatalog.Stars(t.Tier)) : null;

    private static EncLink? ItemLink(WorldData world, string id) =>
        world.ItemById.TryGetValue(id, out var i) ? new EncLink(EncSection.Items, i.Id, Name(i.Name, i.Id), i.CultureName) : null;

    private static void Group(List<EncLinkGroup> groups, string title, params EncLink?[] links) => Group(groups, title, links.AsEnumerable());

    private static void Group(List<EncLinkGroup> groups, string title, IEnumerable<EncLink?> links)
    {
        var list = links.OfType<EncLink>().GroupBy(l => l.Id, StringComparer.Ordinal).Select(g => g.First()).ToList();
        if (list.Count > 0)
            groups.Add(new EncLinkGroup(title, list));
    }

    private static void Fact(List<EncFact> facts, string label, string value)
    {
        if (value.Length > 0)
            facts.Add(new EncFact(label, value));
    }

    private static string N(int value) => value.ToString("N0", CultureInfo.CurrentCulture);

    private static string Name(string name, string id) => name.Length > 0 ? name : id;

    private static string Join(string a, string b) => a.Length == 0 ? b : b.Length == 0 ? a : a + " · " + b;
}
