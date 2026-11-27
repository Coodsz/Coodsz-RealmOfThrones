using System.IO.Compression;
using System.Text;

namespace TaomCoop.Core;

public sealed record TaomZipReport(
    bool MatchesPlaytest,
    string Summary,
    IReadOnlyList<string> Versions);

public static class TaomArchive
{
    public static TaomZipReport Inspect(string zipPath)
    {
        using var archive = ZipFile.OpenRead(zipPath);
        var versions = new List<string>();
        foreach (var pair in TaomStack.ContentVersions)
        {
            var entry = archive.GetEntry($"Modules/{pair.Key}/SubModule.xml");
            if (entry == null)
                return new TaomZipReport(false, pair.Key + " is missing from the archive.", versions);

            using var reader = new StreamReader(entry.Open());
            var identity = ModuleIdentityReader.ReadXml(reader.ReadToEnd());
            versions.Add(pair.Key + " " + identity.Version);
            if (!identity.Version.Equals(pair.Value, StringComparison.OrdinalIgnoreCase))
            {
                return new TaomZipReport(
                    false,
                    pair.Key + " is " + identity.Version + ". The coop playtest needs " + pair.Value + ".",
                    versions);
            }
        }

        var taomDll = HashEntry(archive, "Modules/TAOM/bin/Win64_Shipping_Client/TAOM.dll");
        if (!taomDll.Equals(TaomStack.TaomDllSha256, StringComparison.OrdinalIgnoreCase))
            return new TaomZipReport(false, "TAOM.dll is not the " + TaomStack.TaomVersionText + " file the coop playtest checks.", versions);

        var dependencies = HashEntry(archive, "Modules/TAOM.Dependencies/bin/Win64_Shipping_Client/TAOM.Dependencies.dll");
        if (!dependencies.Equals(TaomStack.DependenciesDllSha256, StringComparison.OrdinalIgnoreCase))
            return new TaomZipReport(false, "TAOM.Dependencies.dll is not the " + TaomStack.TaomVersionText + " file the coop playtest checks.", versions);

        return new TaomZipReport(true, "This archive is the TAOM " + TaomStack.TaomVersionText + " playtest set.", versions);
    }

    public static string? ContentProblem(string gameRoot)
    {
        var modules = Path.Combine(gameRoot, "Modules");
        foreach (var pair in TaomStack.ContentVersions)
        {
            var xml = Path.Combine(modules, pair.Key, "SubModule.xml");
            if (!File.Exists(xml))
                return pair.Key + " is missing.";

            var identity = ModuleIdentityReader.Read(xml);
            if (!identity.Version.Equals(pair.Value, StringComparison.OrdinalIgnoreCase))
            {
                var found = identity.Version.Length == 0 ? "missing a version" : identity.Version;
                return pair.Key + " is " + found + ". The playtest needs " + pair.Value + ".";
            }
        }

        var taomDll = Path.Combine(modules, "TAOM", "bin", "Win64_Shipping_Client", "TAOM.dll");
        if (!File.Exists(taomDll))
            return "TAOM.dll is missing.";
        if (!Sha256Files.Hash(taomDll).Equals(TaomStack.TaomDllSha256, StringComparison.OrdinalIgnoreCase))
            return "TAOM.dll is not the " + TaomStack.TaomVersionText + " playtest file.";

        var dependencies = Path.Combine(modules, "TAOM.Dependencies", "bin", "Win64_Shipping_Client", "TAOM.Dependencies.dll");
        if (!File.Exists(dependencies))
            return "TAOM.Dependencies.dll is missing.";
        if (!Sha256Files.Hash(dependencies).Equals(TaomStack.DependenciesDllSha256, StringComparison.OrdinalIgnoreCase))
            return "TAOM.Dependencies.dll is not the " + TaomStack.TaomVersionText + " playtest file.";

        return null;
    }

    public static string? CompatProblem(string gameRoot)
    {
        if (NewTaomCompat.Problem(Path.Combine(gameRoot, "Modules", TaomStack.CompatModuleId)) is { } kept)
            return kept;
        var xml = Path.Combine(gameRoot, "Modules", TaomStack.CompatModuleId, "SubModule.xml");
        if (!File.Exists(xml))
            return "TAOM.CoopCompat is missing.";

        var identity = ModuleIdentityReader.Read(xml);
        if (!identity.Version.Equals(TaomStack.CompatVersion, StringComparison.OrdinalIgnoreCase))
        {
            var found = identity.Version.Length == 0 ? "missing a version" : identity.Version;
            return "TAOM.CoopCompat is " + found + ". The playtest needs " + TaomStack.CompatVersion + ".";
        }

        return null;
    }

    public static string CoopModulesPath(string gameRoot) =>
        Path.Combine(gameRoot, "Modules", "TAOM.Dependencies", "coop-modules.txt");

    public static bool CoopIsListed(string gameRoot)
    {
        var path = CoopModulesPath(gameRoot);
        return File.Exists(path) && Listed(File.ReadAllText(path), TaomStack.CoopModuleId);
    }

    /// <summary>
    /// True when Install or repair TAOM should take the install path: TAOM or TAOM.CoopCompat is
    /// missing or the wrong version, or Coop is not on TAOM's co-op list. False means every file
    /// matches, so the button takes the repair path and puts both in again from the zips.
    /// </summary>
    public static bool NeedsInstall(string gameRoot) =>
        ContentProblem(gameRoot) != null || CompatProblem(gameRoot) != null || !CoopIsListed(gameRoot);

    public static void InstallContent(string zipPath, string gameRoot, IProgress<string>? progress)
    {
        var report = Inspect(zipPath);
        if (!report.MatchesPlaytest)
            throw new InvalidOperationException(report.Summary);

        var modulesRoot = Path.Combine(gameRoot, "Modules");
        Directory.CreateDirectory(modulesRoot);
        using var archive = ZipFile.OpenRead(zipPath);
        foreach (var moduleId in TaomStack.ContentVersions.Keys)
        {
            var prefix = "Modules/" + moduleId + "/";
            ReplaceFromArchive(archive, prefix, Path.Combine(modulesRoot, moduleId), moduleId, progress);
        }

        EnsureCoopListed(Path.Combine(modulesRoot, "TAOM.Dependencies", "coop-modules.txt"));
        progress?.Report("TAOM will treat Bannerlord Coop as a co-op mod.");
    }

    public static void InstallCompat(string clientZipPath, string gameRoot, IProgress<string>? progress)
    {
        var destination = Path.Combine(gameRoot, "Modules", TaomStack.CompatModuleId);
        // The TAOM.CoopCompat v2 from the new TAOM download is never replaced (CR-XXP67).
        if (NewTaomCompat.Problem(destination) is { } kept)
            throw new InvalidOperationException(kept);
        using var archive = ZipFile.OpenRead(clientZipPath);
        var count = ReplaceFromArchive(archive, "Payload/Modules/TAOM.CoopCompat/", destination, TaomStack.CompatModuleId, progress);
        if (count == 0)
            throw new InvalidOperationException("The client zip does not contain TAOM.CoopCompat.");

        var identity = ModuleIdentityReader.Read(Path.Combine(destination, "SubModule.xml"));
        if (!identity.Version.Equals(TaomStack.CompatVersion, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("TAOM.CoopCompat is " + identity.Version + ", not " + TaomStack.CompatVersion + ".");

        progress?.Report("Installed TAOM.CoopCompat " + identity.Version + ".");
    }

    // TAOM only treats modules listed in TAOM.Dependencies\coop-modules.txt as co-op
    // mods. Since Workshop Coop 0.1.6 the module id is "Coop" (it was CoopNightly).
    public static void EnsureCoopListed(string coopModulesPath)
    {
        var raw = File.Exists(coopModulesPath)
            ? File.ReadAllText(coopModulesPath)
            : "# TAOM co-op module ids\r\n\r\n[modules]\r\n";
        if (Listed(raw, TaomStack.CoopModuleId))
            return;

        var newline = raw.Contains("\r\n") ? "\r\n" : "\n";
        var lines = new List<string>(raw.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n'));
        var header = lines.FindIndex(line => line.Trim().Equals("[modules]", StringComparison.OrdinalIgnoreCase));
        if (header < 0)
        {
            if (lines.Count > 0 && lines[^1].Length > 0)
                lines.Add("");
            lines.Add("[modules]");
            lines.Add(TaomStack.CoopModuleId);
        }
        else
        {
            var insert = lines.Count;
            for (var i = header + 1; i < lines.Count; i++)
            {
                if (lines[i].Trim().StartsWith('['))
                {
                    insert = i;
                    break;
                }
            }

            lines.Insert(insert, TaomStack.CoopModuleId);
        }

        var parent = Path.GetDirectoryName(coopModulesPath);
        if (!string.IsNullOrEmpty(parent))
            Directory.CreateDirectory(parent);
        File.WriteAllText(coopModulesPath, string.Join(newline, lines), new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
    }

    private static bool Listed(string raw, string moduleId)
    {
        var section = "";
        foreach (var rawLine in raw.Replace("\r\n", "\n").Split('\n'))
        {
            var line = rawLine;
            var comment = line.IndexOf('#');
            if (comment >= 0)
                line = line[..comment];
            line = line.Trim();
            if (line.Length == 0)
                continue;
            if (line.StartsWith('[') && line.EndsWith(']'))
            {
                section = line[1..^1].Trim();
                continue;
            }

            if ((section.Length == 0 || section.Equals("modules", StringComparison.OrdinalIgnoreCase))
                && line.Equals(moduleId, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    private static string HashEntry(ZipArchive archive, string path)
    {
        var entry = archive.GetEntry(path) ?? throw new InvalidOperationException(path + " is missing from the archive.");
        using var stream = entry.Open();
        return Sha256Files.Hash(stream);
    }

    private static int ReplaceFromArchive(ZipArchive archive, string prefix, string destination, string moduleId, IProgress<string>? progress)
    {
        var parent = Path.GetDirectoryName(destination) ?? destination;
        var staging = Path.Combine(parent, "." + moduleId + ".installing");
        if (Directory.Exists(staging))
            Directory.Delete(staging, recursive: true);
        Directory.CreateDirectory(staging);
        var count = 0;
        try
        {
            foreach (var entry in archive.Entries)
            {
                var name = entry.FullName.Replace('\\', '/');
                if (!name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) || name.EndsWith('/'))
                    continue;
                ExtractEntry(entry, staging, name[prefix.Length..]);
                count++;
            }

            if (count == 0)
            {
                Directory.Delete(staging, recursive: true);
                return 0;
            }
            if (Directory.Exists(destination))
            {
                progress?.Report("Replacing " + moduleId + ".");
                Directory.Delete(destination, recursive: true);
            }

            Directory.Move(staging, destination);
            progress?.Report("Installed " + moduleId + " (" + count + " files).");
            return count;
        }
        catch
        {
            if (Directory.Exists(staging))
                Directory.Delete(staging, recursive: true);
            throw;
        }
    }

    private static void ExtractEntry(ZipArchiveEntry entry, string moduleRoot, string relative)
    {
        var target = Path.GetFullPath(Path.Combine(moduleRoot, relative.Replace('/', Path.DirectorySeparatorChar)));
        var root = Path.GetFullPath(moduleRoot) + Path.DirectorySeparatorChar;
        if (!target.StartsWith(root, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("The archive contains an unsafe path.");

        var parent = Path.GetDirectoryName(target);
        if (!string.IsNullOrEmpty(parent))
            Directory.CreateDirectory(parent);
        entry.ExtractToFile(target, overwrite: true);
    }
}
