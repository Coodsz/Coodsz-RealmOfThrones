using BannerlordCoopLauncher.Core.Modules;

namespace BannerlordCoopLauncher.Core.Profile;

public static class WorkingLoadOrder
{
    public static List<string> PlaceExtrasBelow(IReadOnlyList<string> current, IReadOnlyList<string> working)
    {
        var present = new HashSet<string>(current, StringComparer.OrdinalIgnoreCase);
        var workingSet = new HashSet<string>(working, StringComparer.OrdinalIgnoreCase);
        var next = working.Where(present.Contains).ToList();
        next.AddRange(current.Where(id => !workingSet.Contains(id)));
        return next;
    }

    public static bool CanMove(IReadOnlyList<string> ids, int from, int to, IReadOnlyList<string> working)
    {
        if (from < 0 || to < 0 || from >= ids.Count || to >= ids.Count || from == to)
            return false;

        var next = ids.ToList();
        var item = next[from];
        next.RemoveAt(from);
        next.Insert(to, item);
        return PlaceExtrasBelow(next, working).SequenceEqual(next, StringComparer.OrdinalIgnoreCase);
    }

    public static bool CanMoveKeepingCoop(IReadOnlyList<string> ids, int from, int to, IReadOnlyList<string> working)
    {
        if (from < 0 || to < 0 || from >= ids.Count || to >= ids.Count || from == to)
            return false;

        var next = ids.ToList();
        var item = next[from];
        next.RemoveAt(from);
        next.Insert(to, item);
        if (BannerlordCoopModules.IsEither(item))
            return FrozenStays(next, working);
        return KeepsCoopPlacement(next, working);
    }

    public static bool KeepsCoopPlacement(IReadOnlyList<string> ids, IReadOnlyList<string> working)
    {
        if (!FrozenStays(ids, working))
            return false;

        var frozen = FrozenSet(working);
        var lastFrozen = -1;
        for (var i = 0; i < ids.Count; i++)
        {
            if (frozen.Contains(ids[i]))
                lastFrozen = i;
        }

        var withCoop = new bool[ids.Count];
        for (var i = 0; i < ids.Count; i++)
        {
            if (!BannerlordCoopModules.IsEither(ids[i]))
                continue;
            var left = i;
            while (left > 0 && !frozen.Contains(ids[left - 1]))
                left--;
            var right = i;
            while (right + 1 < ids.Count && !frozen.Contains(ids[right + 1]))
                right++;
            for (var j = left; j <= right; j++)
                withCoop[j] = true;
        }

        for (var i = 0; i < ids.Count; i++)
        {
            if (frozen.Contains(ids[i]) || BannerlordCoopModules.IsEither(ids[i]))
                continue;
            if (i > lastFrozen || withCoop[i])
                continue;
            return false;
        }

        return true;
    }

    private static bool FrozenStays(IReadOnlyList<string> ids, IReadOnlyList<string> working)
    {
        var present = new HashSet<string>(ids, StringComparer.OrdinalIgnoreCase);
        var expected = working.Where(id => !BannerlordCoopModules.IsEither(id) && present.Contains(id)).ToList();
        var expectedSet = new HashSet<string>(expected, StringComparer.OrdinalIgnoreCase);
        var actual = ids.Where(expectedSet.Contains).ToList();
        return actual.SequenceEqual(expected, StringComparer.OrdinalIgnoreCase);
    }

    private static HashSet<string> FrozenSet(IReadOnlyList<string> working) =>
        new(working.Where(id => !BannerlordCoopModules.IsEither(id)), StringComparer.OrdinalIgnoreCase);
}
