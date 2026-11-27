using System.Text.Json;

namespace RealmOfThrones.Core;

/// <summary>
/// rot-server.json, in the Realm of Thrones server tool's data folder. The fields match the Burning
/// Empires server tool's HostSettings without its Nexus archives. It holds the join password, so it
/// never goes into a shared file. A missing, old or damaged file gets the defaults with the one locked
/// Realm of Thrones setup.
/// </summary>
public sealed class RotServerSettings
{
    public const string FileName = "rot-server.json";

    public string ServerRoot { get; set; } = "";
    // A Coop dedicated server the host unzipped himself (Choose Coop server). Empty: the Workshop.
    public string CoopServerFolder { get; set; } = "";
    public string DataRoot { get; set; } = "";
    // The Bannerlord folder picked with Find Bannerlord; empty when Steam finds it.
    public string GamePath { get; set; } = "";
    public string SaveFile { get; set; } = "";
    // The save the server last ran from a Start, for the older save question at Start.
    public string LastHostedSave { get; set; } = "";
    public string Password { get; set; } = "";
    public string? ServerAddress { get; set; }
    public int AutosaveMinutes { get; set; } = 15;
    public bool SteamRelay { get; set; }
    public string? AcceptedEulaSha256 { get; set; }
    // The mod setup players should join with: the locked Realm of Thrones setup or one of the host's own.
    public RotSetupBook ModSetups { get; set; } = new();
    // A new campaign under this name on the next start (no .sav needed). Empty starts the picked save.
    public string NewCampaignName { get; set; } = "";
    // Open the server's UDP port on the router (UPnP or NAT PMP) while the server runs, like Coodsz Host.
    public bool OpenRouterPort { get; set; } = true;
    // Advertise through Steam was turned on once for a shared address (CGNAT); after that the host's own choice stays.
    public bool SteamLobbyAutoOn { get; set; }
    // The starred cheats of the Cheats page (their ids) and the last commands sent from the Console page.
    public List<string> FavouriteCheats { get; set; } = new();
    public List<string> RecentCommands { get; set; } = new();
    // The server folder whose server once reached SERVING, so Windows has asked about .NET Host for it.
    public string FirewallHintDoneFor { get; set; } = "";

    /// <summary>A missing or unreadable file gives the defaults.</summary>
    public static RotServerSettings Load(string path)
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

    public static RotServerSettings Parse(string json)
    {
        RotServerSettings? settings = null;
        try
        {
            if (!string.IsNullOrWhiteSpace(json))
                settings = JsonSerializer.Deserialize<RotServerSettings>(json, RotClientSettings.ReadOptions);
        }
        catch (JsonException)
        {
        }

        settings ??= new RotServerSettings();
        settings.ModSetups ??= new RotSetupBook();
        settings.FavouriteCheats ??= new List<string>();
        settings.RecentCommands ??= new List<string>();
        settings.ModSetups.Ensure();
        return settings;
    }

    public string Serialize() => JsonSerializer.Serialize(this, RotClientSettings.WriteOptions);

    public void Save(string path) => File.WriteAllText(path, Serialize());
}
