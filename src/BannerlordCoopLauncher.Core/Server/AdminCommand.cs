// Ported from CoopKeep (https://github.com/Aderimo/bannerlord-coopkeep), Server/AdminCommand.cs,
// translated from Turkish and changed for the Coodsz tools (the coopkeep.* server plugin
// commands are left out, set_gold by name and hero.id are added).

using System.Globalization;

namespace BannerlordCoopLauncher.Core.Server;

/// <summary>
/// The coop.debug.* commands the cheat pages send, built with their numbers clamped.
/// </summary>
/// <remarks>
/// <para>
/// CoopKeep read the usage text of each command from a Coop v0.1.6 server and checked the
/// result with <c>coop.debug.hero.info</c>:
/// </para>
/// <code>
/// coop.debug.hero.set_gold_state &lt;hero_id&gt; &lt;gold&gt;        HERO_GOLD_SET hero=... oldGold=... newGold=...
/// coop.debug.hero.set_hitpoints &lt;hero_id&gt; &lt;hit_points&gt;
/// coop.debug.hero.set_age &lt;hero_name_or_id&gt; &lt;age&gt;
/// coop.debug.hero.info &lt;hero_id&gt;
/// </code>
/// <para>
/// Coop removed <c>coop.debug.hero.SetGold</c>. <c>set_gold_state</c> takes the hero id and
/// is in every command list seen so far (Coodsz Host 2026-08, TAOM 0.3.25 2026-10-18);
/// <c>set_gold</c> takes the hero name and only some servers have it (TAOM 0.3.25 does).
/// The cheat pages (TaomCoop.Core CheatActions) send set_gold_state and fall back to set_gold
/// when the server does not know it or it fails.
/// </para>
/// <para>
/// The game's own cheats (<c>campaign.add_gold_to_hero</c> and the like) answer
/// "Cheat mode is disabled!" on the server, which is why these exist.
/// </para>
/// </remarks>
public static class AdminCommand
{
    /// <summary>The commands the builders below make. Anything else is not one of them.</summary>
    public static readonly IReadOnlySet<string> Allowed = new HashSet<string>(StringComparer.Ordinal)
    {
        "coop.debug.hero.set_gold_state",
        "coop.debug.hero.set_gold",
        "coop.debug.hero.set_hitpoints",
        "coop.debug.hero.set_age",
        "coop.debug.hero.info",
        "coop.debug.hero.id",
        "coop.debug.hero.list",
        "coop.debug.clan.give_influence",
        "coop.debug.clan.add_influence",
        "coop.debug.clan.add_renown",
        "coop.debug.clan.info",
        "coop.debug.clan.list",
        "coop.debug.kingdom.list",
        "coop.debug.mobile_party.set_troop_state",
        "coop.debug.mobile_party.set_troop_wounded",
        "coop.debug.mobile_party.character_ids",
        "coop.debug.mobile_party.position",
        "coop.debug.mobile_party.move_to_settlement",
        "coop.debug.mobile_party.add_troops",
        "coop.debug.mobile_party.add_troop_xp",
        "coop.debug.mobileparty.info",
        "coop.debug.settlements.owner_state",
        "coop.debug.item_object.data",
        "coop.debug.players.list",
        "coop.debug.campaign_options.list",
        "coop.debug.mod_config.list",
        "coop.server.shutdown",
    };

    public const string SetGoldById = "coop.debug.hero.set_gold_state";
    public const string SetGoldByNameCommand = "coop.debug.hero.set_gold";
    public const string HeroIdCommand = "coop.debug.hero.id";
    public const string HeroInfoCommand = "coop.debug.hero.info";
    public const string ClanListCommand = "coop.debug.clan.list";
    public const string GiveInfluenceCommand = "coop.debug.clan.give_influence";
    public const string PlayersListCommand = "coop.debug.players.list";

    /// <summary>
    /// The value used for "full health". hero.info does not give the maximum; perks can
    /// lift it over 100.
    /// </summary>
    public const int FullHitPoints = 100;

    /// <summary>Top limit for gold, so a typo does not give someone billions.</summary>
    public const int MaxGold = 10_000_000;

    public const int MinAge = 18;
    public const int MaxAge = 90;
    public const int MaxInfluence = 10_000;
    public const int MaxRenown = 10_000;
    public const int MaxTroopXp = 1_000_000;

    /// <summary>Most troops of one kind a party can be set to.</summary>
    public const int MaxTroopCount = 1000;

    /// <summary>Sets the hero's gold to this value (by hero id).</summary>
    /// <param name="heroId">The hero's registry id, as coop.debug.hero.id prints it.</param>
    public static string SetGold(string heroId, int gold) =>
        SetGoldById + " " + RequireId(heroId) + " " + Number(Math.Clamp(gold, 0, MaxGold));

    /// <summary>Sets the hero's gold to this value (by hero name, for servers that have set_gold).</summary>
    public static string SetGoldByName(string heroName, int gold) =>
        SetGoldByNameCommand + " " + QuoteName(heroName) + " " + Number(Math.Clamp(gold, 0, MaxGold));

    /// <summary>
    /// Sets the hero's hit points, 1 to 100. Zero is not allowed: what it does is unclear,
    /// and Coop has kill_player for that.
    /// </summary>
    public static string SetHitPoints(string heroId, int hitPoints) =>
        "coop.debug.hero.set_hitpoints " + RequireId(heroId) + " " + Number(Math.Clamp(hitPoints, 1, FullHitPoints));

    public static string Heal(string heroId) => SetHitPoints(heroId, FullHitPoints);

    public static string SetAge(string heroId, int age) =>
        "coop.debug.hero.set_age " + RequireId(heroId) + " " + Number(Math.Clamp(age, MinAge, MaxAge));

    /// <summary>Name, gold, health, age and clan of one hero.</summary>
    public static string HeroInfo(string heroId) => HeroInfoCommand + " " + RequireId(heroId);

    /// <summary>Lines of <c>ID: '...', Name: '...'</c> for the heroes with this name.</summary>
    public static string HeroId(string heroName) => HeroIdCommand + " " + QuoteName(heroName);

    /// <summary>Gives a clan influence, 1 to 10000.</summary>
    public static string GiveInfluence(string clanId, int amount) =>
        GiveInfluenceCommand + " " + RequireId(clanId) + " " + Number(Math.Clamp(amount, 1, MaxInfluence));

    /// <summary>Adds influence, or takes it away with a negative amount; the server prints the new value.</summary>
    public static string AddInfluence(string clanId, int amount) =>
        "coop.debug.clan.add_influence " + RequireId(clanId) + " " + Number(Math.Clamp(amount, -MaxInfluence, MaxInfluence));

    public static string AddRenown(string clanId, int amount) =>
        "coop.debug.clan.add_renown " + RequireId(clanId) + " " + Number(Math.Clamp(amount, 1, MaxRenown));

    public static string ClanInfo(string clanId) => "coop.debug.clan.info " + RequireId(clanId);

    public static string HeroList() => "coop.debug.hero.list";
    public static string ClanList() => ClanListCommand;
    public static string KingdomList() => "coop.debug.kingdom.list";

    /// <summary>
    /// Sets how many of one troop a party has (giving and taking both). 0 removes the troop.
    /// Hero rows are refused by the server.
    /// </summary>
    public static string SetTroopState(string partyId, string troopId, int number, int wounded = 0, int xp = 0)
    {
        number = Math.Clamp(number, 0, MaxTroopCount);
        wounded = Math.Clamp(wounded, 0, number);
        var exists = number > 0 ? "true" : "false";
        return "coop.debug.mobile_party.set_troop_state " + RequireId(partyId) + " " + RequireId(troopId) + " " + exists
            + " " + Number(number) + " " + Number(wounded) + " " + Number(Math.Max(0, xp));
    }

    /// <summary>Sets the wounded count (0 heals them all).</summary>
    public static string SetTroopWounded(string partyId, string troopId, int wounded) =>
        "coop.debug.mobile_party.set_troop_wounded " + RequireId(partyId) + " " + RequireId(troopId) + " " + Number(Math.Max(0, wounded));

    /// <summary>
    /// The troops in the hero's party. This one only takes the game id (<c>lord_1_1</c>),
    /// not the Coop registry id (<c>Hero_lord_1_1</c>), so the prefix is dropped.
    /// </summary>
    public static string PartyRoster(string heroId) => "coop.debug.mobile_party.character_ids " + GameStringId(RequireId(heroId));

    /// <summary>Coop registry id to game id: <c>Hero_lord_1_1</c> becomes <c>lord_1_1</c>.</summary>
    public static string GameStringId(string registryId) =>
        registryId.StartsWith("Hero_", StringComparison.Ordinal) && registryId.Length > 5 ? registryId[5..] : registryId;

    public static string PartyPosition(string partyId) => "coop.debug.mobile_party.position " + RequireId(partyId);

    /// <summary>Moves a player party into a settlement at once. Lord parties are refused by the server.</summary>
    public static string MoveToSettlement(string partyId, string settlementId) =>
        "coop.debug.mobile_party.move_to_settlement " + RequireId(partyId) + " " + RequireId(settlementId) + " true";

    public static string SettlementOwner(string settlementId) => "coop.debug.settlements.owner_state " + RequireId(settlementId);

    /// <summary>The item's value in the game.</summary>
    public static string ItemData(string itemId) => "coop.debug.item_object.data " + RequireId(itemId);

    /// <summary>Adds Coop's fixed group of recruits to the hero's party (kind and count cannot be picked).</summary>
    public static string AddTroops(string heroName) => "coop.debug.mobile_party.add_troops " + QuoteName(heroName);

    public static string AddTroopXp(string heroName, int xp) =>
        "coop.debug.mobile_party.add_troop_xp " + QuoteName(heroName) + " " + Number(Math.Clamp(xp, 1, MaxTroopXp));

    public static string PartyInfo(string partyId) => "coop.debug.mobileparty.info " + RequireId(partyId);

    public static string PlayersList() => PlayersListCommand;
    public static string CampaignOptionsList() => "coop.debug.campaign_options.list";
    public static string ModConfigList() => "coop.debug.mod_config.list";

    /// <summary>Stops new joins, drops everyone after this many seconds and saves.</summary>
    public static string ScheduleShutdown(int seconds) => "coop.server.shutdown " + Number(Math.Clamp(seconds, 5, 3600));

    public static string CancelShutdown() => "coop.server.shutdown cancel";

    /// <summary>Is this one of the commands above, on one line?</summary>
    public static bool IsAllowed(string? commandLine)
    {
        if (string.IsNullOrWhiteSpace(commandLine))
            return false;
        if (commandLine.Any(c => c is '\r' or '\n'))
            return false;
        return Allowed.Contains(commandLine.Split(' ', 2)[0]);
    }

    /// <summary>
    /// Does the reply say the server does not know the command? The dedicated server prints
    /// "unknown command 'x'", the game console "no game console command 'x'", and the console
    /// queue "ERROR|Unknown command".
    /// </summary>
    public static bool IsUnknownCommandReply(IEnumerable<string>? reply) =>
        reply != null && reply.Any(line => line.Contains("unknown command", StringComparison.OrdinalIgnoreCase)
            || line.Contains("no game console command", StringComparison.OrdinalIgnoreCase)
            || line.Contains("no console command", StringComparison.OrdinalIgnoreCase));

    /// <summary>A number the player typed, or the fallback when it is not one.</summary>
    public static int ParseAmount(string? text, int fallback) =>
        int.TryParse((text ?? "").Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) ? value : fallback;

    private static string Number(int value) => value.ToString(CultureInfo.InvariantCulture);

    /// <summary>For commands that take a name: quoted when it has a space; a quote mark is refused.</summary>
    private static string QuoteName(string? name)
    {
        var clean = ServerCommand.SanitizeArgument(name, 64);
        if (clean.Length == 0)
            throw new ArgumentException("Pick a player first.", nameof(name));
        if (clean.Contains('"'))
            throw new ArgumentException("A name with a quote mark cannot be sent: " + clean, nameof(name));
        return clean.Contains(' ') ? "\"" + clean + "\"" : clean;
    }

    /// <summary>
    /// Ids have no spaces or quotes. One that does is most likely a name, so no command is
    /// made rather than a wrong one.
    /// </summary>
    private static string RequireId(string? id)
    {
        var clean = ServerCommand.SanitizeArgument(id, 64);
        if (clean.Length == 0)
            throw new ArgumentException("The id is empty.", nameof(id));
        if (clean.Any(c => c is ' ' or '"' or '\''))
            throw new ArgumentException("This is not an id: " + clean, nameof(id));
        return clean;
    }
}
