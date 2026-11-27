using BannerlordCoopLauncher.Core.Modules;
using BannerlordCoopLauncher.Core.Profile;

namespace TaomCoop.Core;

/// <summary>The four groups of the Mod list window.</summary>
public enum CompatGroup
{
    TestedInCoop,
    LoadsOnServer,
    WorksInTaom,
    RiskyOrBroken
}

/// <summary>
/// What the client launcher's Mod list window shows: the TAOM compatibility list in four groups
/// (tested in Coop, loads on the server, works in TAOM but not tested in Coop, risky and broken),
/// filtered by a search box, with a Workshop or Nexus link per mod and the mod to add to a setup.
/// </summary>
public static class TaomModList
{
    public const string WorkshopPage = "https://steamcommunity.com/sharedfiles/filedetails/?id=";
    public const string NexusPage = "https://www.nexusmods.com/mountandblade2bannerlord/mods/";

    public static IReadOnlyList<CompatGroup> Order { get; } =
        [CompatGroup.TestedInCoop, CompatGroup.LoadsOnServer, CompatGroup.WorksInTaom, CompatGroup.RiskyOrBroken];

    public static string Title(CompatGroup group) => group switch
    {
        CompatGroup.TestedInCoop => "Tested in Coop",
        CompatGroup.LoadsOnServer => "Loads on the server",
        CompatGroup.WorksInTaom => "Works in TAOM, not tested in Coop",
        _ => "Risky and Broken"
    };

    /// <summary>Risky and broken first (whoever says so), then by source.</summary>
    public static CompatGroup GroupOf(CompatEntry entry)
    {
        if (entry.Kind is CompatTag.Risky or CompatTag.Broken)
            return CompatGroup.RiskyOrBroken;
        var source = entry.Source.Trim();
        if (source.Equals(CompatSource.CoodszCoop, StringComparison.OrdinalIgnoreCase))
            return CompatGroup.TestedInCoop;
        if (source.Equals(CompatSource.ServerLoads, StringComparison.OrdinalIgnoreCase) || entry.Kind == CompatTag.Loads)
            return CompatGroup.LoadsOnServer;
        return CompatGroup.WorksInTaom;
    }

    /// <summary>True when every word of the search is in the mod's name, id, note, source or Workshop id.</summary>
    public static bool Matches(CompatEntry entry, string? search)
    {
        if (string.IsNullOrWhiteSpace(search))
            return true;
        var text = string.Join(" ", entry.Name, entry.Id, entry.Note, CompatSource.Label(entry.Source), entry.WorkshopId, entry.NexusId,
            TaomCompatList.BadgeText(entry.Kind) ?? "");
        return search.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .All(word => text.Contains(word, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>The groups in window order, each sorted by name. Empty groups are kept so the window
    /// can say a group has nothing for this search.</summary>
    public static IReadOnlyList<(CompatGroup Group, IReadOnlyList<CompatEntry> Mods)> Groups(TaomCompatList list, string? search = null) =>
        Order.Select(group => (group, (IReadOnlyList<CompatEntry>)list.Mods
                .Where(entry => GroupOf(entry) == group && Matches(entry, search))
                .OrderBy(entry => entry.Label, StringComparer.OrdinalIgnoreCase)
                .ToList()))
            .ToList();

    /// <summary>The Steam Workshop page, else the Nexus Mods page, else null.</summary>
    public static string? Link(CompatEntry entry)
    {
        if (SharedModList.IsWorkshopId(entry.WorkshopId))
            return WorkshopPage + entry.WorkshopId.Trim();
        var nexus = entry.NexusId.Trim();
        return nexus.Length > 0 && nexus.All(char.IsDigit) ? NexusPage + nexus : null;
    }

    public static string? LinkText(CompatEntry entry) =>
        SharedModList.IsWorkshopId(entry.WorkshopId) ? "Workshop" : Link(entry) != null ? "Nexus" : null;

    /// <summary>The copy of this mod on this PC: by module id, then Workshop id, then name.</summary>
    public static FoundMod? OnThisPc(CompatEntry entry, IEnumerable<FoundMod> mods)
    {
        var list = mods as IReadOnlyList<FoundMod> ?? mods.ToList();
        if (entry.Id.Length > 0)
        {
            var byId = list.FirstOrDefault(mod => mod.ModuleId.Equals(entry.Id, StringComparison.OrdinalIgnoreCase));
            if (byId != null)
                return byId;
        }

        if (SharedModList.IsWorkshopId(entry.WorkshopId))
        {
            var byWorkshop = list.FirstOrDefault(mod => mod.WorkshopId == entry.WorkshopId.Trim());
            if (byWorkshop != null)
                return byWorkshop;
        }

        return entry.Name.Length == 0
            ? null
            : list.FirstOrDefault(mod => mod.Name.Equals(entry.Name, StringComparison.OrdinalIgnoreCase) || mod.ModuleId.Equals(entry.Name, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// What Add to setup puts on the setup: the copy on this PC when there is one, else the list's
    /// module id with its Workshop id (Install mods then downloads it). Null with a reason when the
    /// mod cannot be added yet: the list does not know its module id and it is not on this PC.
    /// </summary>
    public static (string Name, string ModuleId, string WorkshopId)? AddTarget(CompatEntry entry, FoundMod? found, out string? reason)
    {
        reason = null;
        if (found != null)
            return (found.Name, found.ModuleId, found.WorkshopId);
        if (entry.Id.Length > 0)
            return (entry.Label, entry.Id, SharedModList.IsWorkshopId(entry.WorkshopId) ? entry.WorkshopId.Trim() : "0");
        reason = SharedModList.IsWorkshopId(entry.WorkshopId)
            ? entry.Label + " is not on this PC yet. Subscribe on its Workshop page, wait for Steam, then click Add to setup again."
            : entry.Label + " is not on this PC yet. Get it from its Nexus page or ModDB, put the folder in the game's Modules folder, then click Add to setup again.";
        return null;
    }

    /// <summary>The line Add to setup shows for a locked setup.</summary>
    public static string LockedText(string setupName) =>
        setupName + " is locked. Click Copy first to make a setup you can add mods to.";
}
