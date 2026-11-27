using System.Text.RegularExpressions;

namespace BannerlordCoopLauncher.Core.Server;

/// <summary>
/// Play hold: once the host lets time run from the tool, the campaign keeps running until the host
/// pauses it, also while every player is in a settlement or a battle. Coop pauses the campaign by itself
/// then (its auto pause), and this puts the host's speed back.
/// </summary>
/// <remarks>
/// <para>
/// Coop writes its auto pause as two console lines, in either order by build: "Pausing campaign because
/// every connected player is occupied" (older) or "Occupancy pause ownership "acquired"" (newer, with the state in quotes), next to
/// "Applying server time control" from a Play speed to Pause. Only that pair counts. A pause a player asked
/// for (Peer requested time control), a join pause, Coop's pause of an empty server, or a game that was
/// already paused is left alone.
/// </para>
/// <para>
/// The host's own pause ends the hold, and so does a server that stops. When Coop pauses more than
/// <see cref="MostResends"/> times in <see cref="ResendWindow"/>, the hold lets go instead of fighting it.
/// </para>
/// </remarks>
public sealed class PlayHold
{
    public static readonly TimeSpan PairWindow = TimeSpan.FromSeconds(2);
    public const int MostResends = 5;
    public static readonly TimeSpan ResendWindow = TimeSpan.FromMinutes(1);

    private static readonly Regex AutoPause = new(
        @"\[Coop\] (?:Pausing campaign because every connected player is occupied|Occupancy pause ownership ""?acquired""?)",
        RegexOptions.CultureInvariant);
    private static readonly Regex Applied = new(
        @"\[Coop\] Applying server time control: current=""?(?<current>\w+)""? requested=""?(?<requested>\w+)""? effective=""?(?<effective>\w+)""?",
        RegexOptions.CultureInvariant);
    private static readonly Regex PeerPause = new(
        @"\[Coop\] Peer requested time control: .*\bmode=""?Pause\b", RegexOptions.CultureInvariant);

    // Safe on the console reader thread. Only the window thread changes the hold state.
    public static bool IsTimeControlLine(string line) =>
        PeerPause.IsMatch(line) || Applied.IsMatch(line) || AutoPause.IsMatch(line);

    private readonly Queue<DateTime> _resends = new();
    private DateTime? _autoPauseAt;
    private DateTime? _playToPauseAt;
    private DateTime? _peerPauseAt;

    /// <summary>The host's time command that is held, or null when nothing is held.</summary>
    public string? Command { get; private set; }

    public bool Held => Command != null;

    /// <summary>The speed held (<see cref="JoinPause.NormalMode"/> or FastMode), or null.</summary>
    public string? Mode => Command == null ? null : JoinPause.HostMode(Command);

    /// <summary>True when the last line made the hold let go, because Coop kept pausing.</summary>
    public bool GaveUp { get; private set; }

    /// <summary>
    /// Goes up with every change the host makes (a time command, a stop), so a reply or a resend that was
    /// on its way can tell a newer press came after it.
    /// </summary>
    public int Generation { get; private set; }

    /// <summary>
    /// A resend queued for <paramref name="command"/> at <paramref name="generation"/> still applies at its turn: no newer
    /// press, the same speed held, and pause on join (<paramref name="joinPausing"/>) does not hold the game.
    /// </summary>
    public bool ResendStands(int generation, string command, bool joinPausing) =>
        Generation == generation && Command == command && !joinPausing;

    /// <summary>A command the host sent from the tool: a Play speed is held, a pause ends the hold.</summary>
    public void HostSent(string command)
    {
        var mode = JoinPause.HostMode(command);
        if (mode == null)
            return;
        Command = mode == JoinPause.PauseMode ? null : command.Trim();
        Generation++;
        GaveUp = false;
        Forget();
    }

    /// <summary>
    /// A press the server refused changed nothing: the hold goes back to <paramref name="command"/>, the
    /// <see cref="Command"/> from before it (null for none). Nothing changes when a newer press came after
    /// that one (<paramref name="generation"/> is the <see cref="Generation"/> the press made).
    /// </summary>
    public void Restore(string? command, int generation)
    {
        if (generation != Generation)
            return;
        Command = command;
        GaveUp = false;
        Forget();
    }

    /// <summary>The server stopped or started again: nothing is held and the resend count starts over.</summary>
    public void Stop()
    {
        Command = null;
        Generation++;
        GaveUp = false;
        _resends.Clear();
        Forget();
    }

    /// <summary>
    /// A console line from the running server. Returns true when <see cref="Command"/> should go out
    /// now. <paramref name="joinPausing"/> is true while pause on join holds the game, which the hold
    /// leaves alone.
    /// </summary>
    public bool Line(string? line, DateTime now, bool joinPausing)
    {
        GaveUp = false;
        if (Command == null || string.IsNullOrEmpty(line) || !line.Contains("[Coop] ", StringComparison.Ordinal))
            return false;

        if (PeerPause.IsMatch(line))
        {
            _peerPauseAt = now;
            return false;
        }

        var applied = Applied.Match(line);
        if (applied.Success)
        {
            var fromPlay = JoinPause.ModeIn(applied.Groups["current"].Value) is JoinPause.NormalMode or JoinPause.FastMode;
            var toPause = applied.Groups["requested"].Value == JoinPause.PauseMode && applied.Groups["effective"].Value == JoinPause.PauseMode;
            var askedByPlayer = _peerPauseAt is { } asked && now - asked <= PairWindow;
            _playToPauseAt = fromPlay && toPause && !askedByPlayer ? now : null;
            _peerPauseAt = null;
        }
        else if (AutoPause.IsMatch(line))
            _autoPauseAt = now;
        else
            return false;

        if (_autoPauseAt is not { } auto || _playToPauseAt is not { } paused || (auto - paused).Duration() > PairWindow)
            return false;
        Forget();
        if (joinPausing)
            return false;

        while (_resends.Count > 0 && now - _resends.Peek() > ResendWindow)
            _resends.Dequeue();
        if (_resends.Count >= MostResends)
        {
            Command = null;
            Generation++;
            _resends.Clear();
            GaveUp = true;
            return false;
        }

        _resends.Enqueue(now);
        return true;
    }

    private void Forget()
    {
        _autoPauseAt = null;
        _playToPauseAt = null;
        _peerPauseAt = null;
    }
}
