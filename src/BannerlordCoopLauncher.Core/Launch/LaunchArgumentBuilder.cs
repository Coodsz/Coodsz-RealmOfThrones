using BannerlordCoopLauncher.Core.Models;
using BannerlordCoopLauncher.Core.Profile;
using BannerlordCoopLauncher.Core.Steam;

namespace BannerlordCoopLauncher.Core.Launch;

public sealed record LaunchPlan(
    string Executable,
    string WorkingDirectory,
    string Arguments,
    IReadOnlyList<string> ModuleOrder,
    bool AutoConnect,
    bool Diagnostic);

public static class LaunchArgumentBuilder
{
    public static LaunchPlan Create(
        string gameRoot,
        ServerProfile profile,
        bool diagnostic,
        string? steamName = null,
        IReadOnlyDictionary<string, string>? launchIds = null,
        ulong? connectLobby = null,
        string? directEndpoint = null,
        IReadOnlyList<string>? addOns = null)
    {
        _ = steamName;
        var order = ApplyLaunchIds(ModuleOrder(profile), launchIds);
        // Client add-ons (DedicatedServer.*) load last; Coop leaves them out of the join check.
        if (addOns is { Count: > 0 })
            order = order.Concat(addOns.Where(id => !order.Contains(id, StringComparer.OrdinalIgnoreCase))).ToList();
        var autoConnect = !diagnostic && profile.AutoConnect;
        var includeClient = !diagnostic;
        var lobby = !diagnostic && connectLobby is > 0 ? connectLobby : null;
        var endpoint = lobby == null && autoConnect ? directEndpoint : null;
        return new LaunchPlan(
            GameRoot.ExecutablePath(gameRoot),
            GameRoot.WorkingDirectory(gameRoot),
            Format(order, includeClient, autoConnect && lobby == null, lobby, endpoint),
            order,
            autoConnect,
            diagnostic);
    }

    public static IReadOnlyList<string> ModuleOrder(ServerProfile profile)
    {
        var allowed = new HashSet<string>(profile.BaseModules, StringComparer.Ordinal);
        foreach (var mod in profile.EnabledMods)
            allowed.Add(mod.ModuleId);

        var source = profile.ModuleLoadOrder.Count > 0
            ? profile.ModuleLoadOrder
            : profile.BaseModules.Concat(profile.EnabledMods.Select(mod => mod.ModuleId));

        var order = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var moduleId in source)
        {
            if (!allowed.Contains(moduleId) || !seen.Add(moduleId))
                continue;
            order.Add(moduleId);
        }

        foreach (var moduleId in profile.BaseModules.Concat(profile.EnabledMods.Select(mod => mod.ModuleId)))
        {
            if (seen.Add(moduleId))
                order.Add(moduleId);
        }

        return SettlementBridgeRule.Filter(order);
    }

    public static IReadOnlyList<string> ApplyLaunchIds(IReadOnlyList<string> order, IReadOnlyDictionary<string, string>? launchIds)
    {
        if (launchIds == null || launchIds.Count == 0)
            return order;
        return order
            .Select(id => launchIds.TryGetValue(id, out var actual) && !string.IsNullOrWhiteSpace(actual) ? actual : id)
            .ToList();
    }

    public static string Format(IReadOnlyList<string> moduleOrder, bool includeClient, bool autoConnect, ulong? connectLobby = null, string? directEndpoint = null)
    {
        var parts = new List<string> { "/singleplayer" };
        if (includeClient)
            parts.Add("/client");
        if (connectLobby is > 0)
        {
            parts.Add("+connect_lobby");
            parts.Add(connectLobby.Value.ToString(System.Globalization.CultureInfo.InvariantCulture));
        }
        else if (autoConnect)
        {
            parts.Add("/autoconnect");
            if (!string.IsNullOrWhiteSpace(directEndpoint))
                parts.Add(directEndpoint.Trim());
        }
        parts.Add("_MODULES_*" + string.Join("*", moduleOrder) + "*_MODULES_");
        return string.Join(" ", parts);
    }
}

public static class SteamHostName
{
    public static bool IsValid(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return false;
        var value = name.Trim();
        if (value.Length is < 2 or > 32)
            return false;
        // A Steam profile name can use almost any character (Cobra|El!te|#666).
        // Only a double quote (it would end the quoted argument) and control
        // characters are refused.
        return value.All(ch => ch != '"' && !char.IsControl(ch));
    }

    public static string Quote(string name) => "\"" + name.Trim() + "\"";
}
