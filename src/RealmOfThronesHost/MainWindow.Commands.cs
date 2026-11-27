using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using BannerlordCoopLauncher.Core.Launch;
using BannerlordCoopLauncher.Core.Modules;
using BannerlordCoopLauncher.Core.Saves;
using BannerlordCoopLauncher.Core.Support;
using BannerlordCoopLauncher.Core.Updates;
using Microsoft.Win32;
using RealmOfThrones.Core;
using TaomCoop.Core;

namespace RealmOfThronesHost;

public partial class MainWindow
{
    private bool ServerIsRunning() => DedicatedServerWatch.IsRunning(_settings.ServerRoot);

    private bool OwnsServerProcess() => _process is { HasExited: false };

    // A server this window did not start (the tool was closed and opened again)
    // still reads commands from server-data\console-queue.
    private bool CommandPipeReady() => OwnsServerProcess() || ServerIsRunning();

    private string CheatBlockReason() => "Server is not running.";

    private IReadOnlyList<string> CommandQueueRoots() => new[]
    {
        Path.Combine(_settings.ServerRoot, "server-data"),
        Path.Combine(_settings.DataRoot, "server-data")
    };

    // A server this window did not start saves through the command queue.
    // launcher.server.save is the dedicated server's own save command.
    private bool AskQueuedSave()
    {
        var reply = ConsoleQueue.Send(CommandQueueRoots(), "launcher.server.save");
        if (reply.Text.StartsWith("Usage", StringComparison.OrdinalIgnoreCase))
            reply = ConsoleQueue.Send(CommandQueueRoots(), "launcher.server.save " + ConfiguredSaveName());
        Dispatcher.Invoke(() => NoteLog("Save: " + reply.Text));
        return reply.Succeeded
            && !reply.Text.Contains("failed", StringComparison.OrdinalIgnoreCase)
            && !reply.Text.Contains("not started", StringComparison.OrdinalIgnoreCase)
            && !reply.Text.Contains("unavailable", StringComparison.OrdinalIgnoreCase);
    }

    // The reply of a server this window did not start, through its console queue. Not picked up: the command was not sent,
    // a failure for every caller. Picked up with no answer yet: it may still land (ConsoleSlow), so the Cheats page never
    // says Done or records an undo for it, and the buttons never resend it.
    private async Task<(string Text, bool Sent)> SendThroughQueue(string command)
    {
        Remember("> " + command);
        try
        {
            var reply = await Task.Run(() => ConsoleQueue.Send(CommandQueueRoots(), command));
            var text = !reply.PickedUp ? ServerCommandQueue.NotSent(reply.Text)
                : !reply.Answered && !reply.Failed ? ServerCommandQueue.ConsoleSlow
                : reply.Failed ? "The server refused that command: " + reply.Text : reply.Text;
            Remember(text);
            return (text, reply.PickedUp);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            var text = ServerCommandQueue.NotSent(ex.Message);
            Remember(text);
            return (text, false);
        }
    }

    private void Send_Click(object sender, RoutedEventArgs e)
    {
        var command = CommandBox.Text.Trim();
        CommandBox.Clear();
        // Checks the line against the Coop command list first (MainWindow.Console.cs).
        SendConsoleCommand(command);
    }

    private void CommandBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
            Send_Click(sender, e);
    }

    // A PC with no browser set makes the open throw, so a failure is a log line with the link instead.
    private void OpenCommandReference_Click(object sender, RoutedEventArgs e)
    {
        const string cheats = "https://bannerlordcoop.com/cheats?tab=all";
        if (!ShellOpen.TryOpen(cheats, out _))
            NoteLog("Could not open the browser. " + cheats);
    }

    // ---- Pause and Play ------------------------------------------------------------------------

    private void PauseServer_Click(object sender, RoutedEventArgs e) => _ = SetServerTime(BannerlordCoopLauncher.Core.Server.JoinPause.PauseMode);

    private void PlayServer_Click(object sender, RoutedEventArgs e) => _ = SetServerTime(BannerlordCoopLauncher.Core.Server.JoinPause.NormalMode);

    // The Play button's right click menu.
    private void PlayServerFast_Click(object sender, RoutedEventArgs e) => _ = SetServerTime(BannerlordCoopLauncher.Core.Server.JoinPause.FastMode);

    // Goes out the way the Cheats page sends (stdin, or the command queue for a server this window did
    // not start) and reads the reply, so a refusal shows instead of a done line.
    private async Task SetServerTime(string mode)
    {
        if (_busy || _startingServer)
        {
            SetControl("Wait for the current action to finish.", failed: true);
            return;
        }

        if (!CommandPipeReady())
        {
            SetControl(CheatBlockReason(), failed: true);
            ShowTimeButtons(false);
            return;
        }

        var command = ServerTime.Command(mode);
        // Play holds its speed until Pause. The hold changes before the send, so a Coop pause handled meanwhile
        // can't put the old speed back. A server this window did not start holds nothing: its lines never reach
        // the hold.
        var heldBefore = _playHold.Command;
        var holds = OwnsServerProcess();
        if (holds)
            _playHold.HostSent(command);
        else
            _playHold.Stop();
        // The host's own speed wins over a join pause too.
        _joinPause.HostSent(command);
        var press = _playHold.Generation;
        var reply = await RunCommandAndRead(command);
        var result = ServerTime.Result(mode, true, string.Join("\n", reply), holds);
        // A refused press changed nothing on the server, so the hold goes back to what it was.
        if (result.Failed)
            _playHold.Restore(heldBefore, press);
        SetControl(result.Line, failed: result.Failed);
    }

    // Pause and Play only while the campaign is up and no other server action runs.
    private void ShowTimeButtons(bool serving)
    {
        var on = ServerTime.CanUse(serving, _busy || _startingServer);
        PauseServerButton.IsEnabled = on;
        PlayServerButton.IsEnabled = on;
    }

    private string SendCommand(string command)
    {
        var refusal = ServerCommandPolicy.Refusal(command);
        if (refusal != null)
        {
            Remember(refusal);
            return refusal;
        }

        if (command.Trim().Equals("save", StringComparison.OrdinalIgnoreCase))
        {
            _ = ShowWorldSave();
            return "Saving the world.";
        }

        if (!CommandPipeReady())
        {
            var message = CheatBlockReason();
            Remember(message);
            return message;
        }

        // Every write goes through the command queue in its turn (RunCommandAndRead); the reply shows when it is read.
        _ = RunCommandAndRead(command);
        return "Sent.";
    }
}
