using BannerlordCoopLauncher.Core.Localization;
using BannerlordCoopLauncher.Core.Parsing;
using TaomCoop.Core;

namespace RealmOfThrones.Core;

/// <summary>
/// Sorts extras using manifest order, with the four RoT modules represented by one node.

/// </summary>
public static class RotAutoSort
{
    // Mods players found work only at the very bottom of the load order. Auto sort puts them last.
    public static readonly IReadOnlySet<string> LoadsLast = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "RTSCamera", "RTSCamera.CommandSystem" };

    public sealed record Result(bool Changed, IReadOnlyList<string> Lines);

    /// <summary>Sorts the setup in place and reports dependency cycles separately.</summary>
    public static Result Sort(TaomSetup setup, IReadOnlyDictionary<string, ModuleManifest> manifests)
    {
        if (setup.Locked)
            throw new InvalidOperationException(setup.Name + " is locked. Click New setup or Copy to make a setup you can add mods to.");
        RotLoadOrder.Normalize(setup);
        var lines = new List<string>();
        var before = RotLoadOrder.ClientRows(setup)
            .Where(row => row.ModuleId == RotStack.CoreModuleId || (!row.IsCore && !RotLoadOrder.LoadsAfterHarmony(row.ModuleId)))
            .Select(row => row.ModuleId).ToList();
        var ids = new HashSet<string>(before, StringComparer.OrdinalIgnoreCase);
        ModuleManifest? M(string id) => manifests.TryGetValue(id, out var manifest) ? manifest : null;
        IEnumerable<string> Needs(string id) => M(id)?.Needs.Select(link => link.Id) ?? Enumerable.Empty<string>();
        IEnumerable<string> LoadsBefore(string id) => M(id)?.LoadsBefore ?? (IEnumerable<string>)Array.Empty<string>();

        var index = before.Select((id, i) => (id, i)).ToDictionary(item => item.id, item => item.i, StringComparer.OrdinalIgnoreCase);
        var next = before.ToDictionary(id => id, _ => new List<string>(), StringComparer.OrdinalIgnoreCase);
        void Edge(string first, string second)
        {
            if (first.Equals(second, StringComparison.OrdinalIgnoreCase) || next[first].Contains(second, StringComparer.OrdinalIgnoreCase))
                return;
            next[first].Add(second);
        }

        string Node(string id) => RotStack.IsRotModule(id) ? RotStack.CoreModuleId : id;
        foreach (var id in before.Where(id => id != RotStack.CoreModuleId).Concat(RotStack.RotModules))
        {
            foreach (var need in Needs(id).Select(Node).Where(ids.Contains))
                Edge(need, Node(id));
            foreach (var later in LoadsBefore(id).Select(Node).Where(ids.Contains))
                Edge(Node(id), later);
        }

        List<string> Place()
        {
            var incoming = before.ToDictionary(id => id, _ => 0, StringComparer.OrdinalIgnoreCase);
            foreach (var children in next.Values)
                foreach (var child in children)
                    incoming[child]++;

            var ready = new SortedSet<string>(before.Where(id => incoming[id] == 0),
                Comparer<string>.Create((a, b) => LoadsLast.Contains(a) == LoadsLast.Contains(b)
                    ? index[a].CompareTo(index[b]) : LoadsLast.Contains(a) ? 1 : -1));
            var placed = new List<string>();
            while (ready.Count > 0)
            {
                var id = ready.Min!;
                ready.Remove(id);
                placed.Add(id);
                foreach (var child in next[id])
                    if (--incoming[child] == 0)
                        ready.Add(child);
            }
            return placed;
        }

        var order = Place();
        if (order.Count != before.Count)
        {
            // Keep the order within each cycle and leave its edges to other mods intact.
            var remaining = before.Except(order, StringComparer.OrdinalIgnoreCase).ToList();
            var reachable = remaining.ToDictionary(id => id, id => Reachable(id, next), StringComparer.OrdinalIgnoreCase);
            var reported = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var id in remaining)
            {
                if (reported.Contains(id) || !reachable[id].Contains(id))
                    continue;

                var cycle = remaining.Where(other => reachable[id].Contains(other) && reachable[other].Contains(id)).ToList();
                var members = cycle.ToHashSet(StringComparer.OrdinalIgnoreCase);
                reported.UnionWith(members);
                foreach (var member in cycle)
                    next[member].RemoveAll(members.Contains);
                for (var i = 1; i < cycle.Count; i++)
                    Edge(cycle[i - 1], cycle[i]);

                var labels = cycle.Select(member => setup.Extras.FirstOrDefault(mod => mod.ModuleId.Equals(member, StringComparison.OrdinalIgnoreCase))?.Label ?? member);
                lines.Add(string.Format(Translator.T("{0} have a dependency cycle. Their relative order stayed the same."), string.Join(", ", labels)));
            }
            order = Place();
        }

        var changed = !before.SequenceEqual(order, StringComparer.OrdinalIgnoreCase);
        if (changed)
        {
            var sorted = new List<TaomExtraMod>();
            var slot = RotLoadOrder.SlotBeforeRot;
            foreach (var id in order)
            {
                if (id == RotStack.CoreModuleId)
                {
                    slot = RotLoadOrder.SlotAfterRot;
                    continue;
                }

                var mod = setup.Extras.First(mod => mod.ModuleId.Equals(id, StringComparison.OrdinalIgnoreCase));
                mod.Slot = slot;
                sorted.Add(mod);
            }

            setup.Extras = sorted.Concat(setup.Extras.Except(sorted)).ToList();
            RotLoadOrder.Normalize(setup);
            if (lines.Count == 0)
                lines.Add("Sorted the extra mods so each one loads after the mods it needs.");
        }

        return new Result(changed, lines);
    }

    private static HashSet<string> Reachable(string id, Dictionary<string, List<string>> next)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var pending = new Stack<string>(next[id]);
        while (pending.TryPop(out var child))
        {
            if (seen.Add(child))
                foreach (var descendant in next[child])
                    pending.Push(descendant);
        }
        return seen;
    }
}

/// <summary>
/// Checks before Connect or Start server. Hard problems (a missing or turned off dependency, two
/// mods that do not work together) block; the rest are warnings: a dependency that loads too late,
/// a mod made for another game version, a mod that brings its own copy of a library mod, a mod that
/// is on this PC more than once, a mod with blocked DLLs.
/// </summary>
public static class RotModChecks
{
    // Optional TaleWorlds modules a mod may name; the game starts without them.
    private static readonly HashSet<string> Soft = new(StringComparer.OrdinalIgnoreCase) { "CustomBattle", "StoryMode", "BirthAndDeath", "NavalDLC" };

    public static IReadOnlyList<TaomModIssue> Run(TaomSetup setup, IReadOnlyDictionary<string, TaomModFact> facts, string gameVersion = RotStack.GameVersion)
    {
        var issues = new List<TaomModIssue>();
        var order = RotLoadOrder.ClientOrder(setup).ToList();
        var position = order.Select((id, i) => (id, i)).ToDictionary(item => item.id, item => item.i, StringComparer.OrdinalIgnoreCase);
        var labels = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var extra in setup.Extras)
            labels.TryAdd(extra.ModuleId.Trim(), extra.Label);
        string Label(string id) => labels.TryGetValue(id, out var label) ? label : id;
        var disabled = new HashSet<string>(setup.Extras.Where(mod => !mod.Enabled).Select(mod => mod.ModuleId), StringComparer.OrdinalIgnoreCase);
        var pairs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var mod in setup.EnabledExtras.Where(mod => position.ContainsKey(mod.ModuleId)))
        {
            var id = mod.ModuleId;
            facts.TryGetValue(id, out var fact);
            var manifest = fact?.Manifest;
            if (fact != null && fact.Copies.Count > 1)
                issues.Add(new TaomModIssue(false, mod.Label + " is on this PC " + fact.Copies.Count + " times. The game can load the wrong copy. Keep one copy."));
            if (fact is { BlockedDlls: > 0 })
                issues.Add(new TaomModIssue(false, "Unblock DLLs. " + mod.Label + " has DLL files Windows marked as downloaded from the internet. The launcher unblocks them before the game starts."));
            if (manifest == null)
                continue;

            foreach (var need in manifest.RequiredIds.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                // A mod made for the Workshop Coop or the nightly means whichever Coop the set runs.
                var target = CoopId(need);
                if (Soft.Contains(target))
                    continue;
                if (!position.ContainsKey(target))
                {
                    issues.Add(new TaomModIssue(true, disabled.Contains(target)
                        ? mod.Label + " needs " + Label(target) + ", which is turned off."
                        : mod.Label + " needs " + Label(target) + ", which is not on this setup."));
                    continue;
                }

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
            if (native != null && !TaomModChecks.SameGame(native, gameVersion))
                issues.Add(new TaomModIssue(false, mod.Label + " was made for Bannerlord " + native.Trim() + ". Realm of Thrones runs on " + gameVersion.TrimStart('v') + ", so it may not work."));

            // The core already loads Harmony, ButterLib, UIExtenderEx and MCM from their own modules.
            if (fact!.BundledLibs.Count > 0)
                issues.Add(new TaomModIssue(false, mod.Label + " brings its own " + string.Join(", ", fact.BundledLibs) + ". The Realm of Thrones core already loads them, and two copies can clash."));
        }

        foreach (var problem in ReversedOrder(setup, facts).Distinct())
            issues.Add(new TaomModIssue(false, OrderMessage(setup, problem)));

        return issues;
    }

    // Check positions even when a row is off. Its constraints will apply when it is enabled.
    internal static IEnumerable<(string First, string Second)> ReversedOrder(TaomSetup setup, IReadOnlyDictionary<string, TaomModFact> facts)
    {
        var rows = RotLoadOrder.ClientRows(setup);
        var positions = rows.Select((row, index) => (row.ModuleId, index))
            .ToDictionary(row => row.ModuleId, row => row.index, StringComparer.OrdinalIgnoreCase);

        foreach (var row in rows)
        {
            var id = row.ModuleId;
            if (!facts.TryGetValue(id, out var fact) || fact.Manifest is not { } manifest)
                continue;

            foreach (var need in manifest.Needs.Select(link => CoopId(link.Id)))
                if (positions.TryGetValue(need, out var before) && before > positions[id])
                    yield return (rows[before].ModuleId, id);

            foreach (var later in manifest.LoadsBefore.Select(CoopId))
                if (positions.TryGetValue(later, out var after) && positions[id] > after)
                    yield return (id, rows[after].ModuleId);
        }
    }

    internal static string OrderMessage(TaomSetup setup, (string First, string Second) edge)
    {
        string Label(string id) => setup.Extras.FirstOrDefault(mod => mod.ModuleId.Trim().Equals(id, StringComparison.OrdinalIgnoreCase))?.Label
            ?? RotJoinCheck.Name(id);
        return string.Format(Translator.T("{0} must load before {1}. Click Auto sort."), Label(edge.First), Label(edge.Second));
    }

    private static string CoopId(string id) =>
        id.Equals("Coop", StringComparison.OrdinalIgnoreCase) || id.Equals("CoopNightly", StringComparison.OrdinalIgnoreCase)
            ? RotStack.CoopModuleId
            : id;
}
