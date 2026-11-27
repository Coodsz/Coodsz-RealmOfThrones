using System.Text.RegularExpressions;

namespace BannerlordCoopLauncher.Core.Modules;

/// <summary>
/// UIExtenderEx v3 ships a Compiled Prefabs submodule whose DLL needs Bannerlord.UIExtenderEx.CodeGenerator.dll
/// and Bannerlord.UIExtenderEx.Compiler.dll next to it. When one of them is gone (an incomplete Steam download,
/// or antivirus quarantine) the game says "UIExtenderEx Compiled Prefabs submodule could not be loaded
/// correctly due to a dependency conflict" and can crash. The client launchers then take that submodule out of
/// UIExtenderEx's SubModule.xml and set its CompiledPrefabs setting to false, right before the game starts, and
/// put the original back when the game closes, the same safe way as the TAOM dependency fix (see
/// SubModuleXmlPatch). A complete copy is never changed. Not done on dedicated servers.
/// </summary>
public static class UIExtenderExPrefabsFix
{
    public const string ModuleId = "Bannerlord.UIExtenderEx";
    public const string BackupName = "SubModule.xml.coodsz-uiex-original";
    public const string MarkName = "SubModule.xml.coodsz-uiex";
    public const string CompiledPrefabsDll = "Bannerlord.UIExtenderEx.CompiledPrefabs.dll";

    /// <summary>The DLLs the Compiled Prefabs DLL references (read from the v3.0.3 assembly). Add a new one here.</summary>
    public static readonly IReadOnlyList<string> CompiledPrefabsNeeds =
        ["Bannerlord.UIExtenderEx.CodeGenerator.dll", "Bannerlord.UIExtenderEx.Compiler.dll"];

    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(2);

    private static readonly Regex CompiledSubModule = new(
        @"^[ \t]*<SubModule>(?:(?!</SubModule>).)*?<DLLName\s+value\s*=\s*""Bannerlord\.UIExtenderEx\.CompiledPrefabs\.dll""(?:(?!</SubModule>).)*?</SubModule>[ \t]*(?:\r?\n)?",
        RegexOptions.Compiled | RegexOptions.Multiline | RegexOptions.Singleline | RegexOptions.IgnoreCase, Timeout);

    private static readonly Regex CompiledSetting = new(
        @"<Bool\s[^>]*?\bId\s*=\s*""CompiledPrefabs""[^>]*>",
        RegexOptions.Compiled | RegexOptions.IgnoreCase, Timeout);

    private static readonly Regex ValueAttribute = new(
        @"(?<=\s)Value\s*=\s*""(?<value>[^""]*)""",
        RegexOptions.Compiled | RegexOptions.IgnoreCase, Timeout);

    private static readonly Regex ModuleIdValue = new(
        @"<Id\s+value\s*=\s*""(?<id>[^""]*)""",
        RegexOptions.Compiled | RegexOptions.IgnoreCase, Timeout);

    /// <summary>True when UIExtenderEx is in this launch list (any case).</summary>
    public static bool InLaunch(IEnumerable<string> launchModules) =>
        launchModules.Any(id => id != null && id.Trim().Equals(ModuleId, StringComparison.OrdinalIgnoreCase));

    /// <summary>The DLLs this UIExtenderEx copy is missing for its Compiled Prefabs: none when the copy is
    /// complete, or when it has no Compiled Prefabs submodule or DLL at all.</summary>
    public static IReadOnlyList<string> MissingFiles(string moduleDirectory)
    {
        var xmlPath = Path.Combine(moduleDirectory, SubModuleXmlPatch.FileName);
        if (SubModuleXmlPatch.IsApplied(moduleDirectory, MarkName))
            xmlPath = Path.Combine(moduleDirectory, BackupName);
        if (!File.Exists(xmlPath) || !CompiledSubModule.IsMatch(SubModuleXmlPatch.ReadText(xmlPath, out _)))
            return [];
        var bin = Path.Combine(moduleDirectory, "bin", "Win64_Shipping_Client");
        if (!File.Exists(Path.Combine(bin, CompiledPrefabsDll)))
            return [];
        return CompiledPrefabsNeeds.Where(dll => !File.Exists(Path.Combine(bin, dll))).ToList();
    }

    /// <summary>What Check mods tells the player about this UIExtenderEx copy, or null when it is fine.</summary>
    public static string? HealthProblem(string? moduleDirectory)
    {
        if (string.IsNullOrWhiteSpace(moduleDirectory) || !Directory.Exists(moduleDirectory))
            return null;
        var missing = MissingFiles(moduleDirectory);
        if (missing.Count == 0)
            return null;
        // Whole sentences for one and for more files, so translations never get an English " is" or " are".
        return missing.Count == 1
            ? "UIExtenderEx is incomplete. " + Names(missing) + " is missing. Verify it in Steam (Library, Bannerlord, Properties, Installed Files, Verify) or check your antivirus quarantine, then Check mods again."
            : "UIExtenderEx is incomplete. " + Names(missing) + " are missing. Verify it in Steam (Library, Bannerlord, Properties, Installed Files, Verify) or check your antivirus quarantine, then Check mods again.";
    }

    /// <summary>Check mods lines for every UIExtenderEx copy under these roots.</summary>
    public static IReadOnlyList<string> HealthProblems(IEnumerable<string> roots) =>
        FindCopies(roots).Select(HealthProblem).Where(line => line != null).Select(line => line!).Distinct().ToList();

    /// <summary>The SubModule.xml without the Compiled Prefabs submodule and with CompiledPrefabs set to
    /// false, or null when it is already like that. Everything else stays as it is.</summary>
    public static string? Change(string xml)
    {
        var changed = CompiledSubModule.Replace(xml, "");
        changed = CompiledSetting.Replace(changed, match => TurnOff(match.Value));
        return changed == xml ? null : changed;
    }

    private static string TurnOff(string tag)
    {
        var value = ValueAttribute.Match(tag);
        if (value.Success)
            return value.Groups["value"].Value.Equals("false", StringComparison.OrdinalIgnoreCase)
                ? tag
                : tag[..value.Index] + "Value=\"false\"" + tag[(value.Index + value.Length)..];
        var selfClosing = tag.EndsWith("/>", StringComparison.Ordinal);
        var body = tag[..^(selfClosing ? 2 : 1)].TrimEnd();
        return body + " Value=\"false\"" + (selfClosing ? " />" : ">");
    }

    /// <summary>Every UIExtenderEx folder under these roots (Workshop content folders, the game's
    /// Modules folder).</summary>
    public static IReadOnlyList<string> FindCopies(IEnumerable<string> roots)
    {
        var copies = new List<string>();
        foreach (var root in roots.Where(root => !string.IsNullOrWhiteSpace(root)).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            string[] children;
            try
            {
                if (!Directory.Exists(root))
                    continue;
                children = Directory.GetDirectories(root);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                continue;
            }

            foreach (var child in children)
            {
                try
                {
                    var xmlPath = Path.Combine(child, SubModuleXmlPatch.FileName);
                    if (!File.Exists(xmlPath))
                        continue;
                    var id = ModuleIdValue.Match(SubModuleXmlPatch.ReadText(xmlPath, out _));
                    if (id.Success && id.Groups["id"].Value.Trim().Equals(ModuleId, StringComparison.OrdinalIgnoreCase))
                        copies.Add(child);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                }
            }
        }

        return copies;
    }

    /// <summary>Changes one UIExtenderEx folder. True when the file was changed.</summary>
    public static bool Apply(string moduleDirectory) =>
        SubModuleXmlPatch.Apply(moduleDirectory, BackupName, MarkName, Change, out _);

    public static bool Restore(string moduleDirectory) => SubModuleXmlPatch.Restore(moduleDirectory, BackupName, MarkName);

    public static IReadOnlyList<string> RestoreAll(IEnumerable<string> roots, List<string>? problems = null) =>
        SubModuleXmlPatch.RestoreAll(roots, BackupName, MarkName, problems);

    /// <summary>The activity log line when the fix is applied for these missing DLLs.</summary>
    public static string Line(IReadOnlyList<string> missing) => missing.Count == 1
        ? "UIExtenderEx is missing " + Names(missing) + ", so its Compiled Prefabs are off for this game. Verify UIExtenderEx in Steam or check your antivirus to get the file back."
        : "UIExtenderEx is missing " + Names(missing) + ", so its Compiled Prefabs are off for this game. Verify UIExtenderEx in Steam or check your antivirus to get the files back.";

    private static string Names(IReadOnlyList<string> names) =>
        names.Count <= 1
            ? string.Join("", names)
            : string.Join(", ", names.Take(names.Count - 1)) + " and " + names[^1];

    /// <summary>Right before a client starts the game: changes every UIExtenderEx copy that is missing a DLL
    /// its Compiled Prefabs need, when UIExtenderEx is in the launch list. Returns the lines for the activity
    /// log (none when nothing had to change).</summary>
    public static IReadOnlyList<string> ApplyForLaunch(IEnumerable<string> launchModules, IEnumerable<string> roots)
    {
        if (!InLaunch(launchModules))
            return [];
        var lines = new List<string>();
        foreach (var folder in FindCopies(roots))
        {
            try
            {
                var missing = MissingFiles(folder);
                if (missing.Count > 0 && Apply(folder))
                {
                    var line = Line(missing);
                    if (!lines.Contains(line))
                        lines.Add(line);
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                lines.Add("Could not turn off UIExtenderEx's Compiled Prefabs in " + folder + ". " + ex.Message);
            }
        }

        return lines;
    }
}
