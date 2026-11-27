using System.Globalization;
using BannerlordCoopLauncher.Core.Configuration;

namespace BannerlordCoopLauncher.Core.Keep;

/// <summary>One preset card: a name, what it is for, and the Coop settings it sets.</summary>
public sealed record WorldPreset(string Id, string Title, string Description, IReadOnlyDictionary<string, object> Values);

/// <summary>
/// The world preset cards (Casual, Standard, Hardcore) on the Settings page and in the new world
/// wizard, after CoopKeep's difficulty cards. A preset only sets keys of
/// <see cref="ModSettingsCatalog"/>, which are the settings the Coop server reads from
/// mod-config.json; everything else on the page stays as it is.
/// </summary>
public static class WorldPresets
{
    public const string Casual = "casual";
    public const string Standard = "standard";
    public const string Hardcore = "hardcore";

    private static Dictionary<string, object> Levels(string level) =>
        ModSettingsCatalog.All.Where(d => d.Kind == ModSettingKind.Level).ToDictionary(d => d.Id, _ => (object)level);

    private static IReadOnlyDictionary<string, object> With(string level, params (string Key, object Value)[] extra)
    {
        var values = Levels(level);
        foreach (var (key, value) in extra)
            values[ModSettingsCatalog.OptionsSection + "." + key] = value;
        return values;
    }

    public static readonly IReadOnlyList<WorldPreset> All =
    [
        new(Casual, "Casual", "Very easy. Nobody dies in battle and no executions. For playing the story together.",
            With("VeryEasy", ("enableHeroExecutions", false), ("enablePlayerClanMemberExecutions", false), ("enablePlayerExecutions", false),
                ("playerWoundedBattleEntry", true))),
        new(Standard, "Standard", "Easy. Some challenge, but mistakes are forgiven. No executions.",
            With("Easy", ("enableHeroExecutions", false), ("enablePlayerClanMemberExecutions", false), ("enablePlayerExecutions", false),
                ("playerWoundedBattleEntry", true))),
        new(Hardcore, "Hardcore", "Realistic. Heroes can die in battle, AI lords can be executed, wounded players sit battles out.",
            With("Realistic", ("enableHeroExecutions", true), ("enablePlayerClanMemberExecutions", false), ("enablePlayerExecutions", false),
                ("playerWoundedBattleEntry", false))),
    ];

    public static WorldPreset? Find(string? id) => All.FirstOrDefault(p => p.Id.Equals(id, StringComparison.OrdinalIgnoreCase));

    /// <summary>Copies the preset's settings into <paramref name="values"/>; the rest stays.</summary>
    public static void Apply(IDictionary<string, object> values, WorldPreset preset)
    {
        foreach (var (id, value) in preset.Values)
            values[id] = value;
    }

    /// <summary>The preset every one of whose settings matches, or <see langword="null"/> (custom).</summary>
    public static WorldPreset? Detect(IReadOnlyDictionary<string, object> values) =>
        All.FirstOrDefault(preset => preset.Values.All(pair => Same(values, pair.Key, pair.Value)));

    private static bool Same(IReadOnlyDictionary<string, object> values, string id, object wanted)
    {
        var setting = ModSettingsCatalog.Find(id);
        var value = values.TryGetValue(id, out var v) ? v : setting?.Default;
        return value switch
        {
            string text when wanted is string w => text.Equals(w, StringComparison.OrdinalIgnoreCase),
            _ => Equals(value, wanted),
        };
    }

    /// <summary>The short tags on the Dashboard: "Very easy", "battle size 1000", "battle death off", "player cheats on".</summary>
    public static IReadOnlyList<string> Chips(IReadOnlyDictionary<string, object> values)
    {
        var chips = new List<string>();
        var level = ModSettingsCatalog.DetectPreset(values);
        chips.Add(level switch
        {
            "VeryEasy" => "Very easy",
            "Easy" => "Easy",
            "Realistic" => "Realistic",
            _ => "Mixed difficulty",
        });
        var battle = values.TryGetValue(ModSettingsCatalog.OptionsSection + ".battleSize", out var size) && size is int n
            ? n
            : ModSettingsCatalog.BattleSizeDefault;
        chips.Add("battle size " + battle.ToString(CultureInfo.InvariantCulture));
        var death = values.TryGetValue(ModSettingsCatalog.DifficultySection + ".battleDeath", out var d) ? d as string : "VeryEasy";
        chips.Add("VeryEasy".Equals(death, StringComparison.OrdinalIgnoreCase) ? "battle death off" : "battle death on");
        var cheats = values.TryGetValue(ModSettingsCatalog.OptionsSection + ".clientsCanUseCheats", out var c) && c is true;
        chips.Add(cheats ? "player cheats on" : "player cheats off");
        return chips;
    }
}
