using System.Text.Json;
using System.Text.Json.Serialization;

namespace BannerlordCoopLauncher.Core.Updates;

/// <summary>A thing a release added, badged NEW until the player tries it.</summary>
public sealed record NewFeature(string Id, string Version);

/// <summary>
/// The NEW badges each tool shows on the things its latest update added. Every feature is
/// tied to the version that brought it, so a later update only badges its own new things.
/// Clicking the badged button marks it seen for good (<see cref="NewFeatureStore"/>).
/// </summary>
public static class NewFeatures
{
    public const string Badge = "NEW";
    public const string BadgeTip = "New, try it";

    // Feature ids. Shared ids (the What's new button, Guides) mean the same thing in every tool.
    public const string WhatsNewButton = "whats-new";
    public const string Guides = "guides";
    public const string GameSpeed = "game-speed";
    public const string Home = "home";
    public const string Players = "players";
    public const string Map = "map";
    public const string Encyclopedia = "encyclopedia";
    public const string Cheats = "cheats";
    public const string StreamerMode = "streamer-mode";
    public const string WorldPresets = "world-presets";
    public const string CreateWorld = "create-world";
    public const string JoinHelp = "join-help";
    public const string SaveLogs = "save-logs";
    public const string RbmSettings = "rbm-settings";
    public const string RbmCoopFriendly = "rbm-coop-friendly";
    public const string ClanCap = "clan-cap";

    // v1.2.53, v1.1.33 and v1.0.12.
    private const string Friends53 = "v1.2.53";
    private const string Taom33 = "v1.1.33";
    private const string AD1259v12 = "v1.0.12";

    // v1.2.55 and v1.0.14: the RBM settings card on Coodsz Host's Settings page, and Keep RBM coop
    // friendly in Coop and Friends and the AD1259 client launcher.
    private const string Friends55 = "v1.2.55";
    private const string AD1259v14 = "v1.0.14";

    // v1.2.57: Max clans per kingdom on Coodsz Host's Settings page.
    private const string Friends57 = "v1.2.57";

    // v1.0.0: the first Burning Empires tools, so every badge they carry over from TAOM is new.
    private const string Be100 = "v1.0.0";

    /// <summary>Coodsz Host (the AD1259 server tool uses <see cref="AD1259"/>).</summary>
    public static readonly IReadOnlyList<NewFeature> CoodszHost =
    [
        new(Home, Friends53), new(Players, Friends53), new(Map, Friends53), new(Encyclopedia, Friends53),
        new(Guides, Friends53), new(Cheats, Friends53), new(StreamerMode, Friends53), new(GameSpeed, Friends53),
        new(WorldPresets, Friends53), new(CreateWorld, Friends53), new(WhatsNewButton, Friends53),
        new(RbmSettings, Friends55),
        new(ClanCap, Friends57),
    ];

    public static readonly IReadOnlyList<NewFeature> Friends =
    [
        new(WhatsNewButton, Friends53), new(JoinHelp, Friends53), new(SaveLogs, Friends53),
        new(RbmCoopFriendly, Friends55),
    ];

    public static readonly IReadOnlyList<NewFeature> TaomServer =
    [
        new(Guides, Taom33), new(GameSpeed, Taom33), new(WhatsNewButton, Taom33),
    ];

    public static readonly IReadOnlyList<NewFeature> TaomLauncher =
    [
        new(Guides, Taom33), new(WhatsNewButton, Taom33),
    ];

    /// <summary>Both AD1259 tools: the What's new button, and Keep RBM coop friendly in the client launcher.</summary>
    public static readonly IReadOnlyList<NewFeature> AD1259 =
    [
        new(WhatsNewButton, AD1259v12), new(RbmCoopFriendly, AD1259v14),
    ];

    public static readonly IReadOnlyList<NewFeature> BeServer =
    [
        new(Guides, Be100), new(GameSpeed, Be100), new(WhatsNewButton, Be100),
    ];

    public static readonly IReadOnlyList<NewFeature> BeLauncher =
    [
        new(Guides, Be100), new(WhatsNewButton, Be100),
    ];

    /// <summary>The list for a tool, by the name it uses for its What's new notes.</summary>
    public static IReadOnlyList<NewFeature> For(string tool) => tool switch
    {
        "coodsz-host" => CoodszHost,
        "friends-launcher" => Friends,
        "taom-server" => TaomServer,
        "taom-launcher" => TaomLauncher,
        "be-server" => BeServer,
        "be-launcher" => BeLauncher,
        "ad1259-server" or "ad1259-launcher" => AD1259,
        _ => [],
    };
}

/// <summary>
/// Which NEW badges a tool shows, kept in %AppData%\Coodsz\new-features-&lt;tool&gt;.json next
/// to the What's new files. It keeps the ids the player tried and the version the tool ran
/// before this one: a feature is badged while it is not tried and came after that version.
/// A fresh install (or the first version with badges) badges this version's own features.
/// </summary>
public sealed class NewFeatureStore
{
    private sealed class State
    {
        [JsonPropertyName("version")] public string? Version { get; set; }
        [JsonPropertyName("previous")] public string? Previous { get; set; }
        [JsonPropertyName("seen")] public List<string> Seen { get; set; } = new();
    }

    private readonly string _path;
    private readonly State _state;
    private readonly IReadOnlyList<NewFeature> _features;

    private NewFeatureStore(string path, State state, IReadOnlyList<NewFeature> features)
    {
        _path = path;
        _state = state;
        _features = features;
    }

    /// <summary>The version this tool runs now.</summary>
    public string Version => _state.Version ?? "";

    /// <summary>The version the tool ran before this one, or null on a fresh install.</summary>
    public string? Previous => _state.Previous;

    public static string PathFor(string tool, string? folder = null) =>
        System.IO.Path.Combine(folder ?? WhatsNew.SettingsFolder, "new-features-" + tool + ".json");

    /// <summary>Reads the tool's file and moves it on to <paramref name="version"/> when the tool was updated.</summary>
    public static NewFeatureStore Load(string tool, string version, IReadOnlyList<NewFeature> features, string? folder = null)
    {
        var path = PathFor(tool, folder);
        var state = Read(path) ?? new State();
        if (!SameVersion(state.Version, version))
        {
            state.Previous = state.Version;
            state.Version = version;
            var store = new NewFeatureStore(path, state, features);
            store.Save();
            return store;
        }

        return new NewFeatureStore(path, state, features);
    }

    /// <summary>True when the badge for <paramref name="id"/> shows.</summary>
    public bool Shows(string id)
    {
        var feature = _features.FirstOrDefault(f => f.Id == id);
        return feature != null && Shows(feature, Version, Previous, _state.Seen.Contains(id));
    }

    /// <summary>The badge rule on its own: not tried, not newer than this copy, and new since the version before.</summary>
    public static bool Shows(NewFeature feature, string version, string? previous, bool seen)
    {
        if (seen || ReleaseNotice.IsNewer(feature.Version, version))
            return false;
        return string.IsNullOrWhiteSpace(previous)
            ? SameVersion(feature.Version, version)
            : ReleaseNotice.IsNewer(feature.Version, previous);
    }

    public bool IsSeen(string id) => _state.Seen.Contains(id);

    /// <summary>The player tried it: its badge never shows again.</summary>
    public void MarkSeen(string id)
    {
        if (_state.Seen.Contains(id))
            return;
        _state.Seen.Add(id);
        Save();
    }

    private static bool SameVersion(string? left, string right) =>
        left != null && string.Equals(left.Trim(), right.Trim(), StringComparison.OrdinalIgnoreCase);

    private static State? Read(string path)
    {
        try
        {
            if (!File.Exists(path))
                return null;
            var state = JsonSerializer.Deserialize<State>(File.ReadAllText(path));
            if (state != null)
                state.Seen ??= new List<string>();
            return state;
        }
        catch (IOException) { return null; }
        catch (UnauthorizedAccessException) { return null; }
        catch (JsonException) { return null; }
    }

    private void Save()
    {
        try
        {
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(_path)!);
            var temp = _path + ".tmp";
            File.WriteAllText(temp, JsonSerializer.Serialize(_state, new JsonSerializerOptions { WriteIndented = true }));
            File.Move(temp, _path, overwrite: true);
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}
