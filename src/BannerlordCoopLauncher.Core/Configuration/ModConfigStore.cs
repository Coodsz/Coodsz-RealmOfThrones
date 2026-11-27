// Ported from CoopKeep (https://github.com/Aderimo/bannerlord-coopkeep),
// Configuration/ModConfigStore.cs, translated from Turkish and changed for the Coodsz tools.

using System.Text.Json;

namespace BannerlordCoopLauncher.Core.Configuration;

/// <summary>
/// Reads and changes in place Coop's gameplay settings file, <c>CoopData\mod-config.json</c>.
/// </summary>
/// <remarks>
/// <para>
/// The server's own help text (v0.1.6): <i>"Gameplay settings live in mod-config.json one
/// level up, in CoopData\ itself - shared with player-hosted co-op."</i> The difficulty
/// values are applied by the hosting side <b>on every start</b>, so writing the file right
/// before Start is enough.
/// </para>
/// <para>
/// The game's own Host Co-op button shares the file, so only the settings in
/// <see cref="ModSettingsCatalog"/> are changed; comments, key order and every other key
/// stay as they are.
/// </para>
/// <para>
/// The file has to read as JSON with comments first. A capital <c>True</c> makes the Coop
/// server ignore the whole file, so the TAOM and Coodsz tools run their health check
/// (<c>CoopModConfig.Check</c>) before they write through this store.
/// </para>
/// </remarks>
public sealed class ModConfigStore(string path)
{
    public string Path { get; } = System.IO.Path.GetFullPath(path);

    public bool Exists => File.Exists(Path);

    /// <summary>
    /// Makes the file from the mod's own template (<c>mod-config.default.json</c>) when it
    /// is missing.
    /// </summary>
    /// <remarks>The server makes the same copy on its first start; doing it first means the
    /// host's choices already apply on that first start.</remarks>
    public void EnsureExists(string? templatePath)
    {
        if (Exists) return;

        var directory = System.IO.Path.GetDirectoryName(Path);
        if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);

        if (templatePath is not null && File.Exists(templatePath))
            File.Copy(templatePath, Path);
        else
            File.WriteAllText(Path, "{\n  \"difficulty\": {\n  },\n  \"modOptions\": {\n  },\n  \"voice\": {\n  }\n}\n");
    }

    /// <summary>Every setting in the catalogue; the ones the file does not have get the default of the server's Coop
    /// (coopVersion, ModSettingsCatalog.Defaults), the value that server uses for a missing key.</summary>
    /// <exception cref="InvalidOperationException">The file does not read as JSON with comments.</exception>
    public Dictionary<string, object> Read(string? coopVersion = null) =>
        Exists ? ReadText(File.ReadAllText(Path), coopVersion) : ModSettingsCatalog.Defaults(coopVersion);

    /// <summary><see cref="Read"/> for file text that is already loaded.</summary>
    public static Dictionary<string, object> ReadText(string text, string? coopVersion = null)
    {
        var values = ModSettingsCatalog.Defaults(coopVersion);
        try
        {
            using var doc = JsoncEditor.Parse(text).ToJsonDocument();
            foreach (var d in ModSettingsCatalog.All)
            {
                // Keys match without regard to case, like the server's reader.
                if (Property(doc.RootElement, d.Section) is { ValueKind: JsonValueKind.Object } section &&
                    Property(section, d.Key) is { } value)
                {
                    values[d.Id] = ModSettingsCatalog.FromJson(d, value);
                }
            }
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException("mod-config.json has a mistake on line " + ((ex.LineNumber ?? 0) + 1) + " and cannot be read.", ex);
        }

        return values;
    }

    /// <summary>
    /// Writes the given settings. Only the keys whose value really changes are touched.
    /// </summary>
    /// <param name="coopVersion">The server's Coop version. A key a newer Coop added is only written when that
    /// Coop has it, or when the file already has the key (the server's own template put it there).</param>
    /// <returns>How many settings were changed.</returns>
    public int Write(IReadOnlyDictionary<string, object> values, string? coopVersion = null)
    {
        var text = File.ReadAllText(Path);
        var updated = WriteText(text, values, out var changed, coopVersion);
        if (changed > 0) JsoncEditor.Parse(updated).Save(Path);
        return changed;
    }

    /// <summary><see cref="Write"/> on file text: the changed text and how many settings changed.</summary>
    public static string WriteText(string text, IReadOnlyDictionary<string, object> values, out int changed, string? coopVersion = null)
    {
        var editor = JsoncEditor.Parse(text);
        var current = ReadText(text);
        changed = 0;

        foreach (var (id, value) in values)
        {
            if (ModSettingsCatalog.Find(id) is not { } d) continue;

            // Never a key the server's Coop does not know.
            var present = editor.GetRawValue(d.Section, d.Key) is not null;
            if (!present && !ModSettingsCatalog.Supports(d, coopVersion)) continue;

            // Compared as written, so a list of SteamID64s matches by its entries.
            var normalized = ModSettingsCatalog.Normalize(d, value);
            if (current.TryGetValue(d.Id, out var old) && present &&
                ModSettingsCatalog.ToJsonLiteral(d, old) == ModSettingsCatalog.ToJsonLiteral(d, normalized))
                continue;

            editor.SetRawValue(d.Section, d.Key, ModSettingsCatalog.ToJsonLiteral(d, normalized));
            changed++;
        }

        return editor.Text;
    }

    private static JsonElement? Property(JsonElement parent, string name)
    {
        if (parent.ValueKind != JsonValueKind.Object) return null;
        JsonElement? found = null;
        foreach (var property in parent.EnumerateObject())
        {
            // The last one wins when a key is in the block twice, as with the server's reader.
            if (property.Name.Equals(name, StringComparison.OrdinalIgnoreCase))
                found = property.Value;
        }

        return found;
    }
}
