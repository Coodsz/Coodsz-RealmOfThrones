using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace BannerlordCoopLauncher.Core.Server;

/// <summary>
/// The PvP rules: block the caught player's Leave in an encounter between enemy players, and end an
/// encounter nobody answers within <see cref="Seconds"/>. Both off by default.
/// </summary>
public sealed record HostPvpRules(bool BlockLeave, bool ResponseTimer, int Seconds)
{
    public static readonly HostPvpRules Defaults = new(false, false, PvpRulesFile.DefaultSeconds);
}

/// <summary>
/// The PvP rules in coodsz-server-tools.json, next to the clan cap and the server rules:
/// <c>{ "pvpBlockLeave": false, "pvpResponseTimer": false, "pvpResponseSeconds": 30 }</c>. The Coodsz
/// Server Tools module reads them within a few seconds (compat\CoodszServerTools\src\PvpRulesRules.cs).
/// </summary>
public static class PvpRulesFile
{
    public const string BlockLeaveKey = "pvpBlockLeave";
    public const string TimerKey = "pvpResponseTimer";
    public const string SecondsKey = "pvpResponseSeconds";

    public const int DefaultSeconds = 30;
    public const int FewestSeconds = 10;
    public const int MostSeconds = 300;

    public const string BadSeconds = "Seconds to answer must be a whole number from 10 to 300.";

    private static readonly JsonDocumentOptions ReadOptions = new() { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip };

    /// <summary>The rules in the file. A missing key, or a missing or broken file, keeps the default (off, 30 seconds).</summary>
    public static HostPvpRules Read(string path)
    {
        try
        {
            if (!File.Exists(path) || JsonNode.Parse(File.ReadAllText(path), documentOptions: ReadOptions) is not JsonObject root)
                return HostPvpRules.Defaults;
            var seconds = root[SecondsKey] is JsonValue value && value.TryGetValue<int>(out var number) ? number : DefaultSeconds;
            return new HostPvpRules(Flag(root, BlockLeaveKey), Flag(root, TimerKey), Math.Clamp(seconds, FewestSeconds, MostSeconds));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or InvalidOperationException)
        {
            return HostPvpRules.Defaults;
        }
    }

    private static bool Flag(JsonObject root, string key) =>
        root[key] is JsonValue value && value.TryGetValue<bool>(out var on) && on;

    /// <summary>Writes the three keys and keeps every other key (the clan cap, the server rules) in the file, through a temporary file.</summary>
    public static void Write(string path, HostPvpRules rules)
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

        root[BlockLeaveKey] = rules.BlockLeave;
        root[TimerKey] = rules.ResponseTimer;
        root[SecondsKey] = Math.Clamp(rules.Seconds, FewestSeconds, MostSeconds);
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        var temp = path + ".tmp";
        File.WriteAllText(temp, root.ToJsonString(new JsonSerializerOptions { WriteIndented = true }) + Environment.NewLine, new UTF8Encoding(false));
        File.Move(temp, path, overwrite: true);
    }

    /// <summary>The number in the Settings page box: empty is the default, 30.</summary>
    public static int ParseBox(string? text)
    {
        var clean = (text ?? "").Trim();
        if (clean.Length == 0)
            return DefaultSeconds;
        if (!int.TryParse(clean, NumberStyles.None, CultureInfo.InvariantCulture, out var value) || value < FewestSeconds || value > MostSeconds)
            throw new InvalidOperationException(BadSeconds);
        return value;
    }
}
