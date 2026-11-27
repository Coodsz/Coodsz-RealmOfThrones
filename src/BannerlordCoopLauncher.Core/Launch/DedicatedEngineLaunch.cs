namespace BannerlordCoopLauncher.Core.Launch;

public sealed record EngineStart(
    string FileName,
    string WorkingDirectory,
    string Arguments,
    IReadOnlyDictionary<string, string> Environment,
    IReadOnlyList<string> Modules);

/// <summary>
/// Starts the Bannerlord dedicated server engine of a Coop server folder directly, with our own
/// module list. This is the same last step Coop's Start-ModdedServer.ps1 takes (engine\dotnet\dotnet.exe
/// TaleWorlds.Starter.DotNetCore.dll _MODULES_*...*_MODULES_ /dedicatedcustomserver port region 0,
/// run from engine\bin\Win64_Shipping_Server), and what BannerlordCoopServer.exe does with its own
/// fixed list. BANNERLORD_USER_DIR is the server data folder (server-config.json, Game Saves, logs),
/// the same folder BannerlordCoopServer.exe --data-dir points at. COOP_DATA_DIR holds mod-config.json.
/// </summary>
public static class DedicatedEngineLaunch
{
    public const int DefaultEnginePort = 7210;
    public const string DefaultRegion = "EU";

    public static string EngineRoot(string serverRoot) => Path.Combine(serverRoot, "engine");

    public static string Dotnet(string serverRoot) => Path.Combine(EngineRoot(serverRoot), "dotnet", "dotnet.exe");

    public static string ServerBin(string serverRoot) => Path.Combine(EngineRoot(serverRoot), "bin", "Win64_Shipping_Server");

    public static string Starter(string serverRoot) => Path.Combine(ServerBin(serverRoot), "TaleWorlds.Starter.DotNetCore.dll");

    /// <summary>Null when the server folder has the engine pieces; otherwise what is missing.</summary>
    public static string? MissingPiece(string serverRoot)
    {
        if (!File.Exists(Dotnet(serverRoot)))
            return "engine\\dotnet\\dotnet.exe is missing from the server folder.";
        if (!File.Exists(Starter(serverRoot)))
            return "TaleWorlds.Starter.DotNetCore.dll is missing from the server folder.";
        return null;
    }

    public static string ModuleToken(IReadOnlyList<string> modules)
    {
        if (modules.Count == 0)
            throw new InvalidOperationException("The module list is empty.");
        foreach (var id in modules)
        {
            if (string.IsNullOrWhiteSpace(id) || id.Contains('*') || id.Contains('"'))
                throw new InvalidOperationException("That module id cannot be loaded.");
        }

        var token = "_MODULES_*" + string.Join("*", modules) + "*_MODULES_";
        return token.Contains(' ') ? "\"" + token + "\"" : token;
    }

    public static EngineStart Build(
        string serverRoot,
        IReadOnlyList<string> modules,
        string userDirectory,
        string coopDataDirectory,
        int enginePort = DefaultEnginePort,
        string region = DefaultRegion)
    {
        if (enginePort is < 1 or > 65535)
            throw new InvalidOperationException("The engine port must be from 1 to 65535.");
        if (string.IsNullOrWhiteSpace(region) || !region.All(char.IsLetter))
            throw new InvalidOperationException("The region must be letters, such as EU.");
        var arguments = "TaleWorlds.Starter.DotNetCore.dll " + ModuleToken(modules)
            + " /dedicatedcustomserver " + enginePort.ToString(System.Globalization.CultureInfo.InvariantCulture)
            + " " + region.Trim() + " 0";
        var environment = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["DOTNET_ROOT"] = Path.Combine(EngineRoot(serverRoot), "dotnet"),
            ["DOTNET_MULTILEVEL_LOOKUP"] = "0",
            ["BANNERLORD_USER_DIR"] = userDirectory,
            ["COOP_DATA_DIR"] = coopDataDirectory
        };
        return new EngineStart(Dotnet(serverRoot), ServerBin(serverRoot), arguments, environment, modules.ToList());
    }
}
