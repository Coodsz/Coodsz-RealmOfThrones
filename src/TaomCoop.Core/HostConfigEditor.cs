using System.Text.Json;
using System.Text.RegularExpressions;

namespace TaomCoop.Core;

public static class HostConfigEditor
{
    public const string AutosaveRange = "Autosave minutes must be from 0 to 1440.";

    /// <summary>
    /// The Autosave minutes box. Empty means keep the value already in server-config.json (null),
    /// so a blank box no longer blocks Save settings. Text that is not a whole number, or a number
    /// outside 0 to 1440, still explains the range.
    /// </summary>
    public static int? ParseAutosave(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return null;
        if (!int.TryParse(text.Trim(), System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out var minutes)
            || minutes is < 0 or > 1440)
            throw new InvalidOperationException(AutosaveRange);
        return minutes;
    }

    /// <summary>Writes the host settings into server-config.json. A null autosaveMinutes keeps the stored value.</summary>
    public static string Apply(string raw, string saveName, string password, int? autosaveMinutes, bool steam, bool logFile)
    {
        if (string.IsNullOrWhiteSpace(saveName)
            || saveName.Equals("default_new_game", StringComparison.OrdinalIgnoreCase)
            || saveName != Path.GetFileName(saveName)
            || Path.GetExtension(saveName).Length != 0
            || saveName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
            throw new InvalidOperationException("Choose one existing TAOM campaign name. default_new_game is a vanilla bootstrap and will not load.");

        if (autosaveMinutes is < 0 or > 1440)
            throw new InvalidOperationException(AutosaveRange);

        raw = SetString(raw, "saveName", saveName.Trim());
        raw = SetString(raw, "password", password);
        if (autosaveMinutes is { } minutes)
            raw = SetNumber(raw, "autosaveMinutes", minutes);
        raw = SetBool(raw, "steam", steam);
        raw = SetBool(raw, "logFile", logFile);
        return raw;
    }

    private static string SetString(string raw, string name, string value) =>
        Replace(raw, name, JsonSerializer.Serialize(value));

    private static string SetNumber(string raw, string name, int value) =>
        Replace(raw, name, value.ToString());

    private static string SetBool(string raw, string name, bool value) =>
        Replace(raw, name, value ? "true" : "false");

    private static string Replace(string raw, string name, string token)
    {
        raw = JsoncValues.Repair(raw);
        var pattern = "(\"" + Regex.Escape(name) + "\"\\s*:\\s*)(?:\"(?:\\\\.|[^\"\\\\])*\"|-?\\d+(?:\\.\\d+)?|true|false)";
        var regex = new Regex(pattern, RegexOptions.IgnoreCase, TimeSpan.FromSeconds(1));
        var count = regex.Matches(raw).Count;
        if (count > 1)
            throw new InvalidOperationException("server-config.json does not have one " + name + " setting.");
        if (count == 0)
            return JsoncValues.Insert(raw, name, token);
        return regex.Replace(raw, match => match.Groups[1].Value + token, 1);
    }
}
