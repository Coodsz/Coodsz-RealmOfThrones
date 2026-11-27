using System.Text.RegularExpressions;

namespace TaomCoop.Core;

public static class JsoncValues
{
    public static string Repair(string json)
    {
        if (string.IsNullOrEmpty(json))
            return json;
        var text = FixBoolCase(RestoreDollarNumbers(json), null);
        text = Regex.Replace(text, "(\"modOptions\"\\s*:\\s*),", "$1{}", RegexOptions.None, TimeSpan.FromSeconds(1));
        string previous;
        do
        {
            previous = text;
            text = Regex.Replace(text, "\\{(\\s*),", "{$1", RegexOptions.None, TimeSpan.FromSeconds(1));
            text = Regex.Replace(text, ",(\\s*),", ",$1", RegexOptions.None, TimeSpan.FromSeconds(1));
        }
        while (!text.Equals(previous, StringComparison.Ordinal));
        return text;
    }

    /// <summary>One value written as True, FALSE and so on. The Coop server's parser only
    /// reads lowercase true and false, and one capital letter makes it drop the whole file.</summary>
    public readonly record struct BoolFix(int Line, string Key, string Written, string Fixed);

    private static readonly Regex CasedBool = new(
        "(\"(?<key>[^\"\\r\\n]+)\"[ \\t]*:[ \\t]*)(?<value>true|false)(?=[ \\t]*(?:,|\\}|\\]|//|/\\*|\\r|\\n|$))",
        RegexOptions.IgnoreCase | RegexOptions.Multiline | RegexOptions.CultureInvariant,
        TimeSpan.FromSeconds(1));

    /// <summary>Lowercases true/false values written with capitals (hand edits like
    /// "clientsCanUseCheats": True). Only a key's whole value is touched, so text
    /// inside strings is left alone. Each change is added to fixes when it is given.</summary>
    public static string FixBoolCase(string json, List<BoolFix>? fixes)
    {
        if (string.IsNullOrEmpty(json))
            return json;
        return CasedBool.Replace(json, match =>
        {
            var value = match.Groups["value"].Value;
            var lower = value.ToLowerInvariant();
            if (value.Equals(lower, StringComparison.Ordinal))
                return match.Value;
            fixes?.Add(new BoolFix(LineOf(json, match.Groups["value"].Index), match.Groups["key"].Value, value, lower));
            return match.Groups[1].Value + lower;
        });
    }

    public static int LineOf(string text, int index)
    {
        var line = 1;
        for (var i = 0; i < index && i < text.Length; i++)
            if (text[i] == '\n')
                line++;
        return line;
    }

    private static string RestoreDollarNumbers(string json)
    {
        var newline = json.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
        var lines = json.Replace("\r\n", "\n").Split('\n');
        var output = new List<string>(lines.Length);
        var changed = false;
        foreach (var line in lines)
        {
            var broken = Regex.Match(line, "^(\\s*)\\$1(\\d+(?:\\.\\d+)?)\\s*,?\\s*$", RegexOptions.None, TimeSpan.FromSeconds(1));
            if (!broken.Success)
            {
                var kept = Regex.Replace(
                    line,
                    "(\"[^\"]+\"\\s*:\\s*)\\$1(\\d+(?:\\.\\d+)?)",
                    match => match.Groups[1].Value + match.Groups[2].Value,
                    RegexOptions.None,
                    TimeSpan.FromSeconds(1));
                if (!kept.Equals(line, StringComparison.Ordinal))
                    changed = true;
                output.Add(kept);
                continue;
            }

            changed = true;
            var key = KeyAbove(output);
            if (key == null)
                continue;
            var indent = broken.Groups[1].Value.Length == 0 ? "  " : broken.Groups[1].Value;
            output.Add(indent + "\"" + key + "\": " + broken.Groups[2].Value + ",");
        }

        return changed ? string.Join(newline, output) : json;
    }

    private static string? KeyAbove(List<string> lines)
    {
        for (var i = lines.Count - 1; i >= 0 && i >= lines.Count - 6; i--)
        {
            var line = lines[i].Trim();
            if (line.Length == 0)
                continue;
            if (!line.StartsWith("//", StringComparison.Ordinal))
                return null;
            if (line.Contains("scheduled restarts", StringComparison.OrdinalIgnoreCase))
                return "autoRestartHours";
            if (line.Contains("autosav", StringComparison.OrdinalIgnoreCase))
                return "autosaveMinutes";
        }

        return null;
    }

    /// <summary>Puts key inside the named block (for example modOptions), because the Coop
    /// server only reads mod options from there. Makes the block when the file has none.</summary>
    public static string InsertInto(string json, string section, string key, string raw)
    {
        if (!Regex.IsMatch(json, "\"" + Regex.Escape(section) + "\"\\s*:\\s*\\{", RegexOptions.None, TimeSpan.FromSeconds(1)))
            return Insert(json, section, "{\n    \"" + key + "\": " + raw + "\n  }");
        if (Block(json, section) is not { } block)
            throw new InvalidOperationException("Could not find the end of " + section + " in the configuration file.");
        var (start, close) = block;
        // The comma goes right after the last value, even when a comment follows it.
        var last = LastValueEnd(json, start + 1, close);
        if (last >= 0 && json[last] != ',')
        {
            json = json.Insert(last + 1, ",");
            close++;
        }

        var head = json[..close].TrimEnd();
        return head + "\n    \"" + key + "\": " + raw + "\n  " + json[close..];
    }

    /// <summary>Where the named block's braces are, or null when the file has no such block.</summary>
    public static (int Open, int Close)? Block(string json, string section)
    {
        foreach (var property in Properties(json))
        {
            if (property.Depth != 1 || !property.Key.Equals(section, StringComparison.OrdinalIgnoreCase)
                || property.ValueStart >= json.Length || json[property.ValueStart] != '{')
                continue;
            var close = MatchingBrace(json, property.ValueStart);
            return close < 0 ? null : (property.ValueStart, close);
        }

        return null;
    }

    /// <summary>One "key": value in the file. Depth 1 is the top level; Parent is the name of
    /// the block it sits in (null at the top level). ValueEnd is just past the value.</summary>
    public readonly record struct Property(string Key, int Depth, string? Parent, int Start, int ValueStart, int ValueEnd);

    /// <summary>Every property in the file, skipping strings and comments, so a key written
    /// in a comment or inside text is never taken for a setting.</summary>
    public static List<Property> Properties(string json)
    {
        var found = new List<Property>();
        var open = new Stack<string?>();
        string? pending = null;
        for (var i = 0; i < json.Length; i++)
        {
            var c = json[i];
            if (c == '/' && i + 1 < json.Length && json[i + 1] == '/')
            {
                while (i < json.Length && json[i] != '\n')
                    i++;
            }
            else if (c == '/' && i + 1 < json.Length && json[i + 1] == '*')
            {
                var stop = json.IndexOf("*/", i + 2, StringComparison.Ordinal);
                i = stop < 0 ? json.Length : stop + 1;
            }
            else if (c == '"')
            {
                var end = SkipString(json, i);
                var colon = NextSignificant(json, end + 1);
                if (open.Count > 0 && colon < json.Length && json[colon] == ':')
                {
                    var valueStart = NextSignificant(json, colon + 1);
                    found.Add(new Property(json[(i + 1)..end], open.Count, open.Peek(), i, valueStart, ValueEndAt(json, valueStart)));
                    pending = json[(i + 1)..end];
                    i = colon;
                }
                else
                {
                    pending = null;
                    i = end;
                }
            }
            else if (c is '{' or '[')
            {
                open.Push(pending);
                pending = null;
            }
            else if (c is '}' or ']')
            {
                if (open.Count > 0)
                    open.Pop();
                pending = null;
            }
            else if (c == ',')
                pending = null;
        }

        return found;
    }

    /// <summary>Takes one property out, with its comma, and the line too when nothing else is on it.</summary>
    public static string RemoveProperty(string json, Property property)
    {
        var end = property.ValueEnd;
        var after = end;
        while (after < json.Length && (json[after] == ' ' || json[after] == '\t'))
            after++;
        if (after < json.Length && json[after] == ',')
            end = after + 1;
        var text = json.Remove(property.Start, end - property.Start);
        var lineStart = text.LastIndexOf('\n', Math.Max(0, property.Start - 1)) + 1;
        var lineEnd = text.IndexOf('\n', property.Start);
        if (lineEnd < 0)
            lineEnd = text.Length - 1;
        if (lineStart <= property.Start && text[lineStart..Math.Min(text.Length, lineEnd + 1)].Trim().Length == 0)
            text = text.Remove(lineStart, Math.Min(text.Length, lineEnd + 1) - lineStart);
        return text;
    }

    private static int NextSignificant(string json, int from)
    {
        var i = from;
        while (i < json.Length)
        {
            if (char.IsWhiteSpace(json[i]))
                i++;
            else if (json[i] == '/' && i + 1 < json.Length && json[i + 1] == '/')
            {
                while (i < json.Length && json[i] != '\n')
                    i++;
            }
            else if (json[i] == '/' && i + 1 < json.Length && json[i + 1] == '*')
            {
                var stop = json.IndexOf("*/", i + 2, StringComparison.Ordinal);
                i = stop < 0 ? json.Length : stop + 2;
            }
            else
                break;
        }

        return i;
    }

    private static int ValueEndAt(string json, int start)
    {
        if (start >= json.Length)
            return start;
        var c = json[start];
        if (c == '"')
            return SkipString(json, start) + 1;
        if (c is '{' or '[')
        {
            var depth = 0;
            for (var i = start; i < json.Length; i++)
            {
                var d = json[i];
                if (d == '"')
                    i = SkipString(json, i);
                else if (d is '{' or '[')
                    depth++;
                else if (d is '}' or ']' && --depth == 0)
                    return i + 1;
            }

            return json.Length;
        }

        var end = start;
        while (end < json.Length && json[end] is not (',' or '}' or ']' or '\r' or '\n')
            && !(json[end] == '/' && end + 1 < json.Length && json[end + 1] is '/' or '*'))
            end++;
        while (end > start && char.IsWhiteSpace(json[end - 1]))
            end--;
        return end;
    }

    // The index of the last character of real JSON (not a comment or space) in from..end.
    private static int LastValueEnd(string json, int from, int end)
    {
        var last = -1;
        for (var i = from; i < end; i++)
        {
            var c = json[i];
            if (c == '"')
            {
                i = SkipString(json, i);
                last = i;
            }
            else if (c == '/' && i + 1 < end && json[i + 1] == '/')
            {
                while (i < end && json[i] != '\n')
                    i++;
            }
            else if (c == '/' && i + 1 < end && json[i + 1] == '*')
            {
                var stop = json.IndexOf("*/", i + 2, StringComparison.Ordinal);
                i = stop < 0 ? end : stop + 1;
            }
            else if (!char.IsWhiteSpace(c))
                last = i;
        }

        return last;
    }

    private static int SkipString(string json, int quote)
    {
        for (var i = quote + 1; i < json.Length; i++)
        {
            if (json[i] == '\\')
                i++;
            else if (json[i] == '"')
                return i;
        }

        return json.Length - 1;
    }

    private static int MatchingBrace(string json, int open)
    {
        var depth = 0;
        for (var i = open; i < json.Length; i++)
        {
            var c = json[i];
            if (c == '"')
                i = SkipString(json, i);
            else if (c == '/' && i + 1 < json.Length && json[i + 1] == '/')
            {
                while (i < json.Length && json[i] != '\n')
                    i++;
            }
            else if (c == '/' && i + 1 < json.Length && json[i + 1] == '*')
            {
                var stop = json.IndexOf("*/", i + 2, StringComparison.Ordinal);
                if (stop < 0)
                    return -1;
                i = stop + 1;
            }
            else if (c == '{')
                depth++;
            else if (c == '}' && --depth == 0)
                return i;
        }

        return -1;
    }

    public static string Insert(string json, string key, string raw)
    {
        var close = json.LastIndexOf('}');
        if (close < 0)
            throw new InvalidOperationException("Could not find " + key + " in the configuration file.");
        var head = json[..close].TrimEnd();
        var comma = head.Length > 0 && head[^1] != '{' && head[^1] != ',' ? "," : "";
        return head + comma + "\n  \"" + key + "\": " + raw + "\n" + json[close..];
    }
}
