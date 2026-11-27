// Ported from CoopKeep (https://github.com/Aderimo/bannerlord-coopkeep), Server/ServerResourceMonitor.cs,
// translated from Turkish and changed for the Coodsz tools (no priority change).

using System.Diagnostics;

namespace BannerlordCoopLauncher.Core.Server;

/// <summary>What the server uses right now.</summary>
/// <param name="MemoryBytes">Working set of every process counted.</param>
/// <param name="CpuPercent">Average CPU since the last sample, as a share of all cores.</param>
/// <param name="ProcessCount">How many processes were counted.</param>
public readonly record struct ServerResources(long MemoryBytes, double CpuPercent, int ProcessCount)
{
    public double MemoryGigabytes => MemoryBytes / 1024d / 1024d / 1024d;

    public static ServerResources Empty => new(0, 0, 0);
}

/// <summary>
/// Memory and CPU of the server's process tree.
/// </summary>
/// <remarks>
/// <para>
/// The program that starts the server is not where the memory is. Coodsz Host starts
/// PowerShell, which starts engine\dotnet\dotnet.exe under the server folder; the TAOM
/// server tool starts BannerlordCoopServer.exe, which starts the same engine. Measuring
/// only the first process would show a few megabytes.
/// </para>
/// <para>
/// Processes are matched by <b>path</b>: their executable sits under the server folder.
/// Matching by name would be wrong, because the game client running on the same PC has
/// similar names. The process this tool started is counted too (the PowerShell wrapper).
/// </para>
/// <para>The dedicated server has no memory limit setting; this only measures.</para>
/// </remarks>
public sealed class ServerResourceMonitor(string serverRootDirectory)
{
    private readonly string _root = NormalizeDirectory(serverRootDirectory);

    private TimeSpan _previousCpuTime;
    private DateTimeOffset _previousSampleAt;
    private bool _hasPreviousSample;

    public string Root => _root;

    /// <summary>Takes one sample.</summary>
    /// <param name="rootProcessId">The process this tool started, when it did; counted even outside the folder.</param>
    public ServerResources Sample(int? rootProcessId)
    {
        long memory = 0;
        var cpuTime = TimeSpan.Zero;
        var count = 0;

        foreach (var process in Process.GetProcesses())
        {
            try
            {
                if (!BelongsToServer(process, rootProcessId))
                    continue;
                memory += process.WorkingSet64;
                cpuTime += process.TotalProcessorTime;
                count++;
            }
            catch (Exception e) when (e is InvalidOperationException
                                       or System.ComponentModel.Win32Exception
                                       or NotSupportedException)
            {
                // The process ended meanwhile, or it refuses access; skip it.
            }
            finally
            {
                process.Dispose();
            }
        }

        var now = DateTimeOffset.UtcNow;
        var cpuPercent = 0d;
        if (_hasPreviousSample && count > 0)
        {
            var wall = (now - _previousSampleAt).TotalMilliseconds;
            var used = (cpuTime - _previousCpuTime).TotalMilliseconds;
            if (wall > 0)
                cpuPercent = Math.Clamp(used / (wall * Environment.ProcessorCount) * 100d, 0, 100);
        }

        _previousCpuTime = cpuTime;
        _previousSampleAt = now;
        _hasPreviousSample = count > 0;

        return count == 0 ? ServerResources.Empty : new ServerResources(memory, cpuPercent, count);
    }

    /// <summary>Call when the server stopped, so the next sample does not give a wrong CPU value.</summary>
    public void Reset()
    {
        _hasPreviousSample = false;
        _previousCpuTime = TimeSpan.Zero;
    }

    /// <summary>Is this executable under the server folder?</summary>
    public static bool IsUnderRoot(string? executablePath, string? serverRoot)
    {
        var root = NormalizeDirectory(serverRoot);
        return root.Length > 0 && !string.IsNullOrWhiteSpace(executablePath)
            && executablePath.StartsWith(root, StringComparison.OrdinalIgnoreCase);
    }

    private bool BelongsToServer(Process process, int? rootProcessId)
    {
        if (rootProcessId is { } root && process.Id == root)
            return true;
        if (_root.Length == 0)
            return false;
        var path = process.MainModule?.FileName;
        return path is not null && path.StartsWith(_root, StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizeDirectory(string? directory)
    {
        if (string.IsNullOrWhiteSpace(directory))
            return string.Empty;
        string full;
        try
        {
            full = Path.GetFullPath(directory);
        }
        catch (Exception e) when (e is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return string.Empty;
        }

        return full.EndsWith(Path.DirectorySeparatorChar) ? full : full + Path.DirectorySeparatorChar;
    }
}
