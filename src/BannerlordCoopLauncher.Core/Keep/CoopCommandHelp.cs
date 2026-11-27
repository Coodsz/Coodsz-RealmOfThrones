using System.Text.RegularExpressions;

namespace BannerlordCoopLauncher.Core.Keep;

/// <summary>Help for one parameter: what it is, an example value, where to find it and the list command that shows it.</summary>
public sealed record CoopParamHelp(string What, string Example, string Where, string? FindCommand = null);

/// <summary>The "How to use" guide of one command on the Console page.</summary>
public sealed record CoopCommandGuide(
    string Plain,
    IReadOnlyList<(CoopParameter Parameter, CoopParamHelp Help)> Parameters,
    IReadOnlyList<string> ExampleValues,
    string ExampleLine,
    IReadOnlyList<string> FindCommands);

/// <summary>
/// Plain help for the Coop commands of the Console page: a better description where the site's is
/// thin, and for every parameter what it is, an example and the list command that shows its ids.
/// Ids and list commands were checked against the Coop source.
/// </summary>
public static class CoopCommandHelp
{
    public const string HeroList = "coop.debug.hero.list";
    public const string ClanList = "coop.debug.clan.list";
    public const string KingdomList = "coop.debug.kingdom.list";
    public const string TownList = "coop.debug.town.list_towns";
    public const string VillageList = "coop.debug.village.list";
    public const string PlayerList = "coop.debug.players.list";
    public const string PartyList = "coop.debug.mobileparty.list";
    public const string ArmyList = "coop.debug.army.list";
    public const string LocationList = "coop.debug.location.list";
    public const string CharacterList = "coop.debug.character_objects.list";
    public const string ItemList = "coop.debug.town.list_items";
    public const string BannerItemList = "coop.debug.hero.list_banneritems";
    public const string IssueTypeList = "coop.debug.issues.list_types";
    public const string SiegeList = "coop.debug.siege.list";

    /// <summary>The hand written descriptions of the commands people use most.</summary>
    private static readonly Dictionary<string, string> Plains = new(StringComparer.Ordinal)
    {
        ["coop.unstuck"] = "Frees a stuck player. Fill in their controller id, because without a player the server refuses it.",
        ["coop.debug.players.list"] = "Lists every player the server knows, online or not, with their controller id and the ids of their hero, party and clan.",
        ["coop.debug.players.party_state"] = "Shows what the server knows about one player's party.",
        ["coop.debug.hero.list"] = "Lists heroes with their ids. Add the start of a name to see only the heroes whose name begins with it.",
        ["coop.debug.hero.id"] = "Finds the id of every hero with exactly this name.",
        ["coop.debug.hero.info"] = "Shows everything the server stores about one hero.",
        ["coop.debug.hero.set_gold"] = "Sets the gold of every hero with exactly this name.",
        ["coop.debug.hero.set_gold_state"] = "Sets the gold of one hero, found by id. Gold can't go below 0.",
        ["coop.debug.hero.gold_state"] = "Shows how much gold one hero has.",
        ["coop.debug.hero.set_age"] = "Sets a hero's age in years.",
        ["coop.debug.hero.set_hitpoints"] = "Sets a hero's health.",
        ["coop.debug.hero.set_relation"] = "Sets how much two heroes like each other, from minus 100 to 100.",
        ["coop.debug.hero.get_relation"] = "Shows how much two heroes like each other.",
        ["coop.debug.hero.kill_player"] = "Kills a player's hero. Use it only when a hero is stuck and the player agrees.",
        ["coop.debug.hero.create_hero"] = "Makes a new hero from a troop or lord template.",
        ["coop.debug.hero.add_power"] = "Adds power to a hero.",
        ["coop.debug.hero.refresh_volunteers"] = "Refreshes the recruits waiting in a settlement.",
        ["coop.debug.hero_developer.add_skill_xp"] = "Adds XP to one skill of a hero.",
        ["coop.debug.hero_developer.add_attribute_points"] = "Adds unspent attribute points to a hero.",
        ["coop.debug.hero_developer.add_focus_points"] = "Adds unspent focus points to a hero.",
        ["coop.debug.hero_developer.reset_skills"] = "Resets a hero's skills, attributes and focus. This can't be undone.",
        ["coop.debug.hero_developer.stats"] = "Shows a hero's skills, attributes and focus.",
        ["coop.debug.clan.list"] = "Lists every clan with its id.",
        ["coop.debug.clan.info"] = "Shows a short summary of a clan.",
        ["coop.debug.clan.add_influence"] = "Adds influence to a clan. A minus number takes it away.",
        ["coop.debug.clan.give_influence"] = "Gives influence to a clan.",
        ["coop.debug.clan.add_renown"] = "Adds renown to a clan. The clan tier follows the renown.",
        ["coop.debug.clan.join_kingdom"] = "Makes a clan join a kingdom.",
        ["coop.debug.clan.leave_kingdom"] = "Makes a clan leave its kingdom.",
        ["coop.debug.clan.change_clan_kingdom"] = "Moves a clan to another kingdom.",
        ["coop.debug.clan.change_clan_leader"] = "Makes another hero the leader of a clan.",
        ["coop.debug.clan.add_companion"] = "Adds a companion to a clan.",
        ["coop.debug.clan.remove_companion"] = "Takes a companion out of their clan.",
        ["coop.debug.clan.add_heirs"] = "Adds heirs to a player clan, so the player can go on after their hero dies.",
        ["coop.debug.clan.destroy_clan"] = "Removes a clan from the game. This can't be undone.",
        ["coop.debug.clan.daily_gold_change"] = "Shows how much gold a clan is expected to win or lose each day.",
        ["coop.debug.kingdom.list"] = "Lists every kingdom with its id.",
        ["coop.debug.kingdom.info"] = "Shows what the server stores about a kingdom.",
        ["coop.debug.kingdom.create"] = "Makes a new kingdom led by this hero's clan.",
        ["coop.debug.kingdom.declare_war"] = "Starts a war between two kingdoms or factions.",
        ["coop.debug.kingdom.make_peace"] = "Makes peace between two kingdoms or factions.",
        ["coop.debug.kingdom.force_ally"] = "Makes two kingdoms allies.",
        ["coop.debug.kingdom.force_trade_agreement"] = "Makes a trade agreement between two kingdoms.",
        ["coop.debug.kingdom.force_player_join_kingdom"] = "Moves a player's clan into a kingdom. The player must be online.",
        ["coop.debug.kingdom.force_player_vassalage"] = "Makes a player a vassal of a kingdom. The player must be online.",
        ["coop.debug.kingdom.decisions"] = "Lists the votes waiting in a kingdom and how players voted.",
        ["coop.debug.kingdom.list_policies"] = "Lists the policies a kingdom has now.",
        ["coop.debug.kingdom.resolve_decision"] = "Ends a kingdom vote now.",
        ["coop.debug.kingdom.remove_decision"] = "Removes a vote from a kingdom.",
        ["coop.debug.mobileparty.list"] = "Lists every party on the map with its id.",
        ["coop.debug.mobile_party.add_troops"] = "Adds 11 imperial troops to a hero's party.",
        ["coop.debug.mobile_party.add_troop_xp"] = "Adds XP to every troop in a hero's party.",
        ["coop.debug.army.list"] = "Lists every army with its id.",
        ["coop.debug.army.create"] = "Makes a new army led by a hero.",
        ["coop.debug.army.destroy"] = "Breaks up an army.",
        ["coop.debug.town.list_towns"] = "Lists every town with its id.",
        ["coop.debug.town.info"] = "Shows what the server stores about a town.",
        ["coop.debug.town.set_prosperity"] = "Sets a town's prosperity.",
        ["coop.debug.town.set_loyalty"] = "Sets a town's loyalty.",
        ["coop.debug.town.set_security"] = "Sets a town's security.",
        ["coop.debug.town.set_food_stocks"] = "Sets how much food a town has.",
        ["coop.debug.town.set_governor"] = "Makes a hero the governor of a town.",
        ["coop.debug.town.start_rebellion"] = "Starts a rebellion in a town.",
        ["coop.debug.town.list_items"] = "Lists every item id in the game.",
        ["coop.debug.village.list"] = "Lists every village with its id.",
        ["coop.debug.village.info"] = "Shows what the server stores about a village.",
        ["coop.debug.village.set_hearth"] = "Sets a village's hearth.",
        ["coop.debug.village.set_state"] = "Sets a village's state, for example normal or looted.",
        ["coop.debug.settlements.info"] = "Shows what the server stores about a town, castle or village.",
        ["coop.debug.settlements.capture_by_siege"] = "Gives a town or castle to a hero as if they took it in a siege.",
        ["coop.debug.settlements.set_owner_clan"] = "Gives a town, castle or village to this hero's clan.",
        ["coop.debug.settlements.set_militia"] = "Sets the militia of a settlement.",
        ["coop.debug.settlements.owner_state"] = "Shows who owns a settlement.",
        ["coop.debug.siege.list"] = "Lists the sieges going on now.",
        ["coop.debug.alley.list"] = "Lists the alleys of a town with their numbers.",
        ["coop.debug.alley.set_owner"] = "Gives an alley to a hero.",
        ["coop.debug.alley.abandon"] = "Makes the owner of an alley give it up.",
        ["coop.debug.save.save_as"] = "Saves the campaign under a new name.",
        ["coop.debug.save.force_autosave"] = "Makes the server autosave now.",
        ["coop.debug.save.state"] = "Shows whether the server is saving.",
        ["coop.debug.advance_time"] = "Moves campaign time forward by this many days.",
        ["coop.debug.set_time_mode"] = "Pauses the campaign or lets it run at normal or double speed.",
        ["coop.debug.get_time_mode"] = "Shows whether the campaign is paused or running.",
        ["coop.debug.crafting.add_crafted_items"] = "Puts one of every weapon crafted in this campaign into a hero's inventory.",
        ["coop.debug.campaign_options.list"] = "Lists the campaign options and their values.",
        ["coop.debug.companions.list_wanderers"] = "Lists the wanderers who can be hired as companions.",
    };

    private static readonly Dictionary<string, string> Objects = new(StringComparer.Ordinal)
    {
        ["hero"] = "a hero", ["hero_developer"] = "a hero", ["clan"] = "a clan", ["kingdom"] = "a kingdom",
        ["town"] = "a town", ["village"] = "a village", ["settlements"] = "a settlement", ["settlement_component"] = "a settlement",
        ["mobile_party"] = "a party", ["mobileparty"] = "a party", ["army"] = "an army", ["alley"] = "an alley",
        ["siege"] = "a siege", ["workshop"] = "a workshop", ["crafting"] = "a hero", ["player_captivity"] = "a hero",
        ["captivity"] = "a hero", ["companions"] = "a companion", ["romance"] = "a romance", ["map_event"] = "a battle",
        ["battle"] = "a battle", ["caravans"] = "a caravan", ["players"] = "a player", ["item_roster"] = "an item roster",
        ["inventory"] = "an inventory", ["location"] = "a location", ["save"] = "the save", ["campaign_options"] = "the campaign",
        ["tournament"] = "a tournament", ["arenas"] = "an arena", ["issues"] = "an issue", ["villager_party"] = "a villager party",
    };

    /// <summary>A plain sentence for the command: hand written, the site's own when it says something, or made from the name.</summary>
    public static string Plain(CoopCommandInfo command)
    {
        if (Plains.TryGetValue(command.Name, out var plain))
            return plain;
        var site = command.Description.Trim();
        if (site.Length > 0 && !Thin(site))
            return site;
        return FromName(command.Name);
    }

    private static bool Thin(string description) =>
        description.StartsWith("Runs ", StringComparison.Ordinal)
        || description.Contains("for co-op debugging", StringComparison.Ordinal)
        || description.StartsWith("Reports list", StringComparison.Ordinal)
        || Regex.IsMatch(description, @"^Reports [a-z ]+\.$") && description.Split(' ').Length <= 4;

    /// <summary>add_focus_points under hero_developer becomes "Adds focus points to a hero."</summary>
    public static string FromName(string name)
    {
        var parts = name.Split('.');
        var last = parts[^1];
        var prefix = parts.Length >= 3 ? parts[^2] : "";
        var thing = Objects.TryGetValue(prefix, out var o) ? o : "";
        var words = last.Split('_', StringSplitOptions.RemoveEmptyEntries).ToList();
        if (words.Count == 0)
            return "Runs this command.";
        string Rest(int skip) => string.Join(" ", words.Skip(skip));
        var noun = Regex.Replace(thing, "^(a|an|the) ", "");
        if (words.Count == 1 && words[0] == "state")
            return "Shows the state" + (thing.Length > 0 ? " of " + thing : "") + ".";
        if (words.Count == 1 && words[0] == "list")
            return noun.Length > 0 ? "Lists every " + noun + " with its id." : "Shows a list.";
        if (words[^1] == "state" && words.Count > 1)
            return "Shows the " + string.Join(" ", words.Take(words.Count - 1)) + " state" + (thing.Length > 0 ? " of " + thing : "") + ".";
        var verb = words[0];
        var rest = Rest(1);
        var (said, link) = verb switch
        {
            "add" => ("Adds", " to "),
            "set" => ("Sets", " of "),
            "remove" => ("Removes", " from "),
            "list" => ("Lists", " of "),
            "get" => ("Shows", " of "),
            "give" => ("Gives", " to "),
            "spawn" => ("Spawns", " for "),
            "create" => ("Creates", " for "),
            "destroy" => ("Destroys", " of "),
            "kill" => ("Kills", " of "),
            "start" => ("Starts", " for "),
            "stop" or "end" => ("Stops", " for "),
            "reset" => ("Resets", " of "),
            "clear" => ("Clears", " of "),
            "change" => ("Changes", " of "),
            "force" => ("Forces", " for "),
            "toggle" => ("Turns on or off", " for "),
            "teleport" => ("Teleports", " "),
            "refresh" => ("Refreshes", " of "),
            "request" => ("Asks for", " for "),
            "apply" => ("Applies", " to "),
            "open" => ("Opens", " for "),
            "close" => ("Closes", " for "),
            "info" => ("Shows information about", " "),
            "dump" or "field" => ("Shows every field", " of "),
            _ => ("", ""),
        };
        if (said.Length == 0)
            return Capital(Rest(0)) + (thing.Length > 0 ? " for " + thing : "") + ".";
        if (verb == "info")
            return "Shows information about " + (thing.Length > 0 ? thing : "this") + ".";
        if (rest.Length == 0)
            return said + " " + (thing.Length > 0 ? thing : "it") + ".";
        return said + " " + rest + (thing.Length > 0 && link.Trim().Length > 0 ? link + thing : "") + ".";
    }

    private static string Capital(string text) => text.Length == 0 ? text : char.ToUpperInvariant(text[0]) + text[1..];

    private static string Key(string name) => name.Replace("_", "").ToLowerInvariant();

    private static readonly HashSet<string> Flags = new(StringComparer.Ordinal)
    {
        "grantrewards", "ignoreinfluencecost", "declaremissingwars", "enabled", "evaluate", "exists", "force", "includeall",
        "inrebelliousstate", "instant", "isfinal", "isonland", "originalisonland", "policywasactive", "resolvemissing",
    };

    private static readonly Dictionary<string, (string What, string Example)> Numbers = new(StringComparer.Ordinal)
    {
        ["amount"] = ("How much to add. A minus number takes away where the command allows it.", "100"),
        ["count"] = ("How many.", "2"),
        ["number"] = ("How many troops.", "10"),
        ["numberofitems"] = ("How many items.", "10"),
        ["gold"] = ("The gold amount.", "10000"),
        ["renown"] = ("How much renown to add.", "100"),
        ["xp"] = ("The XP amount.", "500"),
        ["xpamount"] = ("The XP amount.", "1000"),
        ["pointcount"] = ("How many points.", "5"),
        ["value"] = ("The new value. For a yes or no setting use true or false.", "100"),
        ["power"] = ("How much power to add.", "10"),
        ["progress"] = ("The progress, a number.", "50"),
        ["hitpoints"] = ("The health.", "100"),
        ["woundedcount"] = ("How many of them are wounded.", "0"),
        ["age"] = ("The age in years.", "25"),
        ["days"] = ("How many days.", "1"),
        ["seconds"] = ("How many seconds.", "5"),
        ["milliseconds"] = ("How many milliseconds.", "1000"),
        ["evidenceholdmilliseconds"] = ("How long to wait in milliseconds before the autosave is checked.", "1000"),
        ["degrees"] = ("An angle in degrees.", "90"),
        ["distance"] = ("A distance on the map.", "5"),
        ["prosperity"] = ("The new prosperity.", "5000"),
        ["hearth"] = ("The new hearth.", "500"),
        ["loyalty"] = ("The new loyalty, from 0 to 100.", "80"),
        ["security"] = ("The new security, from 0 to 100.", "80"),
        ["militia"] = ("The new militia.", "100"),
        ["foodstocks"] = ("How much food.", "100"),
        ["tradetax"] = ("The trade tax that has built up.", "1000"),
        ["paylimit"] = ("The most gold a day the garrison may cost.", "500"),
        ["expectedplayercount"] = ("How many players you expect.", "2"),
        ["armypartycount"] = ("How many parties the army should have.", "3"),
        ["demandtime"] = ("The demand time, a number.", "1"),
        ["x"] = ("The x position on the map.", "100"),
        ["y"] = ("The y position on the map.", "100"),
        ["offsetx"] = ("How far to move along x.", "5"),
        ["offsety"] = ("How far to move along y.", "5"),
        ["originalx"] = ("The x position to go back to.", "100"),
        ["originaly"] = ("The y position to go back to.", "100"),
    };

    private const string ChoiceWhere = "Send the command without this value and the reply lists the words it takes.";

    private static readonly Dictionary<string, CoopParamHelp> Choices = new(StringComparer.Ordinal)
    {
        ["action"] = new("What the command should do, one of the words it takes, for example state.", "state", ChoiceWhere),
        ["mode"] = new("The mode, one of the words the command takes, for example on, off or status.", "status", ChoiceWhere),
        ["operation"] = new("The step to run, for example prepare, state or restore.", "state", ChoiceWhere),
        ["option"] = new("The option to pick.", "1", ChoiceWhere),
        ["outcome"] = new("The result to pick, for example success, or a vote number.", "success", ChoiceWhere),
        ["state"] = new("The state to set.", "state", ChoiceWhere),
        ["expectedstate"] = new("The screen state you expect.", "open", ChoiceWhere),
        ["siegestate"] = new("The siege state to set.", "0", ChoiceWhere),
        ["side"] = new("The battle side, Attacker or Defender.", "Attacker", "Attacker or Defender."),
        ["type"] = new("What kind of thing the next value is, Settlement, MobileParty or None.", "Settlement", "Settlement, MobileParty or None."),
        ["timemode"] = new("Pause, Play_1x or Play_2x.", "Pause", "Pause, Play_1x or Play_2x."),
        ["deathdetail"] = new("How the hero dies, old_age, battle or execution.", "old_age", "old_age, battle or execution."),
        ["armytype"] = new("The kind of army, for example Patrolling, Besieger, Raider or Defender.", "Patrolling", "Patrolling, Besieger, Raider or Defender."),
        ["disbandreason"] = new("Why the army breaks up, a game reason name or number.", "0", ChoiceWhere),
        ["supportweight"] = new("How strongly to vote, for example SlightlyFavor, StronglyFavor or FullyPush.", "StronglyFavor", "SlightlyFavor, StronglyFavor or FullyPush."),
        ["authority"] = new("Who should be in charge, as the command describes.", "server", ChoiceWhere),
        ["workshoptype"] = new("A workshop type id, for example brewery or pottery_shop.", "brewery", "Workshop type ids are in coop.debug.town.list_workshops for a town."),
        ["collection"] = new("The name of a kingdom list, for example Clans.", "Clans", ChoiceWhere),
        ["decisiontype"] = new("The kind of kingdom vote.", "DeclareWarDecision", "coop.debug.kingdom.add_decision_usage lists the kinds and their values."),
        ["decisionarg1"] = new("The first extra value the vote needs.", "empire", "coop.debug.kingdom.add_decision_usage shows what each kind of vote needs."),
        ["decisionarg2"] = new("The second extra value the vote needs.", "sturgia", "coop.debug.kingdom.add_decision_usage shows what each kind of vote needs."),
        ["decisionarg3"] = new("The third extra value the vote needs.", "true", "coop.debug.kingdom.add_decision_usage shows what each kind of vote needs."),
        ["decisionindex"] = new("The number of a waiting kingdom vote.", "1", "coop.debug.kingdom.list_kingdom_decisions <kingdom_id> lists the votes with their numbers."),
        ["policyid"] = new("A policy id, for example policy_feudal_inheritance.", "policy_feudal_inheritance", "coop.debug.kingdom.list_policies <kingdom_id> shows a kingdom's policies."),
        ["valueid"] = new("The id to add to the kingdom list, for example a clan id.", "clan_empire_north_1", "coop.debug.kingdom.collection_list <collection> <kingdom_id> shows what is in the list."),
        ["valueidorindex"] = new("The id or the number of the entry to remove.", "1", "coop.debug.kingdom.collection_list <collection> <kingdom_id> shows what is in the list."),
        ["issueid"] = new("The id of a quest a hero gives.", "issue_1", "coop.debug.hero.issues lists heroes with quests and coop.debug.hero.get_issue <hero_id> shows one."),
        ["questtypekey"] = new("The kind of quest.", "1", "coop.debug.issues.list_types lists the kinds.", IssueTypeList),
        ["objectid"] = new("The id of what the army should go for, or none to finish.", "town_ES1", "A settlement id like town_ES1 or a party id from coop.debug.mobileparty.list.", PartyList),
        ["menuid"] = new("The id of a game menu.", "town", "Menu ids come from the game, for example town or castle."),
        ["machineid"] = new("The id of a siege machine in a battle.", "1", "Read it from the siege machine dump of the battle."),
        ["mountagentid"] = new("The id of a mount in a battle.", "1", "Read it from the mount state command of the battle."),
        ["mapeventid"] = new("The id of a battle on the map.", "1", "coop.debug.siege.list and the map event state commands show battle ids.", SiegeList),
        ["besiegercampid"] = new("The id of a siege camp.", "1", "coop.debug.siege.list shows the sieges.", SiegeList),
        ["strategyid"] = new("The id of a siege strategy.", "1", ChoiceWhere),
        ["skillname"] = new("A skill name, for example OneHanded, TwoHanded, Polearm, Bow, Crossbow, Throwing, Riding, Athletics, Crafting, Scouting, Tactics, Roguery, Charm, Leadership, Trade, Steward, Medicine or Engineering.", "OneHanded", "Use one of the names above. Crafting is smithing."),
        ["alleyindex"] = new("The alley's number in the town, starting at 0.", "0", "coop.debug.alley.list <settlement_id> lists a town's alleys."),
        ["upgradetargetindex"] = new("Which upgrade to pick, starting at 0.", "0", "0 is the first upgrade the troop has."),
        ["nameprefix"] = new("The start of a name. Leave it out to list everyone.", "Lu", "Any letters a name starts with."),
        ["newcustomname"] = new("The new name. Put quotes around a name with spaces.", "My Party", "Any name you like."),
        ["savename"] = new("The name for the save file.", "coop_backup", "Any name with no spaces."),
        ["filename"] = new("The file name.", "coop_dump", "Any name with no spaces."),
        ["player"] = new("A player's controller id, or their hero name in quotes.", "<SteamID64>", "coop.debug.players.list shows each player's controller id.", PlayerList),
        ["newownerid"] = new("The id of the hero who gets it.", "lord_1_1", "coop.debug.hero.list lists heroes with their ids.", HeroList),
        ["parent"] = new("The hero who is the parent of the heirs, an id or exact name. Leave it out to use the clan leader.", "lord_1_1", "coop.debug.hero.list shows hero ids.", HeroList),
        ["characterid"] = new("A troop or character template id, for example imperial_recruit.", "imperial_recruit", "coop.debug.character_objects.list lists them.", CharacterList),
        ["characterobjectid"] = new("A troop or character template id, for example imperial_recruit.", "imperial_recruit", "coop.debug.character_objects.list lists them.", CharacterList),
        ["locationid"] = new("A location in a settlement, for example tavern, lordshall, arena or prison.", "tavern", "coop.debug.location.list lists the locations.", LocationList),
        ["banneritemid"] = new("A banner item id.", "banner_item_1", "coop.debug.hero.list_banneritems lists them.", BannerItemList),
        ["armyid"] = new("An army id.", "army_1", "coop.debug.army.list shows each army with its StringId.", ArmyList),
        ["settlementcomponentid"] = new("The id of a town or castle part of a settlement, like town_comp_ES3.", "town_comp_ES3", "coop.debug.town.list_towns shows town ids like town_comp_ES3.", TownList),
    };

    /// <summary>The help of one parameter, or null when no rule knows it (only a generic line is left).</summary>
    public static CoopParamHelp? Known(string name, string? heroName = null, string? heroId = null, string? controllerId = null)
    {
        var key = Key(name);
        var hero = string.IsNullOrWhiteSpace(heroName) ? "Lucon" : heroName!;
        var heroIdExample = string.IsNullOrWhiteSpace(heroId) ? "lord_1_1" : heroId!;
        if (Choices.TryGetValue(key, out var choice))
            return key == "player" && !string.IsNullOrWhiteSpace(controllerId) ? choice with { Example = controllerId! } : choice;
        if (Flags.Contains(key))
            return new("true or false.", key == "force" || key == "instant" ? "false" : "true", "Type true or false.");
        if (Numbers.TryGetValue(key, out var number))
            return new(number.What, number.Example, "Type a whole number.");

        // Names and ids, matched on what the parameter is called.
        if (key is "heroname" or "leaderheroname" or "captorhero" or "prisonerhero")
            return new("A hero's name exactly as the game shows it. Put quotes around a name with spaces.", hero,
                "coop.debug.hero.list shows every hero with its name. Player heroes are on the Cheats page cards.", HeroList);
        if (key.Contains("heronameorid") || key == "heronameorpartyid")
            return new("A hero's exact name or their id. An id is safer when two heroes share a name.", hero,
                "coop.debug.hero.list shows names and ids. coop.debug.players.list shows player hero ids.", HeroList);
        if (key.Contains("controller"))
            return new("A player's controller id, which is their SteamID64.", string.IsNullOrWhiteSpace(controllerId) ? "<SteamID64>" : controllerId!,
                "coop.debug.players.list shows each player's ControllerId.", PlayerList);
        if (key.Contains("hero"))
            return new("A hero id, like lord_1_1. Player heroes have their own ids.", heroIdExample,
                "coop.debug.hero.list lists heroes with their ids. coop.debug.players.list shows player hero ids.", HeroList);
        if (key.Contains("clan"))
            return key.Contains("party")
                ? new("The id of a clan party.", "lord_6_5_party_1", "coop.debug.mobileparty.list lists parties with their ids.", PartyList)
                : new("A clan id, like clan_empire_north_1." + (key.Contains("name") ? " A clan name works too." : ""), "clan_empire_north_1",
                    "coop.debug.clan.list lists clans with their ids. coop.debug.players.list shows player clan ids.", ClanList);
        if (key.Contains("kingdomname"))
            return new("The new kingdom's name. Put quotes around a name with spaces.", "Kingdom of the West", "Any name you like.");
        if (key.Contains("kingdom") || key.Contains("faction"))
            return new("A kingdom id, like empire, empire_w, empire_s, sturgia, vlandia, battania, aserai or khuzait.", key.EndsWith("2id", StringComparison.Ordinal) ? "sturgia" : "empire",
                "coop.debug.kingdom.list lists kingdoms with their ids.", KingdomList);
        if (key.Contains("village"))
            return new("A village id.", "castle_village_comp_K7_2", "coop.debug.village.list lists villages with their ids.", VillageList);
        if (key is "townname")
            return new("A town's name exactly as the game shows it.", "Danustica", "coop.debug.town.list_towns lists towns with their names.", TownList);
        if (key.Contains("town"))
            return new("A town id with comp_ in it, like town_comp_V1. An id like town_V1 is not found." + (key.Contains("name") ? " A town name works too." : ""), "town_comp_V1",
                "coop.debug.town.list_towns lists towns with their ids.", TownList);
        if (key.Contains("castle"))
            return new("A castle id, like castle_A1.", "castle_A1",
                "Castle ids look like castle_A1. coop.debug.settlements.info <settlementId> shows one.");
        if (key.Contains("settlement") && !key.Contains("party"))
            return new("A settlement id. Towns look like town_ES1, castles castle_A1." + (key.Contains("name") ? " A name works too." : ""), "town_ES1",
                "coop.debug.town.list_towns shows town ids like town_comp_ES1. Drop the comp_ part for the settlement id.", TownList);
        if (key.Contains("party"))
            return new("A party id, like lord_6_5_party_1.", "lord_6_5_party_1",
                "coop.debug.mobileparty.list lists parties with their ids. coop.debug.players.list shows player party ids.", PartyList);
        if (key.Contains("item"))
            return new("An item id, like grain.", "grain", "coop.debug.town.list_items lists every item id.", ItemList);
        return null;
    }

    /// <summary>The help of one parameter, with a plain fallback for a name no rule knows.</summary>
    public static CoopParamHelp For(string name, string? heroName = null, string? heroId = null, string? controllerId = null) =>
        Known(name, heroName, heroId, controllerId)
        ?? new("The " + name.Replace('_', ' ') + " this command needs.", "1", ChoiceWhere);

    /// <summary>The whole guide: the plain text, each parameter's help, the example values and line, and the list commands to find ids.</summary>
    public static CoopCommandGuide Guide(CoopCommandInfo command, string? heroName = null, string? heroId = null, string? controllerId = null)
    {
        var parameters = command.Parameters.Select(p => (p, For(p.Name, heroName, heroId, controllerId))).ToList();
        var values = parameters.Select(p => p.p.Optional ? "" : p.Item2.Example).ToList();
        var line = CoopCommandCatalog.Build(command, values);
        var find = FindCommands(command);
        return new CoopCommandGuide(Plain(command), parameters, values, line, find);
    }

    /// <summary>The list commands (SERVER or BOTH, no values needed) that show the ids this command's parameters need.</summary>
    public static IReadOnlyList<string> FindCommands(CoopCommandInfo command, IReadOnlyList<CoopCommandInfo>? catalog = null)
    {
        var list = catalog ?? CoopCommandCatalog.All;
        return command.Parameters
            .Select(p => Known(p.Name)?.FindCommand)
            .Where(c => c != null)
            .Select(c => c!)
            .Distinct(StringComparer.Ordinal)
            .Where(c => list.FirstOrDefault(x => x.Name == c) is { IsClientOnly: false } info && info.Parameters.All(p => p.Optional))
            .ToList();
    }
}
