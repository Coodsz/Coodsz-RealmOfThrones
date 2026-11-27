using System.Net;
using System.Net.Http.Headers;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;

namespace BannerlordCoopLauncher.Core.Joining;

/// <summary>The real network: SSDP over UDP multicast, HTTP to the router, NAT-PMP to the gateway.</summary>
public sealed class RouterNetwork : IRouterNetwork
{
    private static readonly HttpClient Http = new(new SocketsHttpHandler
    {
        UseProxy = false,
        ConnectTimeout = TimeSpan.FromSeconds(3),
        PooledConnectionLifetime = TimeSpan.FromMinutes(1)
    })
    {
        Timeout = TimeSpan.FromSeconds(5)
    };

    public async Task<IReadOnlyList<string>> SearchAsync(IReadOnlyList<string> requests, TimeSpan timeout, CancellationToken cancellationToken)
    {
        var answers = new List<string>();
        var sockets = new List<Socket>();
        try
        {
            // One socket per home network address, so the search leaves through the Wi Fi or cable
            // adapter and not through Radmin or Hamachi, which Windows may prefer for multicast.
            var locals = HomeAddresses();
            if (locals.Count == 0)
                locals.Add(IPAddress.Any);
            foreach (var local in locals)
            {
                var socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
                try
                {
                    socket.Bind(new IPEndPoint(local, 0));
                    if (!local.Equals(IPAddress.Any))
                        socket.SetSocketOption(SocketOptionLevel.IP, SocketOptionName.MulticastInterface, local.GetAddressBytes());
                    socket.SetSocketOption(SocketOptionLevel.IP, SocketOptionName.MulticastTimeToLive, 2);
                    sockets.Add(socket);
                }
                catch (SocketException)
                {
                    socket.Dispose();
                }
            }

            var targets = new List<IPEndPoint> { new(IPAddress.Parse(Upnp.MulticastAddress), Upnp.SsdpPort) };
            targets.AddRange(Gateways().Select(gateway => new IPEndPoint(gateway, Upnp.SsdpPort)));
            foreach (var socket in sockets)
                foreach (var request in requests)
                    foreach (var target in targets)
                    {
                        try
                        {
                            await socket.SendToAsync(Encoding.ASCII.GetBytes(request), SocketFlags.None, target, cancellationToken).ConfigureAwait(false);
                        }
                        catch (SocketException)
                        {
                        }
                    }

            using var window = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            window.CancelAfter(timeout);
            var receivers = sockets.Select(socket => ReceiveAllAsync(socket, answers, window.Token)).ToList();
            await Task.WhenAll(receivers).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
        }
        finally
        {
            foreach (var socket in sockets)
                socket.Dispose();
        }

        lock (answers)
            return answers.ToList();
    }

    private static async Task ReceiveAllAsync(Socket socket, List<string> answers, CancellationToken token)
    {
        var buffer = new byte[8192];
        while (!token.IsCancellationRequested)
        {
            try
            {
                var received = await socket.ReceiveFromAsync(buffer, SocketFlags.None, new IPEndPoint(IPAddress.Any, 0), token).ConfigureAwait(false);
                var text = Encoding.ASCII.GetString(buffer, 0, received.ReceivedBytes);
                lock (answers)
                    answers.Add(text);
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (SocketException)
            {
                // Windows reports an ICMP "port unreachable" from the unicast try as a reset; keep listening.
            }
        }
    }

    public async Task<string?> GetAsync(Uri url, CancellationToken cancellationToken)
    {
        try
        {
            return await Http.GetStringAsync(url, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException && !cancellationToken.IsCancellationRequested)
        {
            return null;
        }
    }

    public async Task<string?> PostSoapAsync(Uri url, string soapAction, string body, CancellationToken cancellationToken)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, url);
            request.Content = new StringContent(body, Encoding.UTF8);
            request.Content.Headers.ContentType = MediaTypeHeaderValue.Parse("text/xml; charset=\"utf-8\"");
            request.Headers.TryAddWithoutValidation("SOAPAction", soapAction);
            request.Headers.ConnectionClose = true;
            using var response = await Http.SendAsync(request, cancellationToken).ConfigureAwait(false);
            return await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException && !cancellationToken.IsCancellationRequested)
        {
            return null;
        }
    }

    public string? LocalAddressFor(IPAddress router)
    {
        try
        {
            using var socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
            socket.Connect(router, Upnp.SsdpPort);
            return (socket.LocalEndPoint as IPEndPoint)?.Address.ToString();
        }
        catch (SocketException)
        {
            return null;
        }
    }

    public IReadOnlyList<IPAddress> Gateways()
    {
        var gateways = new List<IPAddress>();
        foreach (var adapter in HomeAdapters())
            foreach (var gateway in adapter.GetIPProperties().GatewayAddresses)
                if (gateway.Address.AddressFamily == AddressFamily.InterNetwork && Ipv4.IsUsable(gateway.Address.ToString()) && !gateways.Contains(gateway.Address))
                    gateways.Add(gateway.Address);
        return gateways;
    }

    public async Task<byte[]?> NatPmpAsync(IPAddress gateway, byte[] request, TimeSpan timeout, CancellationToken cancellationToken)
    {
        using var socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
        try
        {
            socket.Connect(gateway, NatPmp.Port);
            var buffer = new byte[64];
            // RFC 6886 resends after 250 ms, doubling; two tries keep a button quick.
            foreach (var wait in new[] { TimeSpan.FromMilliseconds(250), timeout })
            {
                await socket.SendAsync(request, SocketFlags.None, cancellationToken).ConfigureAwait(false);
                using var window = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                window.CancelAfter(wait);
                try
                {
                    var count = await socket.ReceiveAsync(buffer, SocketFlags.None, window.Token).ConfigureAwait(false);
                    return buffer[..count];
                }
                catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
                {
                }
            }
        }
        catch (SocketException)
        {
        }

        return null;
    }

    private static List<IPAddress> HomeAddresses()
    {
        var addresses = new List<IPAddress>();
        foreach (var adapter in HomeAdapters())
            foreach (var unicast in adapter.GetIPProperties().UnicastAddresses)
                if (unicast.Address.AddressFamily == AddressFamily.InterNetwork
                    && NetworkAdapters.Classify(adapter.Name, adapter.Description, unicast.Address.ToString(), true) == AdapterKind.Lan)
                    addresses.Add(unicast.Address);
        return addresses;
    }

    // Up adapters with a router, without the virtual LAN apps (they have no UPnP router).
    private static IEnumerable<NetworkInterface> HomeAdapters()
    {
        NetworkInterface[] all;
        try
        {
            all = NetworkInterface.GetAllNetworkInterfaces();
        }
        catch (NetworkInformationException)
        {
            yield break;
        }

        foreach (var adapter in all)
        {
            bool keep;
            try
            {
                keep = adapter.OperationalStatus == OperationalStatus.Up
                    && adapter.NetworkInterfaceType != NetworkInterfaceType.Loopback
                    && adapter.GetIPProperties().GatewayAddresses.Any(g => g.Address.AddressFamily == AddressFamily.InterNetwork);
                if (keep)
                {
                    var text = adapter.Name + " " + adapter.Description;
                    keep = !"Radmin|Hamachi|LogMeIn|ZeroTier|Tailscale".Split('|').Any(word => text.Contains(word, StringComparison.OrdinalIgnoreCase));
                }
            }
            catch (NetworkInformationException)
            {
                keep = false;
            }

            if (keep)
                yield return adapter;
        }
    }
}

/// <summary>
/// Keeps the server port open while the server runs: opens it, renews the lease at half time,
/// and removes it on Close. One keeper per tool; the host calls Open on start and Close on stop or exit.
/// </summary>
public sealed class RouterPortKeeper : IDisposable
{
    private readonly Func<RouterPortMapper> _newMapper;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private RouterPortMapper? _mapper;
    private CancellationTokenSource? _renewal;

    public RouterPortKeeper(Func<RouterPortMapper>? newMapper = null) =>
        _newMapper = newMapper ?? (() => new RouterPortMapper(new RouterNetwork()));

    /// <summary>The last answer; null before the first try.</summary>
    public PortOpenResult? Last { get; private set; }

    public bool IsOpen => _mapper?.HasMapping == true;

    public int? Port { get; private set; }

    /// <summary>Raised from a background thread when the state changes (opened, renew failed, closed).</summary>
    public event Action<PortOpenResult?>? Changed;

    public async Task<PortOpenResult> OpenAsync(int port, string description = RouterPortMapper.DefaultDescription, TimeSpan? lease = null)
    {
        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            await CloseCoreAsync().ConfigureAwait(false);
            Port = port;
            var mapper = _newMapper();
            var result = await mapper.OpenAsync(port, description, lease ?? RouterPortMapper.DefaultLease, CancellationToken.None).ConfigureAwait(false);
            _mapper = mapper;
            Last = result;
            if (mapper.HasMapping && mapper.LeaseSeconds > 0)
            {
                _renewal = new CancellationTokenSource();
                _ = RenewLoopAsync(mapper, port, description, lease ?? RouterPortMapper.DefaultLease, _renewal.Token);
            }

            Changed?.Invoke(result);
            return result;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task CloseAsync()
    {
        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            var had = IsOpen;
            await CloseCoreAsync().ConfigureAwait(false);
            Port = null;
            if (had)
                Changed?.Invoke(null);
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>For window close: removes the mapping, waiting at most this long.</summary>
    public void CloseNow(TimeSpan wait)
    {
        try
        {
            Task.Run(CloseAsync).Wait(wait);
        }
        catch (AggregateException)
        {
        }
    }

    private async Task CloseCoreAsync()
    {
        _renewal?.Cancel();
        _renewal = null;
        if (_mapper != null)
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(4));
            try
            {
                await _mapper.CloseAsync(timeout.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
            }
        }

        _mapper = null;
    }

    private async Task RenewLoopAsync(RouterPortMapper mapper, int port, string description, TimeSpan lease, CancellationToken token)
    {
        try
        {
            while (!token.IsCancellationRequested)
            {
                await Task.Delay(TimeSpan.FromSeconds(Math.Max(60, mapper.LeaseSeconds / 2)), token).ConfigureAwait(false);
                if (await mapper.RenewAsync(token).ConfigureAwait(false))
                    continue;
                // The router restarted or forgot the mapping: try once from the start.
                var again = await mapper.OpenAsync(port, description, lease, token).ConfigureAwait(false);
                Last = again;
                Changed?.Invoke(again);
                if (!mapper.HasMapping)
                    return;
            }
        }
        catch (OperationCanceledException)
        {
        }
    }

    public void Dispose()
    {
        _renewal?.Cancel();
        _gate.Dispose();
    }
}
