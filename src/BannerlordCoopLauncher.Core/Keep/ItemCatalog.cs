// Ported from CoopKeep (https://github.com/Aderimo/bannerlord-coopkeep),
// src/CoopKeep.Core/Installation/ServerItemCatalog.cs and src/CoopKeep.App/ViewModels/MainViewModel.Items.cs
// (groups, filter, search score and sort), translated from Turkish and changed for Coodsz Host.

namespace BannerlordCoopLauncher.Core.Keep;

/// <summary>The category sidebar of the Items window, in its order.</summary>
public enum ItemGroup
{
    Sword, Dagger, Axe, Mace, Polearm, Thrown, Bow, Crossbow, Ammo, Shield,
    Head, Body, Legs, Hands, Cape,
    Horse, Harness,
    Food, Goods,
    Other,
}

public enum ItemSort { Cheapest, MostExpensive, Tier, Name }

public enum ItemWear { All, Civilian, Battle }

/// <summary>An item of <c>coodsz.items</c>.</summary>
public sealed record GameItem(
    string Id, string Name, string Type, int Value, int Tier, string CultureId, string CultureName,
    bool Civilian, bool Food, bool TradeGood, string WeaponClass, double Weight)
{
    public ItemGroup Group => ItemCatalog.GroupOf(this);
}

/// <summary>What the Items window shows for one choice of category, search, sort and wear filter.</summary>
public sealed record ItemPage(IReadOnlyList<GameItem> Shown, int Total)
{
    public bool Cut => Total > Shown.Count;
}

public static class ItemCatalog
{
    /// <summary>More cards than this make the window slow; the rest are reached by searching.</summary>
    public const int MaxShown = 240;

    public static readonly IReadOnlyList<ItemGroup> Order = Enum.GetValues<ItemGroup>();

    /// <summary>The sidebar names.</summary>
    public static string GroupName(ItemGroup group) => group switch
    {
        ItemGroup.Sword => "Swords",
        ItemGroup.Dagger => "Daggers",
        ItemGroup.Axe => "Axes",
        ItemGroup.Mace => "Maces",
        ItemGroup.Polearm => "Spears and polearms",
        ItemGroup.Thrown => "Throwing weapons",
        ItemGroup.Bow => "Bows and slings",
        ItemGroup.Crossbow => "Crossbows",
        ItemGroup.Ammo => "Arrows and bolts",
        ItemGroup.Shield => "Shields",
        ItemGroup.Head => "Head",
        ItemGroup.Body => "Body armor and clothes",
        ItemGroup.Legs => "Boots and legs",
        ItemGroup.Hands => "Gloves",
        ItemGroup.Cape => "Capes",
        ItemGroup.Horse => "Horses and camels",
        ItemGroup.Harness => "Harness and saddles",
        ItemGroup.Food => "Food",
        ItemGroup.Goods => "Materials and trade goods",
        _ => "Other",
    };

    /// <summary>The item's category, from its type and weapon class.</summary>
    public static ItemGroup GroupOf(GameItem i)
    {
        if (i.Food) return ItemGroup.Food;

        return i.Type switch
        {
            "Goods" or "Animal" => ItemGroup.Goods,
            "HeadArmor" => ItemGroup.Head,
            "BodyArmor" or "ChestArmor" => ItemGroup.Body,
            "LegArmor" => ItemGroup.Legs,
            "HandArmor" => ItemGroup.Hands,
            "Cape" => ItemGroup.Cape,
            "Horse" => ItemGroup.Horse,
            "HorseHarness" => ItemGroup.Harness,
            "Shield" => ItemGroup.Shield,
            "Bow" or "Sling" => ItemGroup.Bow,
            "Crossbow" => ItemGroup.Crossbow,
            "Arrows" or "Bolts" or "SlingStones" or "Bullets" => ItemGroup.Ammo,
            "Thrown" => ItemGroup.Thrown,
            _ => i.WeaponClass switch
            {
                "OneHandedSword" or "TwoHandedSword" => ItemGroup.Sword,
                "Dagger" => ItemGroup.Dagger,
                "OneHandedAxe" or "TwoHandedAxe" => ItemGroup.Axe,
                "Mace" or "TwoHandedMace" or "Pick" => ItemGroup.Mace,
                "OneHandedPolearm" or "TwoHandedPolearm" or "LowGripPolearm" => ItemGroup.Polearm,
                "Javelin" or "ThrowingAxe" or "ThrowingKnife" or "Stone" => ItemGroup.Thrown,
                _ => i.TradeGood ? ItemGroup.Goods : ItemGroup.Other,
            },
        };
    }

    /// <summary>Banners, tournament gear, dummies and the trash item are not something to give.</summary>
    public static IReadOnlyList<GameItem> Giveable(IEnumerable<GameItem> items) =>
        items.Where(i => i.Name.Length > 0 && i.Type is not ("Banner" or "Invalid")
                         && !i.Id.Contains("tournament", StringComparison.Ordinal)
                         && !i.Name.StartsWith("Dummy", StringComparison.OrdinalIgnoreCase)
                         && !i.Id.StartsWith("dummy", StringComparison.OrdinalIgnoreCase)
                         && i.Id != "trash_item" && !i.Name.Equals("Trash Item", StringComparison.OrdinalIgnoreCase))
            .GroupBy(i => i.Id, StringComparer.Ordinal)
            .Select(g => g.First())
            .ToList();

    /// <summary>How many items each category has (categories with none are left out).</summary>
    public static IReadOnlyList<(ItemGroup Group, int Count)> Counts(IEnumerable<GameItem> items)
    {
        var counts = items.GroupBy(i => i.Group).ToDictionary(g => g.Key, g => g.Count());
        return Order.Where(counts.ContainsKey).Select(g => (g, counts[g])).ToList();
    }

    /// <summary>
    /// The cards to show: one category (or all), a search over name, category and culture (best
    /// match first), the wear filter, then the sort, cut at <see cref="MaxShown"/>.
    /// </summary>
    public static ItemPage Filter(IEnumerable<GameItem> items, ItemGroup? group, string? query, ItemSort sort, ItemWear wear)
    {
        var q = (query ?? "").Trim();
        IEnumerable<GameItem> list = items;
        if (group is { } g)
            list = list.Where(i => i.Group == g);
        if (wear != ItemWear.All)
            list = list.Where(i => i.Civilian == (wear == ItemWear.Civilian));

        if (q.Length > 0)
        {
            list = list.Select(i => (i, score: Score(i, q)))
                .Where(x => x.score > 0)
                .OrderByDescending(x => x.score)
                .ThenBy(x => x.i.Value)
                .Select(x => x.i);
        }
        else
        {
            list = sort switch
            {
                ItemSort.MostExpensive => list.OrderByDescending(i => i.Value).ThenBy(i => i.Name, StringComparer.CurrentCultureIgnoreCase),
                ItemSort.Name => list.OrderBy(i => i.Name, StringComparer.CurrentCultureIgnoreCase),
                ItemSort.Tier => list.OrderByDescending(i => i.Tier).ThenBy(i => i.Value),
                _ => list.OrderBy(i => i.Value).ThenBy(i => i.Name, StringComparer.CurrentCultureIgnoreCase),
            };
        }

        var all = list.ToList();
        return new ItemPage(all.Take(MaxShown).ToList(), all.Count);
    }

    private static int Score(GameItem i, string q)
    {
        if (i.Name.Equals(q, StringComparison.CurrentCultureIgnoreCase)) return 100;
        if (i.Name.StartsWith(q, StringComparison.CurrentCultureIgnoreCase)) return 80;
        if (i.Name.Contains(q, StringComparison.CurrentCultureIgnoreCase)) return 60;
        if (GroupName(i.Group).Contains(q, StringComparison.CurrentCultureIgnoreCase) || i.Type.Contains(q, StringComparison.OrdinalIgnoreCase)) return 30;
        if (i.CultureName.Contains(q, StringComparison.CurrentCultureIgnoreCase) || i.CultureId.Contains(q, StringComparison.OrdinalIgnoreCase)) return 20;
        return 0;
    }

    /// <summary>The line above the cards.</summary>
    public static string ResultText(ItemPage page) =>
        page.Cut
            ? "First " + page.Shown.Count + " of " + page.Total + "; narrow the search"
            : page.Total + " items";

    /// <summary>"Neutral" for items with no culture, like the game's encyclopedia.</summary>
    public static string CultureLabel(GameItem item) => item.CultureName.Length > 0 ? item.CultureName : "Neutral";
}
