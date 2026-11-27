using System.Globalization;
using System.Net;
using System.Security;
using System.Text;
using System.Xml;
using System.Xml.Linq;

namespace BannerlordCoopLauncher.Core.Joining;

/// <summary>A router's port mapping service: where to send the SOAP calls, and which service it is.</summary>
public sealed record UpnpService(Uri ControlUrl, string ServiceType);

/// <summary>An error answer from the router (a SOAP fault), such as 718 ConflictInMappingEntry.</summary>
public sealed record UpnpFault(int Code, string Description);

/// <summary>What the router says is mapped on an external port.</summary>
public sealed record UpnpMapping(string InternalClient, int InternalPort, bool Enabled, string Description, int LeaseSeconds);

/// <summary>
/// The small part of UPnP IGD this tool needs: find the router with SSDP, read its device
/// description, then ask it for its internet address and to add or remove one UDP mapping.
/// Everything here only builds or reads text, so the tests run it on recorded router answers.
/// </summary>
public static class Upnp
{
    public const string MulticastAddress = "239.255.255.250";
    public const int SsdpPort = 1900;

    public const int NoSuchEntry = 714;
    public const int ConflictInMappingEntry = 718;
    public const int OnlyPermanentLeasesSupported = 725;
    public const int ActionNotAuthorized = 606;

    /// <summary>What the search asks for. Routers answer the device type, some only the service type.</summary>
    public static readonly IReadOnlyList<string> SearchTargets = new[]
    {
        "urn:schemas-upnp-org:device:InternetGatewayDevice:1",
        "urn:schemas-upnp-org:device:InternetGatewayDevice:2",
        "urn:schemas-upnp-org:service:WANIPConnection:1",
        "urn:schemas-upnp-org:service:WANIPConnection:2",
        "urn:schemas-upnp-org:service:WANPPPConnection:1",
    };

    private static readonly string[] ServicePrefixes =
    {
        "urn:schemas-upnp-org:service:WANIPConnection:",
        "urn:schemas-upnp-org:service:WANPPPConnection:",
    };

    public static string SearchRequest(string searchTarget) => string.Join("\r\n",
        "M-SEARCH * HTTP/1.1",
        "HOST: " + MulticastAddress + ":" + SsdpPort.ToString(CultureInfo.InvariantCulture),
        "MAN: \"ssdp:discover\"",
        "MX: 2",
        "ST: " + searchTarget,
        "",
        "");

    /// <summary>The LOCATION header of an SSDP answer, when it points at an http device description.</summary>
    public static Uri? ParseLocation(string response)
    {
        if (string.IsNullOrWhiteSpace(response))
            return null;
        var lines = response.Replace("\r\n", "\n").Split('\n');
        if (lines.Length == 0 || !lines[0].Contains("200", StringComparison.Ordinal))
            return null;
        foreach (var line in lines.Skip(1))
        {
            var colon = line.IndexOf(':');
            if (colon <= 0)
                continue;
            if (!line[..colon].Trim().Equals("LOCATION", StringComparison.OrdinalIgnoreCase))
                continue;
            var value = line[(colon + 1)..].Trim();
            return Uri.TryCreate(value, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttp ? uri : null;
        }

        return null;
    }

    /// <summary>
    /// The WANIPConnection and WANPPPConnection services in a device description, best first
    /// (WANIPConnection:2, WANIPConnection:1, then PPP). Control URLs are made absolute against
    /// URLBase, or the description's own address when there is none.
    /// </summary>
    public static IReadOnlyList<UpnpService> ParseServices(string descriptionXml, Uri location)
    {
        XDocument doc;
        try
        {
            doc = XDocument.Parse(descriptionXml);
        }
        catch (XmlException)
        {
            return Array.Empty<UpnpService>();
        }

        var baseText = doc.Descendants().FirstOrDefault(e => e.Name.LocalName == "URLBase")?.Value.Trim();
        var baseUri = !string.IsNullOrEmpty(baseText) && Uri.TryCreate(baseText, UriKind.Absolute, out var parsed) ? parsed : location;
        var services = new List<UpnpService>();
        foreach (var service in doc.Descendants().Where(e => e.Name.LocalName == "service"))
        {
            var type = service.Elements().FirstOrDefault(e => e.Name.LocalName == "serviceType")?.Value.Trim() ?? "";
            var control = service.Elements().FirstOrDefault(e => e.Name.LocalName == "controlURL")?.Value.Trim() ?? "";
            if (control.Length == 0 || !ServicePrefixes.Any(prefix => type.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)))
                continue;
            if (!Uri.TryCreate(baseUri, control, out var controlUrl) || controlUrl.Scheme != Uri.UriSchemeHttp)
                continue;
            if (services.All(s => s.ControlUrl != controlUrl))
                services.Add(new UpnpService(controlUrl, type));
        }

        return services
            .OrderBy(s => s.ServiceType.Contains("WANPPP", StringComparison.OrdinalIgnoreCase) ? 1 : 0)
            .ThenByDescending(s => s.ServiceType[^1])
            .ToList();
    }

    public static string SoapAction(UpnpService service, string action) => "\"" + service.ServiceType + "#" + action + "\"";

    public static string Envelope(string serviceType, string action, params (string Name, string Value)[] arguments)
    {
        var body = new StringBuilder();
        foreach (var (name, value) in arguments)
            body.Append('<').Append(name).Append('>').Append(SecurityElement.Escape(value)).Append("</").Append(name).Append('>');
        return "<?xml version=\"1.0\"?>"
            + "<s:Envelope xmlns:s=\"http://schemas.xmlsoap.org/soap/envelope/\" s:encodingStyle=\"http://schemas.xmlsoap.org/soap/encoding/\">"
            + "<s:Body><u:" + action + " xmlns:u=\"" + SecurityElement.Escape(serviceType) + "\">"
            + body
            + "</u:" + action + "></s:Body></s:Envelope>";
    }

    public static string GetExternalIpAddress(string serviceType) => Envelope(serviceType, "GetExternalIPAddress");

    public static string AddPortMapping(string serviceType, int externalPort, string internalClient, int internalPort, string description, int leaseSeconds) =>
        Envelope(serviceType, "AddPortMapping",
            ("NewRemoteHost", ""),
            ("NewExternalPort", externalPort.ToString(CultureInfo.InvariantCulture)),
            ("NewProtocol", "UDP"),
            ("NewInternalPort", internalPort.ToString(CultureInfo.InvariantCulture)),
            ("NewInternalClient", internalClient),
            ("NewEnabled", "1"),
            ("NewPortMappingDescription", description),
            ("NewLeaseDuration", leaseSeconds.ToString(CultureInfo.InvariantCulture)));

    public static string DeletePortMapping(string serviceType, int externalPort) =>
        Envelope(serviceType, "DeletePortMapping",
            ("NewRemoteHost", ""),
            ("NewExternalPort", externalPort.ToString(CultureInfo.InvariantCulture)),
            ("NewProtocol", "UDP"));

    public static string GetSpecificPortMappingEntry(string serviceType, int externalPort) =>
        Envelope(serviceType, "GetSpecificPortMappingEntry",
            ("NewRemoteHost", ""),
            ("NewExternalPort", externalPort.ToString(CultureInfo.InvariantCulture)),
            ("NewProtocol", "UDP"));

    /// <summary>The router's error, when the answer is a SOAP fault (any HTTP status).</summary>
    public static UpnpFault? ParseFault(string responseXml)
    {
        var doc = TryParse(responseXml);
        if (doc == null || !doc.Descendants().Any(e => e.Name.LocalName.Equals("Fault")))
            return null;
        var codeText = doc.Descendants().FirstOrDefault(e => e.Name.LocalName == "errorCode")?.Value.Trim();
        var description = doc.Descendants().FirstOrDefault(e => e.Name.LocalName == "errorDescription")?.Value.Trim()
            ?? doc.Descendants().FirstOrDefault(e => e.Name.LocalName == "faultstring")?.Value.Trim()
            ?? "";
        return new UpnpFault(int.TryParse(codeText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var code) ? code : 0, description);
    }

    /// <summary>One out argument of a successful answer, such as NewExternalIPAddress.</summary>
    public static string? ReadValue(string responseXml, string name) =>
        TryParse(responseXml)?.Descendants().FirstOrDefault(e => e.Name.LocalName == name)?.Value.Trim();

    public static UpnpMapping? ParseMapping(string responseXml)
    {
        var client = ReadValue(responseXml, "NewInternalClient");
        if (string.IsNullOrWhiteSpace(client))
            return null;
        int Number(string name) => int.TryParse(ReadValue(responseXml, name), NumberStyles.Integer, CultureInfo.InvariantCulture, out var n) ? n : 0;
        var enabled = ReadValue(responseXml, "NewEnabled");
        return new UpnpMapping(
            client,
            Number("NewInternalPort"),
            enabled is null or "1" || enabled.Equals("true", StringComparison.OrdinalIgnoreCase),
            ReadValue(responseXml, "NewPortMappingDescription") ?? "",
            Number("NewLeaseDuration"));
    }

    public static IPAddress? RouterAddress(UpnpService service) =>
        IPAddress.TryParse(service.ControlUrl.Host, out var address) ? address : null;

    private static XDocument? TryParse(string xml)
    {
        if (string.IsNullOrWhiteSpace(xml))
            return null;
        try
        {
            return XDocument.Parse(xml);
        }
        catch (XmlException)
        {
            return null;
        }
    }
}

/// <summary>
/// NAT-PMP (RFC 6886), which Apple routers and some others speak instead of UPnP:
/// two small UDP packets to the gateway on port 5351.
/// </summary>
public static class NatPmp
{
    public const int Port = 5351;

    public sealed record ExternalAddressAnswer(int Result, string Address);

    public sealed record MappingAnswer(int Result, int InternalPort, int ExternalPort, int LifetimeSeconds);

    public static byte[] ExternalAddressRequest() => new byte[] { 0, 0 };

    /// <summary>A UDP mapping request. A lifetime of 0 with external port 0 removes the mapping.</summary>
    public static byte[] MapUdpRequest(int internalPort, int externalPort, int lifetimeSeconds)
    {
        var packet = new byte[12];
        packet[1] = 1;
        WriteUInt16(packet, 4, internalPort);
        WriteUInt16(packet, 6, externalPort);
        packet[8] = (byte)(lifetimeSeconds >> 24);
        packet[9] = (byte)(lifetimeSeconds >> 16);
        packet[10] = (byte)(lifetimeSeconds >> 8);
        packet[11] = (byte)lifetimeSeconds;
        return packet;
    }

    public static ExternalAddressAnswer? ParseExternalAddress(byte[]? packet)
    {
        if (packet == null || packet.Length < 12 || packet[0] != 0 || packet[1] != 128)
            return null;
        var result = ReadUInt16(packet, 2);
        var address = new IPAddress(packet[8..12]).ToString();
        return new ExternalAddressAnswer(result, address);
    }

    public static MappingAnswer? ParseMapping(byte[]? packet)
    {
        if (packet == null || packet.Length < 16 || packet[0] != 0 || packet[1] != 129)
            return null;
        var lifetime = (packet[12] << 24) | (packet[13] << 16) | (packet[14] << 8) | packet[15];
        return new MappingAnswer(ReadUInt16(packet, 2), ReadUInt16(packet, 8), ReadUInt16(packet, 10), lifetime);
    }

    private static void WriteUInt16(byte[] packet, int offset, int value)
    {
        packet[offset] = (byte)(value >> 8);
        packet[offset + 1] = (byte)value;
    }

    private static int ReadUInt16(byte[] packet, int offset) => (packet[offset] << 8) | packet[offset + 1];
}
