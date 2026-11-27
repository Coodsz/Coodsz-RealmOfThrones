// Ported from CoopKeep (https://github.com/Aderimo/bannerlord-coopkeep),
// src/CoopKeep.Core/Protocol/ServerToolsParser.cs and src/CoopKeep.Core/Installation/ServerItemCatalog.cs,
// translated from Turkish and changed for the coodsz.* line formats.

using System.Globalization;
using System.Text.RegularExpressions;

namespace BannerlordCoopLauncher.Core.Keep;

/// <summary>A row of <c>coodsz.heroes</c>.</summary>
public sealed record HeroSummary(
    string Id, string Name, string ClanId, string ClanName, string KingdomName, int Gold, int HitPoints, int MaxHitPoints,
    int Level, string PartyId, string Place, bool IsPlayer);

/// <summary><c>coodsz.hero</c>: the header and stat tiles of the player profile.</summary>
public sealed record HeroProfile(
    string Id, string Name, int Level, int Gold, int HitPoints, int MaxHitPoints, double Age, int Influence, int Renown,
    int ClanTier, int Troops, int Wounded, string ClanId, string ClanName, string KingdomId, string KingdomName,
    string CultureId, string CultureName, string PlaceId, string PlaceName, string PartyId);

/// <summary><c>coodsz.skills</c>.</summary>
public sealed record HeroDevelopment(
    int Level, int TotalXp, int UnspentAttributePoints, int UnspentFocusPoints,
    IReadOnlyList<HeroAttribute> Attributes, IReadOnlyList<HeroSkill> Skills);

/// <summary>vigor, control, endurance, cunning, social, intelligence.</summary>
public sealed record HeroAttribute(string Id, string Name, int Value);

/// <summary>A skill: level, focus points, XP stored toward the next level and its attribute.</summary>
public sealed record HeroSkill(string Id, string Name, int Value, int Focus, int XpProgress, string AttributeId);

/// <summary>One worn item. <see cref="Set"/> is <c>battle</c> or <c>civilian</c>.</summary>
public sealed record EquipmentPiece(string Set, string Slot, string ItemId, string Name, int Value);

/// <summary>One line of the party inventory.</summary>
public sealed record InventoryItem(string Id, string Name, int Count, int Value);

/// <summary><c>coodsz.roster</c>: troop and wounded counts leave heroes out.</summary>
public sealed record PartyRoster(string PartyId, int Troops, int Wounded, IReadOnlyList<RosterTroop> Members, IReadOnlyList<RosterTroop> Prisoners);

public sealed record RosterTroop(string Id, string Name, int Number, int Wounded, int Xp, bool IsHero, int Tier, string CultureId);

/// <summary><c>COODSZ_SET hero=... what=troop:imperial_recruit old=... new=...</c>.</summary>
public sealed record ToolsChange(string HeroId, string What, int Old, int New)
{
    public bool Changed => Old != New;
}

/// <summary><c>COODSZ_FILE|items|path|count</c>: a long list written to a file by the module.</summary>
public sealed record ToolsFile(string Name, string Path, int Count);

/// <summary>A troop kind of <c>coodsz.troop_types</c>.</summary>
public sealed record TroopType(string Id, string Name, string CultureId, string CultureName, int Tier, string FormationClass, bool Mounted, bool Ranged)
{
    public TroopClass Class => TroopCatalog.ClassOf(FormationClass, Mounted, Ranged);
}

/// <summary>
/// Reads the answers of the Coodsz Server Tools module. Every answer is lines of fields split by
/// <c>|</c>; lines it does not know (server log lines) are skipped.
/// </summary>
public static partial class ToolsParser
{
    public const string End = "COODSZ_END";
    public const string ErrorPrefix = "COODSZ_ERROR";

    [GeneratedRegex(@"COODSZ_SET hero=(?<hero>\S+) what=(?<what>\S+) old=(?<old>-?\d+) new=(?<new>-?\d+)")]
    private static partial Regex ChangeLine();

    /// <summary>The module answered the ping.</summary>
    public static bool IsPong(IEnumerable<string> lines) => lines.Any(line => line.Contains(ToolsCommand.PingReply, StringComparison.Ordinal));

    /// <summary>The text after COODSZ_ERROR, or null.</summary>
    public static string? Error(IEnumerable<string> lines)
    {
        foreach (var raw in lines)
        {
            var at = raw.IndexOf(ErrorPrefix, StringComparison.Ordinal);
            if (at >= 0)
                return raw[(at + ErrorPrefix.Length)..].Trim();
            if (raw.TrimStart().StartsWith("Usage:", StringComparison.Ordinal))
                return raw.Trim();
        }

        return null;
    }

    public static IReadOnlyList<HeroSummary> ParseHeroes(IEnumerable<string> lines) =>
        Fields(lines).Where(f => f[0] == "H" && f.Length >= 13)
            .Select(f => new HeroSummary(f[1], f[2], f[3], f[4], f[5], Int(f[6]), Int(f[7]), Int(f[8]), Int(f[9]), f[10], f[11], f[12] == "1"))
            .ToList();

    public static HeroProfile? ParseProfile(IEnumerable<string> lines)
    {
        var f = Fields(lines).FirstOrDefault(x => x[0] == "HERO" && x.Length >= 22);
        return f == null
            ? null
            : new HeroProfile(f[1], f[2], Int(f[3]), Int(f[4]), Int(f[5]), Int(f[6]), Dbl(f[7]), Int(f[8]), Int(f[9]), Int(f[10]),
                Int(f[11]), Int(f[12]), f[13], f[14], f[15], f[16], f[17], f[18], f[19], f[20], f[21]);
    }

    public static HeroDevelopment? ParseDevelopment(IEnumerable<string> lines)
    {
        string[]? head = null;
        var attributes = new List<HeroAttribute>();
        var skills = new List<HeroSkill>();
        foreach (var f in Fields(lines))
        {
            switch (f[0])
            {
                case "DEV" when f.Length >= 5: head = f; break;
                case "ATTR" when f.Length >= 4: attributes.Add(new HeroAttribute(f[1], f[2], Int(f[3]))); break;
                case "SKILL" when f.Length >= 7: skills.Add(new HeroSkill(f[1], f[2], Int(f[3]), Int(f[4]), Int(f[5]), f[6])); break;
            }
        }

        return head == null ? null : new HeroDevelopment(Int(head[1]), Int(head[2]), Int(head[3]), Int(head[4]), attributes, skills);
    }

    public static IReadOnlyList<EquipmentPiece> ParseEquipment(IEnumerable<string> lines) =>
        Fields(lines).Where(f => f[0] == "EQ" && f.Length >= 5)
            .Select(f => new EquipmentPiece(f[1], f[2], f[3], f[4], f.Length > 5 ? Int(f[5]) : 0))
            .ToList();

    public static IReadOnlyList<InventoryItem> ParseInventory(IEnumerable<string> lines) =>
        Fields(lines).Where(f => f[0] == "INV" && f.Length >= 5)
            .Select(f => new InventoryItem(f[1], f[2], Int(f[3]), Int(f[4])))
            .ToList();

    public static PartyRoster? ParseRoster(IEnumerable<string> lines)
    {
        string[]? party = null;
        var members = new List<RosterTroop>();
        var prisoners = new List<RosterTroop>();
        foreach (var f in Fields(lines))
        {
            if (f[0] == "PARTY" && f.Length >= 4)
                party = f;
            else if (f[0] == "TROOP" && f.Length >= 8)
            {
                var troop = new RosterTroop(f[2], f[3], Int(f[4]), Int(f[5]), Int(f[6]), f[7] == "1", f.Length > 8 ? Int(f[8]) : 0, f.Length > 9 ? f[9] : "");
                (f[1] == "prisoners" ? prisoners : members).Add(troop);
            }
        }

        return party == null ? null : new PartyRoster(party[1], Int(party[2]), Int(party[3]), members, prisoners);
    }

    /// <summary>The last COODSZ_SET line.</summary>
    public static ToolsChange? ParseChange(IEnumerable<string> lines)
    {
        ToolsChange? last = null;
        foreach (var line in lines)
        {
            var m = ChangeLine().Match(line);
            if (m.Success)
                last = new ToolsChange(m.Groups["hero"].Value, m.Groups["what"].Value, Int(m.Groups["old"].Value), Int(m.Groups["new"].Value));
        }

        return last;
    }

    public static ToolsFile? ParseFile(IEnumerable<string> lines)
    {
        var f = Fields(lines).FirstOrDefault(x => x[0] == "COODSZ_FILE" && x.Length >= 4);
        return f == null ? null : new ToolsFile(f[1], f[2], Int(f[3]));
    }

    public static IReadOnlyList<GameItem> ParseItems(IEnumerable<string> lines) =>
        Fields(lines).Where(f => f[0] == "ITEM" && f.Length >= 13)
            .Select(f => new GameItem(f[1], f[2], f[3], Int(f[4]), Int(f[5]), f[6], f[7], f[8] == "1", f[9] == "1", f[10] == "1", f[11], Dbl(f[12])))
            .ToList();

    public static IReadOnlyList<TroopType> ParseTroopTypes(IEnumerable<string> lines) =>
        Fields(lines).Where(f => f[0] == "TT" && f.Length >= 9)
            .Select(f => new TroopType(f[1], f[2], f[3], f[4], Int(f[5]), f[6], f[7] == "1", f[8] == "1"))
            .ToList();

    /// <summary>Has the answer reached its end mark?</summary>
    public static bool IsComplete(IEnumerable<string> lines) => lines.Any(l => l.Trim() == End);

    private static IEnumerable<string[]> Fields(IEnumerable<string> lines) =>
        lines.Select(l => l.Trim()).Where(l => l.Length > 0 && l.Contains('|') && !l.StartsWith('[')).Select(l => l.Split('|'));

    private static int Int(string s) =>
        double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var d) ? (int)Math.Round(d) : 0;

    private static double Dbl(string s) =>
        double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var d) ? d : 0;
}
