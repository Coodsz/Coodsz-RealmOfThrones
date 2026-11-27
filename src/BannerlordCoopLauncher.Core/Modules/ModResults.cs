using BannerlordCoopLauncher.Core.IO;
using System.Globalization;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace BannerlordCoopLauncher.Core.Modules;

/// <summary>One result this PC saw for a mod: the game on this PC, or the server this PC runs.</summary>
public sealed class ModResult
{
    public const string Worked = "worked";
    public const string Crashed = "crashed";
    public const string Passed = "passed";
    public const string Failed = "failed";

    public const string ThisPc = "game";
    public const string YourServer = "server";

    [JsonPropertyName("id")] public string Id { get; set; } = "";
    [JsonPropertyName("version")] public string Version { get; set; } = "";
    [JsonPropertyName("result")] public string Result { get; set; } = "";
    [JsonPropertyName("where")] public string Where { get; set; } = "";
    /// <summary>yyyy-MM-dd HH:mm local time (results taken from older files may have only the day).</summary>
    [JsonPropertyName("date")] public string Date { get; set; } = "";
    [JsonPropertyName("reason")] public string Reason { get; set; } = "";
}

/// <summary>The small second pill next to the list badge. Kind is Worked, Crashed, Passed or Failed.</summary>
public sealed record ModResultPill(string Kind, string Text, string Tip, bool Older)
{
    public bool Good => Kind is "Worked" or "Passed";

    /// <summary>The hover of the pill: its own text first, so a pill trimmed in a narrow column still reads in full.</summary>
    public string Hover => Text + "\n" + Tip;

    /// <summary>The same pill in the current language, for rows that bind it.</summary>
    public ModResultPill Translated() => this with
    {
        Text = Localization.Translator.T(Text),
        Tip = string.Join("\n", Tip.Split('\n').Select(Localization.Translator.T))
    };
}

/// <summary>
/// The player's own test results, kept per PC in %AppData%\Coodsz\mod-results.json and shared by
/// every Coodsz tool on that PC: the TAOM server tool's "Test mods on the server", a game session
/// that joined and closed normally, and Find the crashing mod. A mod keeps one result per version,
/// the newest. A newer version of the mod shows the old result dimmed as an older version.
/// </summary>
public static class ModResults
{
    public static string DefaultPath { get; set; } =
        Path.Combine(ToolData.RoamingRoot, "mod-results.json");

    private static readonly object Gate = new();
    private static (string Path, DateTime Stamp, List<ModResult> Results)? _cache;

    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    private sealed class ResultsFile
    {
        [JsonPropertyName("kind")] public string Kind { get; set; } = "CoodszModResults";
        [JsonPropertyName("schemaVersion")] public int SchemaVersion { get; set; } = 1;
        [JsonPropertyName("results")] public List<ModResult> Results { get; set; } = new();
    }

    public static string Now() => DateTime.Now.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);

    /// <summary>The saved results. An unreadable or missing file reads as none.</summary>
    public static List<ModResult> Load(string? path = null)
    {
        path ??= DefaultPath;
        lock (Gate)
        {
            try
            {
                if (!File.Exists(path))
                    return new List<ModResult>();
                var stamp = File.GetLastWriteTimeUtc(path);
                if (_cache is { } cache && cache.Path == path && cache.Stamp == stamp)
                    return cache.Results.ToList();
                var file = JsonSerializer.Deserialize<ResultsFile>(File.ReadAllText(path), Options);
                var results = file?.Results?.Where(result => result != null && result.Id.Trim().Length > 0).ToList() ?? new List<ModResult>();
                _cache = (path, stamp, results);
                return results.ToList();
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
            {
                return new List<ModResult>();
            }
        }
    }

    /// <summary>Adds results; each replaces the one kept for the same mod and version. Never throws
    /// for a file problem (a result that cannot be saved is only lost).</summary>
    public static bool Record(IEnumerable<ModResult> results, string? path = null)
    {
        path ??= DefaultPath;
        var adding = results.Where(result => result.Id.Trim().Length > 0).ToList();
        if (adding.Count == 0)
            return false;
        lock (Gate)
        {
            try
            {
                var kept = Load(path);
                foreach (var result in adding)
                {
                    result.Id = result.Id.Trim();
                    result.Version = (result.Version ?? "").Trim();
                    if (result.Date.Length == 0)
                        result.Date = Now();
                    kept.RemoveAll(old => Same(old, result));
                    kept.Add(result);
                }

                var directory = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(directory))
                    Directory.CreateDirectory(directory);
                var temp = path + ".tmp";
                File.WriteAllText(temp, JsonSerializer.Serialize(new ResultsFile { Results = kept }, Options));
                File.Move(temp, path, overwrite: true);
                _cache = null;
                return true;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                return false;
            }
        }
    }

    public static bool Record(ModResult result, string? path = null) => Record(new[] { result }, path);

    /// <summary>Adds the server load test results this PC's file does not have yet (or has older).</summary>
    public static bool ImportServerTests(IEnumerable<ModResult> serverResults, string? path = null)
    {
        var kept = Load(path);
        var missing = serverResults.Where(result => !kept.Any(old => Same(old, result)
            && string.CompareOrdinal(Day(old.Date), Day(result.Date)) >= 0)).ToList();
        return missing.Count > 0 && Record(missing, path);
    }

    /// <summary>
    /// The pill for a mod, or <see langword="null"/> when this PC has no result for it. The result for
    /// the version on this PC wins; without one the newest result of another version shows as older.
    /// An empty version takes the newest result as it is.
    /// </summary>
    public static ModResultPill? Pill(string? moduleId, string? version, IReadOnlyList<ModResult>? results = null)
    {
        var id = (moduleId ?? "").Trim();
        if (id.Length == 0)
            return null;
        results ??= Load();
        var mine = results.Where(result => result.Id.Equals(id, StringComparison.OrdinalIgnoreCase)).ToList();
        if (mine.Count == 0)
            return null;
        var current = (version ?? "").Trim();
        var match = current.Length == 0 ? Newest(mine) : Newest(mine.Where(result => result.Version == current));
        if (match != null)
            return Describe(match, older: false, current);
        return Describe(Newest(mine)!, older: true, current);
    }

    /// <summary>The newest result: the latest date, and the later line when the dates are equal.</summary>
    private static ModResult? Newest(IEnumerable<ModResult> results)
    {
        ModResult? best = null;
        foreach (var result in results)
            if (best == null || string.CompareOrdinal(result.Date, best.Date) >= 0)
                best = result;
        return best;
    }

    private static ModResultPill Describe(ModResult result, bool older, string currentVersion)
    {
        var server = result.Where == ModResult.YourServer;
        var kind = result.Result switch
        {
            ModResult.Crashed => "Crashed",
            ModResult.Passed => "Passed",
            ModResult.Failed => "Failed",
            _ => server ? "Passed" : "Worked"
        };
        var text = (kind, older) switch
        {
            ("Crashed", false) => "Crashed for you",
            ("Crashed", true) => "Crashed for you, older version",
            ("Passed", false) => "Passed on your server",
            ("Passed", true) => "Passed on your server, older version",
            ("Failed", false) => "Failed on your server",
            ("Failed", true) => "Failed on your server, older version",
            (_, false) => "Worked for you",
            _ => "Worked for you, older version"
        };
        var lines = new List<string>();
        var day = Day(result.Date);
        if (day.Length > 0 && server)
            lines.Add("Tested on your server on " + day + ".");
        else if (day.Length > 0)
            lines.Add("Seen on this PC on " + day + ".");
        if (result.Reason.Trim().Length > 0)
            lines.Add(result.Reason.Trim());
        if (result.Version.Length > 0)
            lines.Add("Mod version " + result.Version + ".");
        if (older && currentVersion.Length > 0)
            lines.Add("This result is for an older version. The mod on this PC is " + currentVersion + " now.");
        else if (older)
            lines.Add("This result is for an older version.");
        lines.Add("Only this PC keeps this result.");
        return new ModResultPill(kind, text, string.Join("\n", lines), older);
    }

    private static bool Same(ModResult a, ModResult b) =>
        a.Id.Trim().Equals(b.Id.Trim(), StringComparison.OrdinalIgnoreCase) && (a.Version ?? "").Trim() == (b.Version ?? "").Trim();

    private static string Day(string? date) => (date ?? "").Trim() is { Length: >= 10 } text ? text[..10] : (date ?? "").Trim();

    /// <summary>
    /// True when a game that closed left no crash report and no hard crash exit code (the same
    /// test StartupCrashExplain uses: 0 or -1 is a normal close).
    /// </summary>
    public static bool ClosedNormally(int? exitCode, string? crashReport) =>
        string.IsNullOrWhiteSpace(crashReport) && exitCode is null or 0 or -1;

    /// <summary>"worked" for each extra mod of a session that joined the server and closed normally.</summary>
    public static IReadOnlyList<ModResult> SessionWorked(IEnumerable<(string Id, string? Version)> extras, string? date = null)
    {
        var when = date ?? Now();
        return extras
            .Where(mod => !string.IsNullOrWhiteSpace(mod.Id))
            .GroupBy(mod => mod.Id.Trim(), StringComparer.OrdinalIgnoreCase)
            .Select(group => new ModResult
            {
                Id = group.Key,
                Version = (group.First().Version ?? "").Trim(),
                Result = ModResult.Worked,
                Where = ModResult.ThisPc,
                Date = when,
                Reason = "Joined the server with this mod and the game closed normally."
            })
            .ToList();
    }
}

/// <summary>The neutral grey badge for a mod no list knows yet.</summary>
public static class UnknownModBadge
{
    public const string Text = "Unknown, not tested";

    public const string Tip = "Nobody has reported this mod yet. Test it with Find the crashing mod, or ask the host to run Test mods on the server.";

    /// <summary>For Coop and Friends and Coodsz Host, which go by the community list.</summary>
    public const string CommunityTip = "Nobody has reported this mod yet. Try it once with Test to main menu in Coop and Friends, then report how it worked.";

    /// <summary>
    /// False for modules that are not community mods, which get no badge: the official modules,
    /// Coop itself, the DedicatedServer.* host modules and Coop Save Rescue.
    /// </summary>
    public static bool Applies(string? moduleId)
    {
        var id = (moduleId ?? "").Trim();
        if (id.Length == 0 || CoopJoinRules.IsIgnored(id))
            return false;
        return !id.Equals("Coop", StringComparison.OrdinalIgnoreCase) && !id.Equals("CoopNightly", StringComparison.OrdinalIgnoreCase);
    }
}
