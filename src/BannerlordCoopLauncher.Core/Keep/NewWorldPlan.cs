using BannerlordCoopLauncher.Core.Configuration;
using BannerlordCoopLauncher.Core.Modules;
using BannerlordCoopLauncher.Core.Saves;

namespace BannerlordCoopLauncher.Core.Keep;

/// <summary>The steps of the new world wizard, after CoopKeep's New world dialog.</summary>
public enum NewWorldStep
{
    Start,
    Name,
    Difficulty,
    Rules,
    Server,
}

/// <summary>
/// What the new world wizard collects: a fresh campaign or a single player save, the save name,
/// a preset, a few rules and the join password. Finishing it does what + New save and Save
/// settings already do: the name becomes the setup's new campaign, and the settings are kept for
/// that save and written to mod-config.json when it starts. Nothing the server does not read.
/// </summary>
public sealed class NewWorldPlan
{
    public static readonly IReadOnlyList<NewWorldStep> Steps =
        [NewWorldStep.Start, NewWorldStep.Name, NewWorldStep.Difficulty, NewWorldStep.Rules, NewWorldStep.Server];

    /// <summary>The on/off rules of the Rules step, all Coop settings from mod-config.json.</summary>
    public static readonly IReadOnlyList<string> RuleIds =
    [
        "modOptions.clientsCanUseCheats",
        "modOptions.fastForwardEnabled",
        "modOptions.autoPauseEnabled",
        "modOptions.showPlayerNameplates",
        "modOptions.coopClansEnabled",
        "modOptions.voiceEnabled",
    ];

    public const string BattleSizeId = "modOptions.battleSize";

    public NewWorldPlan(IReadOnlyDictionary<string, object>? current = null, string? coopVersion = null)
    {
        Values = ModSettingsCatalog.Defaults(coopVersion);
        if (current != null)
            foreach (var (id, value) in current)
                Values[id] = value;
        PresetId = WorldPresets.Detect(Values)?.Id;
        if (PresetId == null)
            UsePreset(WorldPresets.Standard);
    }

    public bool FromSinglePlayer { get; set; }

    public string Name { get; set; } = "";

    public string Password { get; set; } = "";

    public string? PresetId { get; private set; }

    /// <summary>Every Coop setting of the new world.</summary>
    public Dictionary<string, object> Values { get; }

    public static string StepTitle(NewWorldStep step) => step switch
    {
        NewWorldStep.Name => "Name",
        NewWorldStep.Difficulty => "Difficulty",
        NewWorldStep.Rules => "Rules",
        NewWorldStep.Server => "Server",
        _ => "Start",
    };

    public void UsePreset(string id)
    {
        if (WorldPresets.Find(id) is not { } preset)
            throw new ArgumentException("Unknown preset " + id + ".", nameof(id));
        WorldPresets.Apply(Values, preset);
        PresetId = preset.Id;
    }

    public bool Rule(string id) => Values.TryGetValue(id, out var value) && value is true;

    public void SetRule(string id, bool on)
    {
        if (!RuleIds.Contains(id))
            throw new ArgumentException("Not a wizard rule: " + id + ".", nameof(id));
        Values[id] = on;
    }

    private BigBattleMode _battleMode;

    /// <summary>What the server's mod list allows: 200..1000, or up to BigBattles.Max with Limitless
    /// and its Coop patch. Leaving the wider range brings a bigger battle size back to 1000.</summary>
    public BigBattleMode BattleMode
    {
        get => _battleMode;
        set
        {
            _battleMode = value;
            BattleSize = BattleSize;
        }
    }

    public int BattleSize
    {
        get => Values.TryGetValue(BattleSizeId, out var value) && value is int size ? size : ModSettingsCatalog.BattleSizeDefault;
        set => Values[BattleSizeId] = BigBattles.Clamp(value, BattleMode);
    }

    /// <summary>Why the name cannot be used, or "" when it can.</summary>
    public static string NameProblem(string? name, IEnumerable<string> existingSaves)
    {
        var trimmed = (name ?? "").Trim();
        if (trimmed.Length == 0)
            return "Type a name for the world.";
        if (Server.NightlyServer.IsBlank(trimmed))
            return "Pick a campaign name. default_new_game and default_new_game_warsails are the server's templates.";
        if (!SaveSet.IsValidSaveName(trimmed) || trimmed.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || trimmed.Contains('"'))
            return "That save name cannot be used as a file name.";
        if (existingSaves.Contains(trimmed, StringComparer.OrdinalIgnoreCase))
            return "A save named " + trimmed + " already exists. Pick another name.";
        // The wizard always starts a new campaign, which the server start script names by its own rule.
        return SaveSet.NewSaveNameProblem(trimmed) ?? "";
    }

    /// <summary>Why the wizard cannot go on from this step, or "".</summary>
    public string Problem(NewWorldStep step, IEnumerable<string> existingSaves) => step switch
    {
        NewWorldStep.Name => NameProblem(Name, existingSaves),
        NewWorldStep.Difficulty when PresetId == null => "Pick a difficulty.",
        _ => "",
    };

    public static NewWorldStep? Next(NewWorldStep step)
    {
        var index = ((IList<NewWorldStep>)Steps).IndexOf(step);
        return index >= 0 && index < Steps.Count - 1 ? Steps[index + 1] : null;
    }

    public static NewWorldStep? Back(NewWorldStep step)
    {
        var index = ((IList<NewWorldStep>)Steps).IndexOf(step);
        return index > 0 ? Steps[index - 1] : null;
    }
}
