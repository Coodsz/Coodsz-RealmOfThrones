using System.Text.RegularExpressions;
using BannerlordCoopLauncher.Core.Parsing;

namespace TaomCoop.Core;

/// <summary>Which TAOM stack a PC is on. Legacy148 is the sealed kit path: TAOM 2.0.29.5 on Bannerlord 1.4.8 with Coop 0.1.6
/// and TAOM.CoopCompat 0.3.25. Line157 is TAOM 2.0.40 and later on Bannerlord 1.5.x, with Haterade's TAOM.CoopCompat of the
/// same version as TAOM and Coop v0.1.8 (tools guide section 7).</summary>
public enum TaomLine
{
    Legacy148,
    Line157
}

/// <summary>
/// Picks the TAOM stack by the game's Modules\Native version, so one tool serves both game lines while players move
/// to 1.5.7. For the read only checks anything that can't be read counts as the 1.4.8 path, which is how the tools
/// behaved before. A step that writes asks TryDetect and stops when the line is not known for sure.
/// </summary>
public static class TaomGameLine
{
    public static readonly Version FirstLine157 = new(1, 5, 0);

    public static TaomLine Detect(string? gameRoot) => TryDetect(gameRoot, out var line) ? line : TaomLine.Legacy148;

    /// <summary>False when Native's SubModule.xml is there but doesn't give the game version (locked, no access, not a module
    /// file, or a version that is missing or can't be read as numbers), so a step that writes can stop instead of guessing
    /// the line. A folder with no Native is the 1.4.8 path, as before.</summary>
    public static bool TryDetect(string? gameRoot, out TaomLine line)
    {
        line = TaomLine.Legacy148;
        if (string.IsNullOrWhiteSpace(gameRoot))
            return true;
        try
        {
            var xml = Path.Combine(gameRoot, "Modules", "Native", "SubModule.xml");
            if (!File.Exists(xml))
                return true;
            if (!SubModuleXml.TryRead(File.ReadAllText(xml), out var info, out _))
                return false;
            // A 1.5.7 game whose Native version is missing or odd must not count as 1.4.8: the 1.4.8 install would
            // put the 2.0.29.5 kit and TAOM.CoopCompat 0.3.25 over the TAOM that is there.
            return TryFromVersion(info!.Version, out line);
        }
        catch (IOException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
    }

    /// <summary>The line for a Native version ("v1.5.7", "v1.5.7.12345"). Only the first three numbers count, the same way
    /// NewerGame reads them. A version that can't be read counts as the 1.4.8 path here.</summary>
    public static TaomLine FromVersion(string? version) => TryFromVersion(version, out var line) ? line : TaomLine.Legacy148;

    /// <summary>False when the version is missing or is not three or more numbers, with the line left at the 1.4.8 path.</summary>
    public static bool TryFromVersion(string? version, out TaomLine line)
    {
        line = TaomLine.Legacy148;
        var match = Regex.Match((version ?? "").Trim(), @"^[vV]?(\d+)\.(\d+)\.(\d+)(?:\.\d+)*$");
        if (!match.Success || !int.TryParse(match.Groups[1].Value, out var major) || !int.TryParse(match.Groups[2].Value, out var minor)
            || !int.TryParse(match.Groups[3].Value, out var patch))
            return false;
        line = new Version(major, minor, patch) >= FirstLine157 ? TaomLine.Line157 : TaomLine.Legacy148;
        return true;
    }
}
