using BannerlordCoopLauncher.Core.Server;
using TaomCoop.Core;

namespace RealmOfThronesHost;

// Play hold (BannerlordCoopLauncher.Core\Server\PlayHold.cs): after Play the campaign runs until the host
// presses Pause, also when Coop pauses it because every player is in a settlement or a battle. The server
// stopping or starting again ends the hold.
public partial class MainWindow
{
    private const string PlayHoldResumedText = "Coop paused the game because every player is in a settlement or a battle. Play is held, so the game runs again.";
    private const string PlayHoldMaybeText = "Coop paused the game because every player is in a settlement or a battle. Play is held, so Play went out again, but the server's console is slow, so the game may not run yet. If it stays paused, press Play.";
    private const string PlayHoldFailedText = "Play hold could not start the game again. Press Play.";
    private const string PlayHoldGaveUpText = "Coop kept pausing the game, so Play hold stopped. Press Play to hold it again.";

    private readonly PlayHold _playHold = new();

    // A Play hold resend still applies at its turn: the host made no newer press, the same speed is held, and pause on
    // join does not hold the game.
    private bool PlayHoldStillStands(int generation, string command) =>
        _playHold.ResendStands(generation, command, _joinPause.Pausing);

    // A [Coop] line of this window's own server, on the window thread.
    private async void NotePlayHold(string line)
    {
        if (_playHold.Line(line, DateTime.UtcNow, _joinPause.Pausing))
        {
            var command = _playHold.Command!;
            var generation = _playHold.Generation;
            var sent = false;
            var unconfirmed = false;
            try
            {
                // Recorded as the host's held speed now, in queue order, so a join pause step queued after it does not undo
                // it. At its turn the resend goes out only while the same hold stands and no join pause holds the game.
                _joinPause.HostSent(command);
                var run = CurrentRun();
                var reply = await _commands.Enqueue(async () =>
                {
                    var lines = await ReadCommandReply(command, run, () => PlayHoldStillStands(generation, command));
                    // A command that never reached the server (no server, a broken pipe) does not count as sent.
                    return _commandWriteFailed ? null : lines;
                }, () => PlayHoldStillStands(generation, command));
                if (reply == null && !PlayHoldStillStands(generation, command))
                    return;
                sent = reply != null && !ServerTime.Result(JoinPause.HostMode(command) ?? JoinPause.NormalMode, true, string.Join("\n", reply), false).Failed;
                unconfirmed = sent && ServerCommandQueue.Unconfirmed(reply);
            }
            catch (Exception ex)
            {
                NoteLog(ex.Message);
            }

            NoteLog(!sent ? PlayHoldFailedText : unconfirmed ? PlayHoldMaybeText : PlayHoldResumedText);
            if (!sent)
                SetControl(PlayHoldFailedText, failed: true);
        }
        else if (_playHold.GaveUp)
        {
            NoteLog(PlayHoldGaveUpText);
            SetControl(PlayHoldGaveUpText, failed: true);
        }
    }
}
