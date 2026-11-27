using BannerlordCoopLauncher.Core.Modules;
using BannerlordCoopLauncher.Core.Profile;

namespace TaomCoop.Core;

/// <summary>
/// A setup names each extra by module id and Workshop id. When the mod on this PC has another
/// module id than the setup says (a built in list written before the mod was seen, or a mod that
/// changed its id), the installed mod with the same Workshop id is used, so launching, the checks
/// and the server list all use the id the game knows.
/// </summary>
public static class TaomModIds
{
    /// <summary>A copy of the setup with each extra's module id taken from the installed mod with
    /// the same Workshop id when its own id is not installed. The setup itself is not changed.</summary>
    public static TaomSetup Resolve(TaomSetup setup, IEnumerable<FoundMod> installed)
    {
        var mods = installed.ToList();
        var ids = new HashSet<string>(mods.Select(mod => mod.ModuleId), StringComparer.OrdinalIgnoreCase);
        var copy = setup.Clone();
        var used = new HashSet<string>(copy.Extras.Select(mod => mod.ModuleId), StringComparer.OrdinalIgnoreCase);
        foreach (var extra in copy.Extras)
        {
            if (ids.Contains(extra.ModuleId) || !SharedModList.IsWorkshopId(extra.WorkshopId))
                continue;
            var match = mods.FirstOrDefault(mod => mod.WorkshopId == extra.WorkshopId.Trim()
                && TaomLoadOrder.RefusalReason(mod.ModuleId) == null
                && !used.Contains(mod.ModuleId));
            if (match == null)
                continue;
            used.Add(match.ModuleId);
            extra.ModuleId = match.ModuleId;
        }

        return copy;
    }
}
