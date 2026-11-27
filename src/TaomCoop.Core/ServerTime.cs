using BannerlordCoopLauncher.Core.Server;

namespace TaomCoop.Core;

/// <summary>
/// The Pause and Play buttons next to Stop in the TAOM and Burning Empires server tools: the commands
/// they send, when they can be pressed, and the short line they show. The commands are the time modes
/// pause on join uses (<see cref="JoinPause"/>).
/// </summary>
public static class ServerTime
{
    public const string PauseCommand = JoinPause.PauseCommand;
    public const string PlayCommand = JoinPause.SetModeCommand + " " + JoinPause.NormalMode;
    public const string FastCommand = JoinPause.SetModeCommand + " " + JoinPause.FastMode;

    /// <summary>The command for a time mode (<see cref="JoinPause.PauseMode"/>, NormalMode or FastMode).</summary>
    public static string Command(string mode) => mode switch
    {
        JoinPause.PauseMode => PauseCommand,
        JoinPause.FastMode => FastCommand,
        _ => PlayCommand,
    };

    /// <summary>Only a campaign that is up takes a time mode, and not while another action holds the controls.</summary>
    public static bool CanUse(bool serving, bool busy) => serving && !busy;

    /// <summary>
    /// The line shown after a press: the server's own failure line when it refused, or a short
    /// done line. <paramref name="sent"/> is false when the command never reached the server; the
    /// reply is then the reason. <paramref name="held"/> is false for a server the window did not start,
    /// which Play hold can't follow, so Play then runs without the hold.
    /// </summary>
    public static (string Line, bool Failed) Result(string mode, bool sent, string? reply, bool held = true)
    {
        var lines = (reply ?? "").Replace("\r", "").Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (!sent)
            return (lines.FirstOrDefault() ?? "The command was not sent.", true);
        // The write began but the pipe was full: it may still land, so it is neither done nor a failure to try again.
        if (lines.Contains(ServerCommandQueue.ConsoleSlow))
            return (ServerCommandQueue.ConsoleSlow, false);
        // A command the console queue never handed over (a server this window did not start) did nothing.
        var failed = lines.FirstOrDefault(line => CommandReply.Tone(line) != CommandReplyTone.Normal
            || line.StartsWith("The server refused", StringComparison.OrdinalIgnoreCase)
            || line.StartsWith(BannerlordCoopLauncher.Core.Support.ConsoleQueue.NotPickedUp, StringComparison.Ordinal));
        if (failed != null)
            return (failed, true);
        return (mode switch
        {
            JoinPause.PauseMode => "The server is paused.",
            JoinPause.FastMode => held ? "Play held at 2x speed. The game runs until you press Pause." : "Server running at 2x speed.",
            _ => held ? "Play held. The game runs until you press Pause." : "Server running at normal speed.",
        }, false);
    }
}
