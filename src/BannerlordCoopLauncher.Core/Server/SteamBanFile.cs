// Adapted from CoopKeep (https://github.com/Aderimo/bannerlord-coopkeep), Server/SteamBanList.cs
// (the SteamID64 check and the write through a temporary file), changed for the Coodsz tools.

using System.Text;
using System.Text.Json;

namespace BannerlordCoopLauncher.Core.Server;

/// <summary>
/// The ban list the Launcher Cheats submodule reads: server-data\steam-bans.json, a plain
/// JSON array of SteamID64 strings. Coodsz Host has always written it that way and the
/// submodule refuses those players when they join, so the format stays.
/// </summary>
public static class SteamBanFile
{
    public const string FileName = "steam-bans.json";

    private static readonly JsonSerializerOptions WriteOptions = new() { WriteIndented = true };

    /// <summary>
    /// A SteamID64 of a person's account: 17 digits that start with 7656119. Anything else
    /// (a peer index, a name, a short number) can never match a Steam player.
    /// </summary>
    public static bool IsSteamId64(string? id) =>
        id is { Length: 17 }
        && id.StartsWith("7656119", StringComparison.Ordinal)
        && id.All(char.IsAsciiDigit);

    /// <summary>
    /// The SteamID64s in the file. Reads the plain array and also Coop's own
    /// <c>{"steamIds":[...]}</c> shape. A missing or broken file gives an empty list.
    /// </summary>
    public static IReadOnlyList<string> Read(string path)
    {
        if (!File.Exists(path))
            return Array.Empty<string>();
        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(path), new JsonDocumentOptions
            {
                AllowTrailingCommas = true,
                CommentHandling = JsonCommentHandling.Skip
            });
            var list = doc.RootElement.ValueKind switch
            {
                JsonValueKind.Array => doc.RootElement,
                JsonValueKind.Object when doc.RootElement.TryGetProperty("steamIds", out var ids) && ids.ValueKind == JsonValueKind.Array => ids,
                _ => default
            };
            if (list.ValueKind != JsonValueKind.Array)
                return Array.Empty<string>();
            return list.EnumerateArray()
                .Select(item => item.ValueKind switch
                {
                    JsonValueKind.String => item.GetString()?.Trim(),
                    JsonValueKind.Number => item.GetRawText(),
                    _ => null
                })
                .Where(IsSteamId64)
                .Select(id => id!)
                .Distinct(StringComparer.Ordinal)
                .ToList();
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            return Array.Empty<string>();
        }
    }

    /// <summary>
    /// Writes the list as a plain JSON array. Only real SteamID64s are written, once each.
    /// The file is written next to the list first and then moved over it, so the server
    /// never reads a half written list.
    /// </summary>
    public static void Write(string path, IEnumerable<string> ids)
    {
        var clean = ids.Select(id => id.Trim()).Where(IsSteamId64).Distinct(StringComparer.Ordinal).ToList();
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        var temp = path + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(clean, WriteOptions), new UTF8Encoding(false));
        File.Move(temp, path, overwrite: true);
    }
}
