using System.Text.Json;
using System.Text.Json.Nodes;

namespace BannerlordCoopLauncher.Core.Network;

public static class JoinGuidance
{
    public const string IpRequired = "Enter the host's IP address, such as 192.168.1.10, or a custom port-forwarded address, such as host.example.com. Put the port in the UDP port box. A Steam name goes in Steam lobby.";
    public const string SteamNameHint = "Make sure the host's Steam username is correct.";
    public const string PortForward = "A custom public address needs UDP 4200 port forwarded.";

    public static bool IsIpv4(string? address)
    {
        if (string.IsNullOrWhiteSpace(address))
            return false;
        var parts = address.Trim().Split('.');
        if (parts.Length != 4)
            return false;
        foreach (var part in parts)
        {
            if (part.Length == 0 || part.Length > 3 || !part.All(char.IsDigit))
                return false;
            if (!int.TryParse(part, out var octet) || octet > 255)
                return false;
        }

        return true;
    }

    public static bool IsDirectAddress(string? address) => IsIpv4(address) || IsPortForwardedName(address);

    public static bool IsPortForwardedName(string? address)
    {
        if (string.IsNullOrWhiteSpace(address))
            return false;
        var value = address.Trim();
        if (value.Length > 253 || value.Contains(' ') || value.Contains(':') || value.Contains('/') || value.Contains('\\'))
            return false;
        var labels = value.Split('.');
        if (labels.Length < 2)
            return false;
        var hasLetter = false;
        foreach (var label in labels)
        {
            if (label.Length is < 1 or > 63 || label.StartsWith('-') || label.EndsWith('-'))
                return false;
            foreach (var ch in label)
            {
                if (char.IsLetter(ch))
                    hasLetter = true;
                else if (!char.IsDigit(ch) && ch != '-')
                    return false;
            }
        }

        return hasLetter && labels[^1].Length >= 2;
    }

    public static bool IsHomeNetwork(string? address)
    {
        if (!IsIpv4(address))
            return false;
        var parts = address!.Trim().Split('.');
        if (parts[0] is "10" || (parts[0] == "192" && parts[1] == "168"))
            return true;
        return parts[0] == "172" && int.TryParse(parts[1], out var second) && second is >= 16 and <= 31;
    }

    public static bool IsLoopback(string? address)
    {
        if (!IsIpv4(address))
            return false;
        return address!.Trim().Split('.')[0] == "127";
    }

    public static string DescribeProbe(ServerProbeResult probe, string address)
    {
        // The probe is a real Coop connection handshake, so an answer means the server is up and
        // no answer means a joining player would get no answer either.
        if (probe.Reachability == ServerReachability.Online)
            return "The Coop server answered. You can click Connect.";
        if (probe.Reachability == ServerReachability.OtherVersion)
            return "A server answered, but it uses a different Coop network version. Make sure you and the host have the same Coop version.";
        var quiet = probe.Reachability == ServerReachability.NoReply;
        if (IsLoopback(address))
        {
            return quiet
                ? "No Coop server answered on 127.0.0.1. This address is only the host's own PC. Friends cannot use it. Check that the server is started on this PC and uses this port."
                : "127.0.0.1 could not be reached. This address is only the host's own PC. Friends cannot use it.";
        }

        if (quiet && IsHomeNetwork(address))
            return "No Coop server answered on that UDP port. Check that the server is started and the port is right. A firewall on the host's PC can also block it. That address only works for people on the same home network. " + PortForward + " On the host's own PC, use 127.0.0.1.";
        if (quiet)
            return "No Coop server answered on that UDP port. The server may be stopped, the address or port may be wrong, a firewall may block it, or the port is not forwarded. " + PortForward + " Some routers do not answer when the host checks their own public address, so ask a friend to check. On the host's own PC, use 127.0.0.1.";
        if (IsHomeNetwork(address))
            return "That IP could not be reached. That address only works for people on the same home network. " + PortForward;
        var kind = IsIpv4(address) ? "IP" : "address";
        return "That " + kind + " could not be reached. " + PortForward + " Check the address and that this PC is online.";
    }

    public static string? RemoveDirectAddress(string json)
    {
        JsonObject root;
        try
        {
            root = JsonNode.Parse(string.IsNullOrWhiteSpace(json) ? "{}" : json) as JsonObject ?? new JsonObject();
        }
        catch (JsonException)
        {
            return null;
        }

        if (root["Connection"] is not JsonObject connection || connection["DirectConnection"] is not JsonObject direct)
            return null;
        if (direct["lastServerAddress"] == null)
            return null;

        direct.Remove("lastServerAddress");
        return root.ToJsonString(new JsonSerializerOptions { WriteIndented = true }) + Environment.NewLine;
    }

    public static bool StoredAddressIsNotIp(string json)
    {
        JsonObject root;
        try
        {
            root = JsonNode.Parse(string.IsNullOrWhiteSpace(json) ? "{}" : json) as JsonObject ?? new JsonObject();
        }
        catch (JsonException)
        {
            return false;
        }

        if (root["Connection"] is not JsonObject connection || connection["DirectConnection"] is not JsonObject direct)
            return false;
        if (direct["lastServerAddress"] is not JsonValue value || !value.TryGetValue<string>(out var address))
            return false;
        return !string.IsNullOrWhiteSpace(address) && !IsIpv4(address);
    }

    public static string SetDirectAddress(string json, string address)
    {
        address = address.Trim();
        if (!IsDirectAddress(address))
            throw new InvalidOperationException(IpRequired);
        JsonObject root;
        try
        {
            root = JsonNode.Parse(string.IsNullOrWhiteSpace(json) ? "{}" : json) as JsonObject ?? new JsonObject();
        }
        catch (JsonException)
        {
            root = new JsonObject();
        }

        var connection = root["Connection"] as JsonObject ?? new JsonObject();
        var direct = connection["DirectConnection"] as JsonObject ?? new JsonObject();
        direct["lastServerAddress"] = address;
        connection["DirectConnection"] = direct;
        root["Connection"] = connection;
        return root.ToJsonString(new JsonSerializerOptions { WriteIndented = true }) + Environment.NewLine;
    }
}
