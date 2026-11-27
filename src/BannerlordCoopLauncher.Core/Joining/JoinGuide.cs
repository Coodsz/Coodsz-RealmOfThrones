namespace BannerlordCoopLauncher.Core.Joining;

public enum GuideTopic
{
    SteamLobby,
    SameWifi,
    VirtualLan,
    PortForward,
    Host
}

/// <summary>One short step. Copy is a value shown with a Copy button (an address), or null.</summary>
public sealed record GuideStep(string Text, string? Copy = null);

/// <summary>A topic in the picker, with whether it works with what the tool sees right now.</summary>
public sealed record GuideChoice(GuideTopic Topic, string Title, bool WorksNow, string Status);

/// <summary>
/// The in app join guide: four ways to join plus You are the host, each with a few short steps
/// that use what the tool detected (addresses, port, router, firewall), and one line fixes for
/// the usual problems. The window only shows what this returns, so every tool can share it.
/// </summary>
public static class JoinGuide
{
    public const string Title = "How to join";
    public const string SomethingWentWrong = "Something went wrong";

    public static string TopicTitle(GuideTopic topic) => topic switch
    {
        GuideTopic.SteamLobby => "Steam lobby",
        GuideTopic.SameWifi => "Same Wi Fi",
        GuideTopic.VirtualLan => "Radmin VPN or Hamachi",
        GuideTopic.PortForward => "Port forwarding",
        _ => "You are the host"
    };

    /// <summary>
    /// The picker, easiest first: Steam lobby, same Wi Fi, a virtual LAN, port forwarding. In a
    /// server tool the ways that work right now move to the top, keeping that order. You are the
    /// host stays last.
    /// </summary>
    public static IReadOnlyList<GuideChoice> Choices(JoinFacts facts)
    {
        var ways = new[] { GuideTopic.SteamLobby, GuideTopic.SameWifi, GuideTopic.VirtualLan, GuideTopic.PortForward }
            .Select(topic => Choice(topic, facts))
            .ToList();
        if (facts.IsHost)
            ways = ways.OrderBy(choice => choice.WorksNow ? 0 : 1).ToList();
        ways.Add(Choice(GuideTopic.Host, facts));
        return ways;
    }

    /// <summary>The topic the window opens on.</summary>
    public static GuideTopic Suggested(JoinFacts facts)
    {
        if (!facts.IsHost && facts.LocalServer != null)
            return GuideTopic.Host;
        if (!facts.IsHost)
            return GuideTopic.SteamLobby;
        return Choices(facts).First().Topic;
    }

    private static GuideChoice Choice(GuideTopic topic, JoinFacts facts)
    {
        var title = TopicTitle(topic);
        if (!facts.IsHost)
            return topic == GuideTopic.Host && facts.LocalServer != null
                ? new GuideChoice(topic, title, true, "A server runs on this PC.")
                : topic == GuideTopic.VirtualLan && facts.VirtualLans.Count > 0
                    ? new GuideChoice(topic, title, true, facts.VirtualLans[0].AppName + " is on this PC.")
                    : new GuideChoice(topic, title, false, "");
        return topic switch
        {
            GuideTopic.SteamLobby => facts.SteamLobbyOn == true
                ? new GuideChoice(topic, title, true, facts.SteamOption + " is on.")
                : new GuideChoice(topic, title, false, "Turn on " + facts.SteamOption + " first."),
            GuideTopic.SameWifi => facts.Lan != null
                ? new GuideChoice(topic, title, true, "Only for friends on your home network.")
                : new GuideChoice(topic, title, false, "No home network address found."),
            GuideTopic.VirtualLan => facts.VirtualLans.Count > 0
                ? new GuideChoice(topic, title, true, facts.VirtualLans[0].AppName + " is on this PC.")
                : new GuideChoice(topic, title, false, "Radmin VPN or Hamachi is not on this PC."),
            GuideTopic.PortForward => facts.PortOpened
                ? new GuideChoice(topic, title, true, "The port was opened on your router automatically.")
                : facts.SharedAddress
                    ? new GuideChoice(topic, title, false, "Your internet provider shares your address.")
                    : facts.PortOpensOnStart
                        ? new GuideChoice(topic, title, false, "The port opens on your router when the server starts.")
                        : new GuideChoice(topic, title, false, "Needs the port open on your router."),
            _ => new GuideChoice(topic, title, false, "")
        };
    }

    public static IReadOnlyList<GuideStep> Steps(GuideTopic topic, JoinFacts facts) => topic switch
    {
        GuideTopic.SteamLobby => SteamSteps(facts),
        GuideTopic.SameWifi => WifiSteps(facts),
        GuideTopic.VirtualLan => VpnSteps(facts),
        GuideTopic.PortForward => ForwardSteps(facts),
        _ => HostSteps(facts)
    };

    private static List<GuideStep> SteamSteps(JoinFacts facts)
    {
        if (!facts.IsHost)
        {
            var client = new List<GuideStep>
            {
                new("Steam has to be open and logged in on both PCs."),
                new("The host ticks " + facts.SteamOption + " in the server tool and restarts the server.")
            };
            if (facts.Taom)
                client.Add(new(TaomLobbyWait));
            client.Add(new("Pick Steam lobby and type the host's Steam profile name exactly, the name friends see in Steam."));
            client.Add(new("Click Connect."));
            return client;
        }

        var steps = new List<GuideStep> { new("Steam has to be open and logged in on your PC and your friends' PCs.") };
        steps.Add(facts.SteamLobbyOn == true
            ? new GuideStep(facts.SteamOption + " is on. Restart the server after you change it.")
            : new GuideStep("Tick " + facts.SteamOption + " in " + facts.ServerTool + ", save, and restart the server."));
        steps.Add(string.IsNullOrWhiteSpace(facts.SteamName)
            ? new GuideStep("Friends pick Steam lobby in " + facts.ClientTool + " and type your Steam profile name exactly.")
            : new GuideStep("Friends pick Steam lobby in " + facts.ClientTool + " and type your Steam profile name exactly.", facts.SteamName));
        if (facts.Taom)
            steps.Add(new(TaomLobbyWait));
        steps.Add(new("They click Connect."));
        return steps;
    }

    // The server shows in the Steam lobby list only once the campaign has loaded, and the first
    // start of a new TAOM campaign builds the map cache before that.
    private const string TaomLobbyWait = "The first start of a new TAOM campaign can take up to an hour, so the server can take up to an hour to show up in the Steam lobby.";

    private static List<GuideStep> WifiSteps(JoinFacts facts)
    {
        const string ipconfig = "Open Command Prompt, type ipconfig and find IPv4 Address. It starts with 192.168 or 10.";
        if (!facts.IsHost)
            return new()
            {
                new("You and the host have to be on the same Wi Fi or home network."),
                new("On the host PC, open Command Prompt, type ipconfig and find IPv4 Address. It starts with 192.168 or 10."),
                new("Pick Direct, type that address and UDP port " + facts.Port + "."),
                new("Check the number carefully. 192 is often typed as 198. Then click Check server.")
            };
        return new()
        {
            new("This only works for friends on the same Wi Fi or home network as you."),
            facts.Lan is { } lan ? new GuideStep("Your address on this network is " + lan.Address + ".", lan.Address) : new GuideStep(ipconfig),
            new("Friends pick Direct, type that address and UDP port " + facts.Port + "."),
            new("Check the number carefully. 192 is often typed as 198.")
        };
    }

    private static List<GuideStep> VpnSteps(JoinFacts facts)
    {
        if (!facts.IsHost)
        {
            var steps = new List<GuideStep> { new("You and the host install the same app, Radmin VPN or Hamachi, and join the host's network.") };
            steps.Add(new("Ask the host for their address in the app. Radmin addresses usually start with 26, Hamachi addresses with 25."));
            if (facts.VirtualLans.Count > 0)
                steps.Add(new(facts.VirtualLans[0].AppName + " is on this PC. Type the host's address, not your own."));
            steps.Add(new("Pick Direct, type the host's address and UDP port " + facts.Port + "."));
            steps.Add(new("Click Check server, then Connect."));
            return steps;
        }

        var host = new List<GuideStep> { new("Everyone installs the same app, Radmin VPN or Hamachi. Make a network and give friends its name and password.") };
        if (facts.VirtualLans.Count > 0)
            host.AddRange(facts.VirtualLans.Select(vpn => new GuideStep("Your " + vpn.AppName + " address is " + vpn.Address + ".", vpn.Address)));
        else
            host.Add(new("Your address shows in the app. Radmin addresses usually start with 26, Hamachi addresses with 25."));
        host.Add(new("Friends pick Direct, type that address and UDP port " + facts.Port + "."));
        host.Add(new(facts.Firewall == FirewallState.Allowed
            ? "Windows Firewall already lets UDP " + facts.Port + " in."
            : "If Check server gets no answer, click Allow through firewall in " + facts.ServerTool + "."));
        return host;
    }

    private static List<GuideStep> ForwardSteps(JoinFacts facts)
    {
        const string shared = "If the WAN IP on the router page is different from whatismyip.com, the internet provider shares the address. Use Steam lobby or a VPN instead.";
        if (!facts.IsHost)
            return new()
            {
                new("The host forwards UDP " + facts.Port + " on their router to their PC and allows it in Windows Firewall."),
                new("The host finds their public IP on whatismyip.com and sends it to you."),
                new("Pick Direct, type that address and UDP port " + facts.Port + ", then click Check server."),
                new(shared)
            };
        if (facts.SharedAddress)
            return new()
            {
                new("Your internet provider shares your address (" + (facts.Router?.ExternalAddress ?? "") + "), so port forwarding cannot work on this connection."),
                new("Use Steam lobby or Radmin VPN instead.")
            };

        var steps = new List<GuideStep>();
        if (facts.PortOpened)
            steps.Add(new(facts.ServerTool + " opened UDP " + facts.Port + " on your router for you while the server runs."));
        else if (facts.PortOpensOnStart)
            steps.Add(new(facts.ServerTool + " tries to open UDP " + facts.Port + " on your router when the server starts. If that fails, forward it on your router page."));
        else if (facts.Lan is { } lan)
            steps.Add(new("On your router page, forward UDP " + facts.Port + " to this PC's IPv4 address " + lan.Address + ".", lan.Address));
        else
            steps.Add(new("On your router page, forward UDP " + facts.Port + " to this PC's IPv4 address."));
        steps.Add(new(facts.Firewall == FirewallState.Allowed
            ? "Windows Firewall already lets UDP " + facts.Port + " in."
            : "Click Allow through firewall so Windows Firewall lets UDP " + facts.Port + " in."));
        steps.Add(facts.PublicAddress is { } publicAddress
            ? new GuideStep("Friends type your public address " + publicAddress + " and UDP port " + facts.Port + ".", publicAddress)
            : new GuideStep("Friends type your public IP from whatismyip.com and UDP port " + facts.Port + "."));
        steps.Add(new("Checking your own public IP from this PC can fail on some routers. Ask a friend to click Check server."));
        steps.Add(new(shared));
        return steps;
    }

    private static List<GuideStep> HostSteps(JoinFacts facts)
    {
        var port = facts.LocalServer?.Port ?? facts.Port;
        var steps = new List<GuideStep>();
        if (!facts.IsHost && facts.LocalServer != null)
            steps.Add(new("A server runs on this PC right now, so you are the host."));
        steps.Add(new(facts.IsHost
            ? "Start the server here and wait until it is ready."
            : "Start the server in the server tool and wait until it is ready."));
        if (facts.Taom)
            steps.Add(new("The first start of a new TAOM campaign can take up to an hour. Let it finish."));
        steps.Add(new("In " + facts.ClientTool + " pick Direct, type 127.0.0.1 and UDP port " + port + ".", Ipv4.Loopback));
        steps.Add(new("127.0.0.1 only means this PC. Never give it to friends."));
        return steps;
    }

    /// <summary>One line fixes for the usual problems, for the Something went wrong list.</summary>
    public static IReadOnlyList<string> Fixes(JoinFacts facts)
    {
        var tool = facts.IsHost ? facts.ServerTool : "the server tool";
        return new[]
        {
            "No answer on Check server. Make sure the server is running, the address and port are right, and Windows Firewall allows UDP " + facts.Port + ".",
            "Stuck on Contacting the server. The address is wrong (often your own VPN or router), or the host's internet does not allow direct joins. Use Steam lobby.",
            "Module validation failed. Use the same setup on both sides, save the module settings in " + tool + " and restart the server.",
            "The save does not match the mods. Start a new campaign with this mod list.",
            "Mod Configuration Menu is missing or too old. Get v5.12.3 or newer from Nexus Mods and put it in the game's Modules folder.",
            "A game module is missing. In Steam, open Bannerlord properties and verify the game files.",
            "A Workshop mod was removed. Get it from Nexus Mods or ModDB.",
            "Big battles lag. Lower the battle size in " + tool + ".",
            "Still stuck. Click Send my logs and post the code you get on Discord."
        };
    }
}
