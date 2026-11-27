using BannerlordCoopLauncher.Core.Modules;

namespace BannerlordCoopLauncher.Core.Profile;

public static class ExtraDependencyOrder
{
    private static readonly HashSet<string> Soft = new(StringComparer.OrdinalIgnoreCase)
    {
        "CustomBattle", "StoryMode", "BirthAndDeath"
    };

    public static List<string> Sort(
        IReadOnlyList<string> current,
        IReadOnlyList<string> working,
        IReadOnlyDictionary<string, IReadOnlyList<string>> dependencies)
    {
        var placed = WorkingLoadOrder.PlaceExtrasBelow(current, working);
        var workingSet = new HashSet<string>(working, StringComparer.OrdinalIgnoreCase);
        var head = placed.Where(workingSet.Contains).ToList();
        var extras = placed.Where(id => !workingSet.Contains(id)).ToList();
        var extraSet = new HashSet<string>(extras, StringComparer.OrdinalIgnoreCase);
        var index = extras.Select((id, i) => (id, i)).ToDictionary(item => item.id, item => item.i, StringComparer.OrdinalIgnoreCase);
        var incoming = extras.ToDictionary(id => id, _ => 0, StringComparer.OrdinalIgnoreCase);
        var next = extras.ToDictionary(id => id, _ => new List<string>(), StringComparer.OrdinalIgnoreCase);
        foreach (var id in extras)
        {
            if (!dependencies.TryGetValue(id, out var needs))
                continue;
            foreach (var dependency in needs)
            {
                if (!extraSet.Contains(dependency) || Soft.Contains(dependency))
                    continue;
                next[dependency].Add(id);
                incoming[id]++;
            }
        }

        var ready = new SortedSet<string>(
            extras.Where(id => incoming[id] == 0),
            Comparer<string>.Create((a, b) =>
            {
                var order = index[a].CompareTo(index[b]);
                return order != 0 ? order : StringComparer.OrdinalIgnoreCase.Compare(a, b);
            }));
        var sorted = new List<string>();
        while (ready.Count > 0)
        {
            var id = ready.Min!;
            ready.Remove(id);
            sorted.Add(id);
            foreach (var child in next[id])
            {
                incoming[child]--;
                if (incoming[child] == 0)
                    ready.Add(child);
            }
        }

        foreach (var id in extras)
        {
            if (!sorted.Contains(id, StringComparer.OrdinalIgnoreCase))
                sorted.Add(id);
        }

        var result = new List<string>(head.Count + sorted.Count);
        result.AddRange(head);
        result.AddRange(sorted);
        return result;
    }

    public static IReadOnlyList<string> Problems(
        IReadOnlyList<string> order,
        IReadOnlyList<string> working,
        IReadOnlyDictionary<string, IReadOnlyList<string>> dependencies,
        IReadOnlyDictionary<string, string> names,
        IReadOnlySet<string> enabled)
    {
        var workingSet = new HashSet<string>(working, StringComparer.OrdinalIgnoreCase);
        var position = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < order.Count; i++)
            position[order[i]] = i;
        var lines = new List<string>();
        foreach (var id in order.Where(item => !workingSet.Contains(item)))
        {
            if (!dependencies.TryGetValue(id, out var needs))
                continue;
            var name = names.TryGetValue(id, out var known) ? known : id;
            foreach (var dependency in needs.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                if (Soft.Contains(dependency))
                    continue;
                var listed = ListedId(position, dependency);
                var neededName = listed != null && names.TryGetValue(listed, out var listedName)
                    ? listedName
                    : names.TryGetValue(dependency, out var needed) ? needed : dependency;
                if (listed == null)
                {
                    lines.Add(name + " needs " + neededName + ", which is not in this list. The game can stop while starting.");
                    continue;
                }

                if (!enabled.Contains(listed))
                    lines.Add(name + " needs " + neededName + ", which is turned off. The game can stop while starting.");
                else if (position[listed] > position[id])
                    lines.Add(name + " still loads before " + neededName + ". The game can stop while starting.");
            }
        }

        return lines;
    }

    private static string? ListedId(IReadOnlyDictionary<string, int> position, string dependency)
    {
        if (position.ContainsKey(dependency))
            return dependency;
        if (!BannerlordCoopModules.IsEither(dependency))
            return null;
        foreach (var id in position.Keys)
        {
            if (BannerlordCoopModules.IsEither(id))
                return id;
        }

        return null;
    }
}
