using System.Text.Json;

namespace TaomCoop.Core;

/// <summary>
/// taom-launcher.json, next to the TAOM client launcher. The fields before ModSetups are the
/// ones every earlier version wrote, with the same names, so an old file loads unchanged.
/// An old file has no ModSetups: it gets Plain TAOM, which is exactly the sealed TAOM stack.
/// </summary>
public sealed class TaomClientSettings
{
    public string GamePath { get; set; } = "";
    public string TaomZip { get; set; } = "";
    public string CompatZip { get; set; } = "";
    public bool UseSteam { get; set; }
    public string SteamName { get; set; } = "";
    public string Host { get; set; } = "";
    public string Port { get; set; } = "4200";
    public string Password { get; set; } = "";
    public bool DllsUnblocked { get; set; }
    public TaomSetupBook ModSetups { get; set; } = new();
    // What this player last joined each host with (JoinMemory), to warn before a different mod list.
    public Dictionary<string, List<string>> LastJoins { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    // Don't ask again on the Set your Coop aside? window: CoopParking.AlwaysAnswer or NeverAnswer, empty to ask.
    public string SetAsideCoop { get; set; } = "";

    private static readonly JsonSerializerOptions ReadOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        AllowTrailingCommas = true,
        ReadCommentHandling = JsonCommentHandling.Skip
    };

    private static readonly JsonSerializerOptions WriteOptions = new() { WriteIndented = true };

    /// <summary>A missing or unreadable file gives the defaults, like before.</summary>
    public static TaomClientSettings Load(string path)
    {
        TaomClientSettings settings;
        try
        {
            settings = File.Exists(path) ? Parse(File.ReadAllText(path)) : new TaomClientSettings();
        }
        catch (IOException)
        {
            settings = new TaomClientSettings();
        }

        settings.ModSetups.Ensure();
        return settings;
    }

    public static TaomClientSettings Parse(string json)
    {
        TaomClientSettings? settings = null;
        try
        {
            if (!string.IsNullOrWhiteSpace(json))
                settings = JsonSerializer.Deserialize<TaomClientSettings>(json, ReadOptions);
        }
        catch (JsonException)
        {
        }

        settings ??= new TaomClientSettings();
        settings.ModSetups ??= new TaomSetupBook();
        settings.LastJoins = new Dictionary<string, List<string>>(settings.LastJoins ?? new Dictionary<string, List<string>>(), StringComparer.OrdinalIgnoreCase);
        settings.ModSetups.Ensure();
        return settings;
    }

    public string Serialize() => JsonSerializer.Serialize(this, WriteOptions);

    public void Save(string path) => File.WriteAllText(path, Serialize());
}
