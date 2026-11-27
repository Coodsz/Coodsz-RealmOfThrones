using System.Text.RegularExpressions;

namespace BannerlordCoopLauncher.Core.Server;

/// <summary>What <see cref="JoinPause"/> asks the host tool to do.</summary>
public enum JoinPauseStep
{
    None,
    /// <summary>Ask the server for its time mode, then call <see cref="JoinPause.Paused"/> with it.</summary>
    Pause,
    /// <summary>Send <see cref="JoinPause.ResumeCommand"/> to put back the speed from before the pause.</summary>
    Resume,
}

/// <summary>
/// Pause on join: while a player is joining or loading and has not been seen on the map yet, the
/// campaign is paused, so the world does not run on without them, and the speed from before is put
/// back when everyone is in. It runs from the <c>@DS@ players</c> events Coodsz Host already reads.
/// </summary>
/// <remarks>
/// <para>
/// A player seen on the map once never pauses the game again that session, so a reconnect or a
/// player who walks into a menu does not stop everyone. After <see cref="GiveUpAfter"/> the pause
/// is let go and the players still loading are given up on, so a stuck loader cannot freeze the world.
/// </para>
/// <para>
/// The tool only resumes a pause it made itself. When the game was already paused (the host paused
/// it, or Coop paused an empty server), nothing is sent. When the host sets a speed during the pause,
/// the host's choice wins and the pause is no longer this class's to resume.
/// </para>
/// </remarks>
public sealed class JoinPause
{
    public const string PauseMode = "Pause";
    public const string NormalMode = "Play_1x";
    public const string FastMode = "Play_2x";
    public const string GetModeCommand = "coop.debug.get_time_mode";
    public const string SetModeCommand = "coop.debug.set_time_mode";
    public const string PauseCommand = SetModeCommand + " " + PauseMode;

    public static readonly TimeSpan GiveUpAfter = TimeSpan.FromMinutes(3);

    // What the Coop dedicated server writes 5 seconds after any player (a rejoin too) entered the campaign, when it sets
    // normal speed by itself, also over a pause the host chose (CampaignRuleOverrides in DedicatedServer.LauncherBattleSize).
    private static readonly Regex ServerJoinResume = new(
        @"\[DedicatedServer\]\s+Player synchronization completed; campaign time resumed\.", RegexOptions.CultureInvariant);
    private static readonly Regex ModeWord = new(@"\b(Pause|Play_1x|Play_2x)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly Regex HostTimeCommand = new(
        @"^\s*(?:coop\.debug\.set_time_mode|launcher\.time\.set)\s+(\S+)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private readonly HashSet<string> _seenOnMap = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _givenUp = new(StringComparer.OrdinalIgnoreCase);
    // The last key of each peer index in the list, so a player waited for as #id keeps their state
    // (given up) once the name shows.
    private readonly Dictionary<int, string> _keyById = new();
    private IReadOnlyList<ConnectedPlayer> _players = Array.Empty<ConnectedPlayer>();
    private bool _asking;
    private bool _alreadyPaused;
    private DateTime _since;
    // Counts the host's time commands, so a choice made while a pause is on its way is seen.
    private int _hostCommands;
    private int _hostCommandsAtAsk;
    private int _hostCommandsAtPause;
    // The host's last time command this session was a pause.
    private bool _hostPaused;

    /// <summary>True while the game is paused by this class and will be resumed by it.</summary>
    public bool Holding { get; private set; }

    /// <summary>True while a join pause is on its way or holds the game.</summary>
    public bool Pausing => Holding || _asking;

    /// <summary>The speed put back after the pause.</summary>
    public string ResumeMode { get; private set; } = NormalMode;

    /// <summary>The speed the host last chose with this tool, or null. Used when the server does not say its speed.</summary>
    public string? HostChoice { get; private set; }

    /// <summary>The players still being waited for, for the log line.</summary>
    public IReadOnlyList<string> Waiting { get; private set; } = Array.Empty<string>();

    /// <summary>True when the last <see cref="JoinPauseStep.Resume"/> came from <see cref="GiveUpAfter"/>.</summary>
    public bool GaveUp { get; private set; }

    public string ResumeCommand => SetModeCommand + " " + ResumeMode;

    /// <summary>
    /// How many time commands (set_time_mode or launcher.time.set) the host sent with this tool, so a step taken before
    /// one can be dropped after it. Only time commands move it; any other command <see cref="HostSent"/> sees does not.
    /// </summary>
    public int HostCommandCount => _hostCommands;

    /// <summary>
    /// A new player list from a tool that polls (the Realm of Thrones Server Tool): while the server is not serving,
    /// every one is a new session, so who was on the map and who was given up on are forgotten.
    /// </summary>
    public JoinPauseStep UpdateServing(IReadOnlyList<ConnectedPlayer> players, DateTime now, bool enabled, bool serving)
    {
        if (serving)
            return Update(players, now, enabled);
        Reset();
        return JoinPauseStep.None;
    }

    /// <summary>A key for a player: the name, or the peer index while the name is still the joining placeholder.</summary>
    public static string KeyOf(ConnectedPlayer player) =>
        player.IsJoining ? "#" + player.Id : player.Name.Trim();

    /// <summary>A new player list from the server.</summary>
    public JoinPauseStep Update(IReadOnlyList<ConnectedPlayer> players, DateTime now, bool enabled)
    {
        _players = players ?? Array.Empty<ConnectedPlayer>();
        return Tick(now, enabled);
    }

    /// <summary>Called on the tool's clock too, so the give up time passes without a new player list.</summary>
    public JoinPauseStep Tick(DateTime now, bool enabled)
    {
        GaveUp = false;
        CarryKeys();
        foreach (var player in _players.Where(player => player.IsOnMap))
        {
            _seenOnMap.Add(KeyOf(player));
            if (!player.IsJoining)
                _seenOnMap.Add("#" + player.Id);
        }

        var waiting = _players.Where(player => !player.IsOnMap)
            .Select(KeyOf)
            .Where(key => !_seenOnMap.Contains(key) && !_givenUp.Contains(key))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        Waiting = waiting;

        if (Holding)
        {
            if (!enabled || waiting.Count == 0)
                return Release();
            if (now - _since >= GiveUpAfter)
            {
                _givenUp.UnionWith(waiting);
                GaveUp = true;
                return Release();
            }

            return JoinPauseStep.None;
        }

        if (waiting.Count == 0)
        {
            _alreadyPaused = false;
            return JoinPauseStep.None;
        }

        if (!enabled || _asking || _alreadyPaused)
            return JoinPauseStep.None;
        _asking = true;
        _hostCommandsAtAsk = _hostCommands;
        return JoinPauseStep.Pause;
    }

    /// <summary>
    /// The server's answer to <see cref="GetModeCommand"/> (null when it gave none). Returns true when
    /// the tool should now send <see cref="PauseCommand"/>; false when the game was already paused,
    /// or the host chose a speed while the question was out.
    /// </summary>
    public bool Paused(string? serverReply, DateTime now)
    {
        _asking = false;
        if (_hostCommands != _hostCommandsAtAsk)
            return false;
        var mode = ModeIn(serverReply) ?? HostChoice ?? NormalMode;
        if (mode == PauseMode)
        {
            // Already paused by the host or by Coop: leave it, and do not ask again for these players.
            _alreadyPaused = true;
            return false;
        }

        ResumeMode = mode;
        Holding = true;
        _since = now;
        _hostCommandsAtPause = _hostCommands;
        return true;
    }

    /// <summary>
    /// Called once <see cref="PauseCommand"/> went out and the server took it (answered or not). When
    /// the host chose a speed after <see cref="Paused"/>, the pause may have landed after the host's
    /// choice and nothing would resume it: this returns the command that puts the host's speed back,
    /// or null when nothing is needed.
    /// </summary>
    public string? PauseLanded()
    {
        if (_hostCommands == _hostCommandsAtPause)
            return null;
        Holding = false;
        return HostChoice is null or PauseMode ? null : SetModeCommand + " " + HostChoice;
    }

    /// <summary>The pause could not be sent; try again with the next player list.</summary>
    public void PauseFailed()
    {
        _asking = false;
        Holding = false;
    }

    /// <summary>
    /// A command the host sent from this tool. A time command is the host's own choice: it is
    /// remembered, and a pause this class made is no longer resumed by it.
    /// </summary>
    public void HostSent(string command)
    {
        var mode = HostMode(command);
        if (mode == null)
            return;
        HostChoice = mode;
        _hostPaused = mode == PauseMode;
        _hostCommands++;
        Holding = false;
        // A host who paused, or let time run, during a join has decided for these players.
        _alreadyPaused = Waiting.Count > 0;
    }

    /// <summary>
    /// The server refused <paramref name="command"/>, or it failed. A refused pause is not the host's pause, so it is not
    /// sent again after a join, unless he sent another time command since (<paramref name="hostCommandCount"/> is
    /// <see cref="HostCommandCount"/> right after <see cref="HostSent"/>).
    /// </summary>
    public void HostSendFailed(string command, int hostCommandCount)
    {
        if (HostMode(command) == PauseMode && hostCommandCount == _hostCommands)
            _hostPaused = false;
    }

    /// <summary>The server stopped or started again: a new session.</summary>
    public void Reset()
    {
        _seenOnMap.Clear();
        _givenUp.Clear();
        _keyById.Clear();
        _players = Array.Empty<ConnectedPlayer>();
        _asking = false;
        _alreadyPaused = false;
        _hostPaused = false;
        Holding = false;
        Waiting = Array.Empty<string>();
        GaveUp = false;
    }

    /// <summary>
    /// True when the host's last time command this session was a pause and <paramref name="line"/> is the server's own
    /// resume after a join: the tool sends the host's pause again, so a join never undoes it.
    /// </summary>
    public bool HostPauseUndone(string? line) =>
        _hostPaused && line != null && ServerJoinResume.IsMatch(line);

    /// <summary>The speed a host time command sets (launcher.time.set Play counts as normal speed), or null for another command.</summary>
    public static string? HostMode(string? command)
    {
        var match = HostTimeCommand.Match(command ?? "");
        if (!match.Success)
            return null;
        var word = match.Groups[1].Value;
        return word.Equals("Play", StringComparison.OrdinalIgnoreCase) ? NormalMode : ModeIn(word);
    }

    /// <summary>The time mode word in a server reply, like "Time control set to Play_2x", or null.</summary>
    public static string? ModeIn(string? text)
    {
        var match = ModeWord.Match(text ?? "");
        if (!match.Success)
            return null;
        var word = match.Groups[1].Value;
        return word.Equals(PauseMode, StringComparison.OrdinalIgnoreCase) ? PauseMode
            : word.Equals(FastMode, StringComparison.OrdinalIgnoreCase) ? FastMode
            : NormalMode;
    }

    // A player waited for as #id whose name now shows is the same player: a give up on #id moves to
    // the name, so the name does not count as a new player and pause the game a second time.
    private void CarryKeys()
    {
        var present = new HashSet<int>();
        foreach (var player in _players)
        {
            present.Add(player.Id);
            var key = KeyOf(player);
            if (!player.IsJoining && _keyById.TryGetValue(player.Id, out var before)
                && before == "#" + player.Id && _givenUp.Remove(before))
                _givenUp.Add(key);
            _keyById[player.Id] = key;
        }

        foreach (var id in _keyById.Keys.Where(id => !present.Contains(id)).ToList())
            _keyById.Remove(id);
    }

    private JoinPauseStep Release()
    {
        Holding = false;
        return JoinPauseStep.Resume;
    }
}
