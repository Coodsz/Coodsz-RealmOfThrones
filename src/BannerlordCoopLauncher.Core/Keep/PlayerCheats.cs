using System.Globalization;
using System.Text.RegularExpressions;

namespace BannerlordCoopLauncher.Core.Keep;

/// <summary>The category chips of the Cheats page.</summary>
public enum CheatCategory { Gold, Clan, Skills, Party, Hero, World }

/// <summary>What one box of a cheat's form holds.</summary>
public enum CheatInputKind { Number, Skill, Kingdom, Town, Village, Hero, Toggle }

/// <summary>One box of a cheat's form: a number with preset chips, or a dropdown, or a tick box.</summary>
public sealed record CheatInput(string Key, string Label, CheatInputKind Kind, int Default = 0, int Min = 0, int Max = 0,
    IReadOnlyList<int>? Presets = null);

/// <summary>The player (or nobody, for World cheats) a cheat is sent for, with the ids the server knows.</summary>
public sealed record CheatTarget(string Name, string HeroId = "", string ClanId = "", string PartyId = "", string ControllerId = "",
    int? Gold = null, double? Age = null, bool Online = true, int? HitPoints = null, int? MaxHitPoints = null, int? ClanTier = null);

/// <summary>A built cheat: the line to send and, when there is one, the line that puts it back.</summary>
public sealed record CheatBuild(string Command, string? Undo, string Summary, string? Note = null);

/// <summary>
/// One cheat of the Cheats page. Most send a Coop SERVER command; <paramref name="Tools"/> ones send a
/// coodsz.* command of the Coodsz Server Tools module, which answers with the value it read back.
/// </summary>
public sealed record PlayerCheat(string Id, string Title, CheatCategory Category, string CoopCommand, string Help,
    IReadOnlyList<CheatInput> Inputs, bool NotAPlayer = false, bool CannotUndo = false, bool OnlineOnly = false,
    bool NeedsGold = false, bool Tools = false);

/// <summary>
/// The player cheats of Coodsz Host and how each one becomes a Coop command line. The page only
/// shows them and sends what <see cref="Build"/> makes.
/// </summary>
public static class PlayerCheats
{
    public const int MaxGold = 100_000_000;

    /// <summary>The lowest clan tier Coop takes as a vassal (the game's ClanTierModel.VassalEligibleTier).</summary>
    public const int VassalTier = 2;

    /// <summary>Shown after Join a kingdom when the clan tier is not known (a server without Coodsz Server Tools).</summary>
    public const string VassalTierNote = "Coop still turns it down if the clan tier is below 2, and then only says so in the server log.";

    /// <summary>The shown name and the DefaultSkills name Coop looks up (case does not matter).</summary>
    public static readonly IReadOnlyList<(string Id, string Name)> Skills = new[]
    {
        ("OneHanded", "One handed"), ("TwoHanded", "Two handed"), ("Polearm", "Polearm"), ("Bow", "Bow"),
        ("Crossbow", "Crossbow"), ("Throwing", "Throwing"), ("Riding", "Riding"), ("Athletics", "Athletics"),
        ("Crafting", "Smithing"), ("Scouting", "Scouting"), ("Tactics", "Tactics"), ("Roguery", "Roguery"),
        ("Charm", "Charm"), ("Leadership", "Leadership"), ("Trade", "Trade"), ("Steward", "Steward"),
        ("Medicine", "Medicine"), ("Engineering", "Engineering"),
    };

    private static CheatInput Amount(string label, int value, int min, int max, params int[] presets) =>
        new("amount", label, CheatInputKind.Number, value, min, max, presets);

    public static readonly IReadOnlyList<PlayerCheat> All = new[]
    {
        new PlayerCheat("gold.give", "Give gold", CheatCategory.Gold, "coop.debug.hero.set_gold",
            "Adds gold to what the player has now. Coop sets the new total on every hero with this exact name.",
            new[] { Amount("Gold to give", 10_000, 1, MaxGold, 1_000, 10_000, 50_000, 100_000) }, NeedsGold: true),
        new PlayerCheat("gold.take", "Take gold", CheatCategory.Gold, "coop.debug.hero.set_gold",
            "Takes gold away. Gold never goes below 0.",
            new[] { Amount("Gold to take", 1_000, 1, MaxGold, 1_000, 10_000, 50_000, 100_000) }, NeedsGold: true),
        new PlayerCheat("gold.set", "Set gold", CheatCategory.Gold, "coop.debug.hero.set_gold",
            "Sets the gold to exactly this amount. Coop sets it on every hero with this exact name.",
            new[] { Amount("Gold", 10_000, 0, MaxGold, 0, 10_000, 100_000, 1_000_000) }),

        new PlayerCheat("clan.influence", "Give influence", CheatCategory.Clan, "coop.debug.clan.add_influence",
            "Adds influence to the player's clan. A minus number takes it away.",
            new[] { Amount("Influence", 100, -10_000, 10_000, 50, 100, 500, 1_000) }),
        new PlayerCheat("clan.renown", "Give renown", CheatCategory.Clan, "coop.debug.clan.add_renown",
            "Adds renown to the player's clan. The clan tier follows the renown.",
            new[] { Amount("Renown", 100, 1, 100_000, 50, 100, 500, 1_000) }, CannotUndo: true),
        new PlayerCheat("clan.vassal", "Join a kingdom", CheatCategory.Clan, "coop.debug.kingdom.force_player_vassalage",
            "Makes the player a vassal of the kingdom you pick.",
            new[]
            {
                new CheatInput("kingdom", "Kingdom", CheatInputKind.Kingdom),
                new CheatInput("rewards", "Give the vassal rewards", CheatInputKind.Toggle, Default: 1),
            }, CannotUndo: true, OnlineOnly: true),
        new PlayerCheat("clan.renown.take", "Take renown", CheatCategory.Clan, "coodsz.renown",
            "Takes renown from the player's clan. The server tools read the renown back after the change.",
            new[] { Amount("Renown", 100, 1, ToolsCommand.MaxRenown, 50, 100, 500, 1_000) }, Tools: true),
        new PlayerCheat("clan.tier", "Set clan tier", CheatCategory.Clan, "coodsz.clan_tier",
            "Sets the tier of the player's clan.",
            new[] { new CheatInput("amount", "Tier", CheatInputKind.Number, 1, 0, 6, new[] { 1, 2, 3, 4, 5, 6 }) }, Tools: true),

        new PlayerCheat("skills.xp", "Give skill XP", CheatCategory.Skills, "coop.debug.hero_developer.add_skill_xp",
            "Adds XP to one skill of the player's hero.",
            new[]
            {
                new CheatInput("skill", "Skill", CheatInputKind.Skill),
                Amount("XP", 1_000, 1, 1_000_000, 500, 1_000, 5_000, 20_000),
            }, CannotUndo: true),
        new PlayerCheat("skills.attribute", "Give attribute points", CheatCategory.Skills, "coop.debug.hero_developer.add_attribute_points",
            "Adds unspent attribute points the player can spend in the character screen.",
            new[] { Amount("Points", 1, 1, 100, 1, 2, 5, 10) }),
        new PlayerCheat("skills.focus", "Give focus points", CheatCategory.Skills, "coop.debug.hero_developer.add_focus_points",
            "Adds unspent focus points the player can spend in the character screen.",
            new[] { Amount("Points", 1, 1, 100, 1, 2, 5, 10) }),
        new PlayerCheat("skills.reset", "Reset skills", CheatCategory.Skills, "coop.debug.hero_developer.reset_skills",
            "Resets the hero's skills, attributes and focus. This can't be undone.",
            Array.Empty<CheatInput>(), CannotUndo: true),

        new PlayerCheat("party.troops", "Give troops", CheatCategory.Party, "coop.debug.mobile_party.add_troops",
            "Adds 11 imperial troops to the player's party (5 vigla recruits, 2 recruits, 2 equites, 2 heavy horsemen).",
            Array.Empty<CheatInput>(), CannotUndo: true),
        new PlayerCheat("party.troopxp", "Give troop XP", CheatCategory.Party, "coop.debug.mobile_party.add_troop_xp",
            "Adds XP to every troop in the player's party.",
            new[] { Amount("XP per troop", 500, 1, 1_000_000, 100, 500, 1_000, 5_000) }, CannotUndo: true),
        new PlayerCheat("party.crafted", "Give crafted items", CheatCategory.Party, "coop.debug.crafting.add_crafted_items",
            "Puts one of every weapon crafted in this campaign into the player's inventory.",
            Array.Empty<CheatInput>(), CannotUndo: true),
        new PlayerCheat("party.hideout", "Teleport to a hideout", CheatCategory.Party, "coop.debug.hideout.teleport_party",
            "Moves the player's party to the nearest bandit hideout and reveals it. The party must be on the map, not in a town, battle or army.",
            Array.Empty<CheatInput>(), CannotUndo: true, OnlineOnly: true),
        new PlayerCheat("party.army", "Give or take troops", CheatCategory.Party, "coodsz.troops",
            "Pick any troop kinds to give, or troops from the party to take. The server confirms each kind.",
            Array.Empty<CheatInput>(), Tools: true),
        new PlayerCheat("party.heal", "Heal wounded troops", CheatCategory.Party, "coodsz.heal_troops",
            "Heals every wounded troop in the player's party.",
            Array.Empty<CheatInput>(), CannotUndo: true, Tools: true),
        new PlayerCheat("party.items", "Give or take items", CheatCategory.Party, "coodsz.give_item",
            "Give any item in the game, take items back, or give their value as gold. Each item is confirmed by the server.",
            Array.Empty<CheatInput>(), Tools: true),

        new PlayerCheat("hero.age", "Set age", CheatCategory.Hero, "coop.debug.hero.set_age",
            "Sets the hero's age in years.",
            new[] { Amount("Age", 25, 18, 90, 20, 25, 30, 40) }),
        new PlayerCheat("hero.relation", "Set relation", CheatCategory.Hero, "coop.debug.hero.set_relation",
            "Sets how much the player's hero and another hero like each other, from minus 100 to 100.",
            new[]
            {
                new CheatInput("hero", "Other hero", CheatInputKind.Hero),
                new CheatInput("amount", "Relation", CheatInputKind.Number, 50, -100, 100, new[] { -100, 0, 50, 100 }),
            }, CannotUndo: true),
        new PlayerCheat("hero.heal", "Heal to full", CheatCategory.Hero, "coodsz.health",
            "Heals the hero to their full health.", Array.Empty<CheatInput>(), Tools: true),
        new PlayerCheat("hero.health.give", "Give health", CheatCategory.Hero, "coodsz.health",
            "Adds this much health, up to the hero's maximum.",
            new[] { Amount("Health", 50, 1, 10_000, 10, 25, 50, 100) }, Tools: true),
        new PlayerCheat("hero.health.take", "Take health", CheatCategory.Hero, "coodsz.health",
            "Takes this much health. Health never goes below 1.",
            new[] { Amount("Health", 50, 1, 10_000, 10, 25, 50, 100) }, Tools: true),
        new PlayerCheat("hero.health.set", "Set health", CheatCategory.Hero, "coodsz.health",
            "Sets the health to exactly this amount.",
            new[] { Amount("Health", 100, 1, 10_000, 25, 50, 100, 150) }, Tools: true),

        new PlayerCheat("world.prosperity", "Set town prosperity", CheatCategory.World, "coop.debug.town.set_prosperity",
            "Sets the prosperity of a town.",
            new[]
            {
                new CheatInput("town", "Town", CheatInputKind.Town),
                Amount("Prosperity", 5_000, 0, 100_000, 1_000, 3_000, 5_000, 10_000),
            }, NotAPlayer: true, CannotUndo: true),
        new PlayerCheat("world.hearth", "Set village hearth", CheatCategory.World, "coop.debug.village.set_hearth",
            "Sets the hearth of a village.",
            new[]
            {
                new CheatInput("village", "Village", CheatInputKind.Village),
                Amount("Hearth", 500, 0, 10_000, 100, 300, 500, 1_000),
            }, NotAPlayer: true, CannotUndo: true),
    };

    public static PlayerCheat? Find(string id) => All.FirstOrDefault(c => c.Id == id);

    /// <summary>
    /// The cheats of one category; Give and Take gold only when the player's gold is known. With
    /// <paramref name="tools"/> false (a Cheats page that stays on coop.debug: the AD1259 tool, whose server
    /// loads Coodsz Server Tools only for the host rules and settings sync, and the TAOM and Realm of Thrones
    /// tools) the coodsz.* cheats are left out.
    /// </summary>
    public static IReadOnlyList<PlayerCheat> In(CheatCategory category, CheatTarget? target, bool tools = true) =>
        All.Where(c => c.Category == category && Works(c, target, tools)).ToList();

    /// <summary>The favourite cheats (by id, in the order of <see cref="All"/>) that work for this player and server.</summary>
    public static IReadOnlyList<PlayerCheat> Favourites(IEnumerable<string> ids, CheatTarget? target, bool tools = true)
    {
        var set = new HashSet<string>(ids, StringComparer.Ordinal);
        return All.Where(c => set.Contains(c.Id) && Works(c, target, tools)).ToList();
    }

    /// <summary>
    /// Whether the cheat can be shown: a coodsz.* cheat needs the Coodsz Server Tools module, and Give
    /// and Take gold need the player's gold.
    /// </summary>
    public static bool Works(PlayerCheat cheat, CheatTarget? target, bool tools = true) =>
        (tools || !cheat.Tools) && (!cheat.NeedsGold || target?.Gold != null);

    /// <summary>The cheat categories that have at least one cheat on a server with or without the tools module.</summary>
    public static IReadOnlyList<CheatCategory> Categories(bool tools = true) =>
        Enum.GetValues<CheatCategory>().Where(category => All.Any(c => c.Category == category && (tools || !c.Tools))).ToList();

    /// <summary>The note shown for an offline player, or null when the cheat works offline too.</summary>
    public static string? OfflineNote(PlayerCheat cheat, CheatTarget? target) =>
        cheat.OnlineOnly && target is { Online: false }
            ? "This only works while the player is online. Coop needs their game connected for it."
            : null;

    /// <summary>The gold after Give gold: the current gold plus the amount, at most <see cref="MaxGold"/>.</summary>
    public static int GoldAfterGive(int current, int amount) => (int)Math.Clamp((long)current + amount, 0, MaxGold);

    /// <summary>The gold after Take gold: never below 0.</summary>
    public static int GoldAfterTake(int current, int amount) => (int)Math.Clamp((long)current - amount, 0, MaxGold);

    /// <summary>A whole number as typed (spaces, commas and dots between thousands are allowed).</summary>
    public static bool TryNumber(string? text, out int value)
    {
        var clean = (text ?? "").Trim().Replace(" ", "").Replace(",", "").Replace(".", "").Replace(" ", "");
        return int.TryParse(clean, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out value);
    }

    /// <summary>
    /// Builds the command for this cheat, player and form values. Throws <see cref="ArgumentException"/>
    /// with the line to show when a value is missing or out of range, or an id is not known yet.
    /// </summary>
    public static CheatBuild Build(PlayerCheat cheat, CheatTarget? target, IReadOnlyDictionary<string, string> values)
    {
        if (!cheat.NotAPlayer && (target == null || target.Name.Trim().Length == 0))
            throw new ArgumentException("Pick a player first.");

        int Number(string key)
        {
            var input = cheat.Inputs.First(i => i.Key == key);
            if (!values.TryGetValue(key, out var text) || !TryNumber(text, out var n))
                throw new ArgumentException("Type a whole number for " + input.Label.ToLowerInvariant() + ".");
            if (n < input.Min || n > input.Max)
                throw new ArgumentException(input.Label + " must be from " + N(input.Min) + " to " + N(input.Max) + ".");
            return n;
        }

        string Pick(string key, string what)
        {
            if (!values.TryGetValue(key, out var id) || string.IsNullOrWhiteSpace(id))
                throw new ArgumentException("Pick " + what + " first.");
            return id.Trim();
        }

        string Need(string id, string what) =>
            id.Trim().Length > 0 ? id.Trim() : throw new ArgumentException("The server has not said which " + what + " is " + target!.Name + "'s yet. Press refresh above and try again.");

        var name = target?.Name.Trim() ?? "";
        var hero = target == null ? "" : target.HeroId.Trim().Length > 0 ? target.HeroId.Trim() : name;
        var q = CoopArg.Quote(name);
        var c = cheat.CoopCommand;
        switch (cheat.Id)
        {
            case "gold.give":
            case "gold.take":
            {
                if (target!.Gold is not int gold)
                    throw new ArgumentException("The player's gold is not known yet, so use Set gold.");
                var amount = Number("amount");
                var after = cheat.Id == "gold.give" ? GoldAfterGive(gold, amount) : GoldAfterTake(gold, amount);
                return new CheatBuild(c + " " + q + " " + N0(after), c + " " + q + " " + N0(gold),
                    (cheat.Id == "gold.give" ? "gave " : "took ") + N(Math.Abs(after - gold)) + " gold (" + N(gold) + " to " + N(after) + ")");
            }
            case "gold.set":
            {
                var gold = Number("amount");
                var undo = target!.Gold is int old ? c + " " + q + " " + N0(old) : null;
                return new CheatBuild(c + " " + q + " " + N0(gold), undo, "set gold to " + N(gold));
            }
            case "clan.influence":
            {
                var amount = Number("amount");
                if (amount == 0)
                    throw new ArgumentException("Type a number that is not 0.");
                var clan = Need(target!.ClanId, "clan");
                return new CheatBuild(c + " " + clan + " " + N0(amount), c + " " + clan + " " + N0(-amount), "influence " + Signed(amount));
            }
            case "clan.renown":
            {
                var amount = Number("amount");
                var clan = Need(target!.ClanId, "clan");
                return new CheatBuild(c + " " + clan + " " + N0(amount), null, "renown " + Signed(amount));
            }
            case "clan.vassal":
            {
                var kingdom = Pick("kingdom", "a kingdom");
                var controller = Need(target!.ControllerId, "controller id");
                // Coop answers "Queued vassalage" first and turns the request down a moment later, only in
                // its log, when the clan tier is too low. So a tier that is known to be too low is refused here.
                if (target.ClanTier is int tier && tier < VassalTier)
                    throw new ArgumentException("Coop only takes a clan of tier " + VassalTier + " or higher as a vassal, and " + target.Name.Trim()
                        + "'s clan is tier " + tier + ". Use Set clan tier first, then join the kingdom.");
                var rewards = !values.TryGetValue("rewards", out var r) || r != "0";
                return new CheatBuild(c + " " + controller + " " + CoopArg.Quote(kingdom) + " " + (rewards ? "true" : "false"), null, "asked to join kingdom " + kingdom,
                    target.ClanTier == null ? VassalTierNote : null);
            }
            case "skills.xp":
            {
                var skill = Pick("skill", "a skill");
                if (!Skills.Any(s => s.Id == skill))
                    throw new ArgumentException("Pick a skill from the list.");
                var xp = Number("amount");
                return new CheatBuild(c + " " + CoopArg.Quote(hero) + " " + skill + " " + N0(xp), null, SkillName(skill) + " XP +" + N(xp));
            }
            case "skills.attribute":
            case "skills.focus":
            {
                var points = Number("amount");
                var what = cheat.Id == "skills.attribute" ? "attribute points +" : "focus points +";
                return new CheatBuild(c + " " + CoopArg.Quote(hero) + " " + N0(points), c + " " + CoopArg.Quote(hero) + " " + N0(-points), what + points);
            }
            case "skills.reset":
                return new CheatBuild(c + " " + CoopArg.Quote(hero), null, "reset the skills");
            case "party.troops":
                return new CheatBuild(c + " " + q, null, "gave 11 troops");
            case "party.troopxp":
            {
                var xp = Number("amount");
                return new CheatBuild(c + " " + q + " " + N0(xp), null, "troop XP +" + N(xp));
            }
            case "party.crafted":
                return new CheatBuild(c + " " + q, null, "gave the crafted items");
            case "party.hideout":
                return new CheatBuild(c + " " + Need(target!.PartyId, "party"), null, "teleported to a hideout");
            case "hero.age":
            {
                var age = Number("amount");
                var undo = target!.Age is double old && old > 0 ? c + " " + CoopArg.Quote(hero) + " " + N0((int)Math.Round(old)) : null;
                return new CheatBuild(c + " " + CoopArg.Quote(hero) + " " + N0(age), undo, "age set to " + age);
            }
            case "hero.relation":
            {
                var other = Pick("hero", "the other hero");
                var self = Need(target!.HeroId, "hero");
                if (string.Equals(other, self, StringComparison.Ordinal))
                    throw new ArgumentException("Pick a hero other than the player.");
                var value = Number("amount");
                return new CheatBuild(c + " " + self + " " + CoopArg.Quote(other) + " " + N0(value), null, "relation with " + other + " set to " + value);
            }
            case "world.prosperity":
            {
                var town = Pick("town", "a town");
                var value = Number("amount");
                return new CheatBuild(c + " " + CoopArg.Quote(town) + " " + N0(value), null, "prosperity of " + town + " set to " + N(value));
            }
            case "world.hearth":
            {
                var village = Pick("village", "a village");
                var value = Number("amount");
                return new CheatBuild(c + " " + CoopArg.Quote(village) + " " + N0(value), null, "hearth of " + village + " set to " + N(value));
            }

            // The Coodsz Server Tools commands: the undo comes from the value the module reads back (ToolsUndo).
            case "clan.renown.take":
            {
                var amount = Number("amount");
                return new CheatBuild(ToolsCommand.Renown(Need(target!.HeroId, "hero"), -amount), null, "renown -" + N(amount));
            }
            case "clan.tier":
            {
                var tier = Number("amount");
                return new CheatBuild(ToolsCommand.ClanTier(Need(target!.HeroId, "hero"), tier), null, "clan tier set to " + tier);
            }
            case "hero.heal":
            {
                var max = target!.MaxHitPoints ?? 100;
                if (target.HitPoints is int hp && hp >= max)
                    throw new ArgumentException(target.Name + " is already at full health.");
                return new CheatBuild(ToolsCommand.Health(Need(target.HeroId, "hero"), max), null, "healed to full");
            }
            case "hero.health.give":
            {
                var amount = Number("amount");
                var hp = Math.Min((target!.HitPoints ?? 0) + amount, target.MaxHitPoints ?? 100);
                return new CheatBuild(ToolsCommand.Health(Need(target.HeroId, "hero"), hp), null, "health +" + amount);
            }
            case "hero.health.take":
            {
                var amount = Number("amount");
                var hp = Math.Max((target!.HitPoints ?? 1) - amount, 1);
                return new CheatBuild(ToolsCommand.Health(Need(target.HeroId, "hero"), hp), null, "health -" + amount);
            }
            case "hero.health.set":
            {
                var hp = Number("amount");
                return new CheatBuild(ToolsCommand.Health(Need(target!.HeroId, "hero"), hp), null, "health set to " + hp);
            }
            case "party.heal":
                return new CheatBuild(ToolsCommand.HealTroops(Need(target!.HeroId, "hero")), null, "healed the wounded troops");
            case "party.items":
                return new CheatBuild(ToolsCommand.GiveItem(Need(target!.HeroId, "hero"), "item_id", 1), null, "items");
            case "party.army":
                throw new ArgumentException("Click troops to add them, then press Run.");
        }

        throw new ArgumentException("This cheat is not known.");
    }

    /// <summary>
    /// The undo of a Coodsz Server Tools cheat from the old and new value the module read back, as the
    /// earlier Cheats page did it; null when nothing changed or the cheat has no undo.
    /// </summary>
    public static string? ToolsUndo(string cheatId, ToolsChange change)
    {
        if (!change.Changed)
            return null;
        return cheatId switch
        {
            "clan.renown.take" => ToolsCommand.Renown(change.HeroId, change.Old - change.New),
            "clan.tier" => ToolsCommand.ClanTier(change.HeroId, change.Old),
            "hero.heal" or "hero.health.give" or "hero.health.take" or "hero.health.set" => ToolsCommand.Health(change.HeroId, change.Old),
            _ => null,
        };
    }

    public static string SkillName(string id) => Skills.FirstOrDefault(s => s.Id == id).Name ?? id;

    private static string N0(int value) => value.ToString(CultureInfo.InvariantCulture);
    private static string N(int value) => value.ToString("N0", CultureInfo.InvariantCulture);
    private static string Signed(int value) => (value > 0 ? "+" : "") + N(value);
}

/// <summary>A registered Coop player from coop.debug.players.list.</summary>
public sealed record CoopPlayerRow(string Name, string ControllerId, string HeroId, string PartyId, string ClanId);

/// <summary>An id and a name from a Coop list command (kingdoms, towns, villages, heroes).</summary>
public sealed record CoopListItem(string Id, string Name);

/// <summary>A player card of a Cheats page without Coodsz Server Tools: the name, online or not, and what Coop knows.</summary>
public sealed record CheatPlayer(string Name, bool Online, CoopPlayerRow? Coop)
{
    public string HeroId => Coop?.HeroId ?? "";
}

/// <summary>
/// The players of a Cheats page that only has Coop's own commands (the TAOM server tool): the
/// players the host lists as connected, then the players coop.debug.players.list knows that the
/// host did not list (shown as not online, like Coodsz Host's page does).
/// </summary>
public static class CheatRoster
{
    public static IReadOnlyList<CheatPlayer> Merge(IEnumerable<string> online, IEnumerable<CoopPlayerRow> coop)
    {
        var rows = coop.Where(r => r.Name.Trim().Length > 0).ToList();
        var list = new List<CheatPlayer>();
        foreach (var name in online.Select(n => (n ?? "").Trim()).Where(n => n.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase))
            list.Add(new CheatPlayer(name, true, rows.FirstOrDefault(r => string.Equals(r.Name.Trim(), name, StringComparison.OrdinalIgnoreCase))));
        foreach (var row in rows)
        {
            if (list.Any(p => string.Equals(p.Name, row.Name.Trim(), StringComparison.OrdinalIgnoreCase)
                              || row.HeroId.Length > 0 && p.HeroId == row.HeroId))
                continue;
            list.Add(new CheatPlayer(row.Name.Trim(), false, row));
        }

        return list;
    }

    /// <summary>The target a cheat is built for: the ids come from coop.debug.players.list; gold and age are not known.</summary>
    public static CheatTarget Target(CheatPlayer player) =>
        new(player.Coop?.Name.Trim() is { Length: > 0 } coopName ? coopName : player.Name,
            player.Coop?.HeroId ?? "", player.Coop?.ClanId ?? "", player.Coop?.PartyId ?? "", player.Coop?.ControllerId ?? "",
            Online: player.Online);
}

/// <summary>Reads the replies of the Coop inspect commands the Cheats page fills its lists from.</summary>
public static class CoopReplies
{
    public const string PlayersCommand = "coop.debug.players.list";
    public const string KingdomsCommand = "coop.debug.kingdom.list";
    public const string TownsCommand = "coop.debug.town.list_towns";
    public const string VillagesCommand = "coop.debug.village.list";
    public const string HeroesCommand = "coop.debug.hero.list";

    private static IEnumerable<string> Lines(IEnumerable<string> reply) =>
        reply.SelectMany(l => (l ?? "").Replace("\r", "").Split('\n'));

    /// <summary>
    /// "- PlayerName: X (you)", "ControllerId: Y", then "Hero: id resolved, ...", "Party: ...", "Clan: ..."
    /// for each player; ids shown as &lt;none&gt; stay empty.
    /// </summary>
    public static IReadOnlyList<CoopPlayerRow> Players(IEnumerable<string> reply)
    {
        var rows = new List<CoopPlayerRow>();
        string? name = null;
        string controller = "", hero = "", party = "", clan = "";

        void Flush()
        {
            if (name != null)
                rows.Add(new CoopPlayerRow(name, controller, hero, party, clan));
            name = null;
            controller = hero = party = clan = "";
        }

        foreach (var raw in Lines(reply))
        {
            var line = raw.Trim();
            if (line.StartsWith("- PlayerName:", StringComparison.Ordinal))
            {
                Flush();
                name = line["- PlayerName:".Length..].Trim();
                if (name.EndsWith(" (you)", StringComparison.Ordinal))
                    name = name[..^6].TrimEnd();
            }
            else if (name != null && line.StartsWith("ControllerId:", StringComparison.Ordinal))
                controller = line["ControllerId:".Length..].Trim();
            else if (name != null && line.StartsWith("Hero:", StringComparison.Ordinal))
                hero = IdOf(line["Hero:".Length..]);
            else if (name != null && line.StartsWith("Party:", StringComparison.Ordinal))
                party = IdOf(line["Party:".Length..]);
            else if (name != null && line.StartsWith("Clan:", StringComparison.Ordinal))
                clan = IdOf(line["Clan:".Length..]);
        }

        Flush();
        return rows;
    }

    private static string IdOf(string rest)
    {
        var word = rest.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? "";
        return word.StartsWith('<') ? "" : word;
    }

    /// <summary>coop.debug.kingdom.list: "Name: 'X'" then " Id : 'Y'".</summary>
    public static IReadOnlyList<CoopListItem> Kingdoms(IEnumerable<string> reply)
    {
        var list = new List<CoopListItem>();
        string? name = null;
        foreach (var raw in Lines(reply))
        {
            var line = raw.Trim();
            if (Quoted(line, "Name") is { } n)
                name = n;
            else if (Quoted(line, "Id") is { } id && name != null)
            {
                list.Add(new CoopListItem(id, name));
                name = null;
            }
        }

        return Sorted(list);
    }

    /// <summary>coop.debug.town.list_towns and coop.debug.village.list: "ID: 'x'" then "Name: 'y'".</summary>
    public static IReadOnlyList<CoopListItem> IdThenName(IEnumerable<string> reply)
    {
        var list = new List<CoopListItem>();
        string? id = null;
        foreach (var raw in Lines(reply))
        {
            var line = raw.Trim();
            if (Quoted(line, "ID") is { } i)
                id = i;
            else if (Quoted(line, "Name") is { } n && id != null)
            {
                list.Add(new CoopListItem(id, n));
                id = null;
            }
        }

        return Sorted(list);
    }

    private static readonly Regex HeroLine = new(@"^ID: '(?<id>[^']*)', Name: '(?<name>.*?)', Game ID", RegexOptions.CultureInvariant);

    /// <summary>coop.debug.hero.list: "ID: 'id', Name: 'name', Game ID: ..." per hero.</summary>
    public static IReadOnlyList<CoopListItem> Heroes(IEnumerable<string> reply) =>
        Sorted(Lines(reply).Select(l => HeroLine.Match(l.Trim())).Where(m => m.Success && m.Groups["id"].Value.Length > 0)
            .Select(m => new CoopListItem(m.Groups["id"].Value, m.Groups["name"].Value)).ToList());

    /// <summary>The value of "Key: 'value'" (spaces before the colon allowed), or null.</summary>
    private static string? Quoted(string line, string key)
    {
        var m = Regex.Match(line, "^" + Regex.Escape(key) + @"\s*:\s*'(.*)'$", RegexOptions.CultureInvariant);
        return m.Success ? m.Groups[1].Value : null;
    }

    private static IReadOnlyList<CoopListItem> Sorted(List<CoopListItem> list) =>
        list.GroupBy(i => i.Id, StringComparer.Ordinal).Select(g => g.First())
            .OrderBy(i => i.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
}
