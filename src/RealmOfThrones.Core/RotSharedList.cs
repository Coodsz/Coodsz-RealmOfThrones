using BannerlordCoopLauncher.Core.Joining;
using BannerlordCoopLauncher.Core.Modules;
using BannerlordCoopLauncher.Core.Profile;
using TaomCoop.Core;

namespace RealmOfThrones.Core;

/// <summary>Which part of a list a module is: the pinned core (game modules, Coop, Realm of Thrones and
/// our compat module), a library mod (Harmony, ButterLib, UIExtenderEx, MCM) or an extra. Same values, in
/// the same order, as SharedModPart.</summary>
public enum RotModPart
{
    Core,
    Library,
    Extra
}

// Same values, in the same order, as SharedModProblemKind.
public enum RotVersionProblemKind
{
    /// <summary>On the host's list, not on this PC.</summary>
    Missing,
    /// <summary>On both, with another version here.</summary>
    Different,
    /// <summary>Loads on this PC, not on the host's list.</summary>
    NotOnHost
}

/// <summary>One difference between the host's list and this PC, with the line the launcher shows.</summary>
public sealed record RotVersionProblem(RotVersionProblemKind Kind, RotModPart Part, string ModuleId, string Name, string HostVersion, string ThisPcVersion, string Text);

/// <summary>
/// Share mod list and Import mod list for Realm of Thrones setups, with the same coodsz-mod-list.json
/// file the other tools use (SharedModList). The file lists the full load order, the Realm of Thrones
/// core included, so the other side can check its versions too. A list made for TAOM, Burning
/// Empires or Chaos in Calradia is refused: it belongs to another mod's tools.
///
/// The Coop nightly turns a join down on any difference in module ids or versions, both ways. So the
/// launcher compares the host's list with this PC before a join (VersionProblems and NotOnHost).
/// </summary>
public static class RotSharedList
{
    /// <summary>The entries to write for a setup, in load order. describe gives a module's name,
    /// version and folder when it is on this PC.</summary>
    public static IReadOnlyList<SharedModEntry> Entries(TaomSetup setup, Func<string, (string Name, string Version, string? Directory)?> describe)
    {
        // A repeated id (a hand edited settings file) keeps its first extra, as ClientOrder does.
        var extras = new Dictionary<string, TaomExtraMod>(StringComparer.OrdinalIgnoreCase);
        foreach (var mod in setup.Extras)
            extras.TryAdd(mod.ModuleId.Trim(), mod);
        var list = new List<SharedModEntry>();
        foreach (var id in RotLoadOrder.ClientOrder(setup))
        {
            var found = describe(id);
            extras.TryGetValue(id, out var extra);
            var library = RotStack.Libraries.FirstOrDefault(mod => mod.ModuleId.Equals(id, StringComparison.OrdinalIgnoreCase));
            var name = extra?.Label ?? found?.Name ?? (id == RotStack.CoopModuleId ? "Bannerlord Coop" : library?.Name ?? id);
            var workshop = id == RotStack.CoopModuleId ? RotStack.CoopWorkshopId
                : library != null ? library.WorkshopId
                : SharedModList.WorkshopIdFor(extra?.WorkshopId, found?.Directory);
            // The Coop entry carries the exact Coop build of the folder it was read from, as its Version too, so the other
            // side compares builds (InstalledVersion) and not only the v0.1.8 every nightly says.
            var build = id == RotStack.CoopModuleId ? CoopBuildId.FromModule(found?.Directory) : null;
            list.Add(new SharedModEntry(id, name, build ?? found?.Version ?? "", workshop, build));
        }

        return list;
    }

    /// <summary>
    /// The version of one module of the host's list on this PC, the one VersionProblems compares, or null when the
    /// module is not here. found is the module on this PC (its SubModule.xml version and folder). Coop is its exact
    /// build (Common.dll) when the list has a build there, the way Entries writes it. The compat module is the copy this
    /// launcher carries (carriedCompat, null when it carries none), since Connect puts that in before the game starts.
    /// </summary>
    public static string? InstalledVersion(SharedModEntry listed, (string Name, string Version, string? Directory)? found, string? carriedCompat)
    {
        var id = (listed.Id ?? "").Trim();
        if (carriedCompat != null && id.Equals(RotStack.CompatModuleId, StringComparison.OrdinalIgnoreCase))
            return carriedCompat;
        if (found is not { } mod)
            return null;
        if (id.Equals(RotStack.CoopModuleId, StringComparison.OrdinalIgnoreCase)
            && (!string.IsNullOrWhiteSpace(listed.CoopBuild) || CoopBuildId.Commit(listed.Version) != null))
            return CoopBuildId.FromModule(mod.Directory) ?? mod.Version;
        return mod.Version;
    }

    /// <summary>Why this list cannot be imported on a Realm of Thrones setup, or null when it can. A
    /// list with a TAOM or Burning Empires core module, or a Chaos in Calradia module, belongs to that mod.</summary>
    public static string? ForeignGame(IReadOnlyList<SharedModEntry> shared)
    {
        var other = shared.FirstOrDefault(entry => RotLoadOrder.OtherGame(entry.Id) != null);
        return other == null
            ? null
            : "This mod list is for " + RotLoadOrder.OtherGame(other.Id) + " (it has " + other.Id.Trim() + "), not Realm of Thrones. Ask the host for the list from the Realm of Thrones tools.";
    }

    /// <summary>
    /// Matches a setup to a shared list. Extra mods on the list are added (a mod not on this PC
    /// needs a Workshop link), turned on and put in the list's order. Extras on this setup that the
    /// list does not have are turned off, not removed, and go after the listed ones. Core modules are
    /// never added. The first RoT module marks which extras load before the group. The setup must be
    /// unlocked, and a list of another overhaul is refused before anything changes.
    /// </summary>
    public static TaomImportResult Import(TaomSetup setup, IReadOnlyList<SharedModEntry> shared, IReadOnlyList<FoundMod> onThisPc)
    {
        if (setup.Locked)
            throw new InvalidOperationException(setup.Name + " is locked. Make a new setup for the list.");
        var foreign = ForeignGame(shared);
        if (foreign != null)
            throw new InvalidOperationException(foreign);

        var before = setup.Clone();
        var added = new List<string>();
        var turnedOn = new List<string>();
        var turnedOff = new List<string>();
        var refused = new List<string>();
        var noLink = new List<string>();
        var listed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var wanted = new List<TaomExtraMod>();
        var beforeRot = shared.Any(entry => RotStack.IsRotModule(entry.Id.Trim()));
        foreach (var entry in shared)
        {
            var id = entry.Id.Trim();
            if (RotStack.IsRotModule(id))
                beforeRot = false;
            if (RotLoadOrder.IsCore(id) || !listed.Add(id))
                continue;
            var label = string.IsNullOrWhiteSpace(entry.Name) ? id : entry.Name.Trim();
            // Client add-ons such as the join password module come with the launcher.
            if (id.StartsWith("DedicatedServer.", StringComparison.OrdinalIgnoreCase))
                continue;
            if (RotLoadOrder.RefusalReason(id) != null)
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

            mod.Slot = beforeRot ? RotLoadOrder.SlotBeforeRot : RotLoadOrder.SlotAfterRot;
            wanted.Add(mod);
        }

        var others = setup.Extras.Where(mod => !listed.Contains(mod.ModuleId)).ToList();
        foreach (var mod in others.Where(mod => mod.Enabled))
        {
            mod.Enabled = false;
            turnedOff.Add(mod.Label);
        }

        setup.Extras = wanted.Concat(others).ToList();
        RotLoadOrder.Normalize(setup);

        var missingCore = RotStack.ClientModules
            .Where(id => !shared.Any(entry => entry.Id.Trim().Equals(id, StringComparison.OrdinalIgnoreCase)))
            .ToList();
        return new TaomImportResult(added, turnedOn, turnedOff, refused, noLink, missingCore, !before.SameMods(setup));
    }

    /// <summary>Enabled extras that are not on this PC. A Workshop one can be downloaded.</summary>
    public static IReadOnlyList<TaomExtraMod> Missing(TaomSetup setup, IEnumerable<string> installedIds) =>
        TaomSharedList.Missing(setup, installedIds);

    /// <summary>
    /// The extras the server needs that are not on this PC, for the Connect prompt: a Workshop mod is
    /// subscribed through Steam, one that comes with the launcher folder is copied, and the rest are
    /// got by hand.
    /// </summary>
    public static IReadOnlyList<ServerModNeed> ServerNeeds(TaomSetup setup, IEnumerable<string> installedIds, Func<string, bool> bundled) =>
        TaomSharedList.ServerNeeds(setup, installedIds, bundled);

    /// <summary>Which part of the Realm of Thrones list a module is.</summary>
    public static RotModPart PartOf(string moduleId) =>
        RotStack.IsLibrary(moduleId) ? RotModPart.Library
        : RotLoadOrder.IsCore(moduleId) ? RotModPart.Core
        : RotModPart.Extra;

    /// <summary>
    /// The modules of the host's list that are missing on this PC or have another version here, in the
    /// list's order: the library mods, the core and the extras alike (SharedModVersions.AgainstHost).
    /// installed maps each module id on this PC to its version. Client add-ons (DedicatedServer.*) come
    /// with the tools and are skipped.
    /// </summary>
    public static IReadOnlyList<RotVersionProblem> VersionProblems(IReadOnlyList<SharedModEntry> host, IReadOnlyDictionary<string, string> installed) =>
        SharedModVersions.AgainstHost(host, installed, Part, IsClientAddOn).Select(Rot).ToList();

    /// <summary>The modules this PC loads (active, in launch order) that the host's list does not have
    /// (SharedModVersions.NotOnHost). name gives a module's name when it is known. Client add-ons
    /// (DedicatedServer.*) are skipped.</summary>
    public static IReadOnlyList<RotVersionProblem> NotOnHost(IReadOnlyList<SharedModEntry> host, IEnumerable<string> active, Func<string, string?>? name = null) =>
        SharedModVersions.NotOnHost(host, active, Part, IsClientAddOn, name).Select(Rot).ToList();

    private static SharedModPart Part(string moduleId) => (SharedModPart)PartOf(moduleId);

    private static RotVersionProblem Rot(SharedModProblem problem) =>
        new((RotVersionProblemKind)problem.Kind, (RotModPart)problem.Part, problem.ModuleId, problem.Name, problem.HostVersion, problem.ThisPcVersion, problem.Text);

    // The pinned Coop's join check skips DedicatedServer.* ids (ModuleValidator.ShouldValidateModule), so the
    // compare leaves client add-ons such as the join password module out too.
    private static bool IsClientAddOn(string id) =>
        id.StartsWith("DedicatedServer.", StringComparison.OrdinalIgnoreCase);
}
