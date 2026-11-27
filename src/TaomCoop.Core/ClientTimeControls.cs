using System.Text.RegularExpressions;

namespace TaomCoop.Core;

/// <summary>
/// Players' Pause, Play and Fast forward on a Coop dedicated server (allowClientTimeControls in server-config.json).
/// The start script reads a file without the key as off, while the tools and Coop's template for a new file have it
/// on, so an older file left players' time buttons doing nothing and fast forward capped at 1x (Coop #55, Burning
/// Empires #11 and #12). The Coodsz Server Tool and the Burning Empires Server Tool add it on every start.
/// </summary>
public static class ClientTimeControls
{
    /// <summary>The start script's match for the setting, copied as it is there (a test keeps the two equal).</summary>
    public const string ScriptPattern = "(?m)^\\s*\"allowClientTimeControls\"\\s*:\\s*(?<value>true|false)\\s*,?";

    private static readonly Regex ScriptKey = new(ScriptPattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    // How the Coodsz Server Tool's Settings page reads the value: the first plain match, lowercase only.
    private static readonly Regex PageValue = new("\"allowClientTimeControls\"\\s*:\\s*(?<v>-?\\d+(?:\\.\\d+)?|true|false)");

    /// <summary>
    /// The text with the key added as on, or null when the file has it. "Has it" is the script's own match, so the
    /// two never disagree (a hand typed False stays off, a key only in a comment is no key). The line goes right
    /// after the opening brace, which needs no comma fix up after the last value; the file allows a trailing comma.
    /// </summary>
    public static string? WithKey(string serverConfig)
    {
        if (ScriptKey.IsMatch(serverConfig))
            return null;
        // An off the script's line match misses (a one line file) is still an off, read the way the Settings
        // page reads it, so the box shows it unticked and the script reads off too.
        var page = PageValue.Match(serverConfig);
        if (page.Success && page.Groups["v"].Value == "false")
            return null;
        var open = ObjectOpen(serverConfig);
        if (open < 0)
            return null;
        var newline = serverConfig.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
        var rest = serverConfig[(open + 1)..];
        return serverConfig[..(open + 1)] + newline + "  \"allowClientTimeControls\": true,"
            + (rest.StartsWith('\n') || rest.StartsWith("\r\n", StringComparison.Ordinal) ? "" : newline) + rest;
    }

    // The { that opens the file's object: the first one outside a comment, or -1. A stray character before
    // it does not stop the server (the start script reads the file with regular expressions), so not here either.
    private static int ObjectOpen(string text)
    {
        for (var i = 0; i < text.Length; i++)
        {
            if (text[i] == '{')
                return i;
            if (text[i] == '/' && i + 1 < text.Length && text[i + 1] == '/')
            {
                var end = text.IndexOf('\n', i);
                if (end < 0)
                    return -1;
                i = end;
            }
            else if (text[i] == '/' && i + 1 < text.Length && text[i + 1] == '*')
            {
                var end = text.IndexOf("*/", i + 2, StringComparison.Ordinal);
                if (end < 0)
                    return -1;
                i = end + 1;
            }
        }

        return -1;
    }
}
