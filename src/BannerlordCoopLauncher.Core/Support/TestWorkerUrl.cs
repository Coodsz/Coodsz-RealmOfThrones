using System.Text.RegularExpressions;

namespace BannerlordCoopLauncher.Core.Support;

/// <summary>
/// Contributor builds: COODSZ_TEST_WORKER_URL=http://127.0.0.1:port (or http://localhost:port) swaps the base of
/// the report, crash and replies endpoints, so a live test can talk to a local worker. A missing override keeps submissions offline.
/// </summary>
public static class TestWorkerUrl
{
    private static readonly Regex Allowed = new(@"^http://(127\.0\.0\.1|localhost):[0-9]{1,5}$", RegexOptions.CultureInvariant);
    private const string Live = "https://coodsz-mod-reports.coodsz.workers.dev";

    /// <summary>The accepted override base, or null when it is unset or not a local http address.</summary>
    public static string? Base(string? value)
    {
        var text = value?.Trim().TrimEnd('/');
        return text != null && Allowed.IsMatch(text) ? text : null;
    }

    /// <summary>Swaps the live worker's base for the override. Anything else comes back unchanged.</summary>
    public static string? Apply(string? endpoint, string? overrideValue)
    {
        var b = Base(overrideValue);
        return b != null && endpoint != null && endpoint.StartsWith(Live, StringComparison.Ordinal)
            ? b + endpoint.Substring(Live.Length)
            : endpoint;
    }

    // Contributor builds may exercise the reporting client against a local test worker.
    public static string? Apply(string? endpoint)
    {
        var local = Base(Environment.GetEnvironmentVariable("COODSZ_TEST_WORKER_URL"));
        if (local is null || endpoint is null || !endpoint.StartsWith(Live + "/", StringComparison.Ordinal))
            return null;
        return Apply(endpoint, local);
    }
}
