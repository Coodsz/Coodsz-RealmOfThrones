namespace BannerlordCoopLauncher.Core.Modules;

public static class BannerlordCoopModules
{
    public const string Coop = "Coop";
    public const string CoopNightly = "CoopNightly";

    public static bool IsEither(string? moduleId) =>
        moduleId != null
        && (moduleId.Equals(Coop, StringComparison.OrdinalIgnoreCase)
            || moduleId.Equals(CoopNightly, StringComparison.OrdinalIgnoreCase));

    public static string Keep(IEnumerable<string> installedIds)
    {
        var ids = new HashSet<string>(installedIds, StringComparer.OrdinalIgnoreCase);
        if (ids.Contains(Coop))
            return Coop;
        if (ids.Contains(CoopNightly))
            return CoopNightly;
        return Coop;
    }

    public static bool Hide(string moduleId, IEnumerable<string> installedIds) =>
        IsEither(moduleId) && !moduleId.Equals(Keep(installedIds), StringComparison.OrdinalIgnoreCase);
}
