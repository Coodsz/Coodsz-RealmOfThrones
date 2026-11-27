using System.Diagnostics;

namespace TaomCoop.Core;

public static class DedicatedServerWatch
{
    public static bool IsRunning(string? serverRoot) => TryStarted(serverRoot, out _);

    public static bool IsEngine(string? processPath, string? serverRoot)
    {
        if (string.IsNullOrWhiteSpace(processPath))
            return false;
        if (!string.IsNullOrWhiteSpace(serverRoot))
            return IsUnder(processPath, Path.Combine(serverRoot, "engine", "dotnet"));
        return Path.GetFullPath(processPath).IndexOf(@"\DedicatedServer\engine\dotnet", StringComparison.OrdinalIgnoreCase) >= 0;
    }

    public static bool IsUnder(string? path, string? root)
    {
        if (string.IsNullOrWhiteSpace(path) || string.IsNullOrWhiteSpace(root))
            return false;
        var full = Path.GetFullPath(path);
        var baseRoot = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return full.Equals(baseRoot, StringComparison.OrdinalIgnoreCase)
            || full.StartsWith(baseRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    }

    public static bool LockHeld(string? serverRoot)
    {
        if (string.IsNullOrWhiteSpace(serverRoot))
            return false;
        var path = Path.Combine(serverRoot, "server-data", "server-instance.lock");
        if (!File.Exists(path))
            return false;
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
            return false;
        }
        catch (IOException)
        {
            return true;
        }
        catch (UnauthorizedAccessException)
        {
            return true;
        }
    }

    /// <summary>A server process of this folder is running (the lock file is not asked).</summary>
    public static bool ProcessRunning(string? serverRoot)
    {
        DateTime? started = null;
        return Consider(Process.GetProcessesByName("BannerlordCoopServer"), serverRoot, engineOnly: false, ref started)
            | Consider(Process.GetProcessesByName("dotnet"), serverRoot, engineOnly: true, ref started);
    }

    public static bool TryStarted(string? serverRoot, out DateTime? started) => TryStarted(serverRoot, out started, out _);

    /// <summary>
    /// The same, with the id of the server process that was found first (null when only the lock file says a server runs).
    /// The command queue keeps both, so a server restarted by someone else is another run even when Windows won't give its
    /// start time.
    /// </summary>
    public static bool TryStarted(string? serverRoot, out DateTime? started, out int? processId)
    {
        started = null;
        processId = null;
        var found = false;
        found |= Consider(Process.GetProcessesByName("BannerlordCoopServer"), serverRoot, engineOnly: false, ref started, ref processId);
        found |= Consider(Process.GetProcessesByName("dotnet"), serverRoot, engineOnly: true, ref started, ref processId);
        if (found || !LockHeld(serverRoot))
            return found;
        var lockPath = Path.Combine(serverRoot!, "server-data", "server-instance.lock");
        started = File.Exists(lockPath) ? File.GetCreationTime(lockPath) : DateTime.Now;
        return true;
    }

    private static bool Consider(Process[] processes, string? serverRoot, bool engineOnly, ref DateTime? started)
    {
        int? processId = null;
        return Consider(processes, serverRoot, engineOnly, ref started, ref processId);
    }

    private static bool Consider(Process[] processes, string? serverRoot, bool engineOnly, ref DateTime? started, ref int? processId)
    {
        var found = false;
        foreach (var process in processes)
        {
            try
            {
                string? path = null;
                try { path = process.MainModule?.FileName; }
                catch (Exception) { }
                if (engineOnly)
                {
                    if (!IsEngine(path, serverRoot))
                        continue;
                }
                else if (!string.IsNullOrWhiteSpace(serverRoot) && path != null && !IsUnder(path, serverRoot))
                    continue;
                found = true;
                try { processId ??= process.Id; }
                catch (Exception) { }
                try
                {
                    if (started == null || process.StartTime < started)
                        started = process.StartTime;
                }
                catch (Exception)
                {
                }
            }
            finally
            {
                process.Dispose();
            }
        }

        return found;
    }
}
