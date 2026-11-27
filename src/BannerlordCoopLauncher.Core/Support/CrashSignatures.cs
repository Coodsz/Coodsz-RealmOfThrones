using BannerlordCoopLauncher.Core.IO;
using System.IO.Compression;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using BannerlordCoopLauncher.Core.Modules;

namespace BannerlordCoopLauncher.Core.Support;

/// <summary>What kind of problem a known crash pattern is. The worker uses it as an issue label.</summary>
public static class CrashCategory
{
    public const string NotACrash = "not-a-crash";
    public const string Setup = "setup";
    public const string ModAssets = "mod-assets";
    public const string ModMissingPatch = "mod-missing-patch";
    public const string CoopBug = "coop-bug";
    public const string SilentExit = "silent-exit";
    public const string ModMismatch = "mod-mismatch";

    public static IReadOnlyList<string> All { get; } = [NotACrash, Setup, ModAssets, ModMissingPatch, CoopBug, SilentExit, ModMismatch];
}

/// <summary>The log a rule reads. CrashEvidence.FromZip fills them from the packed crash report.</summary>
public static class CrashLogSource
{
    /// <summary>Coop_client.log and Coop_client_&lt;pid&gt;.log: the game's own copies first, then the ones in Coop's crash report folder.</summary>
    public const string CoopClient = "coopClient";

    /// <summary>report.txt of Coop's crash report folder (it has the exit code).</summary>
    public const string CoopReport = "coopReport";

    /// <summary>The engine log (rgl_log), the one of Coop's crash report folder first, then the newest.</summary>
    public const string Rgl = "rgl";

    public const string Watchdog = "watchdog";

    /// <summary>The server window of Coodsz Host (this run).</summary>
    public const string Server = "server";

    /// <summary>The Coop server log (coop-server-*.log), newest first.</summary>
    public const string ServerLog = "serverLog";

    /// <summary>The console of Coodsz Host, with its "Server process exited with code" lines.</summary>
    public const string HostConsole = "hostConsole";

    /// <summary>The launcher's action and activity logs.</summary>
    public const string Launcher = "launcher";

    /// <summary>
    /// crash-stack.txt, the text summary read from the crash dump on this PC (DumpStackSummary), with the managed
    /// exception. A game that dies in a module's OnSubModuleLoad can leave that line nowhere else (Coop crash #62).
    /// Tools from before this source never fill it, so a rule on it only goes in any.
    /// </summary>
    public const string CrashStack = "crashStack";
}

/// <summary>One check of a signature. Exactly one of Contains, Regex, LastLine or Module is set.</summary>
public sealed class CrashRule
{
    [JsonPropertyName("file")] public string? File { get; set; }

    /// <summary>
    /// Optional, instead of File: the rule holds when it holds for one of these logs. A tool from before
    /// this field reads a rule without a file, which never matches, so it only goes in all or any.
    /// </summary>
    [JsonPropertyName("files")] public List<string>? Files { get; set; }

    /// <summary>A line has this text (any case).</summary>
    [JsonPropertyName("contains")] public string? Contains { get; set; }

    /// <summary>A line matches this regular expression.</summary>
    [JsonPropertyName("regex")] public string? Regex { get; set; }

    /// <summary>The last line that is not blank and not ignored (of the first log of that kind) matches this regular expression.</summary>
    [JsonPropertyName("lastLine")] public string? LastLine { get; set; }

    /// <summary>This module is turned on.</summary>
    [JsonPropertyName("module")] public string? Module { get; set; }

    /// <summary>Lines matching this regular expression are skipped.</summary>
    [JsonPropertyName("ignore")] public string? Ignore { get; set; }

    /// <summary>At least this many lines must match (default 1).</summary>
    [JsonPropertyName("minCount")] public int? MinCount { get; set; }

    /// <summary>Only the last this many lines of each log are read.</summary>
    [JsonPropertyName("tail")] public int? Tail { get; set; }

    /// <summary>Only the first log of that kind is read: the newest, or the one of the crash itself (an older run never matches).</summary>
    [JsonPropertyName("newestOnly")] public bool? NewestOnly { get; set; }
}

public sealed class CrashMatch
{
    /// <summary>The exit code must be one of these (empty or missing: any exit code, or none known).</summary>
    [JsonPropertyName("exitCodes")] public List<int>? ExitCodes { get; set; }

    /// <summary>Every one of these must match.</summary>
    [JsonPropertyName("all")] public List<CrashRule>? All { get; set; }

    /// <summary>At least one of these must match (when there are any).</summary>
    [JsonPropertyName("any")] public List<CrashRule>? Any { get; set; }

    /// <summary>None of these may match.</summary>
    [JsonPropertyName("none")] public List<CrashRule>? None { get; set; }
}

/// <summary>One known crash pattern of crash-signatures.json.</summary>
public sealed class CrashSignature
{
    [JsonPropertyName("id")] public string Id { get; set; } = "";

    /// <summary>client, server or both.</summary>
    [JsonPropertyName("side")] public string Side { get; set; } = "both";

    /// <summary>Optional. The tools (coop, ad1259, taom, be, rot) this signature is for. Missing or empty: every tool. Older tools ignore it.</summary>
    [JsonPropertyName("tools")] public List<string>? Tools { get; set; }

    /// <summary>The highest priority match wins; equal ones keep the file's order.</summary>
    [JsonPropertyName("priority")] public int Priority { get; set; }

    [JsonPropertyName("category")] public string Category { get; set; } = "";
    [JsonPropertyName("match")] public CrashMatch Match { get; set; } = new();
    [JsonPropertyName("title")] public string Title { get; set; } = "";
    [JsonPropertyName("whatHappened")] public string WhatHappened { get; set; } = "";
    [JsonPropertyName("whatToDo")] public string WhatToDo { get; set; } = "";

    /// <summary>Optional. Only Coodsz Host prints it, as the What not to do line of its own explanation.</summary>
    [JsonPropertyName("whatNotToDo")] public string? WhatNotToDo { get; set; }

    /// <summary>False: the screen says No need to report this. True: sending still helps (a known bug being worked on).</summary>
    [JsonPropertyName("reportNeeded")] public bool ReportNeeded { get; set; }

    /// <summary>
    /// Optional. True: Send my logs runs it too and shows it as a hint above the logs, Send stays as it is.
    /// Only a setup signature without exit codes counts (CrashScreen.FitsSendMyLogs), so a new signature
    /// shows there only when it is added on purpose. Older tools ignore it.
    /// </summary>
    [JsonPropertyName("sendMyLogs")] public bool SendMyLogs { get; set; }

    /// <summary>Extra issue labels for the worker.</summary>
    [JsonPropertyName("labels")] public List<string>? Labels { get; set; }

    /// <summary>The issue in Coodsz-Crash-Reports that collects these reports (the worker comments there instead of opening a new one).</summary>
    [JsonPropertyName("trackingIssue")] public int? TrackingIssue { get; set; }

    /// <summary>The upstream issue, like the Coop issue for a Coop bug.</summary>
    [JsonPropertyName("upstream")] public string? Upstream { get; set; }

    /// <summary>Optional player text per language (ar, de, fr, ja, ko, pt, ru, tr, zh-CN). A missing language or field falls back to English. Placeholders like {mod} stay in the text.</summary>
    [JsonPropertyName("translations")] public Dictionary<string, CrashSignatureText>? Translations { get; set; }
}

/// <summary>The player text of a signature in one language. Every field is optional.</summary>
public sealed class CrashSignatureText
{
    [JsonPropertyName("title")] public string? Title { get; set; }
    [JsonPropertyName("whatHappened")] public string? WhatHappened { get; set; }
    [JsonPropertyName("whatToDo")] public string? WhatToDo { get; set; }
    [JsonPropertyName("whatNotToDo")] public string? WhatNotToDo { get; set; }
}

/// <summary>
/// crash-signatures.json: the known crash patterns, in compat-list next to mods.json and published
/// with it in Coodsz-Coop-and-Friends, so a new pattern needs no tool release. The tools download it
/// when a crash screen opens and keep a copy; offline the kept copy is used, and the copy built into
/// the tools when there is none.
/// </summary>
public sealed class CrashSignatureFile
{
    public const int SupportedSchema = 1;
    public const string DefaultUrl = "https://raw.githubusercontent.com/" + ModCompatDatabase.Repository + "/main/compat-list/crash-signatures.json";
    private const string EmbeddedName = "Coodsz.CompatList.crash-signatures.json";

    [JsonPropertyName("schema")] public int Schema { get; set; }
    [JsonPropertyName("version")] public string Version { get; set; } = "";
    [JsonPropertyName("signatures")] public List<CrashSignature> Signatures { get; set; } = new();

    public static CrashSignatureFile Empty { get; } = new() { Schema = SupportedSchema };

    /// <summary>Shared by every Coodsz tool, next to the mod list copy.</summary>
    public static string CachePath { get; set; } =
        Path.Combine(ToolData.RoamingRoot, "crash-signatures.json");

    private static readonly object Gate = new();
    private static CrashSignatureFile? _current;

    /// <summary>The kept or built in copy, or the last one downloaded in this run.</summary>
    public static CrashSignatureFile Current
    {
        get
        {
            lock (Gate)
                return _current ??= LoadCached(CachePath);
        }
    }

    private static readonly JsonSerializerOptions Options = new()
    {
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        PropertyNameCaseInsensitive = true,
    };

    /// <summary>Reads the file text; null when it is damaged or a newer schema.</summary>
    public static CrashSignatureFile? Parse(string json)
    {
        try
        {
            var file = JsonSerializer.Deserialize<CrashSignatureFile>(json, Options);
            if (file == null || file.Schema != SupportedSchema)
                return null;
            file.Signatures = file.Signatures.Where(s => !string.IsNullOrWhiteSpace(s.Id) && s.Match != null).ToList();
            return file;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    public static CrashSignatureFile Embedded()
    {
        using var stream = typeof(CrashSignatureFile).Assembly.GetManifestResourceStream(EmbeddedName);
        if (stream == null) return Empty;
        using var reader = new StreamReader(stream);
        return Parse(reader.ReadToEnd()) ?? Empty;
    }

    /// <summary>The kept copy; the built in one when there is none or it is damaged.</summary>
    public static CrashSignatureFile LoadCached(string cachePath)
    {
        try
        {
            if (File.Exists(cachePath) && Parse(File.ReadAllText(cachePath)) is { Signatures.Count: > 0 } kept)
                return kept;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
        }

        return Embedded();
    }

    /// <summary>Downloads the file and keeps a copy; offline the kept (or built in) copy. Never throws for a network problem.</summary>
    public static async Task<CrashSignatureFile> FetchAsync(HttpClient http, string? cachePath = null, string url = DefaultUrl,
        CancellationToken cancellationToken = default)
    {
        cachePath ??= CachePath;
        try
        {
            var json = await http.GetStringAsync(url, cancellationToken).ConfigureAwait(false);
            if (Parse(json) is { Signatures.Count: > 0 } file)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(cachePath)!);
                var temp = cachePath + ".tmp";
                await File.WriteAllTextAsync(temp, json, cancellationToken).ConfigureAwait(false);
                File.Move(temp, cachePath, overwrite: true);
                if (cachePath == CachePath)
                    lock (Gate)
                        _current = file;
                return file;
            }
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException or IOException or UnauthorizedAccessException)
        {
            // Offline, or GitHub cannot be reached: the kept copy is used.
        }

        return cachePath == CachePath ? Current : LoadCached(cachePath);
    }

    /// <summary>Downloads the file with a short wait, for a crash screen that is opening.</summary>
    public static async Task<CrashSignatureFile> FetchForScreenAsync(string userAgent)
    {
        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(6) };
            http.DefaultRequestHeaders.UserAgent.ParseAdd(userAgent);
            return await FetchAsync(http).ConfigureAwait(false);
        }
        catch (Exception)
        {
            return Current;
        }
    }
}

/// <summary>
/// What the matcher reads: the exit code, the logs by kind (each kind newest first) and the turned on
/// modules. FromZip builds it from the packed crash report, so it reads the same files that are sent.
/// </summary>
public sealed class CrashEvidence
{
    public string Side { get; init; } = "client";

    /// <summary>The tool that made the report (coop, ad1259, taom, be, rot), for the tools field of a signature. Null: every signature fits.</summary>
    public string? Tool { get; init; }
    public int? ExitCode { get; init; }
    public IReadOnlyDictionary<string, IReadOnlyList<string>> Texts { get; init; } = new Dictionary<string, IReadOnlyList<string>>();
    public IReadOnlyList<string> Modules { get; init; } = Array.Empty<string>();

    private readonly Dictionary<(string, int), string[]> _lines = new();

    public IReadOnlyList<string> TextsOf(string? source) =>
        source != null && Texts.TryGetValue(source, out var texts) ? texts : Array.Empty<string>();

    internal string[] LinesOf(string source, int index)
    {
        if (_lines.TryGetValue((source, index), out var lines))
            return lines;
        lines = TextsOf(source)[index].Replace("\r", "").Split('\n');
        _lines[(source, index)] = lines;
        return lines;
    }

    private static readonly Regex ClientExitCode = new(@"^\s*Exit code:\s*(-?\d+)", RegexOptions.Multiline | RegexOptions.CultureInvariant);
    private static readonly Regex ServerExitCode = new(@"(?:Server process|engine|process) exited with code\s+(-?\d+)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly Regex CrashFolderEntry = new(@"^crash-reports/(?<folder>[^/]+)/(?<rest>.+)$", RegexOptions.CultureInvariant);
    private static readonly Regex RglName = new(@"^rgl_log_\d+\.txt$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly Regex WatchdogName = new(@"^watchdog_log_\d+\.txt$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    // Coop_client.log, or the Coop_client_<pid>.log Coop 0.1.7 writes when Coop_client.log is taken (ClientJoinLog).
    private static readonly Regex CoopClientName = new(@"^Coop_client(?:_\d+)?\.log$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    /// <summary>The exit code the logs give: report.txt of Coop's crash report for a client, the last exit line of Coodsz Host for a server.</summary>
    public static int? ExitCodeFrom(string side, IReadOnlyDictionary<string, IReadOnlyList<string>> texts)
    {
        IEnumerable<string> Of(string source) => texts.TryGetValue(source, out var list) ? list : [];
        if (side == "server")
        {
            foreach (var text in Of(CrashLogSource.HostConsole).Concat(Of(CrashLogSource.Server)))
            {
                var matches = ServerExitCode.Matches(text);
                if (matches.Count > 0 && int.TryParse(matches[^1].Groups[1].Value, out var code))
                    return code;
            }

            return null;
        }

        foreach (var text in Of(CrashLogSource.CoopReport))
        {
            var match = ClientExitCode.Match(text);
            if (match.Success && int.TryParse(match.Groups[1].Value, out var code))
                return code;
        }

        return null;
    }

    /// <summary>
    /// The evidence of a packed crash report zip. The zip lists each kind of log newest first
    /// (LogZipBuilder.AddNewest), and Coop's crash report folder holds the logs of the crash it
    /// was made for, so those come first. A server ignores the engine logs of a single player game.
    /// exitCode, when the tool knows it, wins over the one in the logs.
    /// </summary>
    public static CrashEvidence FromZip(string zipPath, string side, int? exitCode = null, IEnumerable<string>? modules = null, string? tool = null)
    {
        var first = new Dictionary<string, List<string>>();
        var later = new Dictionary<string, List<string>>();
        void Add(Dictionary<string, List<string>> into, string source, string text)
        {
            if (!into.TryGetValue(source, out var list))
                into[source] = list = new List<string>();
            list.Add(text);
        }

        // Only the newest game/Coop_client_<pid>.log is this run's evidence; the older ones stay in the zip for people to read.
        var pidLogTaken = false;
        using (var zip = ZipFile.OpenRead(zipPath))
        {
            foreach (var entry in zip.Entries.Where(e => e.Name.Length > 0 && LogZipBuilder.IsTextName(e.Name)))
            {
                var name = entry.FullName;
                var file = entry.Name;
                (Dictionary<string, List<string>> Into, string Source)? target = null;
                var folder = CrashFolderEntry.Match(name);
                if (folder.Success)
                {
                    var rest = folder.Groups["rest"].Value;
                    if (rest.Equals("report.txt", StringComparison.OrdinalIgnoreCase)) target = (first, CrashLogSource.CoopReport);
                    else if (CoopClientName.IsMatch(file)) target = (later, CrashLogSource.CoopClient);
                    else if (RglName.IsMatch(file)) target = (first, CrashLogSource.Rgl);
                    else if (WatchdogName.IsMatch(file)) target = (first, CrashLogSource.Watchdog);
                }
                // game/Coop_client.log comes before the Coop_client_<pid>.log files in the zip (ClientLogZip).
                else if (name.StartsWith("game/", StringComparison.OrdinalIgnoreCase) && name.IndexOf('/', 5) < 0 && CoopClientName.IsMatch(file))
                {
                    var pidLog = !file.Equals("Coop_client.log", StringComparison.OrdinalIgnoreCase);
                    if (!pidLog || !pidLogTaken)
                        target = (first, CrashLogSource.CoopClient);
                    pidLogTaken |= pidLog;
                }
                else if (name.StartsWith("engine-logs/", StringComparison.OrdinalIgnoreCase) && RglName.IsMatch(file)) target = (later, CrashLogSource.Rgl);
                else if (name.StartsWith("engine-logs/", StringComparison.OrdinalIgnoreCase) && WatchdogName.IsMatch(file)) target = (later, CrashLogSource.Watchdog);
                else if (name.Equals(ServerEventLog.EngineTailName, StringComparison.OrdinalIgnoreCase)) target = (later, CrashLogSource.Rgl);
                // The server's own engine minute and the end of its rgl_log_errors (a fatal assert lands only there).
                else if (name.Equals(ServerExitEvidence.EntryName, StringComparison.OrdinalIgnoreCase)) target = (later, CrashLogSource.Rgl);
                else if (name.Equals("host/server-window.txt", StringComparison.OrdinalIgnoreCase)) target = (first, CrashLogSource.Server);
                else if (name.StartsWith("server-logs/coop-server-", StringComparison.OrdinalIgnoreCase)) target = (later, CrashLogSource.ServerLog);
                else if (name.Equals("host/console-window.txt", StringComparison.OrdinalIgnoreCase)) target = (first, CrashLogSource.HostConsole);
                else if (name.StartsWith("host-console/", StringComparison.OrdinalIgnoreCase)) target = (later, CrashLogSource.HostConsole);
                else if (name.StartsWith("launcher/", StringComparison.OrdinalIgnoreCase)) target = (later, CrashLogSource.Launcher);
                else if (name.Equals(DumpStackSummary.FileName, StringComparison.OrdinalIgnoreCase)) target = (first, CrashLogSource.CrashStack);
                if (target is not { } t)
                    continue;
                using var reader = new StreamReader(entry.Open(), Encoding.UTF8);
                var text = reader.ReadToEnd();
                if (side == "server" && t.Source == CrashLogSource.Rgl && text.Contains("/singleplayer", StringComparison.OrdinalIgnoreCase))
                    continue;
                Add(t.Into, t.Source, text);
            }
        }

        var texts = new Dictionary<string, IReadOnlyList<string>>();
        foreach (var source in first.Keys.Union(later.Keys))
            texts[source] = (first.TryGetValue(source, out var a) ? a : new List<string>())
                .Concat(later.TryGetValue(source, out var b) ? b : new List<string>()).ToList();
        return new CrashEvidence
        {
            Side = side == "server" ? "server" : "client",
            Tool = string.IsNullOrWhiteSpace(tool) ? null : tool,
            ExitCode = exitCode ?? ExitCodeFrom(side, texts),
            Texts = texts,
            Modules = modules?.ToList() ?? new List<string>()
        };
    }
}

/// <summary>A known crash the matcher found, with the player text filled in and the lines that matched.</summary>
public sealed record CrashSignatureMatch(CrashSignature Signature, string Title, string WhatHappened, string WhatToDo, IReadOnlyList<string> Evidence, string SignaturesVersion)
{
    /// <summary>The What not to do text with its placeholders filled, or null.</summary>
    public string? WhatNotToDo { get; init; }

    /// <summary>The regex groups the rules captured, for the placeholders.</summary>
    public IReadOnlyDictionary<string, string> Groups { get; init; } = new Dictionary<string, string>();

    /// <summary>
    /// The player text in a language (Translator.Current on the screen), placeholders filled. A missing
    /// language or field is the English text. The report to Coodsz always uses the English properties.
    /// </summary>
    public CrashSignatureText Localized(string? language)
    {
        CrashSignatureText? t = null;
        if (!string.IsNullOrEmpty(language) && Signature.Translations != null)
            Signature.Translations.TryGetValue(language, out t);
        string? Pick(string? translated, string? english) =>
            string.IsNullOrWhiteSpace(translated) ? english : CrashSignatureMatcher.Fill(translated, Groups);
        return new CrashSignatureText
        {
            Title = Pick(t?.Title, Title),
            WhatHappened = Pick(t?.WhatHappened, WhatHappened),
            WhatToDo = Pick(t?.WhatToDo, WhatToDo),
            WhatNotToDo = Pick(t?.WhatNotToDo, WhatNotToDo)
        };
    }
}

/// <summary>
/// Runs crash-signatures.json over the logs of a crash report and returns the best known match:
/// the highest priority signature for this side whose exit code and rules all hold. Null when
/// nothing matches (an unknown crash). A broken or slow regular expression never matches.
/// </summary>
public static class CrashSignatureMatcher
{
    public const int MaxEvidence = 10;
    private const int LinesPerRule = 3;
    private static readonly TimeSpan RegexTimeout = TimeSpan.FromSeconds(1);
    private static readonly Regex Placeholder = new(@"\{(?<name>[A-Za-z][A-Za-z0-9_]*)\}", RegexOptions.CultureInvariant);

    public static CrashSignatureMatch? Match(CrashSignatureFile file, CrashEvidence evidence)
    {
        CrashSignatureMatch? best = null;
        foreach (var signature in file.Signatures)
        {
            if (best != null && signature.Priority <= best.Signature.Priority)
                continue;
            if (TryMatch(signature, evidence, file.Version) is { } match)
                best = match;
        }

        return best;
    }

    private static CrashSignatureMatch? TryMatch(CrashSignature signature, CrashEvidence evidence, string version)
    {
        if (!SideFits(signature.Side, evidence.Side))
            return null;
        if (!ToolFits(signature.Tools, evidence.Tool))
            return null;
        var match = signature.Match;
        var lines = new List<string>();
        var groups = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (match.ExitCodes is { Count: > 0 } codes)
        {
            if (evidence.ExitCode is not { } code || !codes.Contains(code))
                return null;
            lines.Add("Exit code " + code);
        }

        foreach (var rule in match.All ?? [])
            if (!Check(rule, evidence, lines, groups))
                return null;

        if (match.Any is { Count: > 0 } any)
        {
            var hit = false;
            foreach (var rule in any)
                hit |= Check(rule, evidence, lines, groups);
            if (!hit)
                return null;
        }

        foreach (var rule in match.None ?? [])
            if (Check(rule, evidence, new List<string>(), new Dictionary<string, string>()))
                return null;

        string Fill(string text) => CrashSignatureMatcher.Fill(text, groups);
        var evidenceLines = lines.Select(CrashReports.CleanCrashLine).Where(line => line.Length > 0).Distinct().Take(MaxEvidence).ToList();
        return new CrashSignatureMatch(signature, Fill(signature.Title), Fill(signature.WhatHappened), Fill(signature.WhatToDo), evidenceLines, version)
        {
            WhatNotToDo = string.IsNullOrWhiteSpace(signature.WhatNotToDo) ? null : Fill(signature.WhatNotToDo),
            Groups = groups
        };
    }

    /// <summary>Fills {name} placeholders with the captured groups; a missing one becomes a mod.</summary>
    public static string Fill(string? text, IReadOnlyDictionary<string, string> groups) =>
        Placeholder.Replace(text ?? "", m => groups.TryGetValue(m.Groups["name"].Value, out var value) ? value : "a mod");

    public static bool SideFits(string? signatureSide, string evidenceSide) =>
        signatureSide is null or "" or "both" || string.Equals(signatureSide, evidenceSide, StringComparison.OrdinalIgnoreCase);

    /// <summary>A signature without tools fits every tool; so does evidence without a tool.</summary>
    public static bool ToolFits(IReadOnlyCollection<string>? tools, string? evidenceTool) =>
        tools is null or { Count: 0 } || string.IsNullOrWhiteSpace(evidenceTool) || tools.Contains(evidenceTool, StringComparer.OrdinalIgnoreCase);

    private static bool Check(CrashRule rule, CrashEvidence evidence, List<string> lines, Dictionary<string, string> groups)
    {
        try
        {
            if (!string.IsNullOrWhiteSpace(rule.Module))
            {
                if (!evidence.Modules.Contains(rule.Module.Trim(), StringComparer.OrdinalIgnoreCase))
                    return false;
                lines.Add("Module on: " + rule.Module.Trim());
                return true;
            }

            // Several logs: the first one the rule holds for gives the lines.
            if (rule.Files is { Count: > 0 } files)
                return files.Any(file => !string.IsNullOrWhiteSpace(file) && Check(ForFile(rule, file), evidence, lines, groups));

            if (string.IsNullOrWhiteSpace(rule.File))
                return false;
            var texts = evidence.TextsOf(rule.File);
            if (texts.Count == 0)
                return false;
            var ignore = string.IsNullOrEmpty(rule.Ignore) ? null : new Regex(rule.Ignore, RegexOptions.CultureInvariant, RegexTimeout);

            if (!string.IsNullOrEmpty(rule.LastLine))
            {
                // Only the first log of that kind: the newest, or the one of the crash itself.
                var all = evidence.LinesOf(rule.File, 0);
                for (var i = all.Length - 1; i >= 0; i--)
                {
                    var line = all[i];
                    if (string.IsNullOrWhiteSpace(line) || (ignore != null && ignore.IsMatch(line)))
                        continue;
                    var m = new Regex(rule.LastLine, RegexOptions.CultureInvariant, RegexTimeout).Match(line);
                    if (!m.Success)
                        return false;
                    Capture(m, groups);
                    lines.Add(line.Trim());
                    return true;
                }

                return false;
            }

            Regex? regex = null;
            if (!string.IsNullOrEmpty(rule.Regex))
                regex = new Regex(rule.Regex, RegexOptions.CultureInvariant, RegexTimeout);
            else if (string.IsNullOrEmpty(rule.Contains))
                return false;

            var need = Math.Max(1, rule.MinCount ?? 1);
            var count = 0;
            var found = new List<string>();
            var logs = rule.NewestOnly == true ? Math.Min(1, texts.Count) : texts.Count;
            for (var t = 0; t < logs; t++)
            {
                var all = evidence.LinesOf(rule.File, t);
                var start = rule.Tail is > 0 and var tail ? Math.Max(0, all.Length - tail) : 0;
                for (var i = start; i < all.Length; i++)
                {
                    var line = all[i];
                    if (ignore != null && ignore.IsMatch(line))
                        continue;
                    if (regex != null)
                    {
                        var m = regex.Match(line);
                        if (!m.Success)
                            continue;
                        Capture(m, groups);
                    }
                    else if (!line.Contains(rule.Contains!, StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    count++;
                    if (found.Count < LinesPerRule)
                        found.Add(line.Trim());
                    if (count >= need && found.Count >= LinesPerRule)
                        break;
                }

                if (count >= need && found.Count >= LinesPerRule)
                    break;
            }

            if (count < need)
                return false;
            if (need > 1)
                found.Add("(" + (count >= need && found.Count >= LinesPerRule ? "at least " + count : count.ToString()) + " lines like this)");
            lines.AddRange(found);
            return true;
        }
        catch (Exception e) when (e is ArgumentException or RegexMatchTimeoutException)
        {
            // A broken pattern in the downloaded file never matches, and never stops the crash screen.
            return false;
        }
    }

    private static CrashRule ForFile(CrashRule rule, string file) => new()
    {
        File = file,
        Contains = rule.Contains,
        Regex = rule.Regex,
        LastLine = rule.LastLine,
        Ignore = rule.Ignore,
        MinCount = rule.MinCount,
        Tail = rule.Tail,
        NewestOnly = rule.NewestOnly
    };

    private static void Capture(System.Text.RegularExpressions.Match match, Dictionary<string, string> groups)
    {
        foreach (Group group in match.Groups)
            if (group.Success && !int.TryParse(group.Name, out _) && group.Value.Trim().Length > 0)
                groups.TryAdd(group.Name, group.Value.Trim());
    }
}

/// <summary>Which way a crash screen shows its Send button.</summary>
public enum CrashScreenMode
{
    /// <summary>No triage (Send my logs): the screen is as before.</summary>
    None,

    /// <summary>A known cause that needs no report: the big No need to report this box, Send is a small faded link.</summary>
    KnownNoReport,

    /// <summary>A known bug that is being worked on: the box asks for the logs, Send stays big.</summary>
    KnownSendHelps,

    /// <summary>Nothing known matched: Send stays big and obvious.</summary>
    Unknown,

    /// <summary>Send my logs found a known setup problem: a hint with the fix above the logs, Send stays exactly as it is.</summary>
    Hint
}

/// <summary>The crash screen texts and the rule for which mode it is in.</summary>
public static class CrashScreen
{
    public const string NoNeedTitle = "No need to report this";
    public const string KnownCoopBugTitle = "This is a known Coop bug we're working on. Please send your logs, every report helps us fix it.";
    public const string KnownBugTitle = "We know this one and are still working on it. Please send your logs, every report helps us fix it.";
    public const string UnknownTitle = "We don't know this one yet. Sending it helps us fix it.";
    public const string WhatHappened = "What happened";
    public const string WhatToDo = "What to do";
    public const string SendAnywayText = "Still think it's something else? Send it anyway";
    public const string SendAnywayTip = "Sends this zip and summary to Coodsz anyway. Only Coodsz can open it.";
    public const string MoreInfoText = "More about this bug";
    public const string HintTitle = "This looks like a known problem. Try the fix below first. If it doesn't help, send your logs.";

    /// <summary>The mode: none when the matcher did not run. Send my logs only shows a hint, and only for an allowed setup signature.</summary>
    public static CrashScreenMode ModeFor(string tool, string reason, bool triageRan, CrashSignatureMatch? match)
    {
        if (HintApplies(tool, reason))
            return triageRan && match != null && FitsSendMyLogs(match.Signature) ? CrashScreenMode.Hint : CrashScreenMode.None;
        if (!Applies(tool, reason) || !triageRan)
            return CrashScreenMode.None;
        if (match == null)
            return CrashScreenMode.Unknown;
        return match.Signature.ReportNeeded ? CrashScreenMode.KnownSendHelps : CrashScreenMode.KnownNoReport;
    }

    /// <summary>Triage runs for the crash reports of Coop and Friends, Coodsz Host, the AD1259 tools, the TAOM tools, the Burning Empires tools and the Realm of Thrones tools. Send my logs runs it only as a hint (HintApplies).</summary>
    public static bool Applies(string tool, string reason) =>
        tool is "coop" or "ad1259" or "taom" or "be" or "rot" && reason is CrashReason.Crash or CrashReason.ServerCrash;

    /// <summary>Send my logs of the same tools, game or server side: only the signatures FitsSendMyLogs keeps run, as a hint.</summary>
    public static bool HintApplies(string tool, string reason) =>
        tool is "coop" or "ad1259" or "taom" or "be" or "rot" && reason == CrashReason.Logs;

    /// <summary>The matcher runs for a crash report and, as a hint, for Send my logs.</summary>
    public static bool RunsTriage(string tool, string reason) => Applies(tool, reason) || HintApplies(tool, reason);

    /// <summary>
    /// The allow list of Send my logs: a signature marked sendMyLogs that is a setup problem and needs no
    /// exit code. Exit code and silent exit signatures never run there, since a log sent without a crash
    /// can still hold an old exit line.
    /// </summary>
    public static bool FitsSendMyLogs(CrashSignature signature) =>
        signature.SendMyLogs && signature.Category == CrashCategory.Setup && signature.Match?.ExitCodes is not { Count: > 0 };

    /// <summary>The signatures Send my logs runs.</summary>
    public static CrashSignatureFile ForSendMyLogs(CrashSignatureFile file) => new()
    {
        Schema = file.Schema,
        Version = file.Version,
        Signatures = file.Signatures.Where(FitsSendMyLogs).ToList()
    };

    /// <summary>The headline of the box at the top.</summary>
    public static string Headline(CrashScreenMode mode, CrashSignatureMatch? match) => mode switch
    {
        CrashScreenMode.Hint => HintTitle,
        CrashScreenMode.KnownNoReport => NoNeedTitle,
        CrashScreenMode.KnownSendHelps => match?.Signature.Category == CrashCategory.CoopBug ? KnownCoopBugTitle : KnownBugTitle,
        CrashScreenMode.Unknown => UnknownTitle,
        _ => ""
    };

    /// <summary>True when Send is the big button; false when it is the small faded Send it anyway link.</summary>
    public static bool BigSend(CrashScreenMode mode) => mode != CrashScreenMode.KnownNoReport;
}

/// <summary>What the report says about the match: it goes into the summary the worker reads and into report.json.</summary>
public sealed class CrashReportTriage
{
    public const int MaxTitle = 200;
    public const int MaxWhatHappened = 1000;

    /// <summary>The matched signature id, or null for an unknown crash.</summary>
    [JsonPropertyName("signature")] public string? Signature { get; set; }
    [JsonPropertyName("category")] public string? Category { get; set; }
    [JsonPropertyName("signaturesVersion")] public string SignaturesVersion { get; set; } = "";
    [JsonPropertyName("title")] public string? Title { get; set; }
    [JsonPropertyName("whatHappened")] public string? WhatHappened { get; set; }
    [JsonPropertyName("reportNeeded")] public bool? ReportNeeded { get; set; }
    [JsonPropertyName("trackingIssue")] public int? TrackingIssue { get; set; }
    [JsonPropertyName("labels")] public List<string>? Labels { get; set; }

    /// <summary>Up to 10 cleaned log lines that matched.</summary>
    [JsonPropertyName("evidence")] public List<string> Evidence { get; set; } = new();

    private static readonly Regex Id = new("^[a-z0-9][a-z0-9-]{0,63}$", RegexOptions.CultureInvariant);
    private static readonly Regex Label = new("^[a-z0-9][a-z0-9-]{0,29}$", RegexOptions.CultureInvariant);
    private static readonly Regex Version = new(@"^[A-Za-z0-9_.\-]{1,40}$", RegexOptions.CultureInvariant);

    /// <summary>The triage of a match (null: an unknown crash), every value cleaned and capped.</summary>
    public static CrashReportTriage From(CrashSignatureMatch? match, string signaturesVersion)
    {
        var version = Version.IsMatch(signaturesVersion ?? "") ? signaturesVersion! : "";
        if (match == null || !Id.IsMatch(match.Signature.Id) || !CrashCategory.All.Contains(match.Signature.Category))
            return new CrashReportTriage { SignaturesVersion = version };
        return new CrashReportTriage
        {
            Signature = match.Signature.Id,
            Category = match.Signature.Category,
            SignaturesVersion = version,
            Title = Cap(match.Title, MaxTitle),
            WhatHappened = Cap(match.WhatHappened, MaxWhatHappened),
            ReportNeeded = match.Signature.ReportNeeded,
            TrackingIssue = match.Signature.ReportNeeded && match.Signature.TrackingIssue is > 0 and < 1_000_000 ? match.Signature.TrackingIssue : null,
            Labels = (match.Signature.Labels ?? []).Where(label => Label.IsMatch(label)).Distinct().Take(3).ToList(),
            Evidence = match.Evidence.Select(CrashReports.CleanCrashLine).Where(line => line.Length > 0).Take(CrashSignatureMatcher.MaxEvidence).ToList()
        };
    }

    private static string Cap(string text, int max)
    {
        var cleaned = Regex.Replace(LogZipBuilder.Clean(text ?? ""), @"\s+", " ").Trim();
        return cleaned.Length > max ? cleaned[..(max - 3)] + "..." : cleaned;
    }
}
