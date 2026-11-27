using BannerlordCoopLauncher.Core.IO;
using System.Globalization;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace BannerlordCoopLauncher.Core.Localization;

public sealed record UiLanguageInfo(string Code, string NativeName, bool RightToLeft);

/// <summary>
/// Turns the English text the tools show into the picked language. The tables are
/// keyed by the English text itself (Languages\xx.json), so the code keeps writing
/// English and the window layer translates what reaches the screen. Text with values
/// in it matches entries that hold {0}, {1}, and so on. Anything without an entry
/// stays English. Logs stay English on purpose so support can read them.
/// </summary>
public static class Translator
{
    public const string English = "en";

    public static readonly IReadOnlyList<UiLanguageInfo> Languages = new[]
    {
        new UiLanguageInfo(English, "English", false),
        new UiLanguageInfo("ar", "العربية", true),
        new UiLanguageInfo("de", "Deutsch", false),
        new UiLanguageInfo("fr", "Français", false),
        new UiLanguageInfo("ja", "日本語", false),
        new UiLanguageInfo("ko", "한국어", false),
        new UiLanguageInfo("pt", "Português", false),
        new UiLanguageInfo("ru", "Русский", false),
        new UiLanguageInfo("tr", "Türkçe", false),
        new UiLanguageInfo(SimplifiedChinese, "简体中文", false),
    };

    public const string SimplifiedChinese = "zh-CN";

    /// <summary>Chinese, Japanese or Korean: the windows tell WPF, so it picks the right font for the letters.</summary>
    public static bool EastAsian => Current is "ja" or "ko" or SimplifiedChinese;

    private static readonly object Gate = new();
    private static Table _table = Table.Empty;

    public static string Current { get; private set; } = English;

    public static bool RightToLeft => Languages.FirstOrDefault(language => language.Code == Current)?.RightToLeft == true;

    public static event Action? Changed;

    /// <summary>One file for every Coodsz tool, so a player picks the language once.</summary>
    public static string SettingsPath { get; set; } =
        Path.Combine(ToolData.RoamingRoot, "language.txt");

    /// <summary>The saved pick, or the Windows language when it is one of ours, or English.</summary>
    public static string StartupLanguage()
    {
        try
        {
            if (File.Exists(SettingsPath))
            {
                var saved = File.ReadAllText(SettingsPath).Trim();
                if (IsKnown(saved))
                    return saved;
            }
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }

        return FromCulture(CultureInfo.CurrentUICulture);
    }

    /// <summary>Our language for a Windows language, or English. Any Chinese Windows gets Simplified Chinese.</summary>
    public static string FromCulture(CultureInfo culture)
    {
        var system = culture.TwoLetterISOLanguageName;
        if (system == "zh")
            return SimplifiedChinese;
        return IsKnown(system) ? system : English;
    }

    public static bool HasSavedLanguage
    {
        get
        {
            try
            {
                return File.Exists(SettingsPath) && IsKnown(File.ReadAllText(SettingsPath).Trim());
            }
            catch (IOException) { return false; }
            catch (UnauthorizedAccessException) { return false; }
        }
    }

    public static bool IsKnown(string code) => Languages.Any(language => language.Code == code);

    public static void Use(string code, bool save = false)
    {
        if (!IsKnown(code))
            code = English;
        lock (Gate)
        {
            _table = code == English ? Table.Empty : Table.Load(code);
            Current = code;
        }

        if (save)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath)!);
                File.WriteAllText(SettingsPath, code);
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }

        Changed?.Invoke();
    }

    /// <summary>The text in the current language, or the same text when there is no entry.</summary>
    public static string T(string? english)
    {
        if (string.IsNullOrEmpty(english))
            return english ?? "";
        Table table;
        lock (Gate)
            table = _table;
        return table.Translate(english);
    }

    /// <summary>Loads a language table without switching to it (tests, checks).</summary>
    public static IReadOnlyDictionary<string, string> Entries(string code) => Table.Load(code).Exact;

    private sealed class Table
    {
        public static readonly Table Empty = new(new Dictionary<string, string>(StringComparer.Ordinal));

        // Pattern only takes a text of several lines when the key has several lines too; Wide takes
        // any text and is the last try for a text of several lines (see Lookup).
        private readonly List<(Regex Pattern, Regex Wide, string Text)> _templates = new();
        private readonly Dictionary<string, string> _seen = new(StringComparer.Ordinal);

        private Table(Dictionary<string, string> exact)
        {
            Exact = exact;
            foreach (var (key, value) in exact.OrderByDescending(pair => Regex.Replace(pair.Key, @"\{\d+\}", "").Length))
            {
                if (!key.Contains('{'))
                    continue;
                var literal = Regex.Replace(key, @"\{\d+\}", "");
                if (literal.Count(char.IsLetter) < 4)
                    continue;
                var pattern = Pattern(key, wide: false);
                _templates.Add((pattern, key.Contains('\n') ? pattern : Pattern(key, wide: true), value));
            }
        }

        public Dictionary<string, string> Exact { get; }

        public static Table Load(string code)
        {
            var assembly = typeof(Translator).Assembly;
            using var stream = assembly.GetManifestResourceStream("Coodsz.Languages." + code + ".json");
            if (stream == null)
                return Empty;
            var entries = JsonSerializer.Deserialize<Dictionary<string, string>>(stream) ?? new();
            var exact = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var (key, value) in entries)
                if (!string.IsNullOrWhiteSpace(value))
                    exact[key] = value;
            return new Table(exact);
        }

        public string Translate(string text)
        {
            if (Exact.Count == 0)
                return text;
            lock (_seen)
                if (_seen.TryGetValue(text, out var known))
                    return known;

            var result = Lookup(text, 0) ?? text;
            lock (_seen)
            {
                if (_seen.Count > 4000)
                    _seen.Clear();
                _seen[text] = result;
            }

            return result;
        }

        private string? Lookup(string text, int depth)
        {
            if (Exact.TryGetValue(text, out var exact))
                return exact;

            var core = text.Trim();
            if (core.Length == 0)
                return null;
            if (core.Length != text.Length)
            {
                var inner = Lookup(core, depth);
                if (inner == null)
                    return null;
                var start = text.IndexOf(core, StringComparison.Ordinal);
                return text[..start] + inner + text[(start + core.Length)..];
            }

            if (depth < 2 && Template(text, depth, wide: false) is { } template)
                return template;

            // A text of several lines goes line by line when every line is translated that way, so a
            // short template like "Burning Empires {0}" can't take a whole message and leave its other
            // lines in English. When only some lines are, a one line template over the whole text (a
            // value that holds a line break) is tried first, the way it was before, then those lines.
            var lines = text.Contains('\n');
            if (depth > 0)
                return lines && depth < 2 ? Template(text, depth, wide: true) : null;

            // Messages put together from several known lines or sentences.
            if (lines)
            {
                var byLine = Pieces(text.Split('\n'), "\n", out var everyLine);
                return everyLine ? byLine : Template(text, depth, wide: true) ?? byLine;
            }

            var sentences = Regex.Split(text, @"(?<=[.!?:])\s+(?=[A-Z0-9])");
            return sentences.Length > 1 ? Pieces(sentences, " ", out _) : null;
        }

        private string? Template(string text, int depth, bool wide)
        {
            foreach (var (pattern, widePattern, translated) in _templates)
            {
                var match = (wide ? widePattern : pattern).Match(text);
                if (!match.Success)
                    continue;
                return Regex.Replace(translated, @"\{(\d+)\}", placeholder =>
                {
                    var group = match.Groups["p" + placeholder.Groups[1].Value];
                    return group.Success ? Lookup(group.Value, depth + 1) ?? group.Value : placeholder.Value;
                });
            }

            return null;
        }

        // every: true when each piece that has text was translated.
        private string? Pieces(string[] pieces, string separator, out bool every)
        {
            var changed = false;
            every = true;
            var output = new StringBuilder();
            for (var i = 0; i < pieces.Length; i++)
            {
                // Chinese and Japanese put no space after their full width 。！？：
                if (i > 0 && !(separator == " " && output.Length > 0 && "。！？：".Contains(output[^1])))
                    output.Append(separator);
                var piece = pieces[i].TrimEnd('\r');
                var translated = piece.Length == 0 ? null : Lookup(piece, 0);
                changed |= translated != null;
                every &= translated != null || piece.Trim().Length == 0;
                output.Append(translated ?? piece);
                if (pieces[i].EndsWith('\r'))
                    output.Append('\r');
            }

            return changed ? output.ToString() : null;
        }

        // wide: a placeholder may take line breaks even when the key has none.
        private static Regex Pattern(string key, bool wide)
        {
            var pattern = new StringBuilder("^");
            var used = new HashSet<string>();
            var last = 0;
            foreach (Match placeholder in Regex.Matches(key, @"\{(\d+)\}"))
            {
                pattern.Append(Regex.Escape(key[last..placeholder.Index]));
                var name = "p" + placeholder.Groups[1].Value;
                pattern.Append(used.Add(name) ? "(?<" + name + ">.*?)" : @"\k<" + name + ">");
                last = placeholder.Index + placeholder.Length;
            }

            pattern.Append(Regex.Escape(key[last..])).Append('$');
            var lines = wide || key.Contains('\n') ? RegexOptions.Singleline : RegexOptions.None;
            return new Regex(pattern.ToString(), lines | RegexOptions.CultureInvariant);
        }
    }
}
