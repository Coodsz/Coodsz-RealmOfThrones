using System.Text.Json;

namespace TaomCoop.Core;

public readonly record struct ServerFrameworkResult(bool Ready, string Detail);

public static class ServerFramework
{
    // createDotnetHome: Coodsz Host makes engine\dotnet itself; the TAOM host
    // expects Prepare server to have made it.
    public static ServerFrameworkResult Ensure(string serverRoot, string? globalShared = null, bool createDotnetHome = false)
    {
        var configPath = Path.Combine(serverRoot, "engine", "bin", "Win64_Shipping_Server", "TaleWorlds.Starter.DotNetCore.runtimeconfig.json");
        if (!File.Exists(configPath))
            return new ServerFrameworkResult(true, "");

        var dotnetHome = Path.Combine(serverRoot, "engine", "dotnet");
        if (createDotnetHome)
            Directory.CreateDirectory(dotnetHome);
        else if (!Directory.Exists(dotnetHome))
            return new ServerFrameworkResult(false, "Prepare server has not created engine\\dotnet yet.");

        FrameworkRequest[] requests;
        try
        {
            requests = ReadRequests(File.ReadAllText(configPath));
        }
        catch (JsonException)
        {
            return new ServerFrameworkResult(false, "The server's .NET config could not be read.");
        }

        if (requests.Length == 0)
            return new ServerFrameworkResult(true, "");

        globalShared ??= DefaultGlobalShared();
        var privateShared = Path.Combine(dotnetHome, "shared");
        var copied = new List<string>();
        var missing = new List<string>();
        foreach (var request in requests)
        {
            var local = Path.Combine(privateShared, request.Name);
            if (HasMatch(local, request))
                continue;
            var source = BestSource(Path.Combine(globalShared, request.Name), request);
            if (source == null)
            {
                missing.Add(request.Name + " " + request.Version);
                continue;
            }

            var destination = Path.Combine(local, Path.GetFileName(source));
            CopyDirectory(source, destination);
            copied.Add(request.Name + " " + Path.GetFileName(source));
        }

        if (missing.Count > 0)
        {
            return new ServerFrameworkResult(false,
                "The dedicated server did not start. It looks in its own engine\\dotnet folder and does not use the normal .NET install until the files are copied there. Missing: "
                + string.Join(", ", missing)
                + ". Install the .NET 6 Desktop Runtime x64, then click Start server again. https://dotnet.microsoft.com/download/dotnet/6.0");
        }

        if (copied.Count == 0)
            return new ServerFrameworkResult(true, "");
        return new ServerFrameworkResult(true, "Copied " + string.Join(", ", copied) + " into the server's engine\\dotnet folder.");
    }

    public static string DefaultGlobalShared()
    {
        var primary = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "dotnet", "shared");
        if (Directory.Exists(primary))
            return primary;
        var wide = Environment.GetEnvironmentVariable("ProgramW6432");
        if (!string.IsNullOrWhiteSpace(wide))
        {
            var alternate = Path.Combine(wide, "dotnet", "shared");
            if (Directory.Exists(alternate))
                return alternate;
        }

        return primary;
    }

    private static bool HasMatch(string frameworkDirectory, FrameworkRequest request)
    {
        if (!Directory.Exists(frameworkDirectory))
            return false;
        return Directory.EnumerateDirectories(frameworkDirectory)
            .Select(Path.GetFileName)
            .Any(name => Satisfies(name, request));
    }

    private static string? BestSource(string frameworkDirectory, FrameworkRequest request)
    {
        if (!Directory.Exists(frameworkDirectory))
            return null;
        string? bestPath = null;
        Version? best = null;
        foreach (var directory in Directory.EnumerateDirectories(frameworkDirectory))
        {
            var name = Path.GetFileName(directory);
            if (!Satisfies(name, request) || !Version.TryParse(name, out var version))
                continue;
            if (best == null || version > best)
            {
                best = version;
                bestPath = directory;
            }
        }

        return bestPath;
    }

    private static bool Satisfies(string? folderName, FrameworkRequest request)
    {
        if (!Version.TryParse(folderName, out var installed))
            return false;
        if (request.Exact)
            return installed == request.Parsed;
        return installed.Major == request.Parsed.Major
            && installed.Minor == request.Parsed.Minor
            && installed.Build >= request.Parsed.Build;
    }

    private static void CopyDirectory(string source, string destination)
    {
        Directory.CreateDirectory(destination);
        foreach (var file in Directory.EnumerateFiles(source))
        {
            var target = Path.Combine(destination, Path.GetFileName(file));
            if (!File.Exists(target))
                File.Copy(file, target);
        }

        foreach (var directory in Directory.EnumerateDirectories(source))
            CopyDirectory(directory, Path.Combine(destination, Path.GetFileName(directory)));
    }

    private static FrameworkRequest[] ReadRequests(string json)
    {
        using var document = JsonDocument.Parse(json);
        if (!document.RootElement.TryGetProperty("runtimeOptions", out var options))
            return [];
        var exact = options.TryGetProperty("rollForward", out var roll) && roll.GetString() == "Disable";
        if (options.TryGetProperty("frameworks", out var frameworks) && frameworks.ValueKind == JsonValueKind.Array)
        {
            return frameworks.EnumerateArray()
                .Select(item => ToRequest(item, exact))
                .Where(item => item != null)
                .Select(item => item!.Value)
                .ToArray();
        }

        if (options.TryGetProperty("framework", out var single))
        {
            var request = ToRequest(single, exact);
            return request == null ? [] : [request.Value];
        }

        return [];
    }

    private static FrameworkRequest? ToRequest(JsonElement item, bool exact)
    {
        if (!item.TryGetProperty("name", out var nameElement) || !item.TryGetProperty("version", out var versionElement))
            return null;
        var name = nameElement.GetString();
        var version = versionElement.GetString();
        if (string.IsNullOrWhiteSpace(name) || !Version.TryParse(version, out var parsed))
            return null;
        if (name.Contains('\\') || name.Contains('/') || name.Contains(".."))
            return null;
        return new FrameworkRequest(name, version!, parsed, exact);
    }

    private readonly record struct FrameworkRequest(string Name, string Version, Version Parsed, bool Exact);
}
