// Ported from CoopKeep (https://github.com/Aderimo/bannerlord-coopkeep), Protocol/DsEvent.cs,
// and changed for the Coodsz tools.

namespace BannerlordCoopLauncher.Core.Server;

/// <summary>
/// The machine readable events the dedicated server writes with the <c>@DS@</c> prefix.
/// They appear on the server's stdout and in coop-server-*.log.
/// </summary>
/// <remarks>
/// Coop's module DLLs are pinned by SHA-256, so nothing can be added inside the mod.
/// This protocol is the one structured way to follow the server from outside.
/// </remarks>
public abstract record DsEvent
{
    private DsEvent() { }

    /// <summary>
    /// <c>{"ev":"state","phase":"serving","save":"...","pw":false}</c>. A server that cannot
    /// load its campaign sends phase "fatal" (kept in <see cref="RawPhase"/>) with the reason
    /// in "detail".
    /// </summary>
    public sealed record State(
        ServerPhase Phase,
        string RawPhase,
        string? SaveName,
        bool PasswordRequired,
        string? Detail = null) : DsEvent;

    /// <summary>
    /// <c>{"ev":"players","list":[{"id":0,"name":"...","state":"on map","addr":"..."}]}</c>.
    /// The server sends the whole list on every change, a snapshot and not a delta.
    /// </summary>
    public sealed record Players(IReadOnlyList<ConnectedPlayer> List) : DsEvent;

    /// <summary>
    /// <c>{"ev":"commands","builtin":[...],"game":[...]}</c>, sent once at start.
    /// <c>builtin</c> holds status, players, save, say, kick, stop and help. <c>game</c>
    /// holds several hundred engine console commands, many of them cheats
    /// (campaign.add_gold_to_hero and the like), which is why lines this tool writes
    /// to the server go through <see cref="ServerCommand"/>.
    /// </summary>
    public sealed record Commands(
        IReadOnlyList<string> Builtin,
        IReadOnlyList<string> Game) : DsEvent;

    /// <summary>An <c>ev</c> value this tool does not know. A new event type must not crash anything.</summary>
    public sealed record Unknown(string EventName, string RawJson) : DsEvent;
}

/// <summary>One player in the <c>@DS@ players</c> event.</summary>
/// <param name="Id">Peer index. Not permanent: it is given out again every session, so it is no identity.</param>
/// <param name="Name">Player name, the one half permanent identifier visible from outside.</param>
/// <param name="State">Connection state: handshake, creating character, loading, on map.</param>
/// <param name="Address">
/// <c>ip:port</c>. Not an identity either: everyone who joins through the Steam relay shows as 127.0.0.1.
/// </param>
public sealed record ConnectedPlayer(
    int Id,
    string Name,
    string State,
    string? Address)
{
    /// <summary>The name the server shows while a player has not picked one yet.</summary>
    public const string JoiningPlaceholder = "(joining)";

    /// <summary>Is the player on the campaign map and playing?</summary>
    public bool IsOnMap => string.Equals(State, "on map", StringComparison.OrdinalIgnoreCase);

    /// <summary>Does the player still have the joining placeholder for a name?</summary>
    public bool IsJoining =>
        Name.Length == 0 || Name.Equals(JoiningPlaceholder, StringComparison.OrdinalIgnoreCase);
}
