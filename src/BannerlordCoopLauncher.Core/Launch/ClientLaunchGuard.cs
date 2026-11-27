using System.Diagnostics;

namespace BannerlordCoopLauncher.Core.Launch;

public static class ClientLaunchGuard
{
    public static void EnsureSingleGame()
    {
        if (Process.GetProcessesByName("Bannerlord").Length > 0)
            throw new InvalidOperationException("Bannerlord is already open. Close it before connecting again. A second copy crashes while the first one holds the config file.");
    }

    public const string AlreadyOpenTitle = "Bannerlord is already open";
    public const string AlreadyOpenAsk = "Bannerlord is still running on this PC, maybe a game that froze or did not finish closing. A second copy would crash.\n\nClose it now and connect? Anything not saved in that game is lost.";
    public const string AlreadyOpenKept = "Bannerlord is still open, so Connect stopped. Close it, then click Connect again.";
    public const string ClosedOpenGame = "Closed the Bannerlord that was still running.";
    public const string CouldNotCloseGame = "Could not close Bannerlord. End Bannerlord.exe in Task Manager (Details tab), then click Connect again.";

    public static bool IsGameRunning() => Process.GetProcessesByName("Bannerlord").Length > 0;

    public const string SecondCopyClosed = "Another Bannerlord was open when this one closed. Two copies clash over the same log files. If you did not start the other one on purpose, close it and click Connect once.";

    /// <summary>True when a Bannerlord other than <paramref name="exitedId"/> is running.</summary>
    public static bool OtherGameRunning(int exitedId)
    {
        var running = Process.GetProcessesByName("Bannerlord");
        try
        {
            return running.Any(process => process.Id != exitedId);
        }
        finally
        {
            foreach (var process in running)
                process.Dispose();
        }
    }

    /// <summary>
    /// Ends every running Bannerlord, a frozen one included, and waits for it to go.
    /// True when none is left.
    /// </summary>
    public static async Task<bool> CloseRunningGamesAsync() =>
        await CloseAsync(Process.GetProcessesByName("Bannerlord"), TimeSpan.FromSeconds(15)) || !IsGameRunning();

    public static async Task<bool> CloseAsync(IEnumerable<Process> processes, TimeSpan wait)
    {
        var closed = true;
        foreach (var process in processes)
        {
            using (process)
            {
                try
                {
                    if (!process.HasExited)
                        process.Kill(entireProcessTree: true);
                    using var timeout = new CancellationTokenSource(wait);
                    await process.WaitForExitAsync(timeout.Token);
                }
                catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException or OperationCanceledException)
                {
                    // A game that was already ending can refuse the kill; only one still there counts.
                    closed &= HasExited(process);
                }
            }
        }

        return closed;
    }

    private static bool HasExited(Process process)
    {
        try { return process.HasExited; }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            // Started as administrator: this tool can not even look at it.
            return false;
        }
    }

    public static bool UnlockEngineConfig(string documentsRoot)
    {
        var path = Path.Combine(documentsRoot, "Configs", "engine_config.txt");
        if (!File.Exists(path))
            return false;
        var attributes = File.GetAttributes(path);
        if ((attributes & FileAttributes.ReadOnly) == 0)
            return false;
        File.SetAttributes(path, attributes & ~FileAttributes.ReadOnly);
        return true;
    }

    public static void KeepEngineConfigWritable(string documentsRoot)
    {
        _ = Task.Run(async () =>
        {
            while (Process.GetProcessesByName("Bannerlord").Length > 0)
            {
                try { UnlockEngineConfig(documentsRoot); }
                catch { }
                await Task.Delay(2000);
            }
        });
    }
}
