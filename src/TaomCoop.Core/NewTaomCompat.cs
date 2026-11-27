using BannerlordCoopLauncher.Core.IO;

namespace TaomCoop.Core;

/// <summary>
/// The official TAOM.CoopCompat v2 that ships inside the full TAOM download for Bannerlord 1.5.7 and Coop 0.1.8.1
/// (v2.0.40 first). It has the same module id and folder name as the kit 0.3.25 these tools run, but its README warns
/// that tools putting the old 0.3.25 back break joins on 1.5.7 (player report CR-XXP67, TAOM #85). So the TAOM tools
/// never replace, delete or set it aside: they stop the step and say why. Found by its version (v1 or later, the kits
/// are all v0.x), by its TAOM.CoopCompat.Coop.dll that no kit has, or by the v2.0.40 TAOM.CoopCompat.dll hash.
/// </summary>
public static class NewTaomCompat
{
    public const string DllSha256 = "FC45CB1294C77AAD1557B1DFB98F0122B7CD4A8D2553514482EB34A6280AE4D8";
    public const long DllLength = 27136;

    private static readonly string[] Bins = ["Win64_Shipping_Client", "Win64_Shipping_Server"];

    /// <summary>
    /// The version of the new module in a TAOM.CoopCompat folder ("" when its SubModule.xml gives none), or null when
    /// the folder is missing or holds a kit TAOM.CoopCompat. hash is for tests (FileHash.TrySha256, "" when unreadable).
    /// </summary>
    public static string? Found(string? moduleFolder, Func<string, string>? hash = null) => Identify(moduleFolder, hash).Version;

    /// <summary>
    /// The plain line for a step that stops because the new module is there, or because the folder can't be read well
    /// enough to tell (CR-XXP67 review), or null when the step may write there.
    /// </summary>
    public static string? Problem(string? moduleFolder, Func<string, string>? hash = null) =>
        Identify(moduleFolder, hash) switch
        {
            { Version: { } version } => Text(version),
            { Unsure: true } => UnsureText(moduleFolder!),
            _ => null
        };

    // A SubModule.xml that is there but can't be read (locked, no access) does not stop the bin checks. With no answer
    // from it and no kit Bootstrap DLL, or a DLL of the v2.0.40 size whose hash can't be read, the folder is unsure.
    private static (string? Version, bool Unsure) Identify(string? moduleFolder, Func<string, string>? hash)
    {
        if (string.IsNullOrWhiteSpace(moduleFolder) || !Directory.Exists(moduleFolder))
            return (null, false);
        var version = "";
        var readable = true;
        try
        {
            var xml = Path.Combine(moduleFolder, "SubModule.xml");
            if (File.Exists(xml))
                version = ModuleIdentityReader.Read(xml).Version;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            readable = false;
        }

        if (Version.TryParse(version.TrimStart('v', 'V'), out var parsed) && parsed.Major >= 1)
            return (version, false);
        var kitMarker = false;
        var hashUnknown = false;
        foreach (var bin in Bins.Select(name => Path.Combine(moduleFolder, "bin", name)))
        {
            try
            {
                if (File.Exists(Path.Combine(bin, "TAOM.CoopCompat.Coop.dll")))
                    return (version, false);
                kitMarker |= File.Exists(Path.Combine(bin, "TAOM.CoopCompat.Bootstrap.dll"));
                // Only a file of the right size is hashed, so the 1.2 MB kit DLL is never read.
                var dll = Path.Combine(bin, "TAOM.CoopCompat.dll");
                if (!File.Exists(dll) || new FileInfo(dll).Length != DllLength)
                    continue;
                var found = (hash ?? FileHash.TrySha256)(dll);
                if (found.Equals(DllSha256, StringComparison.OrdinalIgnoreCase))
                    return (version, false);
                hashUnknown |= found.Length == 0;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                hashUnknown = true;
            }
        }

        return (null, (!readable && !kitMarker) || hashUnknown);
    }

    // One expression, so tools/ExtractUiText finds both whole texts the languages translate.
    public static string Text(string? version) =>
        string.IsNullOrEmpty(version)
            ? "This TAOM.CoopCompat is the one in the new TAOM download for Bannerlord 1.5.7. These TAOM tools run TAOM " + TaomStack.TaomVersionText
                + " on Bannerlord " + TaomStack.GameVersion.TrimStart('v') + " with Coop " + TaomStack.CoopVersion.TrimStart('v')
                + ", so they leave it alone. Use the TAOM download's own instructions for 1.5.7."
            : "This TAOM.CoopCompat " + version + " is the one in the new TAOM download for Bannerlord 1.5.7. These TAOM tools run TAOM " + TaomStack.TaomVersionText
                + " on Bannerlord " + TaomStack.GameVersion.TrimStart('v') + " with Coop " + TaomStack.CoopVersion.TrimStart('v')
                + ", so they leave it alone. Use the TAOM download's own instructions for 1.5.7.";

    public static string UnsureText(string moduleFolder) =>
        "These TAOM tools could not read the TAOM.CoopCompat in " + moduleFolder + ", so they can't tell which one it is and leave it alone. Close any program that may have it open, antivirus too, then try again.";
}
