using BannerlordCoopLauncher.Core.Configuration;

namespace TaomCoop.Core;

public sealed record DifficultySetting(string Key, string Label, string Hint);

/// <summary>
/// The campaign difficulty block of Coop's mod-config.json. The hosting side applies it
/// every time the server starts, and joining players get the same values with the save.
/// Each value is "VeryEasy", "Easy", or "Realistic" (the hardest).
/// </summary>
public static class CoopDifficulty
{
    public static readonly IReadOnlyList<string> Levels = ModSettingsCatalog.Levels;

    public static readonly IReadOnlyList<DifficultySetting> Settings =
    [
        new("playerReceivedDamage", "Damage players take", "Very easy: -75%. Easy: -50%. Realistic: full damage."),
        new("playerTroopsReceivedDamage", "Damage your troops take", "Very easy: -50%. Easy: -25%. Realistic: full damage."),
        new("combatAIDifficulty", "Enemy fighting skill", "Very easy: Normal. Easy: Veteran. Realistic: Challenging."),
        new("recruitmentDifficulty", "Troops from notables", "Very easy: +2 troops. Easy: +1 troop. Realistic: no extra troops."),
        new("playerMapMovementSpeed", "Party speed on the map", "Very easy: +10%. Easy: +5%. Realistic: normal speed."),
        new("stealthAndDisguiseDifficulty", "Stealth and disguise", "Very easy: Normal. Easy: Veteran. Realistic: Challenging."),
        new("persuasionSuccessChance", "Persuasion chance", "Very easy: +10%. Easy: +5%. Realistic: normal chance."),
        new("clanMemberDeathChance", "Clan members dying in battle", "Very easy: never. Easy: half as often. Realistic: normal."),
        new("battleDeath", "Heroes dying in battle", "Very easy: no hero dies. Easy: player heroes do not die. Realistic: every hero can die."),
    ];

    public static string LevelName(string level) => level switch
    {
        "VeryEasy" => "Very easy",
        "Easy" => "Easy",
        _ => "Realistic",
    };

    /// <summary>The level saved for this setting (read inside the difficulty block, where the
    /// server reads it), or null when the file does not have it.</summary>
    public static string? Read(string json, string key) =>
        CoopModConfig.ReadRaw(json, key) is { Length: > 1 } raw && raw[0] == '"' && raw[^1] == '"'
            ? Normalize(raw[1..^1])
            : null;

    /// <summary>
    /// Writes a level. A file without this setting is left as it is (false), because the
    /// value belongs inside the difficulty block and Coop writes the full block itself.
    /// </summary>
    public static bool TrySet(ref string json, string key, string level)
    {
        if (!Levels.Contains(level))
            throw new ArgumentException("Unknown difficulty " + level + ".", nameof(level));
        if (CoopModConfig.ReadRaw(json, key) == null)
            return false;
        json = CoopModConfig.SetValue(json, key, "\"" + level + "\"");
        return true;
    }

    /// <summary>
    /// Shown above the difficulty boxes while any of them is greyed out. Coop adds the whole difficulty block
    /// to mod-config.json the first time the server loads it (ModConfig.MigrateDifficultySettings).
    /// </summary>
    public const string MissingNote =
        "Some boxes are greyed out because Coop's mod-config.json doesn't have these settings yet. Start the server once and Coop adds them, then open Settings again.";

    /// <summary>The save status when one or more greyed out boxes were left as they are.</summary>
    public const string SkippedOnSave =
        "Saved settings, but not the greyed out difficulty boxes, because Coop's mod-config.json doesn't have them yet. Start the server once, then save them again. Restart the server to use the rest.";

    /// <summary>How many difficulty settings this file does not have yet (every one when there is no file).</summary>
    public static int MissingCount(string json) => Settings.Count(setting => Read(json, setting.Key) == null);

    private static string? Normalize(string value) =>
        Levels.FirstOrDefault(level => level.Equals(value, StringComparison.OrdinalIgnoreCase));
}
