using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace BannerlordCoopLauncher.Core.Server;

/// <summary>Max clans per kingdom: 0 is no limit. The ruling clan counts; mercenaries only when asked.</summary>
public sealed record ClanCap(int MaxClans, bool CountMercenaries)
{
    public static readonly ClanCap Off = new(0, false);
    public bool Enabled => MaxClans > 0;
}

/// <summary>
/// The settings file the Coodsz Server Tools module reads on the dedicated server
/// (compat\CoodszServerTools\src\ClanCapRules.cs): coodsz-server-tools.json next to Coop's
/// server-config.json, <c>{ "maxClansPerKingdom": 0, "countMercenaries": false }</c>. Hosts can
/// edit it by hand too; the module reads it again within a few seconds of a change.
/// </summary>
public static class ClanCapFile
{
    public const string FileName = "coodsz-server-tools.json";
    public const string MaxKey = "maxClansPerKingdom";
    public const string MercenaryKey = "countMercenaries";
    public const int Highest = 500;

    public const string BadNumber = "Max clans per kingdom must be a whole number from 0 to 500. 0 means no limit.";

    private static readonly JsonDocumentOptions ReadOptions = new() { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip };

    public static string PathIn(string serverConfigFolder) => Path.Combine(serverConfigFolder, FileName);

    /// <summary>The cap in the file. A missing or broken file is no limit, like the module reads it.</summary>
    public static ClanCap Read(string path)
    {
        try
        {
            if (!File.Exists(path) || JsonNode.Parse(File.ReadAllText(path), documentOptions: ReadOptions) is not JsonObject root)
                return ClanCap.Off;
            var max = root[MaxKey] is JsonValue value && value.TryGetValue<int>(out var number) ? number : 0;
            var mercenaries = root[MercenaryKey] is JsonValue flag && flag.TryGetValue<bool>(out var on) && on;
            return new ClanCap(Math.Clamp(max, 0, Highest), mercenaries);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or InvalidOperationException)
        {
            return ClanCap.Off;
        }
    }

    /// <summary>Writes the two keys and keeps any other key in the file, through a temporary file.</summary>
    public static void Write(string path, ClanCap cap)
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

        root[MaxKey] = Math.Clamp(cap.MaxClans, 0, Highest);
        root[MercenaryKey] = cap.CountMercenaries;
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        var temp = path + ".tmp";
        File.WriteAllText(temp, root.ToJsonString(new JsonSerializerOptions { WriteIndented = true }) + Environment.NewLine, new UTF8Encoding(false));
        File.Move(temp, path, overwrite: true);
    }

    /// <summary>The number in the Settings page box: empty is 0 (no limit).</summary>
    public static int ParseBox(string? text)
    {
        var clean = (text ?? "").Trim();
        if (clean.Length == 0)
            return 0;
        if (!int.TryParse(clean, NumberStyles.None, CultureInfo.InvariantCulture, out var value) || value > Highest)
            throw new InvalidOperationException(BadNumber);
        return value;
    }
}
