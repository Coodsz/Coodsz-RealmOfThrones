using System.Globalization;
using System.Text.Json;

namespace TaomCoop.Core;

public sealed record GameplaySetting(string Group, string Key, string Label, string Kind, double Min, double Max, string Hint);

public static class GameplaySettingsEditor
{
    public static readonly IReadOnlyList<GameplaySetting> Definitions =
    [
        new("wotr", "wotr.enabled", "War of the Ring enabled", "bool", 0, 0, "Turns the scripted War of the Ring on. Off keeps these phases from advancing."),
        new("wotr", "wotr.phase1Day", "Isengard War phase day", "int", 1, 365, "The campaign day the Isengard War phase starts. Use a whole number from 1 to 365."),
        new("wotr", "wotr.phase2Day", "Full War phase day", "int", 2, 365, "The campaign day the full war starts. It must be later than the Isengard War day. Use a whole number from 2 to 365."),
        new("wotr", "wotr.testMode", "War of the Ring test mode", "bool", 0, 0, "Uses a shortened war schedule so you can test the phases. Leave this off for a normal campaign."),
        new("momentum", "momentum.enabled", "Momentum enabled", "bool", 0, 0, "Tracks war momentum from battles and events."),
        new("momentum", "momentum.victoryEnabled", "Momentum victory enabled", "bool", 0, 0, "A side can win the war when momentum reaches the threshold below."),
        new("momentum", "momentum.victoryThreshold", "Momentum victory threshold", "int", 100, 2000, "How much momentum is required for that victory. Use a whole number from 100 to 2000."),
        new("momentum", "momentum.participationMultiplier", "Player participation multiplier", "float", 1, 3, "How strongly a player's own actions count toward momentum. Use a number from 1 to 3."),
        new("momentum", "momentum.requirePlayerParticipation", "Require player participation for victory", "bool", 0, 0, "Momentum cannot win the war unless players took part."),
        new("momentum", "momentum.minimumPlayerEvents", "Minimum player events for victory", "int", 0, 20, "How many player events are required before that victory can happen. Use a whole number from 0 to 20."),
        new("party", "party.enabled", "Party scaling enabled", "bool", 0, 0, "Master switch for party size. Capacity changes apply after a restart. They raise the limit. They do not create troops."),
        new("party", "party.lordMultiplier", "Lord party capacity multiplier", "float", 1, 20, "Scales AI lord party size. Player clans are included only when the option below is on. Use a number from 1 to 20."),
        new("party", "party.lordFlatBonus", "Lord party capacity flat bonus", "float", 0, 2000, "Added after the multiplier. It raises the limit. It does not grant troops. Use a number from 0 to 2000."),
        new("party", "party.garrisonMultiplier", "Garrison capacity multiplier", "float", 1, 10, "Scales every settlement garrison, including player-owned ones. A huge garrison can discourage sieges. Use a number from 1 to 10."),
        new("party", "party.foodRelief", "AI lord food relief", "float", 0, 0.95, "Lowers how hard food limits AI lords. 0 is neutral. 0.5 waives half the food factor. Player clans never get this. Use a number from 0 to 0.95."),
        new("party", "party.wageRelief", "AI lord wage relief", "float", 0, 0.95, "Lowers AI lord wages. Player clans never get this, including players who have disconnected. Use a number from 0 to 0.95."),
        new("party", "party.includePlayerClans", "Apply lord capacity to all co-op player clans", "bool", 0, 0, "The lord capacity multiplier and bonus also apply to every registered player clan."),
        new("settlement", "settlement.foodTuning", "TAOM settlement food tuning", "bool", 0, 0, "Towns use the TAOM food rules for consumption, production, and storage. Off restores the game's own food rules."),
        new("settlement", "settlement.economyTuning", "TAOM settlement economy tuning", "bool", 0, 0, "Town markets use the TAOM gold targets and regeneration. Off restores the game's own economy rules."),
        new("bandits", "bandits.enabled", "Bandit scaling enabled", "bool", 0, 0, "Master switch for the bandit settings. Scaling stays tied to how far the campaign has progressed."),
        new("bandits", "bandits.densityCurve", "Bandit density curve", "float", 0, 5, "Raises how many bandit parties the map is allowed to hold as the campaign progresses. It does not fill the map immediately. Use a number from 0 to 5."),
        new("bandits", "bandits.partySizeCurve", "Bandit party-size curve", "float", 0, 5, "Applies when a new bandit party is created. Parties already on the map stay the same size. Use a number from 0 to 5."),
        new("bandits", "bandits.bossFightCurve", "Bandit boss-fight curve", "float", 0, 5, "Scales hideout fights only. Roaming parties are unchanged. Use a number from 0 to 5."),
        new("bandits", "bandits.maxHideoutsPerFaction", "Maximum hideouts per faction", "int", 1, 100, "The highest number of hideouts one bandit faction may have. Raising it does not create hideouts immediately. Use a whole number from 1 to 100."),
        new("bandits", "bandits.maxPartiesPerHideout", "Maximum parties per hideout", "int", 1, 20, "The highest number of parties one hideout may send out. Raising it does not add parties immediately. Use a whole number from 1 to 20."),
        new("bandits", "bandits.initialHideoutsPerFaction", "Initial hideouts per faction", "int", 1, 30, "How many hideouts each faction starts with. This applies to a new campaign only. Use a whole number from 1 to 30."),
        new("army", "army.enabled", "Army strategic intelligence enabled", "bool", 0, 0, "Turns on the planning that chooses where AI armies march and fight."),
        new("army", "army.warTheatersEnabled", "Army war theaters enabled", "bool", 0, 0, "Keeps armies focused on the part of the war they belong to."),
        new("army", "army.commitmentMultiplier", "Army commitment multiplier", "float", 1, 10, "How firmly an army stays on its current march or siege. Use a number from 1 to 10."),
        new("army", "army.priorityBoost", "Army maximum priority boost", "float", 1, 5, "The extra importance an urgent target can receive. Use a number from 1 to 5."),
        new("army", "army.evilAggressionScale", "Evil-faction aggression scale", "float", 0.5, 3, "How hard evil factions push their attacks. 1 is the normal rate. Use a number from 0.5 to 3."),
        new("army", "army.borderProximityFloor", "Army border-proximity floor", "float", 0, 1, "The lowest importance a border target can have, so nearby threats stay visible to armies. Use a number from 0 to 1."),
        new("army", "army.borderRescueRadiusInTownGaps", "Army border rescue radius", "float", 1, 20, "How far a defending army will travel, measured in gaps between towns, to help a border settlement. Use a number from 1 to 20."),
        new("army", "army.defenderPriorityMultiplier", "Army defender priority multiplier", "float", 1, 5, "How much defending is favored over attacking. Use a number from 1 to 5."),
        new("balance", "balance.customTroopPower", "Custom troop power enabled", "bool", 0, 0, "Uses TAOM troop power in battles. Off uses the game's own troop power."),
        new("balance", "balance.overrideVanillaTiers", "Override vanilla tier power", "bool", 0, 0, "Also replaces the game's tier power with the tier numbers below. Off leaves those tiers alone."),
        new("balance", "balance.tier7Power", "Tier 7 power", "float", 2, 6, "Strength used for tier 7 troops when custom power is on. Use a number from 2 to 6."),
        new("balance", "balance.tier8Power", "Tier 8 power", "float", 2, 7, "Strength used for tier 8 troops when custom power is on. Use a number from 2 to 7."),
        new("balance", "balance.tier9Power", "Tier 9 power", "float", 2, 8, "Strength used for tier 9 troops when custom power is on. Use a number from 2 to 8."),
        new("balance", "balance.tier10Power", "Tier 10 power", "float", 2, 9, "Strength used for tier 10 troops when custom power is on. Use a number from 2 to 9."),
        new("balance", "balance.heroMultiplier", "Hero power multiplier", "float", 1, 3, "Extra strength applied to heroes. Use a number from 1 to 3."),
        new("balance", "balance.mountedMultiplier", "Mounted troop multiplier", "float", 1, 2, "Extra strength applied to mounted troops. Use a number from 1 to 2."),
        new("balance", "balance.customCasualtyRatios", "Custom casualty ratios enabled", "bool", 0, 0, "Uses the blunt chances below to decide who is wounded instead of killed."),
        new("balance", "balance.playerBluntChance", "Player blunt casualty chance", "float", 0, 1, "The chance a player's troop is wounded instead of killed. 0 turns that protection off. 1 means they are wounded. Use a number from 0 to 1."),
        new("balance", "balance.aiBluntChance", "AI blunt casualty chance", "float", 0, 1, "The chance an AI troop is wounded instead of killed. Use a number from 0 to 1."),
        new("balance", "balance.culturalSurvivalBonuses", "Cultural survival bonuses enabled", "bool", 0, 0, "Cultures that are harder to wipe out keep that bonus.")
    ];

    public static readonly IReadOnlyList<GameplaySetting> AdvancedDefinitions =
    [
        new("advanced", "party.troopWeight", "Heavy troops count more toward party size", "bool", 0, 0, "When on, TAOM's heavy troops take more than one place in a party, for example elite troops 2 or 3, warg riders 2, cave trolls 4 and mumakil 10. Off counts every troop as one. Applies after a restart.")
    ];

    public static IReadOnlyDictionary<string, string> Read(string json, out int field, out int siege, out int sally)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        RequireExact(root, "schemaVersion", "battleSize", "taom");
        var battle = root.GetProperty("battleSize");
        RequireExact(battle, "field", "siege", "sallyOut");
        field = ReadWhole(battle, "field", 200, 2048);
        siege = ReadWhole(battle, "siege", 150, 2048);
        sally = ReadWhole(battle, "sallyOut", 150, 2048);
        var taom = root.GetProperty("taom");
        // Newer kits add settings this tool does not show (the 0.3.25 kit adds the mcm.* set), so only the shown ones are required.
        RequireAll(taom, Definitions.Select(item => item.Key).ToArray());
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var definition in Definitions)
        {
            var value = taom.GetProperty(definition.Key);
            values[definition.Key] = value.ValueKind switch
            {
                JsonValueKind.True => "true",
                JsonValueKind.False => "false",
                _ => value.GetRawText()
            };
        }

        foreach (var definition in AdvancedDefinitions)
        {
            if (!taom.TryGetProperty(definition.Key, out var value))
                continue;
            if (value.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
                throw new InvalidOperationException(definition.Key + " must be true or false.");
            values[definition.Key] = value.GetBoolean() ? "true" : "false";
        }

        return values;
    }

    public static string Apply(string json, IReadOnlyDictionary<string, string> values, string fieldText, string siegeText, string sallyText)
    {
        var current = Read(json, out var currentField, out var currentSiege, out var currentSally);
        // An empty box keeps the value the file already has instead of blocking every other setting from saving.
        var field = string.IsNullOrWhiteSpace(fieldText) ? currentField : ParseWhole(fieldText, "Field battle size", 200, 2048);
        var siege = string.IsNullOrWhiteSpace(siegeText) ? currentSiege : ParseWhole(siegeText, "Siege battle size", 150, 2048);
        var sally = string.IsNullOrWhiteSpace(sallyText) ? currentSally : ParseWhole(sallyText, "Sally-out battle size", 150, 2048);
        var parsed = new Dictionary<string, object>(StringComparer.Ordinal);
        foreach (var definition in Definitions)
        {
            if (!values.TryGetValue(definition.Key, out var text) || string.IsNullOrWhiteSpace(text))
                text = current[definition.Key];
            parsed[definition.Key] = Parse(definition, text);
        }

        foreach (var definition in AdvancedDefinitions)
        {
            // An older kit's TAOM.CoopCompat doesn't read this key, so it is never added to a kit that lacks it.
            if (!current.TryGetValue(definition.Key, out var currentValue))
                continue;
            if (!values.TryGetValue(definition.Key, out var text) || string.IsNullOrWhiteSpace(text))
                text = currentValue;
            parsed[definition.Key] = Parse(definition, text);
        }

        if ((int)parsed["wotr.phase2Day"] <= (int)parsed["wotr.phase1Day"])
            throw new InvalidOperationException("Full War phase day must be later than the Isengard War phase day.");

        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true }))
        {
            writer.WriteStartObject();
            writer.WriteNumber("schemaVersion", 1);
            writer.WriteStartObject("battleSize");
            writer.WriteNumber("field", field);
            writer.WriteNumber("siege", siege);
            writer.WriteNumber("sallyOut", sally);
            writer.WriteEndObject();
            writer.WriteStartObject("taom");
            foreach (var definition in Definitions)
                WriteValue(writer, definition.Key, parsed[definition.Key]);
            foreach (var definition in AdvancedDefinitions)
                if (parsed.TryGetValue(definition.Key, out var advancedValue))
                    WriteValue(writer, definition.Key, advancedValue);
            // Settings this tool does not show are written back as they were.
            using (var original = JsonDocument.Parse(json))
            {
                foreach (var property in original.RootElement.GetProperty("taom").EnumerateObject())
                {
                    if (parsed.ContainsKey(property.Name))
                        continue;
                    writer.WritePropertyName(property.Name);
                    property.Value.WriteTo(writer);
                }
            }
            writer.WriteEndObject();
            writer.WriteEndObject();
        }

        return System.Text.Encoding.UTF8.GetString(stream.ToArray()) + Environment.NewLine;
    }

    private static object Parse(GameplaySetting definition, string text)
    {
        if (definition.Kind == "bool")
        {
            if (text.Equals("true", StringComparison.OrdinalIgnoreCase))
                return true;
            if (text.Equals("false", StringComparison.OrdinalIgnoreCase))
                return false;
            throw new InvalidOperationException(definition.Label + " must be true or false.");
        }

        if (!double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var number)
            || double.IsNaN(number) || double.IsInfinity(number)
            || number < definition.Min || number > definition.Max
            || (definition.Kind == "int" && number != Math.Truncate(number)))
            throw new InvalidOperationException(definition.Label + " must be a " + definition.Kind + " from " + FormatLimit(definition.Min) + " to " + FormatLimit(definition.Max) + ".");

        return definition.Kind == "int" ? (object)(int)number : number;
    }

    private static int ParseWhole(string text, string label, int min, int max)
    {
        if (!int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var number) || number < min || number > max)
            throw new InvalidOperationException(label + " must be a whole number from " + min + " to " + max + ".");
        return number;
    }

    private static int ReadWhole(JsonElement parent, string name, int min, int max)
    {
        var value = parent.GetProperty(name);
        if (value.ValueKind != JsonValueKind.Number || !value.TryGetInt32(out var number) || number < min || number > max)
            throw new InvalidOperationException("battleSize." + name + " must be a whole number from " + min + " to " + max + ".");
        return number;
    }

    private static void WriteValue(Utf8JsonWriter writer, string name, object value)
    {
        switch (value)
        {
            case bool flag:
                writer.WriteBoolean(name, flag);
                break;
            case int whole:
                writer.WriteNumber(name, whole);
                break;
            case double number:
                writer.WriteNumber(name, number);
                break;
            default:
                throw new InvalidOperationException(name + " has an unsupported value.");
        }
    }

    private static void RequireExact(JsonElement element, params string[] names)
    {
        var actual = element.EnumerateObject().Select(property => property.Name).OrderBy(name => name, StringComparer.Ordinal).ToArray();
        var expected = names.OrderBy(name => name, StringComparer.Ordinal).ToArray();
        if (!actual.SequenceEqual(expected, StringComparer.Ordinal))
            throw new InvalidOperationException("taom-coop-settings.json must contain the exact supported setting set.");
    }

    private static void RequireAll(JsonElement element, params string[] names)
    {
        var actual = new HashSet<string>(element.EnumerateObject().Select(property => property.Name), StringComparer.Ordinal);
        var missing = names.Where(name => !actual.Contains(name)).ToArray();
        if (missing.Length > 0)
            throw new InvalidOperationException("taom-coop-settings.json is missing: " + string.Join(", ", missing) + ".");
    }

    private static string FormatLimit(double value) =>
        value.ToString(value == Math.Truncate(value) ? "0" : "0.##", CultureInfo.InvariantCulture);
}
