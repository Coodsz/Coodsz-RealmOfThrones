namespace BannerlordCoopLauncher.Core.Joining;

/// <summary>
/// What a tool knows about joining right now. The server tools fill the host side (Steam option,
/// router, firewall, addresses); the client launchers fill the player side. Every value is optional
/// so a tool can pass only what it knows.
/// </summary>
public sealed record JoinFacts
{
    /// <summary>True in a server tool, false in a client launcher.</summary>
    public bool IsHost { get; init; }
    public int Port { get; init; } = Joining.LocalServer.DefaultPort;
    /// <summary>The server's Steam lobby option; null when unknown (client side).</summary>
    public bool? SteamLobbyOn { get; init; }
    public string? SteamName { get; init; }
    public bool PasswordNeeded { get; init; }
    public IReadOnlyList<AdapterAddress> Adapters { get; init; } = Array.Empty<AdapterAddress>();
    public PortOpenResult? Router { get; init; }
    public FirewallState Firewall { get; init; } = FirewallState.Unknown;
    public string ClientTool { get; init; } = "Coodsz Coop Client Launcher";
    public string ServerTool { get; init; } = "Coodsz Coop Server Tool";
    /// <summary>The server tool's name for the Steam lobby option. TAOM calls it Advertise through Steam.</summary>
    public string SteamOption { get; init; } = "Steam advertise";
    /// <summary>TAOM tools: the first start of a new campaign is very slow.</summary>
    public bool Taom { get; init; }
    /// <summary>The launcher setup friends pick, when the host tool knows it.</summary>
    public string? Setup { get; init; }
    /// <summary>The campaign map friends pick in the launcher, such as Vanilla campaign Map, when there is no setup name.</summary>
    public string? Map { get; init; }
    /// <summary>Server tool: automatic port opening is on and runs when the server starts.</summary>
    public bool PortOpensOnStart { get; init; }
    /// <summary>Client side: a Coop server runs on this PC.</summary>
    public LocalServerInfo? LocalServer { get; init; }

    public AdapterAddress? Lan => NetworkAdapters.BestLan(Adapters);
    public IReadOnlyList<AdapterAddress> VirtualLans => NetworkAdapters.VirtualLans(Adapters);
    public bool PortOpened => Router?.IsOpen == true;
    public bool SharedAddress => Router?.Outcome == PortOpenOutcome.SharedAddress;

    /// <summary>The internet address friends can use, when the router told it and it is not shared.</summary>
    public string? PublicAddress =>
        Router?.ExternalAddress is { } address && Ipv4.Classify(address) == Ipv4Kind.Public ? address : null;
}

public enum JoinAddressKind
{
    SteamLobby,
    VirtualLan,
    SameWifi,
    Internet,
    ThisPc
}

/// <summary>One line of Addresses your friends can use. Value is what the copy button copies (empty: nothing to copy).</summary>
public sealed record JoinAddress(JoinAddressKind Kind, string Label, string Value, string Note);

public static class JoinPlan
{
    /// <summary>The addresses friends can type, easiest first.</summary>
    public static IReadOnlyList<JoinAddress> Addresses(JoinFacts facts)
    {
        var list = new List<JoinAddress>();
        if (facts.SteamLobbyOn == true)
            list.Add(new JoinAddress(JoinAddressKind.SteamLobby, "Steam lobby", facts.SteamName ?? "",
                string.IsNullOrWhiteSpace(facts.SteamName)
                    ? "Friends pick Steam lobby and type your Steam profile name"
                    : "Friends pick Steam lobby and type this name"));
        foreach (var vpn in facts.VirtualLans)
            list.Add(new JoinAddress(JoinAddressKind.VirtualLan, vpn.AppName, vpn.Address, "For friends in your " + vpn.AppName + " network"));
        if (facts.PublicAddress is { } publicAddress)
            list.Add(new JoinAddress(JoinAddressKind.Internet, "Internet", publicAddress,
                facts.PortOpened ? "The port is open on your router" : "Needs UDP " + facts.Port + " forwarded on your router"));
        if (facts.Lan is { } lan)
            list.Add(new JoinAddress(JoinAddressKind.SameWifi, "Same Wi Fi", lan.Address, "Only for people on your home network"));
        list.Add(new JoinAddress(JoinAddressKind.ThisPc, "This PC", Ipv4.Loopback, "Only for you, on the PC that runs the server"));
        return list;
    }

    /// <summary>
    /// A short message to paste in Discord: which launcher to open, the best addresses with the port,
    /// and whether a password is needed. The password itself is never in it. English lines; the tool
    /// translates each line for its language.
    /// </summary>
    public static IReadOnlyList<string> InviteLines(JoinFacts facts)
    {
        var lines = new List<string> { "Join my Bannerlord Coop server." };
        lines.Add(!string.IsNullOrWhiteSpace(facts.Setup)
            ? "Open " + facts.ClientTool + " and pick the " + facts.Setup + " setup."
            : !string.IsNullOrWhiteSpace(facts.Map)
                ? "Open " + facts.ClientTool + " and pick " + facts.Map + "."
                : "Open " + facts.ClientTool + ".");
        var ways = 0;
        // Steam lobby only while it is on. With a shared address (CGNAT) no internet address reaches the
        // server, so the invite says to use it; the server tools turn it on for that (NeedsSteamLobby).
        if (facts.SteamLobbyOn == true)
        {
            lines.Add(string.IsNullOrWhiteSpace(facts.SteamName)
                ? "Steam lobby. Pick Steam lobby and type my Steam profile name."
                : "Steam lobby. Pick Steam lobby and type " + facts.SteamName + ".");
            if (facts.SharedAddress)
                lines.Add("My internet does not allow direct joins, so click Steam lobby, not Direct.");
            ways++;
        }

        foreach (var vpn in facts.VirtualLans)
        {
            lines.Add(vpn.AppName + ". Join my " + vpn.AppName + " network, pick Direct and type " + vpn.Address + " with UDP port " + facts.Port + ".");
            ways++;
        }

        if (facts.PortOpened && facts.PublicAddress is { } publicAddress)
        {
            lines.Add("Internet. Pick Direct and type " + publicAddress + " with UDP port " + facts.Port + ".");
            ways++;
        }

        if (facts.Lan is { } lan)
        {
            lines.Add("Same Wi Fi only. Pick Direct and type " + lan.Address + " with UDP port " + facts.Port + ".");
            ways++;
        }

        if (ways == 0)
            lines.Add("Ask me for the address. The UDP port is " + facts.Port + ".");
        lines.Add(facts.PasswordNeeded ? "A password is needed, ask me for it." : "No password needed.");
        return lines;
    }

    /// <summary>A shared address (CGNAT) with Steam lobby off: friends off the home network have no way in.</summary>
    public static bool NeedsSteamLobby(JoinFacts facts) => facts.SharedAddress && facts.SteamLobbyOn != true;

    public const string SteamLobbyOffTitle = "Turn on Steam lobby?";
    public const string SteamLobbyOffBody = "Friends can only join you through Steam lobby, because your internet shares its address. Turn on Steam lobby and restart the server?";
    public const string SteamLobbyOffButton = "Turn on and restart";
    public const string SteamLobbyTurnedOn = "Your internet shares its address, so friends can only join through Steam lobby. Steam lobby is turned on for the next server start.";
    public const string InviteCopiedRestart = "Invite copied. The server restarts now with Steam lobby on.";

    /// <summary>What the server tool says after Copy invite. With a shared address and Steam lobby still off it says friends cannot join yet.</summary>
    public static string InviteCopied(JoinFacts facts) =>
        NeedsSteamLobby(facts)
            ? "Invite copied. Steam lobby is off, so friends outside your home network cannot join yet."
            : "Invite copied. Paste it in Discord. The password is not in it.";

    public static string Invite(JoinFacts facts, Func<string, string>? translate = null) =>
        string.Join(Environment.NewLine, InviteLines(facts).Select(line => translate == null ? line : translate(line)));
}
