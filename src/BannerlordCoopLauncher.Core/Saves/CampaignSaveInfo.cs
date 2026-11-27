using System.Globalization;
using System.Text;
using System.Text.Json;

namespace BannerlordCoopLauncher.Core.Saves;

public sealed record CampaignSaveInfo(string Character, string Level, string Day, IReadOnlyList<string> Modules);

public static class CampaignSaveInfoReader
{
    public static CampaignSaveInfo? Read(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            return null;
        try
        {
            using var stream = File.OpenRead(path);
            var length = (int)Math.Min(stream.Length, 65536);
            var buffer = new byte[length];
            var read = stream.Read(buffer, 0, length);
            return ReadHeader(Encoding.UTF8.GetString(buffer, 0, read));
        }
        catch (IOException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
    }

    public static CampaignSaveInfo? ReadHeader(string header)
    {
        if (string.IsNullOrEmpty(header))
            return null;
        var start = header.IndexOf("{\"List\":", StringComparison.Ordinal);
        if (start < 0)
            return null;
        var json = SliceObject(header, start);
        if (json == null)
            return null;
        try
        {
            using var document = JsonDocument.Parse(json);
            if (!document.RootElement.TryGetProperty("List", out var list))
                return null;
            var modules = Field(list, "Modules")
                .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            return new CampaignSaveInfo(Field(list, "CharacterName"), Field(list, "MainHeroLevel"), Field(list, "DayLong"), modules);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    public static string CampaignDay(string day)
    {
        if (string.IsNullOrWhiteSpace(day))
            return "?";
        if (!double.TryParse(day, NumberStyles.Float, CultureInfo.InvariantCulture, out var value)
            && !double.TryParse(day, NumberStyles.Float, CultureInfo.CurrentCulture, out value))
            return day.Trim();
        if (value < 1)
            return "1";
        return Math.Floor(value).ToString(CultureInfo.InvariantCulture);
    }

    public static string Describe(CampaignSaveInfo info)
    {
        var who = info.Character.Length == 0 ? "Unknown character" : info.Character;
        var level = info.Level.Length == 0 ? "?" : info.Level;
        var day = info.Day.Length == 0 ? "?" : info.Day;
        var mods = info.Modules.Count == 0 ? "no module list in the save" : string.Join(", ", info.Modules);
        return who + ", level " + level + ", day " + day + ". Mods in the save: " + mods + ".";
    }

    private static string Field(JsonElement list, string name) =>
        list.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? ""
            : "";

    private static string? SliceObject(string text, int start)
    {
        var depth = 0;
        var inString = false;
        var escape = false;
        for (var i = start; i < text.Length; i++)
        {
            var ch = text[i];
            if (inString)
            {
                if (escape)
                {
                    escape = false;
                    continue;
                }

                if (ch == '\\')
                {
                    escape = true;
                    continue;
                }

                if (ch == '"')
                    inString = false;
                continue;
            }

            if (ch == '"')
            {
                inString = true;
                continue;
            }

            if (ch == '{')
                depth++;
            else if (ch == '}')
            {
                depth--;
                if (depth == 0)
                    return text.Substring(start, i - start + 1);
            }
        }

        return null;
    }
}
