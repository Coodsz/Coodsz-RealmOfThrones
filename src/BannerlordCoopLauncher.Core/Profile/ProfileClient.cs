using BannerlordCoopLauncher.Core.Models;

namespace BannerlordCoopLauncher.Core.Profile;

public sealed class ProfileClient
{
    private readonly HttpClient _http;

    public ProfileClient(HttpClient? http = null)
    {
        _http = http ?? new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
    }

    public async Task<(ServerProfile Profile, string Json)> DownloadAsync(string url, CancellationToken cancellationToken)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme is not ("https" or "http"))
            throw new ProfileException("Profile URL must be an http or https address.");

        string json;
        try
        {
            json = await _http.GetStringAsync(uri, cancellationToken);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            throw new ProfileException("Could not download the server profile. " + ex.Message);
        }

        return (ProfileJson.Parse(json), json);
    }
}
