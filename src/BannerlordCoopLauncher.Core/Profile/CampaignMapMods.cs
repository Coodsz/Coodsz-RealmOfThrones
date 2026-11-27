using BannerlordCoopLauncher.Core.Models;

namespace BannerlordCoopLauncher.Core.Profile;

public static class CampaignMapMods
{
    public static readonly string[] Ids = ["MoreNationsRemastered", "MoreNationsRemasteredFix"];

    public static void Apply(ServerProfile profile, bool includeMoreNations)
    {
        if (!includeMoreNations)
        {
            profile.Mods.RemoveAll(mod => Ids.Contains(mod.ModuleId, StringComparer.OrdinalIgnoreCase));
            profile.ModuleLoadOrder.RemoveAll(id => Ids.Contains(id, StringComparer.OrdinalIgnoreCase));
            return;
        }

        var insertAt = profile.ModuleLoadOrder.FindIndex(id => id.Equals("Coop", StringComparison.OrdinalIgnoreCase));
        if (insertAt < 0)
            insertAt = profile.ModuleLoadOrder.Count;
        else
            insertAt++;

        foreach (var (name, workshopId, moduleId) in new[]
        {
            ("More Nations Remastered", "0", Ids[0]),
            ("MoreNationsRemastered COOP Fix", "0", Ids[1])
        })
        {
            if (profile.Mods.All(mod => !mod.ModuleId.Equals(moduleId, StringComparison.OrdinalIgnoreCase)))
            {
                profile.Mods.Add(new ProfileMod
                {
                    Name = name,
                    WorkshopId = workshopId,
                    ModuleId = moduleId,
                    Enabled = true
                });
            }

            if (profile.ModuleLoadOrder.Any(id => id.Equals(moduleId, StringComparison.OrdinalIgnoreCase)))
                continue;
            var at = Math.Min(insertAt, profile.ModuleLoadOrder.Count);
            profile.ModuleLoadOrder.Insert(at, moduleId);
            insertAt = at + 1;
        }
    }
}
