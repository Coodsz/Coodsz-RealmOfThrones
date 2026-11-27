using System.Text;
using System.Text.RegularExpressions;

namespace TaomCoop.Core;

/// <summary>
/// The TAOM version a campaign save was made on. The save header (an int32 length, then JSON
/// {"List":{"Modules":..., "Module_TAOM":"v2.0.27.0", ...}}) lists each module's version. A
/// campaign made on another TAOM release stops the server during load (MBObjectManager.LoadXml
/// failed for Settlements), so the tool warns before Start.
/// </summary>
public static class SaveTaomVersion
{
    /// <summary>The TAOM content modules whose version has to match the installed TAOM.</summary>
    public static readonly IReadOnlyList<string> Checked = ["TAOM", "TAOM_Map", "LOTRLOME_Armory"];

    /// <summary>Module id to version (without the leading v), from the save header text.</summary>
    public static IReadOnlyDictionary<string, string> ModuleVersions(string? header)
    {
        var versions = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrEmpty(header))
            return versions;
        foreach (Match match in Regex.Matches(header, "\"Module_([A-Za-z0-9_.]+)\"\\s*:\\s*\"v?(\\d+(?:\\.\\d+)*)", RegexOptions.CultureInvariant))
            versions.TryAdd(match.Groups[1].Value, match.Groups[2].Value);
        return versions;
    }

    public static IReadOnlyDictionary<string, string> Read(string path)
    {
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            var buffer = new byte[(int)Math.Min(stream.Length, 65536)];
            var read = stream.Read(buffer, 0, buffer.Length);
            return ModuleVersions(Encoding.UTF8.GetString(buffer, 0, read));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return new Dictionary<string, string>();
        }
    }

    /// <summary>True when the first three parts differ (2.0.27 and 2.0.29 differ, 2.0.29.0 and 2.0.29.5 do not).</summary>
    public static bool Differs(string saveVersion, string installedVersion) =>
        !Three(saveVersion).Equals(Three(installedVersion), StringComparison.OrdinalIgnoreCase);

    private static string Three(string version) =>
        string.Join(".", version.Trim().TrimStart('v', 'V').Split('.').Take(3)
            .Select(part => int.TryParse(part, out var n) ? n.ToString(System.Globalization.CultureInfo.InvariantCulture) : part));

    /// <summary>The save's TAOM version when it differs from the installed TAOM, or null when it matches or the save does not say.</summary>
    public static string? MismatchedVersion(IReadOnlyDictionary<string, string> saveVersions, string installedVersion)
    {
        foreach (var id in Checked)
        {
            if (saveVersions.TryGetValue(id, out var version) && Differs(version, installedVersion))
                return Short(version);
        }

        return null;
    }

    /// <summary>The plain warning shown next to the save and at Start.</summary>
    public static string Warning(string saveVersion, string installedVersion) =>
        "This campaign was made on TAOM " + Short(saveVersion) + ". It will not load on TAOM " + Clean(installedVersion)
        + ". Start a new campaign in Bannerlord and pick that save.";

    private static string Clean(string version) => version.Trim().TrimStart('v', 'V');

    // 2.0.27.0 reads as 2.0.27, 2.0.29.5 stays as it is.
    private static string Short(string version)
    {
        var parts = Clean(version).Split('.').ToList();
        while (parts.Count > 3 && parts[^1] == "0")
            parts.RemoveAt(parts.Count - 1);
        return string.Join(".", parts);
    }
}
