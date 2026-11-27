namespace BannerlordCoopLauncher.Core.Modules;

/// <summary>One mod row as the side Mods card counts it. Works is the tool's own community verdict for the mod.</summary>
public readonly record struct TallyRow(string Id, bool Enabled, bool AlwaysOn, bool Works = false);

/// <summary>
/// The numbers of the side Mods card (redesign D1): every mod that is on, in one group each. The groups follow the row
/// chips (ModChips.Role): Bundled first, then Coodsz module, then Always on, then Works from the community list. A mod in
/// none of them is Other, the grey end of the bar.
/// </summary>
public sealed record ModTally(int On, int AlwaysOn, int Coodsz, int Bundled, int Works)
{
    public static ModTally Empty { get; } = new(0, 0, 0, 0, 0);

    public int Other => Math.Max(0, On - AlwaysOn - Coodsz - Bundled - Works);

    public const string AlwaysOnChip = "Always on";
    public const string CoodszChip = "Coodsz module";
    public const string BundledChip = "Bundled";
    public const string WorksChip = "Works";

    /// <summary>The chips of the side Mods card in their order, each with its count. A group with no mods gets no chip
    /// (and no bar part), so plain TAOM shows Always on alone, not "Coodsz module 0" and "Works 0".</summary>
    public IReadOnlyList<(string Word, int Count)> Chips() =>
        new[] { (AlwaysOnChip, AlwaysOn), (CoodszChip, Coodsz), (BundledChip, Bundled), (WorksChip, Works) }
            .Where(chip => chip.Item2 > 0)
            .ToList();

    public static ModTally Count(IEnumerable<TallyRow> rows)
    {
        int on = 0, alwaysOn = 0, coodsz = 0, bundled = 0, works = 0;
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var row in rows)
        {
            var id = (row.Id ?? "").Trim();
            // A module listed twice (two copies in one setup) counts once.
            if (!row.Enabled || id.Length == 0 || !seen.Add(id))
                continue;
            on++;
            if (CoodszModuleIds.IsBundled(id))
                bundled++;
            else if (CoodszModuleIds.IsCoodsz(id))
                coodsz++;
            else if (row.AlwaysOn)
                alwaysOn++;
            else if (row.Works)
                works++;
        }

        return new ModTally(on, alwaysOn, coodsz, bundled, works);
    }
}
