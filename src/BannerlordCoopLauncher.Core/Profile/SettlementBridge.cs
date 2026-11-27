using BannerlordCoopLauncher.Core.Models;

namespace BannerlordCoopLauncher.Core.Profile;

public static class SettlementBridge
{
    public static void Ensure(ServerProfile profile)
    {
        EnsureMod(profile, "Agriculture Estate", "2880158010", "AgricultureEstate");
        EnsureMod(profile, "Player Settlement", "3720376888", "PlayerSettlement");
        EnsureMod(profile, "PSCoopBridge", "0", "PSCoopBridge", pinned: true);

        var tail = new[] { "AgricultureEstate", "PlayerSettlement", "PSCoopBridge" };
        profile.ModuleLoadOrder.RemoveAll(id => tail.Contains(id, StringComparer.Ordinal));
        profile.ModuleLoadOrder.AddRange(tail);
    }

    private static void EnsureMod(ServerProfile profile, string name, string workshopId, string moduleId, bool pinned = false)
    {
        var existing = profile.Mods.FirstOrDefault(mod => string.Equals(mod.ModuleId, moduleId, StringComparison.Ordinal));
        if (existing == null)
        {
            profile.Mods.Add(new ProfileMod
            {
                Name = name,
                WorkshopId = workshopId,
                ModuleId = moduleId,
                Enabled = true,
                Pinned = pinned
            });
            return;
        }

        existing.Enabled = true;
        existing.Name = name;
        if (pinned)
            existing.Pinned = true;
    }
}
