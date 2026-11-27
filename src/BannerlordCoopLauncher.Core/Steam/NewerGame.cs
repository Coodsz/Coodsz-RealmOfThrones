using System.Text.RegularExpressions;
using BannerlordCoopLauncher.Core.IO;
using BannerlordCoopLauncher.Core.Parsing;

namespace BannerlordCoopLauncher.Core.Steam;

/// <summary>
/// The line every tool shows when this PC's Bannerlord is newer than 1.4.8, the game the tools and Coop 0.1.6 and 0.1.7
/// are made for (Steam moved many players to 1.5.7 on 2026-11-11). Only a warning: it blocks nothing by itself.
/// The Realm of Thrones tools run on a Coop nightly build, not 0.1.6 or 0.1.7, so their line names Realm of Thrones.
/// The Coop and Friends tools pass nightlyFor when the installed Coop is 0.1.8 or newer, the official build or the nightly
/// (CoopInstall.NightlyFor): no line when it was made for this game, and a line that never says to go back to 1.4.8 when
/// the game is newer than it too. With an older Coop they pass coopTools, so their line says which Coop this game needs
/// and where it comes from (Coop's GitHub release, not the Workshop), and the Server Tool also passes host.
/// </summary>
public static class NewerGame
{
    public static readonly Version Supported = new(1, 4, 8);

    /// <summary>The line for the game in this folder (its Modules\Native version), or null when that is 1.4.8 or older,
    /// or the folder or its version can't be read.</summary>
    public static string? Notice(string? gameRoot, bool realmOfThrones = false, string? nightlyFor = null, bool coopTools = false, bool host = false) =>
        NativeVersion(gameRoot) is { } version ? Line(version, realmOfThrones, nightlyFor, coopTools, host) : null;

    /// <summary>The game's Modules\Native version in this folder, or null when it can't be read.</summary>
    public static string? NativeVersion(string? gameRoot, IFileSystem? files = null)
    {
        if (string.IsNullOrWhiteSpace(gameRoot))
            return null;
        try
        {
            var xml = Path.Combine(gameRoot, "Modules", "Native", "SubModule.xml");
            files ??= new PhysicalFileSystem();
            return files.ReadAllText(xml) is { } contents && SubModuleXml.TryRead(contents, out var info, out _) ? info!.Version : null;
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

    /// <summary>True when both are versions with the same first three numbers ("v1.5.7" and "v1.5.7.106711").</summary>
    public static bool SameGame(string? version, string? other) =>
        Parse(version) is { } left && Parse(other) is { } right && left == right;

    /// <summary>The line for a Native version ("v1.5.7", "v1.5.7.12345"), or null when it is 1.4.8 or older or not a version.
    /// Only the first three numbers count, since the build number after them differs between PCs.</summary>
    /// <param name="nightlyFor">The game version the installed Coop of 0.1.8 or newer was made for, "" when that can't be
    /// read, or null when the installed Coop is older or none.</param>
    /// <param name="coopTools">The Coop and Friends tools: an older Coop (or none) gets the line naming Coop 0.1.8.</param>
    /// <param name="host">The Coop Server Tool: that line names Download Coop server for the 0.1.8 dedicated server.</param>
    public static string? Line(string? version, bool realmOfThrones = false, string? nightlyFor = null, bool coopTools = false, bool host = false)
    {
        var found = Parse(version);
        if (found == null || found <= Supported)
            return null;
        if (nightlyFor != null)
        {
            var madeFor = Parse(nightlyFor);
            if (madeFor != null && found <= madeFor)
                return null;
            return madeFor != null
                ? "Bannerlord " + found + " is newer than your Coop, made for Bannerlord " + madeFor + ". The host and every player need a Coop build made for Bannerlord " + found + "."
                : "This tool could not read which Bannerlord your Coop was made for. The host and every player need a Coop build made for Bannerlord " + found + ".";
        }

        // Coop 0.1.8 is the first Coop made for Bannerlord 1.5.7 (Id Coop from Coop's GitHub release, or the nightly). The
        // Workshop item still has 0.1.7 for 1.4.8, so the line never sends anyone there.
        if (coopTools)
            return host
                ? "Bannerlord " + found + " needs Coop 0.1.8 or newer. Your Coop is made for Bannerlord 1.4.8 or is missing. Coop 0.1.8 is not on the Steam Workshop. Click Download Coop server under Advanced: server folder on the Settings page for its dedicated server, unzip it to a short folder, then click Set server folder. Players unzip the Coop zip from Coop's GitHub release into the game's Modules folder as Coop. Or switch back: right click Bannerlord in Steam, then Properties, Betas, and pick v1.4.8."
                : "Bannerlord " + found + " needs Coop 0.1.8 or newer. Your Coop is made for Bannerlord 1.4.8 or is missing. Coop 0.1.8 is not on the Steam Workshop. Get the Coop zip from Coop's GitHub release and unzip it into the game's Modules folder, so it becomes Modules\\Coop. Or switch back: right click Bannerlord in Steam, then Properties, Betas, and pick v1.4.8.";
        return realmOfThrones
            ? "Bannerlord " + found + " isn't supported yet. Realm of Thrones in Coop runs on Bannerlord 1.4.8. To switch back, right click Bannerlord in Steam, then Properties, Betas, and pick v1.4.8."
            : "Bannerlord " + found + " isn't supported yet. The tools work on Bannerlord 1.4.8 with Coop 0.1.6 and 0.1.7. To switch back, right click Bannerlord in Steam, then Properties, Betas, and pick v1.4.8.";
    }

    /// <summary>Whether the Coop Client Launcher needs to direct players to the newer Coop release.</summary>
    public static bool NeedsGitHubCoop(string? version, string? nightlyFor) =>
        nightlyFor == null && Parse(version) is { } found && found > Supported;

    // "v1.5.7" or "v1.5.7.12345" as 1.5.7, or null when it is not a version.
    private static Version? Parse(string? version)
    {
        var match = Regex.Match((version ?? "").Trim(), @"^[vV]?(\d+)\.(\d+)\.(\d+)(?:\.\d+)*$");
        if (!match.Success || !int.TryParse(match.Groups[1].Value, out var major) || !int.TryParse(match.Groups[2].Value, out var minor)
            || !int.TryParse(match.Groups[3].Value, out var patch))
            return null;
        return new Version(major, minor, patch);
    }
}
