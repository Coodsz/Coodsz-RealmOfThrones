using BannerlordCoopLauncher.Core.Parsing;

namespace TaomCoop.Core;

/// <summary>What this PC knows about one module id: its SubModule.xml, every folder it is in,
/// and the shared libraries it brings in its own bin folder.</summary>
public sealed record TaomModFact(string Id, ModuleManifest? Manifest, IReadOnlyList<string> Copies, IReadOnlyList<string> BundledLibs,
    BannerlordCoopLauncher.Core.Modules.DependencyFixState Fix = BannerlordCoopLauncher.Core.Modules.DependencyFixState.NotNeeded,
    int BlockedDlls = 0);

public sealed record TaomModIssue(bool Blocks, string Text);

public static class TaomModFacts
{
    // The libraries TAOM.Dependencies already ships (see its SubModule.xml).
    public static readonly IReadOnlyDictionary<string, string> SharedLibraries = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        ["0Harmony.dll"] = "Harmony",
        ["Bannerlord.ButterLib.dll"] = "ButterLib",
        ["Bannerlord.UIExtenderEx.dll"] = "UIExtenderEx",
        ["MCMv5.dll"] = "MCM"
    };

    /// <summary>Every module folder under these roots (Workshop content folders, the game's Modules
    /// folder), by module id. A mod in two places is a duplicate copy.</summary>
    public static IReadOnlyDictionary<string, TaomModFact> Gather(IEnumerable<string> roots)
    {
        var copies = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        var manifests = new Dictionary<string, ModuleManifest>(StringComparer.OrdinalIgnoreCase);
        var libs = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        foreach (var root in roots.Where(root => !string.IsNullOrWhiteSpace(root)).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (!Directory.Exists(root))
                continue;
            IEnumerable<string> children;
            try
            {
                children = Directory.EnumerateDirectories(root).ToList();
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                continue;
            }

            foreach (var child in children)
            {
                foreach (var module in FindModules(child, 0))
                {
                    var manifest = ModuleManifestReader.Read(module);
                    if (manifest == null)
                        continue;
                    if (!copies.TryGetValue(manifest.Id, out var list))
                        copies[manifest.Id] = list = new List<string>();
                    if (!list.Contains(module, StringComparer.OrdinalIgnoreCase))
                        list.Add(module);
                    manifests.TryAdd(manifest.Id, manifest);
                    if (!libs.ContainsKey(manifest.Id))
                        libs[manifest.Id] = BundledLibsIn(module);
                }
            }
        }

        return copies.ToDictionary(
            pair => pair.Key,
            pair => new TaomModFact(pair.Key, manifests.GetValueOrDefault(pair.Key), pair.Value, libs.GetValueOrDefault(pair.Key) ?? new List<string>(),
                pair.Value.Select(BannerlordCoopLauncher.Core.Modules.TaomDependencyFix.State).OrderByDescending(state => state).First(),
                pair.Value.Sum(folder => BannerlordCoopLauncher.Core.Support.ModUnblock.BlockedDlls(folder).Count)),
            StringComparer.OrdinalIgnoreCase);
    }

    public static List<string> BundledLibsIn(string moduleDirectory)
    {
        var found = new List<string>();
        foreach (var bin in new[] { "Win64_Shipping_Client", "Win64_Shipping_Server" })
        {
            var folder = Path.Combine(moduleDirectory, "bin", bin);
            if (!Directory.Exists(folder))
                continue;
            foreach (var (dll, name) in SharedLibraries)
            {
                if (File.Exists(Path.Combine(folder, dll)) && !found.Contains(name))
                    found.Add(name);
            }
        }

        return found;
    }

    private static IEnumerable<string> FindModules(string directory, int depth)
    {
        if (depth > 3 || !Directory.Exists(directory))
            yield break;
        if (File.Exists(Path.Combine(directory, "SubModule.xml")))
        {
            yield return directory;
            yield break;
        }

        string[] children;
        try
        {
            children = Directory.GetDirectories(directory);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            yield break;
        }

        foreach (var child in children)
        {
            foreach (var nested in FindModules(child, depth + 1))
                yield return nested;
        }
    }
}

/// <summary>
/// Auto sort for the extra mods of a TAOM setup, from each mod's SubModule.xml and the TAOM rules.
/// The core never moves. A mod that TAOM must load after goes before TAOM; a mod that needs a TAOM
/// module goes after TAOM; a mod TAOM.CoopCompat wants before it goes right before Coop; every
/// other mod keeps its place. Inside each place, a mod loads after the mods it needs and before the
/// mods that ask to load after it. Mods that ask for each other first keep their old order.
/// </summary>
public static class TaomAutoSort
{
    private static readonly HashSet<string> TaomModules = new(TaomLoadOrder.TaomBlock, StringComparer.OrdinalIgnoreCase);

    private static int Rank(ExtraSlot slot) => slot switch
    {
        ExtraSlot.BeforeTaom => 0,
        ExtraSlot.BeforeCoop => 1,
        _ => 2
    };

    private static ExtraSlot SlotOf(int rank, string id) => rank switch
    {
        0 => ExtraSlot.BeforeTaom,
        1 when TaomLoadOrder.MayLoadBeforeCoop(id) => ExtraSlot.BeforeCoop,
        _ => ExtraSlot.AfterTaom
    };

    /// <summary>Sorts the setup in place. Returns plain lines that say what changed.</summary>
    public static IReadOnlyList<string> Sort(TaomSetup setup, IReadOnlyDictionary<string, ModuleManifest> manifests)
    {
        if (setup.Locked)
            throw new InvalidOperationException(setup.Name + " is locked. Click New setup or Copy to make a setup you can add mods to.");
        TaomLoadOrder.Normalize(setup);
        var lines = new List<string>();
        var before = setup.Extras.Select(mod => mod.ModuleId).ToList();
        var oldSlots = setup.Extras.ToDictionary(mod => mod.ModuleId, mod => mod.Slot, StringComparer.OrdinalIgnoreCase);
        var ids = new HashSet<string>(setup.Extras.Select(mod => mod.ModuleId), StringComparer.OrdinalIgnoreCase);
        ModuleManifest? M(string id) => manifests.TryGetValue(id, out var manifest) ? manifest : null;
        IEnumerable<string> Needs(string id) => M(id)?.Needs.Select(link => link.Id) ?? Enumerable.Empty<string>();
        IEnumerable<string> LoadsBefore(string id) => M(id)?.LoadsBefore ?? (IEnumerable<string>)Array.Empty<string>();

        // TAOM modules that name an extra as LoadBeforeThis want it loaded before them.
        var wantedBeforeTaom = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var core in TaomModules)
        {
            foreach (var need in Needs(core))
                wantedBeforeTaom.Add(need);
        }

        var rank = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var mod in setup.Extras)
        {
            var id = mod.ModuleId;
            if (TaomLoadOrder.LoadsLast.Contains(id))
                rank[id] = 2;
            else if (TaomLoadOrder.MayLoadBeforeCoop(id))
                rank[id] = 1;
            else if (wantedBeforeTaom.Contains(id) || LoadsBefore(id).Any(TaomModules.Contains))
                rank[id] = 0;
            else
                // The TAOM developer: every extra mod goes below all TAOM files.
                rank[id] = 2;
        }

        // A mod loads no earlier than the place of the mods it needs, and a mod that asks to
        // load before another pulls that one no earlier than its own place.
        for (var pass = 0; pass < ids.Count + 1; pass++)
        {
            var changed = false;
            foreach (var id in ids)
            {
                foreach (var need in Needs(id).Where(ids.Contains))
                {
                    if (rank[need] > rank[id])
                    {
                        rank[id] = Effective(rank[need], id);
                        changed = true;
                    }
                }

                foreach (var later in LoadsBefore(id).Where(ids.Contains))
                {
                    if (rank[later] < rank[id])
                    {
                        rank[later] = Effective(rank[id], later);
                        changed = true;
                    }
                }
            }

            if (!changed)
                break;
        }

        var index = before.Select((id, i) => (id, i)).ToDictionary(item => item.id, item => item.i, StringComparer.OrdinalIgnoreCase);
        var sorted = new List<TaomExtraMod>();
        var cycles = new List<string>();
        foreach (var slotRank in new[] { 0, 1, 2 })
        {
            var group = setup.Extras.Where(mod => rank[mod.ModuleId] == slotRank).ToList();
            var groupIds = new HashSet<string>(group.Select(mod => mod.ModuleId), StringComparer.OrdinalIgnoreCase);
            var incoming = group.ToDictionary(mod => mod.ModuleId, _ => 0, StringComparer.OrdinalIgnoreCase);
            var next = group.ToDictionary(mod => mod.ModuleId, _ => new List<string>(), StringComparer.OrdinalIgnoreCase);
            void Edge(string first, string second)
            {
                if (first.Equals(second, StringComparison.OrdinalIgnoreCase) || next[first].Contains(second, StringComparer.OrdinalIgnoreCase))
                    return;
                next[first].Add(second);
                incoming[second]++;
            }

            foreach (var mod in group)
            {
                foreach (var need in Needs(mod.ModuleId).Where(groupIds.Contains))
                    Edge(need, mod.ModuleId);
                foreach (var later in LoadsBefore(mod.ModuleId).Where(groupIds.Contains))
                    Edge(mod.ModuleId, later);
            }

            var ready = new SortedSet<string>(group.Where(mod => incoming[mod.ModuleId] == 0).Select(mod => mod.ModuleId),
                Comparer<string>.Create((a, b) => index[a].CompareTo(index[b])));
            var placed = new List<string>();
            while (ready.Count > 0)
            {
                var id = ready.Min!;
                ready.Remove(id);
                placed.Add(id);
                foreach (var child in next[id])
                {
                    if (--incoming[child] == 0)
                        ready.Add(child);
                }
            }

            var stuck = group.Where(mod => !placed.Contains(mod.ModuleId, StringComparer.OrdinalIgnoreCase)).OrderBy(mod => index[mod.ModuleId]).ToList();
            if (stuck.Count > 1)
                cycles.Add(string.Join(", ", stuck.Select(mod => mod.Label)));
            placed.AddRange(stuck.Select(mod => mod.ModuleId));
            // TAOM players: RTS Camera works at the very bottom of the load order, and the TAOM
            // Xorberax patch goes last, after Xorberax's Legacy.
            var labels = setup.Extras.ToDictionary(mod => mod.ModuleId, mod => mod.Label, StringComparer.OrdinalIgnoreCase);
            placed = placed.OrderBy(id => TaomLoadOrder.LastRank(id, labels[id])).ToList();
            foreach (var id in placed)
            {
                var mod = setup.Extras.First(item => item.ModuleId.Equals(id, StringComparison.OrdinalIgnoreCase));
                mod.Slot = SlotOf(slotRank, id);
                sorted.Add(mod);
            }
        }

        setup.Extras = sorted;
        TaomLoadOrder.Normalize(setup);
        foreach (var mod in setup.Extras)
        {
            if (oldSlots[mod.ModuleId] == mod.Slot)
                continue;
            lines.Add(mod.Slot switch
            {
                ExtraSlot.BeforeTaom => mod.Label + " now loads before TAOM.",
                ExtraSlot.BeforeCoop => mod.Label + " now loads right before Coop, as TAOM.CoopCompat asks.",
                _ => mod.Label + " now loads after TAOM."
            });
        }

        foreach (var cycle in cycles)
            lines.Add(cycle + " each ask to load first. Their order was left as it was.");
        if (lines.Count == 0 && !before.SequenceEqual(setup.Extras.Select(mod => mod.ModuleId), StringComparer.OrdinalIgnoreCase))
            lines.Add("Sorted the extra mods so each one loads after the mods it needs.");
        return lines;
    }

    /// <summary>True when TAOM itself wants this mod before it: a TAOM module names it LoadBeforeThis, or
    /// the mod asks TAOM to load after it.</summary>
    public static bool TaomWantsItFirst(string id, IReadOnlyDictionary<string, ModuleManifest> manifests)
    {
        if (TaomModules.Any(core => manifests.TryGetValue(core, out var taom) && taom.Needs.Any(link => link.Id.Equals(id, StringComparison.OrdinalIgnoreCase))))
            return true;
        return manifests.TryGetValue(id, out var manifest) && manifest.LoadsBefore.Any(TaomModules.Contains);
    }

    private static int Effective(int rank, string id) => rank == 1 && !TaomLoadOrder.MayLoadBeforeCoop(id) ? 2 : rank;
}

/// <summary>
/// Checks before Connect or Start server. Hard problems (a missing or turned off dependency, two
/// mods that do not work together) block; the rest are warnings: a dependency that loads too late,
/// a mod made for another game version, a mod that brings its own copy of a library TAOM
/// Dependencies already has, a mod that is on this PC more than once.
/// </summary>
public static class TaomModChecks
{
    // Optional TaleWorlds modules a mod may name; the game starts without them.
    private static readonly HashSet<string> Soft = new(StringComparer.OrdinalIgnoreCase) { "CustomBattle", "StoryMode", "BirthAndDeath", "NavalDLC" };

    // TAOM.Dependencies v2.0.29.5 ModulesToLoadAfterThis: consumer mods it knows bundle their own
    // libraries; it loads first and redirects their copies, so they are not warned about.
    private static readonly HashSet<string> KnownBundlers = new(
        ("DismembermentPlus;XorberaxLegacy;DynaCulture;ArtemsLivelyAnimations;FluidCombatLite;Fluid_Combat_Lite;BetterSmithing;FasterTime;"
            + "BanditBlackHole;PerfectFireArrows;IDontCare;ImprovedGarrisons;DistinguishedService;CulturedStartV2;DiplomacyFixes;Diplomacy;"
            + "CalradiaExpanded;CalradiaExpandedKingdoms;CargoHolds;CrashDoctor;AchievementUnblocker;CREST;Crest;BannerlordTogether;BattleLinkMPClient;Coop")
        .Split(';'),
        StringComparer.OrdinalIgnoreCase);

    public static IReadOnlyList<TaomModIssue> Run(TaomSetup setup, IReadOnlyDictionary<string, TaomModFact> facts, TaomCompatList? compat = null, string gameVersion = TaomStack.GameVersion)
    {
        var issues = new List<TaomModIssue>();
        var order = TaomLoadOrder.ClientOrder(setup).ToList();
        var position = order.Select((id, i) => (id, i)).ToDictionary(item => item.id, item => item.i, StringComparer.OrdinalIgnoreCase);
        var labels = setup.Extras.ToDictionary(mod => mod.ModuleId, mod => mod.Label, StringComparer.OrdinalIgnoreCase);
        string Label(string id) => labels.TryGetValue(id, out var label) ? label : id;
        var disabled = new HashSet<string>(setup.Extras.Where(mod => !mod.Enabled).Select(mod => mod.ModuleId), StringComparer.OrdinalIgnoreCase);
        var bundlers = new HashSet<string>(KnownBundlers, StringComparer.OrdinalIgnoreCase);
        if (facts.TryGetValue("TAOM.Dependencies", out var deps) && deps.Manifest != null)
            bundlers.UnionWith(deps.Manifest.LoadsBefore);
        var pairs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var manifests = facts.Where(pair => pair.Value.Manifest != null).ToDictionary(pair => pair.Key, pair => pair.Value.Manifest!, StringComparer.OrdinalIgnoreCase);

        foreach (var mod in setup.EnabledExtras.Where(mod => position.ContainsKey(mod.ModuleId)))
        {
            var id = mod.ModuleId;
            facts.TryGetValue(id, out var fact);
            var manifest = fact?.Manifest;
            var known = compat?.Find(id, mod.WorkshopId, mod.Label);
            if (known is { Kind: CompatTag.Broken })
                issues.Add(new TaomModIssue(false, TaomCompatList.BrokenWarning(mod.Label, known)));
            if (fact?.Fix == BannerlordCoopLauncher.Core.Modules.DependencyFixState.Applied)
                issues.Add(new TaomModIssue(false, "TAOM dependency fix applied to " + mod.Label + ". It no longer asks for the libraries TAOM Dependencies already has."));
            else if (fact?.Fix == BannerlordCoopLauncher.Core.Modules.DependencyFixState.Needed)
                issues.Add(new TaomModIssue(false, mod.Label + " asks for libraries TAOM Dependencies already has. The TAOM dependency fix takes them out of its SubModule.xml while TAOM runs."));
            if (fact != null && fact.Copies.Count > 1)
                issues.Add(new TaomModIssue(false, mod.Label + " is on this PC " + fact.Copies.Count + " times. The game can load the wrong copy. Keep one copy."));
            if (fact is { BlockedDlls: > 0 })
                issues.Add(new TaomModIssue(false, "Unblock DLLs. " + mod.Label + " has DLL files Windows marked as downloaded from the internet. The launcher unblocks them before the game starts."));
            if (TaomLoadOrder.IsTaomDependencyPatch(id, mod.Label))
                issues.Add(new TaomModIssue(false, mod.Label + " is on this setup. It does the same job as the launcher's dependency fix, and both can run together."));
            // The TAOM developer: every extra mod goes below all TAOM files.
            if (mod.Slot == ExtraSlot.BeforeTaom && !TaomAutoSort.TaomWantsItFirst(id, manifests))
                issues.Add(new TaomModIssue(false, mod.Label + " loads before TAOM. The TAOM developer says every extra mod goes below all TAOM files. Click Auto sort."));
            if (manifest == null)
                continue;

            foreach (var need in manifest.RequiredIds.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                var target = need.Equals(TaomStack.LegacyCoopModuleId, StringComparison.OrdinalIgnoreCase) ? TaomStack.CoopModuleId : need;
                if (Soft.Contains(target) || TaomLoadOrder.InsideTaomDependencies.Contains(target))
                    continue;
                if (!position.TryGetValue(target, out var at))
                {
                    issues.Add(new TaomModIssue(true, disabled.Contains(target)
                        ? mod.Label + " needs " + Label(target) + ", which is turned off."
                        : mod.Label + " needs " + Label(target) + ", which is not on this setup."));
                    continue;
                }

                if (at > position[id])
                    issues.Add(new TaomModIssue(false, mod.Label + " loads before " + Label(target) + ", which it needs. Click Auto sort."));
            }

            foreach (var other in manifest.Incompatible)
            {
                if (!position.ContainsKey(other) || other.Equals(id, StringComparison.OrdinalIgnoreCase))
                    continue;
                var key = string.Compare(id, other, StringComparison.OrdinalIgnoreCase) < 0 ? id + "|" + other : other + "|" + id;
                if (pairs.Add(key))
                    issues.Add(new TaomModIssue(true, mod.Label + " does not work together with " + Label(other) + ". Turn one of them off."));
            }

            var native = manifest.NativeVersion;
            if (native != null && !SameGame(native, gameVersion))
                issues.Add(new TaomModIssue(false, mod.Label + " was made for Bannerlord " + native.Trim() + ". This TAOM runs on " + gameVersion.TrimStart('v') + ", so it may not work."));

            if (fact!.BundledLibs.Count > 0 && !bundlers.Contains(id))
                issues.Add(new TaomModIssue(false, mod.Label + " brings its own " + string.Join(", ", fact.BundledLibs) + ". TAOM Dependencies already has them, and two copies can clash."));
        }

        return issues;
    }

    /// <summary>True when the two versions are the same game release line (major and minor).</summary>
    public static bool SameGame(string modVersion, string gameVersion)
    {
        static (int, int)? MajorMinor(string text)
        {
            var parts = text.Trim().TrimStart('v', 'V', 'e', 'E').Split('.');
            return parts.Length >= 2 && int.TryParse(parts[0], out var major) && int.TryParse(parts[1], out var minor) ? (major, minor) : null;
        }

        var mod = MajorMinor(modVersion);
        var game = MajorMinor(gameVersion);
        return mod == null || game == null || mod == game;
    }
}
