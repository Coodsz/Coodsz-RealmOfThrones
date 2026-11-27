using BannerlordCoopLauncher.Core.Models;

namespace BannerlordCoopLauncher.Core.Profile;

public static class ClientLoadOrder
{
    public static void Apply(ServerProfile profile, IReadOnlyList<string>? savedOrder)
    {
        if (savedOrder == null || savedOrder.Count == 0)
            return;
        profile.ModuleLoadOrder = Arrange(profile.ModuleLoadOrder, savedOrder);
    }

    public static List<string> Arrange(IEnumerable<string> current, IEnumerable<string> preferred)
    {
        var remaining = current.ToList();
        var next = new List<string>();
        foreach (var id in preferred)
        {
            var index = remaining.FindIndex(item => item.Equals(id, StringComparison.OrdinalIgnoreCase));
            if (index < 0)
                continue;
            next.Add(remaining[index]);
            remaining.RemoveAt(index);
        }

        next.AddRange(remaining);
        return next;
    }
}
