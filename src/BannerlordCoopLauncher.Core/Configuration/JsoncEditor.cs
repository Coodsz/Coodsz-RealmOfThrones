// Ported from CoopKeep (https://github.com/Aderimo/bannerlord-coopkeep),
// Configuration/JsoncEditor.cs, translated from Turkish and changed for the Coodsz tools.
//
// Copyright (c) 2026 aderimo
//
// Permission is hereby granted, free of charge, to any person obtaining a copy
// of this software and associated documentation files (the "Software"), to deal
// in the Software without restriction, including without limitation the rights
// to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
// copies of the Software, and to permit persons to whom the Software is
// furnished to do so, subject to the following conditions:
//
// The above copyright notice and this permission notice shall be included in all
// copies or substantial portions of the Software.
//
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
// IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
// FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
// AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
// LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
// OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
// SOFTWARE.

using System.Globalization;
using System.Text;
using System.Text.Json;

namespace BannerlordCoopLauncher.Core.Configuration;

/// <summary>
/// Changes <c>server-config.json</c> and <c>mod-config.json</c> in place: only the value
/// of the key asked for changes, the rest of the file is left exactly as it was.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why no serializer?</b> These files belong to the Coop team and are heavily
/// commented, every setting explained line by line. Reading them into an object and
/// writing it back would lose the comments, the key order, the layout and, worst of all,
/// <b>keys this tool does not know</b>.
/// </para>
/// <para>
/// That last point matters: Coop gets new builds often and adds settings. Quietly dropping
/// a key we do not know would throw away a host's setting. Working on the text removes
/// that risk.
/// </para>
/// <para>
/// Keys match without regard to case, the way the Coop server's own reader matches them.
/// </para>
/// </remarks>
public sealed class JsoncEditor
{
    private string _text;

    private JsoncEditor(string text) => _text = text;

    /// <summary>The current text of the file.</summary>
    public string Text => _text;

    public static JsoncEditor Parse(string text) => new(text);

    public static JsoncEditor Load(string path) => new(File.ReadAllText(path));

    /// <summary>
    /// Reads the file as a JSON document, skipping comments and trailing commas the way
    /// the Coop server does. Only for <i>reading</i>; writes always go through the text.
    /// </summary>
    public JsonDocument ToJsonDocument() => JsonDocument.Parse(_text, new JsonDocumentOptions
    {
        CommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    });

    /// <summary>Is the key in the file? Keys inside comments do not count.</summary>
    public bool ContainsKey(string key) => FindValueSpan(key, out _, out _);

    public void SetString(string key, string value) => SetRawValue(key, JsonSerializer.Serialize(value));

    public void SetInt(string key, int value) => SetRawValue(key, value.ToString(CultureInfo.InvariantCulture));

    public void SetBool(string key, bool value) => SetRawValue(key, value ? "true" : "false");

    /// <summary>
    /// Replaces the value of a key with an already serialized JSON literal. A missing key
    /// is added at the end of the file, before the closing brace.
    /// </summary>
    public void SetRawValue(string key, string jsonLiteral)
    {
        if (FindValueSpan(key, out var start, out var length))
        {
            _text = _text[..start] + jsonLiteral + _text[(start + length)..];
            return;
        }

        AppendKey(key, jsonLiteral);
    }

    /// <summary>The value text of a key (the first one in the file), or <see langword="null"/>.</summary>
    public string? GetRawValue(string key) =>
        FindValueSpan(key, out var start, out var length) ? _text.Substring(start, length) : null;

    /// <summary>
    /// Changes a key inside a nested object: <c>{ "section": { "key": ... } }</c>.
    /// </summary>
    /// <remarks>
    /// The <c>mod-config.json</c> settings live in the <c>difficulty</c>, <c>modOptions</c>
    /// and <c>voice</c> objects. A key missing from its section is added before the
    /// section's closing brace; adding it to the root object would make a setting the
    /// server never reads.
    /// </remarks>
    public void SetRawValue(string section, string key, string jsonLiteral)
    {
        if (!FindValueSpan(section, 0, _text.Length, out var sectionStart, out var sectionLength, anyDepth: true) ||
            _text[sectionStart] != '{')
        {
            AppendKey(section, "{ \"" + key + "\": " + jsonLiteral + " }");
            return;
        }

        var sectionEnd = sectionStart + sectionLength;
        if (FindValueSpan(key, sectionStart + 1, sectionEnd - 1, out var start, out var length))
        {
            _text = _text[..start] + jsonLiteral + _text[(start + length)..];
            return;
        }

        InsertBefore(sectionStart, sectionEnd - 1, key, jsonLiteral);
    }

    /// <summary>The value text of a nested key, or <see langword="null"/>.</summary>
    public string? GetRawValue(string section, string key)
    {
        if (!FindValueSpan(section, 0, _text.Length, out var ss, out var sl, anyDepth: true) || _text[ss] != '{')
            return null;

        return FindValueSpan(key, ss + 1, ss + sl - 1, out var start, out var length)
            ? _text.Substring(start, length)
            : null;
    }

    /// <summary>
    /// Writes the file. A <c>.bak</c> copy is taken first: a lost config can stop the
    /// host's server from starting.
    /// </summary>
    public void Save(string path)
    {
        if (File.Exists(path))
            File.Copy(path, path + ".bak", overwrite: true);

        // Write to a temporary file, then move it over, so a cut off write never leaves half a config.
        var temp = path + ".tmp";
        File.WriteAllText(temp, _text, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        File.Move(temp, path, overwrite: true);
    }

    // ------------------------------------------------------------------
    // Text scanning
    // ------------------------------------------------------------------

    /// <summary>
    /// Finds the <c>"key"</c> : &lt;value&gt; pattern and returns where the <i>value</i> is
    /// in the text. Matches inside comments and inside string literals are skipped.
    /// </summary>
    private bool FindValueSpan(string key, out int valueStart, out int valueLength) =>
        FindValueSpan(key, 0, _text.Length, out valueStart, out valueLength, anyDepth: true);

    /// <summary>
    /// Limits the search to <c>[from, to)</c>. With <paramref name="anyDepth"/> off only the
    /// keys on the range's <b>own</b> level match; deeper objects are skipped.
    /// </summary>
    private bool FindValueSpan(string key, int from, int to, out int valueStart, out int valueLength,
        bool anyDepth = false)
    {
        valueStart = 0;
        valueLength = 0;

        var needle = "\"" + key + "\"";
        var i = from;
        var depth = 0;

        while (i < to)
        {
            // Skip comments
            if (IsAt(i, "//")) { i = SkipLineComment(i); continue; }
            if (IsAt(i, "/*")) { i = SkipBlockComment(i); continue; }

            if (_text[i] is '{' or '[') { depth++; i++; continue; }
            if (_text[i] is '}' or ']') { depth--; i++; continue; }

            if (_text[i] == '"')
            {
                var tokenStart = i;
                var tokenEnd = SkipString(i);

                if ((anyDepth || depth == 0) &&
                    tokenEnd - tokenStart == needle.Length &&
                    string.Compare(_text, tokenStart, needle, 0, needle.Length, StringComparison.OrdinalIgnoreCase) == 0)
                {
                    // A key candidate: a ':' has to follow (whitespace and comments in between are skipped)
                    var j = SkipTrivia(tokenEnd);
                    if (j < _text.Length && _text[j] == ':')
                    {
                        var vs = SkipTrivia(j + 1);
                        var ve = SkipValue(vs);
                        if (ve > vs)
                        {
                            valueStart = vs;
                            valueLength = ve - vs;
                            return true;
                        }
                    }
                }

                i = tokenEnd;
                continue;
            }

            i++;
        }

        return false;
    }

    private bool IsAt(int i, string s) =>
        i + s.Length <= _text.Length && string.CompareOrdinal(_text, i, s, 0, s.Length) == 0;

    private int SkipLineComment(int i)
    {
        while (i < _text.Length && _text[i] != '\n') i++;
        return i;
    }

    private int SkipBlockComment(int i)
    {
        i += 2;
        while (i + 1 < _text.Length && !(_text[i] == '*' && _text[i + 1] == '/')) i++;
        return Math.Min(i + 2, _text.Length);
    }

    /// <summary>Starts at the opening quote and returns the position just AFTER the closing quote.</summary>
    private int SkipString(int i)
    {
        i++; // opening quote
        while (i < _text.Length)
        {
            if (_text[i] == '\\') { i += 2; continue; }
            if (_text[i] == '"') return i + 1;
            i++;
        }
        return i;
    }

    /// <summary>Skips whitespace and comments.</summary>
    private int SkipTrivia(int i)
    {
        while (i < _text.Length)
        {
            if (char.IsWhiteSpace(_text[i])) { i++; continue; }
            if (IsAt(i, "//")) { i = SkipLineComment(i); continue; }
            if (IsAt(i, "/*")) { i = SkipBlockComment(i); continue; }
            break;
        }
        return i;
    }

    /// <summary>Finds the end of one JSON value (string, number, bool, null, object, array).</summary>
    private int SkipValue(int i)
    {
        if (i >= _text.Length) return i;

        return _text[i] switch
        {
            '"' => SkipString(i),
            '{' => SkipBalanced(i, '{', '}'),
            '[' => SkipBalanced(i, '[', ']'),
            _ => SkipScalar(i),
        };
    }

    private int SkipScalar(int i)
    {
        var start = i;
        while (i < _text.Length && _text[i] is not (',' or '}' or ']' or '\n' or '\r') && !IsAt(i, "//") && !IsAt(i, "/*"))
            i++;

        // Trailing whitespace is not part of the value
        while (i > start && char.IsWhiteSpace(_text[i - 1])) i--;
        return i;
    }

    private int SkipBalanced(int i, char open, char close)
    {
        var depth = 0;
        while (i < _text.Length)
        {
            var c = _text[i];
            if (c == '"') { i = SkipString(i); continue; }
            if (IsAt(i, "//")) { i = SkipLineComment(i); continue; }
            if (IsAt(i, "/*")) { i = SkipBlockComment(i); continue; }

            if (c == open) depth++;
            else if (c == close)
            {
                depth--;
                if (depth == 0) return i + 1;
            }
            i++;
        }
        return i;
    }

    /// <summary>
    /// Adds a missing key just before the closing brace of the root object.
    /// </summary>
    /// <remarks>
    /// A real case from a user's PC: a <c>server-config.json</c> from 30 August did not
    /// have the <c>"port"</c> key a newer build added. A key has to be added to such a file.
    /// </remarks>
    private void AppendKey(string key, string jsonLiteral)
    {
        var lastBrace = _text.LastIndexOf('}');
        if (lastBrace < 0)
            throw new InvalidOperationException("No JSON object was found. The file may be damaged.");

        // The new key goes after the last line (comments included) before the closing brace.
        var k = lastBrace - 1;
        while (k >= 0 && char.IsWhiteSpace(_text[k])) k--;

        // Is the last value before the closing brace followed by a comma? If not, one is added
        // right after that value, so a trailing // comment never swallows it.
        var last = LastSignificant(_text.IndexOf('{') + 1, lastBrace);
        var needsComma = last >= 0 && _text[last] != ',' && _text[last] != '{';
        var indent = DetectIndent();
        var newline = _text.Contains("\r\n") ? "\r\n" : "\n";

        var insertion = newline + indent + "\"" + key + "\": " + jsonLiteral + newline;

        _text = _text[..(k + 1)] + insertion + _text[lastBrace..];
        if (needsComma)
            _text = _text[..(last + 1)] + "," + _text[(last + 1)..];
    }

    /// <summary>
    /// Adds a key before the closing brace of an object (<paramref name="closeBrace"/>).
    /// </summary>
    private void InsertBefore(int openBrace, int closeBrace, string key, string jsonLiteral)
    {
        var k = closeBrace - 1;
        while (k > openBrace && char.IsWhiteSpace(_text[k])) k--;

        var last = LastSignificant(openBrace + 1, closeBrace);
        var needsComma = last >= 0 && _text[last] != ',';
        var newline = _text.Contains("\r\n") ? "\r\n" : "\n";

        // The section's own indent: the start of the closing brace's line plus one level.
        var lineStart = _text.LastIndexOf('\n', closeBrace) + 1;
        var closeIndent = _text[lineStart..closeBrace];
        if (closeIndent.Trim().Length > 0) closeIndent = "";
        var indent = closeIndent + (DetectIndent() is { Length: > 0 } unit && unit.Length <= 4 ? unit : "  ");

        var insertion = newline + indent + "\"" + key + "\": " + jsonLiteral + newline + closeIndent;

        _text = _text[..(k + 1)] + insertion + _text[closeBrace..];
        if (needsComma)
            _text = _text[..(last + 1)] + "," + _text[(last + 1)..];
    }

    /// <summary>
    /// The last character of real JSON in <c>[from, to)</c> (the end of a value, a comma or
    /// a brace), skipping whitespace and comments; -1 when there is none.
    /// </summary>
    private int LastSignificant(int from, int to)
    {
        var last = -1;
        var i = Math.Max(from, 0);
        while (i < to)
        {
            if (char.IsWhiteSpace(_text[i])) { i++; continue; }
            if (IsAt(i, "//")) { i = SkipLineComment(i); continue; }
            if (IsAt(i, "/*")) { i = SkipBlockComment(i); continue; }
            if (_text[i] == '"')
            {
                i = SkipString(i);
                last = i - 1;
                continue;
            }

            last = i;
            i++;
        }

        return last;
    }

    /// <summary>Copies the indent the file already uses.</summary>
    private string DetectIndent()
    {
        foreach (var line in _text.Split('\n'))
        {
            var trimmed = line.TrimStart();
            if (trimmed.StartsWith('"') && trimmed.Length < line.Length)
                return line[..(line.Length - trimmed.Length)].TrimEnd('\r');
        }
        return "  ";
    }
}
