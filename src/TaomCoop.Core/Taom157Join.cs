using BannerlordCoopLauncher.Core.Modules;

namespace TaomCoop.Core;

/// <summary>The TAOM 1.5.7 modules that depend on the host's setup or installed TAOM aliases.</summary>
public static class Taom157Join
{
    private const string DependenciesId = "TAOM.Dependencies";

    public static bool IsOptional(string id) => IsAlias(id) ||
        id.Equals(Taom157Start.DualWieldModuleId, StringComparison.OrdinalIgnoreCase);

    private static bool IsAlias(string id) => Taom157Start.HelperFolders.Contains(id, StringComparer.OrdinalIgnoreCase);

    private static ModuleIdentity? Installed(string modules, string id)
    {
        // Resolve only the known IDs, never a path supplied by an imported list.
        var knownId = Taom157Start.HelperFolders.Append(Taom157Start.DualWieldModuleId)
            .First(candidate => candidate.Equals(id, StringComparison.OrdinalIgnoreCase));
        var folder = Path.Combine(modules, knownId);
        if (IsAlias(id) && !Taom157Prepare.IsTaomStub(folder))
            return null;
        var identity = ModuleIdentityReader.TryRead(Path.Combine(folder, "SubModule.xml"));
        return identity != null && identity.Id.Equals(knownId, StringComparison.OrdinalIgnoreCase) ? identity : null;
    }

    public static Taom157ModuleRequirement[]? LocalDualWield(string config, int port, string serverRoot)
    {
        if ((BannerlordCoopLauncher.Core.Joining.LocalServer.ConfigPort(config) ?? 4200) != port)
            return null;
        var modules = BannerlordCoopLauncher.Core.Server.NightlyServer.ReadModules(config);
        if (!modules.Contains(TaomModules157.TaomModuleId, StringComparer.OrdinalIgnoreCase))
            return null;
        if (!modules.Contains(Taom157Start.DualWieldModuleId, StringComparer.OrdinalIgnoreCase))
            return [];
        var host = Installed(Path.Combine(serverRoot, "engine", "Modules"), Taom157Start.DualWieldModuleId);
        return host == null || string.IsNullOrWhiteSpace(host.Version) ? null : [new(host.Id, host.Version)];
    }

    public static void ApplyLocalDualWield(TaomSetup launch, Taom157ModuleRequirement[] local)
    {
        launch.Line157Requirements ??= [];
        launch.Line157Requirements.RemoveAll(item => item.Id.Equals(Taom157Start.DualWieldModuleId, StringComparison.OrdinalIgnoreCase));
        launch.Line157Requirements.AddRange(local);
        launch.Extras.RemoveAll(item => item.ModuleId.Equals(Taom157Start.DualWieldModuleId, StringComparison.OrdinalIgnoreCase));
    }

    // Bannerlord omits the changeset when it is zero. Its version check also compares the release channel.
    public static string? OptionalVersionWarning(string name, string listed, string installed)
    {
        if (listed.Length > 1 && installed.Length > 1 && listed[0] == installed[0]
            && listed[0] is 'a' or 'b' or 'e' or 'v' or 'd'
            && Version.TryParse(listed.AsSpan(1), out var want) && want.Build >= 0
            && Version.TryParse(installed.AsSpan(1), out var have) && have.Build >= 0
            && want.Major == have.Major && want.Minor == have.Minor && want.Build == have.Build
            && Math.Max(0, want.Revision) == Math.Max(0, have.Revision))
            return null;
        return SharedModList.VersionWarning(name, listed, installed);
    }

    public static string? Problem(string modules, TaomSetup setup)
    {
        foreach (var requirement in setup.Line157Requirements?.Where(item => IsOptional(item.Id)) ?? [])
        {
            var installed = Installed(modules, requirement.Id);
            if (installed == null || string.IsNullOrWhiteSpace(installed.Version))
                return "The server needs " + requirement.Id + ". Install the same mod as the host, then click Check mods.";
            if (OptionalVersionWarning(requirement.Id, requirement.Version, installed.Version) is { } warning)
                return warning;
        }

        return null;
    }

    public static IReadOnlyList<string> ClientModules(string modules, TaomSetup setup, IReadOnlyList<string> order)
    {
        var requested = setup.Line157Requirements?.Select(item => item.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var aliasesListed = setup.Line157Exact || requested?.Any(IsAlias) == true;
        var aliases = Taom157Start.HelperFolders.Where(id =>
            (!aliasesListed || requested?.Contains(id) == true) && Installed(modules, id) != null);
        var dualWield = requested?.Contains(Taom157Start.DualWieldModuleId) == true
            || (!setup.Line157Exact && order.Contains(Taom157Start.DualWieldModuleId, StringComparer.OrdinalIgnoreCase));
        return MatchOrder(order, aliases, dualWield);
    }

    public static IReadOnlyList<string> HostShareOrder(string server, bool includeDualWield)
    {
        return HostShareOrder(Taom157Start.Modules(server, includeDualWield));
    }

    public static IReadOnlyList<string> HostShareOrder(IReadOnlyList<string> active)
    {
        var core = TaomStack.ClientModules.Where(id => id != TaomStack.CompatModuleId || active.Contains(id));
        return MatchOrder(core, active.Where(IsAlias), active.Contains(Taom157Start.DualWieldModuleId));
    }

    private static IReadOnlyList<string> MatchOrder(IEnumerable<string> core, IEnumerable<string> aliases, bool dualWield)
    {
        var order = core.Where(id => !IsOptional(id)).ToList();
        order.InsertRange(order.IndexOf(DependenciesId) + 1, aliases);
        if (dualWield)
        {
            order.Insert(order.IndexOf(TaomStack.CoopModuleId), Taom157Start.DualWieldModuleId);
        }
        return order;
    }
}
