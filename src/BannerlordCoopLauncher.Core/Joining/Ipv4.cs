using System.Net;
using System.Net.Sockets;

namespace BannerlordCoopLauncher.Core.Joining;

public enum Ipv4Kind
{
    Invalid,
    Unspecified,
    Loopback,
    LinkLocal,
    /// <summary>10.x, 172.16 to 172.31, 192.168.x: a home or office network.</summary>
    Private,
    /// <summary>100.64.0.0/10: an internet provider's shared address space (CGNAT).</summary>
    CarrierShared,
    Multicast,
    Public
}

/// <summary>Plain IPv4 checks shared by the port opener, the address list and the client hints.</summary>
public static class Ipv4
{
    public const string Loopback = "127.0.0.1";

    public static bool TryParse(string? text, out IPAddress address)
    {
        address = IPAddress.None;
        if (string.IsNullOrWhiteSpace(text))
            return false;
        var parts = text.Trim().Split('.');
        if (parts.Length != 4)
            return false;
        foreach (var part in parts)
            if (part.Length is 0 or > 3 || !part.All(char.IsAsciiDigit) || int.Parse(part) > 255)
                return false;
        return IPAddress.TryParse(text.Trim(), out address!) && address.AddressFamily == AddressFamily.InterNetwork;
    }

    public static Ipv4Kind Classify(string? text) => TryParse(text, out var address) ? Classify(address) : Ipv4Kind.Invalid;

    public static Ipv4Kind Classify(IPAddress address)
    {
        if (address.AddressFamily != AddressFamily.InterNetwork)
            return Ipv4Kind.Invalid;
        var b = address.GetAddressBytes();
        if (b[0] == 0)
            return Ipv4Kind.Unspecified;
        if (b[0] == 127)
            return Ipv4Kind.Loopback;
        if (b[0] == 169 && b[1] == 254)
            return Ipv4Kind.LinkLocal;
        if (b[0] == 10 || (b[0] == 172 && b[1] is >= 16 and <= 31) || (b[0] == 192 && b[1] == 168))
            return Ipv4Kind.Private;
        if (b[0] == 100 && b[1] is >= 64 and <= 127)
            return Ipv4Kind.CarrierShared;
        if (b[0] >= 224)
            return Ipv4Kind.Multicast;
        return Ipv4Kind.Public;
    }

    /// <summary>
    /// True when the address a router calls its internet address is not one the internet can
    /// reach: the provider (CGNAT) or a second router in front of this one shares it.
    /// Port forwarding on this router cannot help then.
    /// </summary>
    public static bool IsSharedAddress(string? routerExternal) =>
        Classify(routerExternal) is Ipv4Kind.Private or Ipv4Kind.CarrierShared or Ipv4Kind.LinkLocal or Ipv4Kind.Loopback;

    public static bool IsUsable(string? text) => Classify(text) is Ipv4Kind.Private or Ipv4Kind.CarrierShared or Ipv4Kind.Public;
}
