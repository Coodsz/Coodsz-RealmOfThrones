using System.Text.Json;

namespace BannerlordCoopLauncher.Core.Models;

public sealed class LauncherSettings
{
    public string GamePath { get; set; } = "";
    public string ProfileUrl { get; set; } = ProfileJson.DefaultProfileUrl;
    public string SteamHostName { get; set; } = "";
    public bool UseDirectConnection { get; set; }
    public string DirectHost { get; set; } = "";
    public string DirectPort { get; set; } = "";
    public string ServerPassword { get; set; } = "";
    public string NexusApiKey { get; set; } = "";
    public string CampaignMap { get; set; } = "";
    public List<string> DisabledModules { get; set; } = new();
    public string ActiveSetup { get; set; } = "Default";
    public List<ClientSetup> Setups { get; set; } = new();
    public bool StartSteamAutomatically { get; set; } = true;
    public int SteamStartupTimeoutSeconds { get; set; } = 120;
    // Connect turns off RBM's Lords buy equipment in this PC's RBM settings while RBM is in the setup.
    public bool KeepRbmCoopFriendly { get; set; } = true;
    // The More Nations switch. On, a setup with no map saved uses the More Nations Remastered Map, the
    // way the Coodsz and Friends build did. Off, the Vanilla campaign Map. Players of Coodsz and Friends
    // get it on when their settings fold in (MoreNationsFoldIn).
    public bool MoreNations { get; set; }

    /// <summary>The map a setup with no map saved uses.</summary>
    public string DefaultMap() => MoreNations ? StableClientSetup.MoreNationsMap : StableClientSetup.VanillaMap;

    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true
    };

    public static LauncherSettings Load(string path)
    {
        if (!File.Exists(path))
            return new LauncherSettings();

        var json = File.ReadAllText(path);
        if (string.IsNullOrWhiteSpace(json))
            throw new ProfileException("Empty launcher settings.");

        var settings = JsonSerializer.Deserialize<LauncherSettings>(json, Options) ?? new LauncherSettings();
        settings.EnsureSetups();
        return settings;
    }

    public ClientSetup Current()
    {
        EnsureSetups();
        var setup = Setups.FirstOrDefault(item => item.Name.Equals(ActiveSetup, StringComparison.OrdinalIgnoreCase));
        if (setup != null)
            return setup;
        setup = Setups[0];
        ActiveSetup = setup.Name;
        return setup;
    }

    public void EnsureSetups()
    {
        if (Setups.Count == 0)
        {
            Setups.Add(new ClientSetup
            {
                Name = string.IsNullOrWhiteSpace(ActiveSetup) ? "Default" : ActiveSetup,
                CampaignMap = CampaignMap,
                DisabledModules = DisabledModules.ToList(),
                UseDirectConnection = UseDirectConnection,
                SteamHostName = SteamHostName,
                DirectHost = DirectHost,
                DirectPort = DirectPort,
                ServerPassword = ServerPassword
            });
        }

        foreach (var (from, to) in StableClientSetup.Renames)
            RenameSetup(from, to);

        var vanilla = LockSetup(StableClientSetup.VanillaName, StableClientSetup.VanillaMap);
        var nations = LockSetup(StableClientSetup.MoreNationsName, StableClientSetup.MoreNationsMap);
        var plain = LockSetup(StableClientSetup.PlainName, StableClientSetup.VanillaMap);
        PlaceAfterDefault(vanilla, nations, plain);

        if (string.IsNullOrWhiteSpace(ActiveSetup) || Setups.All(item => !item.Name.Equals(ActiveSetup, StringComparison.OrdinalIgnoreCase)))
            ActiveSetup = Setups[0].Name;
    }

    private void RenameSetup(string from, string to)
    {
        var source = Setups.FirstOrDefault(item => item.Name.Equals(from, StringComparison.OrdinalIgnoreCase));
        if (source == null)
            return;
        var target = Setups.FirstOrDefault(item => item.Name.Equals(to, StringComparison.OrdinalIgnoreCase));
        if (target != null && !ReferenceEquals(source, target))
        {
            if (ActiveSetup.Equals(source.Name, StringComparison.OrdinalIgnoreCase))
                ActiveSetup = to;
            Setups.Remove(source);
            return;
        }

        if (ActiveSetup.Equals(source.Name, StringComparison.OrdinalIgnoreCase))
            ActiveSetup = to;
        source.Name = to;
    }

    private ClientSetup LockSetup(string name, string campaignMap)
    {
        var setup = Setups.FirstOrDefault(item => item.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
        if (setup == null)
        {
            setup = new ClientSetup { Name = name };
            Setups.Add(setup);
        }

        setup.Name = name;
        setup.Locked = true;
        setup.CampaignMap = campaignMap;
        setup.DisabledModules.Clear();
        setup.CustomMods.Clear();
        setup.ModuleOrder.Clear();
        return setup;
    }

    private void PlaceAfterDefault(params ClientSetup[] locked)
    {
        foreach (var setup in locked)
            Setups.Remove(setup);
        var index = Setups.FindIndex(item => item.Name.Equals("Default", StringComparison.OrdinalIgnoreCase));
        var insertAt = index >= 0 ? index + 1 : 0;
        foreach (var setup in locked)
            Setups.Insert(insertAt++, setup);
    }

    public string NextSetupName()
    {
        var number = 2;
        while (Setups.Any(item => item.Name.Equals("Setup " + number, StringComparison.OrdinalIgnoreCase)))
            number++;
        return "Setup " + number;
    }

    public void Save(string path)
    {
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);
        File.WriteAllText(path, JsonSerializer.Serialize(this, Options));
    }
}

public sealed class ClientSetup
{
    public string Name { get; set; } = "Default";
    public string CampaignMap { get; set; } = "";
    public List<string> DisabledModules { get; set; } = new();
    public bool UseDirectConnection { get; set; }
    public string SteamHostName { get; set; } = "";
    public string DirectHost { get; set; } = "";
    public string DirectPort { get; set; } = "";
    public bool Locked { get; set; }
    public List<CustomMod> CustomMods { get; set; } = new();
    public List<string> ModuleOrder { get; set; } = new();
    public List<string> RemovedModules { get; set; } = new();
    public string ServerPassword { get; set; } = "";

    public ClientSetup Copy(string name) => new()
    {
        Name = name,
        CampaignMap = CampaignMap,
        DisabledModules = DisabledModules.ToList(),
        UseDirectConnection = UseDirectConnection,
        SteamHostName = SteamHostName,
        DirectHost = DirectHost,
        DirectPort = DirectPort,
        ServerPassword = ServerPassword,
        Locked = false,
        CustomMods = CustomMods.Select(mod => new CustomMod { Name = mod.Name, ModuleId = mod.ModuleId, WorkshopId = mod.WorkshopId }).ToList(),
        ModuleOrder = ModuleOrder.ToList(),
        RemovedModules = RemovedModules.ToList()
    };
}

public sealed class CustomMod
{
    public string Name { get; set; } = "";
    public string ModuleId { get; set; } = "";
    public string WorkshopId { get; set; } = "0";
}

public static class StableClientSetup
{
    // The Coop version the two Working stable setups are named after (here and in Coodsz Host).
    // A new version renames them through Renames, so the setup and its settings carry over.
    public const string CoopVersion = "0.1.6";
    public const string LegacyName = "Working stable mods for Coop v0.15";
    public const string VanillaName = "Working stable mods for Coop " + CoopVersion + " - Vanilla campaign Map";
    public const string MoreNationsName = "Working stable mods for Coop " + CoopVersion + " - More Nations Remastered Map";

    /// <summary>Older names of the two setups. Both tools rename them once on load, keeping the
    /// setup itself (selection, settings, MCM folder), so add the old name here when the name changes.</summary>
    public static readonly (string From, string To)[] Renames =
    [
        (LegacyName, VanillaName),
        ("Working stable mods for Coop v0.15 - Vanilla map", VanillaName),
        ("Working stable mods for Coop v0.15 - More Nations Remastered", MoreNationsName),
        ("Working stable mods for Coop v0.15 - Vanilla campaign Map", VanillaName),
        ("Working stable mods for Coop v0.15 - More Nations Remastered Map", MoreNationsName)
    ];
    public const string VanillaMap = "Vanilla";
    public const string MoreNationsMap = "MoreNations";
    // Coop alone on the vanilla map, to join a Plain Coop (no mods) server from Coodsz Host.
    public const string PlainName = "Plain Coop (no mods)";

    public static bool IsPlain(string? name) =>
        string.Equals(name, PlainName, StringComparison.OrdinalIgnoreCase);

    public static string MapFor(string name) =>
        name.Equals(MoreNationsName, StringComparison.OrdinalIgnoreCase) ? MoreNationsMap : VanillaMap;
}
