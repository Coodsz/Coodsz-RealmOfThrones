using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using BannerlordCoopLauncher.Core.Network;

namespace TaomCoop.Core;

public static class ClientJoin
{
    public static int ReadBattleSize(string config)
    {
        var match = Regex.Match(config, @"^BattleSize=(\d+)", RegexOptions.Multiline);
        if (!match.Success || !int.TryParse(match.Groups[1].Value, out var size) || size < 200 || size > 1000)
            return 600;
        return size - (size % 10);
    }

    public static string? RaiseLowTerrain(string config)
    {
        var match = Regex.Match(config, @"^terrain_quality\s*=\s*0\s*$", RegexOptions.Multiline);
        if (!match.Success)
            return null;
        return config.Remove(match.Index, match.Length).Insert(match.Index, "terrain_quality = 1");
    }

    public static string ApplyBattleSize(string config, int size)
    {
        if (size < 200 || size > 1000 || size % 10 != 0)
            throw new InvalidOperationException("Battle size must be a step of 10 from 200 to 1000.");
        var line = "BattleSize=" + size.ToString();
        if (Regex.IsMatch(config, @"^BattleSize=\d+", RegexOptions.Multiline))
            return Regex.Replace(config, @"^BattleSize=\d+", line, RegexOptions.Multiline);
        var end = config.EndsWith('\n') ? "" : Environment.NewLine;
        return config + end + line + Environment.NewLine;
    }

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

    // Same wording as Coop and Friends, so every tool explains a Check server answer alike.
    public static string DescribeProbe(ServerProbeResult probe, string address) => JoinGuidance.DescribeProbe(probe, address);

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

    public static string? RemoveNonIpAddress(string json)
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
        if (direct["lastServerAddress"] is not JsonValue value || !value.TryGetValue<string>(out var address))
            return null;
        if (string.IsNullOrWhiteSpace(address) || IsIpv4(address))
            return null;

        direct.Remove("lastServerAddress");
        return root.ToJsonString(new JsonSerializerOptions { WriteIndented = true }) + Environment.NewLine;
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
