using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO.Compression;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using BannerlordCoopLauncher.Core.Support;
using BannerlordCoopLauncher.Core.Updates;

namespace BannerlordCoopLauncher.Core.IO;

/// <summary>
/// The modules a tool used to ship in folders beside its exe (ServerTools, ClientAddOns, SaveRescue,
/// the bundled mods and the like). The build packs each folder as one zip resource
/// (src\CoodszPayload.targets), and the first use writes it out to
/// %LOCALAPPDATA%\Coodsz\&lt;Product&gt;\payload\&lt;version&gt;\&lt;name&gt;.
/// </summary>
public static class ToolPayload
{
    public const string ResourcePrefix = "Coodsz.Payload.";

    // The game mods a client launcher copies into Modules (BundledModules, TaomPlus). Only release
    // builds carry them; a dev build without them says the mods are not bundled.
    public const string BundledMods = "BundledMods";

    /// <summary>
    /// The tool's own module folders older versions shipped beside the exe. Once the exe's copy is written
    /// out they go. The bundled game mods are not in the list: a player may have put those there too.
    /// </summary>
    public static readonly IReadOnlyList<string> OldToolFolders =
        ["ClientAddOns", "ServerTools", "PatchModules", "ImageExporter", "SaveRescue", "DedicatedServerRepair", "ServerAddOns", "WarSync"];

    /// <summary>Where the payloads come from. Tests swap it; the tools use the exe's own resources.</summary>
    internal sealed record PayloadSource(Func<string, Stream?> Open, Func<IEnumerable<string>> Names, Func<string?> VersionFolder, string ExeFolder);

    internal static PayloadSource Source { get; set; } = ExeSource();

    // One lock per payload, so writing out the bundled mods never holds up a small module.
    private static readonly ConcurrentDictionary<string, object> Locks = new(StringComparer.OrdinalIgnoreCase);
    // A written out folder, or null when the exe carries no such payload.
    private static readonly ConcurrentDictionary<string, string?> Written = new(StringComparer.OrdinalIgnoreCase);
    // An open lock file in each version folder this process uses, so another tool never prunes it.
    private static readonly ConcurrentDictionary<string, FileStream> InUse = new(StringComparer.OrdinalIgnoreCase);
    // When a write-out last failed. Until RetryAfter has passed every caller gets the payload's own folder
    // (where it is missing) at once, so a window never waits again for a write-out that just failed.
    private static readonly ConcurrentDictionary<string, DateTime> FailedAt = new(StringComparer.OrdinalIgnoreCase);

    internal static TimeSpan RetryAfter { get; set; } = TimeSpan.FromSeconds(30);

    // Where WriteOutAll notes an old folder it removed. Tests swap it.
    internal static Action<string> Log { get; set; } = ToolData.Log;

    /// <summary>Raised on a worker thread when a payload has been written out.</summary>
    public static event Action<string>? Ready;

    /// <summary>
    /// The folder that holds one payload: the copy written out of this exe, or the folder of that name
    /// beside the exe when this exe carries no payload at all (a dev run). When the exe carries payloads and
    /// this one failed to write out or is not among them, the folder it belongs in (see
    /// <see cref="Unavailable"/>), never the one beside the exe.
    /// Writing out can take a few seconds for the bundled mods, so the window code uses
    /// <see cref="FolderAsync"/> or <see cref="Peek"/> for those.
    /// </summary>
    public static string Folder(string name)
    {
        string? folder;
        var wrote = false;
        var failed = false;
        lock (Locks.GetOrAdd(name, _ => new object()))
        {
            if (Cached(name, out folder))
                return folder ?? Fallback(name);
            // A failed write-out means the exe carries it, so its own folder (where it is missing) at once.
            if (FailedRecently(name))
                return Unavailable(name, Source.VersionFolder());
            folder = WriteOut(name, out var tryAgain);
            if (tryAgain)
            {
                FailedAt[name] = DateTime.UtcNow;
                failed = true;
            }
            else
            {
                Written[name] = folder;
                FailedAt.TryRemove(name, out _);
            }

            wrote = folder != null;
        }

        if (wrote)
            Ready?.Invoke(name);
        return folder ?? (failed ? Unavailable(name, Source.VersionFolder()) : Fallback(name));
    }

    public static Task<string> FolderAsync(string name) => Task.Run(() => Folder(name));

    /// <summary>
    /// Without waiting: the folder when it is ready, the folder beside the exe when the exe has no payload at
    /// all, the folder it belongs in when the exe does not carry this one, its write-out just failed or cannot
    /// run; otherwise null while
    /// the write-out runs in the background
    /// (<see cref="Ready"/> says when it is done).
    /// </summary>
    public static string? Peek(string name)
    {
        if (Cached(name, out var folder))
            return folder ?? Fallback(name);
        if (FailedRecently(name))
            return Unavailable(name, Source.VersionFolder());
        if (!Carries(name))
            return Fallback(name);
        if (Source.VersionFolder() == null)
            return Unavailable(name, null);
        _ = FolderAsync(name);
        return null;
    }

    /// <summary>True when this folder is the payload written out of this exe, not the fallback beside it.</summary>
    public static bool IsWrittenOut(string name, string folder) =>
        Written.TryGetValue(name, out var written) && written != null
        && string.Equals(written, folder, StringComparison.OrdinalIgnoreCase);

    private static bool FailedRecently(string name) =>
        FailedAt.TryGetValue(name, out var at) && DateTime.UtcNow - at < RetryAfter;

    /// <summary>
    /// Where a payload belongs: its folder under %LOCALAPPDATA%\Coodsz\&lt;Product&gt;\payload\&lt;version&gt;,
    /// or, when the tool has no settings folder, the same folder <see cref="Folder"/> answers with then.
    /// For the line that says it is missing.
    /// </summary>
    public static string Expected(string name, params string[] inside)
    {
        var version = Source.VersionFolder();
        var folder = version == null ? Fallback(name) : Path.Combine(version, name);
        return inside.Length == 0 ? folder : Path.Combine([folder, .. inside]);
    }

    /// <summary>The line a tool shows when a module it carries could not be written out.</summary>
    public static string MissingText(string module, string folder) =>
        module + " is missing from " + folder + ". Start the tool again. If it still says this, check that your antivirus did not block or remove it.";

    public static bool Carries(string name)
    {
        using var zip = Source.Open(name);
        return zip != null;
    }

    /// <summary>
    /// Writes out every payload this exe carries, smallest first so the bundled mods come last, and removes
    /// the old copy of each tool folder beside the exe once its own copy is in place.
    /// </summary>
    public static void WriteOutAll()
    {
        var names = Source.Names()
            .Select(name => (Name: name, Size: SizeOf(name)))
            .OrderBy(item => item.Size)
            .Select(item => item.Name)
            .ToList();
        foreach (var name in names)
        {
            var folder = Folder(name);
            if (IsWrittenOut(name, folder) && RemoveOldFolder(Source.ExeFolder, name))
                Log("Removed the old " + name + " folder beside the exe. This exe carries it and wrote it out to " + folder + ".");
        }
    }

    /// <summary>
    /// Deletes one of <see cref="OldToolFolders"/> beside the exe. Never in a folder with project files
    /// (a source checkout), never a bundled mod folder, never while a Coodsz exe of another product shares
    /// the folder (an old TAOM tool may still use its SaveRescue there), and never while an old name exe of
    /// this tool is still there (an old CoodszHost.exe reads ServerTools beside it; ToolRename.Cleanup keeps
    /// it while a shortcut to it could not be moved). True when it was there and is gone.
    /// </summary>
    public static bool RemoveOldFolder(string exeFolder, string name, string? currentExeName = null)
    {
        try
        {
            var folder = Path.Combine(exeFolder, name);
            if (!OldToolFolders.Contains(name, StringComparer.OrdinalIgnoreCase) || !Directory.Exists(folder))
                return false;
            if (Directory.EnumerateFiles(exeFolder, "*.csproj").Any() || Directory.EnumerateFiles(exeFolder, "*.sln").Any())
                return false;
            var current = currentExeName ?? Path.GetFileName(Environment.ProcessPath ?? "");
            if (OtherProductBeside(exeFolder, current) || ToolRename.OldExePaths(exeFolder, current).Any(File.Exists))
                return false;
            Directory.Delete(folder, recursive: true);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // In use or read only; the next start tries again.
            return false;
        }
    }

    // A known Coodsz exe in the folder whose product is not this exe's. When this exe has no known name (the
    // player renamed it), every Coodsz exe beside it counts as another product, so nothing shared goes.
    private static bool OtherProductBeside(string exeFolder, string currentExeName)
    {
        var mine = ToolNames.Find(currentExeName)?.Product;
        return ToolNames.KnownExeNames
            .Where(exe => File.Exists(Path.Combine(exeFolder, exe)))
            .Any(exe => !string.Equals(ToolNames.Find(exe)?.Product, mine, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>Forgets every written out folder, for tests.</summary>
    internal static void Reset()
    {
        Written.Clear();
        FailedAt.Clear();
        foreach (var stream in InUse.Values)
            stream.Dispose();
        InUse.Clear();
    }

    // A cached answer counts only while its folder is still there; a deleted one is written again.
    private static bool Cached(string name, out string? folder) =>
        Written.TryGetValue(name, out folder) && (folder == null || Directory.Exists(folder));

    private static string Beside(string name) => Path.Combine(Source.ExeFolder, name);

    // An exe with any payload in it never falls back to the folder beside it, not even for a name it does not
    // carry (the AD1259 server tool once asked for ImageExporter): release zips no longer ship those folders,
    // so whatever sits there is not ours and would go into the game or server unchecked. Only a build that
    // carries no payload at all (a dev or test run) uses the folder beside the exe.
    private static string Fallback(string name) => CarriesAny() ? Unavailable(name, Source.VersionFolder()) : Beside(name);

    private static bool CarriesAny() => Source.Names().Any();

    /// <summary>
    /// Where a carried payload belongs when it could not be written out this time: its folder under the
    /// version folder (or under %LOCALAPPDATA%\Coodsz\payload when the tool has no settings folder). The
    /// callers find their module missing there and show <see cref="MissingText"/>.
    /// </summary>
    internal static string Unavailable(string name, string? versionFolder) =>
        versionFolder != null ? Path.Combine(versionFolder, name) : Path.Combine(ToolData.DefaultRoot, "payload", name);

    private static long SizeOf(string name)
    {
        using var zip = Source.Open(name);
        return zip?.Length ?? 0;
    }

    // tryAgain is true when the exe carries the payload but writing it out failed this time.
    private static string? WriteOut(string name, out bool tryAgain)
    {
        tryAgain = false;
        var version = Source.VersionFolder();
        if (version == null)
            return null;
        try
        {
            using var zip = Source.Open(name);
            if (zip == null)
                return null;
            tryAgain = true;
            HoldVersion(version);
            var folder = PayloadWriter.Ensure(zip, version, name);
            tryAgain = false;
            return folder;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException or InvalidOperationException)
        {
            return null;
        }
    }

    // A lock file nobody else can delete while this process runs (Windows removes it when the process ends).
    private static void HoldVersion(string versionFolder)
    {
        InUse.GetOrAdd(versionFolder, folder =>
        {
            Directory.CreateDirectory(folder);
            return new FileStream(Path.Combine(folder, PayloadWriter.InUsePrefix + Environment.ProcessId + ".lock"),
                FileMode.Create, FileAccess.Write, FileShare.None, 1, FileOptions.DeleteOnClose);
        });
    }

    private static PayloadSource ExeSource()
    {
        var assembly = Assembly.GetEntryAssembly();
        return new PayloadSource(
            name => assembly?.GetManifestResourceStream(ResourcePrefix + name + ".zip"),
            () => (assembly?.GetManifestResourceNames() ?? [])
                .Where(resource => resource.StartsWith(ResourcePrefix, StringComparison.Ordinal) && resource.EndsWith(".zip", StringComparison.Ordinal))
                .Select(resource => resource[ResourcePrefix.Length..^".zip".Length]),
            () => ToolData.Current?.PayloadFolder(assembly?.GetName().Version?.ToString(3) ?? "0.0.0"),
            AppContext.BaseDirectory);
    }

    /// <summary>
    /// The folder for one payload with everything passed in, for tests: the written out copy when there is
    /// a zip, the folder it belongs in when there is a zip that could not be written out, and the folder
    /// beside the exe only when there is no zip.
    /// </summary>
    public static string Resolve(string name, Stream? zip, string? versionFolder, string exeFolder)
    {
        if (zip == null)
            return Path.Combine(exeFolder, name);
        if (versionFolder != null)
        {
            try
            {
                return PayloadWriter.Ensure(zip, versionFolder, name);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException or InvalidOperationException)
            {
            }
        }

        return Unavailable(name, versionFolder);
    }
}

/// <summary>
/// Writes one payload zip out to a folder and checks it. A sibling &lt;name&gt;.sha256 file lists the
/// zip's content id and every file's SHA-256, so a later start that finds a file changed, missing or
/// added writes the whole folder again.
/// </summary>
public static class PayloadWriter
{
    public const string ManifestSuffix = ".sha256";
    public const string InUsePrefix = "in-use-";

    // How long to wait for another tool that writes the same payload. Tests shorten it.
    internal static TimeSpan LockWait { get; set; } = TimeSpan.FromMinutes(2);

    public static string MutexName(string target) =>
        "Local\\Coodsz.Payload." + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(Path.GetFullPath(target).ToUpperInvariant())))[..32];

    public static string Ensure(Stream zip, string versionFolder, string name)
    {
        var target = Path.Combine(versionFolder, name);
        var manifest = target + ManifestSuffix;
        using var archive = new ZipArchive(zip, ZipArchiveMode.Read, leaveOpen: true);
        var id = ContentId(archive);
        if (IsIntact(target, manifest, id))
            return target;

        // A server tool and a client launcher of one product share the folder, so only one writes at a time.
        using var mutex = new Mutex(false, MutexName(target));
        bool owned;
        try
        {
            owned = mutex.WaitOne(LockWait);
        }
        catch (AbandonedMutexException)
        {
            owned = true;
        }

        // The caller finds the module missing for now and tries again on the next use.
        if (!owned)
            throw new IOException(name + " is being written out by another tool.");
        try
        {
            if (IsIntact(target, manifest, id))
                return target;
            Write(archive, versionFolder, target, manifest, id);
            if (!IsIntact(target, manifest, id))
                throw new InvalidDataException(name + " did not match after it was written out.");
            RemoveOlderVersions(versionFolder);
            return target;
        }
        finally
        {
            mutex.ReleaseMutex();
        }
    }

    /// <summary>
    /// The same for the same files, whenever the zip was made: every entry's path, size and CRC.
    /// </summary>
    public static string ContentId(ZipArchive archive)
    {
        var text = new StringBuilder();
        foreach (var entry in archive.Entries.Where(entry => entry.Name.Length > 0).OrderBy(entry => Normal(entry.FullName), StringComparer.Ordinal))
            text.Append(Normal(entry.FullName)).Append('|').Append(entry.Length).Append('|').Append(entry.Crc32).Append('\n');
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text.ToString())));
    }

    public static bool IsIntact(string target, string manifest, string id)
    {
        try
        {
            if (!File.Exists(manifest) || !Directory.Exists(target))
                return false;
            var lines = File.ReadAllLines(manifest);
            if (lines.Length == 0 || lines[0] != "content " + id)
                return false;
            var listed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var line in lines.Skip(1))
            {
                var parts = line.Split(' ', 3);
                if (parts.Length != 3 || !InsideFolder(target, parts[2]))
                    return false;
                var file = Path.Combine(target, parts[2]);
                var info = new FileInfo(file);
                if (!info.Exists || info.Length.ToString() != parts[1] || FileHash.Sha256(file) != parts[0])
                    return false;
                listed.Add(Normal(parts[2]));
            }

            return Directory.EnumerateFiles(target, "*", SearchOption.AllDirectories)
                .All(file => listed.Contains(Normal(Path.GetRelativePath(target, file))));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    private static void Write(ZipArchive archive, string versionFolder, string target, string manifest, string id)
    {
        Directory.CreateDirectory(versionFolder);
        RemoveAbandonedCopies(versionFolder, Path.GetFileName(target));
        // A name of this process's own, so a copy another tool is still writing is never touched.
        var fresh = target + ".new-" + Environment.ProcessId + "-" + Guid.NewGuid().ToString("N")[..8];
        Directory.CreateDirectory(fresh);
        try
        {
            var root = Path.GetFullPath(fresh) + Path.DirectorySeparatorChar;
            var lines = new List<string> { "content " + id };
            foreach (var entry in archive.Entries.Where(entry => entry.Name.Length > 0))
            {
                var path = Path.GetFullPath(Path.Combine(root, entry.FullName));
                if (Path.IsPathRooted(entry.FullName) || !path.StartsWith(root, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("The payload contains a file outside its folder.");
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                entry.ExtractToFile(path, overwrite: true);
                var relative = Path.GetRelativePath(root, path);
                lines.Add(FileHash.Sha256(path) + " " + new FileInfo(path).Length + " " + relative);
            }

            if (File.Exists(manifest))
                File.Delete(manifest);
            if (Directory.Exists(target))
                Directory.Delete(target, recursive: true);
            Directory.Move(fresh, target);
            File.WriteAllLines(manifest, lines);
            DownloadMark.Clear(target);
        }
        finally
        {
            if (Directory.Exists(fresh))
                TryDelete(fresh);
        }
    }

    // Half written copies a tool left when it was closed in the middle. Only ones whose process is gone.
    private static void RemoveAbandonedCopies(string versionFolder, string name)
    {
        foreach (var folder in Directory.EnumerateDirectories(versionFolder, name + ".new-*"))
        {
            var parts = Path.GetFileName(folder)[(name.Length + ".new-".Length)..].Split('-');
            if (parts.Length == 2 && int.TryParse(parts[0], out var pid) && !IsRunning(pid))
                TryDelete(folder);
        }
    }

    private static bool IsRunning(int pid)
    {
        try
        {
            using var process = Process.GetProcessById(pid);
            return !process.HasExited;
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
        {
            return false;
        }
    }

    /// <summary>
    /// Removes old version folders: only ones older than this version and than the two newest others (a
    /// server tool and a client launcher may be a version or two apart for a while), and never one a running
    /// tool still uses (its in-use lock file cannot be deleted).
    /// </summary>
    public static void RemoveOlderVersions(string versionFolder)
    {
        try
        {
            var full = Path.GetFullPath(versionFolder).TrimEnd('\\', '/');
            var parent = Path.GetDirectoryName(full);
            if (parent == null || !Version.TryParse(Path.GetFileName(full), out var current))
                return;
            var others = new DirectoryInfo(parent).GetDirectories()
                .Where(folder => !folder.FullName.TrimEnd('\\', '/').Equals(full, StringComparison.OrdinalIgnoreCase))
                .Select(folder => (Folder: folder, Version: Version.TryParse(folder.Name, out var version) ? version : null))
                .Where(item => item.Version != null)
                .OrderByDescending(item => item.Version)
                .Skip(2);
            foreach (var (old, version) in others)
            {
                if (version! >= current || InUse(old))
                    continue;
                TryDelete(old.FullName);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }

    // True when a tool that runs this version holds its lock file. A lock file left by a closed tool is removed.
    private static bool InUse(DirectoryInfo folder)
    {
        var used = false;
        foreach (var lockFile in folder.GetFiles(InUsePrefix + "*.lock"))
        {
            try
            {
                lockFile.Delete();
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                used = true;
            }
        }

        return used;
    }

    private static void TryDelete(string folder)
    {
        try
        {
            Directory.Delete(folder, recursive: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // In use; the next write-out tries again.
        }
    }

    /// <summary>
    /// True when a path from a manifest line stays inside the folder: not rooted, no ".." part, and inside
    /// the folder once made full, the same rule Write uses for zip entries.
    /// </summary>
    public static bool InsideFolder(string folder, string relative)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(relative) || Path.IsPathRooted(relative)
                || relative.Split('/', '\\').Any(part => part == ".."))
                return false;
            var root = Path.GetFullPath(folder).TrimEnd('\\', '/') + Path.DirectorySeparatorChar;
            return Path.GetFullPath(Path.Combine(root, relative)).StartsWith(root, StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or IOException)
        {
            return false;
        }
    }

    private static string Normal(string path) => path.Replace('\\', '/');
}
