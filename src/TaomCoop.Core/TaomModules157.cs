namespace TaomCoop.Core;

/// <summary>
/// Checks the TAOM content versions shipped together on Bannerlord 1.5.x. An installed TAOM.CoopCompat
/// must match TAOM. Coop compares the actual module versions between the host and client at join.
/// </summary>
public static class TaomModules157
{
    public const string TaomModuleId = "TAOM";
    public const string UpdateTaom = "Update to TAOM 2.0.41 or newer for Bannerlord 1.5.7.";

    public static bool MeetsMinimum(string version) =>
        System.Version.TryParse(version.TrimStart('v', 'V'), out var parsed)
        && parsed >= new System.Version(2, 0, 41);

    public static readonly string[] ContentModules = ["TAOM", "TAOM_Map", "TAOM.Dependencies", "LOTRLOME_Armory"];

    // The public 2.0.42 package ships Armory 2.0.41. Coop still compares each module against the host.
    public static bool ContentVersionMatches(string id, string version, string taom) =>
        version.Equals(taom, StringComparison.OrdinalIgnoreCase)
        || (id == "LOTRLOME_Armory" && taom.Equals("v2.0.42", StringComparison.OrdinalIgnoreCase)
            && version.Equals("v2.0.41", StringComparison.OrdinalIgnoreCase));

    /// <summary>TAOM's version in Modules\TAOM, or "" when it is missing or can't be read now.</summary>
    public static string TaomVersion(string? gameRoot) =>
        string.IsNullOrWhiteSpace(gameRoot) ? "" : Version(gameRoot, TaomModuleId) ?? "";

    /// <summary>True when Modules\TAOM.CoopCompat is on this PC.</summary>
    public static bool HasCompat(string gameRoot) => File.Exists(Xml(gameRoot, TaomStack.CompatModuleId));

    /// <summary>Checks the content versions shipped together and, when requested, the installed Compat version.</summary>
    public static string? Problem(string gameRoot, bool checkCompat = true)
    {
        // Each file is read once, so a file that gets locked halfway can't change the answer.
        var versions = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var id in ContentModules)
        {
            if (!File.Exists(Xml(gameRoot, id)))
                return id + " is missing. Install TAOM again from your TAOM download.";
            if (Version(gameRoot, id) is not { } version)
                return Unreadable(id);
            versions[id] = version;
        }

        var taom = versions[TaomModuleId];
        if (!MeetsMinimum(taom))
            return UpdateTaom;
        foreach (var id in ContentModules)
        {
            if (!ContentVersionMatches(id, versions[id], taom))
                return id + " is " + Shown(versions[id]) + " but TAOM is " + Shown(taom) + ". Install the modules from the same TAOM download.";
        }

        if (!checkCompat || !HasCompat(gameRoot))
            return null;
        if (Version(gameRoot, TaomStack.CompatModuleId) is not { } compat)
            return Unreadable(TaomStack.CompatModuleId);
        if (System.Version.TryParse(compat.TrimStart('v', 'V'), out var installed)
            && System.Version.TryParse(taom.TrimStart('v', 'V'), out var required) && installed > required)
            return "TAOM.CoopCompat is " + Shown(compat) + " but TAOM is " + Shown(taom) + ". Update TAOM to match TAOM.CoopCompat.";
        if (!compat.Equals(taom, StringComparison.OrdinalIgnoreCase))
            return "TAOM.CoopCompat is " + Shown(compat) + " but TAOM is " + Shown(taom) + ". They need the same version. Click Install or repair TAOM.";
        return null;
    }

    private static string Unreadable(string id) =>
        id + " can't be read now. Close Bannerlord and programs that may have it open, like an antivirus scan, then try again.";

    private static string Xml(string gameRoot, string id) => Path.Combine(gameRoot, "Modules", id, "SubModule.xml");

    // The module's version, or null when its SubModule.xml is missing or can't be read now (locked or no access).
    private static string? Version(string gameRoot, string id)
    {
        var xml = Xml(gameRoot, id);
        return File.Exists(xml) ? ModuleIdentityReader.TryRead(xml)?.Version : null;
    }

    private static string Shown(string version) => version.Length == 0 ? "missing a version" : version;
}

/// <summary>What the Client Launcher's module list expects of a core module on each game line.</summary>
public static class TaomExpected
{
    /// <summary>The version a core module needs, or null when any version will do. On Bannerlord 1.5.x the TAOM modules
    /// and TAOM.CoopCompat need TAOM's own version (taomVersion, "" when TAOM is not on this PC) instead of the 1.4.8
    /// kit pins.</summary>
    public static string? Version(TaomLine line, string id, string taomVersion)
    {
        if (line == TaomLine.Line157 && (TaomModules157.ContentModules.Contains(id) || id == TaomStack.CompatModuleId))
            return taomVersion.Length == 0 ? null : taomVersion;
        if (TaomStack.ContentVersions.TryGetValue(id, out var version))
            return version;
        if (id == TaomStack.CoopModuleId)
            return line == TaomLine.Line157 ? "v0.1.8" : TaomStack.CoopVersion;
        if (id == TaomStack.CompatModuleId)
            return TaomStack.CompatVersion;
        return null;
    }

    /// <summary>True for a core module that may be missing: TAOM.CoopCompat on Bannerlord 1.5.x, which players get as a
    /// separate folder until TAOM ships it inside its own package.</summary>
    public static bool MayBeMissing(TaomLine line, string id) =>
        line == TaomLine.Line157 && id == TaomStack.CompatModuleId;
}

/// <summary>What Install or repair TAOM does. On 1.5.x it only checks, and never reaches the 0.3.25 zips.</summary>
public enum TaomInstallStep
{
    Check157,
    Install148,
    Repair148,
    GameUnreadable
}

public static class TaomInstallPlan
{
    public const string GameUnreadable = "Bannerlord's Native module can't be read now, so the tool can't tell which Bannerlord version this is. Close Bannerlord and programs that may have it open, like an antivirus scan, then try again.";

    /// <summary>needsInstall is the 1.4.8 check (TaomArchive.NeedsInstall); it is only asked on that line. When the game's
    /// version can't be read nothing runs, so a 1.5.x game never gets the 1.4.8 install by mistake.</summary>
    public static TaomInstallStep For(string gameRoot, Func<bool> needsInstall)
    {
        if (!TaomGameLine.TryDetect(gameRoot, out var line))
            return TaomInstallStep.GameUnreadable;
        if (line == TaomLine.Line157)
            return TaomInstallStep.Check157;
        return needsInstall() ? TaomInstallStep.Install148 : TaomInstallStep.Repair148;
    }
}
