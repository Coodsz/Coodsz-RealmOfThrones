using System.Text.RegularExpressions;

namespace BannerlordCoopLauncher.Core.Support;

/// <summary>
/// The text "[DedicatedServer] SERVING" line, the fallback for servers without the @DS@
/// state events. The start banner names the same text before the engine has even loaded:
/// "server is READY when you see:  [DedicatedServer] SERVING", or on its own indented line
/// under "The server is ready when the log shows:". Neither of those is the server serving.
/// </summary>
public static class ServingLine
{
    public const string Marker = "[DedicatedServer] SERVING";

    // An optional time stamp the console log puts in front ("01:10:04.544  " or
    // "[01:10:04.544] "), then the indent the banner adds before the marker.
    private static readonly Regex Line = new(
        @"^(?:\[?\d{1,2}:\d{2}:\d{2}(?:\.\d+)?\]?[ \t]{1,2})?(?<indent>[ \t]*)\[DedicatedServer\] SERVING",
        RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));

    /// <summary>True for the real SERVING line, false for the start banner and other lines.</summary>
    public static bool IsServing(string? line)
    {
        if (string.IsNullOrEmpty(line) || !line.Contains(Marker, StringComparison.Ordinal))
            return false;
        if (line.Contains("when you see", StringComparison.OrdinalIgnoreCase))
            return false;
        // Indented under the banner. The marker glued to the end of another line still counts.
        var match = Line.Match(line);
        return !match.Success || match.Groups["indent"].Length == 0;
    }
}
