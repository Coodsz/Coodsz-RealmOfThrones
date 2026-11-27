// Ported from CoopKeep (https://github.com/Aderimo/bannerlord-coopkeep),
// Configuration/ModSettings.cs, translated from Turkish and changed for the Coodsz tools.
//
// Copyright (c) 2026 aderimo
//
// Permission is hereby granted, free of charge, to any person obtaining a copy
// of this software and associated documentation files (the "Software"), to deal
// in the Software without restriction, including without limitation the rights
// to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
// copies of the Software, and to permit persons to whom the Software is
// furnished to do so, subject to the following conditions:
//
// The above copyright notice and this permission notice shall be included in all
// copies or substantial portions of the Software.
//
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
// IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
// FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
// AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
// LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
// OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
// SOFTWARE.

using System.Globalization;
using System.Text.Json;

namespace BannerlordCoopLauncher.Core.Configuration;

/// <summary>The kind of value a world setting holds.</summary>
public enum ModSettingKind
{
    /// <summary><c>"VeryEasy" | "Easy" | "Realistic"</c>, the game's own difficulty levels.</summary>
    Level,
    Bool,
    Int,
    Number,

    /// <summary>Fixed text choices (<see cref="ModSettingDefinition.Choices"/>).</summary>
    Choice,

    /// <summary>A JSON array of SteamID64 strings, in order (the value is a <c>string[]</c>).</summary>
    SteamIdList,
}

/// <summary>
/// One gameplay setting in <c>mod-config.json</c>.
/// </summary>
/// <param name="Section"><c>difficulty</c>, <c>modOptions</c> or <c>voice</c>.</param>
/// <param name="Key">The key in the file.</param>
/// <param name="Group">For grouping on screen.</param>
/// <param name="Default">The mod's own default (<c>mod-config.default.json</c>).</param>
/// <param name="Label">A short English name for the screen and for error messages.</param>
/// <param name="Since">The first Coop version that reads the key, or <see langword="null"/> when every Coop does.</param>
public sealed record ModSettingDefinition(
    string Section,
    string Key,
    ModSettingKind Kind,
    ModSettingGroup Group,
    object Default,
    string Label,
    double Min = 0,
    double Max = 0,
    IReadOnlyList<string>? Choices = null,
    string? Since = null)
{
    public string Id => Section + "." + Key;
}

public enum ModSettingGroup
{
    /// <summary>The difficulty screen the game shows when a new campaign starts.</summary>
    Difficulty,

    /// <summary>Death, aging, executions.</summary>
    LifeAndDeath,

    /// <summary>Battle size, battle hosts, bandits, AI.</summary>
    World,

    /// <summary>Speed, pausing, economy: things only multiplayer has.</summary>
    Multiplayer,

    /// <summary>Proximity voice chat distances.</summary>
    Voice,

    /// <summary>Cheats.</summary>
    Cheats,
}

/// <summary>
/// Every gameplay setting the Coop server reads from <c>CoopData\mod-config.json</c>, with
/// its section, the mod's default and the range the tools accept. This is the one list the
/// Coodsz tools use for those settings (the TAOM tools, Coodsz Host and the AD1259 server).
/// </summary>
/// <remarks>
/// <para>
/// Source: the <c>mod-config.default.json</c> and <c>ModConfigData.cs</c> of Coop v0.1.6
/// (2026-10-13). The defaults Coop v0.1.7 changed are in <c>NewerDefaults</c>, for a server that runs it.
/// Keys a newer Coop added carry its version in <see cref="ModSettingDefinition.Since"/> (0.1.7 from the
/// Workshop build, 0.1.8 from the nightly), and are only written for a server that has them (<see cref="Supports"/>).
/// The server's own words: <i>"Campaign difficulty, applied by the HOSTING
/// side at every boot; joining players inherit the values with the save transfer."</i> So the
/// difficulty values are the same choices the game offers for a new campaign, and they are
/// applied again on every start.
/// </para>
/// <para>
/// A key outside its section is an unknown key to the server and does nothing. Other keys in
/// the file (the movement limits, keys a newer Coop adds) are not listed here, and
/// <see cref="JsoncEditor"/> leaves them untouched.
/// </para>
/// </remarks>
public static class ModSettingsCatalog
{
    public const string DifficultySection = "difficulty";
    public const string OptionsSection = "modOptions";
    public const string VoiceSection = "voice";

    // The Coop server's BattleSizeProvider reads modOptions.battleSize (a whole number),
    // uses 1000 when it is missing, and clamps it to 200..1000. With Limitless and
    // Limitless.CoopCompat on the server it goes up to BigBattles.Max, so the file range
    // below is that wider one; the screens clamp to the mod list (BigBattles.MaxFor).
    public const int BattleSizeMin = 200;
    public const int BattleSizeMax = 1000;
    public const int BattleSizeDefault = 1000;

    public static readonly IReadOnlyList<string> Sections = [DifficultySection, OptionsSection, VoiceSection];

    public static readonly IReadOnlyList<string> Levels = ["VeryEasy", "Easy", "Realistic"];

    private static ModSettingDefinition Level(string key, string label, ModSettingGroup group = ModSettingGroup.Difficulty) =>
        new(DifficultySection, key, ModSettingKind.Level, group, "VeryEasy", label, Choices: Levels);

    private static ModSettingDefinition Flag(string section, string key, ModSettingGroup group, bool value, string label) =>
        new(section, key, ModSettingKind.Bool, group, value, label);

    public static readonly IReadOnlyList<ModSettingDefinition> All =
    [
        // --- Difficulty (the game's new campaign screen) -----------------
        Level("playerReceivedDamage", "Player received damage"),
        Level("playerTroopsReceivedDamage", "Troop received damage"),
        Level("combatAIDifficulty", "Combat AI"),
        Level("recruitmentDifficulty", "Recruitment"),
        Level("playerMapMovementSpeed", "Map movement"),
        Level("stealthAndDisguiseDifficulty", "Stealth and disguise"),
        Level("persuasionSuccessChance", "Persuasion"),

        // --- Life and death --------------------------------------------
        Level("clanMemberDeathChance", "Clan member death", ModSettingGroup.LifeAndDeath),
        Level("battleDeath", "Battle death", ModSettingGroup.LifeAndDeath),
        Flag(DifficultySection, "birthAndDeath", ModSettingGroup.LifeAndDeath, true, "Birth and death"),
        Flag(DifficultySection, "autoAllocateClanMemberPerks", ModSettingGroup.LifeAndDeath, false, "Auto allocate clan member perks"),
        Flag(OptionsSection, "enableHeroExecutions", ModSettingGroup.LifeAndDeath, false, "Hero executions"),
        Flag(OptionsSection, "enablePlayerClanMemberExecutions", ModSettingGroup.LifeAndDeath, false, "Player clan member executions"),
        Flag(OptionsSection, "enablePlayerExecutions", ModSettingGroup.LifeAndDeath, false, "Player executions"),

        // --- World -----------------------------------------------------
        new(OptionsSection, "battleSize", ModSettingKind.Int, ModSettingGroup.World, BattleSizeDefault, "Battle size", BattleSizeMin, Modules.BigBattles.Max),
        new(OptionsSection, "maximumLootersMultiplier", ModSettingKind.Number, ModSettingGroup.World, 1.0, "Looter / bandit party count", 0, 10),
        new(OptionsSection, "looterPartySizeMultiplier", ModSettingKind.Number, ModSettingGroup.World, 1.0, "Looter / bandit party size", 0.1, 10),
        new(OptionsSection, "wandererLimit", ModSettingKind.Int, ModSettingGroup.World, 32, "Wanderer / companion limit", 0, 500),
        new(OptionsSection, "spawnRateWanderers", ModSettingKind.Int, ModSettingGroup.World, 1, "New wanderers per day", 0, 10, Since: "v0.1.7"),
        Flag(OptionsSection, "ensureUnaffiliatedWanderers", ModSettingGroup.World, true, "Keep free wanderers in taverns"),
        Flag(OptionsSection, "wandererLimitScalesWithPlayers", ModSettingGroup.World, false, "Wanderer limit scales with players"),
        Flag(OptionsSection, "smithingStaminaRecoveryOutsideSettlements", ModSettingGroup.World, true, "Smithing stamina outside settlements"),
        new(OptionsSection, "smithingStaminaRecoveryMultiplier", ModSettingKind.Number, ModSettingGroup.World, 0.1, "Smithing stamina recovery multiplier", 0.1, 5),
        new(OptionsSection, "playerKingdomClanTierRequired", ModSettingKind.Int, ModSettingGroup.World, 4, "Clan tier required to form a kingdom", 0, 6),
        Flag(OptionsSection, "blockAiWarDeclarationsOnOfflinePlayers", ModSettingGroup.World, false, "No AI war declarations on offline players"),
        new(OptionsSection, "playerBattleAiJoinWindowHours", ModSettingKind.Int, ModSettingGroup.World, 6, "AI battle join window (hours)", 0, 168),
        // Real seconds after a battle that AI parties leave the player alone. Coop has no top; 600 (ten minutes)
        // is far past any loot screen, and more would just make a player safe from AI for good.
        new(OptionsSection, "postBattleAiAmnestySeconds", ModSettingKind.Int, ModSettingGroup.World, 0, "AI leaves players alone after a battle (seconds)", 0, 600, Since: "v0.1.8"),
        new(OptionsSection, "clansAfterKingdomDestruction", ModSettingKind.Choice, ModSettingGroup.World,
            "Vanilla", "Clans after a kingdom is destroyed", Choices: ["Vanilla", "KeepClans"], Since: "v0.1.8"),
        // SteamID64s in order; Coop lets the first one that has finished loading host the battle.
        new(OptionsSection, "preferredBattleHosts", ModSettingKind.SteamIdList, ModSettingGroup.World,
            Array.Empty<string>(), "Preferred battle hosts", Since: "v0.1.8"),

        // --- Multiplayer -----------------------------------------------
        Flag(OptionsSection, "fastForwardEnabled", ModSettingGroup.Multiplayer, true, "Fast forwarding"),
        Flag(OptionsSection, "autoPauseEnabled", ModSettingGroup.Multiplayer, true, "Auto pause"),
        Flag(OptionsSection, "speedLimitWhilePlayersInBattle", ModSettingGroup.Multiplayer, true, "Speed limit while players are in battle"),
        Flag(OptionsSection, "playerWoundedBattleEntry", ModSettingGroup.Multiplayer, true, "Wounded players can join battles"),
        Flag(OptionsSection, "showPlayerNameplates", ModSettingGroup.Multiplayer, true, "Player nameplates"),
        Flag(OptionsSection, "coopClansEnabled", ModSettingGroup.Multiplayer, true, "Players can share a clan"),
        Flag(OptionsSection, "goldFoodInfluenceChangeForDisconnectedPlayers", ModSettingGroup.Multiplayer, false, "Disconnected player gold, food, and influence"),
        Flag(OptionsSection, "goldFoodInfluenceChangeInSettlements", ModSettingGroup.Multiplayer, false, "Settlement gold, food, and influence"),
        new(OptionsSection, "goldFoodInfluenceChangeInBattles", ModSettingKind.Choice, ModSettingGroup.Multiplayer,
            "Disabled", "Battle gold, food, and influence", Choices: ["Disabled", "OneDayMax", "Enabled"]),
        new(OptionsSection, "lordDefectionRetries", ModSettingKind.Choice, ModSettingGroup.Multiplayer,
            "Vanilla", "Lord defection retries", Choices: ["Vanilla", "NeverExpire", "AlwaysRetry"]),
        // Coop puts 0 or less back to 60 and has no top; an hour is plenty for a vote and keeps a typo from stalling one.
        new(OptionsSection, "kingdomDecisionVoteTimeoutSeconds", ModSettingKind.Int, ModSettingGroup.Multiplayer, 60, "Kingdom decision vote time (seconds)", 1, 3600, Since: "v0.1.7"),

        // --- Voice -----------------------------------------------------
        Flag(OptionsSection, "voiceEnabled", ModSettingGroup.Voice, true, "Voice chat"),
        new(VoiceSection, "mapFullVolumeDistance", ModSettingKind.Int, ModSettingGroup.Voice, 5, "Voice full volume distance on the map", 0, 100),
        new(VoiceSection, "mapMaximumDistance", ModSettingKind.Int, ModSettingGroup.Voice, 20, "Voice range on the map", 1, 100),
        new(VoiceSection, "sceneFullVolumeDistance", ModSettingKind.Int, ModSettingGroup.Voice, 10, "Voice full volume distance in scenes", 0, 200),
        new(VoiceSection, "sceneMaximumDistance", ModSettingKind.Int, ModSettingGroup.Voice, 50, "Voice range in scenes", 1, 200),

        // --- Cheats ----------------------------------------------------
        Flag(OptionsSection, "clientsCanUseCheats", ModSettingGroup.Cheats, false, "Client cheats"),
    ];

    public static ModSettingDefinition? Find(string id) =>
        All.FirstOrDefault(d => d.Id.Equals(id, StringComparison.OrdinalIgnoreCase));

    /// <summary>The setting with this key (in any section), or <see langword="null"/>.</summary>
    public static ModSettingDefinition? FindByKey(string key) =>
        All.FirstOrDefault(d => d.Key.Equals(key, StringComparison.OrdinalIgnoreCase));

    /// <summary>The section the server reads this key from, or <see langword="null"/> for a key
    /// that is not a Coop gameplay setting (server-config.json keys, the movement limits).</summary>
    public static string? SectionOf(string key) => FindByKey(key)?.Section;

    /// <summary>
    /// Defaults a newer Coop changed, with the first Coop version that has them. The catalogue's own default is
    /// Coop 0.1.6's, which TAOM (pinned to 0.1.6), Burning Empires and a file written before 0.1.7 run with.
    /// Coop 0.1.7's server raised smithing stamina recovery from 0.1 to 0.6.
    /// </summary>
    private static readonly (string Id, string Since, object Value)[] NewerDefaults =
    [
        (OptionsSection + ".smithingStaminaRecoveryMultiplier", "v0.1.7", 0.6),
    ];

    /// <summary>The mod's defaults for the Coop version the server runs (Coop 0.1.6's when it is not known).</summary>
    public static Dictionary<string, object> Defaults(string? coopVersion = null) =>
        All.ToDictionary(d => d.Id, d => Default(d, coopVersion));

    /// <summary>One setting's default for the Coop version the server runs: what that server uses when the key is missing.</summary>
    public static object Default(ModSettingDefinition setting, string? coopVersion)
    {
        foreach (var (id, since, value) in NewerDefaults)
        {
            if (id == setting.Id && coopVersion != null && Modules.CoopInstall.Compare(coopVersion, since) >= 0)
                return value;
        }

        return setting.Default;
    }

    /// <summary>True when the server's Coop reads this key. A key with a <see cref="ModSettingDefinition.Since"/>
    /// is not supported by an unknown or unreadable version, so it is never written to a Coop that would not know it.</summary>
    public static bool Supports(ModSettingDefinition setting, string? coopVersion) =>
        setting.Since == null || Modules.CoopInstall.Compare(coopVersion, setting.Since) >= 0;

    /// <summary>True for a SteamID64 as Coop's battle host list takes it: 17 digits.</summary>
    public static bool IsSteamId(string? text) =>
        text is { Length: 17 } && text.All(char.IsAsciiDigit);

    /// <summary>
    /// Difficulty preset: sets every level setting to the same level.
    /// </summary>
    /// <remarks>The same as the easy, normal and realistic buttons on the game's new campaign screen.</remarks>
    public static void ApplyPreset(IDictionary<string, object> values, string level)
    {
        if (!Levels.Contains(level))
            throw new ArgumentException("Unknown level " + level + ".", nameof(level));

        foreach (var d in All.Where(d => d.Kind == ModSettingKind.Level))
            values[d.Id] = level;
    }

    /// <summary>That level when every level setting is the same, otherwise <see langword="null"/> (custom).</summary>
    public static string? DetectPreset(IReadOnlyDictionary<string, object> values)
    {
        var levels = All.Where(d => d.Kind == ModSettingKind.Level)
            .Select(d => values.TryGetValue(d.Id, out var v) ? v as string : null)
            .Distinct()
            .ToList();

        return levels.Count == 1 ? levels[0] : null;
    }

    /// <summary>Checks the value against the setting and clamps it; an unusable value gives the default.</summary>
    public static object Normalize(ModSettingDefinition d, object? value)
    {
        switch (d.Kind)
        {
            case ModSettingKind.Bool:
                return value switch
                {
                    bool b => b,
                    string s when bool.TryParse(s, out var parsed) => parsed,
                    _ => d.Default,
                };

            case ModSettingKind.Int:
                if (!TryNumber(value, out var i)) return d.Default;
                return (int)Math.Clamp(Math.Round(i), d.Min, d.Max);

            case ModSettingKind.Number:
                if (!TryNumber(value, out var n)) return d.Default;
                return Math.Round(Math.Clamp(n, d.Min, d.Max), 2);

            case ModSettingKind.SteamIdList:
                // Trimmed, only real SteamID64s, each once, in the order given.
                var ids = value switch
                {
                    string text => SplitIds(text),
                    IEnumerable<string?> list => list,
                    JsonElement { ValueKind: JsonValueKind.Array } array =>
                        array.EnumerateArray().Where(e => e.ValueKind == JsonValueKind.String).Select(e => e.GetString()),
                    _ => null,
                };
                return ids == null
                    ? d.Default
                    : ids.Select(id => id?.Trim()).Where(IsSteamId).Distinct(StringComparer.Ordinal).ToArray()!;

            default:
                return value is string s2 && d.Choices?.FirstOrDefault(c => c.Equals(s2, StringComparison.OrdinalIgnoreCase)) is { } choice
                    ? choice
                    : d.Default;
        }
    }

    /// <summary>
    /// Reads typed text for a setting: <see langword="null"/> when it is not a number in range
    /// (or not one of the choices), so the screen can say what is wrong instead of clamping.
    /// </summary>
    public static object? ParseText(ModSettingDefinition d, string? text)
    {
        var value = (text ?? "").Trim();
        switch (d.Kind)
        {
            case ModSettingKind.Int:
            case ModSettingKind.Number:
                if (!double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var number)
                    || !double.IsFinite(number) || number < d.Min || number > d.Max)
                    return null;
                if (d.Kind == ModSettingKind.Int && number != Math.Truncate(number))
                    return null;
                return Normalize(d, number);
            case ModSettingKind.Bool:
                return bool.TryParse(value, out var flag) ? flag : null;
            case ModSettingKind.SteamIdList:
                // Comma, space or newline separated; one entry that is not a SteamID64 makes the text wrong.
                var ids = SplitIds(value);
                return ids.All(IsSteamId) ? Normalize(d, ids) : null;
            default:
                return d.Choices?.FirstOrDefault(c => c.Equals(value, StringComparison.OrdinalIgnoreCase));
        }
    }

    /// <summary>Turns the value into the JSON literal written to <c>mod-config.json</c>.</summary>
    public static string ToJsonLiteral(ModSettingDefinition d, object value) => Normalize(d, value) switch
    {
        bool b => b ? "true" : "false",
        int i => i.ToString(CultureInfo.InvariantCulture),
        double n => n.ToString("0.0#", CultureInfo.InvariantCulture),
        string s => JsonSerializer.Serialize(s),
        string[] ids => JsonSerializer.Serialize(ids),
        var other => throw new InvalidOperationException("Unsupported value " + other + "."),
    };

    /// <summary>Turns a JSON value into the setting's type.</summary>
    public static object FromJson(ModSettingDefinition d, JsonElement e) => d.Kind switch
    {
        ModSettingKind.Bool when e.ValueKind is JsonValueKind.True or JsonValueKind.False => e.GetBoolean(),
        ModSettingKind.Int or ModSettingKind.Number when e.ValueKind == JsonValueKind.Number =>
            Normalize(d, e.GetDouble()),
        ModSettingKind.Level or ModSettingKind.Choice when e.ValueKind == JsonValueKind.String =>
            Normalize(d, e.GetString()),
        ModSettingKind.SteamIdList when e.ValueKind == JsonValueKind.Array => Normalize(d, e),
        _ => d.Default,
    };

    private static string[] SplitIds(string text) =>
        text.Split([',', ' ', '\t', '\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    private static bool TryNumber(object? value, out double result)
    {
        switch (value)
        {
            case int i: result = i; return true;
            case long l: result = l; return true;
            case double d when double.IsFinite(d): result = d; return true;
            case float f when float.IsFinite(f): result = f; return true;
            case decimal m: result = (double)m; return true;
            case JsonElement { ValueKind: JsonValueKind.Number } e: result = e.GetDouble(); return true;
            case string s when double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var p):
                result = p; return true;
            default: result = 0; return false;
        }
    }
}
