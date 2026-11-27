using System.Text.RegularExpressions;
using BannerlordCoopLauncher.Core.Server;
using BannerlordCoopLauncher.Core.Support;

namespace TaomCoop.Core;

/// <summary>One @DS@{"ev":"state",...} line the TAOM server writes: boot, loading, serving, stopping or fatal.</summary>
public sealed record ServerState(string Phase, string Save, string Detail)
{
    public bool Fatal => Phase.Equals("fatal", StringComparison.OrdinalIgnoreCase);

    public bool Serving => Phase.Equals("serving", StringComparison.OrdinalIgnoreCase);
}

/// <summary>
/// Reads the server's own state events, so the status follows what the server says. The log
/// banner "server is READY when you see [DedicatedServer] SERVING" is not the server serving.
/// The JSON is read by <see cref="DsEventParser"/>, the one @DS@ parser the tools share.
/// </summary>
public static class ServerStateEvent
{
    private const string StateHint = "\"ev\":\"state\"";
    private const string ServingMarker = ServingLine.Marker;

    /// <summary>The newest state event in the text, or null when it has none.</summary>
    public static ServerState? Last(string? text)
    {
        if (string.IsNullOrEmpty(text))
            return null;
        var end = text.Length;
        while (end > 0)
        {
            var at = text.LastIndexOf(StateHint, end - 1, StringComparison.Ordinal);
            if (at < 0)
                return null;
            var lineStart = text.LastIndexOf('\n', at) + 1;
            var lineEnd = text.IndexOf('\n', at);
            var state = FromLine(text[lineStart..(lineEnd < 0 ? text.Length : lineEnd)]);
            if (state != null)
                return state;
            end = lineStart > 0 ? lineStart - 1 : 0;
        }

        return null;
    }

    /// <summary>The state event in one JSON value (the text after @DS@), or null.</summary>
    public static ServerState? Parse(string json) => FromLine(DsEventParser.Marker + json);

    private static ServerState? FromLine(string line) =>
        DsEventParser.TryParse(line, out var evt) && evt is DsEvent.State { RawPhase.Length: > 0 } state
            ? new ServerState(state.RawPhase, state.SaveName ?? "", state.Detail ?? "")
            : null;

    /// <summary>
    /// True for the server's real SERVING line or its serving state event, not the start
    /// banner that only names SERVING.
    /// </summary>
    public static bool IsServingLine(string? line)
    {
        if (string.IsNullOrEmpty(line))
            return false;
        if (line.Contains(ServingMarker, StringComparison.Ordinal))
            return ServingLine.IsServing(line);
        return line.Contains(DsEventParser.Marker, StringComparison.Ordinal)
            && DsEventParser.TryParse(line, out var evt)
            && evt is DsEvent.State { Phase: ServerPhase.Serving };
    }

    /// <summary>True when the text has a real SERVING line.</summary>
    public static bool HasServingLine(string? text)
    {
        if (string.IsNullOrEmpty(text))
            return false;
        var at = text.IndexOf(ServingMarker, StringComparison.Ordinal);
        while (at >= 0)
        {
            var start = text.LastIndexOf('\n', at) + 1;
            var end = text.IndexOf('\n', at);
            if (IsServingLine(text[start..(end < 0 ? text.Length : end)]))
                return true;
            at = text.IndexOf(ServingMarker, at + ServingMarker.Length, StringComparison.Ordinal);
        }

        return false;
    }

    /// <summary>True when a fatal detail says the save was made on another TAOM release.</summary>
    public static bool OlderTaomSave(string detail) =>
        detail.Contains("use a save created with this TAOM release", StringComparison.OrdinalIgnoreCase)
        || Regex.IsMatch(detail, @"LoadXml failed for (Settlements|Kingdoms|Clans|Heroes)", RegexOptions.IgnoreCase);

    /// <summary>The plain status for a fatal state.</summary>
    public static string FatalStatus(ServerState state) =>
        (OlderTaomSave(state.Detail), state.Save.Length > 0) switch
        {
            (true, true) => "The server could not load the save " + state.Save + ". The save was made with an older TAOM version. Replace it with a campaign started on TAOM " + TaomStack.TaomVersionText + ".",
            (true, false) => "The server could not load the campaign. The save was made with an older TAOM version. Replace it with a campaign started on TAOM " + TaomStack.TaomVersionText + ".",
            (false, true) => "The server could not load the save " + state.Save + " and stopped. " + ShortDetail(state.Detail),
            _ => "The server could not load the campaign and stopped. " + ShortDetail(state.Detail)
        };

    /// <summary>The detail up to the original error, trimmed to a readable length.</summary>
    public static string ShortDetail(string detail)
    {
        var text = detail.Trim();
        var original = text.IndexOf(" Original error", StringComparison.OrdinalIgnoreCase);
        if (original > 0)
            text = text[..original];
        return text.Length <= 220 ? text : text[..217] + "...";
    }
}
