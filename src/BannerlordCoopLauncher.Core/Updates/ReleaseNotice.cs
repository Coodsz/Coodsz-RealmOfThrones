using System.Net.Http.Headers;
using System.Text.Json;

namespace BannerlordCoopLauncher.Core.Updates;

public static class ReleaseNotice
{
    public const string TaomRepo = "Coodsz/Coodsz-TAOM";
    public const string FriendsRepo = "Coodsz/Coodsz-Coop-and-Friends";
    public const string TaomVersion = "v1.2.24";
    public const string FriendsVersion = "v1.3.18";
    public const string AD1259Repo = "Coodsz/Coodsz-AD1259";
    public const string AD1259Version = "v1.1.17";
    public const string BeRepo = "Coodsz/Coodsz-BurningEmpires";
    public const string BeVersion = "v1.0.16";
    public const string RotRepo = "Coodsz/Coodsz-RealmOfThrones";
    public const string RotVersion = "v0.1.4";

    public static string ReleasesPage(string repo) => "https://github.com/" + repo + "/releases/latest";

    public static string? Message(string currentVersion, string? latestTag, string repo)
    {
        if (string.IsNullOrWhiteSpace(latestTag) || !IsNewer(latestTag, currentVersion))
            return null;

        return "A new version is available. This copy is " + Display(currentVersion)
            + ". Download " + Display(latestTag) + " from " + ReleasesPage(repo);
    }

    public static bool IsNewer(string latestTag, string currentVersion)
    {
        var latest = Parse(latestTag);
        var current = Parse(currentVersion);
        if (latest == null || current == null)
            return false;

        var length = Math.Max(latest.Length, current.Length);
        for (var i = 0; i < length; i++)
        {
            var left = i < latest.Length ? latest[i] : 0;
            var right = i < current.Length ? current[i] : 0;
            if (left != right)
                return left > right;
        }

        return false;
    }

    public static async Task<string?> CheckAsync(HttpClient http, string repo, string currentVersion, CancellationToken cancellationToken = default)
    {
        var offer = await CheckOfferAsync(http, repo, currentVersion, cancellationToken).ConfigureAwait(false);
        return offer?.Message;
    }

    public static async Task<ReleaseOffer?> CheckOfferAsync(string repo, string currentVersion, CancellationToken cancellationToken = default)
    {
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(8) };
        return await CheckOfferAsync(http, repo, currentVersion, cancellationToken).ConfigureAwait(false);
    }

    public static async Task<ReleaseOffer?> CheckOfferAsync(HttpClient http, string repo, string currentVersion, CancellationToken cancellationToken = default)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, "https://api.github.com/repos/" + repo + "/releases/latest");
            request.Headers.TryAddWithoutValidation("User-Agent", "Coodsz-ReleaseCheck");
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
            using var response = await http.SendAsync(request, cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
                return null;

            var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            return Offer(currentVersion, ReadTag(body), repo, ReadAssets(body));
        }
        catch
        {
            return null;
        }
    }

    public static ReleaseOffer? Offer(string currentVersion, string? latestTag, string repo, IReadOnlyList<ReleaseAsset> assets)
    {
        var message = Message(currentVersion, latestTag, repo);
        if (message == null || string.IsNullOrWhiteSpace(latestTag))
            return null;
        return new ReleaseOffer(message, latestTag, assets, repo);
    }

    public static IReadOnlyList<ReleaseAsset> ReadAssets(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            if (!document.RootElement.TryGetProperty("assets", out var assets) || assets.ValueKind != JsonValueKind.Array)
                return [];

            var list = new List<ReleaseAsset>();
            foreach (var asset in assets.EnumerateArray())
            {
                var name = asset.TryGetProperty("name", out var nameValue) ? nameValue.GetString() : null;
                var url = asset.TryGetProperty("browser_download_url", out var urlValue) ? urlValue.GetString() : null;
                var digest = asset.TryGetProperty("digest", out var digestValue) && digestValue.ValueKind == JsonValueKind.String ? digestValue.GetString() : null;
                if (!string.IsNullOrWhiteSpace(name) && !string.IsNullOrWhiteSpace(url))
                    list.Add(new ReleaseAsset(name, url, digest));
            }

            return list;
        }
        catch
        {
            return [];
        }
    }

    public static string? ReadTag(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            return document.RootElement.TryGetProperty("tag_name", out var tag) ? tag.GetString() : null;
        }
        catch
        {
            return null;
        }
    }

    private static string Display(string version)
    {
        var text = version.Trim();
        return text.StartsWith("v", StringComparison.OrdinalIgnoreCase) ? "v" + text[1..] : "v" + text;
    }

    private static int[]? Parse(string version)
    {
        var text = version.Trim();
        if (text.StartsWith("v", StringComparison.OrdinalIgnoreCase))
            text = text[1..];
        if (text.Length == 0)
            return null;

        var parts = text.Split('.');
        var numbers = new int[parts.Length];
        for (var i = 0; i < parts.Length; i++)
        {
            if (!int.TryParse(parts[i], out var number) || number < 0)
                return null;
            numbers[i] = number;
        }

        return numbers;
    }
}
