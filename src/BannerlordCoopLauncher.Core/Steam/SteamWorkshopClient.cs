using System.Text.Json;

namespace BannerlordCoopLauncher.Core.Steam;

public sealed record PublishedFile(
    string PublishedFileId,
    int Result,
    long TimeUpdated,
    int ConsumerAppId,
    string Title,
    string ContentHandle = "");

public sealed class SteamWorkshopClient
{
    public const string Endpoint = "https://api.steampowered.com/ISteamRemoteStorage/GetPublishedFileDetails/v1/";

    private readonly HttpClient _http;

    public SteamWorkshopClient(HttpClient? http = null)
    {
        _http = http ?? new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
    }

    public async Task<IReadOnlyDictionary<string, PublishedFile>> GetDetailsAsync(
        IEnumerable<string> publishedFileIds,
        CancellationToken cancellationToken)
    {
        // Steam answers 400 Bad Request for the whole batch when one id is empty, 0 or not a number,
        // so custom mods without a Workshop page (WorkshopId "0") are left out of the request.
        var ids = publishedFileIds.Where(IsWorkshopId).Select(id => id.Trim()).Distinct(StringComparer.Ordinal).ToList();
        var details = new Dictionary<string, PublishedFile>(StringComparer.Ordinal);
        for (var offset = 0; offset < ids.Count; offset += 50)
        {
            var slice = ids.Skip(offset).Take(50).ToList();
            var form = new Dictionary<string, string> { ["itemcount"] = slice.Count.ToString() };
            for (var index = 0; index < slice.Count; index++)
                form[$"publishedfileids[{index}]"] = slice[index];

            using var response = await _http.PostAsync(Endpoint, new FormUrlEncodedContent(form), cancellationToken);
            response.EnsureSuccessStatusCode();
            var json = await response.Content.ReadAsStringAsync(cancellationToken);
            foreach (var file in Parse(json))
                details[file.PublishedFileId] = file;
        }

        return details;
    }

    public static bool IsWorkshopId(string? id) =>
        ulong.TryParse(id?.Trim(), System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var number) && number > 0;

    public static IReadOnlyList<PublishedFile> Parse(string json)
    {
        using var document = JsonDocument.Parse(json);
        if (!document.RootElement.TryGetProperty("response", out var response))
            return Array.Empty<PublishedFile>();
        if (!response.TryGetProperty("publishedfiledetails", out var items) || items.ValueKind != JsonValueKind.Array)
            return Array.Empty<PublishedFile>();

        var files = new List<PublishedFile>();
        foreach (var item in items.EnumerateArray())
        {
            var id = ReadString(item, "publishedfileid");
            if (string.IsNullOrEmpty(id))
                continue;
            files.Add(new PublishedFile(
                id,
                ReadInt(item, "result"),
                ReadLong(item, "time_updated"),
                ReadInt(item, "consumer_app_id"),
                ReadString(item, "title") ?? "",
                ReadString(item, "hcontent_file") ?? ""));
        }

        return files;
    }

    private static string? ReadString(JsonElement item, string name)
    {
        if (!item.TryGetProperty(name, out var value))
            return null;
        return value.ValueKind switch
        {
            JsonValueKind.String => value.GetString(),
            JsonValueKind.Number => value.GetRawText(),
            _ => null
        };
    }

    private static int ReadInt(JsonElement item, string name)
    {
        if (!item.TryGetProperty(name, out var value))
            return 0;
        if (value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var number))
            return number;
        if (value.ValueKind == JsonValueKind.String && int.TryParse(value.GetString(), out number))
            return number;
        return 0;
    }

    private static long ReadLong(JsonElement item, string name)
    {
        if (!item.TryGetProperty(name, out var value))
            return 0;
        if (value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out var number))
            return number;
        if (value.ValueKind == JsonValueKind.String && long.TryParse(value.GetString(), out number))
            return number;
        return 0;
    }
}
