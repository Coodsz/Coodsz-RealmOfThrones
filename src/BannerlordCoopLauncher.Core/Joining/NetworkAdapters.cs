using System.Net.NetworkInformation;
using System.Net.Sockets;

namespace BannerlordCoopLauncher.Core.Joining;

public enum AdapterKind
{
    /// <summary>The home network (Wi Fi or cable), for friends on the same network.</summary>
    Lan,
    Radmin,
    Hamachi,
    ZeroTier,
    Tailscale
}

public sealed record AdapterAddress(string Name, string Description, string Address, AdapterKind Kind, bool HasGateway)
{
    public bool IsVirtualLan => Kind != AdapterKind.Lan;
    public string AppName => NetworkAdapters.AppName(Kind);
}

/// <summary>
/// Finds the addresses friends can type: the home network address and the address of a
/// virtual LAN app (Radmin VPN, Hamachi, ZeroTier, Tailscale) when one is installed and on.
/// </summary>
public static class NetworkAdapters
{
    // Adapters that never carry friends: virtual machines, containers, Bluetooth, packet capture.
    private static readonly string[] Ignored = "Hyper-V|vEthernet|VirtualBox|VMware|WSL|Docker|Bluetooth|Npcap|Loopback|Kernel Debug".Split('|');

    public static string AppName(AdapterKind kind) => kind switch
    {
        AdapterKind.Radmin => "Radmin VPN",
        AdapterKind.Hamachi => "Hamachi",
        AdapterKind.ZeroTier => "ZeroTier",
        AdapterKind.Tailscale => "Tailscale",
        _ => "Home network"
    };

    /// <summary>What an adapter is, or null when friends cannot use it.</summary>
    public static AdapterKind? Classify(string name, string description, string address, bool hasGateway)
    {
        var kind = Ipv4.Classify(address);
        if (kind is Ipv4Kind.Invalid or Ipv4Kind.Unspecified or Ipv4Kind.Loopback or Ipv4Kind.LinkLocal or Ipv4Kind.Multicast)
            return null;
        var text = name + " " + description;
        bool Has(string word) => text.Contains(word, StringComparison.OrdinalIgnoreCase);
        var first = address.Split('.')[0];

        if (Has("Radmin"))
            return AdapterKind.Radmin;
        if (Has("Hamachi") || Has("LogMeIn"))
            return AdapterKind.Hamachi;
        if (Has("ZeroTier"))
            return AdapterKind.ZeroTier;
        if (Has("Tailscale"))
            return AdapterKind.Tailscale;
        if (Ignored.Any(Has))
            return null;
        // Renamed adapters: Radmin hands out 26.x and Hamachi 25.x, both without a gateway.
        if (!hasGateway && first == "26")
            return AdapterKind.Radmin;
        if (!hasGateway && first == "25")
            return AdapterKind.Hamachi;
        if (!hasGateway && kind == Ipv4Kind.CarrierShared)
            return AdapterKind.Tailscale;
        return kind == Ipv4Kind.Private ? AdapterKind.Lan : null;
    }

    /// <summary>The adapters that are on right now, virtual LANs first, then the home network.</summary>
    public static IReadOnlyList<AdapterAddress> Read()
    {
        var found = new List<AdapterAddress>();
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
                var gateway = properties.GatewayAddresses.Any(g => g.Address.AddressFamily == AddressFamily.InterNetwork && !g.Address.Equals(System.Net.IPAddress.Any));
                foreach (var unicast in properties.UnicastAddresses)
                {
                    if (unicast.Address.AddressFamily != AddressFamily.InterNetwork)
                        continue;
                    var address = unicast.Address.ToString();
                    var kind = Classify(adapter.Name, adapter.Description, address, gateway);
                    if (kind != null)
                        found.Add(new AdapterAddress(adapter.Name, adapter.Description, address, kind.Value, gateway));
                }
            }
            catch (NetworkInformationException)
            {
            }
        }

        return Order(found);
    }

    public static IReadOnlyList<AdapterAddress> Order(IEnumerable<AdapterAddress> adapters) =>
        adapters
            .GroupBy(a => a.Address)
            .Select(g => g.First())
            .OrderBy(a => a.Kind == AdapterKind.Lan ? 1 : 0)
            .ThenBy(a => a.Kind)
            .ThenByDescending(a => a.HasGateway)
            .ToList();

    /// <summary>The home network address friends on the same Wi Fi type: one with a router first.</summary>
    public static AdapterAddress? BestLan(IEnumerable<AdapterAddress> adapters) =>
        adapters.Where(a => a.Kind == AdapterKind.Lan).OrderByDescending(a => a.HasGateway).FirstOrDefault();

    public static IReadOnlyList<AdapterAddress> VirtualLans(IEnumerable<AdapterAddress> adapters) =>
        adapters.Where(a => a.IsVirtualLan).ToList();
}
