using System.Security.Cryptography;
using System.Xml;
using System.Xml.Linq;

namespace TaomCoop.Core;

public static class TaomStack
{
    public const string GameVersion = "v1.4.8";
    // TAOM-Coop 0.3.25 r2: TAOM Public 2.0.29.5 + the released Workshop Coop 0.1.6
    // (module id "Coop"; the old CoopNightly 0.1.5 playtest copy is retired).
    public const string KitVersion = "0.3.25";
    public const string TaomVersionText = "2.0.29.5";
    public const string CoopVersion = "v0.1.6";
    public const string CoopModuleId = "Coop";
    public const string LegacyCoopModuleId = "CoopNightly";
    public const string CoopWorkshopId = "3770450698";
    public const string CoopIdentity = "workshop-3770450698-v0.1.6-20261007";
    public const string CompatVersion = "v0.3.25";
    public const string CompatModuleId = "TAOM.CoopCompat";
    public const string DirectPort = "4200";

    public static readonly IReadOnlyDictionary<string, string> ContentVersions =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["TAOM"] = "v2.0.29.5",
            ["TAOM_Map"] = "v2.0.29.5",
            ["TAOM.Dependencies"] = "v2.0.29.5",
            ["LOTRLOME_Armory"] = "v2.0.29.5"
        };

    public const string TaomDllSha256 = "AEEC07AA61C84F45E9E8662C294CFF68B000F041D8FCA453CF30058CB9AD14D8";
    public const string DependenciesDllSha256 = "3B8A9274F254D4CC0BD2546BADB8A5111AD2BB1488DDAA4C0B4DB9C6815E6E2A";
    // Workshop Coop 0.1.6: the client and dedicated-server Coop.Core.dll are the same file.
    public const string CoopCoreSha256 = "7CD7F77CCFE88CB482A2D13FCEA593E17C7B9EF05D3CF449D844D38BA5D2A587";
    // The Coop 0.1.6 server DLLs in engine\Modules\Coop\bin\Win64_Shipping_Server that the TAOM host kit checks before it
    // copies a server: $requiredCoopPins in HostKit-0.3.25\Tools\Prepare-Host.ps1, lines 65 to 71 (Assert-WorkshopServer).
    public static readonly IReadOnlyDictionary<string, string> CoopServerPins =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["Common.dll"] = "D5A500734AA27D179E36AF882F4B41F7DB481F1BB485C5426F050FB4A7BB6494",
            ["Coop.Core.dll"] = CoopCoreSha256,
            ["Coop.Steam.dll"] = "89D46D98B38D257D5C4BA678657E92500940FB635474E3E8A7E6A283A5649660",
            ["GameInterface.dll"] = "104CC2CC1DA6460A27BF519148987B5EE0E257B37ACED80C9877C1DB65A6649F",
            ["Missions.dll"] = "904620C9E93C6A76F0B665AF89C05C240D770493C4570EF7C459786581E49DD6"
        };

    public const string SetText = "Bannerlord 1.4.8, TAOM 2.0.29.5, Coop 0.1.6, TAOM.CoopCompat 0.3.25";
    public const string ClientKitPattern = "TAOM-Coop-v0.3.25-CompatOnly-CLIENT*.zip";
    public const string HostKitPattern = "TAOM-Coop-v0.3.25-CompatOnly-HOST-OPERATOR*.zip";
    // The exact CompatOnly zips the release carries (tools\release-assets.json). Release builds embed them
    // (TaomCompatZip), and a zip on this PC or the embedded copy is only used with this SHA-256.
    public const string ClientKitName = "TAOM-Coop-v0.3.25-CompatOnly-CLIENT-PLAYTEST-1675bea7-a2ac41c2-r2.zip";
    public const string ClientKitSha256 = "621EC05F1439C9D236E4DC7CC2BA2712E96B810DDD15AD70E7726B8812ED42EC";
    public const string HostKitName = "TAOM-Coop-v0.3.25-CompatOnly-HOST-OPERATOR-1675bea7-a2ac41c2-r2.zip";
    public const string HostKitSha256 = "ECB996F6DAEDD329F8423323BB302900FE1C0E2269E043E21938120BE4B3D672";

    public static readonly string[] ClientModules =
    [
        "TAOM.Dependencies",
        "Native",
        "SandBoxCore",
        "Sandbox",
        "StoryMode",
        "CustomBattle",
        "TAOM",
        "TAOM_Map",
        "LOTRLOME_Armory",
        "Coop",
        "TAOM.CoopCompat"
    ];

    public static readonly string[] ServerModules =
    [
        "TAOM.Dependencies",
        "Native",
        "SandBoxCore",
        "Sandbox",
        "StoryMode",
        "CustomBattle",
        "TAOM",
        "TAOM_Map",
        "LOTRLOME_Armory",
        "Coop",
        "TAOM.CoopCompat",
        "DedicatedServer.Windows"
    ];

    public static bool CoopIsAdjacent(IReadOnlyList<string> modules)
    {
        var index = modules.ToList().FindIndex(id => id == CoopModuleId);
        return index >= 0 && index + 1 < modules.Count && modules[index + 1] == CompatModuleId;
    }
}

public sealed record ModuleIdentity(string Id, string Version);

public static class ModuleIdentityReader
{
    public static ModuleIdentity Read(string subModulePath)
    {
        var text = File.ReadAllText(subModulePath);
        return ReadXml(text);
    }

    /// <summary>Like <see cref="Read"/>, or null when the file can't be read now (locked by a game or an antivirus, or no access),
    /// so a module list scan shows that module as unknown instead of throwing.</summary>
    public static ModuleIdentity? TryRead(string subModulePath)
    {
        try
        {
            return Read(subModulePath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    public static ModuleIdentity ReadXml(string xml)
    {
        try
        {
            var module = XDocument.Parse(xml).Element("Module");
            return new ModuleIdentity(ReadValue(module, "Id"), ReadValue(module, "Version"));
        }
        catch (XmlException)
        {
            return new ModuleIdentity("", "");
        }
    }

    private static string ReadValue(XElement? module, string name) =>
        module?.Element(name)?.Attribute("value")?.Value.Trim() ?? "";
}

public static class Sha256Files
{
    public static string Hash(string path) => BannerlordCoopLauncher.Core.IO.FileHash.Sha256(path);

    public static string Hash(Stream stream) => BannerlordCoopLauncher.Core.IO.FileHash.Sha256(stream);
}
