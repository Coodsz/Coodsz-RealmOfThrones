using System.IO.Compression;
using System.Net.Http.Headers;
using System.Text.Json;
using BannerlordCoopLauncher.Core.Parsing;

namespace BannerlordCoopLauncher.Core.Nexus;

public sealed record NexusMod(int ModId, string ModuleId, string Name);

public static class NexusModCatalog
{
    public const string GameDomain = "mountandblade2bannerlord";

    public static IReadOnlyList<NexusMod> Mods { get; } =
    [
        new(2006, "Bannerlord.Harmony", "Harmony"),
        new(2018, "Bannerlord.ButterLib", "ButterLib"),
        new(2102, "Bannerlord.UIExtenderEx", "UIExtenderEx"),
        new(612, "Bannerlord.MBOptionScreen", "Mod Configuration Menu v5"),
        new(688, "ImprovedGarrisons", "Improved Garrisons"),
        new(3462, "XorberaxLegacy", "Xorberax's Legacy"),
        new(11879, "CoopNightly", "Bannerlord Coop")
    ];
}

public sealed class NexusException : Exception
{
    public NexusException(string message) : base(message) { }
}

public sealed record NexusFileChoice(long FileId, string FileName, string Version);

public sealed class NexusModsClient
{
    private readonly HttpClient _http;

    public NexusModsClient(HttpClient? http = null)
    {
        _http = http ?? new HttpClient { Timeout = TimeSpan.FromMinutes(5) };
    }

    public async Task<NexusFileChoice> ChooseFileAsync(int modId, string apiKey, CancellationToken cancellationToken)
    {
        using var document = await GetJsonAsync(
            $"https://api.nexusmods.com/v1/games/{NexusModCatalog.GameDomain}/mods/{modId}/files.json",
            apiKey,
            cancellationToken);
        if (!document.RootElement.TryGetProperty("files", out var files) || files.ValueKind != JsonValueKind.Array)
            throw new NexusException("Nexus did not return a file list.");

        JsonElement? chosen = null;
        long newest = -1;
        foreach (var file in files.EnumerateArray())
        {
            var category = file.TryGetProperty("category_name", out var categoryValue) ? categoryValue.GetString() : "";
            if (category is "OLD_VERSION" or "ARCHIVED" or "REMOVED")
                continue;
            var primary = file.TryGetProperty("is_primary", out var primaryValue) && primaryValue.ValueKind == JsonValueKind.True;
            var uploaded = file.TryGetProperty("uploaded_timestamp", out var uploadedValue) && uploadedValue.TryGetInt64(out var stamp)
                ? stamp
                : 0;
            if (primary)
            {
                chosen = file;
                break;
            }

            if (uploaded >= newest)
            {
                newest = uploaded;
                chosen = file;
            }
        }

        if (chosen == null || !chosen.Value.TryGetProperty("file_id", out var idValue))
            throw new NexusException("Nexus has no downloadable file for this mod.");

        var fileId = idValue.TryGetInt64(out var parsed) ? parsed : 0;
        var name = chosen.Value.TryGetProperty("file_name", out var nameValue) ? nameValue.GetString() ?? "mod.zip" : "mod.zip";
        var version = chosen.Value.TryGetProperty("version", out var versionValue) ? versionValue.GetString() ?? "" : "";
        return new NexusFileChoice(fileId, name, version);
    }

    public async Task<Uri> GetDownloadLinkAsync(int modId, long fileId, string apiKey, CancellationToken cancellationToken)
    {
        using var document = await GetJsonAsync(
            $"https://api.nexusmods.com/v1/games/{NexusModCatalog.GameDomain}/mods/{modId}/files/{fileId}/download_link.json",
            apiKey,
            cancellationToken);
        if (document.RootElement.ValueKind != JsonValueKind.Array || document.RootElement.GetArrayLength() == 0)
            throw new NexusException("Nexus did not return a download link. A Premium membership is required for direct downloads.");

        var uriText = document.RootElement[0].GetProperty("URI").GetString();
        if (!Uri.TryCreate(uriText, UriKind.Absolute, out var uri))
            throw new NexusException("Nexus returned an invalid download link.");
        return uri;
    }

    public async Task DownloadAsync(Uri uri, string destination, CancellationToken cancellationToken)
    {
        using var response = await _http.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();
        await using var input = await response.Content.ReadAsStreamAsync(cancellationToken);
        await using var output = File.Create(destination);
        await input.CopyToAsync(output, cancellationToken);
    }

    private async Task<JsonDocument> GetJsonAsync(string url, string apiKey, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.TryAddWithoutValidation("apikey", apiKey.Trim());
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        request.Headers.UserAgent.ParseAdd("BannerlordCoopLauncher/0.1.0");
        using var response = await _http.SendAsync(request, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized)
            throw new NexusException("Nexus rejected the API key.");
        if ((int)response.StatusCode == 403)
            throw new NexusException("Nexus did not allow this download. Direct downloads need a Premium membership.");
        if (!response.IsSuccessStatusCode)
            throw new NexusException("Nexus request failed: " + (int)response.StatusCode);
        return JsonDocument.Parse(body);
    }
}

public static class ModuleZipInstaller
{
    public static void ExtractSafe(string zipPath, string destination)
    {
        Directory.CreateDirectory(destination);
        var root = Path.GetFullPath(destination) + Path.DirectorySeparatorChar;
        using var archive = ZipFile.OpenRead(zipPath);
        foreach (var entry in archive.Entries)
        {
            var target = Path.GetFullPath(Path.Combine(destination, entry.FullName.Replace('/', Path.DirectorySeparatorChar)));
            if (!target.StartsWith(root, StringComparison.OrdinalIgnoreCase) && !target.Equals(root.TrimEnd(Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase))
                throw new NexusException("The mod archive contains an unsafe path.");
        }

        ZipFile.ExtractToDirectory(zipPath, destination, overwriteFiles: true);
    }

    public static string? FindModuleDirectory(string extractedRoot, string moduleId)
    {
        if (!Directory.Exists(extractedRoot))
            return null;

        foreach (var xmlPath in Directory.EnumerateFiles(extractedRoot, "SubModule.xml", SearchOption.AllDirectories))
        {
            if (!SubModuleXml.TryRead(File.ReadAllText(xmlPath), out var info, out _) || info == null)
                continue;
            if (string.Equals(info.Id, moduleId, StringComparison.OrdinalIgnoreCase))
                return Path.GetDirectoryName(xmlPath);
        }

        return null;
    }

    public static void InstallDirectory(string moduleDirectory, string gameRoot, string moduleId)
    {
        var destination = Path.GetFullPath(Path.Combine(gameRoot, "Modules", moduleId));
        var source = Path.GetFullPath(moduleDirectory);
        if (source.Equals(destination, StringComparison.OrdinalIgnoreCase)
            || source.StartsWith(destination + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            return;
        if (Directory.Exists(destination))
            Directory.Delete(destination, recursive: true);
        Copy(source, destination);
    }

    private static void Copy(string source, string destination)
    {
        Directory.CreateDirectory(destination);
        foreach (var file in Directory.EnumerateFiles(source))
            File.Copy(file, Path.Combine(destination, Path.GetFileName(file)), overwrite: true);
        foreach (var directory in Directory.EnumerateDirectories(source))
            Copy(directory, Path.Combine(destination, Path.GetFileName(directory)));
    }
}
