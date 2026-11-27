using System.Globalization;
using System.IO.Compression;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using BannerlordCoopLauncher.Core.Modules;
using BannerlordCoopLauncher.Core.Server;

namespace BannerlordCoopLauncher.Core.Support;

/// <summary>One file of the crash report zip, as the consent window lists it and the summary carries it.</summary>
public sealed record CrashReportFile(
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("size")] long Size);

/// <summary>Why the logs are sent.</summary>
public static class CrashReason
{
    /// <summary>The game crashed (the crash bar of a client).</summary>
    public const string Crash = "crash";

    /// <summary>The server crashed or stopped by itself (the bar of a server tool).</summary>
    public const string ServerCrash = "server-crash";

    /// <summary>Send my logs: no crash, a stuck join, a desync or lag.</summary>
    public const string Logs = "logs";
}

/// <summary>What went wrong, picked in the consent window of Send my logs.</summary>
public static class CrashProblem
{
    public const string Join = "join";
    public const string Desync = "desync";
    public const string Lag = "lag";
    public const string Other = "other";

    /// <summary>The choices in the order the window shows them, with their labels.</summary>
    public static IReadOnlyList<(string Id, string Label)> Choices { get; } =
    [
        (Join, "Stuck joining or cannot connect"),
        (Desync, "Desync, players see different things"),
        (Lag, "Lag or low FPS"),
        (Other, "Something else")
    ];

    public static bool IsKnown(string? problem) => problem is Join or Desync or Lag or Other;
}

/// <summary>
/// The small summary sent next to the zip. Only these fields go to the worker: the tool, the
/// versions, the one crash line, a suspected mod and the file list. No log text otherwise, no
/// Steam id, name, folder path or address (CrashReports.BuildSummary cleans every value).
/// </summary>
public sealed class CrashReportSummary
{
    public const string Kind = "CoodszCrashReport";

    [JsonPropertyName("kind")] public string FileKind { get; set; } = Kind;
    [JsonPropertyName("schema")] public int Schema { get; set; } = 1;
    [JsonPropertyName("install")] public string Install { get; set; } = "";

    /// <summary>coop (Coop and Friends and Coodsz Host), taom, ad1259 or be (Burning Empires).</summary>
    [JsonPropertyName("tool")] public string Tool { get; set; } = "coop";

    /// <summary>client or server.</summary>
    [JsonPropertyName("side")] public string Side { get; set; } = "client";
    [JsonPropertyName("reason")] public string Reason { get; set; } = CrashReason.Crash;
    [JsonPropertyName("problem")] public string? Problem { get; set; }
    [JsonPropertyName("toolVersion")] public string ToolVersion { get; set; } = "";
    [JsonPropertyName("coop")] public string Coop { get; set; } = "";
    [JsonPropertyName("game")] public string Game { get; set; } = "";
    [JsonPropertyName("crashLine")] public string CrashLine { get; set; } = "";
    [JsonPropertyName("suspectedMod")] public string? SuspectedMod { get; set; }
    [JsonPropertyName("files")] public List<CrashReportFile> Files { get; set; } = new();

    /// <summary>The known crash the tool matched before sending (crash-signatures.json), or only the signatures version for an unknown one. Null when no triage ran.</summary>
    [JsonPropertyName("triage")] public CrashReportTriage? Triage { get; set; }

    /// <summary>The player's Discord name, so Coodsz can reach them about the report (CrashContact). Never in the zip. Null when none was typed.</summary>
    [JsonPropertyName("contact")] public string? Contact { get; set; }

    /// <summary>The report's reply key (ReportReplies.NewKey), set only on the copy that is sent (WithReplyKey). Never in the zip, the preview or a log.</summary>
    [JsonPropertyName("replyKey")] public string? ReplyKey { get; set; }

    /// <summary>No, still broken: the earlier report's code, its reply key and the steps answer (ReportFollowUps). Set only on the copy that is sent. Never in the zip, the preview or a log.</summary>
    [JsonPropertyName("followUp")] public CrashFollowUp? FollowUp { get; set; }
}

/// <summary>summary.followUp: the worker adds the report to this earlier report's issue when the key opens it.</summary>
public sealed class CrashFollowUp
{
    [JsonPropertyName("code")] public string Code { get; set; } = "";
    [JsonPropertyName("key")] public string Key { get; set; } = "";
    [JsonPropertyName("steps")] public string Steps { get; set; } = FollowUpSteps.All;
}

/// <summary>
/// What a tool knows when it offers a crash report. Collect packs the logs into the zip at the
/// path it is given with the limits it is given (text only, shorter ends, the known passwords
/// taken out) and returns the lines about what it left out. It runs off the window thread, so it
/// only uses values the tool read before.
/// </summary>
public sealed class CrashReportRequest
{
    public string Tool { get; init; } = "coop";
    public string Side { get; init; } = "client";
    public string Reason { get; init; } = CrashReason.Crash;
    public string? Problem { get; init; }
    public string ToolVersion { get; init; } = "";
    public string Coop { get; init; } = "";
    public string Game { get; init; } = "";
    public string? CrashLine { get; init; }
    public string? SuspectedMod { get; init; }

    /// <summary>The exit code when the tool knows it (Coodsz Host does). It wins over the one in the logs for the crash triage.</summary>
    public int? ExitCode { get; init; }

    /// <summary>The passwords this tool knows (the join or server password). They are taken out of every file.</summary>
    public IReadOnlyList<string> Secrets { get; init; } = Array.Empty<string>();

    /// <summary>The start of the zip's file name, like coodsz-client-logs.</summary>
    public string FileStem { get; init; } = "coodsz-logs";

    /// <summary>The effective load order with versions, for modules.json.</summary>
    public IReadOnlyList<(string Id, string? Version)> Modules { get; init; } = Array.Empty<(string Id, string? Version)>();

    public required Func<string, LogZipLimits, IReadOnlyList<string>> Collect { get; init; }

    /// <summary>
    /// Finds the one crash dump this report may carry (CrashDumps.LatestClientDump or
    /// LatestServerDump with CrashDumps.Since and Until), or null. Runs off the window thread. Null: no dump.
    /// </summary>
    public Func<string?>? FindDump { get; init; }

    /// <summary>The server folders this tool runs. A crash stack read may load the DAC of their engine\dotnet (DacTrust).</summary>
    public IReadOnlyList<string> ServerFolders { get; init; } = Array.Empty<string>();

    /// <summary>
    /// The folders whose Coodsz module DLLs go into coodsz-modules.txt with their SHA256 (ModuleFingerprints.Roots).
    /// Empty: no such file.
    /// </summary>
    public IReadOnlyList<(string Label, string Folder)> ModuleRoots { get; init; } = Array.Empty<(string Label, string Folder)>();

    /// <summary>Add the crash dump: the consent window's checkbox sets it, for this send only. On by default.</summary>
    public bool IncludeDump { get; set; } = true;

    public bool IsServer => Side == "server";
}

/// <summary>A packed report, ready for the consent window.</summary>
public sealed record CrashReportPack(string ZipPath, CrashReportSummary Summary, IReadOnlyList<string> Skipped)
{
    /// <summary>Which files the cleaner changed or cut, for report.json when the player's extras are added.</summary>
    public LogZipTrace? Trace { get; init; }

    /// <summary>True when the crash triage ran (a crash report or a Send my logs of Coop and Friends, Coodsz Host, the AD1259, TAOM or Burning Empires tools).</summary>
    public bool TriageRan { get; init; }

    /// <summary>The known crash the triage found, or null.</summary>
    public CrashSignatureMatch? Match { get; init; }

    /// <summary>The crash dump that fits and can be added (not in ZipPath yet; WithExtras adds it), or null.</summary>
    public CrashDumpPick? Dump { get; init; }

    public long ZipBytes => File.Exists(ZipPath) ? new FileInfo(ZipPath).Length : 0;
    public long TotalBytes => Summary.Files.Sum(file => file.Size);
}

public enum CrashReportSendStatus
{
    Sent,
    RateLimited,
    TooLarge,
    Refused,
    Failed
}

/// <summary>
/// The send result. Detail is for the tool's log: the HTTP status and the worker's own error text.
/// Replies is true when the worker answered "replies": true, so the reply key is kept (ReportReplies).
/// Filed is the worker's "filed": true when the issue (or the follow up comment) went to GitHub, null
/// when the answer had none.
/// </summary>
public sealed record CrashReportSendResult(CrashReportSendStatus Status, string? Code, string Message, string? Detail = null, bool Replies = false, bool? Filed = null);

/// <summary>The buttons of the crash bar of a client, biggest first.</summary>
public enum CrashBarButton
{
    /// <summary>Send crash report: the biggest button.</summary>
    SendCrashReport,

    /// <summary>Tell everyone which mod broke: the second action.</summary>
    TellWhichMod,

    /// <summary>Save a copy instead: the small link.</summary>
    SaveCopy
}

/// <summary>Which one time card a client shows. A tool shows one card at a time.</summary>
public enum IntroCard
{
    None,

    /// <summary>New: send crash reports right from here.</summary>
    CrashReports,

    /// <summary>New: report mods right here.</summary>
    ModReports
}

/// <summary>
/// Crash reports: Send crash report (the crash bar of a client), Send server crash report (the bar
/// of a server tool after the server crashed) and Send my logs (on the main page of every tool).
/// The tool packs the logs it already packs for Save a copy, text only and cleaned
/// (LogZipBuilder.Clean and LogScrubber), shows the file list and summary, and sends nothing
/// until the player clicks Send. The one newest crash dump of this crash goes too while Add the
/// crash dump is ticked and it fits CrashDumps.Budget. The worker keeps the zip 30 days and answers with a short code
/// like CR-7KQ2M that the player posts in Discord instead of the files.
/// </summary>
public static class CrashReports
{
    /// <summary>The crash endpoint: ModReports.CrashEndpoint, the report worker ending in /crash.</summary>
    public const string Endpoint = ModReports.CrashEndpoint;

    /// <summary>The worker refuses a zip over 8 MiB (MAX_CRASH_ZIP). The tools stay well under it.</summary>
    public const long WorkerMaxZipBytes = 8L * 1024 * 1024;
    public const long MaxZipBytes = 7_500_000;

    /// <summary>The end of each log the first pack keeps; FitToSize shortens it further when the zip is too big.</summary>
    public const long FirstTextBytes = 4_000_000;
    public static IReadOnlyList<long> ShorterEnds { get; } = [2_000_000, 1_000_000, 500_000, 250_000, 100_000, 40_000];

    public const int MaxCrashLine = 300;
    public const int MaxFiles = 500;

    public const string PostOnlyCode = "Post only this code in Discord, not your log files.";
    /// <summary>PrivacyText without the crash dump sentence, for a report with no dump to add.</summary>
    public const string PrivacyTextNoDump = "Only the logs are cleaned. Your Windows name, PC name, Steam ids, addresses and passwords are taken out of them before they are packed. Logs can still show in game player names. Saves are left out. Only Coodsz can open the report, and it is deleted after 30 days.";

    public const string PrivacyText = "Only the logs are cleaned. Your Windows name, PC name, Steam ids, addresses and passwords are taken out of them before they are packed. Logs can still show in game player names. Saves are left out. The crash dump is not cleaned and is only sent while Add the crash dump is ticked. Only Coodsz can open the report, and it is deleted after 30 days.";

    private static readonly Regex CodePattern = new("^CR-[23456789ABCDEFGHJKMNPQRSTUVWXYZ]{5}$", RegexOptions.CultureInvariant);
    private static readonly Regex StrongLine = new(@"(?i)\b(?:unhandled exception|fatal error|EXCEPTION_ACCESS_VIOLATION|access violation|stack overflow|out of memory)\b", RegexOptions.CultureInvariant);
    private static readonly Regex TypedException = new(@"\b(?:[A-Z]\w*\.)*[A-Z]\w*Exception\b\s*:", RegexOptions.CultureInvariant);
    // "crashes" too: the start script's own refusals say a mod "crashes it" (crash report #32).
    private static readonly Regex AnyException = new(@"(?i)\bexception\b|\bcrash(?:ed|es)?\b", RegexOptions.CultureInvariant);
    // A warning that something "may still crash" (the start script's headless loading note, which the
    // console wraps onto its own line) says nothing crashed. Live test 2026-10-26 showed it as the cause.
    private static readonly Regex MayCrash = new(@"(?i)\b(?:may|might|can|could)\s+(?:still\s+)?crash\b", RegexOptions.CultureInvariant);

    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    /// <summary>True for an https address: the crash buttons show.</summary>
    public static bool HasEndpoint(string? endpoint = Endpoint) => ModReports.HasEndpoint(endpoint);

    /// <summary>Where the packed zips wait until they are sent or saved. Older ones are cleared.</summary>
    public static string TempFolder() => Path.Combine(Path.GetTempPath(), "Coodsz", "crash-reports");

    // ----- Show and hide rules -----

    /// <summary>The one time card is kept per tool and side, like coop-client or taom-server.</summary>
    public static string IntroKey(string tool, string side) => tool + "-" + side;

    /// <summary>The New: send crash reports right from here card, until it is closed or used once in this tool.</summary>
    public static bool ShouldShowIntro(ModReportSettings settings, string tool, string side, string? endpoint = Endpoint) =>
        HasEndpoint(endpoint) && !settings.CrashIntroSeen.Contains(IntroKey(tool, side), StringComparer.OrdinalIgnoreCase);

    public static void MarkIntroSeen(ModReportSettings settings, string tool, string side)
    {
        if (!settings.CrashIntroSeen.Contains(IntroKey(tool, side), StringComparer.OrdinalIgnoreCase))
            settings.CrashIntroSeen.Add(IntroKey(tool, side));
    }

    /// <summary>The card a client shows on start: the crash report card first, then the mod report card, one at a time.</summary>
    public static IntroCard IntroFor(ModReportSettings settings, string tool, string? crashEndpoint = Endpoint, string? reportEndpoint = ModReports.Endpoint)
    {
        if (ShouldShowIntro(settings, tool, "client", crashEndpoint))
            return IntroCard.CrashReports;
        return ModReports.ShouldShowIntro(settings, reportEndpoint) ? IntroCard.ModReports : IntroCard.None;
    }

    /// <summary>The crash bar of a client shows after a crash when crash reports can be sent, or a mod can be reported.</summary>
    public static bool ShowsClientCrashBar(int modCount, string? crashEndpoint = Endpoint, string? reportEndpoint = ModReports.Endpoint) =>
        HasEndpoint(crashEndpoint) || ModReports.OffersCrashReport(modCount, reportEndpoint);

    /// <summary>The crash bar's buttons, biggest first: Send crash report, then Tell everyone which mod broke when
    /// there are mods to rate, and always the small Save a copy instead link.</summary>
    public static IReadOnlyList<CrashBarButton> CrashBarButtons(int modCount, string? crashEndpoint = Endpoint, string? reportEndpoint = ModReports.Endpoint)
    {
        var buttons = new List<CrashBarButton>();
        if (HasEndpoint(crashEndpoint))
            buttons.Add(CrashBarButton.SendCrashReport);
        if (ModReports.OffersCrashReport(modCount, reportEndpoint))
            buttons.Add(CrashBarButton.TellWhichMod);
        buttons.Add(CrashBarButton.SaveCopy);
        return buttons;
    }

    /// <summary>The server tools show Send server crash report when the server crashed or stopped by
    /// itself: not after Stop, Restart or a clean exit.</summary>
    public static bool ShowsServerCrashBar(ServerExitInfo exit, string? endpoint = Endpoint) =>
        HasEndpoint(endpoint) && exit.IsCrash;

    /// <summary>The title of the server bar: a real crash says so, a save that did not load or a
    /// failed module check says the server stopped with an error.</summary>
    public static string ServerCrashTitle(ServerExitKind kind) =>
        kind is ServerExitKind.Unknown or ServerExitKind.FatalWhileServing ? "The server crashed" : "The server stopped with an error";

    // ----- The crash line -----

    /// <summary>
    /// The one line that says what crashed, from the first text that has one (the crash report,
    /// then the logs): an unhandled exception, an access violation or a fatal error first, then
    /// an exception with its message, then any line about an exception or a crash. The last
    /// such line of a text wins, since the crash comes at the end. Cleaned, at most 300 characters.
    /// </summary>
    public static string? FindCrashLine(params string?[] texts)
    {
        foreach (var pattern in new[] { StrongLine, TypedException, AnyException })
        {
            foreach (var text in texts)
            {
                if (string.IsNullOrWhiteSpace(text))
                    continue;
                // Only the end of a long log is read; the crash is at the end.
                var tail = text.Length > 2_000_000 ? text[^2_000_000..] : text;
                var lines = tail.Replace("\r", "").Split('\n');
                for (var i = lines.Length - 1; i >= 0; i--)
                {
                    var line = lines[i].Trim();
                    if (line.Length >= 8 && pattern.IsMatch(line) && !MayCrash.IsMatch(line))
                        return CleanCrashLine(line);
                }
            }
        }

        return null;
    }

    /// <summary>A crash line as it may be sent: cleaned like the logs, one line, at most 300 characters.</summary>
    public static string CleanCrashLine(string? line)
    {
        var cleaned = LogZipBuilder.Clean(line ?? "");
        cleaned = Regex.Replace(cleaned, @"\s+", " ").Trim();
        return cleaned.Length > MaxCrashLine ? cleaned[..(MaxCrashLine - 3)] + "..." : cleaned;
    }

    // ----- Packing -----

    /// <summary>The summary for a pack: every value cleaned, the file list capped.</summary>
    public static CrashReportSummary BuildSummary(CrashReportRequest request, string install, IEnumerable<CrashReportFile> files) => new()
    {
        Install = Regex.IsMatch(install ?? "", "^[0-9a-f]{32}$") ? install! : "",
        Tool = request.Tool is "taom" or "ad1259" or "be" or "rot" ? request.Tool : "coop",
        Side = request.Side == "server" ? "server" : "client",
        Reason = request.Reason is CrashReason.ServerCrash or CrashReason.Logs ? request.Reason : CrashReason.Crash,
        Problem = request.Reason == CrashReason.Logs && CrashProblem.IsKnown(request.Problem) ? request.Problem : null,
        ToolVersion = ModReports.CleanVersion(request.ToolVersion),
        Coop = ModReports.CleanVersion(request.Coop),
        Game = ModReports.CleanVersion(request.Game),
        CrashLine = string.IsNullOrWhiteSpace(request.CrashLine) ? "" : CleanCrashLine(request.CrashLine),
        SuspectedMod = ModReports.CleanId(request.SuspectedMod),
        Files = files.Select(file => new CrashReportFile(CleanFileName(file.Name), Math.Max(0, file.Size)))
            .Where(file => file.Name.Length > 0)
            .Take(MaxFiles)
            .ToList()
    };

    /// <summary>The same summary with another problem picked in the consent window.</summary>
    public static CrashReportSummary WithProblem(CrashReportSummary summary, string? problem)
    {
        var copy = CloneSummary(summary);
        copy.Problem = summary.Reason == CrashReason.Logs && CrashProblem.IsKnown(problem) ? problem : null;
        return copy;
    }

    /// <summary>A deep copy of a summary.</summary>
    public static CrashReportSummary CloneSummary(CrashReportSummary summary) =>
        JsonSerializer.Deserialize<CrashReportSummary>(JsonSerializer.Serialize(summary, Options), Options)!;

    /// <summary>Exactly the summary text that is sent.</summary>
    public static string SummaryJson(CrashReportSummary summary) => JsonSerializer.Serialize(summary, Options);

    // The worker takes plain names and folders only (no drive, no backslash, no .. part). The zip's
    // own entry names are cleaned the same way when it is packed (LogZipBuilder.CleanEntryName),
    // so the list matches the zip.
    private static string CleanFileName(string? name) => LogZipBuilder.CleanEntryName(name);

    /// <summary>
    /// Packs the logs for the consent window: the tool's own collection, text only, each file cut
    /// to its end, with the known passwords taken out, then shortened until the zip fits. Nothing
    /// is sent here. signaturesLater gives the signatures only when the triage needs them, after the
    /// packing, so a download that runs next to the packing is waited for as late as possible.
    /// </summary>
    public static CrashReportPack Prepare(CrashReportRequest request, string install, string? folder = null, CrashSignatureFile? signatures = null,
        Func<CrashSignatureFile?>? signaturesLater = null)
    {
        folder ??= TempFolder();
        Directory.CreateDirectory(folder);
        ClearOld(folder);
        var zipPath = Path.Combine(folder, request.FileStem + "-" + DateTime.Now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture) + "-"
            + Guid.NewGuid().ToString("N")[..6] + ".zip");
        try
        {
            var trace = new LogZipTrace();
            var limits = new LogZipLimits(TextOnly: true, MaxTextBytes: FirstTextBytes) { Secrets = request.Secrets, Trace = trace };
            var skipped = request.Collect(zipPath, limits).ToList();
            // The crash dump is measured now and added only when the report is sent or saved with
            // Add the crash dump ticked; its room is kept free so the logs shown are the logs sent.
            var dump = PickDump(request, zipPath, skipped, out var found);
            // crash-stack.txt goes whether or not the dump does: it is text only and cleaned like the logs.
            AddCrashStackSummary(zipPath, found, request, trace);
            var reserve = dump == null ? 0 : dump.CompressedSize + CrashDumps.EntryOverhead;
            if (FitToSize(zipPath, MaxZipBytes - reserve, trace: trace) is { } note)
                skipped.Add(note);
            // The known crash patterns run over the same files that are sent, before anything is sent.
            if (signatures == null && signaturesLater != null && CrashScreen.RunsTriage(request.Tool, request.Reason))
                signatures = signaturesLater();
            var (ran, match, triage) = Triage(zipPath, request, signatures);
            AddGenerated(zipPath, request, trace, triage);
            var summary = BuildSummary(request, install, FileList(zipPath));
            summary.Triage = triage;
            return new CrashReportPack(zipPath, summary, skipped) { Trace = trace, TriageRan = ran, Match = match, Dump = dump };
        }
        catch
        {
            // A half packed zip never stays behind.
            DeleteQuietly(zipPath);
            DeleteQuietly(zipPath + ".tmp");
            throw;
        }
    }

    /// <summary>
    /// The one crash dump the report may carry, measured: null when the tool finds none, when no
    /// dump of this crash fits CrashDumps.Budget or when looking for it failed (then the too big or left out
    /// line goes into the notes and NOTES.txt). A dump lookup never stops the report.
    /// </summary>
    private static CrashDumpPick? PickDump(CrashReportRequest request, string zipPath, List<string> skipped, out string? found)
    {
        string note;
        found = null;
        try
        {
            var path = request.FindDump?.Invoke();
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
                return null;
            found = path;
            // The full dump of a crash folder is often far too big; its small stack only dump goes instead (CrashDumps.Pick).
            var (dump, tooBig) = CrashDumps.Pick(path);
            if (dump != null || !tooBig)
                return dump;
            note = CrashDumps.TooBigText;
        }
        catch (Exception)
        {
            note = CrashDumps.LeftOutText;
        }

        skipped.Add(note);
        if (File.Exists(zipPath))
            CrashDumps.AppendNote(zipPath, note);
        return null;
    }

    /// <summary>
    /// crash-stack.txt from the dumps of this crash (CrashDumps.SameCrash(found)), newest first: an
    /// empty dump or one still being written is passed over, and so is one that gives no text (not
    /// readable, not a dump), so an older dump of the same crash is read instead. A text with no stack
    /// (the reader timed out or stopped, or the read hit its time limit before any frames,
    /// DumpStackSummary.NoStack) is kept as a fallback and one more dump is tried, since the small
    /// dump.dmp often reads in time where the full one does not. A timed out read that got frames goes.
    /// It is chosen apart from the dump that is sent, so the summary is made even when no dump can be
    /// sent. readForTests stands in for ReadInChild.
    /// </summary>
    internal static void AddCrashStackSummary(string zipPath, string? found, CrashReportRequest request, LogZipTrace? trace,
        Func<string, string?>? readForTests = null)
    {
        if (found == null || !File.Exists(zipPath))
            return;
        List<string> dumps;
        try
        {
            dumps = CrashDumps.SameCrash(found).ToList();
        }
        catch (Exception)
        {
            // A crash folder that cannot be listed never stops the report.
            return;
        }

        string? fallback = null;
        var afterFallback = 0;
        foreach (var path in dumps)
        {
            try
            {
                if (CrashDumps.SizeOf(path) <= 0 || CrashDumps.Busy(path))
                    continue;
            }
            catch (Exception)
            {
                // Only this dump is passed over.
                continue;
            }

            // After a fallback one more dump is read; a dump that gives no text at all does not use that try.
            if (fallback != null && afterFallback >= 1)
                break;
            var afterOne = fallback != null;
            var text = ReadStackSummary(path, request, readForTests);
            if (text == null)
                continue;
            if (afterOne)
                afterFallback++;
            if (DumpStackSummary.NoStack(text))
            {
                fallback ??= text;
                continue;
            }

            if (WriteStackSummary(zipPath, text, request, trace))
                return;
        }

        if (fallback != null)
            WriteStackSummary(zipPath, fallback, request, trace);
    }

    /// <summary>
    /// crash-stack.txt (DumpStackSummary): the crash stack read from the dump of this crash on this PC,
    /// cleaned like the logs, with its line in NOTES.txt. It goes even when Add the crash dump is unticked
    /// or the dump is too big, since it holds no memory. Nothing is added for no dump, a file that is not a
    /// dump or a text the cleaner could not finish. The dump is read by a reader process
    /// (DumpStackSummary.ReadInChild) once per dump file: a repack takes the text from StackCache.
    /// readForTests stands in for ReadInChild. True when crash-stack.txt went in.
    /// </summary>
    // crash-stack.txt's text per dump file (path, write time, size), so a repack does not read the dump again.
    internal static readonly System.Collections.Concurrent.ConcurrentDictionary<string, string?> StackCache = new(StringComparer.OrdinalIgnoreCase);

    internal static bool AddStackSummary(string zipPath, string? dumpPath, CrashReportRequest request, LogZipTrace? trace,
        Func<string, string?>? readForTests = null) =>
        !string.IsNullOrWhiteSpace(dumpPath) && File.Exists(zipPath)
        && ReadStackSummary(dumpPath, request, readForTests) is { } text && WriteStackSummary(zipPath, text, request, trace);

    // The uncleaned crash-stack.txt text of one dump, from StackCache or the reader; null when it gives none.
    private static string? ReadStackSummary(string dumpPath, CrashReportRequest request, Func<string, string?>? readForTests)
    {
        string? text;
        try
        {
            var file = new FileInfo(dumpPath);
            // The same dump is the same file at the same size and write time. The text is kept uncleaned,
            // since the passwords taken out can change between packs.
            var key = file.FullName + "|" + file.LastWriteTimeUtc.Ticks.ToString(System.Globalization.CultureInfo.InvariantCulture) + "|" + file.Length.ToString(System.Globalization.CultureInfo.InvariantCulture);
            if (!StackCache.TryGetValue(key, out text))
            {
                text = (readForTests ?? (path => DumpStackSummary.ReadInChild(path, request.ServerFolders)))(dumpPath);
                if (StackCache.Count >= 16)
                    StackCache.Clear();
                StackCache[key] = text;
            }
        }
        catch (Exception)
        {
            // A dump the reader trips over never stops the report.
            return null;
        }

        return text;
    }

    // Puts the text in the zip cleaned like the logs, with its NOTES.txt line; false when the cleaner could
    // not finish or the zip could not be written (locked, disk full). The work goes into a copy that
    // replaces the zip only when it is done, so a failure leaves the pack as it was, without the summary.
    private static bool WriteStackSummary(string zipPath, string text, CrashReportRequest request, LogZipTrace? trace)
    {
        if (!LogZipBuilder.TryClean(text, request.Secrets, out var cleaned))
            return false;
        var copy = zipPath + ".stack.tmp";
        try
        {
            File.Copy(zipPath, copy, overwrite: true);
            using (var zip = ZipFile.Open(copy, ZipArchiveMode.Update))
            {
                zip.GetEntry(DumpStackSummary.FileName)?.Delete();
                Put(zip, DumpStackSummary.FileName, cleaned);
            }

            CrashDumps.AppendNote(copy, DumpStackSummary.Note);
            File.Move(copy, zipPath, overwrite: true);
        }
        catch (Exception)
        {
            // crash-stack.txt is extra: the report goes on without it.
            DeleteQuietly(copy);
            return false;
        }

        trace?.Mark(DumpStackSummary.FileName, redacted: cleaned != text, truncated: false);
        return true;
    }

    /// <summary>
    /// The crash triage of a packed zip: only for a crash report of Coop and Friends, Coodsz Host,
    /// the AD1259, TAOM or Burning Empires tools (CrashScreen.Applies) and only with a signatures file. A failure to read
    /// the zip leaves the report unknown instead of stopping it. Send my logs of the same tools runs only the
    /// signatures on its allow list (CrashScreen.FitsSendMyLogs), as a hint.
    /// </summary>
    public static (bool Ran, CrashSignatureMatch? Match, CrashReportTriage? Triage) Triage(string zipPath, CrashReportRequest request, CrashSignatureFile? signatures)
    {
        if (signatures == null || !CrashScreen.RunsTriage(request.Tool, request.Reason))
            return (false, null, null);
        if (CrashScreen.HintApplies(request.Tool, request.Reason))
            signatures = CrashScreen.ForSendMyLogs(signatures);
        try
        {
            var evidence = CrashEvidence.FromZip(zipPath, request.Side, request.ExitCode, request.Modules.Select(module => module.Id), request.Tool);
            var match = CrashSignatureMatcher.Match(signatures, evidence);
            return (true, match, CrashReportTriage.From(match, signatures.Version));
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException)
        {
            return (true, null, CrashReportTriage.From(null, signatures.Version));
        }
    }

    /// <summary>The passwords a tool passes as Secrets: every one it knows, empty ones dropped, each once.</summary>
    public static IReadOnlyList<string> KnownSecrets(params string?[] passwords) =>
        passwords.Where(password => !string.IsNullOrEmpty(password)).Select(password => password!).Distinct(StringComparer.Ordinal).ToList();

    /// <summary>Deletes a file and ignores a file that is gone or locked.</summary>
    public static void DeleteQuietly(string path)
    {
        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }

    public const string ManifestName = "report.json";
    public const string ModulesName = "modules.json";
    public const string EnvironmentName = "environment.json";
    public const int MaxModules = 400;

    /// <summary>
    /// Adds the generated files to a packed zip: modules.json (the effective load order with
    /// versions), environment.json (tool, Coop, game and Windows versions, no names or paths),
    /// coodsz-modules.txt when the request names ModuleRoots (each Coodsz module DLL with its SHA256) and
    /// last report.json, which lists every other file with its size, its SHA256 and whether the
    /// cleaner changed it (redacted) or only its newest part is kept (truncated).
    /// </summary>
    public static void AddGenerated(string zipPath, CrashReportRequest request, LogZipTrace? trace = null, CrashReportTriage? triage = null)
    {
        using (var update = ZipFile.Open(zipPath, ZipArchiveMode.Update))
        {
            foreach (var name in new[] { ManifestName, ModulesName, EnvironmentName, ModuleFingerprints.FileName })
                update.GetEntry(name)?.Delete();
            Put(update, ModulesName, ModulesJson(request.Modules));
            Put(update, EnvironmentName, EnvironmentJson(request));
            if (request.ModuleRoots.Count > 0)
                // Cleaned per field inside Find (the folder names through the log cleaner, the versions kept to plain
                // characters); the header, hashes and labels stay whole, so no column is cut and no secret shows as a gap.
                Put(update, ModuleFingerprints.FileName, ModuleFingerprints.Text(ModuleFingerprints.Find(request.ModuleRoots, secrets: request.Secrets),
                    ToolName(request.Tool, request.Side), ModReports.CleanVersion(request.ToolVersion)));
        }

        var files = new List<object>();
        using (var read = ZipFile.OpenRead(zipPath))
            foreach (var entry in read.Entries.Where(entry => entry.Name.Length > 0))
            {
                using var stream = entry.Open();
                var hash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(stream)).ToLowerInvariant();
                var generated = entry.FullName is ModulesName or EnvironmentName or DumpStackSummary.FileName or ModuleFingerprints.FileName;
                var fromPlayer = entry.FullName == ReportExtras.NotesName || entry.FullName.StartsWith(ReportExtras.ScreenshotFolder, StringComparison.Ordinal);
                var (redacted, truncated) = trace?.Get(entry.FullName) ?? default;
                var dump = entry.FullName.EndsWith(".dmp", StringComparison.OrdinalIgnoreCase);
                files.Add(new { name = entry.FullName, size = entry.Length, sha256 = hash, redacted, truncated, generated, fromPlayer, dump });
            }

        using var zip = ZipFile.Open(zipPath, ZipArchiveMode.Update);
        Put(zip, ManifestName, JsonSerializer.Serialize(new
        {
            kind = "CoodszCrashReportManifest",
            schema = 1,
            note = "Every file in this zip except report.json. Text files were cleaned of the Windows user and PC names, Steam ids, addresses and passwords (in game player names can remain); redacted says the cleaner changed the file, truncated says only its newest part is kept. fromPlayer marks what the player added: " + ReportExtras.NotesName + " (their own words, cleaned the same way) and the pictures in " + ReportExtras.ScreenshotFolder + " (re-encoded without metadata, not cleaned). dump marks the crash dump the player chose to add: a memory snapshot, not cleaned.",
            perFileCapBytes = FirstTextBytes,
            zipCapBytes = MaxZipBytes,
            triage,
            files
        }, Options));
    }

    // The tool's own name for the header of coodsz-modules.txt, like Coodsz TAOM Server Tool.
    private static string ToolName(string tool, string side) =>
        "Coodsz " + (tool switch { "taom" => "TAOM", "ad1259" => "AD1259", "be" => "Burning Empires", "rot" => "Realm of Thrones", _ => "Coop" })
        + (side == "server" ? " Server Tool" : " Client Launcher");

    /// <summary>modules.json: the effective load order with versions, plain mod ids only.</summary>
    public static string ModulesJson(IEnumerable<(string Id, string? Version)> modules)
    {
        var list = modules
            .Select(module => (Id: ModReports.CleanId(module.Id), Version: ModReports.CleanVersion(module.Version)))
            .Where(module => module.Id != null)
            .Take(MaxModules)
            .Select((module, index) => new { order = index + 1, id = module.Id, version = module.Version })
            .ToList();
        return JsonSerializer.Serialize(new { kind = "CoodszCrashModules", schema = 1, count = list.Count, modules = list }, Options);
    }

    /// <summary>environment.json: the tool, Coop, game and Windows versions. No user name, PC name or path.</summary>
    public static string EnvironmentJson(CrashReportRequest request) => JsonSerializer.Serialize(new
    {
        kind = "CoodszCrashEnvironment",
        schema = 1,
        tool = request.Tool is "taom" or "ad1259" or "be" or "rot" ? request.Tool : "coop",
        side = request.Side == "server" ? "server" : "client",
        toolVersion = ModReports.CleanVersion(request.ToolVersion),
        coop = ModReports.CleanVersion(request.Coop),
        game = ModReports.CleanVersion(request.Game),
        os = "Windows " + Environment.OSVersion.Version.ToString(),
        os64 = Environment.Is64BitOperatingSystem,
        dotnet = Environment.Version.ToString()
    }, Options);

    private static void Put(ZipArchive zip, string name, string text)
    {
        using var writer = new StreamWriter(zip.CreateEntry(name, CompressionLevel.Optimal).Open(), new UTF8Encoding(false));
        writer.Write(text);
    }

    /// <summary>
    /// Shortens the logs in a zip until it is at most maxBytes: each text file keeps a shorter end
    /// each round (NOTES.txt stays whole), and as a last step the largest files are left out.
    /// Returns a line for the notes when anything was cut, or null when the zip already fit.
    /// </summary>
    public static string? FitToSize(string zipPath, long maxBytes, IReadOnlyList<long>? ends = null, LogZipTrace? trace = null)
    {
        var note = Fit(zipPath, maxBytes, ends, out var cut);
        foreach (var name in cut)
            trace?.Mark(name, redacted: false, truncated: true);
        return note;
    }

    private static string? Fit(string zipPath, long maxBytes, IReadOnlyList<long>? ends, out List<string> cut)
    {
        cut = new List<string>();
        if (!File.Exists(zipPath) || new FileInfo(zipPath).Length <= maxBytes)
            return null;
        var entries = new List<(string Name, string Text)>();
        // A crash dump or a picture is kept byte for byte, never cut or left out: the dump budget made room for it.
        var binary = new List<string>();
        using (var zip = ZipFile.OpenRead(zipPath))
            foreach (var entry in zip.Entries.Where(entry => entry.Name.Length > 0))
            {
                if (IsBinaryEntry(entry.FullName))
                {
                    binary.Add(entry.FullName);
                    continue;
                }

                using var reader = new StreamReader(entry.Open(), Encoding.UTF8);
                entries.Add((entry.FullName, reader.ReadToEnd()));
            }

        void Write(string path, IEnumerable<(string Name, string Text)> texts) => WriteFit(path, texts, binary);

        foreach (var end in ends ?? ShorterEnds)
        {
            Write(zipPath, entries.Select(entry => (entry.Name, Cut(entry.Name, entry.Text, end))));
            if (new FileInfo(zipPath).Length <= maxBytes)
            {
                cut.AddRange(entries.Where(entry => !IsNotes(entry.Name) && entry.Text.Length > end).Select(entry => entry.Name));
                return "Long logs were cut to their last " + LogZipBuilder.SizeText(end) + " so the report is small enough to send.";
            }
        }

        var last = (ends ?? ShorterEnds).Min();
        cut.AddRange(entries.Where(entry => !IsNotes(entry.Name) && entry.Text.Length > last).Select(entry => entry.Name));
        var kept = entries.Select(entry => (entry.Name, Text: Cut(entry.Name, entry.Text, last))).ToList();
        var dropped = new List<string>();
        while (kept.Count > 1 && new FileInfo(zipPath).Length > maxBytes)
        {
            var largest = kept.Where(entry => !IsNotes(entry.Name)).OrderByDescending(entry => entry.Text.Length).First();
            kept.Remove(largest);
            dropped.Add(largest.Name);
            Write(zipPath, kept);
        }

        return "Long logs were cut to their last " + LogZipBuilder.SizeText(last) + " and these were left out so the report is small enough to send: "
            + string.Join(", ", dropped) + ".";
    }

    private static bool IsNotes(string name) => name.Equals("NOTES.txt", StringComparison.OrdinalIgnoreCase);

    private static string Cut(string name, string text, long end) =>
        IsNotes(name) || text.Length <= end
            ? text
            : "[Only the last " + LogZipBuilder.SizeText(end) + " of this file is included.]" + Environment.NewLine + text[^(int)end..];

    // A dump or a picture someone added to a zip before it was shortened (CrashDumps.TryAdd).
    private static bool IsBinaryEntry(string name) =>
        name.EndsWith(".dmp", StringComparison.OrdinalIgnoreCase) || name.StartsWith(ReportExtras.ScreenshotFolder, StringComparison.Ordinal);

    // The shortened texts, and the binary entries copied over from the zip as it is now.
    private static void WriteFit(string zipPath, IEnumerable<(string Name, string Text)> entries, IReadOnlyList<string> binary)
    {
        var temp = zipPath + ".tmp";
        using (var zip = ZipFile.Open(temp, ZipArchiveMode.Create))
        {
            foreach (var (name, text) in entries)
            {
                using var writer = new StreamWriter(zip.CreateEntry(name, CompressionLevel.Optimal).Open(), new UTF8Encoding(false));
                writer.Write(text);
            }

            if (binary.Count > 0)
            {
                using var source = ZipFile.OpenRead(zipPath);
                foreach (var name in binary)
                {
                    if (source.GetEntry(name) is not { } entry)
                        continue;
                    using var from = entry.Open();
                    using var to = zip.CreateEntry(name, CompressionLevel.Optimal).Open();
                    from.CopyTo(to);
                }
            }
        }

        File.Move(temp, zipPath, overwrite: true);
    }

    /// <summary>Every file of the zip with its size, as the consent window lists them.</summary>
    public static IReadOnlyList<CrashReportFile> FileList(string zipPath)
    {
        if (!File.Exists(zipPath))
            return Array.Empty<CrashReportFile>();
        using var zip = ZipFile.OpenRead(zipPath);
        return zip.Entries.Where(entry => entry.Name.Length > 0).Select(entry => new CrashReportFile(entry.FullName, entry.Length)).ToList();
    }

    /// <summary>Save a copy instead: the same zip the report would send, where the player picks.</summary>
    public static void SaveCopy(CrashReportPack pack, string path) => File.Copy(pack.ZipPath, path, overwrite: true);

    /// <summary>True when the player wrote something or added a screenshot.</summary>
    public static bool HasExtras(string? notes, IReadOnlyCollection<ReportScreenshot>? screenshots) =>
        !string.IsNullOrWhiteSpace(notes) || screenshots is { Count: > 0 };

    /// <summary>
    /// The report with what the player added: a new zip next to the packed one, with the
    /// explanation cleaned like the logs (player-notes.txt) and the screenshots stripped of metadata
    /// (screenshots/), the logs shortened again when the pictures need the room, and report.json
    /// made again so it lists them. With includeDump the pack's crash dump goes in too (crash-dumps/),
    /// unless it and the screenshots pass CrashDumps.Budget together. The summary's file list matches
    /// the new zip. With nothing added it returns the same pack. The caller deletes the new zip when
    /// it is done with it.
    /// </summary>
    public static CrashReportPack WithExtras(CrashReportRequest request, CrashReportPack pack, string? notes, IReadOnlyList<ReportScreenshot>? screenshots,
        bool includeDump = false)
    {
        screenshots ??= Array.Empty<ReportScreenshot>();
        var cleanedNotes = ReportExtras.CleanNotes(notes, request.Secrets);
        var dump = includeDump ? pack.Dump : null;
        if (cleanedNotes.Length == 0 && screenshots.Count == 0 && dump == null)
            return pack;
        if (screenshots.Count > ReportExtras.MaxScreenshots)
            throw new ArgumentException("At most " + ReportExtras.MaxScreenshots + " screenshots can be added.", nameof(screenshots));
        var shots = screenshots.Select(shot => shot with { Bytes = ReportExtras.StripMetadata(shot.Bytes) ?? throw new InvalidDataException("A screenshot is not a PNG or JPEG picture.") }).ToList();
        if (shots.Sum(shot => shot.Size) > ReportExtras.MaxScreenshotsTotalBytes || shots.Any(shot => shot.Size > ReportExtras.MaxScreenshotBytes))
            throw new ArgumentException("The screenshots are too large.", nameof(screenshots));

        var zipPath = Path.Combine(Path.GetDirectoryName(pack.ZipPath)!, Path.GetFileNameWithoutExtension(pack.ZipPath) + "-full-" + Guid.NewGuid().ToString("N")[..6] + ".zip");
        try
        {
            File.Copy(pack.ZipPath, zipPath);
            using (var zip = ZipFile.Open(zipPath, ZipArchiveMode.Update))
                zip.GetEntry(ManifestName)?.Delete();

            // Room for the pictures and the explanation: the logs are shortened again when the zip would pass the cap.
            var extraBytes = shots.Sum(shot => shot.Size) + Encoding.UTF8.GetByteCount(cleanedNotes);
            // Each call marks its own copy, so a second Send or Save does not keep the cuts of the first.
            var trace = pack.Trace?.Copy();
            var skipped = pack.Skipped.ToList();
            // The dump shares its budget with the pictures, so the logs always keep their part.
            if (dump != null && !CrashDumps.FitsWith(dump, extraBytes))
            {
                skipped.Add(CrashDumps.TooBigWithScreenshotsText);
                CrashDumps.AppendNote(zipPath, CrashDumps.TooBigWithScreenshotsText);
                dump = null;
            }

            var reserve = dump == null ? 0 : dump.CompressedSize + CrashDumps.EntryOverhead;
            if (FitToSize(zipPath, Math.Max(200_000, MaxZipBytes - extraBytes - 64_000 - reserve), trace: trace) is { } note)
                skipped.Add(note);

            using (var zip = ZipFile.Open(zipPath, ZipArchiveMode.Update))
                ReportExtras.Write(zip, cleanedNotes, shots);

            // The dump is streamed into a new zip (Update mode would hold all of it in memory), and
            // a dump that cannot be added is left out with a note instead of stopping the report.
            if (dump != null)
            {
                var added = CrashDumps.Write(zipPath, dump, CrashDumps.RoomFor(dump, CrashDumps.Budget - extraBytes));
                if (added.Pick is { } written)
                    CrashDumps.AppendNote(zipPath, CrashDumps.AddedLine(written));
                else
                {
                    skipped.Add(added.Note!);
                    CrashDumps.AppendNote(zipPath, added.Note!);
                }
            }

            AddGenerated(zipPath, request, trace, pack.Summary.Triage);

            var summary = CloneSummary(pack.Summary);
            summary.Files = FileList(zipPath)
                .Select(file => new CrashReportFile(CleanFileName(file.Name), Math.Max(0, file.Size)))
                .Where(file => file.Name.Length > 0)
                .Take(MaxFiles)
                .ToList();
            return new CrashReportPack(zipPath, summary, skipped) { Trace = trace, TriageRan = pack.TriageRan, Match = pack.Match, Dump = pack.Dump };
        }
        catch
        {
            DeleteQuietly(zipPath);
            DeleteQuietly(zipPath + ".tmp");
            DeleteQuietly(zipPath + ".dump.tmp");
            throw;
        }
    }

    private static void ClearOld(string folder)
    {
        try
        {
            foreach (var old in Directory.GetFiles(folder, "*.zip").Where(path => LogZipBuilder.SafeWriteTime(path) < DateTime.UtcNow.AddDays(-2)))
                File.Delete(old);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }

    /// <summary>File sizes as the window shows them: 812 KB, 3.4 MB.</summary>
    public static string SizeText(long bytes) => bytes < 1000 ? bytes.ToString(CultureInfo.InvariantCulture) + " B" : LogZipBuilder.SizeText(bytes);

    // ----- Sending -----

    public static bool IsCode(string? code) => CodePattern.IsMatch(code ?? "");

    /// <summary>Posts the summary and the zip. Never throws for a network problem.</summary>
    public static async Task<CrashReportSendResult> SendAsync(HttpClient http, CrashReportPack pack, CrashReportSummary? summary = null,
        string? endpoint = Endpoint, CancellationToken cancellationToken = default)
    {
        if (!HasEndpoint(endpoint))
            return new CrashReportSendResult(CrashReportSendStatus.Failed, null, "Crash reports cannot be sent from this version. Click Save a copy instead.");
        if (!File.Exists(pack.ZipPath))
            return new CrashReportSendResult(CrashReportSendStatus.Failed, null, "The log zip is gone. Close this window and try again.");
        if (pack.ZipBytes > WorkerMaxZipBytes)
            return new CrashReportSendResult(CrashReportSendStatus.TooLarge, null, "The logs are too large to send. Click Save a copy instead.");
        try
        {
            using var form = BuildForm(SummaryJson(summary ?? pack.Summary),
                await File.ReadAllBytesAsync(pack.ZipPath, cancellationToken).ConfigureAwait(false));
            using var response = await http.PostAsync(TestWorkerUrl.Apply(endpoint!.Trim()), form, cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                var status = ((int)response.StatusCode).ToString(CultureInfo.InvariantCulture);
                var error = ReadError(await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false));
                var detail = error == null ? "HTTP " + status : "HTTP " + status + ", the worker said: " + error;
                var reason = error == null ? status : status + ": " + error;
                if (response.StatusCode == HttpStatusCode.TooManyRequests)
                    return new CrashReportSendResult(CrashReportSendStatus.RateLimited, null, "Too many reports from this PC right now. Try again later, or click Save a copy instead.", detail);
                if (response.StatusCode == HttpStatusCode.RequestEntityTooLarge)
                    return new CrashReportSendResult(CrashReportSendStatus.TooLarge, null, "The logs are too large to send. Click Save a copy instead.", detail);
                return new CrashReportSendResult(CrashReportSendStatus.Refused, null,
                    "The report was turned down (" + reason + "). Click Save a copy instead.", detail);
            }
            var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            var code = ReadCode(body);
            return code == null
                ? new CrashReportSendResult(CrashReportSendStatus.Failed, null, "The report was sent, but no code came back. Try again, or click Save a copy instead.")
                : new CrashReportSendResult(CrashReportSendStatus.Sent, code, "Sent. " + PostOnlyCode, Replies: ReadReplies(body), Filed: ReadFiled(body));
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or IOException)
        {
            return new CrashReportSendResult(CrashReportSendStatus.Failed, null, "Could not send the report. Check the internet connection and try again, or click Save a copy instead.");
        }
    }

    /// <summary>
    /// The multipart form the worker reads: a summary part and a zip part. Every name is quoted,
    /// name="summary", because the Cloudflare runtime's formData() refuses an unquoted name=summary
    /// (the form .NET writes by default) as a part with no name, and the worker answers 400 not a form.
    /// </summary>
    public static MultipartFormDataContent BuildForm(string summaryJson, byte[] zipBytes)
    {
        var form = new MultipartFormDataContent();
        var summary = new StringContent(summaryJson, Encoding.UTF8, "application/json");
        summary.Headers.ContentDisposition = new ContentDispositionHeaderValue("form-data") { Name = "\"summary\"" };
        form.Add(summary);
        var zip = new ByteArrayContent(zipBytes);
        zip.Headers.ContentType = new MediaTypeHeaderValue("application/zip");
        zip.Headers.ContentDisposition = new ContentDispositionHeaderValue("form-data") { Name = "\"zip\"", FileName = "\"logs.zip\"" };
        form.Add(zip);
        return form;
    }

    /// <summary>The error text in the worker's answer, like invalid file name, or null. Kept short and on one line.</summary>
    public static string? ReadError(string? json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json ?? "");
            if (doc.RootElement.ValueKind != JsonValueKind.Object || !doc.RootElement.TryGetProperty("error", out var error) || error.ValueKind != JsonValueKind.String)
                return null;
            var text = Regex.Replace(error.GetString() ?? "", @"\s+", " ").Trim();
            return text.Length == 0 ? null : text.Length > 200 ? text[..200] : text;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>The worker's "filed": true or false, or null when the answer has none.</summary>
    public static bool? ReadFiled(string? json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json ?? "");
            return doc.RootElement.ValueKind == JsonValueKind.Object && doc.RootElement.TryGetProperty("filed", out var filed)
                && filed.ValueKind is JsonValueKind.True or JsonValueKind.False
                ? filed.GetBoolean()
                : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>True only when the worker's answer says "replies": true. An older worker never does, so the tool drops the key.</summary>
    public static bool ReadReplies(string? json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json ?? "");
            return doc.RootElement.ValueKind == JsonValueKind.Object && doc.RootElement.TryGetProperty("replies", out var replies)
                && replies.ValueKind == JsonValueKind.True;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    /// <summary>A copy of the summary that carries the reply key, for the send only. A key that is not a real key is left out.</summary>
    public static CrashReportSummary WithReplyKey(CrashReportSummary summary, string? key)
    {
        var copy = CloneSummary(summary);
        copy.ReplyKey = ReportReplies.IsKey(key) ? key : null;
        return copy;
    }

    /// <summary>The code in the worker's answer, or null.</summary>
    public static string? ReadCode(string? json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json ?? "");
            return doc.RootElement.TryGetProperty("code", out var code) && code.ValueKind == JsonValueKind.String && IsCode(code.GetString())
                ? code.GetString()
                : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
