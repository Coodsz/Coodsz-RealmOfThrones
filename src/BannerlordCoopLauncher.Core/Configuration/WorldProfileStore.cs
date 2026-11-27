// Ported from CoopKeep (https://github.com/Aderimo/bannerlord-coopkeep),
// Configuration/WorldProfileStore.cs, translated from Turkish and changed for the Coodsz tools.
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

using System.Text.Json;
using BannerlordCoopLauncher.Core.Saves;

namespace BannerlordCoopLauncher.Core.Configuration;

/// <summary>
/// The gameplay settings of each campaign save.
/// </summary>
/// <remarks>
/// <para>
/// <c>mod-config.json</c> is one shared file, but a host may keep an easy world and a
/// realistic world side by side. So the settings of each save are kept in a file of their
/// own, and the ones for the save being started are written into <c>mod-config.json</c>
/// right before the server starts.
/// </para>
/// <para>
/// The profiles are written next to the tool's own settings, <b>not</b> in the save folder:
/// that folder belongs to the server, and a third file next to the <c>.sav</c> and
/// <c>.json</c> pair could confuse the mod's own tools.
/// </para>
/// </remarks>
public sealed class WorldProfileStore(string directory)
{
    public string Directory { get; } = System.IO.Path.GetFullPath(directory);

    private string PathFor(string saveName)
    {
        if (!SaveSet.IsValidSaveName(saveName))
            throw new ArgumentException("Not a usable save name: '" + saveName + "'.", nameof(saveName));

        return System.IO.Path.Combine(Directory, saveName + ".settings.json");
    }

    public bool Exists(string saveName) => SaveSet.IsValidSaveName(saveName) && File.Exists(PathFor(saveName));

    /// <summary>The settings kept for this save; <see langword="null"/> when it has none. A setting the kept file doesn't
    /// have (one a newer tool added, like Preferred battle hosts) takes its value from <paramref name="missing"/> when given,
    /// usually mod-config.json as it is now, so an older profile never resets a value the host set in that file.</summary>
    public Dictionary<string, object>? Load(string saveName, string? coopVersion = null, IReadOnlyDictionary<string, object>? missing = null)
    {
        if (!SaveSet.IsValidSaveName(saveName)) return null;
        var path = PathFor(saveName);
        if (!File.Exists(path)) return null;

        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(path));
            var values = ModSettingsCatalog.Defaults(coopVersion);
            if (missing != null)
            {
                foreach (var (id, value) in missing)
                {
                    if (values.ContainsKey(id))
                        values[id] = value;
                }
            }

            foreach (var d in ModSettingsCatalog.All)
            {
                if (doc.RootElement.TryGetProperty(d.Id, out var e))
                    values[d.Id] = ModSettingsCatalog.FromJson(d, e);
            }

            return values;
        }
        catch (Exception e) when (e is IOException or JsonException or UnauthorizedAccessException)
        {
            // A damaged profile is ignored; the host can pick the settings again.
            return null;
        }
    }

    public void Save(string saveName, IReadOnlyDictionary<string, object> values)
    {
        System.IO.Directory.CreateDirectory(Directory);

        var normalized = new SortedDictionary<string, object>(StringComparer.Ordinal);
        foreach (var d in ModSettingsCatalog.All)
        {
            values.TryGetValue(d.Id, out var v);
            normalized[d.Id] = ModSettingsCatalog.Normalize(d, v);
        }

        var path = PathFor(saveName);
        var temp = path + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(normalized, new JsonSerializerOptions { WriteIndented = true }));
        File.Move(temp, path, overwrite: true);
    }

    public void Rename(string from, string to)
    {
        var source = PathFor(from);
        if (File.Exists(source)) File.Move(source, PathFor(to), overwrite: true);
    }

    public void Duplicate(string from, string to)
    {
        var source = PathFor(from);
        if (File.Exists(source)) File.Copy(source, PathFor(to), overwrite: true);
    }

    public void Delete(string saveName)
    {
        var path = PathFor(saveName);
        if (File.Exists(path)) File.Delete(path);
    }
}
