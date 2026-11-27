using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using BannerlordCoopLauncher.Core.IO;
using BannerlordCoopLauncher.Core.Modules;

namespace BannerlordCoopLauncher.Core.Support;

/// <summary>The notice one tool shows, in its language.</summary>
public sealed record AnnouncementText(string Id, string Title, string Text)
{
    /// <summary>What Dismiss keeps: the id and a hash of the shown words, so a new id or new words show again.</summary>
    public string DismissKey =>
        Id + ":" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(Title + "\n" + Text)))[..16];
}

/// <summary>
/// announcement.json: one notice for the tool windows (like Coodsz is on vacation), in compat-list next to
/// mods.json and published with it in Coodsz-Coop-and-Friends, so Coodsz turns it on and off from GitHub
/// without a release. The tools download it when the window opens and keep a copy for the next start.
/// The words are plain text: nothing in them is read as a link or markup.
/// </summary>
public sealed class AnnouncementFile
{
    public const int SupportedSchema = 1;
    public const string DefaultUrl = "https://raw.githubusercontent.com/" + ModCompatDatabase.Repository + "/main/compat-list/announcement.json";

    /// <summary>A bigger answer is not an announcement; it is not read.</summary>
    public const int MaxBytes = 64 * 1024;

    [JsonPropertyName("schema")] public int Schema { get; set; }
    [JsonPropertyName("id")] public string Id { get; set; } = "";
    [JsonPropertyName("enabled")] public bool Enabled { get; set; }

    /// <summary>The tool families that show it (coop, taom, ad1259, be): the Server Tool and the Client Launcher of each.</summary>
    [JsonPropertyName("tools")] public List<string>? Tools { get; set; }

    /// <summary>The words per language code (en, ar, de, fr, ja, ko, pt, ru, tr, zh-CN). A missing language falls back to en.</summary>
    [JsonPropertyName("title")] public Dictionary<string, string>? Title { get; set; }
    [JsonPropertyName("text")] public Dictionary<string, string>? Text { get; set; }

    /// <summary>Shared by every Coodsz tool, next to the mod list copy.</summary>
    public static string CachePath { get; set; } = Path.Combine(ToolData.RoamingRoot, "announcement.json");

    private static readonly JsonSerializerOptions Options = new()
    {
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        PropertyNameCaseInsensitive = true,
    };

    /// <summary>Reads the file text; null when it is too big, damaged or a newer schema.</summary>
    public static AnnouncementFile? Parse(string? json)
    {
        // A file saved with a BOM on GitHub still reads.
        json = json?.TrimStart('\uFEFF');
        if (string.IsNullOrWhiteSpace(json) || Encoding.UTF8.GetByteCount(json) > MaxBytes)
            return null;
        try
        {
            var file = JsonSerializer.Deserialize<AnnouncementFile>(json, Options);
            return file is { Schema: SupportedSchema } ? file : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>
    /// What this tool family shows in this language, or null: off, not for this family, no id, or no English
    /// title or text. A missing or blank translation is the English words.
    /// </summary>
    public AnnouncementText? For(string family, string? language)
    {
        if (!Enabled || string.IsNullOrWhiteSpace(Id) || Tools == null
            || !Tools.Any(tool => string.Equals(tool?.Trim(), family, StringComparison.OrdinalIgnoreCase)))
            return null;
        var title = Pick(Title, language);
        var text = Pick(Text, language);
        if (string.IsNullOrWhiteSpace(title) || string.IsNullOrWhiteSpace(text))
            return null;
        return new AnnouncementText(Id.Trim(), title.Trim(), text.Trim());
    }

    private static string? Pick(Dictionary<string, string>? words, string? language)
    {
        if (words == null)
            return null;
        if (!string.IsNullOrEmpty(language) && words.TryGetValue(language, out var translated) && !string.IsNullOrWhiteSpace(translated))
            return translated;
        return words.TryGetValue("en", out var english) ? english : null;
    }

    /// <summary>The kept copy, or null when there is none or it is damaged.</summary>
    public static AnnouncementFile? LoadCached(string? cachePath = null)
    {
        try
        {
            var path = cachePath ?? CachePath;
            return File.Exists(path) ? Parse(File.ReadAllText(path)) : null;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>
    /// Downloads the file and keeps a copy. Offline, a damaged answer or one over MaxBytes: the kept copy.
    /// Gone from GitHub (404): the kept copy is deleted too, so a removed notice stays away. Never throws.
    /// </summary>
    public static async Task<AnnouncementFile?> FetchAsync(HttpClient http, string? cachePath = null, string url = DefaultUrl,
        CancellationToken cancellationToken = default)
    {
        cachePath ??= CachePath;
        try
        {
            using var response = await http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
            if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
            {
                try
                {
                    File.Delete(cachePath);
                }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException)
                {
                    // A kept copy that can not be deleted now still stays hidden this run.
                }

                return null;
            }

            response.EnsureSuccessStatusCode();
            if (response.Content.Headers.ContentLength > MaxBytes)
                return LoadCached(cachePath);
            var json = await ReadCapped(response.Content, cancellationToken).ConfigureAwait(false);
            if (json != null && Parse(json) is { } file)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(cachePath)!);
                // Every tool shares the copy, so each writes its own temp file first.
                var temp = cachePath + "." + Environment.ProcessId + ".tmp";
                await File.WriteAllTextAsync(temp, json, cancellationToken).ConfigureAwait(false);
                File.Move(temp, cachePath, overwrite: true);
                return file;
            }
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException or IOException or UnauthorizedAccessException)
        {
            // Offline, or GitHub cannot be reached: the kept copy is used.
        }

        return LoadCached(cachePath);
    }

    // The body as text, or null when it is longer than MaxBytes.
    private static async Task<string?> ReadCapped(HttpContent content, CancellationToken cancellationToken)
    {
        await using var stream = await content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        using var buffer = new MemoryStream();
        var chunk = new byte[8192];
        int read;
        while ((read = await stream.ReadAsync(chunk, cancellationToken).ConfigureAwait(false)) > 0)
        {
            if (buffer.Length + read > MaxBytes)
                return null;
            buffer.Write(chunk, 0, read);
        }

        return Encoding.UTF8.GetString(buffer.GetBuffer(), 0, (int)buffer.Length);
    }

    /// <summary>Downloads the file with a short wait, for a window that is opening.</summary>
    public static async Task<AnnouncementFile?> FetchForWindowAsync(string userAgent)
    {
        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(6) };
            http.DefaultRequestHeaders.UserAgent.ParseAdd(userAgent);
            // The timeout above covers the headers only; this one covers a body that stalls too.
            using var wait = new CancellationTokenSource(TimeSpan.FromSeconds(6));
            return await FetchAsync(http, cancellationToken: wait.Token).ConfigureAwait(false);
        }
        catch (Exception)
        {
            return LoadCached();
        }
    }
}

/// <summary>The notices one tool was told to hide (Dismiss), one DismissKey per line, in the tool's own folder.</summary>
public static class AnnouncementDismissals
{
    private const int Keep = 20;

    /// <summary>This tool's file: its key in the name, since the Coop Server Tool and client share one folder.</summary>
    public static string PathForThisTool() =>
        ToolData.File("announcement-dismissed-" + (ToolData.CurrentPlan?.Key ?? "tool") + ".txt");

    public static bool IsDismissed(string path, AnnouncementText shown)
    {
        try
        {
            return File.Exists(path) && File.ReadLines(path).Any(line => line.Trim() == shown.DismissKey);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    public static void Dismiss(string path, AnnouncementText shown)
    {
        try
        {
            var keys = File.Exists(path) ? File.ReadAllLines(path).Select(line => line.Trim()).Where(line => line.Length > 0).ToList() : [];
            if (keys.Contains(shown.DismissKey))
                return;
            keys.Add(shown.DismissKey);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllLines(path, keys.TakeLast(Keep));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // The notice is hidden for this run anyway.
        }
    }
}
