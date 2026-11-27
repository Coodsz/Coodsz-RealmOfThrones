// The coodsz.* command builders follow CoopKeep's server tools builders
// (https://github.com/Aderimo/bannerlord-coopkeep, src/CoopKeep.Core/Server/AdminCommand.cs),
// changed for the Coodsz Server Tools module.

using System.Globalization;
using BannerlordCoopLauncher.Core.Server;

namespace BannerlordCoopLauncher.Core.Keep;

/// <summary>
/// The coodsz.* console commands of the Coodsz Server Tools module (compat\CoodszServerTools),
/// built with their numbers clamped and their ids checked, like <see cref="AdminCommand"/>.
/// </summary>
public static class ToolsCommand
{
    /// <summary>A server only module id: Coop leaves DedicatedServer.* out of the client module check.</summary>
    public const string ModuleId = "DedicatedServer.CoodszTools";
    public const string ModuleVersion = "v1.8.1";
    public const string PingReply = "COODSZ_TOOLS v1";

    public const int MaxGold = 10_000_000;
    public const int MaxInfluence = 10_000;
    public const int MaxRenown = 100_000;
    public const int MaxTroops = 1000;
    public const int MaxItems = 10_000;
    public const int MaxXp = 1_000_000;
    public const int MinAge = 18;
    public const int MaxAge = 90;
    public const int MaxSkill = 330;

    public static string Ping() => "coodsz.ping";

    /// <summary>The heroes that look like players, or every living hero whose name contains <paramref name="name"/>.</summary>
    public static string Heroes(string? name = null)
    {
        var clean = ServerCommand.SanitizeArgument(name, 64).Replace("\"", "");
        return clean.Length == 0 ? "coodsz.heroes" : "coodsz.heroes " + clean;
    }

    public static string Hero(string heroId) => "coodsz.hero " + Id(heroId);
    public static string Skills(string heroId) => "coodsz.skills " + Id(heroId);
    public static string Equipment(string heroId) => "coodsz.equipment " + Id(heroId);
    public static string Inventory(string heroId) => "coodsz.inventory " + Id(heroId);
    public static string Roster(string heroId) => "coodsz.roster " + Id(heroId);

    /// <summary>Gives gold, or takes it with a negative amount.</summary>
    public static string Gold(string heroId, int amount) => "coodsz.gold " + Id(heroId) + " " + N(NotZero(Math.Clamp(amount, -MaxGold, MaxGold)));
    public static string SetGold(string heroId, int gold) => "coodsz.set_gold " + Id(heroId) + " " + N(Math.Clamp(gold, 0, MaxGold));
    public static string Influence(string heroId, int amount) => "coodsz.influence " + Id(heroId) + " " + N(NotZero(Math.Clamp(amount, -MaxInfluence, MaxInfluence)));
    public static string Renown(string heroId, int amount) => "coodsz.renown " + Id(heroId) + " " + N(NotZero(Math.Clamp(amount, -MaxRenown, MaxRenown)));
    public static string ClanTier(string heroId, int tier) => "coodsz.clan_tier " + Id(heroId) + " " + N(Math.Clamp(tier, 0, 10));
    public static string Health(string heroId, int hitPoints) => "coodsz.health " + Id(heroId) + " " + N(Math.Clamp(hitPoints, 1, 10_000));
    public static string Age(string heroId, int age) => "coodsz.age " + Id(heroId) + " " + N(Math.Clamp(age, MinAge, MaxAge));

    public static string Skill(string heroId, string skillId, int levels) =>
        "coodsz.skill " + Id(heroId) + " " + Word(skillId) + " " + N(NotZero(Math.Clamp(levels, -MaxSkill, MaxSkill)));

    public static string SkillXp(string heroId, string skillId, int xp) =>
        "coodsz.skill_xp " + Id(heroId) + " " + Word(skillId) + " " + N(Math.Clamp(xp, 1, MaxXp));

    public static string Focus(string heroId, string skillId, int points) =>
        "coodsz.focus " + Id(heroId) + " " + Word(skillId) + " " + N(NotZero(Math.Clamp(points, -10, 10)));

    public static string Attribute(string heroId, string attributeId, int points) =>
        "coodsz.attribute " + Id(heroId) + " " + Word(attributeId) + " " + N(NotZero(Math.Clamp(points, -10, 10)));

    public static string Unspent(string heroId, bool attribute, int points) =>
        "coodsz.unspent " + Id(heroId) + " " + (attribute ? "attribute" : "focus") + " " + N(NotZero(Math.Clamp(points, -50, 50)));

    /// <summary>Adds troops of one kind, or takes them with a negative count.</summary>
    public static string Troops(string heroId, string troopId, int count) =>
        "coodsz.troops " + Id(heroId) + " " + Id(troopId) + " " + N(NotZero(Math.Clamp(count, -MaxTroops, MaxTroops)));

    public static string HealTroops(string heroId) => "coodsz.heal_troops " + Id(heroId);
    public static string TroopXp(string heroId, int xp) => "coodsz.troop_xp " + Id(heroId) + " " + N(Math.Clamp(xp, 1, MaxXp));

    public static string GiveItem(string heroId, string itemId, int count) =>
        "coodsz.give_item " + Id(heroId) + " " + Id(itemId) + " " + N(Math.Clamp(count, 1, MaxItems));

    public static string TakeItem(string heroId, string itemId, int count) =>
        "coodsz.take_item " + Id(heroId) + " " + Id(itemId) + " " + N(Math.Clamp(count, 1, MaxItems));

    public static string Items() => "coodsz.items";
    public static string TroopTypes() => "coodsz.troop_types";

    /// <summary>Kingdoms, clans, lords, settlements and troop details for the Encyclopedia page (read only, written to a file).</summary>
    public static string World() => "coodsz.world";

    /// <summary>Kingdom colours, settlements and the player and lord parties with their map positions (read only, written to a file); <paramref name="partiesOnly"/> leaves the settlements out.</summary>
    public static string Map(bool partiesOnly = false) => partiesOnly ? "coodsz.map parties" : "coodsz.map";

    /// <summary>The real terrain of the campaign map, <paramref name="rows"/> rows from <paramref name="fromRow"/> of a grid <paramref name="columns"/> cells across (read only, written to a file).</summary>
    public static string Terrain(int columns, int fromRow, int rows) =>
        "coodsz.terrain " + N(Math.Clamp(columns, 16, 2000)) + " " + N(Math.Max(0, fromRow)) + " " + N(Math.Clamp(rows, 1, 4000));

    /// <summary>Is this one of the module's commands?</summary>
    public static bool IsToolsCommand(string? commandLine) =>
        !string.IsNullOrWhiteSpace(commandLine) && commandLine.TrimStart().StartsWith("coodsz.", StringComparison.Ordinal);

    private static string N(int value) => value.ToString(CultureInfo.InvariantCulture);

    private static int NotZero(int value) =>
        value != 0 ? value : throw new ArgumentException("Type a number that is not 0.", nameof(value));

    /// <summary>Ids have no spaces or quotes; one that does is a name, so no command is made.</summary>
    private static string Id(string? id)
    {
        var clean = ServerCommand.SanitizeArgument(id, 96);
        if (clean.Length == 0)
            throw new ArgumentException("The id is empty.", nameof(id));
        if (clean.Any(c => c is ' ' or '"' or '\'' or '|'))
            throw new ArgumentException("This is not an id: " + clean, nameof(id));
        return clean;
    }

    private static string Word(string? id) =>
        !string.IsNullOrEmpty(id) && id.All(char.IsAsciiLetter)
            ? id
            : throw new ArgumentException("This is not a skill or attribute id: " + id, nameof(id));
}
