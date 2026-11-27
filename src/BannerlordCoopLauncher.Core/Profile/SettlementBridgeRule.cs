namespace BannerlordCoopLauncher.Core.Profile;

// PSCoopBridge only works with Player Settlement. Without it, PSCoopBridge looks for
// Player Settlement on every frame (a search through every loaded type), which drops
// a player's game to about 10 fps from the main menu on. The dedicated server skips
// that search, but the server and players must load the same mods, so both sides
// leave PSCoopBridge out whenever Player Settlement is not on.
public static class SettlementBridgeRule
{
    public const string Bridge = "PSCoopBridge";
    public const string PlayerSettlement = "PlayerSettlement";

    public const string TurnedOffNote =
        "PSCoopBridge was turned off because Player Settlement is not on. Without Player Settlement it makes players' games run at about 10 fps. Players' launchers turn it off the same way. If the server says this save does not match the mods, start a new campaign.";

    public static bool BridgeWithoutSettlement(IEnumerable<string> enabledModuleIds)
    {
        var ids = enabledModuleIds as ICollection<string> ?? enabledModuleIds.ToList();
        return ids.Contains(Bridge, StringComparer.OrdinalIgnoreCase)
            && !ids.Contains(PlayerSettlement, StringComparer.OrdinalIgnoreCase);
    }

    public static IReadOnlyList<string> Filter(IReadOnlyList<string> moduleOrder) =>
        BridgeWithoutSettlement(moduleOrder)
            ? moduleOrder.Where(id => !id.Equals(Bridge, StringComparison.OrdinalIgnoreCase)).ToList()
            : moduleOrder;
}
