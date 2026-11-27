using BannerlordCoopLauncher.Core.IO;
using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace BannerlordCoopLauncher.Core.Modules;

/// <summary>One mod of the player's mod list in a report: only its id and version.</summary>
public sealed record ModReportMod(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("version")] string Version);

/// <summary>What the player says about one mod.</summary>
public sealed class ModReportItem
{
    public const string Works = "works";
    public const string Broken = "broken";

    /// <summary>Works, but only together with the mod in <see cref="With"/>.</summary>
    public const string WorksWith = "works-with";

    [JsonPropertyName("id")] public string Id { get; set; } = "";
    [JsonPropertyName("name")] public string? Name { get; set; }
    [JsonPropertyName("version")] public string Version { get; set; } = "";
    [JsonPropertyName("result")] public string Result { get; set; } = "";
    [JsonPropertyName("with")] public string? With { get; set; }

    /// <summary>The tool a remembered vote was sent from (ModReportSettings.Sent only, never in a report). Votes
    /// saved before votes were kept per tool have none and count for every tool, so nothing is sent again.</summary>
    [JsonPropertyName("tool")] public string? Tool { get; set; }
}

/// <summary>
/// The anonymous report the tools send: the mods the player rates, the Coop and game versions
/// and the mod list of the setup in its load order (ids and versions). It never holds a Steam
/// id, a name, a folder path or an address; <see cref="ModReports.Build"/> drops any value that
/// looks like one. The install id is a random number made on this PC for these reports only;
/// the endpoint keeps only a hash of it, so one PC has one vote per mod version.
/// </summary>
public sealed class ModReport
{
    public const string Kind = "CoodszModReport";

    [JsonPropertyName("kind")] public string FileKind { get; set; } = Kind;
    [JsonPropertyName("schema")] public int Schema { get; set; } = 1;
    [JsonPropertyName("install")] public string Install { get; set; } = "";

    /// <summary>coop (the community list), taom (the TAOM list), ad1259 (the AD1259 pack) or be (the Burning Empires list).</summary>
    [JsonPropertyName("tool")] public string Tool { get; set; } = "";
    [JsonPropertyName("toolVersion")] public string ToolVersion { get; set; } = "";
    [JsonPropertyName("coop")] public string Coop { get; set; } = "";
    [JsonPropertyName("game")] public string Game { get; set; } = "";
    [JsonPropertyName("reports")] public List<ModReportItem> Reports { get; set; } = new();
    [JsonPropertyName("mods")] public List<ModReportMod> Mods { get; set; } = new();
}

/// <summary>This PC's report settings, kept in %AppData%\Coodsz\mod-reports.json and shared by the Coodsz tools.</summary>
public sealed class ModReportSettings
{
    [JsonPropertyName("kind")] public string Kind { get; set; } = "CoodszModReportSettings";

    /// <summary>The random install id (32 hex digits). Made the first time a report is built.</summary>
    [JsonPropertyName("install")] public string Install { get; set; } = "";

    /// <summary>Share how your mods worked after a game closes. Off until the player turns it on.</summary>
    [JsonPropertyName("askAfterGame")] public bool AskAfterGame { get; set; }

    /// <summary>The player has seen exactly what is sent (shown before the first send).</summary>
    [JsonPropertyName("previewSeen")] public bool PreviewSeen { get; set; }

    /// <summary>What this PC already sent, one line per mod and version.</summary>
    [JsonPropertyName("sent")] public List<ModReportItem> Sent { get; set; } = new();

    /// <summary>How many times in a row the player closed the bar after a game with its x.</summary>
    [JsonPropertyName("barClosed")] public int BarClosed { get; set; }

    /// <summary>The bar after a game is hidden for good (closed three times in a row).</summary>
    [JsonPropertyName("barOff")] public bool BarOff { get; set; }

    /// <summary>The one time New: report mods right here card was closed or used.</summary>
    [JsonPropertyName("introSeen")] public bool IntroSeen { get; set; }

    /// <summary>The tools (like coop-client or taom-server) whose one time New: send crash reports right from here card was closed or used.</summary>
    [JsonPropertyName("crashIntroSeen")] public List<string> CrashIntroSeen { get; set; } = new();

    /// <summary>The calm Played with mods? line under the title was closed with its x.</summary>
    [JsonPropertyName("hintClosed")] public bool HintClosed { get; set; }

    /// <summary>The Discord name the player last typed in a crash report window, filled in again next time (CrashContact).</summary>
    [JsonPropertyName("crashContact")] public string CrashContact { get; set; } = "";
}

/// <summary>What a tool shows after a game closed, see <see cref="ModReports.AfterGame"/>.</summary>
public enum ModReportPrompt
{
    None,
    /// <summary>Share how your mods worked? (the player turned Ask me after a game on).</summary>
    Window,
    /// <summary>The small bar at the top: How did your mods work?</summary>
    Bar,
    /// <summary>The crash bar with Tell everyone which mod broke.</summary>
    CrashBar
}

/// <summary>What the report bar at the top of a main window shows. A higher kind replaces a lower one.</summary>
public enum ModReportBarKind
{
    None,
    /// <summary>The one time New: report mods right here card.</summary>
    Intro,
    /// <summary>How did your mods work? after a game closed normally.</summary>
    AfterGame,
    /// <summary>The game crashed, with Tell everyone which mod broke.</summary>
    Crash
}

/// <summary>What happened to a send.</summary>
public enum ModReportSendStatus
{
    Sent,
    /// <summary>Nothing new: this PC already sent the same result for these versions.</summary>
    AlreadySent,
    RateLimited,
    Refused,
    Failed
}

public sealed record ModReportSendResult(ModReportSendStatus Status, string Message);

/// <summary>
/// One click community mod reports. With <see cref="Endpoint"/> empty the tools keep opening the
/// GitHub issue form (<see cref="CommunityCompat.ReportUrl"/>); once the endpoint is deployed
/// (server/mod-reports-worker) the Works, Broken and Works only with buttons send a small
/// anonymous report there instead, and no GitHub account is needed. Reports land as pending
/// GitHub issues that Coodsz approves before the tally counts them.
/// </summary>
public static class ModReports
{
    /// <summary>
    /// The report endpoint (the deployed worker's https address ending in /report). Empty until
    /// Coodsz deploys it: then every tool falls back to the GitHub issue form.
    /// </summary>
    public const string Endpoint = "https://coodsz-mod-reports.coodsz.workers.dev/report";

    /// <summary>
    /// The crash report endpoint: the same worker, ending in /crash (server/mod-reports-worker/src/crash.js).
    /// Send crash report, Send server crash report and Send my logs post the cleaned log zip there (CrashReports).
    /// </summary>
    public const string CrashEndpoint = "https://coodsz-mod-reports.coodsz.workers.dev/crash";

    public const int MaxReports = 25;
    public const int MaxMods = 250;

    public static string SettingsPath { get; set; } =
        Path.Combine(ToolData.RoamingRoot, "mod-reports.json");

    /// <summary>The badge tip line when a click opens the report buttons.</summary>
    public const string ClickTip = "Click to report how this mod worked. No GitHub account needed.";

    /// <summary>The badge tip line for the current endpoint: the report buttons, or the GitHub form.</summary>
    public static string ReportTip(string? endpoint = Endpoint) =>
        HasEndpoint(endpoint) ? ClickTip : CommunityCompat.ReportTip;

    /// <summary>The privacy line the report window and the README show.</summary>
    public const string PrivacyText = "Your report is anonymous. It holds the mods you rate, their versions, your result, the Coop and Bannerlord versions and the mod ids and versions of this setup in load order. No Steam id, name, folder path or address is sent. A random number made on this PC lets the server keep one vote per mod version; the server keeps only a scrambled copy of it. Coodsz checks reports before they count.";

    private static readonly Regex IdPattern = new(@"^[\p{L}\p{N}_.\-' ()+&!]{1,100}$", RegexOptions.CultureInvariant);
    private static readonly Regex VersionPattern = new(@"^[\p{L}\p{N}_.\-+ ]{0,40}$", RegexOptions.CultureInvariant);
    private static readonly Regex InstallPattern = new("^[0-9a-f]{32}$", RegexOptions.CultureInvariant);
    // A SteamID64 (7656119 and ten more digits) anywhere in a value.
    private static readonly Regex SteamIdPattern = new(@"7656119\d{10}", RegexOptions.CultureInvariant);

    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    private static readonly object Gate = new();
    private static bool _askedThisSession;

    /// <summary>True for an https address: the report buttons are used instead of the GitHub form.</summary>
    public static bool HasEndpoint(string? endpoint = Endpoint) =>
        Uri.TryCreate((endpoint ?? "").Trim(), UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps;

    // ----- Building the report -----

    /// <summary>A mod id the report may carry, or null (empty, too long, a path, a Steam id).</summary>
    public static string? CleanId(string? id)
    {
        var text = (id ?? "").Trim();
        return IdPattern.IsMatch(text) && !SteamIdPattern.IsMatch(text) ? text : null;
    }

    /// <summary>A version the report may carry; anything else becomes empty.</summary>
    public static string CleanVersion(string? version)
    {
        var text = (version ?? "").Trim();
        return VersionPattern.IsMatch(text) && !SteamIdPattern.IsMatch(text) ? text : "";
    }

    public static bool IsResult(string? result) =>
        result is ModReportItem.Works or ModReportItem.Broken or ModReportItem.WorksWith;

    /// <summary>
    /// The report to send. Items without a valid id or result are left out, a Works only with
    /// without a valid other mod becomes Works, and the mod list keeps its order without repeats.
    /// Names that are not plain mod names are dropped (the id is enough).
    /// </summary>
    public static ModReport Build(string install, string tool, string? toolVersion, string? coopVersion, string? gameVersion,
        IEnumerable<ModReportItem> items, IEnumerable<ModReportMod> mods)
    {
        var report = new ModReport
        {
            Install = InstallPattern.IsMatch(install ?? "") ? install! : "",
            Tool = tool is "taom" or "ad1259" or "be" or "rot" ? tool : "coop",
            ToolVersion = CleanVersion(toolVersion),
            Coop = CleanVersion(coopVersion),
            Game = CleanVersion(gameVersion)
        };

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var mod in mods)
        {
            if (report.Mods.Count >= MaxMods)
                break;
            if (CleanId(mod?.Id) is { } id && seen.Add(id))
                report.Mods.Add(new ModReportMod(id, CleanVersion(mod!.Version)));
        }

        var rated = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in items)
        {
            if (report.Reports.Count >= MaxReports)
                break;
            if (CleanId(item?.Id) is not { } id || !IsResult(item!.Result) || !rated.Add(id))
                continue;
            var with = item.Result == ModReportItem.WorksWith ? CleanId(item.With) : null;
            if (with != null && with.Equals(id, StringComparison.OrdinalIgnoreCase))
                with = null;
            var name = CleanId(item.Name);
            report.Reports.Add(new ModReportItem
            {
                Id = id,
                Name = name == null || name.Equals(id, StringComparison.Ordinal) ? null : name,
                Version = CleanVersion(item.Version),
                Result = with == null && item.Result == ModReportItem.WorksWith ? ModReportItem.Works : item.Result,
                With = with
            });
        }

        return report;
    }

    /// <summary>Exactly the text that is sent, for the window to show before the first send.</summary>
    public static string PreviewText(ModReport report) => JsonSerializer.Serialize(report, Options);

    /// <summary>The ids the mod list of a module order gives: every mod that is not an official module.</summary>
    public static List<ModReportMod> ModList(IEnumerable<(string Id, string? Version)> loadOrder) =>
        loadOrder
            .Where(mod => !string.IsNullOrWhiteSpace(mod.Id) && !CoopJoinRules.IsIgnored(mod.Id.Trim()))
            .Select(mod => new ModReportMod(mod.Id.Trim(), (mod.Version ?? "").Trim()))
            .ToList();

    // ----- One vote per mod version -----

    /// <summary>The items this PC has not sent yet from this tool with the same result (one vote per tool and
    /// mod version, like the worker keeps them; a changed result is sent again and replaces the old vote on the server).</summary>
    public static List<ModReportItem> NotSentYet(ModReportSettings settings, IEnumerable<ModReportItem> items, string tool) =>
        items.Where(item => Remembered(settings, item, tool) is not { } old || old.Result != item.Result
            || !string.Equals(old.With ?? "", item.With ?? "", StringComparison.OrdinalIgnoreCase)).ToList();

    /// <summary>Keeps what was sent: one line per tool, mod and version, the newest replaces the old one.</summary>
    public static void RememberSent(ModReportSettings settings, IEnumerable<ModReportItem> items, string tool)
    {
        foreach (var item in items)
        {
            settings.Sent.RemoveAll(old => SameVote(old, item) && string.Equals(old.Tool, tool, StringComparison.OrdinalIgnoreCase));
            settings.Sent.Add(new ModReportItem { Id = item.Id, Version = item.Version, Result = item.Result, With = item.With, Tool = tool });
        }
    }

    /// <summary>The result this PC already sent from this tool for this mod version, or null.</summary>
    public static ModReportItem? SentFor(ModReportSettings settings, string moduleId, string? version, string tool) =>
        Remembered(settings, new ModReportItem { Id = moduleId, Version = version ?? "" }, tool);

    // The tool's own vote for this mod version, else one saved before votes had a tool.
    private static ModReportItem? Remembered(ModReportSettings settings, ModReportItem item, string tool) =>
        settings.Sent.LastOrDefault(old => SameVote(old, item) && string.Equals(old.Tool, tool, StringComparison.OrdinalIgnoreCase))
        ?? settings.Sent.LastOrDefault(old => SameVote(old, item) && old.Tool == null);

    /// <summary>The key one vote is kept under: the mod id (any case) and its exact version.</summary>
    public static string VoteKey(string install, string moduleId, string? version) =>
        install + "|" + ModCompatDatabase.Normalize(moduleId) + "|" + (version ?? "").Trim().ToLowerInvariant();

    private static bool SameVote(ModReportItem a, ModReportItem b) =>
        a.Id.Trim().Equals(b.Id.Trim(), StringComparison.OrdinalIgnoreCase) && (a.Version ?? "").Trim() == (b.Version ?? "").Trim();

    // ----- The opt in prompt after a game -----

    /// <summary>True when Share how your mods worked should open after a game closed: the player
    /// turned it on, the endpoint is set, there are mods to rate and it has not opened this run.</summary>
    public static bool ShouldAskAfterGame(ModReportSettings settings, int modCount, string? endpoint = Endpoint)
    {
        lock (Gate)
            return settings.AskAfterGame && HasEndpoint(endpoint) && modCount > 0 && !_askedThisSession;
    }

    /// <summary>The prompt opened this run; it stays closed until the tool starts again.</summary>
    public static void MarkAsked()
    {
        lock (Gate)
            _askedThisSession = true;
    }

    /// <summary>For tests: a new run.</summary>
    public static void ResetSession()
    {
        lock (Gate)
        {
            _askedThisSession = false;
            _barShownThisSession = false;
        }
    }

    // ----- The report bar, the crash button and the one time card -----

    /// <summary>The bar after a game hides for good after this many x clicks in a row.</summary>
    public const int BarCloseLimit = 3;

    private static bool _barShownThisSession;

    /// <summary>
    /// What to show after a game closed. Nothing without the endpoint or mods to rate. The
    /// window when the player turned Ask me after a game on (once per run). Otherwise the
    /// crash bar after a crash, and after a normal close the small bar, at most once per run
    /// and never again once the player closed it three times in a row.
    /// </summary>
    public static ModReportPrompt AfterGame(ModReportSettings settings, int modCount, bool crashed, string? endpoint = Endpoint)
    {
        if (!HasEndpoint(endpoint) || modCount <= 0)
            return ModReportPrompt.None;
        if (ShouldAskAfterGame(settings, modCount, endpoint))
            return ModReportPrompt.Window;
        if (crashed)
            return ModReportPrompt.CrashBar;
        return ShouldShowBar(settings, modCount, endpoint) ? ModReportPrompt.Bar : ModReportPrompt.None;
    }

    /// <summary>True when How did your mods work? may show: endpoint, mods, not hidden for good, not shown this run.</summary>
    public static bool ShouldShowBar(ModReportSettings settings, int modCount, string? endpoint = Endpoint)
    {
        lock (Gate)
            return HasEndpoint(endpoint) && modCount > 0 && !settings.BarOff && !_barShownThisSession;
    }

    /// <summary>The bar showed this run; it stays away until the tool starts again.</summary>
    public static void MarkBarShown()
    {
        lock (Gate)
            _barShownThisSession = true;
    }

    /// <summary>The x on the bar after a game. The third x in a row hides it for good and returns true.</summary>
    public static bool CloseBar(ModReportSettings settings)
    {
        settings.BarClosed++;
        if (settings.BarClosed >= BarCloseLimit)
            settings.BarOff = true;
        return settings.BarOff;
    }

    /// <summary>Report on the bar: the x clicks before it no longer count as in a row.</summary>
    public static void UsedBar(ModReportSettings settings) => settings.BarClosed = 0;

    /// <summary>True when a game ended without a crash: no crash report and a normal exit code. The
    /// AD1259 client reads it here, since it keeps no per PC results (ModResults).</summary>
    public static bool ClosedNormally(int? exitCode, string? crashReport) => ModResults.ClosedNormally(exitCode, crashReport);

    /// <summary>True for a mod a player can rate: not an official module, not Coop itself and not a base game module.</summary>
    public static bool Rates(string? moduleId, bool baseGame = false) => !baseGame && UnknownModBadge.Applies(moduleId);

    /// <summary>The crash button shows after a crash whenever reports can be sent and the run had mods to rate.</summary>
    public static bool OffersCrashReport(int modCount, string? endpoint = Endpoint) => HasEndpoint(endpoint) && modCount > 0;

    /// <summary>What Report a mod says when the setup has no extra mod to rate, like plain TAOM.</summary>
    public const string NothingToRateText = "No extra mods in this setup to rate. Add a mod first, then report how it worked.";

    /// <summary>The message for Report a mod when there is nothing to rate, or null when the report window can open.</summary>
    public static string? NothingToRate(int rateCount) => rateCount <= 0 ? NothingToRateText : null;

    /// <summary>The calm Played with mods? line under the title: shown until the player sends a report
    /// or closes it with its x, then gone for good.</summary>
    public static bool ShowsHint(ModReportSettings settings, string? endpoint = Endpoint) =>
        HasEndpoint(endpoint) && !settings.HintClosed && settings.Sent.Count == 0;

    /// <summary>The one time New: report mods right here card, until it is closed or used once.</summary>
    public static bool ShouldShowIntro(ModReportSettings settings, string? endpoint = Endpoint) =>
        HasEndpoint(endpoint) && !settings.IntroSeen;

    /// <summary>True when a bar of this kind may take the place of what the bar shows now. The crash
    /// bar replaces anything, the bar after a game replaces the card, and the card only shows alone.</summary>
    public static bool Replaces(ModReportBarKind next, ModReportBarKind current) =>
        next != ModReportBarKind.None && (next > current || next == ModReportBarKind.Crash);

    /// <summary>The green chip next to a mod, like 12 say works, or null when nobody said so.</summary>
    public static string? WorksChip(int works) => works > 0 ? works.ToString(CultureInfo.InvariantCulture) + " say works" : null;

    /// <summary>The red chip next to a mod, like 2 say broken, or null when nobody said so.</summary>
    public static string? BrokenChip(int broken) => broken > 0 ? broken.ToString(CultureInfo.InvariantCulture) + " say broken" : null;

    /// <summary>The tip line with both counts, like Players reported: 12 say works, 2 say broken.</summary>
    public static string CountsLine(int works, int broken) =>
        "Players reported: " + works.ToString(CultureInfo.InvariantCulture) + " say works, " + broken.ToString(CultureInfo.InvariantCulture) + " say broken.";

    // ----- Settings file -----

    public static ModReportSettings LoadSettings(string? path = null)
    {
        path ??= SettingsPath;
        try
        {
            if (File.Exists(path) && JsonSerializer.Deserialize<ModReportSettings>(File.ReadAllText(path), Options) is { } settings)
            {
                settings.Sent = (settings.Sent ?? new()).Where(item => item != null && item.Id.Trim().Length > 0).ToList();
                settings.Install ??= "";
                settings.CrashContact ??= "";
                settings.CrashIntroSeen = (settings.CrashIntroSeen ?? new()).Where(tool => !string.IsNullOrWhiteSpace(tool)).ToList();
                return settings;
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
        }

        return new ModReportSettings();
    }

    /// <summary>Saves the settings. A file problem only loses the change.</summary>
    public static bool SaveSettings(ModReportSettings settings, string? path = null)
    {
        path ??= SettingsPath;
        try
        {
            var directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(directory))
                Directory.CreateDirectory(directory);
            var temp = path + ".tmp";
            File.WriteAllText(temp, JsonSerializer.Serialize(settings, Options));
            File.Move(temp, path, overwrite: true);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    /// <summary>The install id, made (32 random hex digits) and kept the first time.</summary>
    public static string InstallId(ModReportSettings settings) =>
        settings.Install is { } id && InstallPattern.IsMatch(id)
            ? id
            : settings.Install = Convert.ToHexString(RandomNumberGenerator.GetBytes(16)).ToLowerInvariant();

    // ----- Sending -----

    /// <summary>Posts the report to the endpoint. Never throws for a network problem.</summary>
    public static async Task<ModReportSendResult> SendAsync(HttpClient http, ModReport report, string? endpoint = Endpoint,
        CancellationToken cancellationToken = default)
    {
        if (!HasEndpoint(endpoint))
            return new ModReportSendResult(ModReportSendStatus.Failed, "Reports cannot be sent from this version yet.");
        if (report.Reports.Count == 0)
            return new ModReportSendResult(ModReportSendStatus.AlreadySent, "There is nothing new to send.");
        try
        {
            using var content = JsonContent.Create(report, options: Options);
            using var response = await http.PostAsync(Support.TestWorkerUrl.Apply(endpoint!.Trim()), content, cancellationToken).ConfigureAwait(false);
            if (response.StatusCode == HttpStatusCode.TooManyRequests)
                return new ModReportSendResult(ModReportSendStatus.RateLimited, "Too many reports from this PC right now. Try again later.");
            if (!response.IsSuccessStatusCode)
                return new ModReportSendResult(ModReportSendStatus.Refused,
                    "The report was turned down (" + ((int)response.StatusCode).ToString(CultureInfo.InvariantCulture) + ").");
            return new ModReportSendResult(ModReportSendStatus.Sent, "Thank you. Your report was sent. It counts once Coodsz has checked it.");
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or IOException)
        {
            return new ModReportSendResult(ModReportSendStatus.Failed, "Could not send the report. Check the internet connection and try again.");
        }
    }
}
