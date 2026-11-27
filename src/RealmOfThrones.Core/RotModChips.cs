using BannerlordCoopLauncher.Core.Modules;
using TaomCoop.Core;

namespace RealmOfThrones.Core;

/// <summary>The player counts the tally (.github/scripts/tally-taom-compat.mjs --list rot) writes on a
/// Realm of Thrones list entry or a "community" entry.</summary>
public sealed class RotModCounts
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string WorkshopId { get; set; } = "";
    public int Works { get; set; }
    public int Broken { get; set; }
    public int ClientOnly { get; set; }

    internal static RotModCounts Clean(RotModCounts counts)
    {
        counts.Id = (counts.Id ?? "").Trim();
        counts.Name = (counts.Name ?? "").Trim();
        counts.WorkshopId = (counts.WorkshopId ?? "").Trim();
        return counts;
    }
}

/// <summary>The status chip of a mod row. Kind picks the color: Works green, Risky gold, Reported blue,
/// Broken red, Unknown grey. WorksChip and BrokenChip are the green and red count chips, or null.</summary>
public sealed record RotStatusChip(string Kind, string Text, string Tip)
{
    public string? WorksChip { get; init; }
    public string? BrokenChip { get; init; }
}

/// <summary>The status column of a mod row in both Realm of Thrones tools, the same chips as Coodsz Coop and
/// Friends: the list status chip and the count chips (then the Report link, which the window adds). Everything
/// comes from the Realm of Thrones list and the reports sent with tool rot, never from the Coop and Friends list.</summary>
public static class RotModChips
{
    // The chip kinds (colors, not screen text).
    public const string Works = nameof(Works);
    public const string Risky = nameof(Risky);
    public const string Reported = nameof(Reported);
    public const string Broken = nameof(Broken);
    public const string Unknown = nameof(Unknown);

    /// <summary>The tip of the grey Unknown, not tested badge. The shared tip names Test mods on the server, which the
    /// Realm of Thrones Server Tool does not have.</summary>
    public const string UnknownTip = "Nobody has reported this mod yet. Try it, then click Report to say how it worked.";

    /// <summary>True for a mod that gets a list status chip: not the Realm of Thrones core, Coop or an official module.</summary>
    public static bool IsExtraMod(string? moduleId)
    {
        var id = (moduleId ?? "").Trim();
        return UnknownModBadge.Applies(id) && !RotLoadOrder.IsCore(id);
    }

    /// <summary>True when the list has nothing to say about an extra mod, so the grey Unknown, not tested badge shows.</summary>
    public static bool ShowsUnknown(RotCompatList list, string? moduleId, string? workshopId, string? name)
    {
        if (!IsExtraMod(moduleId))
            return false;
        var entry = list.Find(moduleId, workshopId, name);
        return entry == null || RotCompatList.BadgeText(entry.Kind) == null;
    }

    /// <summary>The chip color of a list tag.</summary>
    public static string KindOf(CompatTag tag) => tag switch
    {
        CompatTag.Safe or CompatTag.Everyone => Works,
        CompatTag.Broken => Broken,
        CompatTag.Reported or CompatTag.Loads => Reported,
        CompatTag.Risky => Risky,
        _ => Unknown
    };

    /// <summary>The status chip of an extra mod, or null for the Realm of Thrones core, Coop and the official
    /// modules. A mod the list does not know is Unknown, not tested, or Unknown once players reported it (the
    /// count chips then say what they found). reportTip adds the line that says a click opens the report.</summary>
    public static RotStatusChip? Status(RotCompatList list, string? moduleId, string? workshopId, string? name, bool reportTip,
        Func<string, string>? translate = null)
    {
        if (!IsExtraMod(moduleId))
            return null;
        var entry = list.Find(moduleId, workshopId, name);
        var badge = entry == null ? null : RotCompatList.BadgeText(entry.Kind);
        var counts = list.CountsFor(moduleId, workshopId, name);
        var lines = new List<string>();
        string kind;
        string text;
        if (entry != null && badge != null)
        {
            kind = KindOf(entry.Kind);
            text = badge;
            lines.Add(RotCompatList.Hover(entry, translate));
        }
        else
        {
            kind = Unknown;
            text = counts == null ? UnknownModBadge.Text : "Unknown";
            if (counts == null)
                lines.Add(UnknownTip);
        }

        if (counts != null)
            lines.Add(ModReports.CountsLine(counts.Works, counts.Broken));
        if (reportTip)
            lines.Add(ModReports.ClickTip);
        return new RotStatusChip(kind, text, string.Join("\n", lines))
        {
            WorksChip = ModReports.WorksChip(counts?.Works ?? 0),
            BrokenChip = ModReports.BrokenChip(counts?.Broken ?? 0)
        };
    }

    /// <summary>True when a mod row offers the Report link: sending is set up (RotRelease.ReportsReady and the
    /// report endpoint) and the module is one players rate (not Coop or an official module).</summary>
    public static bool OffersReport(string? moduleId, string? endpoint = ModReports.Endpoint) =>
        RotRelease.ReportsReady && ModReports.HasEndpoint(endpoint) && ModReports.Rates(moduleId);

    /// <summary>The mods the server loads for a setup, in order, with their versions: every core module and the
    /// extras that load (loads, the staged ones), in the order of the rows on the mods page.</summary>
    public static IReadOnlyList<(string Id, string? Version)> ServerReportOrder(TaomSetup shown, Func<string, bool> loads, Func<string, string> versionOf) =>
        RotLoadOrder.ServerRows(shown)
            .Where(row => row.IsCore || (shown.Extras[row.ExtraIndex].Enabled && loads(row.ModuleId)))
            .Select(row => (row.ModuleId, (string?)versionOf(row.ModuleId)))
            .ToList();
}
