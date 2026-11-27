namespace BannerlordCoopLauncher.Core.Keep;

/// <summary>The filter chips above the Modules list, after CoopKeep's Mods page.</summary>
public enum ModFilterKind
{
    All,
    Enabled,
    Disabled,
    Works,
    Broken,
    ClientOnly,
    Unknown,
}

/// <summary>
/// Which mod rows a chip shows. The community result is the badge the row already has (Works,
/// Broken, ClientOnly or Unknown, see ModCompatBadge); a row with no badge counts as Unknown.
/// </summary>
public static class ModFilter
{
    /// <summary>The chips in the order the page shows them.</summary>
    public static readonly IReadOnlyList<ModFilterKind> Chips =
    [
        ModFilterKind.All, ModFilterKind.Enabled, ModFilterKind.Disabled,
        ModFilterKind.Works, ModFilterKind.Broken, ModFilterKind.ClientOnly, ModFilterKind.Unknown,
    ];

    /// <summary>The chip's English caption.</summary>
    public static string Title(ModFilterKind kind) => kind switch
    {
        ModFilterKind.Enabled => "Enabled",
        ModFilterKind.Disabled => "Turned off",
        ModFilterKind.Works => "Community: works",
        ModFilterKind.Broken => "Community: broken",
        ModFilterKind.ClientOnly => "Community: client only",
        ModFilterKind.Unknown => "Community: unknown",
        _ => "All",
    };

    /// <summary>The community kind of a row: its badge kind, or Unknown without a badge.</summary>
    public static string CommunityKind(string? badgeKind) =>
        badgeKind is "Works" or "Broken" or "ClientOnly" ? badgeKind : "Unknown";

    public static bool Matches(ModFilterKind kind, bool enabled, string? badgeKind) => kind switch
    {
        ModFilterKind.Enabled => enabled,
        ModFilterKind.Disabled => !enabled,
        ModFilterKind.Works => CommunityKind(badgeKind) == "Works",
        ModFilterKind.Broken => CommunityKind(badgeKind) == "Broken",
        ModFilterKind.ClientOnly => CommunityKind(badgeKind) == "ClientOnly",
        ModFilterKind.Unknown => CommunityKind(badgeKind) == "Unknown",
        _ => true,
    };

    /// <summary>How many rows each chip shows.</summary>
    public static IReadOnlyDictionary<ModFilterKind, int> Counts(IEnumerable<(bool Enabled, string? BadgeKind)> rows)
    {
        var list = rows.ToList();
        return Chips.ToDictionary(kind => kind, kind => list.Count(row => Matches(kind, row.Enabled, row.BadgeKind)));
    }
}
