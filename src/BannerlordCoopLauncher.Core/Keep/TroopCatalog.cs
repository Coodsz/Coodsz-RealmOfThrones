namespace BannerlordCoopLauncher.Core.Keep;

public enum TroopClass { Infantry, Archer, Cavalry, HorseArcher }

/// <summary>The troop picker of the Cheats page Army tab: culture and class filters and a search.</summary>
public static class TroopCatalog
{
    public static string ClassName(TroopClass troopClass) => troopClass switch
    {
        TroopClass.Archer => "Archer",
        TroopClass.Cavalry => "Cavalry",
        TroopClass.HorseArcher => "Horse archer",
        _ => "Infantry",
    };

    /// <summary>The game's formation class folded into the four the game's party screen shows.</summary>
    public static TroopClass ClassOf(string formationClass, bool mounted, bool ranged) => formationClass switch
    {
        "Ranged" => TroopClass.Archer,
        "Cavalry" or "LightCavalry" or "HeavyCavalry" => TroopClass.Cavalry,
        "HorseArcher" => TroopClass.HorseArcher,
        "Infantry" or "HeavyInfantry" or "Skirmisher" => TroopClass.Infantry,
        _ => mounted ? ranged ? TroopClass.HorseArcher : TroopClass.Cavalry : ranged ? TroopClass.Archer : TroopClass.Infantry,
    };

    /// <summary>The cultures that have troops, by name.</summary>
    public static IReadOnlyList<(string Id, string Name)> Cultures(IEnumerable<TroopType> troops) =>
        troops.Where(t => t.CultureId.Length > 0)
            .GroupBy(t => t.CultureId, StringComparer.Ordinal)
            .Select(g => (g.Key, g.First().CultureName.Length > 0 ? g.First().CultureName : g.Key))
            .OrderBy(c => c.Item2, StringComparer.CurrentCultureIgnoreCase)
            .ToList();

    /// <summary>Troops of one culture and class (null for all) whose name contains the search, by culture, tier and name.</summary>
    public static IReadOnlyList<TroopType> Filter(IEnumerable<TroopType> troops, string? cultureId, TroopClass? troopClass, string? query)
    {
        var q = (query ?? "").Trim();
        return troops
            .Where(t => string.IsNullOrEmpty(cultureId) || t.CultureId == cultureId)
            .Where(t => troopClass == null || t.Class == troopClass)
            .Where(t => q.Length == 0 || t.Name.Contains(q, StringComparison.CurrentCultureIgnoreCase)
                        || t.CultureName.Contains(q, StringComparison.CurrentCultureIgnoreCase))
            .OrderBy(t => t.CultureName, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(t => t.Tier)
            .ThenBy(t => t.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    /// <summary>One star per tier, 1 to 6 (tier 0 recruits get one).</summary>
    public static string Stars(int tier) => new('★', Math.Clamp(tier, 1, 6));

    /// <summary>"200 troops · 3 wounded".</summary>
    public static string ArmyLine(int troops, int wounded) =>
        troops + " troops · " + wounded + " wounded";
}
