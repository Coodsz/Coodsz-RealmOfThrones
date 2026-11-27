using BannerlordCoopLauncher.Core.Models;

namespace BannerlordCoopLauncher.Core.Profile;

public static class RemovedModLaunch
{
    public static void Drop(ServerProfile profile, IEnumerable<string> removedIds)
    {
        var removed = new HashSet<string>(removedIds.Where(id => !string.IsNullOrWhiteSpace(id)), StringComparer.OrdinalIgnoreCase);
        if (removed.Count == 0)
            return;

        profile.Mods.RemoveAll(mod => removed.Contains(mod.ModuleId));
        profile.ModuleLoadOrder.RemoveAll(removed.Contains);
    }
}
