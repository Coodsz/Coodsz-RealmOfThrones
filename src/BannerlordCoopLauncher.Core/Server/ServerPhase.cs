// Ported from CoopKeep (https://github.com/Aderimo/bannerlord-coopkeep), Protocol/ServerPhase.cs,
// and changed for the Coodsz tools.

namespace BannerlordCoopLauncher.Core.Server;

/// <summary>
/// The life cycle phase the dedicated server reports in its
/// <c>@DS@{"ev":"state","phase":...}</c> event: <c>boot</c>, then <c>loading</c>,
/// then <c>serving</c>, and on the way down <c>stopping</c> and <c>shutdown</c>.
/// </summary>
/// <remarks>
/// Ready is read from this protocol field rather than from text such as "SERVING".
/// The Coop log wording changes often; the protocol field is much more stable.
/// </remarks>
public enum ServerPhase
{
    /// <summary>A phase name this tool does not know. Unknown names never throw.</summary>
    Unknown = 0,

    Starting,
    Boot,
    Loading,

    /// <summary>The server is up and takes players. This is the ready signal.</summary>
    Serving,

    Stopping,
    Shutdown,
}

public static class ServerPhaseExtensions
{
    /// <summary>
    /// Turns the raw phase name into the enum. An unknown name gives
    /// <see cref="ServerPhase.Unknown"/> and never throws, because Coop can add a phase.
    /// </summary>
    public static ServerPhase ParsePhase(string? raw) => raw?.Trim().ToLowerInvariant() switch
    {
        "starting" => ServerPhase.Starting,
        "boot" => ServerPhase.Boot,
        "loading" => ServerPhase.Loading,
        "serving" => ServerPhase.Serving,
        "stopping" => ServerPhase.Stopping,
        "shutdown" => ServerPhase.Shutdown,
        _ => ServerPhase.Unknown,
    };

    /// <summary>Does the server take players in this phase?</summary>
    public static bool IsAcceptingPlayers(this ServerPhase phase) => phase == ServerPhase.Serving;

    /// <summary>Is the server still coming up in this phase?</summary>
    public static bool IsStartingUp(this ServerPhase phase) =>
        phase is ServerPhase.Starting or ServerPhase.Boot or ServerPhase.Loading;

    /// <summary>Is the server on its way down in this phase?</summary>
    public static bool IsShuttingDown(this ServerPhase phase) =>
        phase is ServerPhase.Stopping or ServerPhase.Shutdown;
}
