using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text.RegularExpressions;

namespace BannerlordCoopLauncher.Core.Joining;

/// <summary>One adapter on this PC: its IPv4 addresses and the gateways it sends through.</summary>
public sealed record PcAdapter(string Name, string Description, bool Virtual, IReadOnlyList<string> Addresses, IReadOnlyList<string> Gateways);

public enum OwnAddressKind
{
    /// <summary>127.x with no Coop server on this PC.</summary>
    Loopback,
    /// <summary>The address of one of this PC's adapters, with no Coop server on this PC.</summary>
    ThisPc,
    /// <summary>The gateway of a VPN, virtual machine, WSL or Docker adapter.</summary>
    AdapterGateway,
    /// <summary>The gateway of the Wi Fi or cable adapter: the home router.</summary>
    Router
}

public sealed record OwnAddressMatch(string Typed, string Address, OwnAddressKind Kind, string Adapter)
{
    public string Warning => OwnAddressCheck.Warning(this);
}

/// <summary>
/// Notices a Direct address that points back at this PC instead of the host: a VPN or virtual
/// adapter's own address or gateway, the home router, or 127.0.0.1. A player who types their VPN's
/// gateway gets an endless connect screen in the game, so Connect warns first. A Coop server
/// running on this PC makes 127.0.0.1 and this PC's own addresses fine (the host joining their own server).
/// </summary>
public static class OwnAddressCheck
{
    // VPN apps, tunnels and virtual machine or container switches. Their gateway is never a friend's server.
    private static readonly Regex VirtualWords = new Regex(
        @"\b(VPN|TAP|TUN|Wintun|WireGuard|OpenVPN|WAN Miniport|Hyper-V|vEthernet|WSL|Docker|VirtualBox|VMware|Radmin|Hamachi|LogMeIn|ZeroTier|Tailscale|NordLynx|Cisco|AnyConnect|FortiClient|Fortinet|GlobalProtect|Proton|Mullvad|Windscribe|Surfshark|Outline|Amnezia|Npcap|Loopback)\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    /// <summary>True for an adapter that is not plain Wi Fi, cable or mobile broadband.</summary>
    public static bool LooksVirtual(string name, string description, NetworkInterfaceType type) =>
        type is not (NetworkInterfaceType.Ethernet or NetworkInterfaceType.Ethernet3Megabit or NetworkInterfaceType.FastEthernetT
            or NetworkInterfaceType.FastEthernetFx or NetworkInterfaceType.GigabitEthernet or NetworkInterfaceType.Wireless80211
            or NetworkInterfaceType.Wwanpp or NetworkInterfaceType.Wwanpp2)
        || VirtualWords.IsMatch(name + " " + description);

    /// <summary>
    /// The first of the host's addresses (the typed IP, or what a name resolved to) that is this PC
    /// or one of its gateways, or null when none is. Gateways are flagged even with a server on this PC.
    /// </summary>
    public static OwnAddressMatch? Find(string typed, IEnumerable<string> addresses, IReadOnlyList<PcAdapter> adapters, bool localServerRunning)
    {
        foreach (var address in addresses.Select(a => a.Trim()).Where(a => Ipv4.TryParse(a, out _)))
        {
            var gateway = adapters.FirstOrDefault(adapter => adapter.Gateways.Contains(address));
            if (gateway != null)
                return new OwnAddressMatch(typed, address, gateway.Virtual ? OwnAddressKind.AdapterGateway : OwnAddressKind.Router, gateway.Name);
            if (localServerRunning)
                continue;
            if (Ipv4.Classify(address) == Ipv4Kind.Loopback)
                return new OwnAddressMatch(typed, address, OwnAddressKind.Loopback, "");
            var own = adapters.FirstOrDefault(adapter => adapter.Addresses.Contains(address));
            if (own != null)
                return new OwnAddressMatch(typed, address, OwnAddressKind.ThisPc, own.Name);
        }

        return null;
    }

    public static string Warning(OwnAddressMatch match) => match.Kind switch
    {
        OwnAddressKind.Loopback => match.Typed + " is this PC, and no Coop server is running on it. Click Use Steam lobby and type your host's Steam name.",
        OwnAddressKind.Router => match.Typed + " is your router, not your host. Click Use Steam lobby and type your host's Steam name.",
        _ => match.Typed + " belongs to your VPN or a network adapter on this PC, not to your host. Click Use Steam lobby and type your host's Steam name."
    };

    /// <summary>Every adapter that is on, VPN and virtual ones too, with its IPv4 addresses and gateways.</summary>
    public static IReadOnlyList<PcAdapter> Read()
    {
        var found = new List<PcAdapter>();
        NetworkInterface[] adapters;
        try
        {
            adapters = NetworkInterface.GetAllNetworkInterfaces();
        }
        catch (NetworkInformationException)
        {
            return found;
        }

        foreach (var adapter in adapters)
        {
            try
            {
                if (adapter.OperationalStatus != OperationalStatus.Up || adapter.NetworkInterfaceType == NetworkInterfaceType.Loopback)
                    continue;
                var properties = adapter.GetIPProperties();
                var addresses = properties.UnicastAddresses
                    .Where(u => u.Address.AddressFamily == AddressFamily.InterNetwork)
                    .Select(u => u.Address.ToString())
                    .ToList();
                var gateways = properties.GatewayAddresses
                    .Where(g => g.Address.AddressFamily == AddressFamily.InterNetwork && !g.Address.Equals(IPAddress.Any))
                    .Select(g => g.Address.ToString())
                    .ToList();
                if (addresses.Count > 0 || gateways.Count > 0)
                    found.Add(new PcAdapter(adapter.Name, adapter.Description, LooksVirtual(adapter.Name, adapter.Description, adapter.NetworkInterfaceType), addresses, gateways));
            }
            catch (NetworkInformationException)
            {
            }
        }

        return found;
    }
}
