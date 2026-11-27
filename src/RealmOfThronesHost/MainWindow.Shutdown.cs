using System.IO;
using BannerlordCoopLauncher.Core.Server;
using TaomCoop.Core;

namespace RealmOfThronesHost;

// Stop and Restart send the server its own "stop" over the console pipe (it writes its
// save on stop and exits) and wait up to 2 minutes before StopServer ends what is left.
// Restart first warns the players who are on with "say". Only a server this window
// started has that pipe; any other one is ended as before.
public partial class MainWindow
{
    private static readonly TimeSpan StopWait = TimeSpan.FromSeconds(120);

    // In its turn of the command queue (WriteQueued), so it never writes into another command or its reply read.
    private Task<bool> WriteToServerConsole(string line)
    {
        if (!ServerCommand.IsAllowed(line))
            return Task.FromResult(false);
        var process = _process;
        if (process is not { HasExited: false })
            return Task.FromResult(false);
        return WriteQueued(() => WriteLine(process, line));
    }

    private bool WriteLine(System.Diagnostics.Process process, string line)
    {
        try
        {
            // Off the calling thread with one 5 second deadline for the lock and the write. A write that never began is not
            // sent (and never goes out later), so the fallback or a forced stop runs. One stuck on a full pipe may still land,
            // so it counts as written: the save or stop is not written a second time, and the stop wait still ends it.
            return ServerCommandQueue.TryWriteWithin(_stdinLock, ServerCommandQueue.StdinLockWait, () =>
            {
                process.StandardInput.WriteLine(line);
                process.StandardInput.Flush();
            }) != ConsoleWrite.NotSent;
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException or ObjectDisposedException)
        {
            return false;
        }
    }

    // Runs off the window thread.
    private void AskServerToStop()
    {
        _stopAsked = true;
        var process = _process;
        // This runs off the window thread, so it may wait for its turn here.
        if (!WriteToServerConsole(ServerCommand.Stop()).GetAwaiter().GetResult() || process == null)
            return;
        NoteLog("Sent stop. Waiting up to 2 minutes for the server's save on stop.");
        try
        {
            if (!process.WaitForExit((int)StopWait.TotalMilliseconds))
                NoteLog("The server did not finish stopping within 2 minutes, so it was ended. The last save it wrote is kept.");
        }
        catch (InvalidOperationException)
        {
        }
    }

    private async Task WarnPlayersOfRestart()
    {
        if (_onlinePlayers.Count == 0)
            return;
        if (!await WriteToServerConsole(ServerCommand.Say("The server restarts in 30 seconds. The campaign is saved first.")))
        {
            NoteLog("Players were not warned before the restart. A server that was running before this tool opened does not take chat messages from it.");
            return;
        }

        SetControl("Players were told the server restarts in 30 seconds.");
        await Task.Delay(TimeSpan.FromSeconds(20));
        await WriteToServerConsole(ServerCommand.Say("The server restarts in 10 seconds."));
        await Task.Delay(TimeSpan.FromSeconds(10));
    }
}
