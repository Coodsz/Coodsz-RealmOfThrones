using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace BannerlordCoopLauncher.Core.Support;

/// <summary>
/// One Coodsz module DLL found on this PC: where (a folder label and the path inside it, no user path), its file version
/// and SHA256. Checked false: the DLL could not be read or is too large to hash, and Sha256 says which.
/// </summary>
public sealed record ModuleFingerprint(string Where, string Version, string Sha256, bool OurBuild, bool Checked = true);

/// <summary>
/// coodsz-modules.txt in a report: every Coodsz module DLL in the game's Modules, the Workshop items next to
/// the game and the server folders the tool runs, with its version and SHA256, marked our build when the hash
/// is one this tool knows and not in this tool's list when it is not: a changed copy, or a Coodsz build newer
/// than the tool, which is why the header points to the list on master. The known hashes are made at build time from the module
/// DLLs in the repository (the Core csproj, after a release signed them) plus Support\KnownModuleHashes.txt,
/// which keeps the hashes of earlier builds, so nobody keeps the list by hand. Report text only, no window.
/// </summary>
public static class ModuleFingerprints
{
    public const string FileName = "coodsz-modules.txt";
    public const string OurBuildText = "our build";
    public const string ChangedText = "not in this tool's list";
    public const string NotCheckedText = "not checked";
    public const string CouldNotReadText = "could not read";
    public const string TooLargeText = "over 64 MB, not hashed";
    public const string OddVersionText = "odd version";

    /// <summary>A DLL larger than this is listed without its hash; no module DLL comes near it.</summary>
    public const long MaxDllBytes = 64L * 1024 * 1024;

    /// <summary>The longest version a row keeps.</summary>
    public const int MaxVersionChars = 40;

    /// <summary>A report lists at most this many DLLs, so a folder full of copies can't fill it.</summary>
    public const int MaxDlls = 200;

    private static readonly string[] Platforms = ["Win64_Shipping_Client", "Win64_Shipping_Server"];

    /// <summary>
    /// The DLL names of the Coodsz modules and the modules the tools ship, also the ones only published on the
    /// Workshop (Diplomacy, Limitless, Stuck Battle Fix), whose builds are not kept in the repository.
    /// </summary>
    public static IReadOnlySet<string> DllNames { get; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "CoodszJoinPassword.dll", "CoodszMenuGuard.dll", "CoodszSettingsSync.Coop.dll", "DedicatedServer.CoodszTools.dll",
        "DedicatedServer.IGCoopCompat.dll", "CoodszImageExporter.dll", "CoopSaveRescue.dll", "CoodszWarSync.dll",
        "BurningEmpires.CoopCompat.dll", "Diplomacy.CoopCompat.dll", "Limitless.CoopCompat.dll", "StuckBattleFix.dll",
        "RealmOfThrones.CoopCompat.dll", "Coodsz.CoopLink.dll", "AD1259CoopCompat.dll", "AD1259CoopClientFix.dll"
    };

    private static readonly Lazy<IReadOnlySet<string>> Known = new(() =>
        ParseKnown(Resource("Coodsz.KnownModuleHashes.txt") + "\n" + Resource("Coodsz.KnownModuleHashes.History.txt")));

    /// <summary>Every SHA256 of a Coodsz build this tool knows, lower case.</summary>
    public static IReadOnlySet<string> KnownHashes => Known.Value;

    /// <summary>The hashes of a list: the first word of each line, lower case. Empty lines and # notes are skipped.</summary>
    public static IReadOnlySet<string> ParseKnown(string? text)
    {
        var hashes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var line in (text ?? "").Split('\n'))
        {
            var word = line.Trim().Split((char[]?)null, 2, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
            if (word != null && !word.StartsWith('#') && word.Length == 64 && word.All(Uri.IsHexDigit))
                hashes.Add(word.ToLowerInvariant());
        }

        return hashes;
    }

    /// <summary>The SHA256 of a file, lower case hex.</summary>
    public static string Sha256(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
    }

    /// <summary>
    /// The folders a report looks in, each with the label the report shows instead of the path: the game's
    /// Modules, the Workshop items of the Steam library the game is in, and each server's Modules and engine\Modules.
    /// </summary>
    public static IReadOnlyList<(string Label, string Folder)> Roots(string? gameRoot, IEnumerable<string?>? serverRoots = null)
    {
        var roots = new List<(string Label, string Folder)>();
        if (!string.IsNullOrWhiteSpace(gameRoot))
        {
            roots.Add(("Modules", Path.Combine(gameRoot, "Modules")));
            // <library>\steamapps\common\Mount & Blade II Bannerlord -> <library>\steamapps\workshop\content\261550
            string? steamApps = null;
            try
            {
                steamApps = Path.GetDirectoryName(Path.GetDirectoryName(Path.GetFullPath(gameRoot).TrimEnd('\\', '/')));
            }
            catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
            {
            }

            if (!string.IsNullOrEmpty(steamApps))
                roots.Add(("Workshop", Path.Combine(steamApps, "workshop", "content", "261550")));
        }

        var servers = (serverRoots ?? []).Where(root => !string.IsNullOrWhiteSpace(root)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        for (var i = 0; i < servers.Count; i++)
        {
            var label = servers.Count == 1 ? "Server" : "Server " + (i + 1);
            roots.Add((label + "\\Modules", Path.Combine(servers[i]!, "Modules")));
            roots.Add((label + "\\engine\\Modules", Path.Combine(servers[i]!, "engine", "Modules")));
        }

        return roots;
    }

    /// <summary>Every Coodsz module DLL under the roots (a module folder's bin\Win64_Shipping_Client or Server), hashed. Folders that are gone or locked are skipped.</summary>
    /// <remarks>
    /// The DLL's FileVersion is free text a changed copy can fill with anything, so only <see cref="SafeVersion"/> of it
    /// is written. The module folder name goes through the log cleaner with the request's passwords; the hash and the
    /// label are written as they are, so the cleaner never cuts a column.
    /// </remarks>
    public static IReadOnlyList<ModuleFingerprint> Find(IEnumerable<(string Label, string Folder)> roots, IReadOnlySet<string>? known = null, IEnumerable<string>? secrets = null)
    {
        known ??= KnownHashes;
        var found = new List<ModuleFingerprint>();
        foreach (var (label, folder) in roots)
        {
            string[] modules;
            try
            {
                if (!Directory.Exists(folder))
                    continue;
                modules = Directory.GetDirectories(folder);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                continue;
            }

            foreach (var module in modules.OrderBy(path => path, StringComparer.OrdinalIgnoreCase))
                foreach (var platform in Platforms)
                {
                    var bin = Path.Combine(module, "bin", platform);
                    IEnumerable<string> dlls;
                    try
                    {
                        dlls = Directory.Exists(bin) ? Directory.GetFiles(bin, "*.dll").Where(dll => DllNames.Contains(Path.GetFileName(dll))).Order(StringComparer.OrdinalIgnoreCase).ToList() : [];
                    }
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                    {
                        continue;
                    }

                    foreach (var dll in dlls)
                    {
                        if (found.Count >= MaxDlls)
                            return found;
                        var where = label + "\\" + CleanFolder(Path.GetFileName(module), secrets) + "\\bin\\" + platform + "\\" + Path.GetFileName(dll);
                        try
                        {
                            if (new FileInfo(dll).Length > MaxDllBytes)
                            {
                                found.Add(new ModuleFingerprint(where, "", TooLargeText, false, false));
                                continue;
                            }

                            var hash = Sha256(dll);
                            found.Add(new ModuleFingerprint(where, SafeVersion(FileVersionInfo.GetVersionInfo(dll).FileVersion), hash, known.Contains(hash)));
                        }
                        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                        {
                            // A locked or unreadable DLL still gets a row, so a report never hides a module.
                            found.Add(new ModuleFingerprint(where, "", CouldNotReadText, false, false));
                        }
                    }
                }
        }

        return found;
    }

    /// <summary>
    /// The version a row shows: only letters, digits and . + _ -, at most MaxVersionChars, and "odd version" when
    /// anything else was there or it was longer, so a changed DLL can never write its own columns or lines.
    /// </summary>
    public static string SafeVersion(string? version)
    {
        var raw = (version ?? "").Trim();
        var kept = new string(raw.Where(c => c is (>= '0' and <= '9') or (>= 'A' and <= 'Z') or (>= 'a' and <= 'z') or '.' or '+' or '_' or '-').ToArray());
        // A Steam id is not a version, like ModReports.CleanVersion says.
        return kept.Length != raw.Length || kept.Length > MaxVersionChars || SteamId.IsMatch(kept) ? OddVersionText : kept;
    }

    /// <summary>The longest module folder name a row keeps.</summary>
    public const int MaxFolderChars = 80;

    private static readonly Regex SteamId = new(@"7656119\d{10}", RegexOptions.CultureInvariant);
    private static readonly Regex NotFolderChar = new(@"[^A-Za-z0-9 _.\-+()\[\]]", RegexOptions.CultureInvariant);

    /// <summary>
    /// The module folder name a row shows: a Steam id anywhere in it becomes steamid, the log cleaner takes out the
    /// PC's names and the passwords, and then only ASCII letters, digits, space and _ . - + ( ) [ ] are kept (anything
    /// else, a line or column break, a direction mark or a look-alike bar, becomes _), at most MaxFolderChars.
    /// </summary>
    public static string CleanFolder(string name, IEnumerable<string>? secrets = null)
    {
        var cleaned = LogZipBuilder.Clean(SteamId.Replace(name, "steamid"), secrets);
        cleaned = NotFolderChar.Replace(SteamId.Replace(cleaned, "steamid"), "_");
        return cleaned.Length > MaxFolderChars ? cleaned[..MaxFolderChars] : cleaned;
    }

    /// <summary>The text of coodsz-modules.txt: one line per DLL with its version, SHA256 and our build or changed.</summary>
    public static string Text(IReadOnlyList<ModuleFingerprint> found, string? tool = null, string? toolVersion = null)
    {
        var text = new StringBuilder();
        // An older tool does not know a newer module build, so the header says where the full list is.
        text.Append("Listed by ").Append(string.IsNullOrWhiteSpace(tool) ? "a Coodsz tool" : tool).Append(' ')
            .Append(string.IsNullOrWhiteSpace(toolVersion) ? "(no version)" : toolVersion)
            .Append(". A hash it does not list may be a newer Coodsz build: check it against the module DLLs and src/BannerlordCoopLauncher.Core/Support/KnownModuleHashes.txt on master.\r\n");
        text.Append("Coodsz modules on this PC (where | version | SHA256 | ").Append(OurBuildText).Append(" or ").Append(ChangedText).Append(")\r\n");
        if (found.Count == 0)
            text.Append("No Coodsz module found.\r\n");
        foreach (var dll in found)
            text.Append(dll.Where).Append(" | ").Append(dll.Version.Length > 0 ? dll.Version : "no version").Append(" | ")
                .Append(dll.Sha256).Append(" | ").Append(!dll.Checked ? NotCheckedText : dll.OurBuild ? OurBuildText : ChangedText).Append("\r\n");
        if (found.Count >= MaxDlls)
            text.Append("Stopped at ").Append(MaxDlls).Append(" DLLs, so more may be on this PC.\r\n");
        return text.ToString();
    }

    private static string Resource(string name)
    {
        using var stream = typeof(ModuleFingerprints).Assembly.GetManifestResourceStream(name);
        if (stream == null)
            return "";
        using var reader = new StreamReader(stream, Encoding.UTF8);
        return reader.ReadToEnd();
    }
}
