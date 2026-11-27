using System.Diagnostics;
using System.Reflection;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Xml;
using System.Xml.Linq;

namespace TaomCoop.Core;

/// <summary>What <see cref="Taom157ServerSetup.Apply"/> did to a server folder.</summary>
public sealed record Taom157SetupResult(
    IReadOnlyList<string> Moved,
    IReadOnlyList<string> HarmonyRemoved,
    bool HarmonyCopied,
    IReadOnlyList<string> DepsWritten,
    IReadOnlyList<string> DepsRemoved,
    string? DepsBackup)
{
    public bool Changed => Moved.Count > 0 || HarmonyRemoved.Count > 0 || HarmonyCopied || DepsWritten.Count > 0 || DepsRemoved.Count > 0;
}

/// <summary>
/// The setup TAOM needs on the public Coop dedicated server for Bannerlord 1.5.7 (Coop v0.1.8), from Haterade's test of
/// 21 November 2026 (tools guide section 7). The server package does none of it, and a player should never do it by hand:
///   1. TAOM.dll needs MCMv5, ButterLib and UIExtenderEx. Their DLLs sit in the server bin folders of TAOM.Dependencies
///      and TAOM, where the engine does not look, so they move into engine\bin\Win64_Shipping_Server. The modules' own
///      0Harmony.dll goes, and Coop's server 0Harmony.dll is copied into the engine folder instead.
///   2. The engine runs on .NET 8 and only finds assemblies listed in TaleWorlds.Starter.DotNetCore.deps.json, so each
///      of those DLLs gets a runtime entry there. Without them TAOM.dll throws ReflectionTypeLoadException and the server
///      stops with exit code 5.
///   3. The server starts with --no-watchdog, which NightlyServer.StartPlan already passes. With the watchdog on, every
///      missing creature animation writes a crash dump of 1 to 2 GB. That step is the start's, not this class's.
/// Apply does steps 1 and 2 and is safe to run on every start: a second run on the same files changes nothing.
/// </summary>
public static class Taom157ServerSetup
{
    public const string DepsFileName = "TaleWorlds.Starter.DotNetCore.deps.json";

    /// <summary>The deps file as the server shipped it, kept next to it before the first change.</summary>
    public const string DepsBackupName = DepsFileName + ".before-taom";

    public const string HarmonyDll = "0Harmony.dll";

    /// <summary>
    /// This setup's own record in the engine folder. Its first line is the SHA-256 of the deps file as this setup last
    /// wrote it (two of them, old and new, while the file is being swapped). Each line after it is a DLL this setup
    /// moved in and the module it came from, with a tab between. A DLL
    /// in the record is replaced when that module brings a new copy (a TAOM update). Any other DLL in the engine folder
    /// is the server's own and is never touched. A deps file with another hash means the server was updated or
    /// unpacked again, so the record starts over from that file.
    /// </summary>
    public const string RecordFileName = "coodsz-taom-helpers.txt";

    /// <summary>The modules whose server bin folders hold TAOM's helper DLLs. When both hold a DLL of the same name,
    /// the first one's copy is the one that moves.</summary>
    public static readonly IReadOnlyList<string> HelperModules = ["TAOM.Dependencies", "TAOM"];

    private const string ServerBin = "Win64_Shipping_Server";
    // System.*, Microsoft.Bcl.*, Microsoft.CodeAnalysis* and Serilog*, matched without letter case. In lower case so
    // tools\ExtractUiText doesn't take a lone capitalized word for screen text.
    private static readonly string[] SkippedPrefixes = ["system.", "microsoft.bcl.", "microsoft.codeanalysis", "serilog"];

    public static string EngineBin(string serverRoot) => Path.Combine(Path.GetFullPath(serverRoot), "engine", "bin", ServerBin);

    public static string ModuleBin(string serverRoot, string moduleId) =>
        Path.Combine(Path.GetFullPath(serverRoot), "engine", "Modules", moduleId, "bin", ServerBin);

    public static string DepsPath(string serverRoot) => Path.Combine(EngineBin(serverRoot), DepsFileName);

    /// <summary>One plain line for why the setup can't run on this server folder, or null when it can. Everything is
    /// read here before any file moves, so a folder that is refused is left untouched.</summary>
    public static string? Problem(string serverRoot)
    {
        if (!File.Exists(DepsPath(serverRoot)))
            return "This is not a Coop server for Bannerlord 1.5.7. " + DepsFileName + " is missing from its engine\\bin\\" + ServerBin + " folder.";
        foreach (var id in HelperModules)
        {
            var subModule = SubModulePath(serverRoot, id);
            if (!File.Exists(subModule))
                return id + " is not in the server's engine\\Modules folder.";
            // Apply moves files out of the module's bin folder, so a folder that is only a link to a Workshop item or
            // another server is refused: the move would take the files out of that other place.
            if (IsLink(Path.GetDirectoryName(subModule)!) || IsLink(Path.GetDirectoryName(ModuleBin(serverRoot, id))!) || IsLink(ModuleBin(serverRoot, id)))
                return id + " in the server's engine\\Modules folder is a link to another folder. Copy " + id + " into the server instead.";
            try
            {
                if (ServerLoadedDlls(subModule) == null)
                    return "The server's copy of " + id + " has a SubModule.xml that can't be read as a module file. Copy " + id + " into the server again.";
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                return Locked(subModule);
            }
        }

        if (!File.Exists(Path.Combine(ModuleBin(serverRoot, TaomStack.CoopModuleId), HarmonyDll))
            && !File.Exists(Path.Combine(EngineBin(serverRoot), HarmonyDll)))
            return "Coop's " + HarmonyDll + " is missing from the server's engine\\Modules\\Coop\\bin\\" + ServerBin + " folder.";
        try
        {
            return ReadDeps(serverRoot) == null
                ? DepsFileName + " in the server's engine\\bin\\" + ServerBin + " folder doesn't list the DLLs the server loads, so TAOM can't be set up on it."
                : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return Locked(DepsPath(serverRoot));
        }
    }

    private static string Locked(string path) =>
        path + " can't be read now. Close programs that may have it open, like an antivirus scan, then start the server again.";

    private static bool IsLink(string folder) =>
        Directory.Exists(folder) && (File.GetAttributes(folder) & FileAttributes.ReparsePoint) != 0;

    /// <summary>
    /// The helper DLLs to move, as full paths: every DLL in the server bin of TAOM.Dependencies and TAOM that no
    /// submodule the server loads names, except 0Harmony.dll, System.*, Microsoft.Bcl.*, Microsoft.CodeAnalysis*,
    /// Serilog*, anything Coop ships, and anything already in the engine folder that this setup did not put there
    /// from that same module. A helper recorded with no owner counts again when the module's copy differs from the
    /// engine copy. Throws the usual file exceptions when one of those two copies is locked.
    /// </summary>
    public static IReadOnlyList<string> Helpers(string serverRoot) => Helpers(serverRoot, ReadRecord(serverRoot).Owned);

    private static IReadOnlyList<string> Helpers(string serverRoot, IReadOnlyDictionary<string, string> owned)
    {
        var engine = Names(EngineBin(serverRoot));
        var coop = Names(ModuleBin(serverRoot, TaomStack.CoopModuleId));
        var found = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var id in HelperModules)
        {
            var bin = ModuleBin(serverRoot, id);
            if (!Directory.Exists(bin))
                continue;
            // A submodule tagged DedicatedServerType none (TAOM's MCMv5, ButterLib and UIExtenderEx, and TAOM.Client if
            // it ever ships in the server bin) is not loaded by the server, so its DLL is a helper too.
            var loaded = ServerLoadedDlls(SubModulePath(serverRoot, id)) ?? new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var path in Directory.EnumerateFiles(bin, "*.dll").OrderBy(Path.GetFileName, StringComparer.OrdinalIgnoreCase))
            {
                var name = Path.GetFileName(path);
                // The first module's copy of a name is the one that counts, on every run, also when that copy stays.
                if (!seen.Add(name))
                    continue;
                if (name.Equals(HarmonyDll, StringComparison.OrdinalIgnoreCase)
                    || loaded.Contains(name)
                    || SkippedPrefixes.Any(prefix => name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                    || coop.Contains(name))
                    continue;
                // A helper is replaced only by a new copy from the module it came from. One that a later TAOM ships in
                // the other module keeps the engine copy. One whose module is not known (a record made again from the
                // backup) keeps the engine copy while the module's copy is the same file. A copy that differs is a TAOM
                // update: it moves in, and Apply records that module as the owner.
                var mine = owned.TryGetValue(name, out var from) && from.Equals(id, StringComparison.OrdinalIgnoreCase);
                var update = from == NoOwner && engine.Contains(name)
                    && !Sha256Files.Hash(path).Equals(Sha256Files.Hash(Path.Combine(EngineBin(serverRoot), name)), StringComparison.OrdinalIgnoreCase);
                if (engine.Contains(name) && !mine && !update)
                    continue;
                found.Add(path);
            }
        }

        return found;
    }

    /// <summary>Steps 1 and 2 on this server folder. Throws InvalidOperationException with <see cref="Problem"/>'s line
    /// when the folder is not ready, and the usual file exceptions when a file is locked. A run that stopped halfway is
    /// finished by the next one.</summary>
    public static Taom157SetupResult Apply(string serverRoot)
    {
        if (Problem(serverRoot) is { } problem)
            throw new InvalidOperationException(problem);

        var engineBin = EngineBin(serverRoot);
        var depsPath = DepsPath(serverRoot);
        var record = ReadRecord(serverRoot);
        var owned = new Dictionary<string, string>(record.Owned, StringComparer.OrdinalIgnoreCase);
        string? backup = null;
        var depsHash = Sha256Files.Hash(depsPath);
        if (!record.DepsHashes.Contains(depsHash, StringComparer.OrdinalIgnoreCase))
        {
            var current = ReadDeps(serverRoot)!.Value.Runtime;
            // Only helpers that are assemblies have an entry to look for.
            var inEngine = owned.Keys.Where(name => Versions(Path.Combine(engineBin, name)) != null).ToList();
            if (inEngine.Count > 0 && inEngine.All(name => EntryName(current, name) != null))
            {
                // Still this setup's file, changed by hand: every helper in the engine folder is listed. The backup
                // and the record stay, and only the hash is taken. A server that ships every one of those names
                // itself would be read the same way.
                WriteRecord(serverRoot, depsHash, owned);
            }
            else if (owned.Count == 0 && ReadDepsFile(Path.Combine(engineBin, DepsBackupName)) is { } before
                && before.Runtime.All(entry => EntryName(current, entry.Key) != null)
                && EntryName(current, HarmonyDll) != null && EntryName(before.Runtime, HarmonyDll) == null)
            {
                // The record is gone but the backup is there, the file still lists all of it, and it lists 0Harmony.dll,
                // which only this setup adds: this setup's file, not a newer server's.
                // What it lists on top of the backup is a helper again, also one whose DLL is no longer in the engine
                // folder: the pass below for helpers that are gone takes that one out of the file and the record.
                // Which module it came from is not known any more, and a module that still holds a copy of the name
                // is not proof (when both ship one, the first module's copy is the one that moved). So it is recorded
                // with no owner: it stays listed, and the engine copy is kept until a module's copy differs from it.
                foreach (var name in current.Select(entry => entry.Key).Where(name => EntryName(before.Runtime, name) == null
                             && !name.Equals(HarmonyDll, StringComparison.OrdinalIgnoreCase)
                             && IsHelperName(name)).ToList())
                    owned[name] = NoOwner;
                WriteRecord(serverRoot, depsHash, owned);
            }
            else
            {
                // The server's own file, new or after an update. It becomes the backup, and a DLL it lists is the
                // server's own from now on, whoever put the file there before.
                foreach (var name in owned.Keys.Where(name => EntryName(current, name) != null).ToList())
                    owned.Remove(name);
                backup = Path.Combine(engineBin, DepsBackupName);
                File.Copy(depsPath, backup, overwrite: true);
                WriteRecord(serverRoot, depsHash, owned);
            }
        }

        var moved = new List<string>();
        foreach (var source in Helpers(serverRoot, owned))
        {
            var name = Path.GetFileName(source);
            var module = HelperModules.First(id => source.StartsWith(ModuleBin(serverRoot, id) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase));
            // Recorded before the move, so a move that stops halfway never leaves a DLL in the engine folder that the
            // next run takes for the server's own.
            if (!owned.TryGetValue(name, out var from) || !from.Equals(module, StringComparison.OrdinalIgnoreCase))
            {
                owned[name] = module;
                WriteRecord(serverRoot, depsHash, owned);
            }

            File.Move(source, Path.Combine(engineBin, name), overwrite: true);
            moved.Add(name);
        }

        // Coop's copy goes in first, so a copy that fails never leaves the server with no 0Harmony.dll at all.
        var copied = CopyCoopHarmony(serverRoot);
        var removed = new List<string>();
        foreach (var id in HelperModules)
        {
            var harmony = Path.Combine(ModuleBin(serverRoot, id), HarmonyDll);
            if (!File.Exists(harmony))
                continue;
            File.Delete(harmony);
            removed.Add(id + "\\" + HarmonyDll);
        }

        // A helper that is gone from the engine folder (a TAOM update that dropped it) is no helper any more: it leaves
        // the record, and its entry leaves the deps file, since the .NET host may refuse a listed assembly that is missing.
        var gone = owned.Keys.Where(name => !File.Exists(Path.Combine(engineBin, name)))
            .OrderBy(name => name, StringComparer.OrdinalIgnoreCase).ToList();
        // The record keeps their names until the deps file without them is in place, so a stop in between leaves them
        // for the next run to remove.
        var kept = owned.Where(pair => !gone.Contains(pair.Key, StringComparer.OrdinalIgnoreCase))
            .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.OrdinalIgnoreCase);
        var listed = kept.Keys.Append(HarmonyDll)
            .Where(name => File.Exists(Path.Combine(engineBin, name)))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
            .ToList();
        var (written, dropped) = ListInDeps(serverRoot, listed, gone, owned, kept);
        // With nothing to change in the deps file the record still has to forget the helpers that are gone.
        if (gone.Count > 0 && written.Count == 0 && dropped.Count == 0)
            WriteRecord(serverRoot, Sha256Files.Hash(depsPath), kept);
        return new Taom157SetupResult(moved, removed, copied, written, dropped, backup);
    }

    /// <summary>The DLL names the submodules a dedicated server loads name in this SubModule.xml, or null when the file
    /// can't be read as XML. A submodule tagged DedicatedServerType none is for the game client only.</summary>
    public static IReadOnlySet<string>? ServerLoadedDlls(string subModulePath)
    {
        XDocument document;
        try
        {
            document = XDocument.Load(subModulePath);
        }
        catch (XmlException)
        {
            return null;
        }

        // Well formed XML that is no module file would name no DLL, and then the module's own DLL would move too.
        if (document.Root?.Name.LocalName != "Module")
            return null;
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var subModule in document.Descendants("SubModule"))
        {
            var dll = subModule.Element("DLLName")?.Attribute("value")?.Value.Trim();
            if (string.IsNullOrEmpty(dll))
                continue;
            var clientOnly = subModule.Descendants("Tag").Any(tag =>
                string.Equals(tag.Attribute("key")?.Value, "DedicatedServerType", StringComparison.OrdinalIgnoreCase)
                && string.Equals(tag.Attribute("value")?.Value, "none", StringComparison.OrdinalIgnoreCase));
            if (!clientOnly)
                names.Add(dll);
        }

        return names;
    }

    // Coop's server 0Harmony.dll is the one build every module on the server has to share.
    private static bool CopyCoopHarmony(string serverRoot)
    {
        var source = Path.Combine(ModuleBin(serverRoot, TaomStack.CoopModuleId), HarmonyDll);
        var target = Path.Combine(EngineBin(serverRoot), HarmonyDll);
        if (!File.Exists(source))
            return false;
        if (File.Exists(target) && Sha256Files.Hash(source).Equals(Sha256Files.Hash(target), StringComparison.OrdinalIgnoreCase))
            return false;
        // The 0Harmony.dll that was in the engine folder is kept once next to it: another build someone put there, or
        // after a Coop server update the older Coop copy from this setup.
        var kept = target + ".before-taom";
        if (File.Exists(target) && !File.Exists(kept))
            File.Copy(target, kept);
        File.Copy(source, target, overwrite: true);
        return true;
    }

    // Adds or corrects the runtime entry of each DLL, with the assemblyVersion and fileVersion the file itself has,
    // the same shape as the entries the server ships. The file is read and written with a JSON parser; only the
    // runtime list changes.
    private static (IReadOnlyList<string> Written, IReadOnlyList<string> Removed) ListInDeps(string serverRoot, IReadOnlyList<string> names,
        IReadOnlyList<string> gone, IReadOnlyDictionary<string, string> owned, IReadOnlyDictionary<string, string> kept)
    {
        var path = DepsPath(serverRoot);
        // Problem already read this file, so it parses and has the list.
        var (root, runtime) = ReadDeps(serverRoot)!.Value;
        var written = new List<string>();
        foreach (var name in names)
        {
            if (Versions(Path.Combine(EngineBin(serverRoot), name)) is not { } versions)
                continue;
            var (assembly, file) = versions;
            var listedAs = EntryName(runtime, name);
            if (listedAs == name && runtime[name] is JsonObject entry
                && Text(entry["assemblyVersion"]) == assembly && Text(entry["fileVersion"]) == file)
                continue;
            // An entry that differs only in letter case goes, so the list never names one file twice.
            if (listedAs != null)
                runtime.Remove(listedAs);
            runtime[name] = new JsonObject { ["assemblyVersion"] = assembly, ["fileVersion"] = file };
            written.Add(name);
        }

        var removed = new List<string>();
        foreach (var name in gone)
        {
            if (EntryName(runtime, name) is not { } listedAs)
                continue;
            runtime.Remove(listedAs);
            removed.Add(name);
        }

        if (written.Count == 0 && removed.Count == 0)
            return (written, removed);

        var options = new JsonSerializerOptions { WriteIndented = true, Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };
        // Written next to the file first, so a stop in the middle never leaves the server half a deps file.
        var temp = path + ".coodsz-new";
        File.WriteAllText(temp, root.ToJsonString(options) + Environment.NewLine, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        // The record names both files across the swap, the one there now and the new one, so a stop on either side of
        // the move leaves a file the next run still knows as this setup's.
        var newHash = Sha256Files.Hash(temp);
        WriteRecord(serverRoot, Sha256Files.Hash(path) + " " + newHash, owned);
        File.Move(temp, path, overwrite: true);
        WriteRecord(serverRoot, newHash, kept);
        return (written, removed);
    }

    // A version in the deps file as text, or null when the entry holds something else there.
    private static string? Text(JsonNode? node) => node is JsonValue value && value.TryGetValue<string>(out var text) ? text : null;

    // The parsed deps file and its runtime list (targets, the one target, "TaleWorlds.Starter.DotNetCore/<version>",
    // runtime), or null when the file is no JSON, names a key twice, or has no such list.
    private static (JsonNode Root, JsonObject Runtime)? ReadDeps(string serverRoot) => ReadDepsFile(DepsPath(serverRoot));

    private static (JsonNode Root, JsonObject Runtime)? ReadDepsFile(string path)
    {
        try
        {
            if (!File.Exists(path))
                return null;
            var root = JsonNode.Parse(File.ReadAllText(path),
                documentOptions: new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
            if (root?["targets"] is not JsonObject targets)
                return null;
            foreach (var target in targets)
            {
                if (target.Value is not JsonObject libraries)
                    continue;
                foreach (var library in libraries)
                {
                    if (library.Key.StartsWith("TaleWorlds.Starter.DotNetCore/", StringComparison.OrdinalIgnoreCase)
                        && library.Value is JsonObject starter && starter["runtime"] is JsonObject runtime)
                    {
                        // Walked once here, so a key named twice is found now and not in the middle of the setup.
                        _ = runtime.Count;
                        return (root, runtime);
                    }
                }
            }

            return null;
        }
        catch (Exception ex) when (ex is JsonException or ArgumentException or InvalidOperationException)
        {
            return null;
        }
    }

    private static string? EntryName(JsonObject runtime, string name) =>
        runtime.Select(pair => pair.Key).FirstOrDefault(key => key.Equals(name, StringComparison.OrdinalIgnoreCase));

    // Null for a file that is no .NET assembly or is not there: the engine's list only holds assemblies.
    private static (string Assembly, string File)? Versions(string path)
    {
        if (!File.Exists(path))
            return null;
        try
        {
            var assembly = AssemblyName.GetAssemblyName(path).Version ?? new Version(0, 0, 0, 0);
            var info = FileVersionInfo.GetVersionInfo(path);
            var file = new Version(info.FileMajorPart, info.FileMinorPart, info.FileBuildPart, info.FilePrivatePart);
            return (Four(assembly), Four(file));
        }
        catch (BadImageFormatException)
        {
            return null;
        }
    }

    private static string Four(Version version) =>
        Math.Max(version.Major, 0) + "." + Math.Max(version.Minor, 0) + "." + Math.Max(version.Build, 0) + "." + Math.Max(version.Revision, 0);

    private static string SubModulePath(string serverRoot, string moduleId) =>
        Path.Combine(Path.GetFullPath(serverRoot), "engine", "Modules", moduleId, "SubModule.xml");

    private static HashSet<string> Names(string folder) =>
        Directory.Exists(folder)
            ? Directory.EnumerateFiles(folder).Select(path => Path.GetFileName(path)!).ToHashSet(StringComparer.OrdinalIgnoreCase)
            : new HashSet<string>(StringComparer.OrdinalIgnoreCase);

    // The owner of a helper whose module is not known. A module's copy replaces it only when it differs from the
    // engine copy, and that module is the owner from then on.
    private const string NoOwner = "";

    private static bool IsHelperName(string name) =>
        name.Length > 0 && name == Path.GetFileName(name) && name.EndsWith(".dll", StringComparison.OrdinalIgnoreCase);

    // Only plain DLL names from a known module (or none) count, so a record changed by hand can never point outside
    // the folder.
    private static (IReadOnlyList<string> DepsHashes, IReadOnlyDictionary<string, string> Owned) ReadRecord(string serverRoot)
    {
        var owned = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var path = Path.Combine(EngineBin(serverRoot), RecordFileName);
        if (!File.Exists(path))
            return ([], owned);
        var lines = File.ReadAllLines(path);
        foreach (var line in lines.Skip(1))
        {
            var parts = line.Split('\t');
            if (parts.Length != 2)
                continue;
            var name = parts[0].Trim();
            var module = parts[1].Trim();
            if (IsHelperName(name) && (module == NoOwner || HelperModules.Contains(module, StringComparer.OrdinalIgnoreCase)))
                owned[name] = module;
        }

        return (lines.Length > 0 ? lines[0].Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries) : [], owned);
    }

    // Written next to the record first and moved over it, like the deps file: a stop while it writes never leaves a
    // short or empty record, which would make the next run take the helpers it already moved for the server's own.
    private static void WriteRecord(string serverRoot, string depsHash, IReadOnlyDictionary<string, string> owned)
    {
        var path = Path.Combine(EngineBin(serverRoot), RecordFileName);
        var temp = path + ".coodsz-new";
        File.WriteAllLines(temp,
            new[] { depsHash }.Concat(owned.OrderBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase).Select(pair => pair.Key + "\t" + pair.Value)));
        File.Move(temp, path, overwrite: true);
    }
}
