using System.Text.Json;

namespace RealmOfThrones.Core;

/// <summary>
/// rot-launcher.json, in the Realm of Thrones client launcher's data folder. The fields match the
/// Burning Empires launcher's without its Nexus archives. A missing, old or damaged file gets the
/// defaults with the one locked Realm of Thrones setup.
/// </summary>
public sealed class RotClientSettings
{
    public const string FileName = "rot-launcher.json";

    public string GamePath { get; set; } = "";
    public bool UseSteam { get; set; }
    public string SteamName { get; set; } = "";
    public string Host { get; set; } = "";
    public string Port { get; set; } = RotStack.DirectPort;
    public string Password { get; set; } = "";
    public bool DllsUnblocked { get; set; }
    public RotSetupBook ModSetups { get; set; } = new();
    // What this player last joined each host with (JoinMemory), to warn before a different mod list.
    public Dictionary<string, List<string>> LastJoins { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    // The mods each host's server turned the last join down for (RotJoinCheck.RequiredByServer), so Connect stops
    // before the game starts while this setup still leaves them out.
    public Dictionary<string, List<string>> ServerNeeds { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    // The Don't ask again answer to setting another Coop aside at Connect (CoopParking.AlwaysAnswer or NeverAnswer), else ask.
    public string SetAsideCoop { get; set; } = "";

    internal static readonly JsonSerializerOptions ReadOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        AllowTrailingCommas = true,
        ReadCommentHandling = JsonCommentHandling.Skip
    };

    internal static readonly JsonSerializerOptions WriteOptions = new() { WriteIndented = true };

    /// <summary>A missing or unreadable file gives the defaults.</summary>
    public static RotClientSettings Load(string path)
    {
        try
        {
            return File.Exists(path) ? Parse(File.ReadAllText(path)) : Parse("");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return Parse("");
        }
    }

    public static RotClientSettings Parse(string json)
    {
        RotClientSettings? settings = null;
        try
        {
            if (!string.IsNullOrWhiteSpace(json))
                settings = JsonSerializer.Deserialize<RotClientSettings>(json, ReadOptions);
        }
        catch (JsonException)
        {
        }

        settings ??= new RotClientSettings();
        settings.ModSetups ??= new RotSetupBook();
        settings.LastJoins = new Dictionary<string, List<string>>(settings.LastJoins ?? new Dictionary<string, List<string>>(), StringComparer.OrdinalIgnoreCase);
        settings.ServerNeeds = new Dictionary<string, List<string>>(settings.ServerNeeds ?? new Dictionary<string, List<string>>(), StringComparer.OrdinalIgnoreCase);
        settings.ModSetups.Ensure();
        return settings;
    }

    public string Serialize() => JsonSerializer.Serialize(this, WriteOptions);

    public void Save(string path) => File.WriteAllText(path, Serialize());
}
