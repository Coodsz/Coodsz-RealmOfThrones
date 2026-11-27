// Coodsz Settings Sync: the value lines the server sends and the rules both sides use.
// Parts adapted from ModderLords by Ryvius (@PlueRyvius), used with permission.
//
// Engine free (no game, Coop or MCM types), so the launcher's tests compile this file as is.
#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace CoodszSettingsSync
{
    /// <summary>The only value kinds that travel. Anything else stays on each PC.</summary>
    public enum WireKind
    {
        Bool = 'b',
        Int = 'i',
        Float = 'f',
        String = 's',
        Dropdown = 'd',
    }

    /// <summary>Which MCM storage a settings object uses. PerSave never travels, the save carries it.</summary>
    public enum SettingsScope
    {
        Global = 0,
        PerCampaign = 1,
        PerSave = 2,
        Unknown = 3,
    }

    /// <summary>One setting on the wire: its kind, its property id and its value as text.</summary>
    public sealed class WireValue
    {
        public WireValue(WireKind kind, string id, string text)
        {
            Kind = kind;
            Id = id;
            Text = text;
        }

        public WireKind Kind { get; }
        public string Id { get; }
        public string Text { get; }
    }

    public static class SettingsWire
    {
        /// <summary>Bumped when the line format or the message fields change meaning. Other versions are ignored.</summary>
        public const int ProtocolVersion = 1;

        public const int MaxStringLength = 512;
        public const int MaxIdLength = 128;
        public const int MaxValuesPerObject = 512;
        public const int MaxObjectsPerSession = 256;

        public static bool IsKnownProtocol(int version) => version == ProtocolVersion;

        // ---- the server's mark ------------------------------------------------------------------------
        // A player whose game lacks these message types would log an error for each one, so nothing is
        // sent until both sides know the other has the sync. The server puts this mark in the Reason of
        // Coop's NetworkModuleVersionsValidated when the modules match (Coop's client reads Reason only
        // on a refusal), and only answers players who then ask.

        public const string ServerMarkPrefix = "CoodszSettingsSync/";

        public static string ServerMark => ServerMarkPrefix + ProtocolVersion.ToString(CultureInfo.InvariantCulture);

        /// <summary>The protocol version in the server's mark, or null when the reason carries no mark.</summary>
        public static int? ReadServerMark(string? reason)
        {
            if (string.IsNullOrEmpty(reason) || !reason!.StartsWith(ServerMarkPrefix, StringComparison.Ordinal)) return null;
            return int.TryParse(reason.Substring(ServerMarkPrefix.Length), NumberStyles.None, CultureInfo.InvariantCulture, out int version)
                ? version
                : (int?)null;
        }

        // ---- value lines: "<kind char>\t<property id>\t<value>" ---------------------------------------

        public static string? Encode(WireValue value)
        {
            if (!IsValidId(value.Id) || !Enum.IsDefined(typeof(WireKind), value.Kind)) return null;
            string text = value.Text ?? "";
            if (value.Kind == WireKind.String && text.Length > MaxStringLength) return null;
            return ((char)value.Kind).ToString() + "\t" + value.Id + "\t" + text;
        }

        public static WireValue? Decode(string? line)
        {
            if (string.IsNullOrEmpty(line) || line!.Length < 3 || line[1] != '\t') return null;
            var kind = (WireKind)line[0];
            if (!Enum.IsDefined(typeof(WireKind), kind)) return null;
            int tab = line.IndexOf('\t', 2);
            if (tab < 0) return null;
            string id = line.Substring(2, tab - 2);
            string text = line.Substring(tab + 1);
            if (!IsValidId(id)) return null;
            if (kind == WireKind.String && text.Length > MaxStringLength) return null;
            return new WireValue(kind, id, text);
        }

        public static bool IsValidId(string? id) =>
            !string.IsNullOrEmpty(id) && id!.Length <= MaxIdLength && id.IndexOf('\t') < 0 && id.IndexOf('\n') < 0 && id.IndexOf('\r') < 0;

        // ---- value text, invariant culture -----------------------------------------------------------

        public static string FormatBool(bool value) => value ? "1" : "0";
        public static string FormatInt(long value) => value.ToString(CultureInfo.InvariantCulture);
        public static string FormatFloat(double value) => value.ToString("R", CultureInfo.InvariantCulture);

        /// <summary>Text for a live value of one of the 5 kinds, or null when it cannot travel.</summary>
        public static string? FormatValue(WireKind kind, object? value)
        {
            switch (kind)
            {
                case WireKind.Bool: return value is bool b ? FormatBool(b) : null;
                case WireKind.Int:
                    if (value is int i) return FormatInt(i);
                    if (value is long l) return FormatInt(l);
                    return null;
                case WireKind.Float:
                    if (value is float f) return float.IsNaN(f) || float.IsInfinity(f) ? null : FormatFloat(f);
                    if (value is double d) return double.IsNaN(d) || double.IsInfinity(d) ? null : FormatFloat(d);
                    return null;
                case WireKind.String:
                    if (!(value is string s)) return value == null ? "" : null;
                    return s.Length > MaxStringLength ? null : s;
                case WireKind.Dropdown: return value is int index && index >= 0 ? FormatInt(index) : null;
                default: return null;
            }
        }

        /// <summary>Maps MCM's SettingType name (Bool, Int, Float, String, Dropdown) to a wire kind.</summary>
        public static WireKind? KindFromSettingType(string? settingType)
        {
            switch (settingType)
            {
                case "Bool": return WireKind.Bool;
                case "Int": return WireKind.Int;
                case "Float": return WireKind.Float;
                case "String": return WireKind.String;
                case "Dropdown": return WireKind.Dropdown;
                default: return null;
            }
        }

        // ---- the client's decision for one value -----------------------------------------------------

        /// <summary>
        /// What the client does with one incoming value for a setting it has. <paramref name="min"/> and
        /// <paramref name="max"/> are the client's own limits (both 0 means none). <paramref name="dropdownCount"/>
        /// is the number of choices the client's dropdown has. <paramref name="fromFile"/> is true when the server
        /// read its values from the settings files, where a dropdown is stored as a plain whole number.
        /// Returns true with the value to set, or false with the reason it is skipped.
        /// </summary>
        public static bool TryDecide(WireValue incoming, WireKind target, decimal min, decimal max, int dropdownCount,
            bool fromFile, out object? value, out string reason)
        {
            value = null;
            reason = "";
            bool kindOk = incoming.Kind == target
                || (target == WireKind.Float && incoming.Kind == WireKind.Int)
                || (target == WireKind.Dropdown && incoming.Kind == WireKind.Int && fromFile);
            if (!kindOk)
            {
                reason = "kind";
                return false;
            }

            switch (target)
            {
                case WireKind.Bool:
                    if (incoming.Text == "1" || string.Equals(incoming.Text, "true", StringComparison.OrdinalIgnoreCase)) { value = true; return true; }
                    if (incoming.Text == "0" || string.Equals(incoming.Text, "false", StringComparison.OrdinalIgnoreCase)) { value = false; return true; }
                    reason = "value";
                    return false;

                case WireKind.Int:
                {
                    if (!long.TryParse(incoming.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out long number)) { reason = "value"; return false; }
                    decimal clamped = Clamp(number, min, max);
                    if (clamped < int.MinValue || clamped > int.MaxValue) { reason = "value"; return false; }
                    value = (int)clamped;
                    return true;
                }

                case WireKind.Float:
                {
                    if (!double.TryParse(incoming.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out double number)
                        || double.IsNaN(number) || double.IsInfinity(number) || number > float.MaxValue || number < -float.MaxValue)
                    { reason = "value"; return false; }
                    if (HasRange(min, max))
                    {
                        if (number < (double)min) number = (double)min;
                        if (number > (double)max) number = (double)max;
                    }
                    value = (float)number;
                    return true;
                }

                case WireKind.String:
                    if (incoming.Text.Length > MaxStringLength) { reason = "value"; return false; }
                    value = incoming.Text;
                    return true;

                case WireKind.Dropdown:
                    if (!int.TryParse(incoming.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int index)) { reason = "value"; return false; }
                    if (index < 0 || index >= dropdownCount) { reason = "range"; return false; }
                    value = index;
                    return true;

                default:
                    reason = "kind";
                    return false;
            }
        }

        public static bool HasRange(decimal min, decimal max) => !(min == 0 && max == 0) && min <= max;

        public static decimal Clamp(decimal number, decimal min, decimal max)
        {
            if (!HasRange(min, max)) return number;
            if (number < min) return min;
            if (number > max) return max;
            return number;
        }

        // ---- what never travels ----------------------------------------------------------------------

        private static readonly HashSet<string> SkipWords = new HashSet<string>(StringComparer.Ordinal)
        {
            "hotkey", "hotkeys", "key", "keys", "keybind", "keybinds", "keybinding", "keybindings", "inputkey", "shortcut", "shortcuts",
            "ui", "gui", "hud", "interface",
            "sound", "sounds", "audio", "volume", "music",
            "camera", "cam", "zoom", "fov",
        };

        /// <summary>
        /// True for hotkeys, interface, sound and camera settings: they only change one PC's screen, keys or
        /// speakers, and a player keeps their own. Matches whole words in the ids and names, so "Monkey" is kept.
        /// </summary>
        public static bool IsAlwaysSkipped(params string?[] names)
        {
            foreach (string? name in names)
                foreach (string word in Words(name))
                    if (SkipWords.Contains(word)) return true;
            return false;
        }

        /// <summary>True when the value's own type is a game input key (a hotkey stored as a dropdown or text).</summary>
        public static bool IsInputKeyType(string? typeName) =>
            !string.IsNullOrEmpty(typeName) && typeName!.IndexOf("InputKey", StringComparison.Ordinal) >= 0;

        /// <summary>The words of an id or name: split at spaces, symbols and camel case, lower case. "{=abc}" text ids are dropped.</summary>
        public static List<string> Words(string? name)
        {
            var words = new List<string>();
            if (string.IsNullOrEmpty(name)) return words;
            string text = StripTextId(name!);
            var current = new StringBuilder();
            for (int i = 0; i < text.Length; i++)
            {
                char c = text[i];
                if (!char.IsLetterOrDigit(c))
                {
                    Flush(words, current);
                    continue;
                }
                if (current.Length > 0 && char.IsUpper(c))
                {
                    char previous = text[i - 1];
                    bool nextLower = i + 1 < text.Length && char.IsLower(text[i + 1]);
                    if (char.IsLower(previous) || char.IsDigit(previous) || (char.IsUpper(previous) && nextLower))
                        Flush(words, current);
                }
                current.Append(char.ToLowerInvariant(c));
            }
            Flush(words, current);
            return words;
        }

        private static readonly HashSet<string> PrivateWords = new HashSet<string>(StringComparer.Ordinal)
        {
            "token", "tokens", "key", "keys", "apikey", "secret", "secrets", "password", "passwords", "passwd",
            "webhook", "webhooks", "url", "urls", "uri", "path", "paths", "folder", "folders", "file", "files",
            "filename", "dir", "dirs", "directory",
        };

        /// <summary>
        /// A text setting that may hold something private or local to one PC (a token, a password, a web
        /// address, a file or folder): never sent by the host and never taken by a player. Only text settings
        /// are checked this way; bool, int, float and dropdown settings travel as before.
        /// </summary>
        public static bool IsPrivateText(string? value, params string?[] names)
        {
            foreach (string? name in names)
                foreach (string word in Words(name))
                    if (PrivateWords.Contains(word)) return true;
            if (string.IsNullOrEmpty(value)) return false;
            return value!.IndexOf("://", StringComparison.Ordinal) >= 0 || value.IndexOf(":\\", StringComparison.Ordinal) >= 0
                || value.StartsWith("/", StringComparison.Ordinal) || value.StartsWith("\\", StringComparison.Ordinal);
        }

        private static void Flush(List<string> words, StringBuilder current)
        {
            if (current.Length == 0) return;
            words.Add(current.ToString());
            current.Clear();
        }

        private static string StripTextId(string text)
        {
            // "{=MyMod_Key}Open menu key" -> "Open menu key"
            while (text.StartsWith("{=", StringComparison.Ordinal))
            {
                int end = text.IndexOf('}');
                if (end < 0) break;
                text = text.Substring(end + 1);
            }
            return text;
        }
    }

    /// <summary>
    /// The mods whose settings players keep for themselves, which the server never sends. Same file and shape as
    /// ModderLords: recipes.json in the server module's folder, a "PlayersOwnSettings" array of module ids, so a
    /// list made by either tool works in the other. An entry may also be an MCM settings id (Coodsz Host writes one
    /// when it cannot tell which module a settings file belongs to).
    /// </summary>
    public sealed class KeepOwnList
    {
        public const string FileName = "recipes.json";
        public const string Field = "PlayersOwnSettings";

        private readonly HashSet<string> _entries = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        public KeepOwnList(IEnumerable<string>? entries = null)
        {
            if (entries == null) return;
            foreach (string entry in entries)
            {
                string trimmed = (entry ?? "").Trim();
                if (trimmed.Length > 0) _entries.Add(trimmed);
            }
        }

        public int Count => _entries.Count;

        /// <summary>True when any of the names (module id, settings id, settings folder) is on the list.</summary>
        public bool Keeps(params string?[] names)
        {
            foreach (string? name in names)
                if (!string.IsNullOrEmpty(name) && _entries.Contains(name!)) return true;
            return false;
        }
    }
}
