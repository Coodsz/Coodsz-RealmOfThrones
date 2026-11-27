using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Runtime.CompilerServices;
using LiteNetLib;
using LiteNetLib.Utils;

namespace BannerlordCoopLauncher.Core.Network;

public enum ServerReachability
{
    /// <summary>A Coop (LiteNetLib) server answered the connection handshake.</summary>
    Online,
    /// <summary>Nothing answered the handshake in time: stopped, firewall, wrong address or port, or no port forward.</summary>
    NoReply,
    /// <summary>A LiteNetLib server answered but speaks a different network protocol version.</summary>
    OtherVersion,
    /// <summary>The name did not resolve or this PC has no route to it.</summary>
    Unreachable
}

public sealed record ServerProbeResult(ServerReachability Reachability, string Detail);

/// <summary>
/// Checks a Bannerlord Coop server the way a joining player reaches it: a LiteNetLib connection
/// request on its UDP port. The request carries join data the Coop server cannot read, so CoopServer
/// turns it away in OnConnectionRequest (a "malformed password data" reject) before any peer, player
/// or slot exists. That reject is the server's own answer, so it proves the server is up. An older
/// build that accepts every request is disconnected again at once.
/// </summary>
public static class UdpServerProbe
{
    /// <summary>
    /// LiteNetLib's string length prefix set to the largest value. CoopServer reads the password with
    /// GetString(128), which cannot read 65534 bytes from this short request and throws, so the
    /// server rejects with ConnectionRejectCode.IncorrectPassword. It is never a valid password.
    /// </summary>
    public static readonly byte[] ConnectData = { 0xFF, 0xFF };

    /// <summary>
    /// LiteNetLib resends the connection request every 0.5 s, so 4 s gives about eight tries:
    /// enough for a slow or lossy internet path, still short enough for a button.
    /// </summary>
    public static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(4);

    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(15);

    private const string NoAnswer = "No answer to the connection handshake. The server may be stopped, blocked by a firewall, or not forwarded, or the address or port is wrong.";
    private const string Unreachable = "Unable to resolve the hostname or reach the network.";

    public static async Task<ServerProbeResult> ProbeAsync(
        string host,
        int port,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        if (!Models.ProfileJson.IsValidHost(host) || port is < 1 or > 65535)
            return new ServerProbeResult(ServerReachability.Unreachable, "Invalid hostname reports unknown");

        IPAddress address;
        try
        {
            address = await ResolveAsync(host.Trim(), timeout, cancellationToken);
        }
        catch (Exception exception) when (exception is SocketException or ArgumentException
                                              || (exception is OperationCanceledException && !cancellationToken.IsCancellationRequested))
        {
            return new ServerProbeResult(ServerReachability.Unreachable, Unreachable);
        }

        LiteNetLibLoader.EnsureResolvable();
        return await HandshakeAsync(address, port, timeout, cancellationToken);
    }

    // Kept apart from ProbeAsync so LiteNetLib is only loaded after the resolver above is in place.
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static async Task<ServerProbeResult> HandshakeAsync(IPAddress address, int port, TimeSpan timeout, CancellationToken cancellationToken)
    {
        var listener = new EventBasedNetListener();
        ServerProbeResult? result = null;
        void Settle(ServerReachability reachability, string detail) => result ??= new ServerProbeResult(reachability, detail);

        // This side never takes connections; an echoed or stray request is refused.
        listener.ConnectionRequestEvent += request => request.Reject();
        listener.PeerConnectedEvent += peer =>
        {
            Settle(ServerReachability.Online, "The server accepted the connection handshake and the check left at once.");
            peer.Disconnect();
        };
        listener.PeerDisconnectedEvent += (_, info) =>
        {
            switch (info.Reason)
            {
                case DisconnectReason.ConnectionRejected:
                    var reader = info.AdditionalData;
                    Settle(ServerReachability.Online, reader != null && reader.AvailableBytes > 0
                        ? "The server answered the connection handshake and turned the check away with code " + reader.GetByte() + "."
                        : "The server answered the connection handshake and turned the check away.");
                    break;
                case DisconnectReason.InvalidProtocol:
                    Settle(ServerReachability.OtherVersion, "A LiteNetLib server answered with a different protocol version.");
                    break;
                case DisconnectReason.ConnectionFailed:
                case DisconnectReason.Timeout:
                    Settle(ServerReachability.NoReply, NoAnswer);
                    break;
                case DisconnectReason.HostUnreachable:
                case DisconnectReason.NetworkUnreachable:
                case DisconnectReason.UnknownHost:
                    Settle(ServerReachability.Unreachable, Unreachable);
                    break;
                case DisconnectReason.RemoteConnectionClose:
                    Settle(ServerReachability.Online, "The server answered and closed the connection.");
                    break;
            }
        };
        listener.NetworkErrorEvent += (_, error) =>
        {
            if (error is SocketError.HostUnreachable or SocketError.NetworkUnreachable or SocketError.NetworkDown or SocketError.AddressNotAvailable)
                Settle(ServerReachability.Unreachable, Unreachable);
        };

        var manager = new NetManager(listener)
        {
            // One socket in the family of the address being checked.
            IPv6Enabled = address.AddressFamily == AddressFamily.InterNetworkV6,
            // LiteNetLib resends the request every ReconnectDelay; the timeout below ends the wait first.
            ReconnectDelay = 500,
            MaxConnectAttempts = Math.Max(2, (int)Math.Ceiling(timeout.TotalMilliseconds / 500) + 1),
            DisconnectTimeout = Math.Max(1000, (int)timeout.TotalMilliseconds)
        };

        // Manual mode: the socket is read and the resends run only on this loop. With LiteNetLib's own
        // threads, Connect sends the request before it records the peer, so a server on the same PC could
        // answer into that gap. The reject was then dropped, and every reject after it carried the next
        // connection number, so the check only settled about 2 s later on the fifth try.
        if (!manager.StartInManualMode(IPAddress.Any, IPAddress.IPv6Any, 0))
            return new ServerProbeResult(ServerReachability.Unreachable, "Could not open a UDP socket on this PC.");

        try
        {
            var data = new NetDataWriter();
            data.Put(ConnectData);
            if (manager.Connect(new IPEndPoint(address, port), data) == null)
                return new ServerProbeResult(ServerReachability.Unreachable, "Could not start the connection handshake.");

            var clock = Stopwatch.StartNew();
            var lastTick = TimeSpan.Zero;
            while (true)
            {
                manager.PollEvents();
                if (result != null)
                    return result;
                var now = clock.Elapsed;
                manager.ManualUpdate((float)(now - lastTick).TotalMilliseconds);
                lastTick = now;
                if (result != null)
                    return result;
                if (now >= timeout)
                    return new ServerProbeResult(ServerReachability.NoReply, NoAnswer);
                await Task.Delay(PollInterval, cancellationToken);
            }
        }
        finally
        {
            // Sends a disconnect for a peer that is still connecting or connected, then closes the socket.
            manager.Stop(sendDisconnectMessages: true);
        }
    }

    private static async Task<IPAddress> ResolveAsync(string host, TimeSpan timeout, CancellationToken cancellationToken)
    {
        if (IPAddress.TryParse(host, out var literal))
            return literal;

        using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutSource.CancelAfter(timeout);
        var addresses = await Dns.GetHostAddressesAsync(host, timeoutSource.Token);
        return addresses.FirstOrDefault(a => a.AddressFamily == AddressFamily.InterNetwork)
            ?? addresses.FirstOrDefault(a => a.AddressFamily == AddressFamily.InterNetworkV6)
            ?? throw new SocketException((int)SocketError.HostNotFound);
    }
}
