using BannerlordCoopLauncher.Core.Models;

namespace BannerlordCoopLauncher.Core.Profile;

public static class ClientCampaignModes
{
    public static readonly string[] Ids = ["StoryMode", "CustomBattle"];

    public static void KeepOnClient(ServerProfile profile)
    {
        InsertAfterSandBox(profile.BaseModules);
        InsertAfterSandBox(profile.ModuleLoadOrder);
    }

    private static void InsertAfterSandBox(List<string> ids)
    {
        var sandbox = ids.FindIndex(id => id.Equals("SandBox", StringComparison.OrdinalIgnoreCase));
        var at = sandbox < 0 ? ids.Count : sandbox + 1;
        foreach (var id in Ids)
        {
            if (ids.Contains(id, StringComparer.OrdinalIgnoreCase))
                continue;
            at = Math.Min(at, ids.Count);
            ids.Insert(at, id);
            at++;
        }
    }
}
