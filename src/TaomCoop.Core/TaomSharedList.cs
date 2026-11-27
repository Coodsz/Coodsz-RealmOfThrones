using BannerlordCoopLauncher.Core.Joining;
using BannerlordCoopLauncher.Core.Modules;
using BannerlordCoopLauncher.Core.Profile;

namespace TaomCoop.Core;

public sealed record TaomImportResult(
    IReadOnlyList<string> Added,
    IReadOnlyList<string> TurnedOn,
    IReadOnlyList<string> TurnedOff,
    IReadOnlyList<string> Refused,
    IReadOnlyList<string> NoLink,
    IReadOnlyList<string> MissingCore,
    bool Changed);

/// <summary>
/// Share mod list and Import mod list for TAOM setups, with the same coodsz-mod-list.json file
/// Coop and Friends and Coodsz Host use (SharedModList). The file lists the full load order,
/// the TAOM core included, so the other side can check its core versions too.
/// </summary>
public static class TaomSharedList
{
    /// <summary>The entries to write for a setup, in load order. describe gives a module's name,
    /// version and folder when it is on this PC.</summary>
    public static IReadOnlyList<SharedModEntry> Entries(TaomSetup setup, Func<string, (string Name, string Version, string? Directory)?> describe,
        IReadOnlyList<string>? order = null)
    {
        var extras = setup.Extras.ToDictionary(mod => mod.ModuleId, StringComparer.OrdinalIgnoreCase);
        var list = new List<SharedModEntry>();
        foreach (var id in order ?? TaomLoadOrder.ClientOrder(setup))
        {
            var found = describe(id);
            extras.TryGetValue(id, out var extra);
            var name = extra?.Label ?? found?.Name ?? (id == TaomStack.CoopModuleId ? "Bannerlord Coop" : id);
            var workshop = id == TaomStack.CoopModuleId
                ? TaomStack.CoopWorkshopId
                : SharedModList.WorkshopIdFor(extra?.WorkshopId, found?.Directory);
            list.Add(new SharedModEntry(id, name, found?.Version ?? "", workshop));
        }

        return list;
    }

    /// <summary>
    /// Matches a setup to a shared list. Extra mods on the list are added (a mod not on this PC
    /// needs a Workshop link), turned on and put in the list's order; an extra that is listed
    /// before TAOM loads before TAOM. Extras on this setup that the list does not have are
    /// turned off, not removed. Core modules are never added or moved; the TAOM core stays as
    /// it is. The setup must be unlocked.
    /// </summary>
    public static TaomImportResult Import(TaomSetup setup, IReadOnlyList<SharedModEntry> shared, IReadOnlyList<FoundMod> onThisPc,
        bool line157 = false, bool taom157Exact = false)
    {
        if (setup.Locked)
            throw new InvalidOperationException(setup.Name + " is locked. Make a new setup for the list.");

        var before = setup.Clone();
        if (line157)
        {
            var requirements = shared.Where(entry => Taom157Join.IsOptional(entry.Id))
                .DistinctBy(entry => entry.Id, StringComparer.OrdinalIgnoreCase)
                .Select(entry => new Taom157ModuleRequirement(entry.Id, entry.Version)).ToList();
            // Older hosts omitted the aliases from Share even when the server loaded them.
            setup.Line157Requirements = taom157Exact || requirements.Count > 0 ? requirements : null;
            setup.Line157Exact = taom157Exact;
        }
        var added = new List<string>();
        var turnedOn = new List<string>();
        var turnedOff = new List<string>();
        var refused = new List<string>();
        var noLink = new List<string>();
        var taomAt = -1;
        for (var i = 0; i < shared.Count; i++)
        {
            if (shared[i].Id.Equals(TaomLoadOrder.FirstTaomModule, StringComparison.OrdinalIgnoreCase))
            {
                taomAt = i;
                break;
            }
        }

        var listed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var wantedBefore = new List<TaomExtraMod>();
        var wantedAfter = new List<TaomExtraMod>();
        for (var i = 0; i < shared.Count; i++)
        {
            var entry = shared[i];
            var id = entry.Id.Trim();
            if (TaomLoadOrder.IsCore(id) || (line157 && Taom157Join.IsOptional(id)) || !listed.Add(id))
                continue;
            var label = string.IsNullOrWhiteSpace(entry.Name) ? id : entry.Name.Trim();
            // Client add-ons such as the join password module come with the launcher.
            if (id.StartsWith("DedicatedServer.", StringComparison.OrdinalIgnoreCase))
                continue;
            if (TaomLoadOrder.RefusalReason(id) != null)
            {
                refused.Add(label);
                continue;
            }

            var mod = setup.Extras.FirstOrDefault(item => item.ModuleId.Equals(id, StringComparison.OrdinalIgnoreCase));
            if (mod == null)
            {
                var found = onThisPc.FirstOrDefault(item => item.ModuleId.Equals(id, StringComparison.OrdinalIgnoreCase));
                var workshop = found != null && SharedModList.IsWorkshopId(found.WorkshopId)
                    ? found.WorkshopId
                    : SharedModList.IsWorkshopId(entry.WorkshopId) ? entry.WorkshopId.Trim() : "0";
                if (found == null && workshop == "0")
                {
                    noLink.Add(label);
                    continue;
                }

                mod = new TaomExtraMod { Name = found?.Name ?? label, ModuleId = id, WorkshopId = workshop, Enabled = true };
                added.Add(mod.Label);
            }
            else if (!mod.Enabled)
            {
                mod.Enabled = true;
                turnedOn.Add(mod.Label);
            }

            mod.Slot = taomAt >= 0 && i < taomAt ? ExtraSlot.BeforeTaom : ExtraSlot.AfterTaom;
            (mod.Slot == ExtraSlot.BeforeTaom ? wantedBefore : wantedAfter).Add(mod);
        }

        var others = setup.Extras.Where(mod => !listed.Contains(mod.ModuleId)).ToList();
        foreach (var mod in others.Where(mod => mod.Enabled))
        {
            mod.Enabled = false;
            turnedOff.Add(mod.Label);
        }

        setup.Extras = wantedBefore
            .Concat(others.Where(mod => mod.Slot == ExtraSlot.BeforeTaom))
            .Concat(wantedAfter)
            .Concat(others.Where(mod => mod.Slot != ExtraSlot.BeforeTaom))
            .ToList();
        TaomLoadOrder.Normalize(setup);

        var missingCore = TaomStack.ClientModules
            .Where(id => !shared.Any(entry => entry.Id.Equals(id, StringComparison.OrdinalIgnoreCase)))
            .ToList();
        return new TaomImportResult(added, turnedOn, turnedOff, refused, noLink, missingCore, !before.SameMods(setup));
    }

    /// <summary>Enabled extras that are not on this PC. A Workshop one can be downloaded.</summary>
    public static IReadOnlyList<TaomExtraMod> Missing(TaomSetup setup, IEnumerable<string> installedIds)
    {
        var have = new HashSet<string>(installedIds, StringComparer.OrdinalIgnoreCase);
        return setup.EnabledExtras.Where(mod => !have.Contains(mod.ModuleId)).ToList();
    }

    /// <summary>
    /// The extras the server needs that are not on this PC, for the Connect prompt: a Workshop mod is
    /// subscribed through Steam, one that comes with the launcher folder is copied, and the rest are
    /// got by hand. The TAOM core is not here; Install or repair TAOM puts it in.
    /// </summary>
    public static IReadOnlyList<ServerModNeed> ServerNeeds(TaomSetup setup, IEnumerable<string> installedIds, Func<string, bool> bundled)
    {
        var needs = new List<ServerModNeed>();
        foreach (var mod in Missing(setup, installedIds))
        {
            var fetch = SharedModList.IsWorkshopId(mod.WorkshopId) ? ServerModFetch.Workshop
                : bundled(mod.ModuleId) ? ServerModFetch.Launcher
                : ServerModFetch.ByHand;
            var link = fetch == ServerModFetch.Workshop ? ServerModNeeds.WorkshopPage(mod.WorkshopId) : null;
            needs.Add(new ServerModNeed(mod.ModuleId, mod.Label, ServerModProblem.Missing, fetch, link));
        }

        return needs;
    }
}
