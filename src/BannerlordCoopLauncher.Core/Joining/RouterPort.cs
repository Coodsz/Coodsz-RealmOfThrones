using System.Net;

namespace BannerlordCoopLauncher.Core.Joining;

public enum PortOpenOutcome
{
    /// <summary>The router added the mapping.</summary>
    Opened,
    /// <summary>No UPnP or NAT-PMP router answered on this network.</summary>
    NoRouter,
    /// <summary>A router answered but refused the mapping (UPnP off for changes, not allowed, full).</summary>
    RouterSaidNo,
    /// <summary>The port is already mapped to another PC on this network.</summary>
    TakenByOtherPc,
    /// <summary>The router's own internet address is private or CGNAT, so forwarding cannot reach this PC.</summary>
    SharedAddress,
    /// <summary>Something else went wrong talking to the router.</summary>
    Failed
}

public sealed record PortOpenResult(
    PortOpenOutcome Outcome,
    int Port,
    string Method,
    string? ExternalAddress = null,
    string? LocalAddress = null,
    string? OtherPc = null,
    int? RouterCode = null,
    string Detail = "")
{
    public bool IsOpen => Outcome == PortOpenOutcome.Opened;

    /// <summary>The short status line the tools show next to the option.</summary>
    public string Status => Outcome switch
    {
        PortOpenOutcome.Opened when ExternalAddress != null => "Port " + Port + " is open on your router. Friends can use your public address " + ExternalAddress + ".",
        PortOpenOutcome.Opened => "Port " + Port + " is open on your router.",
        PortOpenOutcome.NoRouter => "No router answered. UPnP may be off on your router. Use Steam lobby, a VPN, or forward the port by hand.",
        PortOpenOutcome.RouterSaidNo => "Your router said no to opening the port. Turn on UPnP in your router settings, or forward the port by hand.",
        PortOpenOutcome.TakenByOtherPc => "Port " + Port + " is already open for another PC (" + (OtherPc ?? "") + "). Close it there, or forward the port by hand.",
        PortOpenOutcome.SharedAddress => "Your internet provider shares your address (" + (ExternalAddress ?? "") + "), so port forwarding cannot work. Use Steam lobby or a VPN like Radmin VPN.",
        _ => "Could not open the port on your router. Use Steam lobby, a VPN, or forward the port by hand."
    };

    /// <summary>One English line for the log, with the router details support needs.</summary>
    // Joined with string.Join so the UI text tool leaves this English log line alone.
    public string LogLine => string.Join("", "Router port ", Port, " UDP. ", Outcome, " via ", Method.Length == 0 ? "none" : Method,
        ExternalAddress != null ? string.Join("", ". Router internet address ", ExternalAddress) : "",
        LocalAddress != null ? string.Join("", ". This PC ", LocalAddress) : "",
        OtherPc != null ? string.Join("", ". Mapped to ", OtherPc) : "",
        RouterCode != null ? string.Join("", ". Router code ", RouterCode) : "",
        Detail.Length > 0 ? ". " + Detail : "", ".");
}

/// <summary>What the port opener needs from the network, so tests can play a router.</summary>
public interface IRouterNetwork
{
    /// <summary>Sends the SSDP search and returns every answer received before the timeout.</summary>
    Task<IReadOnlyList<string>> SearchAsync(IReadOnlyList<string> requests, TimeSpan timeout, CancellationToken cancellationToken);

    Task<string?> GetAsync(Uri url, CancellationToken cancellationToken);

    /// <summary>Posts a SOAP call. The body comes back for faults too (routers answer 500 with a fault).</summary>
    Task<string?> PostSoapAsync(Uri url, string soapAction, string body, CancellationToken cancellationToken);

    /// <summary>This PC's own address on the network that reaches the router.</summary>
    string? LocalAddressFor(IPAddress router);

    IReadOnlyList<IPAddress> Gateways();

    Task<byte[]?> NatPmpAsync(IPAddress gateway, byte[] request, TimeSpan timeout, CancellationToken cancellationToken);
}

/// <summary>
/// Opens one UDP port on the home router for the Coop server while it runs, the way a UPnP port
/// mapper (Porthole and the like) does: UPnP IGD first, NAT-PMP second. The mapping has a lease, so
/// it ends on its own if the tool is closed without Close. Never touches a mapping of another PC.
/// </summary>
public sealed class RouterPortMapper
{
    public const string DefaultDescription = "Coodsz Coop server";
    public static readonly TimeSpan DefaultLease = TimeSpan.FromHours(1);
    public static readonly TimeSpan SearchTimeout = TimeSpan.FromSeconds(3);

    private readonly IRouterNetwork _network;
    private UpnpService? _service;
    private IPAddress? _natPmpGateway;
    private string? _localAddress;
    private int _port;
    private int _leaseSeconds;
    private string _description = DefaultDescription;

    public RouterPortMapper(IRouterNetwork network) => _network = network;

    public bool HasMapping { get; private set; }

    /// <summary>The lease the router accepted; 0 means it only keeps permanent mappings.</summary>
    public int LeaseSeconds => _leaseSeconds;

    public async Task<PortOpenResult> OpenAsync(int port, string description, TimeSpan lease, CancellationToken cancellationToken)
    {
        _port = port;
        _description = description;
        _leaseSeconds = (int)Math.Max(0, lease.TotalSeconds);
        HasMapping = false;
        try
        {
            var upnp = await FindUpnpAsync(cancellationToken).ConfigureAwait(false);
            if (upnp != null)
                return await OpenWithUpnpAsync(upnp.Value.Service, upnp.Value.External, cancellationToken).ConfigureAwait(false);
            var natPmp = await OpenWithNatPmpAsync(cancellationToken).ConfigureAwait(false);
            return natPmp ?? new PortOpenResult(PortOpenOutcome.NoRouter, port, "");
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return new PortOpenResult(PortOpenOutcome.Failed, port, _service != null ? "UPnP" : "", Detail: ex.Message);
        }
    }

    /// <summary>Adds the same mapping again before its lease runs out. False when the router no longer takes it.</summary>
    public async Task<bool> RenewAsync(CancellationToken cancellationToken)
    {
        if (!HasMapping)
            return false;
        try
        {
            if (_service != null && _localAddress != null)
            {
                var answer = await _network.PostSoapAsync(_service.ControlUrl, Upnp.SoapAction(_service, "AddPortMapping"),
                    Upnp.AddPortMapping(_service.ServiceType, _port, _localAddress, _port, _description, _leaseSeconds), cancellationToken).ConfigureAwait(false);
                return answer != null && Upnp.ParseFault(answer) == null;
            }

            if (_natPmpGateway != null)
            {
                var answer = NatPmp.ParseMapping(await _network.NatPmpAsync(_natPmpGateway, NatPmp.MapUdpRequest(_port, _port, _leaseSeconds), TimeSpan.FromSeconds(2), cancellationToken).ConfigureAwait(false));
                return answer is { Result: 0 } && answer.ExternalPort == _port;
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
        }

        return false;
    }

    /// <summary>Removes the mapping this mapper added. Safe to call more than once.</summary>
    public async Task CloseAsync(CancellationToken cancellationToken)
    {
        if (!HasMapping)
            return;
        HasMapping = false;
        try
        {
            if (_service != null)
                await _network.PostSoapAsync(_service.ControlUrl, Upnp.SoapAction(_service, "DeletePortMapping"),
                    Upnp.DeletePortMapping(_service.ServiceType, _port), cancellationToken).ConfigureAwait(false);
            else if (_natPmpGateway != null)
                await _network.NatPmpAsync(_natPmpGateway, NatPmp.MapUdpRequest(_port, 0, 0), TimeSpan.FromSeconds(2), cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // The lease ends the mapping anyway.
        }
    }

    private async Task<(UpnpService Service, string External)?> FindUpnpAsync(CancellationToken cancellationToken)
    {
        var requests = Upnp.SearchTargets.Select(Upnp.SearchRequest).ToList();
        var answers = await _network.SearchAsync(requests, SearchTimeout, cancellationToken).ConfigureAwait(false);
        var locations = answers.Select(Upnp.ParseLocation).Where(uri => uri != null).Select(uri => uri!).Distinct().ToList();
        (UpnpService Service, string External)? fallback = null;
        foreach (var location in locations)
        {
            var description = await _network.GetAsync(location, cancellationToken).ConfigureAwait(false);
            if (description == null)
                continue;
            foreach (var service in Upnp.ParseServices(description, location))
            {
                var answer = await _network.PostSoapAsync(service.ControlUrl, Upnp.SoapAction(service, "GetExternalIPAddress"),
                    Upnp.GetExternalIpAddress(service.ServiceType), cancellationToken).ConfigureAwait(false);
                if (answer == null || Upnp.ParseFault(answer) != null)
                    continue;
                var external = Upnp.ReadValue(answer, "NewExternalIPAddress") ?? "";
                // An unconnected PPP service answers with an empty or 0.0.0.0 address; a connected one wins.
                if (Ipv4.IsUsable(external))
                    return (service, external);
                fallback ??= (service, external);
            }
        }

        return fallback;
    }

    private async Task<PortOpenResult> OpenWithUpnpAsync(UpnpService service, string external, CancellationToken cancellationToken)
    {
        _service = service;
        var externalOrNull = Ipv4.IsUsable(external) ? external : null;
        var router = Upnp.RouterAddress(service);
        _localAddress = router != null ? _network.LocalAddressFor(router) : null;
        if (_localAddress == null)
            return new PortOpenResult(PortOpenOutcome.Failed, _port, "UPnP", externalOrNull, Detail: "This PC's address on the router's network was not found");
        if (Ipv4.IsSharedAddress(external))
            return new PortOpenResult(PortOpenOutcome.SharedAddress, _port, "UPnP", external, _localAddress);

        var existing = await _network.PostSoapAsync(service.ControlUrl, Upnp.SoapAction(service, "GetSpecificPortMappingEntry"),
            Upnp.GetSpecificPortMappingEntry(service.ServiceType, _port), cancellationToken).ConfigureAwait(false);
        var mapping = existing != null && Upnp.ParseFault(existing) == null ? Upnp.ParseMapping(existing) : null;
        if (mapping != null && !mapping.InternalClient.Equals(_localAddress, StringComparison.OrdinalIgnoreCase))
            return new PortOpenResult(PortOpenOutcome.TakenByOtherPc, _port, "UPnP", externalOrNull, _localAddress, mapping.InternalClient);

        var fault = await AddAsync(service, cancellationToken).ConfigureAwait(false);
        if (fault?.Code == Upnp.OnlyPermanentLeasesSupported && _leaseSeconds != 0)
        {
            _leaseSeconds = 0;
            fault = await AddAsync(service, cancellationToken).ConfigureAwait(false);
        }

        if (fault == null)
        {
            HasMapping = true;
            return new PortOpenResult(PortOpenOutcome.Opened, _port, "UPnP", externalOrNull, _localAddress,
                Detail: _leaseSeconds == 0 ? "Router keeps only permanent mappings, removed on stop" : "Lease " + _leaseSeconds + " s");
        }

        if (fault.Code == Upnp.ConflictInMappingEntry)
            return new PortOpenResult(PortOpenOutcome.TakenByOtherPc, _port, "UPnP", externalOrNull, _localAddress, mapping?.InternalClient ?? "another device", fault.Code, fault.Description);
        return new PortOpenResult(PortOpenOutcome.RouterSaidNo, _port, "UPnP", externalOrNull, _localAddress, RouterCode: fault.Code, Detail: fault.Description);
    }

    private async Task<UpnpFault?> AddAsync(UpnpService service, CancellationToken cancellationToken)
    {
        var answer = await _network.PostSoapAsync(service.ControlUrl, Upnp.SoapAction(service, "AddPortMapping"),
            Upnp.AddPortMapping(service.ServiceType, _port, _localAddress!, _port, _description, _leaseSeconds), cancellationToken).ConfigureAwait(false);
        if (answer == null)
            return new UpnpFault(0, "No answer");
        return Upnp.ParseFault(answer);
    }

    private async Task<PortOpenResult?> OpenWithNatPmpAsync(CancellationToken cancellationToken)
    {
        foreach (var gateway in _network.Gateways())
        {
            var address = NatPmp.ParseExternalAddress(await _network.NatPmpAsync(gateway, NatPmp.ExternalAddressRequest(), TimeSpan.FromSeconds(2), cancellationToken).ConfigureAwait(false));
            if (address == null)
                continue;
            _natPmpGateway = gateway;
            _localAddress = _network.LocalAddressFor(gateway);
            var external = address.Result == 0 ? address.Address : null;
            if (Ipv4.IsSharedAddress(external))
                return new PortOpenResult(PortOpenOutcome.SharedAddress, _port, "NAT-PMP", external, _localAddress);
            var lease = _leaseSeconds == 0 ? (int)DefaultLease.TotalSeconds : _leaseSeconds;
            _leaseSeconds = lease;
            var map = NatPmp.ParseMapping(await _network.NatPmpAsync(gateway, NatPmp.MapUdpRequest(_port, _port, lease), TimeSpan.FromSeconds(2), cancellationToken).ConfigureAwait(false));
            if (map == null)
                return new PortOpenResult(PortOpenOutcome.Failed, _port, "NAT-PMP", external, _localAddress, Detail: "No answer to the mapping request");
            if (map.Result != 0)
                return new PortOpenResult(PortOpenOutcome.RouterSaidNo, _port, "NAT-PMP", external, _localAddress, RouterCode: map.Result);
            if (map.ExternalPort != _port)
            {
                // The router gave another port: this one is in use. Give the other one back.
                await _network.NatPmpAsync(gateway, NatPmp.MapUdpRequest(_port, 0, 0), TimeSpan.FromSeconds(2), cancellationToken).ConfigureAwait(false);
                return new PortOpenResult(PortOpenOutcome.TakenByOtherPc, _port, "NAT-PMP", external, _localAddress, "another device");
            }

            _leaseSeconds = map.LifetimeSeconds > 0 ? map.LifetimeSeconds : lease;
            HasMapping = true;
            return new PortOpenResult(PortOpenOutcome.Opened, _port, "NAT-PMP", external, _localAddress, Detail: "Lease " + _leaseSeconds + " s");
        }

        return null;
    }
}
