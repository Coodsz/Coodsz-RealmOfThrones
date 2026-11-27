using System.Globalization;
using System.Net;
using System.Net.Sockets;
using BannerlordCoopLauncher.Core.Network;

namespace BannerlordCoopLauncher.Core.Joining;

public enum JoinCheckOutcome
{
    /// <summary>The server answered: start the game with no extra click.</summary>
    Connect,
    /// <summary>No answer, and the address is this PC, its router or a VPN gateway.</summary>
    OwnAddress,
    /// <summary>No answer from the address.</summary>
    NoAnswer
}

/// <summary>What the player picked in the prompt, or what Connect does when there was no prompt.</summary>
public enum JoinCheckChoice
{
    Cancel,
    /// <summary>Start the game: the server answered, or the player clicked Connect anyway.</summary>
    Connect,
    SteamLobby
}

public sealed record JoinCheckResult(JoinCheckOutcome Outcome, string Message, ServerProbeResult? Probe = null, OwnAddressMatch? Own = null)
{
    public string Title => Outcome switch
    {
        JoinCheckOutcome.OwnAddress when Own?.Kind == OwnAddressKind.Router => "This address is your router",
        JoinCheckOutcome.OwnAddress => "This address is on your own PC",
        _ => "The server did not answer"
    };
}

/// <summary>
/// What Connect checks in Direct mode before Bannerlord starts. A wrong address used to start the game
/// into Coop's "Contacting the server" screen with no end, so the launcher runs the Check server probe
/// first. An answer connects straight away; no answer gives a prompt that says why, and the prompt
/// always keeps Connect anyway, since the check can be wrong.
/// </summary>
public static class JoinPreflight
{
    /// <summary>About ten handshake tries, so a slow or far server is not taken for a silent one.</summary>
    public static readonly TimeSpan ProbeTimeout = TimeSpan.FromSeconds(5);

    private static readonly TimeSpan ResolveTimeout = TimeSpan.FromSeconds(2);

    /// <summary>Shown where the player types the Steam name after Use Steam lobby.</summary>
    public const string SteamSwitchHint = "Type your host's Steam name, then click Connect.";

    public static string NoAnswer(string host, int port) =>
        "Nobody answered at " + host + ":" + port.ToString(CultureInfo.InvariantCulture) + ". The server may be off, or your host's internet may not allow direct joins. Steam lobby usually works. Ask your host if unsure.";

    /// <summary>The no answer prompt, also what any failure inside the check falls back to.</summary>
    public static JoinCheckResult Failed(string host, int port) => new(JoinCheckOutcome.NoAnswer, NoAnswer(host.Trim(), port));

    /// <summary>
    /// The one path every client launcher takes on Connect: runs the check, falls back to the no answer
    /// prompt when the check throws, and asks only when the server did not answer. Cancel when the check
    /// was cancelled (the window closed or another action took over).
    /// </summary>
    public static async Task<JoinCheckChoice> DecideAsync(string host, int port, Func<Task<JoinCheckResult>> check, Func<JoinCheckResult, JoinCheckChoice> ask)
    {
        JoinCheckResult result;
        try
        {
            result = await check();
        }
        catch (OperationCanceledException)
        {
            return JoinCheckChoice.Cancel;
        }
        catch (Exception)
        {
            result = Failed(host, port);
        }

        return result.Outcome == JoinCheckOutcome.Connect ? JoinCheckChoice.Connect : ask(result);
    }

    /// <summary>
    /// Checks the host and port. The adapter list, name lookup and probe can be swapped (tests);
    /// by default they are this PC's adapters, DNS and <see cref="UdpServerProbe"/>. The name is looked up
    /// once and the probe goes to that address. Only cancellation throws; any other failure is no answer.
    /// </summary>
    public static async Task<JoinCheckResult> CheckAsync(
        string host,
        int port,
        bool localServerRunning,
        CancellationToken cancellationToken,
        Func<IReadOnlyList<PcAdapter>>? adapters = null,
        Func<string, CancellationToken, Task<IReadOnlyList<string>>>? resolve = null,
        Func<string, int, TimeSpan, CancellationToken, Task<ServerProbeResult>>? probe = null)
    {
        var typed = host.Trim();
        try
        {
            var addresses = await (resolve ?? ResolveAsync)(typed, cancellationToken).ConfigureAwait(false);
            // A name that does not resolve cannot answer; the probe would only look it up again.
            if (addresses.Count == 0)
                return Failed(typed, port);
            var list = await Task.Run(adapters ?? OwnAddressCheck.Read, cancellationToken).ConfigureAwait(false);
            var own = OwnAddressCheck.Find(typed, addresses, list, localServerRunning);
            // An answer wins over the address check: whatever answered the Coop handshake is a Coop server.
            var answer = await (probe ?? UdpServerProbe.ProbeAsync)(addresses[0], port, ProbeTimeout, cancellationToken).ConfigureAwait(false);
            if (answer.Reachability is ServerReachability.Online or ServerReachability.OtherVersion)
                return new JoinCheckResult(JoinCheckOutcome.Connect, "", answer, own);
            return own != null
                ? new JoinCheckResult(JoinCheckOutcome.OwnAddress, own.Warning, answer, own)
                : new JoinCheckResult(JoinCheckOutcome.NoAnswer, NoAnswer(typed, port), answer);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            return Failed(typed, port);
        }
    }

    /// <summary>The IPv4 addresses behind the host: the IP itself, or what the name resolves to (empty when it does not).</summary>
    public static async Task<IReadOnlyList<string>> ResolveAsync(string host, CancellationToken cancellationToken)
    {
        if (Ipv4.TryParse(host, out var literal))
            return new[] { literal.ToString() };
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(ResolveTimeout);
        try
        {
            var found = await Dns.GetHostAddressesAsync(host, timeout.Token).ConfigureAwait(false);
            return found.Where(a => a.AddressFamily == AddressFamily.InterNetwork).Select(a => a.ToString()).ToList();
        }
        catch (Exception exception) when (exception is SocketException or ArgumentException
                                              || (exception is OperationCanceledException && !cancellationToken.IsCancellationRequested))
        {
            return Array.Empty<string>();
        }
    }
}
