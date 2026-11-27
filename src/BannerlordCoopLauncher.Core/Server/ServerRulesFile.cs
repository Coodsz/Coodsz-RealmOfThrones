using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace BannerlordCoopLauncher.Core.Server;

/// <summary>Who may use coop.unstuck: <c>allowed</c> (Coop's own behaviour), <c>notInFights</c> or <c>hostOnly</c>.</summary>
public enum UnstuckChoice
{
    Allowed,
    NotInFights,
    HostOnly,
}

/// <summary>
/// Protect player kingdoms from AI wars, in the order of the dropdown. <see cref="OfflineOnly"/> and
/// <see cref="Off"/> are Coop's own modOptions.blockAiWarDeclarationsOnOfflinePlayers (on and off);
/// <see cref="Always"/> is the Coodsz Server Tools module, with Coop's option on as well.
/// </summary>
public enum KingdomProtectionChoice
{
    OfflineOnly,
    Always,
    Off,
}

/// <summary>
/// The host's server rules. The defaults are what a host gets without opening Advanced. A null
/// <see cref="KingdomProtection"/> means the host never picked one: the dropdown follows Coop's own
/// modOptions.blockAiWarDeclarationsOnOfflinePlayers and nothing changes it (<see cref="ServerRulesFile.ShownKingdomChoice"/>).
/// </summary>
public sealed record HostServerRules(UnstuckChoice Unstuck, bool PauseOnJoin, bool FreezeOfflineXp,
    KingdomProtectionChoice? KingdomProtection = null)
{
    public static readonly HostServerRules Defaults = new(UnstuckChoice.Allowed, true, true, null);
}

/// <summary>
/// The server rules in coodsz-server-tools.json, next to the clan cap (<see cref="ClanCapFile"/>):
/// <c>{ "unstuck": "allowed", "pauseOnJoin": true, "freezeOfflineXp": true, "kingdomProtection": "offlineOnly" }</c>.
/// The Coodsz Server Tools module reads unstuck, freezeOfflineXp and kingdomProtection
/// (compat\CoodszServerTools\src\ServerRulesRules.cs) within a few seconds; pauseOnJoin is read by Coodsz
/// Host itself. A kingdomProtection the host picked also sets Coop's own <see cref="CoopOfflineWarOption"/>
/// in mod-config.json; a missing or unknown one leaves that option as each host already has it.
/// </summary>
public static class ServerRulesFile
{
    public const string UnstuckKey = "unstuck";
    public const string PauseOnJoinKey = "pauseOnJoin";
    public const string FreezeXpKey = "freezeOfflineXp";
    public const string KingdomProtectionKey = "kingdomProtection";

    /// <summary>Coop's own offline war protection, the setting the first and last kingdom choices map onto.</summary>
    public const string CoopOfflineWarOption = "modOptions.blockAiWarDeclarationsOnOfflinePlayers";

    /// <summary>The dropdown on the Settings page, in the order of <see cref="UnstuckChoice"/>.</summary>
    public static readonly IReadOnlyList<string> UnstuckLabels =
    [
        "Allowed",
        "Not in fights, as a prisoner or near enemies",
        "Host only",
    ];

    /// <summary>The kingdom protection dropdown, in the order of <see cref="KingdomProtectionChoice"/>.</summary>
    public static readonly IReadOnlyList<string> KingdomProtectionLabels =
    [
        "Only while their players are offline",
        "Always",
        "Off",
    ];

    public static string ValueOf(KingdomProtectionChoice choice) => choice switch
    {
        KingdomProtectionChoice.Always => "always",
        KingdomProtectionChoice.Off => "off",
        _ => "offlineOnly",
    };

    /// <summary>The host's pick, or null for a missing or unknown value (follow Coop's own option).</summary>
    public static KingdomProtectionChoice? KingdomChoiceOf(string? value) => value?.Trim().ToLowerInvariant() switch
    {
        "offlineonly" => KingdomProtectionChoice.OfflineOnly,
        "always" => KingdomProtectionChoice.Always,
        "off" => KingdomProtectionChoice.Off,
        _ => null,
    };

    /// <summary>
    /// What the dropdown shows: the host's pick, or with none, Coop's own option as this host already has it
    /// (on gives Only while their players are offline, off or missing gives Off). So an update changes nothing.
    /// </summary>
    public static KingdomProtectionChoice ShownKingdomChoice(KingdomProtectionChoice? picked, bool coopOption) =>
        picked ?? (coopOption ? KingdomProtectionChoice.OfflineOnly : KingdomProtectionChoice.Off);

    /// <summary>
    /// What Save settings keeps: null (still following Coop's option) while the host has no pick and leaves the
    /// dropdown on what it showed, otherwise the dropdown's choice.
    /// </summary>
    public static KingdomProtectionChoice? KingdomChoiceToSave(KingdomProtectionChoice onPage, KingdomProtectionChoice? picked, bool coopOption) =>
        picked == null && onPage == ShownKingdomChoice(null, coopOption) ? null : onPage;

    /// <summary>Coop's option in a set of catalogue values; off when missing, like Coop's own default.</summary>
    public static bool CoopOfflineWarIn(IReadOnlyDictionary<string, object> values) =>
        values.TryGetValue(CoopOfflineWarOption, out var value) && value is true;

    /// <summary>
    /// The value of Coop's <see cref="CoopOfflineWarOption"/> for a choice: on for Only while offline and for
    /// Always (Coop's check also covers calls to war and players with no kingdom while they are offline),
    /// off for Off.
    /// </summary>
    public static bool CoopOfflineWarValue(KingdomProtectionChoice choice) => choice != KingdomProtectionChoice.Off;

    private static readonly JsonDocumentOptions ReadOptions = new() { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip };

    public static string ValueOf(UnstuckChoice choice) => choice switch
    {
        UnstuckChoice.NotInFights => "notInFights",
        UnstuckChoice.HostOnly => "hostOnly",
        _ => "allowed",
    };

    public static UnstuckChoice ChoiceOf(string? value) => value?.Trim().ToLowerInvariant() switch
    {
        "notinfights" => UnstuckChoice.NotInFights,
        "hostonly" => UnstuckChoice.HostOnly,
        _ => UnstuckChoice.Allowed,
    };

    /// <summary>The rules in the file. A missing key, or a missing or broken file, keeps the default.</summary>
    public static HostServerRules Read(string path)
    {
        try
        {
            if (!File.Exists(path) || JsonNode.Parse(File.ReadAllText(path), documentOptions: ReadOptions) is not JsonObject root)
                return HostServerRules.Defaults;
            var unstuck = root[UnstuckKey] is JsonValue text && text.TryGetValue<string>(out var value) ? ChoiceOf(value) : UnstuckChoice.Allowed;
            var kingdoms = root[KingdomProtectionKey] is JsonValue kingdomText && kingdomText.TryGetValue<string>(out var kingdomValue)
                ? KingdomChoiceOf(kingdomValue)
                : null;
            return new HostServerRules(unstuck, Flag(root, PauseOnJoinKey), Flag(root, FreezeXpKey), kingdoms);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or InvalidOperationException)
        {
            return HostServerRules.Defaults;
        }
    }

    private static bool Flag(JsonObject root, string key) =>
        root[key] is not JsonValue value || !value.TryGetValue<bool>(out var on) || on;

    /// <summary>Writes the rules' keys and keeps every other key (the clan cap) in the file, through a temporary file.</summary>
    public static void Write(string path, HostServerRules rules)
    {
        JsonObject root;
        try
        {
            root = File.Exists(path) && JsonNode.Parse(File.ReadAllText(path), documentOptions: ReadOptions) is JsonObject read ? read : new JsonObject();
        }
        catch (JsonException)
        {
            root = new JsonObject();
        }

        root[UnstuckKey] = ValueOf(rules.Unstuck);
        root[PauseOnJoinKey] = rules.PauseOnJoin;
        root[FreezeXpKey] = rules.FreezeOfflineXp;
        // No pick stays no pick: the key is left out, so Coop's own option is followed.
        if (rules.KingdomProtection is { } kingdoms)
            root[KingdomProtectionKey] = ValueOf(kingdoms);
        else
            root.Remove(KingdomProtectionKey);
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        var temp = path + ".tmp";
        File.WriteAllText(temp, root.ToJsonString(new JsonSerializerOptions { WriteIndented = true }) + Environment.NewLine, new UTF8Encoding(false));
        File.Move(temp, path, overwrite: true);
    }
}
