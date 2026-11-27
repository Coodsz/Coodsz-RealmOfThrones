using System.Text.RegularExpressions;

namespace BannerlordCoopLauncher.Core.Launch;

public static class JoinResultExplain
{
    public const string SteamUnreachable = "Could not reach the host through Steam. Check the host is still in their session and ask for a new invite. If it still fails, close Cloudflare WARP or any other DNS changer.";

    public static bool Succeeded(string? log)
    {
        if (string.IsNullOrWhiteSpace(log) || Failed(log))
            return false;
        return log.Contains("Connected! Please wait for transfer", StringComparison.OrdinalIgnoreCase)
            || log.Contains("Joining Coop Campaign", StringComparison.OrdinalIgnoreCase)
            || log.Contains("Sending your character to the host", StringComparison.OrdinalIgnoreCase)
            || log.Contains("Received host save transfer", StringComparison.OrdinalIgnoreCase);
    }

    /// <param name="warSailsSetup">True for a tool whose own setup needs War Sails (Realm of Thrones): the two lines that
    /// say the Coodsz tools can't host or join War Sails are left out there.</param>
    /// <param name="nightly">True when this side runs the Coop nightly (CoopInstall.IsNightly): the Coop and Friends tools
    /// host War Sails on it, so the War Sails lines say to turn War Sails (NavalDLC) on or off in the Client Launcher's
    /// mod list instead, and a Bannerlord gap asks for the same nightly build.</param>
    public static IReadOnlyList<string> Explain(string? log, IReadOnlyList<string>? localProblems = null, bool warSailsSetup = false, bool nightly = false)
    {
        var lines = new List<string>();
        if (string.IsNullOrWhiteSpace(log) || !Failed(log))
            return lines;

        if (log.Contains("password is incorrect", StringComparison.OrdinalIgnoreCase))
            lines.Add("The server password is wrong.");

        // A Steam join has no address or port to check: the host left, the invite is old, or WARP or a DNS changer blocks Steam.
        // The log holds every try since launch, so once a retry reached the host the failure is something else, said below.
        if (log.Contains("Could not reach the co-op host through Steam", StringComparison.OrdinalIgnoreCase))
        {
            if (!log.Contains("Steam tunnel to the host established", StringComparison.OrdinalIgnoreCase))
                lines.Add(SteamUnreachable);
        }
        else if (log.Contains("Could not reach the co-op host", StringComparison.OrdinalIgnoreCase)
            || log.Contains("waiting for the server to validate the connection", StringComparison.OrdinalIgnoreCase))
            lines.Add("Could not finish joining. Check the address and that UDP 4200 is open.");

        foreach (Match match in VersionMismatch.Matches(log))
            lines.Add(VersionLine(match.Groups["mod"].Value, match.Groups["who"].Value.Equals("client", StringComparison.OrdinalIgnoreCase) ? match.Groups["right"].Value : match.Groups["left"].Value, match.Groups["who"].Value.Equals("client", StringComparison.OrdinalIgnoreCase) ? match.Groups["left"].Value : match.Groups["right"].Value));
        foreach (Match match in ModuleVersion.Matches(log))
        {
            lines.Add(VersionLine(match.Groups["mod"].Value, match.Groups["yours"].Value, match.Groups["server"].Value));
            var same = "The host and every player need the same version of " + match.Groups["mod"].Value + ".";
            if (!lines.Contains(same))
                lines.Add(same);
        }
        if (ModuleVersion.Matches(log).Any(IsMcm))
            lines.Add(McmSameVersion);
        // War Sails (NavalDLC): the Coodsz launchers never put NavalDLC in the game's module list, and the server tools
        // refuse to host it (WarSailsCheck), except on the Coop nightly, where War Sails is a mod the player turns on or
        // off. Coop names it 'War Sails' (NavalDLC) or 'NavalDLC'.
        if (!warSailsSetup && RequiredModule.Matches(log).Any(required =>
                (required.Groups["id"].Success ? required.Groups["id"] : required.Groups["mod"]).Value.Trim().Equals("NavalDLC", StringComparison.OrdinalIgnoreCase)))
            lines.Add(nightly ? WarSailsServerNightly : WarSailsServer);
        if (!warSailsSetup && NavalDlcRefused.IsMatch(log))
            lines.Add(nightly ? WarSailsInGameNightly : WarSailsInGame);
        foreach (Match match in GameVersion.Matches(log))
            lines.Add(VersionLine("Bannerlord", match.Groups["yours"].Value, match.Groups["server"].Value));
        var buildGap = OfficialBuildGap(log);
        if (buildGap != null)
            lines.Add("Bannerlord builds do not match. The dedicated server is " + buildGap.Value.Server + ". The joining game is " + buildGap.Value.Client + ".");
        var gameGap = GameVersion.Match(log);
        if (buildGap != null || gameGap.Success)
        {
            // The Workshop Coop is made for Bannerlord 1.4.8, so updating it never matches a server on a newer Bannerlord.
            lines.Add(nightly || NewerThan148(gameGap.Success ? gameGap.Groups["server"].Value : null) || NewerThan148(buildGap?.Server)
                ? NightlySameBuild
                : "What to do: Update Bannerlord Coop in Steam Workshop, item 3770450698, so the dedicated server is the same Bannerlord build as the game.");
            lines.Add("What not to do: Do not copy the game folder onto the dedicated server. The game is the player program. The server keeps its own copy.");
        }
        var protocol = ProtocolVersion.Match(log);
        if (protocol.Success)
            lines.Add(VersionLine("Co-op protocol", protocol.Groups["yours"].Value, protocol.Groups["server"].Value));

        if (log.Contains("did not report an exact build", StringComparison.OrdinalIgnoreCase))
            lines.Add("The co-op build does not match the server. Update Bannerlord Coop on both sides.");

        var missing = NamesAfter(log, MissingMods);
        if (missing.Count > 0)
            lines.Add("These mods are missing: " + string.Join(", ", missing) + ".");

        var incompatible = NamesAfter(log, IncompatibleMods);
        if (incompatible.Count > 0)
            lines.Add("These mods are not compatible with the server: " + string.Join(", ", incompatible) + ".");

        if (!lines.Any(line => line.Contains("does not match the server", StringComparison.Ordinal))
            && !lines.Any(line => line.StartsWith("These mods are", StringComparison.Ordinal))
            && (log.Contains("validate the module list", StringComparison.OrdinalIgnoreCase)
                || log.Contains("Module validation failed", StringComparison.OrdinalIgnoreCase)
                || log.Contains("unsupported modules", StringComparison.OrdinalIgnoreCase)))
        {
            lines.Add("The server rejected the module list.");
            var named = QuotedModules(log);
            if (named.Count > 0)
                lines.Add("The server named: " + string.Join(", ", named) + ".");
        }

        if (log.Contains("does not support module", StringComparison.OrdinalIgnoreCase))
        {
            if (!lines.Any(line => line.StartsWith("The server rejected the module list.", StringComparison.Ordinal)))
                lines.Add("The server rejected the module list.");
            var unsupported = QuotedModules(log);
            if (unsupported.Count > 0
                && unsupported.All(name => SettlementMods.Contains(name)))
            {
                lines.Add("What to do: " + string.Join(", ", unsupported)
                    + " only run when the host has Player Settlement on. If this host is not using Player Settlement, use a normal unlocked setup, turn Player Settlement off, Save, then Connect. If the host is on a Working stable setup, install Player Settlement and PSCoopBridge in the dedicated server Modules folder, pick that locked setup, and press Start server again.");
                lines.Add("What not to do: Do not leave PSCoopBridge on when the host does not have Player Settlement.");
            }
            else
            {
                lines.Add("What to do: The dedicated server started without those mods. In the server tool, select the same locked setup the player is joining with, then press Start server. That start writes the full module list. A server that only loads Native, SandBoxCore, Sandbox, Coop, and DedicatedServer.Windows rejects this join.");
                lines.Add("What not to do: Do not turn those mods off on the client to match the short server list. The locked setup is the list that plays.");
            }
        }

        // Coop 0.1.7 writes 'Display Name' (Id) when a mod has a name of its own, and one line for each mod it needs. The id
        // is what counts here, on any of those lines.
        if (RequiredModule.Matches(log).Any(required =>
                (required.Groups["id"].Success ? required.Groups["id"] : required.Groups["mod"]).Value.Trim().Equals(Profile.SettlementBridgeRule.Bridge, StringComparison.OrdinalIgnoreCase))
            && !log.Contains("'" + Profile.SettlementBridgeRule.PlayerSettlement + "'", StringComparison.OrdinalIgnoreCase)
            && !log.Contains("(" + Profile.SettlementBridgeRule.PlayerSettlement + ")", StringComparison.OrdinalIgnoreCase))
        {
            lines.Add("The server has PSCoopBridge on without Player Settlement. This launcher leaves PSCoopBridge off in that case, because without Player Settlement it makes the game run at about 10 fps.");
            lines.Add("What to do: The host updates Coodsz Coop Server Tool (it turns PSCoopBridge off the same way), or turns PSCoopBridge off, then starts a new campaign.");
        }

        if (localProblems != null)
        {
            foreach (var problem in localProblems)
            {
                if (!lines.Any(line => line.Contains(problem, StringComparison.OrdinalIgnoreCase)))
                    lines.Add(problem);
            }
        }

        if (lines.Count == 0 && log.Contains("rejected the connection", StringComparison.OrdinalIgnoreCase))
            lines.Add("The server rejected the connection.");

        return lines;
    }

    public const string McmSameVersion = "With Mod Configuration Menu (MCM) one side usually has an older copy, so both update MCM to the same version.";

    public const string NightlySameBuild = "What to do: The host and every player need the same Coop build, made for the same Bannerlord version as the game.";

    public const string WarSailsServer = "This server runs War Sails (NavalDLC). The Coodsz tools can't host or join War Sails yet, so it was not set up with them. Ask the host for a server without War Sails.";

    // Add mods hides a mod the setup already has (a War Sails row turned off), and a locked setup takes no mods.
    public const string WarSailsServerNightly = "This server runs War Sails (NavalDLC), so every player needs it on. In the Coodsz Coop Client Launcher open the Mods page. If War Sails (NavalDLC) is on the list, turn it on. If not, click Add mods and add it. A locked setup can't take mods, so click New setup on the Settings page first. Then press Save mods and click Connect again.";

    public const string WarSailsInGameNightly = "War Sails (NavalDLC) is on in the joining game, and this server doesn't run War Sails. In the Coodsz Coop Client Launcher turn War Sails (NavalDLC) off on the Mods page, press Save mods, then click Connect again.";

    public const string WarSailsInGame = "War Sails (NavalDLC) is on in the joining game, and this server doesn't run War Sails. Start the game from a Coodsz launcher, which leaves NavalDLC off, or turn NavalDLC off in the game's own launcher, then join again.";

    private static bool IsMcm(Match match) =>
        match.Groups["id"].Value.Trim().Equals("Bannerlord.MBOptionScreen", StringComparison.OrdinalIgnoreCase)
        || match.Groups["mod"].Value.Trim().Equals("Bannerlord.MBOptionScreen", StringComparison.OrdinalIgnoreCase)
        || match.Groups["mod"].Value.Contains("Mod Configuration Menu", StringComparison.OrdinalIgnoreCase);

    private static readonly Regex NavalDlcRefused = new(
        @"(?i)DLC is not supported\.[^\r\n]*\bNavalDLC\b",
        RegexOptions.Compiled);

    private static readonly HashSet<string> SettlementMods = new(StringComparer.OrdinalIgnoreCase)
    {
        "PSCoopBridge",
        "PlayerSettlement",
        "AgricultureEstate"
    };

    private static bool Failed(string log) =>
        log.Contains("Module validation failed", StringComparison.OrdinalIgnoreCase)
        || log.Contains("failed to validate the module list", StringComparison.OrdinalIgnoreCase)
        || log.Contains("Connection rejected by server", StringComparison.OrdinalIgnoreCase)
        || log.Contains("The server rejected the connection", StringComparison.OrdinalIgnoreCase)
        || log.Contains("password is incorrect", StringComparison.OrdinalIgnoreCase)
        || log.Contains("Could not reach the co-op host", StringComparison.OrdinalIgnoreCase)
        || log.Contains("waiting for the server to validate the connection", StringComparison.OrdinalIgnoreCase)
        || log.Contains("unsupported modules", StringComparison.OrdinalIgnoreCase)
        || log.Contains("Wrong version of module", StringComparison.OrdinalIgnoreCase)
        || log.Contains("Wrong game version detected", StringComparison.OrdinalIgnoreCase)
        || log.Contains("host's protocol version", StringComparison.OrdinalIgnoreCase)
        || log.Contains("does not support module", StringComparison.OrdinalIgnoreCase)
        || log.Contains("module validation:", StringComparison.OrdinalIgnoreCase);

    private static string VersionLine(string name, string yourVersion, string serverVersion)
    {
        var compare = CompareVersions(serverVersion, yourVersion);
        var newer = compare > 0
            ? " The server has a newer one than yours."
            : compare < 0
                ? " Yours is newer than the server."
                : "";
        return name + " does not match the server. The server is " + serverVersion + ". Yours is " + yourVersion + "." + newer;
    }

    private static int CompareVersions(string left, string right)
    {
        var a = Parts(left);
        var b = Parts(right);
        var count = Math.Max(a.Count, b.Count);
        for (var i = 0; i < count; i++)
        {
            var av = i < a.Count ? a[i] : 0;
            var bv = i < b.Count ? b[i] : 0;
            if (av != bv)
                return av.CompareTo(bv);
        }

        return 0;
    }

    private static List<int> Parts(string version)
    {
        var parts = new List<int>();
        foreach (Match match in Regex.Matches(version, @"\d+"))
        {
            if (int.TryParse(match.Value, out var number))
                parts.Add(number);
        }

        return parts;
    }

    private static List<string> QuotedModules(string log)
    {
        var names = new List<string>();
        // Coop 0.1.7 writes 'Display Name' (Id); the id is the name the checks below know.
        foreach (Match match in Regex.Matches(log, @"'([^']+)'(?: \(([^)]+)\))?"))
        {
            var name = (match.Groups[2].Success ? match.Groups[2] : match.Groups[1]).Value.Trim();
            if (name.Length == 0 || Regex.IsMatch(name, @"^v?\d+(\.\d+)*$", RegexOptions.IgnoreCase))
                continue;
            if (!names.Contains(name, StringComparer.OrdinalIgnoreCase))
                names.Add(name);
        }

        return names;
    }

    private static List<string> NamesAfter(string log, Regex pattern)
    {
        var match = pattern.Match(log);
        if (!match.Success)
            return new List<string>();
        return match.Groups["names"].Value
            .Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(name => name.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static readonly Regex VersionMismatch = new(
        @"'(?<mod>[^']+)' version '(?<left>[^']+)', but the (?<who>client|server) uses '(?<right>[^']+)'",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex RequiredModule = new(
        @"To join the server the module '(?<mod>[^']+)'(?: \((?<id>[^)]+)\))?",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex MissingMods = new(
        @"missing mods?:\s*(?<names>[A-Za-z0-9_.,; ]+)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex IncompatibleMods = new(
        @"incompatible mods?:\s*(?<names>[A-Za-z0-9_.,; ]+)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex ModuleVersion = new(
        @"Wrong version of module '(?<mod>[^']+)'(?: \((?<id>[^)]+)\))?(?: with version '(?<have>[^']+)')? detected\. Server uses '(?<server>[^']+)', client uses '(?<yours>[^']+)'",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex GameVersion = new(
        @"Wrong game version detected\. Server uses '(?<server>[^']+)', client uses '(?<yours>[^']+)'",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static (string Server, string Client)? OfficialBuildGap(string log)
    {
        var match = LoadedSides.Match(log);
        if (!match.Success)
            return null;
        var server = VersionOf("Native", match.Groups["server"].Value) ?? VersionOf("SandBoxCore", match.Groups["server"].Value);
        var client = VersionOf("Native", match.Groups["client"].Value) ?? VersionOf("SandBoxCore", match.Groups["client"].Value);
        if (server == null || client == null || server.Equals(client, StringComparison.OrdinalIgnoreCase))
            return null;
        return (server, client);
    }

    // "v1.5.7", "1.4.8.12345" and the like; false when it can't be read.
    private static bool NewerThan148(string? version)
    {
        var match = Regex.Match(version ?? "", @"(?<major>\d{1,6})\.(?<minor>\d{1,6})(?:\.(?<patch>\d{1,6}))?");
        if (!match.Success)
            return false;
        var parsed = new Version(int.Parse(match.Groups["major"].Value), int.Parse(match.Groups["minor"].Value), match.Groups["patch"].Success ? int.Parse(match.Groups["patch"].Value) : 0);
        return parsed > new Version(1, 4, 8);
    }

    private static string? VersionOf(string module, string list)
    {
        var match = Regex.Match(list, @"\b" + Regex.Escape(module) + @"\s+v?(?<ver>\d+(?:\.\d+)+)", RegexOptions.IgnoreCase);
        return match.Success ? match.Groups["ver"].Value : null;
    }

    private static readonly Regex LoadedSides = new(
        @"server=\[(?<server>[^\]]+)\]\s*client=\[(?<client>[^\]]+)\]",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex ProtocolVersion = new(
        @"host's protocol version is (?<server>\d+) while your protocol version is (?<yours>\d+)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);
}
