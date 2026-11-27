// Ported from CoopKeep (https://github.com/Aderimo/bannerlord-coopkeep), Protocol/DsEventParser.cs,
// and changed for the Coodsz tools.
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

using System.Text;
using System.Text.Json;

namespace BannerlordCoopLauncher.Core.Server;

/// <summary>
/// Turns the <c>@DS@</c> lines in the server output into typed events.
/// </summary>
/// <remarks>
/// The parser never throws. Broken, cut off or unknown JSON is refused quietly: the
/// lines come from a live process, and a line cut in half must not crash the tool.
/// A line can come from stdout (<c>@DS@{"ev":...}</c>) or from the log file
/// (<c>21:24:25.353  @DS@{"ev":...}</c>), and the engine sometimes glues the event to
/// the end of another line, so the marker is looked for anywhere in the line and only
/// the first JSON value after it is read.
/// </remarks>
public static class DsEventParser
{
    /// <summary>The prefix of event lines.</summary>
    public const string Marker = "@DS@";

    /// <summary>Tries to read one output line.</summary>
    /// <returns>True when the line holds a <c>@DS@</c> event with valid JSON. Ordinary log lines give false.</returns>
    public static bool TryParse(string? line, out DsEvent? evt)
    {
        evt = null;
        if (string.IsNullOrEmpty(line))
            return false;

        var markerAt = line.IndexOf(Marker, StringComparison.Ordinal);
        if (markerAt < 0)
            return false;

        var json = line[(markerAt + Marker.Length)..].Trim();
        if (json.Length == 0 || json[0] != '{')
            return false;

        try
        {
            var reader = new Utf8JsonReader(Encoding.UTF8.GetBytes(json));
            using var doc = JsonDocument.ParseValue(ref reader);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
                return false;
            if (!root.TryGetProperty("ev", out var evProp) || evProp.ValueKind != JsonValueKind.String)
                return false;

            var name = evProp.GetString() ?? string.Empty;
            evt = name switch
            {
                "state" => ParseState(root),
                "players" => ParsePlayers(root),
                "commands" => ParseCommands(root),
                _ => new DsEvent.Unknown(name, root.GetRawText()),
            };
            return true;
        }
        catch (JsonException)
        {
            // A half written line or broken JSON. Ignored.
            return false;
        }
    }

    /// <summary>The phase in the newest state event of a block of log text, or null when it has none.</summary>
    public static DsEvent.State? LastState(string? text) => Last<DsEvent.State>(text, "\"ev\":\"state\"");

    /// <summary>The newest player list in a block of log text, or null when it has none.</summary>
    public static DsEvent.Players? LastPlayers(string? text) => Last<DsEvent.Players>(text, "\"ev\":\"players\"");

    private static T? Last<T>(string? text, string hint) where T : DsEvent
    {
        if (string.IsNullOrEmpty(text))
            return null;
        var lines = text.Replace("\r\n", "\n").Split('\n');
        for (var i = lines.Length - 1; i >= 0; i--)
        {
            if (!lines[i].Contains(hint, StringComparison.Ordinal))
                continue;
            if (TryParse(lines[i], out var evt) && evt is T found)
                return found;
        }

        return null;
    }

    private static DsEvent ParseState(JsonElement root)
    {
        var rawPhase = GetString(root, "phase") ?? string.Empty;
        return new DsEvent.State(
            Phase: ServerPhaseExtensions.ParsePhase(rawPhase),
            RawPhase: rawPhase,
            SaveName: GetString(root, "save"),
            PasswordRequired: GetBool(root, "pw"),
            Detail: GetString(root, "detail"));
    }

    private static DsEvent ParsePlayers(JsonElement root)
    {
        var players = new List<ConnectedPlayer>();
        if (root.TryGetProperty("list", out var list) && list.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in list.EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.Object)
                    continue;
                players.Add(new ConnectedPlayer(
                    Id: GetInt(item, "id") ?? -1,
                    Name: GetString(item, "name") ?? ConnectedPlayer.JoiningPlaceholder,
                    State: GetString(item, "state") ?? "unknown",
                    Address: GetString(item, "addr")));
            }
        }

        return new DsEvent.Players(players);
    }

    private static DsEvent ParseCommands(JsonElement root) =>
        new DsEvent.Commands(
            Builtin: GetStringArray(root, "builtin"),
            Game: GetStringArray(root, "game"));

    // Small helpers: a wrong type gives null instead of an exception.

    private static string? GetString(JsonElement obj, string name) =>
        obj.TryGetProperty(name, out var p) && p.ValueKind == JsonValueKind.String
            ? p.GetString()
            : null;

    private static bool GetBool(JsonElement obj, string name) =>
        obj.TryGetProperty(name, out var p) && p.ValueKind == JsonValueKind.True;

    private static int? GetInt(JsonElement obj, string name) =>
        obj.TryGetProperty(name, out var p) && p.ValueKind == JsonValueKind.Number && p.TryGetInt32(out var v)
            ? v
            : null;

    private static IReadOnlyList<string> GetStringArray(JsonElement obj, string name)
    {
        if (!obj.TryGetProperty(name, out var arr) || arr.ValueKind != JsonValueKind.Array)
            return Array.Empty<string>();

        var result = new List<string>(arr.GetArrayLength());
        foreach (var item in arr.EnumerateArray())
        {
            if (item.ValueKind == JsonValueKind.String && item.GetString() is { } s)
                result.Add(s);
        }

        return result;
    }
}
