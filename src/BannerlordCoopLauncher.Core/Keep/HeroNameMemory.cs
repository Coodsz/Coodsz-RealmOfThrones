using System.Text.Json;

namespace BannerlordCoopLauncher.Core.Keep;

/// <summary>
/// The player heroes' names the server told the host, kept per campaign save in one small file
/// (<c>hero-names.json</c> next to host.json). The save's .json only has hero ids and Steam names,
/// so while the server is stopped the Cheats page shows these names instead of "Player2863".
/// </summary>
public sealed class HeroNameMemory
{
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };
    private readonly string _path;

    public HeroNameMemory(string path) => _path = path;

    /// <summary>Hero id to name for this save; empty when nothing is kept or the file is unreadable.</summary>
    public IReadOnlyDictionary<string, string> For(string? save)
    {
        if (string.IsNullOrWhiteSpace(save))
            return new Dictionary<string, string>(StringComparer.Ordinal);
        return Read().TryGetValue(save, out var names)
            ? new Dictionary<string, string>(names, StringComparer.Ordinal)
            : new Dictionary<string, string>(StringComparer.Ordinal);
    }

    /// <summary>Keeps the names of these heroes for this save; writes the file only when a name changed.</summary>
    public void Remember(string? save, IEnumerable<HeroSummary> heroes)
    {
        if (string.IsNullOrWhiteSpace(save))
            return;
        var all = Read();
        if (!all.TryGetValue(save, out var names))
            all[save] = names = new Dictionary<string, string>(StringComparer.Ordinal);
        var changed = false;
        foreach (var hero in heroes)
        {
            var name = (hero.Name ?? "").Trim();
            if (hero.Id.Length == 0 || name.Length == 0 || (names.TryGetValue(hero.Id, out var kept) && kept == name))
                continue;
            names[hero.Id] = name;
            changed = true;
        }

        if (!changed)
            return;
        try
        {
            var folder = Path.GetDirectoryName(_path);
            if (!string.IsNullOrEmpty(folder))
                Directory.CreateDirectory(folder);
            File.WriteAllText(_path, JsonSerializer.Serialize(all, Options));
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    private Dictionary<string, Dictionary<string, string>> Read()
    {
        try
        {
            if (File.Exists(_path))
                return JsonSerializer.Deserialize<Dictionary<string, Dictionary<string, string>>>(File.ReadAllText(_path))
                       ?? new Dictionary<string, Dictionary<string, string>>(StringComparer.Ordinal);
        }
        catch (Exception e) when (e is IOException or JsonException or UnauthorizedAccessException) { }
        return new Dictionary<string, Dictionary<string, string>>(StringComparer.Ordinal);
    }
}
