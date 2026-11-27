using System.Text.Json;

namespace BannerlordCoopLauncher.Core.Modules;

/// <summary>The Windows dedicated server package manifest.json on Coop's latest release names.</summary>
public sealed record CoopServerPackage(string Name, long Bytes, string Url);

/// <summary>
/// Download Coop server. Coop's GitHub release carries only the client zip and manifest.json; the 4.5 GB server .7z
/// sits on Coop's file host, linked from the release page. This reads the manifest for that link, and the tools open
/// it in the browser (ShellOpen). They never download the .7z themselves. A link that is not https, not on Coop's file
/// host or github.com, or not a .7z gives the release page instead, and so does a manifest that can not be read.
/// </summary>
public static class CoopServerDownload
{
    public const string ManifestUrl = "https://github.com/Bannerlord-Coop-Team/BannerlordCoop/releases/latest/download/manifest.json";

    public const string Button = "Download Coop server";

    // Coop's release file host (the manifest's own links), and on GitHub only Coop's own release downloads, where a
    // later release could put the package.
    private const string FileHost = "pub-c80efa34191141fd803f8508e025f726.r2.dev";
    private const string GitHubReleases = "/Bannerlord-Coop-Team/BannerlordCoop/releases/download/";

    /// <summary>The manifest's windows entry, or null when the text is not a manifest with a windows url.</summary>
    public static CoopServerPackage? Parse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return null;
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind != JsonValueKind.Object
                || !doc.RootElement.TryGetProperty("windows", out var windows) || windows.ValueKind != JsonValueKind.Object
                || !windows.TryGetProperty("url", out var url) || url.ValueKind != JsonValueKind.String)
                return null;
            var name = windows.TryGetProperty("name", out var n) && n.ValueKind == JsonValueKind.String ? n.GetString() ?? "" : "";
            var bytes = windows.TryGetProperty("bytes", out var b) && b.ValueKind == JsonValueKind.Number && b.TryGetInt64(out var size) ? size : 0;
            return new CoopServerPackage(name, bytes, url.GetString() ?? "");
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>
    /// The url in its normal form when it is https on the default port, has no user part, its host is Coop's file
    /// host or Coop's release downloads on github.com, and its path ends in .7z with no escaped characters. Null for
    /// anything else, so a changed or hostile manifest can only send the host to the release page.
    /// </summary>
    public static string? Trusted(string? url)
    {
        if (string.IsNullOrWhiteSpace(url) || !Uri.TryCreate(url.Trim(), UriKind.Absolute, out var uri))
            return null;
        if (uri.Scheme != Uri.UriSchemeHttps || !uri.IsDefaultPort || uri.UserInfo.Length > 0)
            return null;
        var path = uri.AbsolutePath;
        var host = string.Equals(uri.IdnHost, FileHost, StringComparison.OrdinalIgnoreCase)
            || string.Equals(uri.IdnHost, "github.com", StringComparison.OrdinalIgnoreCase) && path.StartsWith(GitHubReleases, StringComparison.OrdinalIgnoreCase);
        // An escaped character (%00, %2e%2e) could make the server read another file than the one checked here.
        return host && !path.Contains('%') && path.EndsWith(".7z", StringComparison.OrdinalIgnoreCase) ? uri.AbsoluteUri : null;
    }

    /// <summary>The link Download Coop server opens for this manifest text: the server .7z, or the release page.</summary>
    public static string LinkFor(string? json) => Trusted(Parse(json)?.Url) ?? CoopInstall.ReleaseUrl;

    /// <summary>
    /// Reads the manifest off the calling thread with a short timeout and gives the link to open. Any failure
    /// (offline, GitHub down, a changed manifest) gives the release page, which links the download too.
    /// </summary>
    public static Task<string> LinkAsync(string userAgent, CancellationToken cancellationToken = default) => Task.Run(async () =>
    {
        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(6), MaxResponseContentBufferSize = 64 * 1024 };
            http.DefaultRequestHeaders.UserAgent.ParseAdd(userAgent);
            return LinkFor(await http.GetStringAsync(ManifestUrl, cancellationToken).ConfigureAwait(false));
        }
        catch (Exception)
        {
            return CoopInstall.ReleaseUrl;
        }
    }, CancellationToken.None);

    /// <summary>
    /// The Download Coop server click: opens the link in the browser and gives the status line, and whether it failed.
    /// <paramref name="button"/> is the button that then picks the unzipped server (Set server folder, Choose Coop server).
    /// </summary>
    public static async Task<(string Line, bool Failed)> OpenAsync(string userAgent, string button)
    {
        var link = await LinkAsync(userAgent);
        if (!Support.ShellOpen.TryOpen(link, out _))
            return (OpenFailedLine(link), true);
        return (link == CoopInstall.ReleaseUrl ? OpenedReleaseLine : OpenedLine(button), false);
    }

    /// <summary>The status while the manifest loads, shown again on a second click meanwhile.</summary>
    public const string LookingUpLine = "Looking up Coop's server download.";

    /// <summary>The status after the server .7z opened in the browser.</summary>
    public static string OpenedLine(string button) =>
        "Your browser is downloading the Coop dedicated server (about 4.5 GB). Unzip it to a short folder like C:\\CoopServer, then click " + button + ".";

    /// <summary>The status after the release page opened instead (no manifest, or a link this tool does not trust).</summary>
    public const string OpenedReleaseLine =
        "Opened Coop's release page. The Windows dedicated server download is linked at the bottom of the release notes, not under Assets.";

    /// <summary>The status when Windows could not open the browser.</summary>
    public static string OpenFailedLine(string link) => "Could not open your browser. Open " + link + " yourself.";
}
