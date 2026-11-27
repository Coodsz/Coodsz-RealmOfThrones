using System.Text.RegularExpressions;

namespace BannerlordCoopLauncher.Core.AD1259;

/// <summary>
/// Mods that ship a shader cache built for an older Bannerlord. The game logs "Shader cache version of
/// the external module (X) is invalid" on every start and sometimes crashes right after it, so the
/// launcher sets those caches aside (ModuleShaderCache) and the game builds its own.
/// </summary>
public static class AD1259ShaderCaches
{
    // Seen on every start with the AD1259 set (OpenSourceWeaponry 2.0.1 from the Workshop).
    public static readonly string[] Known = ["OpenSourceWeaponry"];

    private static readonly Regex Invalid = new(@"Shader cache version of the external module \(([^)]+)\) is invalid", RegexOptions.Compiled);

    /// <summary>Module names the newest engine logs reported with an invalid shader cache.</summary>
    public static IReadOnlyList<string> Reported(string engineLogsDirectory, int newestLogs = 5)
    {
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (!Directory.Exists(engineLogsDirectory))
            return [];
        var logs = Directory.EnumerateFiles(engineLogsDirectory, "rgl_log_*.txt")
            .Where(path => !Path.GetFileName(path).StartsWith("rgl_log_errors", StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(File.GetLastWriteTimeUtc)
            .Take(newestLogs);
        foreach (var log in logs)
        {
            string text;
            try
            {
                text = File.ReadAllText(log);
            }
            catch (IOException)
            {
                continue;
            }
            catch (UnauthorizedAccessException)
            {
                continue;
            }

            foreach (Match match in Invalid.Matches(text))
                names.Add(match.Groups[1].Value.Trim());
        }

        return names.ToList();
    }

    /// <summary>The installed AD1259 mods to set aside: the known ones plus any the logs reported.</summary>
    public static IReadOnlyList<(string Name, string Directory)> ToSetAside(IEnumerable<AD1259ModStatus> mods, IEnumerable<string> reported)
    {
        var wanted = new HashSet<string>(Known.Concat(reported), StringComparer.OrdinalIgnoreCase);
        return mods
            .Where(status => !status.Mod.BaseGame && status.Directories.Count > 0
                && (wanted.Contains(status.Mod.Id) || wanted.Contains(status.Mod.Name)
                    || status.Directories.Any(directory => wanted.Contains(Path.GetFileName(directory)))))
            .SelectMany(status => status.Directories.Select(directory => (status.Mod.Name, directory)))
            .ToList();
    }
}
