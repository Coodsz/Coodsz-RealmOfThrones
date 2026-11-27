// Ported from CoopKeep (https://github.com/Aderimo/bannerlord-coopkeep), Protocol/KnownLogLines.cs,
// and changed for the Coodsz tools.

using System.Text.RegularExpressions;

namespace BannerlordCoopLauncher.Core.Server;

/// <summary>How much a console line matters.</summary>
public enum LogSeverity { Harmless, Info, Warning, Error }

/// <summary>A console line this tool knows and what it means.</summary>
/// <param name="Key">A short name for the line, such as saved or portInUse.</param>
public sealed record KnownLogLine(string Key, LogSeverity Severity, Regex Pattern);

/// <summary>
/// Lines that often show up in the server console, taken from real Coop 0.1.6 sessions.
/// Only the match and its weight are kept here; what to tell the person hosting is
/// decided by the window that shows the line.
/// </summary>
public static class KnownLogLines
{
    private const RegexOptions Options = RegexOptions.Compiled | RegexOptions.CultureInvariant;

    public static readonly IReadOnlyList<KnownLogLine> All = new[]
    {
        new KnownLogLine("dllNoise", LogSeverity.Harmless, new Regex(@"Cannot load: .*\.dll|Could not load file or assembly", Options)),
        new KnownLogLine("eventIndex", LogSeverity.Harmless, new Regex(@"Could not find the event index for", Options)),
        new KnownLogLine("eula", LogSeverity.Error, new Regex(@"EULA acceptance required", Options)),
        // Exit codes 9 and 10: a save from the other War Sails mode, or "warSails" and NavalDLC that don't agree.
        new KnownLogLine("warSails", LogSeverity.Error, new Regex(@"\[DedicatedServer\] FATAL: .*(NavalDLC|""warSails""|War Sails)", Options)),
        // Exit code 11.
        new KnownLogLine("saveDidNotLoad", LogSeverity.Error, new Regex(@"\[DedicatedServer\] FATAL: save '[^'\r\n]*' did not start loading", Options)),
        // Exit codes 7 and 8 from Coop's own server launcher (BannerlordCoopServer.exe).
        new KnownLogLine("devModuleRestage", LogSeverity.Error, new Regex(@"dev module re-stage failed", Options | RegexOptions.IgnoreCase)),
        new KnownLogLine("coopServerFatal", LogSeverity.Error, new Regex(@"\[BannerlordCoopServer\] FATAL: ", Options)),
        new KnownLogLine("moduleMismatch", LogSeverity.Warning, new Regex(@"module mismatch: .*Forcing load", Options)),
        new KnownLogLine("unsupportedModule", LogSeverity.Error, new Regex(@"(Server does not support module|DLC is not supported)", Options)),
        new KnownLogLine("wrongVersion", LogSeverity.Error, new Regex(@"(Wrong game version|Incompatible co-op mod build|incompatible version of the mod)", Options)),
        new KnownLogLine("cheatDisabled", LogSeverity.Info, new Regex(@"Cheat mode is disabled", Options)),
        new KnownLogLine("portInUse", LogSeverity.Error, new Regex(@"(address already in use|Only one usage of each socket address|failed to bind)", Options)),
        new KnownLogLine("moduleVerify", LogSeverity.Error, new Regex(@"(release pins|verification failed).*(mismatch|fail)", Options)),
        new KnownLogLine("saved", LogSeverity.Info, new Regex(@"world saved to '", Options)),
        new KnownLogLine("steamOff", LogSeverity.Info, new Regex(@"Steam discoverability disabled", Options)),
        new KnownLogLine("steamLobby", LogSeverity.Info, new Regex(@"Steam lobby .* created", Options)),
        new KnownLogLine("serving", LogSeverity.Info, new Regex(@"\] SERVING", Options)),
        new KnownLogLine("accessViolation", LogSeverity.Error, new Regex(@"exited unexpectedly \(code -1073741819\)", Options)),
        new KnownLogLine("loadFailed", LogSeverity.Error, new Regex(@"(load failure|failed to load|LoadGame failed)", Options)),
    };

    // "[DedicatedServer] autosave: world saved to 'en bastan.sav' (1142 ms)" and the
    // shutdown and operator saves written the same way.
    private static readonly Regex SavedWorld = new(@"world saved to '(?<file>[^'\r\n]+?)(?:\.sav)?'", Options);

    /// <summary>The first known line that matches, or null.</summary>
    public static KnownLogLine? Match(string? line)
    {
        if (string.IsNullOrEmpty(line))
            return null;
        foreach (var known in All)
        {
            if (known.Pattern.IsMatch(line))
                return known;
        }

        return null;
    }

    /// <summary>Is the line an error or a warning?</summary>
    public static bool IsProblem(string? line)
    {
        if (string.IsNullOrEmpty(line))
            return false;
        var known = Match(line);
        if (known != null)
            return known.Severity is LogSeverity.Error or LogSeverity.Warning;
        return line.Contains("ERROR", StringComparison.Ordinal)
               || line.Contains("Exception", StringComparison.Ordinal)
               || line.StartsWith("! ", StringComparison.Ordinal);
    }

    /// <summary>
    /// The save name in a "world saved to 'name.sav'" line, without .sav, or null when the
    /// line is not one. Only a plain file name is returned, never a path.
    /// </summary>
    public static string? SavedWorldName(string? line)
    {
        if (string.IsNullOrEmpty(line))
            return null;
        var match = SavedWorld.Match(line);
        if (!match.Success)
            return null;
        var name = match.Groups["file"].Value.Trim();
        return name.Length == 0 || name.IndexOfAny(new[] { '/', '\\', ':' }) >= 0 ? null : name;
    }
}
