using System.Text.RegularExpressions;

namespace BannerlordCoopLauncher.Core.Modules;

public enum DependencyFixState
{
    /// <summary>The mod does not ask for a library TAOM Dependencies ships.</summary>
    NotNeeded,
    /// <summary>It asks for one, and its SubModule.xml is not changed yet.</summary>
    Needed,
    /// <summary>Its SubModule.xml is changed by the TAOM dependency fix.</summary>
    Applied
}

/// <summary>
/// The TAOM dependency fix. TAOM.Dependencies ships Harmony, ButterLib, UIExtenderEx and MCM itself,
/// so the separate Bannerlord.Harmony, Bannerlord.ButterLib, Bannerlord.UIExtenderEx and
/// Bannerlord.MBOptionScreen modules are not loaded with TAOM. A mod that names them in DependedModules
/// or DependedModuleMetadatas cannot load ("dependency conflict") until those lines are taken out,
/// which is what the TAOM team tells players to do by hand.
///
/// Why in place, and only while TAOM runs: the game loads a mod by its id, from the Modules folder and
/// from every subscribed Workshop item. A second, patched copy under the same id would make the game
/// load two copies (see WorkshopDuplicates), and the Workshop copy cannot be turned off without
/// breaking Steam and the other tools. So the TAOM client launcher changes the mod's own SubModule.xml
/// right before the game starts and puts the original back when the game closes. The original is kept
/// next to it as SubModule.xml.coodsz-original, and SubModule.xml.coodsz-taom holds the SHA256 of the
/// changed file: a SubModule.xml with another hash was replaced by a Workshop update, so it becomes the
/// new original and is changed again, and a restore never writes over an update. Every leftover (for
/// example after the launcher was closed while the game ran) is put back the next time the TAOM client
/// launcher starts, and Coodsz Coop and Friends puts it back before it starts the game. A dedicated
/// server copy (the Coodsz TAOM server) is the server's own copy, so it is just changed there.
/// </summary>
public static class TaomDependencyFix
{
    public static readonly IReadOnlyList<string> Libraries =
        ["Bannerlord.Harmony", "Bannerlord.ButterLib", "Bannerlord.UIExtenderEx", "Bannerlord.MBOptionScreen"];

    public const string BackupName = "SubModule.xml.coodsz-original";
    public const string MarkName = "SubModule.xml.coodsz-taom";

    private static readonly Regex Entry = new(
        @"^[ \t]*<(?<tag>DependedModule|DependedModuleMetadata)\s[^>]*?\b[Ii]d\s*=\s*""(?<id>Bannerlord\.(?:Harmony|ButterLib|UIExtenderEx|MBOptionScreen))""[^>]*?(?:/>|>\s*</\k<tag>>)[ \t]*(?:\r?\n)?"
        + @"|<(?<tag2>DependedModule|DependedModuleMetadata)\s[^>]*?\b[Ii]d\s*=\s*""(?<id2>Bannerlord\.(?:Harmony|ButterLib|UIExtenderEx|MBOptionScreen))""[^>]*?(?:/>|>\s*</\k<tag2>>)",
        RegexOptions.Compiled | RegexOptions.Multiline | RegexOptions.IgnoreCase, TimeSpan.FromSeconds(2));

    /// <summary>The TAOM Dependencies libraries this SubModule.xml asks for.</summary>
    public static IReadOnlyList<string> NamedLibraries(string xml) =>
        Entry.Matches(xml)
            .Select(match => match.Groups["id"].Success && match.Groups["id"].Value.Length > 0 ? match.Groups["id"].Value : match.Groups["id2"].Value)
            .Select(id => Libraries.First(library => library.Equals(id, StringComparison.OrdinalIgnoreCase)))
            .Distinct()
            .ToList();

    /// <summary>The SubModule.xml without those entries, or null when it names none of them.
    /// Everything else in the file stays as it is.</summary>
    public static string? Strip(string xml) => Entry.IsMatch(xml) ? Entry.Replace(xml, "") : null;

    public static DependencyFixState State(string moduleDirectory)
    {
        var xmlPath = Path.Combine(moduleDirectory, SubModuleXmlPatch.FileName);
        if (!File.Exists(xmlPath))
            return DependencyFixState.NotNeeded;
        if (SubModuleXmlPatch.IsApplied(moduleDirectory, MarkName))
            return DependencyFixState.Applied;
        return Strip(SubModuleXmlPatch.ReadText(xmlPath, out _)) == null ? DependencyFixState.NotNeeded : DependencyFixState.Needed;
    }

    /// <summary>Applies the fix to one mod folder. Returns a plain line when the file was changed, or
    /// null when nothing had to change (no such entries, or already changed).</summary>
    public static string? Apply(string moduleDirectory, string label) =>
        SubModuleXmlPatch.Apply(moduleDirectory, BackupName, MarkName, Strip, out var text)
            ? "TAOM dependency fix applied to " + label + ". It no longer asks for " + string.Join(", ", NamedLibraries(text!)) + ", which TAOM Dependencies already has."
            : null;

    /// <summary>Puts the original SubModule.xml back. True when it was put back; a file a Workshop
    /// update replaced is left as it is.</summary>
    public static bool Restore(string moduleDirectory) => SubModuleXmlPatch.Restore(moduleDirectory, BackupName, MarkName);

    /// <summary>Puts back every changed SubModule.xml under these roots (Workshop content folders, the
    /// game's Modules folder). Returns the mod folders that were put back.</summary>
    public static IReadOnlyList<string> RestoreAll(IEnumerable<string> roots, List<string>? problems = null) =>
        SubModuleXmlPatch.RestoreAll(roots, BackupName, MarkName, problems);
}
