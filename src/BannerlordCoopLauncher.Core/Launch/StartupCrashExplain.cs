using System.Text.RegularExpressions;

namespace BannerlordCoopLauncher.Core.Launch;

public static class StartupCrashExplain
{
    public readonly record struct ModName(string Id, string Name);

    public readonly record struct Capture(int? ExitCode, string? ReportText, string? GameLog);

    public static IReadOnlyList<string> Explain(
        bool stillRunning,
        int? exitCode,
        string? reportText,
        string? gameLog,
        IReadOnlyList<ModName> extras,
        IReadOnlyCollection<string> workingIds,
        bool setupUsesWorkshop = true)
    {
        if (stillRunning)
            return ["The game is still open. This test did not see a startup crash. If it closes on its own, this log will say what to change."];

        var code = exitCode ?? ReadExitCode(reportText);
        var log = gameLog ?? "";
        var clean = log.Contains("Final_cleanup", StringComparison.OrdinalIgnoreCase)
            || log.Contains("Deleting Game", StringComparison.OrdinalIgnoreCase);
        var hardCrash = code is int value && value != 0 && value != -1;
        if (clean && !hardCrash)
        {
            // A clean exit on frame 0, before the managed (mod) code starts, is the
            // engine giving up early, not a player closing the main menu.
            var frames = Regex.Matches(log, @"Frame#\s*:\s*(\d+)");
            var lastFrame = frames.Count > 0 ? frames[^1].Groups[1].Value : null;
            var managedStarted = log.Contains("Mono Loading Step", StringComparison.OrdinalIgnoreCase);
            if (lastFrame == "0" && !managedStarted)
            {
                var sawWorkshop = log.Contains(@"workshop\content\261550", StringComparison.OrdinalIgnoreCase)
                    || log.Contains("workshop/content/261550", StringComparison.OrdinalIgnoreCase);
                return sawWorkshop || !setupUsesWorkshop
                    ? ["The game closed by itself before the main menu, before any mod was loaded. Press Test to main menu again. If it closes again, click Send crash report and post the code on Discord."]
                    : ["The game closed by itself before the main menu. It did not see any Steam Workshop mods, so the mods on this setup were missing. Open Steam and sign in, start this launcher yourself (not as administrator), then press Test to main menu again."];
            }

            return ["The game closed normally. This test did not see a startup crash."];
        }

        var failedLoad = FindFailedLoad(log, extras, workingIds);
        if (failedLoad != null)
            return failedLoad;

        var lines = log.Replace("\r", "").Split('\n');
        var windows = ExceptionWindows(lines);
        for (var i = windows.Count - 1; i >= 0; i--)
        {
            var named = extras.Where(mod => Mentions(windows[i].Text, mod)).ToList();
            if (named.Count > 0)
                return BlameExtras(windows[i].Quote, named);
        }

        var dependency = FindDependency(log);
        if (dependency != null)
        {
            var owner = Describe(dependency.Value.ModuleId, extras);
            var needed = Describe(dependency.Value.NeedsId, extras);
            return
            [
                owner + " needs " + needed + ", and that mod is not loaded. Add " + needed + " and leave it on, or turn " + owner + " off. Then click Save mods and press Test to main menu again. Leave Bannerlord Coop where it is."
            ];
        }

        var duplicate = FindDuplicate(log);
        if (duplicate != null)
        {
            var name = Describe(duplicate, extras);
            return
            [
                name + " is in more than one folder. Move the extra folder out of the Bannerlord Modules folder, click Check mods, and press Test to main menu again."
            ];
        }

        var shader = FindShaderCache(log, extras);
        if (shader != null)
            return [shader];

        for (var i = windows.Count - 1; i >= 0; i--)
        {
            var working = workingIds.FirstOrDefault(id => ContainsToken(windows[i].Text, id));
            if (working != null)
            {
                return
                [
                    "The crash says: " + windows[i].Quote,
                    Describe(working, extras) + " is in the working list. Leave Bannerlord Coop and the working list where they are. Turn off the extra mods, click Save mods, and press Test to main menu again."
                ];
            }
        }

        if (extras.Count == 0)
            return [NoExtraModsLine];

        return ["The game closed while starting. The log does not name one extra mod. Turn off the extra mods you added, click Save mods, and press Test to main menu again. Leave Bannerlord Coop where it is."];
    }

    public const string NoExtraModsLine = "The game closed while starting. This setup has no extra mods. Leave Bannerlord Coop where it is, and use Open client logs if you need the full report.";

    /// <summary>
    /// A game closed while another Bannerlord was open, and neither report.txt nor its engine log has an exception or
    /// names a mod: the two copies clashing over the same log files is the likely cause, so there is no mod to blame
    /// or report (TAOM CR-W4H9X). Any exception line, a core module's included, still gets the hint and the report.
    /// </summary>
    public static bool OnlyTheOtherCopy(bool otherGameRunning, IReadOnlyCollection<string> named, IReadOnlyList<string> lines,
        string? reportText, string? gameLog) =>
        otherGameRunning && named.Count == 0 && lines is [NoExtraModsLine]
        && !HasException(reportText) && !HasException(gameLog);

    private static bool HasException(string? text) =>
        !string.IsNullOrEmpty(text) && ExceptionWindows(text.Replace("\r", "").Split('\n')).Count > 0;

    public static bool SawStartupCrash(IReadOnlyList<string> lines) =>
        lines.Count > 0 && !lines[0].Contains("did not see a startup crash", StringComparison.OrdinalIgnoreCase);

    public static bool NamesShaderCache(IReadOnlyList<string> lines) =>
        lines.Count > 0 && lines[0].Contains("old shader file", StringComparison.OrdinalIgnoreCase);

    public static IReadOnlyList<string> LoadedModuleIds(string? logText)
    {
        if (string.IsNullOrWhiteSpace(logText))
            return Array.Empty<string>();
        var start = logText.IndexOf("Current modules:", StringComparison.OrdinalIgnoreCase);
        if (start < 0)
            return Array.Empty<string>();
        var ids = new List<string>();
        foreach (var raw in logText[start..].Replace("\r", "").Split('\n').Skip(1))
        {
            if (raw.Contains("===="))
                break;
            var match = Regex.Match(raw, @"\b(?<id>[A-Za-z][A-Za-z0-9_.]*)\s*$");
            if (match.Success)
                ids.Add(match.Groups["id"].Value);
        }

        return ids;
    }

    private static string? FindShaderCache(string log, IReadOnlyList<ModName> extras)
    {
        var match = Regex.Match(
            log,
            @"Shader cache version of the external module \((?<name>[^)]+)\) is invalid",
            RegexOptions.IgnoreCase);
        if (!match.Success)
            return null;
        var name = Describe(match.Groups["name"].Value.Trim(), extras);
        return name + " has an old shader file inside the mod. Press Test to main menu again. The launcher sets that file aside and the game builds a new one. Leave Bannerlord Coop where it is.";
    }

    public static Capture Read(string documentsRoot, int processId) =>
        Read(documentsRoot, processId, EngineLogDirectory());

    public static Capture Read(string documentsRoot, int processId, string? engineLogDirectory)
    {
        var folder = FindReportFolder(documentsRoot, processId);
        if (folder == null)
            return ReadEngineLog(engineLogDirectory, processId, default);

        var reportPath = Path.Combine(folder, "report.txt");
        var report = ReadText(reportPath);
        var logDirectory = Path.Combine(folder, "logs");
        var preferred = Path.Combine(logDirectory, "rgl_log_" + processId + ".txt");
        var logPath = File.Exists(preferred)
            ? preferred
            : Directory.Exists(logDirectory)
                ? Directory.GetFiles(logDirectory, "rgl_log_*.txt")
                    .Where(path => !Path.GetFileName(path).Contains("errors", StringComparison.OrdinalIgnoreCase))
                    .OrderByDescending(path => new FileInfo(path).Length)
                    .FirstOrDefault()
                : null;
        return ReadEngineLog(engineLogDirectory, processId, new Capture(ReadExitCode(report), report, logPath == null ? null : ReadTail(logPath)));
    }

    public static string EngineLogDirectory() =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "Mount and Blade II Bannerlord", "logs");

    private static Capture ReadEngineLog(string? engineLogDirectory, int processId, Capture capture)
    {
        if (!string.IsNullOrWhiteSpace(capture.GameLog) || string.IsNullOrWhiteSpace(engineLogDirectory))
            return capture;
        var path = Path.Combine(engineLogDirectory, "rgl_log_" + processId + ".txt");
        return File.Exists(path) ? capture with { GameLog = ReadTail(path) } : capture;
    }

    /// <summary>
    /// The extra mods a crash names, for the report window to mark as suggested Broken: the
    /// mods that could not be loaded, or else the mods in the last exception of the engine log
    /// (then of report.txt) that names any. Empty when the crash names none of them.
    /// </summary>
    public static IReadOnlyList<string> NamedMods(string? reportText, string? gameLog, IReadOnlyList<ModName> extras)
    {
        var failed = FailedLoadMods(gameLog ?? "", extras);
        if (failed.Count > 0)
            return failed.Select(mod => mod.Id).ToList();
        foreach (var text in new[] { gameLog ?? "", reportText ?? "" })
        {
            var windows = ExceptionWindows(text.Replace("\r", "").Split('\n'));
            for (var i = windows.Count - 1; i >= 0; i--)
            {
                var named = extras.Where(mod => Mentions(windows[i].Text, mod)).ToList();
                if (named.Count > 0)
                    return named.Select(mod => mod.Id).ToList();
            }
        }

        return Array.Empty<string>();
    }

    private static IReadOnlyList<string>? FindFailedLoad(
        string log,
        IReadOnlyList<ModName> extras,
        IReadOnlyCollection<string> workingIds)
    {
        var named = FailedLoadMods(log, extras);
        if (named.Count > 0)
        {
            var names = string.Join(", ", named.Select(Display));
            return
            [
                names + " could not be loaded. Turn " + names + " off, click Save mods, and press Test to main menu again. Leave Bannerlord Coop where it is."
            ];
        }

        var working = Regex.Match(log, @"\b(?<id>[A-Za-z0-9_.]+) could not be loaded correctly", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        if (working.Success && workingIds.Any(id => string.Equals(id, working.Groups["id"].Value, StringComparison.OrdinalIgnoreCase)))
        {
            return
            [
                working.Groups["id"].Value + " is in the working list and could not be loaded. Leave Bannerlord Coop and the working list where they are. Turn off the extra mods, click Save mods, and press Test to main menu again."
            ];
        }

        return null;
    }

    private static List<ModName> FailedLoadMods(string log, IReadOnlyList<ModName> extras)
    {
        var named = new List<ModName>();
        foreach (Match match in Regex.Matches(log, @"\b(?<id>[A-Za-z0-9_.]+) could not be loaded correctly", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
        {
            var id = match.Groups["id"].Value;
            var extra = extras.FirstOrDefault(mod => string.Equals(mod.Id, id, StringComparison.OrdinalIgnoreCase));
            if (!string.IsNullOrWhiteSpace(extra.Id) && named.All(mod => !string.Equals(mod.Id, extra.Id, StringComparison.OrdinalIgnoreCase)))
                named.Add(extra);
        }

        foreach (Match match in Regex.Matches(log, "\"(?<name>[^\"]+)\" submodule could not be loaded", RegexOptions.IgnoreCase))
        {
            var quoted = match.Groups["name"].Value;
            foreach (var extra in extras)
            {
                if (named.Any(mod => string.Equals(mod.Id, extra.Id, StringComparison.OrdinalIgnoreCase)))
                    continue;
                if (quoted.Contains(Display(extra), StringComparison.OrdinalIgnoreCase) || quoted.Contains(extra.Id, StringComparison.OrdinalIgnoreCase))
                    named.Add(extra);
            }
        }

        return named;
    }

    public static string? FindReportFolder(string documentsRoot, int processId)
    {
        var root = Path.Combine(documentsRoot, "Coop Crash Reports");
        if (!Directory.Exists(root))
            return null;
        var marker = "_client_" + processId + "_";
        return Directory.GetDirectories(root)
            .Where(dir => Path.GetFileName(dir).Contains(marker, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(Directory.GetLastWriteTimeUtc)
            .FirstOrDefault();
    }

    private static IReadOnlyList<string> BlameExtras(string quote, IReadOnlyList<ModName> named)
    {
        var names = string.Join(", ", named.Select(mod => Display(mod)));
        var change = named.Count == 1
            ? "Turn " + names + " off, click Save mods, and press Test to main menu again."
            : "Turn these off, click Save mods, and press Test to main menu again. Add them back one at a time: " + names + ".";
        return
        [
            "The crash says: " + quote,
            names + (named.Count == 1 ? " shows up in that crash. " : " show up in that crash. ") + change + " Leave Bannerlord Coop where it is."
        ];
    }

    private readonly record struct ExceptionWindow(string Quote, string Text);

    private static readonly Regex ExceptionWord = new(@"Exception(?![A-Za-z])", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static List<ExceptionWindow> ExceptionWindows(string[] lines)
    {
        var windows = new List<ExceptionWindow>();
        for (var i = 0; i < lines.Length; i++)
        {
            // Exception with no letter after it (EXCEPTION_ACCESS_VIOLATION counts). "Loading assembly: ...\BetterExceptionWindow.dll"
            // is not one: the last DLL a game loaded used to be blamed for its crash (TAOM CR-W4H9X).
            if (!ExceptionWord.IsMatch(lines[i]) || IsNoise(lines[i]))
                continue;
            var quote = Clean(lines[i]);
            var text = string.Join(" ", lines.Skip(i).Take(6).Select(Clean));
            windows.Add(new ExceptionWindow(quote, text));
        }

        return windows;
    }

    private static (string ModuleId, string NeedsId)? FindDependency(string log)
    {
        var dependent = Regex.Match(
            log,
            @"dependent module:\s*(?<dep>[A-Za-z0-9_.]+).{0,80}?\bmodule\s+(?<mod>[A-Za-z0-9_.]+)",
            RegexOptions.IgnoreCase | RegexOptions.Singleline);
        if (dependent.Success)
            return (dependent.Groups["mod"].Value, dependent.Groups["dep"].Value);

        var depends = Regex.Match(
            log,
            @"\b(?<mod>[A-Za-z0-9_.]+)\s+depends on(?: module)?\s+(?<dep>[A-Za-z0-9_.]+)",
            RegexOptions.IgnoreCase);
        if (depends.Success)
            return (depends.Groups["mod"].Value, depends.Groups["dep"].Value);
        return null;
    }

    private static string? FindDuplicate(string log)
    {
        var match = Regex.Match(log, @"module(?: id)?\s+(?<id>[A-Za-z0-9_.]+)\s+is already", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        return match.Success ? match.Groups["id"].Value : null;
    }

    private static bool Mentions(string text, ModName mod)
    {
        if (ContainsToken(text, mod.Id))
            return true;
        var name = Display(mod);
        if (name.Length >= 5 && text.Contains(name, StringComparison.OrdinalIgnoreCase))
            return true;
        var shortName = name.Split('(')[0].Trim();
        return shortName.Length >= 5
            && !string.Equals(shortName, name, StringComparison.OrdinalIgnoreCase)
            && text.Contains(shortName, StringComparison.OrdinalIgnoreCase);
    }

    private static bool ContainsToken(string text, string token)
    {
        if (string.IsNullOrWhiteSpace(token) || token.Length < 3)
            return false;
        return Regex.IsMatch(text, @"\b" + Regex.Escape(token) + @"\b", RegexOptions.IgnoreCase);
    }

    private static string Describe(string id, IReadOnlyList<ModName> extras)
    {
        var known = extras.FirstOrDefault(mod => string.Equals(mod.Id, id, StringComparison.OrdinalIgnoreCase));
        return string.IsNullOrWhiteSpace(known.Id) ? id : Display(known);
    }

    private static string Display(ModName mod) =>
        string.IsNullOrWhiteSpace(mod.Name) ? mod.Id : mod.Name;

    private static bool IsNoise(string line)
    {
        return line.Contains("FMOD", StringComparison.OrdinalIgnoreCase)
            || line.Contains("Serilog", StringComparison.OrdinalIgnoreCase)
            || line.Contains("AccessTools", StringComparison.OrdinalIgnoreCase)
            || line.Contains("ReflectionTypeLoadException", StringComparison.OrdinalIgnoreCase)
            || line.Contains("event index", StringComparison.OrdinalIgnoreCase)
            || line.Contains("Non-Zero Device", StringComparison.OrdinalIgnoreCase);
    }

    private static string Clean(string line)
    {
        var text = Regex.Replace(line.Trim(), @"^\[[^\]]+\]\s*", "");
        return text.Length <= 180 ? text : text[..177] + "...";
    }

    private static int? ReadExitCode(string? report)
    {
        if (string.IsNullOrWhiteSpace(report))
            return null;
        var match = Regex.Match(report, @"Exit code:\s*(-?\d+)", RegexOptions.IgnoreCase);
        return match.Success && int.TryParse(match.Groups[1].Value, out var code) ? code : null;
    }

    private static string? ReadText(string path)
    {
        if (!File.Exists(path))
            return null;
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    private static string ReadTail(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        const int max = 400_000;
        if (stream.Length > max)
            stream.Seek(-max, SeekOrigin.End);
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
