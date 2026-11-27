using System.Text.Json;
using System.Text.Json.Serialization;

namespace BannerlordCoopLauncher.Core.Models;

public sealed class ProfileException : Exception
{
    public ProfileException(string message) : base(message) { }
}

public sealed class ProfileMod
{
    public string Name { get; set; } = "";
    public string WorkshopId { get; set; } = "";
    public string ModuleId { get; set; } = "";
    public bool Enabled { get; set; } = true;
    public string RequiredVersion { get; set; } = "";
    public bool Pinned { get; set; }
}

public sealed class ServerProfile
{
    public int SchemaVersion { get; set; }
    public string Name { get; set; } = "";
    public string PackVersion { get; set; } = "";
    public string Host { get; set; } = "";
    public int Port { get; set; }
    public int MissionPort { get; set; }
    public string Protocol { get; set; } = "";
    public bool StartSteamAutomatically { get; set; } = true;
    public int SteamStartupTimeoutSeconds { get; set; } = 120;
    public bool CheckModVersions { get; set; }
    public List<string> BaseModules { get; set; } = new();
    public List<ProfileMod> Mods { get; set; } = new();
    public bool AutoConnect { get; set; } = true;
    public List<string> ModuleLoadOrder { get; set; } = new();

    [JsonIgnore]
    public IEnumerable<ProfileMod> EnabledMods => Mods.Where(mod => mod.Enabled);
}

public static class ProfileJson
{
    public const string DefaultProfileUrl = "https://bannerlord.eu/download/modules.json";

    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };

    public static ServerProfile Parse(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
            throw new ProfileException("Empty launcher settings.");

        ServerProfile? profile;
        try
        {
            profile = JsonSerializer.Deserialize<ServerProfile>(json, Options);
        }
        catch (JsonException ex)
        {
            throw new ProfileException("The server profile is not valid JSON: " + ex.Message);
        }

        if (profile == null)
            throw new ProfileException("Empty launcher settings.");

        Validate(profile);
        return profile;
    }

    /// <summary>
    /// Used when the online profile cannot be read and there is no saved copy yet. The launcher puts
    /// its own default mods on top (DefaultClientSet), so only the base settings matter here.
    /// </summary>
    public static ServerProfile BuiltIn() => new()
    {
        SchemaVersion = 3,
        Port = 4200,
        MissionPort = 4201,
        StartSteamAutomatically = true,
        SteamStartupTimeoutSeconds = 120,
        BaseModules = new List<string> { "Native", "SandBoxCore", "SandBox", "StoryMode", "CustomBattle" },
        Mods = new List<ProfileMod> { new() { Name = "Harmony", WorkshopId = "2859188632", ModuleId = "Bannerlord.Harmony", Enabled = true } },
        AutoConnect = true
    };

    public static string Serialize(ServerProfile profile) =>
        JsonSerializer.Serialize(profile, new JsonSerializerOptions(Options) { WriteIndented = true });

    public static void Validate(ServerProfile profile)
    {
        if (!string.IsNullOrWhiteSpace(profile.Host) && !IsValidHost(profile.Host))
            throw new ProfileException("Host must be an IP or domain, without a port, spaces or URL scheme.");

        if (profile.Port is < 1 or > 65535)
            throw new ProfileException("Port must be between 1 and 65535.");

        if (profile.SteamStartupTimeoutSeconds < 1)
            profile.SteamStartupTimeoutSeconds = 120;

        if (profile.BaseModules.Count == 0)
            throw new ProfileException("The server profile has no base modules.");

        if (profile.Mods.Count == 0)
            throw new ProfileException("The server profile has no mods.");

        foreach (var mod in profile.Mods)
        {
            // A local mod (downloaded from a URL, not the Workshop) may come without a Workshop id;
            // "0" is how the launcher marks local mods. The online profile started listing such
            // mods on 2026-10-15 and the whole profile was refused, so new installs had no mod list.
            if (string.IsNullOrWhiteSpace(mod.WorkshopId))
                mod.WorkshopId = "0";
            if (!IsValidMod(mod))
                throw new ProfileException("Each mod needs a name, a numeric Workshop ID and a valid module ID.");
        }
    }

    public static bool IsValidHost(string? host)
    {
        if (string.IsNullOrWhiteSpace(host))
            return false;

        var value = host.Trim();
        if (value.Contains(' ') || value.Contains(':') || value.Contains('/') || value.Contains('\\'))
            return false;

        return value.All(ch => char.IsLetterOrDigit(ch) || ch is '.' or '-');
    }

    public static bool IsValidMod(ProfileMod mod)
    {
        if (string.IsNullOrWhiteSpace(mod.Name))
            return false;
        if (string.IsNullOrWhiteSpace(mod.WorkshopId) || !mod.WorkshopId.All(char.IsDigit))
            return false;
        return IsSafeModuleId(mod.ModuleId);
    }

    public static bool IsSafeModuleId(string? moduleId)
    {
        if (string.IsNullOrWhiteSpace(moduleId))
            return false;
        var id = moduleId.Trim();
        if (id is "." or "..")
            return false;
        return id.IndexOfAny(Path.GetInvalidFileNameChars()) < 0;
    }

    public static bool IncludesCoopModule(ServerProfile profile) =>
        profile.EnabledMods.Any(mod => mod.ModuleId is "Coop" or "CoopNightly");
}
