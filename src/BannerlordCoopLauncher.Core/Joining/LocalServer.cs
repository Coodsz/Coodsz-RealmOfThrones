using System.Diagnostics;
using System.Globalization;
using System.Text.RegularExpressions;

namespace BannerlordCoopLauncher.Core.Joining;

/// <summary>A Coop dedicated server running on this PC, as the client launcher sees it.</summary>
public sealed record LocalServerInfo(int Port, bool PasswordNeeded);

/// <summary>What the client launcher shows or fills in for a player who is also the host.</summary>
public sealed record OwnServerHint(string Address, int Port, string Note, bool Fill);

/// <summary>
/// Notices that the Coop dedicated server runs on this PC (any server tool starts the same
/// BannerlordCoopServer and engine), reads its port from the server's own server-config.json
/// (in the running server's --data-dir when it has one, as the TAOM server does),
/// and tells the client launcher to use 127.0.0.1.
/// </summary>
public static class LocalServer
{
    public const int DefaultPort = 4200;
    public const string YouAreTheHost = "You are the host, use 127.0.0.1.";

    /// <summary>The server's settings file: Documents\Mount and Blade II Bannerlord\CoopData\DedicatedServer\server-config.json.</summary>
    public static string ServerConfigPath { get; set; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
        "Mount and Blade II Bannerlord", "CoopData", "DedicatedServer", "server-config.json");

    /// <summary>The UDP port in server-config.json (JSON with comments), or null when it has none.</summary>
    public static int? ConfigPort(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return null;
        var match = Regex.Match(StripComments(json), "\"port\"\\s*:\\s*\"?(\\d{1,5})\"?", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        return match.Success && int.TryParse(match.Groups[1].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var port) && port is >= 1 and <= 65535
            ? port
            : null;
    }

    public static bool ConfigHasPassword(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return false;
        var match = Regex.Match(StripComments(json), "\"password\"\\s*:\\s*\"((?:[^\"\\\\]|\\\\.)*)\"", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        return match.Success && match.Groups[1].Value.Trim().Length > 0;
    }

    private static string StripComments(string json) =>
        Regex.Replace(json, "(\"(?:[^\"\\\\]|\\\\.)*\")|//[^\\n]*|/\\*.*?\\*/", m => m.Groups[1].Success ? m.Value : "", RegexOptions.Singleline);

    /// <summary>True when a dedicated server process (the starter or its engine) runs on this PC.</summary>
    public static bool ServerProcessRunning()
    {
        if (!OperatingSystem.IsWindows())
            return false;
        if (Any("BannerlordCoopServer"))
            return true;
        foreach (var process in Process.GetProcessesByName("dotnet"))
        {
            try
            {
                var path = process.MainModule?.FileName;
                if (path != null && IsServerEngine(path))
                    return true;
            }
            catch (Exception)
            {
                // A process can refuse its path while it starts, or belong to another user.
            }
            finally
            {
                process.Dispose();
            }
        }

        return false;
    }

    /// <summary>
    /// True for a server folder's engine\dotnet\dotnet.exe. The Coop server folder is named
    /// DedicatedServer, but the TAOM server tool's folder is not (C:\TAOM-Coop-Host\Server-...), and
    /// with a mod setup it starts the engine without BannerlordCoopServer.exe.
    /// </summary>
    public static bool IsServerEngine(string dotnetPath)
    {
        var path = dotnetPath.Replace('/', '\\');
        if (path.Contains(@"\DedicatedServer\engine\dotnet", StringComparison.OrdinalIgnoreCase))
            return true;
        if (!path.EndsWith(@"\engine\dotnet\dotnet.exe", StringComparison.OrdinalIgnoreCase))
            return false;
        var engine = Path.GetDirectoryName(Path.GetDirectoryName(path));
        return engine != null && File.Exists(Path.Combine(engine, "bin", "Win64_Shipping_Server", "TaleWorlds.Starter.DotNetCore.dll"));
    }

    private static bool Any(string name)
    {
        var found = Process.GetProcessesByName(name);
        foreach (var process in found)
            process.Dispose();
        return found.Length > 0;
    }

    /// <summary>The running local server, or null when none runs here.</summary>
    public static LocalServerInfo? Detect() => Detect(ServerProcessRunning, ReadConfig);

    public static LocalServerInfo? Detect(Func<bool> serverRunning, Func<string?> readConfig)
    {
        if (!serverRunning())
            return null;
        var json = readConfig();
        return new LocalServerInfo(ConfigPort(json) ?? DefaultPort, ConfigHasPassword(json));
    }

    /// <summary>
    /// More server data folders to read server-config.json from, after the --data-dir ones and before
    /// Documents. The Burning Empires client launcher points this at the Data folder in be-server.json,
    /// because its server starts the engine directly and no command line shows a --data-dir.
    /// </summary>
    public static Func<IEnumerable<string>> ExtraDataFolders { get; set; } = () => [];

    /// <summary>
    /// The server-config.json files to read, best first: the data folder of each running
    /// BannerlordCoopServer.exe (its --data-dir, where the TAOM server tool keeps its data),
    /// then each of <see cref="ExtraDataFolders"/>, then the Documents one every other server tool uses.
    /// </summary>
    public static IReadOnlyList<string> ConfigCandidates(IEnumerable<string?> commandLines) =>
        ConfigCandidates(commandLines, ReadExtraDataFolders());

    private static List<string> ReadExtraDataFolders()
    {
        try
        {
            return ExtraDataFolders().ToList();
        }
        catch (Exception)
        {
            // A settings file that cannot be read only costs the extra folder; Documents is still read.
            return [];
        }
    }

    public static IReadOnlyList<string> ConfigCandidates(IEnumerable<string?> commandLines, IEnumerable<string?> extraDataFolders)
    {
        var paths = new List<string>();
        foreach (var line in commandLines)
        {
            var dir = DataDirFromCommandLine(line);
            if (dir == null)
                continue;
            var path = Path.Combine(dir, "server-config.json");
            if (!paths.Contains(path, StringComparer.OrdinalIgnoreCase))
                paths.Add(path);
        }

        foreach (var folder in extraDataFolders)
        {
            var dir = folder?.Trim().TrimEnd('\\', '/');
            if (string.IsNullOrEmpty(dir))
                continue;
            var path = Path.Combine(dir, "server-config.json");
            if (!paths.Contains(path, StringComparer.OrdinalIgnoreCase))
                paths.Add(path);
        }

        if (!paths.Contains(ServerConfigPath, StringComparer.OrdinalIgnoreCase))
            paths.Add(ServerConfigPath);
        return paths;
    }

    /// <summary>The folder after --data-dir (quoted or not, or --data-dir=folder), or null.</summary>
    public static string? DataDirFromCommandLine(string? commandLine)
    {
        if (string.IsNullOrWhiteSpace(commandLine))
            return null;
        var match = Regex.Match(commandLine, "--data-dir(?:\\s+|=)(?:\"([^\"]+)\"|(\\S+))", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        if (!match.Success)
            return null;
        var dir = (match.Groups[1].Success ? match.Groups[1].Value : match.Groups[2].Value).Trim().TrimEnd('\\', '/');
        return dir.Length == 0 ? null : dir;
    }

    private static string? ReadConfig()
    {
        foreach (var path in ConfigCandidates(ServerCommandLines()))
        {
            try
            {
                if (File.Exists(path))
                    return File.ReadAllText(path);
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }

        return null;
    }

    private static List<string?> ServerCommandLines()
    {
        var lines = new List<string?>();
        if (!OperatingSystem.IsWindows())
            return lines;
        foreach (var process in Process.GetProcessesByName("BannerlordCoopServer"))
        {
            try
            {
                lines.Add(ProcessCommandLine.Read(process.Id));
            }
            finally
            {
                process.Dispose();
            }
        }

        return lines;
    }

    /// <summary>
    /// What to suggest when Direct is picked. With the server on this PC and an empty address,
    /// fill 127.0.0.1 and the server port. When the address typed is one of this PC's own
    /// addresses, say to use 127.0.0.1 (routers often cannot loop a public address back).
    /// </summary>
    public static OwnServerHint? Suggest(bool direct, string? address, LocalServerInfo? local, IEnumerable<string>? ownAddresses = null)
    {
        if (!direct || local == null)
            return null;
        var typed = address?.Trim() ?? "";
        if (typed.Length == 0)
            return new OwnServerHint(Ipv4.Loopback, local.Port, YouAreTheHost, Fill: true);
        if (Ipv4.Classify(typed) == Ipv4Kind.Loopback)
            return new OwnServerHint(Ipv4.Loopback, local.Port, YouAreTheHost, Fill: false);
        if (ownAddresses != null && ownAddresses.Contains(typed, StringComparer.OrdinalIgnoreCase))
            return new OwnServerHint(Ipv4.Loopback, local.Port, "That address is this PC. " + YouAreTheHost, Fill: false);
        return null;
    }

    /// <summary>A hint for the address box when a virtual LAN app is on this PC.</summary>
    public static string? VpnHint(IEnumerable<AdapterAddress> adapters)
    {
        var vpn = adapters.FirstOrDefault(a => a.IsVirtualLan);
        if (vpn == null)
            return null;
        return vpn.Kind switch
        {
            AdapterKind.Radmin => "Radmin VPN is on this PC. If the host uses it too, type the host's Radmin VPN address here. It usually starts with 26.",
            AdapterKind.Hamachi => "Hamachi is on this PC. If the host uses it too, type the host's Hamachi address here. It usually starts with 25.",
            _ => vpn.AppName + " is on this PC. If the host uses it too, type the host's " + vpn.AppName + " address here."
        };
    }
}
