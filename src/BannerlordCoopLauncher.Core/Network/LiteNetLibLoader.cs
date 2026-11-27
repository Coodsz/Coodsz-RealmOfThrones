using System.Reflection;
using System.Runtime.Loader;

namespace BannerlordCoopLauncher.Core.Network;

/// <summary>
/// Lets the TAOM tools find LiteNetLib.dll after an in place update. Those tools ship as loose files,
/// and older updaters left every .json file alone, so an install updated by one of them keeps its
/// old deps.json, which does not list LiteNetLib. The current updater replaces deps.json and
/// runtimeconfig.json, but this stays for installs that were updated the old way. Without it the
/// runtime would not load the dll that sits next to the exe.
/// Single file tools carry LiteNetLib inside the exe and never reach this resolver.
/// </summary>
public static class LiteNetLibLoader
{
    private static int registered;

    public static void EnsureResolvable()
    {
        if (Interlocked.Exchange(ref registered, 1) != 0)
            return;
        AssemblyLoadContext.Default.Resolving += (context, name) =>
        {
            var path = FindLocal(name, AppContext.BaseDirectory);
            return path == null ? null : context.LoadFromAssemblyPath(path);
        };
    }

    public static string? FindLocal(AssemblyName name, string baseDirectory)
    {
        if (!string.Equals(name.Name, "LiteNetLib", StringComparison.OrdinalIgnoreCase) || string.IsNullOrEmpty(baseDirectory))
            return null;
        var path = Path.Combine(baseDirectory, "LiteNetLib.dll");
        return File.Exists(path) ? path : null;
    }
}
