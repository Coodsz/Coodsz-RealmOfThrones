using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;

namespace TaomCoop.Core;

public readonly record struct MemoryState(ulong TotalBytes, ulong AvailableBytes, uint LoadPercent);

// TAOM froze at 95 to 97 percent memory on a 16 GB laptop that also ran the dedicated
// server. The launcher and the server tool say so before the game or the server starts.
public static class LowMemory
{
    public const ulong LowFreeBytes = 2UL * 1024 * 1024 * 1024;
    public const uint HighLoadPercent = 90;
    public const ulong SharedPcBytes = 24UL * 1024 * 1024 * 1024;

    public static bool IsLow(MemoryState state) =>
        state.AvailableBytes < LowFreeBytes || state.LoadPercent > HighLoadPercent;

    // A server and the game on one PC need more than 16 GB between them.
    public static bool TooSmallToShare(MemoryState state) => state.TotalBytes < SharedPcBytes;

    /// <summary>The client launcher's warning, or null when memory is fine. <paramref name="game"/> names the overhaul the launcher starts, like TAOM or Burning Empires.</summary>
    public static string? ClientWarning(MemoryState? state, bool serverOnThisPc, string game = "TAOM")
    {
        if (state is not { } memory || memory.TotalBytes == 0)
            return null;
        var free = Gb(memory.AvailableBytes, 1);
        var total = Gb(memory.TotalBytes, 0);
        if (serverOnThisPc && TooSmallToShare(memory))
        {
            return IsLow(memory)
                ? "Low memory: only " + free + " GB of " + total + " GB is free, and a dedicated server is running on this PC too. " + game + " and the server together can fill it and freeze the game. Run the server on another PC if you can, close heavy apps such as browsers, and let Windows manage the page file size."
                : "A dedicated server is running on this PC, which has " + total + " GB of memory. " + game + " and the server together can fill it and freeze the game. Run the server on another PC if you can, close heavy apps such as browsers, and let Windows manage the page file size.";
        }

        return IsLow(memory)
            ? "Low memory: only " + free + " GB of " + total + " GB is free. " + game + " can freeze or close like this. Close heavy apps such as browsers before you play, and let Windows manage the page file size."
            : null;
    }

    /// <summary>The server tool's warning at Start server, or null when memory is fine.</summary>
    public static string? ServerWarning(MemoryState? state, bool gameOnThisPc)
    {
        if (state is not { } memory || memory.TotalBytes == 0)
            return null;
        var free = Gb(memory.AvailableBytes, 1);
        var total = Gb(memory.TotalBytes, 0);
        if (IsLow(memory))
            return "Low memory: only " + free + " GB of " + total + " GB is free. The server and a game on this PC can freeze like this. Close heavy apps such as browsers, play on another PC than the server if you can, and let Windows manage the page file size.";
        return gameOnThisPc && TooSmallToShare(memory)
            ? "Bannerlord is running on this PC too, which has " + total + " GB of memory. The game and the server together can fill it and freeze the game. Run the server on another PC if you can, close heavy apps such as browsers, and let Windows manage the page file size."
            : null;
    }

    public static MemoryState? Read()
    {
        try
        {
            var status = new MemoryStatusEx { Length = (uint)Marshal.SizeOf<MemoryStatusEx>() };
            return GlobalMemoryStatusEx(ref status)
                ? new MemoryState(status.TotalPhys, status.AvailPhys, status.MemoryLoad)
                : null;
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException)
        {
            return null;
        }
    }

    /// <summary>A Coop dedicated server on this PC: BannerlordCoopServer.exe, or the engine's own
    /// dotnet.exe under a server folder's engine\dotnet.</summary>
    public static bool DedicatedServerRunning() =>
        Any("BannerlordCoopServer", _ => true)
        || Any("dotnet", path => path != null && path.Contains(@"\engine\dotnet\", StringComparison.OrdinalIgnoreCase));

    public static bool GameRunning() => Any("Bannerlord", _ => true);

    private static bool Any(string name, Func<string?, bool> match)
    {
        Process[] processes;
        try
        {
            processes = Process.GetProcessesByName(name);
        }
        catch (InvalidOperationException)
        {
            return false;
        }

        var found = false;
        foreach (var process in processes)
        {
            using (process)
            {
                if (found)
                    continue;
                string? path = null;
                try { path = process.MainModule?.FileName; }
                catch (Exception) { }
                found = match(path);
            }
        }

        return found;
    }

    private static string Gb(ulong bytes, int decimals) =>
        Math.Round(bytes / 1073741824.0, decimals).ToString(decimals == 0 ? "0" : "0.0", CultureInfo.InvariantCulture);

    [StructLayout(LayoutKind.Sequential)]
    private struct MemoryStatusEx
    {
        public uint Length;
        public uint MemoryLoad;
        public ulong TotalPhys;
        public ulong AvailPhys;
        public ulong TotalPageFile;
        public ulong AvailPageFile;
        public ulong TotalVirtual;
        public ulong AvailVirtual;
        public ulong AvailExtendedVirtual;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GlobalMemoryStatusEx(ref MemoryStatusEx buffer);
}
