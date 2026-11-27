using System.IO.Compression;
using System.Text;
using System.Text.RegularExpressions;

namespace BannerlordCoopLauncher.Core.Support;

public sealed record LogZipResult(int Included, IReadOnlyList<string> Skipped, bool DumpIncluded);

// How a zip is packed. The default is the full pack Save a copy used to make. A crash report
// (CrashReports) packs text only, keeps a shorter end of each file, and also takes out the
// passwords the tool knows (Secrets) wherever they appear.
public sealed record LogZipLimits(bool TextOnly = false, long MaxTextBytes = LogZipBuilder.MaxTextBytes)
{
    public IReadOnlyList<string> Secrets { get; init; } = Array.Empty<string>();

    /// <summary>When set, every text file written notes whether it was cleaned and cut (the crash report manifest).</summary>
    public LogZipTrace? Trace { get; init; }

    public static LogZipLimits Full { get; } = new();
}

// Which files of a zip the cleaner changed (redacted) and which keep only their end (truncated).
public sealed class LogZipTrace
{
    private readonly Dictionary<string, (bool Redacted, bool Truncated)> _entries = new(StringComparer.OrdinalIgnoreCase);

    public void Mark(string entry, bool redacted, bool truncated)
    {
        lock (_entries)
        {
            var old = _entries.GetValueOrDefault(entry);
            _entries[entry] = (old.Redacted || redacted, old.Truncated || truncated);
        }
    }

    public (bool Redacted, bool Truncated) Get(string entry)
    {
        lock (_entries)
            return _entries.GetValueOrDefault(entry);
    }

    // A copy, so a later pass can mark more files without changing this one.
    public LogZipTrace Copy()
    {
        var copy = new LogZipTrace();
        lock (_entries)
            foreach (var (entry, marks) in _entries)
                copy._entries[entry] = marks;
        return copy;
    }
}

// Collects log files for one zip a player or host can send. Text files are
// cleaned (addresses, Steam ids, passwords) and cut to their last 20 MB.
// Saves are never added. Crash dumps are copied as they are, only when asked.
public sealed class LogZipBuilder
{
    public const long MaxTextBytes = 20_000_000;
    public const long MaxDumpBytes = 2_000_000_000;

    // The shortest known password that is taken out wherever it appears. Shorter ones would
    // cut ordinary words out of the logs; the password patterns still catch them.
    public const int MinSecretLength = 4;

    private readonly LogZipLimits _limits;
    private bool _dumpsNoted;

    public LogZipBuilder(LogZipLimits? limits = null) => _limits = limits ?? LogZipLimits.Full;

    public LogZipLimits Limits => _limits;

    private readonly List<(string Source, string Entry, bool Binary)> _files = new();
    private readonly List<(string Entry, string Text)> _texts = new();
    private readonly List<(string Source, string Folder)> _archives = new();
    private readonly List<string> _skipped = new();
    // Entries that keep a shorter end than the zip's own MaxTextBytes (AddFile with maxTextBytes).
    private readonly Dictionary<string, long> _caps = new(StringComparer.OrdinalIgnoreCase);

    public IReadOnlyList<string> Skipped => _skipped;
    public IEnumerable<string> Entries => _files.Select(file => file.Entry).Concat(_texts.Select(text => text.Entry)).Concat(_archives.Select(archive => archive.Folder + "/"));

    public void AddFile(string? path, string entry)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            return;
        if (IsLeftOut(path))
        {
            _skipped.Add(Path.GetFileName(path) + " was left out.");
            return;
        }

        if (new FileInfo(path).Length == 0 || HasEntry(entry))
            return;
        _files.Add((path, entry, false));
    }

    // A text file that keeps only its last maxTextBytes, for one log that grows far past what is
    // useful (a 16 MB BootPatches.log). The zip's own limit still applies when it is smaller.
    public void AddFile(string? path, string entry, long maxTextBytes)
    {
        if (maxTextBytes > 0 && !string.IsNullOrWhiteSpace(path) && File.Exists(path) && !HasEntry(entry))
            _caps[entry] = maxTextBytes;
        AddFile(path, entry);
    }

    public void AddNewest(IEnumerable<string> paths, string folder, int count)
    {
        foreach (var path in paths.OrderByDescending(SafeWriteTime).Take(count))
            AddFile(path, folder + "/" + Path.GetFileName(path));
    }

    public void AddText(string entry, string? text)
    {
        if (string.IsNullOrWhiteSpace(text) || HasEntry(entry))
            return;
        _texts.Add((entry, text));
    }

    // A crash dump, copied byte for byte. It holds program memory, so callers
    // only add dumps after the person agreed.
    public void AddDump(string path, string entry)
    {
        if (!File.Exists(path) || HasEntry(entry))
            return;
        if (_limits.TextOnly)
        {
            NoteDumpsLeftOut();
            return;
        }

        if (new FileInfo(path).Length > MaxDumpBytes)
        {
            _skipped.Add(Path.GetFileName(path) + " was left out because it is larger than 2 GB.");
            return;
        }

        _files.Add((path, entry, true));
    }

    // A zip another tool wrote (TAOM's taom_crash stall reports). It is unpacked into this
    // folder: text files are cleaned like any other log, saves and shader caches are left
    // out, and anything else (a dump) is copied as it is.
    public void AddZipContents(string path, string folder)
    {
        if (!File.Exists(path) || _archives.Any(archive => archive.Folder.Equals(folder, StringComparison.OrdinalIgnoreCase)))
            return;
        _archives.Add((path, folder));
    }

    public void Skip(string line) => _skipped.Add(line);

    private void NoteDumpsLeftOut()
    {
        if (_dumpsNoted)
            return;
        _dumpsNoted = true;
        _skipped.Add(DumpsLeftOutText);
    }

    // A crash report packs text only; the one dump it may carry is added later (CrashDumps).
    public const string DumpsLeftOutText = "Crash dumps and other non text files were left out. They can hold the game's memory.";

    private string CutNote => CutNoteFor(_limits.MaxTextBytes);

    private static string CutNoteFor(long bytes) => bytes == MaxTextBytes
        ? "[Only the last 20 MB of this file is included.]"
        : "[Only the last " + SizeText(bytes) + " of this file is included.]";

    // The end of a file the zip keeps: the zip's own limit, or a shorter one set by AddFile.
    private long CapFor(string entry) =>
        _caps.TryGetValue(entry, out var cap) ? Math.Min(cap, _limits.MaxTextBytes) : _limits.MaxTextBytes;

    public static string SizeText(long bytes) => bytes >= 1_000_000
        ? (bytes / 1_000_000.0).ToString(bytes % 1_000_000 == 0 ? "0" : "0.#", System.Globalization.CultureInfo.InvariantCulture) + " MB"
        : Math.Max(1, (bytes + 999) / 1000).ToString(System.Globalization.CultureInfo.InvariantCulture) + " KB";

    public LogZipResult Write(string zipPath, string notes)
    {
        var directory = Path.GetDirectoryName(zipPath);
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);
        if (File.Exists(zipPath))
            File.Delete(zipPath);

        var written = new List<string>();
        var dump = false;
        using (var zip = ZipFile.Open(zipPath, ZipArchiveMode.Create))
        {
            foreach (var file in _files)
            {
                var name = EntryName(file.Entry, written);
                if (file.Binary)
                {
                    if (CopyBinary(zip, file.Source, name))
                    {
                        written.Add(name);
                        dump = true;
                    }

                    continue;
                }

                var cap = CapFor(file.Entry);
                var text = ReadTail(file.Source, cap);
                if (text == null)
                    continue;
                if (WriteCleaned(zip, name, text, text.StartsWith(CutNoteFor(cap), StringComparison.Ordinal)))
                    written.Add(name);
            }

            foreach (var text in _texts)
            {
                var name = EntryName(text.Entry, written);
                if (WriteCleaned(zip, name, text.Text, truncated: false))
                    written.Add(name);
            }

            foreach (var archive in _archives)
                dump |= CopyArchive(zip, archive.Source, archive.Folder, written);

            var builder = new StringBuilder(notes.TrimEnd());
            builder.AppendLine();
            builder.AppendLine();
            builder.AppendLine("Files:");
            foreach (var entry in written)
                builder.AppendLine(entry);
            if (_duplicates > 0)
                _skipped.Add(_duplicates == 1
                    ? "1 file was the same as another file in this zip, so only one copy is included."
                    : _duplicates + " files were the same as other files in this zip, so only one copy of each is included.");
            if (_skipped.Count > 0)
            {
                builder.AppendLine();
                builder.AppendLine("Not included:");
                foreach (var line in _skipped)
                    builder.AppendLine(line);
            }

            // The notes name folders (Documents, crash reports), so they are cleaned like the logs.
            if (!WriteCleaned(zip, "NOTES.txt", builder.ToString(), truncated: false))
                WriteText(zip, "NOTES.txt", LogScrubber.TimedOutText);
        }

        return new LogZipResult(written.Count, _skipped, dump);
    }

    // Removes addresses, Steam ids, names and passwords. Any JSON key with "password"
    // in its name keeps the key and loses the value. The game's command line, which the
    // engine and watchdog logs repeat, loses the value after /cooppassword. LogScrubber has
    // the rest (the known passwords, IPv6, other Steam id forms, the PC's names, tokens).
    public static string Clean(string text) => Clean(text, null);

    // Never throws: a text a rule takes too long on becomes LogScrubber.TimedOutText.
    public static string Clean(string text, IEnumerable<string>? secrets) =>
        TryClean(text, secrets, out var cleaned) ? cleaned : LogScrubber.TimedOutText;

    // False when a rule took too long. The file is then left out, never packed half cleaned.
    public static bool TryClean(string text, IEnumerable<string>? secrets, out string cleaned)
    {
        try
        {
            cleaned = CleanOrThrow(text, secrets);
            return true;
        }
        catch (RegexMatchTimeoutException)
        {
            cleaned = "";
            return false;
        }
    }

    /// <summary>Tests only: runs before the rules on every text, so a test can make one take too long.</summary>
    public static Action<string>? BeforeCleanForTests { get; set; }

    private static string CleanOrThrow(string text, IEnumerable<string>? secrets)
    {
        BeforeCleanForTests?.Invoke(text);
        // The known passwords go first, before another rule cuts one in two.
        var cleaned = ProblemReport.Redact(LogScrubber.ScrubOrThrow(text, secrets));
        cleaned = Regex.Replace(
            cleaned,
            "(?i)(/cooppassword\\s+)(\"[^\"\\r\\n]*\"|[^\\s\"]+)",
            match => match.Groups[1].Value + "[password]",
            RegexOptions.None,
            TimeSpan.FromSeconds(2));
        return Regex.Replace(
            cleaned,
            "(?i)(\"[^\"\\r\\n]*password[^\"\\r\\n]*\"\\s*:\\s*\")((?:\\\\.|[^\"\\\\])*)(\")",
            match => match.Groups[1].Value + (match.Groups[2].Value.Length == 0 ? "" : "[password]") + match.Groups[3].Value,
            RegexOptions.None,
            TimeSpan.FromSeconds(2));
    }

    // skipLinks: junctions and symbolic links are neither followed nor listed (NoLinks).
    public static IEnumerable<string> Files(string? directory, string pattern, bool recursive = false, bool skipLinks = false)
    {
        if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory))
            return Array.Empty<string>();
        try
        {
            return skipLinks
                ? Directory.GetFiles(directory, pattern, NoLinks(recursive))
                : Directory.GetFiles(directory, pattern, recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return Array.Empty<string>();
        }
    }

    // Engine logs written by a dedicated server (the start of the file names it).
    // Falls back to the newest logs when none is from a server.
    public static IReadOnlyList<string> ServerEngineLogs(string? engineLogRoot, int count)
    {
        var logs = Files(engineLogRoot, "rgl_log_*.txt").OrderByDescending(SafeWriteTime).ToList();
        var server = logs.Where(path => StartContains(path, "DedicatedServer") || StartContains(path, "Win64_Shipping_Server")).Take(count).ToList();
        return server.Count > 0 ? server : logs.Take(count).ToList();
    }

    // Coop's own server log, newest first. Coop writes Coop_server.log into the folder the engine runs
    // from, which Start-ModdedServer.ps1 sets to engine\bin\Win64_Shipping_Server, and a second server
    // started from the same folder writes Coop_server_(pid).log there instead. It is the only log that
    // names the network handle behind a stuck join. The extension check keeps the .log.lock files out.
    public static IReadOnlyList<string> CoopServerLogs(string? serverRoot, int count) =>
        string.IsNullOrWhiteSpace(serverRoot)
            ? Array.Empty<string>()
            : Files(Launch.DedicatedEngineLaunch.ServerBin(serverRoot), "Coop_server*.log", skipLinks: true)
                .Where(path => Path.GetExtension(path).Equals(".log", StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(SafeWriteTime)
                .Take(count)
                .ToList();

    // The newest CoopServerLogCount of them, each cut to its last CoopServerLogBytes so a long run
    // cannot crowd the other logs out when the pack is fitted to the send limit.
    public const int CoopServerLogCount = 4;
    public const long CoopServerLogBytes = 400_000;

    // A log already in the zip (two servers in one server folder, or picked up elsewhere) is not added twice.
    public void AddCoopServerLogs(string? serverRoot, string folder)
    {
        foreach (var log in CoopServerLogs(serverRoot, CoopServerLogCount))
        {
            if (!HasSource(log))
                AddFile(log, folder + "/" + Path.GetFileName(log), CoopServerLogBytes);
        }
    }

    // The update helper writes these into the tool's own folder (ToolUpdate and ToolRename). Either may be
    // missing: update-skipped.log only exists once an update was skipped. Each keeps its last UpdateLogBytes.
    public static readonly IReadOnlyList<string> UpdateLogNames = ["update-install.log", "update-skipped.log"];
    public const long UpdateLogBytes = 200_000;

    // toolFolder: the folder of the running exe; null takes it from the app itself.
    public void AddToolUpdateLogs(string? toolFolder, string folder)
    {
        var root = string.IsNullOrWhiteSpace(toolFolder) ? AppContext.BaseDirectory : toolFolder;
        foreach (var name in UpdateLogNames)
            AddFile(Path.Combine(root, name), folder + "/" + name, UpdateLogBytes);
    }

    private bool HasSource(string path)
    {
        var full = Path.GetFullPath(path);
        return _files.Any(file => string.Equals(Path.GetFullPath(file.Source), full, StringComparison.OrdinalIgnoreCase));
    }

    // The crash folders and dumps are listed without following a junction or symbolic link, so a
    // link under ProgramData\Mount and Blade II Bannerlord\crashes cannot pull in files from elsewhere.
    public static EnumerationOptions NoLinks(bool recursive) => new()
    {
        RecurseSubdirectories = recursive,
        AttributesToSkip = FileAttributes.ReparsePoint,
        IgnoreInaccessible = true,
        MatchType = MatchType.Win32
    };

    // Bannerlord crash folders made by a dedicated server, newest first.
    public static IReadOnlyList<string> ServerCrashFolders(string? crashRoot)
    {
        if (string.IsNullOrWhiteSpace(crashRoot) || !Directory.Exists(crashRoot))
            return Array.Empty<string>();
        try
        {
            return Directory.GetDirectories(crashRoot, "*", NoLinks(recursive: false))
                .Where(folder => Files(folder, "*.txt", recursive: true, skipLinks: true).Any(file => StartContains(file, "Win64_Shipping_Server")))
                .OrderByDescending(Directory.GetLastWriteTimeUtc)
                .ToList();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return Array.Empty<string>();
        }
    }

    // Each crash folder holds files with the same names (crash_tags.txt and so
    // on), so each one keeps its own folder in the zip.
    public void AddCrashReports(IEnumerable<string> folders)
    {
        foreach (var folder in folders)
        {
            var name = Path.GetFileName(folder);
            foreach (var file in Files(folder, "*.txt", recursive: true, skipLinks: true)
                         .Concat(Files(folder, "*.xml", recursive: true, skipLinks: true))
                         .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                         .Take(10))
                AddFile(file, "crashes/" + name + "/" + Path.GetFileName(file));
        }
    }

    // Dumps from the server's own crash folders, and Windows dumps of
    // BannerlordCoopServer.exe. Other programs' dumps are never taken.
    public static IReadOnlyList<string> ServerDumps(IReadOnlyList<string> serverCrashFolders, string? windowsDumpRoot, int count = 2) =>
        serverCrashFolders.SelectMany(folder => Files(folder, "*.dmp", recursive: true, skipLinks: true))
            .Concat(Files(windowsDumpRoot, "BannerlordCoopServer.exe*.dmp", skipLinks: true))
            .OrderByDescending(SafeWriteTime)
            .Take(count)
            .ToList();

    public void AddDumps(IEnumerable<string> dumps)
    {
        foreach (var dump in dumps)
        {
            var parent = Path.GetFileName(Path.GetDirectoryName(dump) ?? "");
            AddDump(dump, "crash-dumps/" + (parent.Length > 0 ? parent + "-" : "") + Path.GetFileName(dump));
        }
    }

    public static string WindowsDumpRoot() =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CrashDumps");

    /// <summary>Documents\Mount and Blade II Bannerlord\Configs\ModLogs, where CoopModPatch writes its logs.</summary>
    public static string ModLogsRoot() =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Mount and Blade II Bannerlord", "Configs", "ModLogs");

    public const string CoopModPatchFolder = "coopmodpatch";

    // CoopModPatch's own logs and the Improved Garrisons settings it keeps in a server's
    // server-data folder, when they exist. Cleaned and cut like every other log. The first
    // server-data folder that has the settings file gives it.
    public void AddCoopModPatch(string? modLogsRoot, IEnumerable<string?> serverDataRoots)
    {
        if (!string.IsNullOrWhiteSpace(modLogsRoot))
            foreach (var name in new[] { "CoopModPatch-server.log", "CoopModPatch-client.log" })
                AddFile(Path.Combine(modLogsRoot, name), CoopModPatchFolder + "/" + name);
        foreach (var root in serverDataRoots)
            if (!string.IsNullOrWhiteSpace(root))
                AddFile(IgSettingsPath(root), CoopModPatchFolder + "/ig-settings.txt");
    }

    /// <summary>server-data\coopmodpatch\settings\ig-settings.txt under one server-data folder.</summary>
    public static string IgSettingsPath(string serverDataRoot) =>
        Path.Combine(serverDataRoot, "coopmodpatch", "settings", "ig-settings.txt");

    public static string EngineCrashRoot() =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "Mount and Blade II Bannerlord", "crashes");

    public static DateTime SafeWriteTime(string path)
    {
        try
        {
            return File.GetLastWriteTimeUtc(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return DateTime.MinValue;
        }
    }

    private static bool StartContains(string path, string value, int length = 16000)
    {
        try
        {
            using var reader = new StreamReader(new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite));
            var buffer = new char[length];
            var count = reader.ReadBlock(buffer, 0, buffer.Length);
            return new string(buffer, 0, count).Contains(value, StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    private readonly HashSet<string> _textHashes = new(StringComparer.Ordinal);
    private int _duplicates;

    // The folder a zip's contents go into is named after the zip (crash-reports/x/shareable.zip):
    // its parent is the crash folder that holds the zip, or null for a zip that is not in one.
    private static string? ParentFolder(string folder)
    {
        var cut = folder.LastIndexOf('/');
        return cut > 0 && folder.EndsWith(".zip", StringComparison.OrdinalIgnoreCase) ? folder[..cut] : null;
    }

    private bool HasEntry(string entry) =>
        _files.Any(file => file.Entry.Equals(entry, StringComparison.OrdinalIgnoreCase))
        || _texts.Any(text => text.Entry.Equals(entry, StringComparison.OrdinalIgnoreCase));

    private static bool IsLeftOut(string path)
    {
        var name = Path.GetFileName(path);
        return name.EndsWith(".sav", StringComparison.OrdinalIgnoreCase)
            || name.EndsWith(".dmp", StringComparison.OrdinalIgnoreCase)
            || name.EndsWith(".sack", StringComparison.OrdinalIgnoreCase);
    }

    private string? ReadTail(string path, long maxBytes)
    {
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            var cut = stream.Length > maxBytes;
            // The encoding comes from the first bytes, before any seek, so a UTF-16 log that is cut
            // still reads as text and the cleaning rules still match it.
            var (encoding, bomLength) = DetectEncoding(stream);
            if (cut)
            {
                var start = stream.Length - maxBytes;
                // UTF-16 keeps two bytes per unit: start on a unit boundary after the BOM.
                if (encoding is UnicodeEncoding && (start - bomLength) % 2 != 0)
                    start++;
                stream.Seek(Math.Max(start, bomLength), SeekOrigin.Begin);
            }
            else
            {
                stream.Seek(bomLength, SeekOrigin.Begin);
            }

            using var reader = new StreamReader(stream, encoding, detectEncodingFromByteOrderMarks: false);
            var text = reader.ReadToEnd();
            return cut ? CutNoteFor(maxBytes) + Environment.NewLine + text : text;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _skipped.Add(Path.GetFileName(path) + " could not be read: " + ex.Message);
            return null;
        }
    }

    // The encoding a log is written in, from its byte order mark (UTF-8 when there is none), and
    // the length of that mark. Leaves the stream at its start.
    public static (Encoding Encoding, int BomLength) DetectEncoding(Stream stream)
    {
        var head = new byte[3];
        stream.Seek(0, SeekOrigin.Begin);
        var count = 0;
        while (count < head.Length)
        {
            var read = stream.Read(head, count, head.Length - count);
            if (read == 0)
                break;
            count += read;
        }

        stream.Seek(0, SeekOrigin.Begin);
        if (count >= 3 && head[0] == 0xEF && head[1] == 0xBB && head[2] == 0xBF)
            return (new UTF8Encoding(false), 3);
        if (count >= 2 && head[0] == 0xFF && head[1] == 0xFE)
            return (new UnicodeEncoding(bigEndian: false, byteOrderMark: false), 2);
        if (count >= 2 && head[0] == 0xFE && head[1] == 0xFF)
            return (new UnicodeEncoding(bigEndian: true, byteOrderMark: false), 2);
        return (new UTF8Encoding(false), 0);
    }

    private bool CopyBinary(ZipArchive zip, string source, string entryName)
    {
        try
        {
            using var input = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            var entry = zip.CreateEntry(entryName, CompressionLevel.Fastest);
            using var output = entry.Open();
            input.CopyTo(output);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _skipped.Add(Path.GetFileName(source) + " could not be read: " + ex.Message);
            return false;
        }
    }

    // True when a dump came out of the zip.
    private bool CopyArchive(ZipArchive zip, string source, string folder, List<string> written)
    {
        var dump = false;
        try
        {
            using var input = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var inner = new ZipArchive(input, ZipArchiveMode.Read);
            foreach (var item in inner.Entries)
            {
                if (string.IsNullOrEmpty(item.Name))
                    continue;
                var relative = string.Join("/", item.FullName.Replace('\\', '/').Split('/', StringSplitOptions.RemoveEmptyEntries).Where(part => part != ".." && part != "."));
                if (HasEntry(folder + "/" + relative))
                    continue;
                // A zip inside a crash folder (the game's shareable.zip) repeats the folder's own
                // files: a file the folder already gave is not packed a second time.
                if (ParentFolder(folder) is { } parent && written.Contains(CleanEntryName(parent + "/" + relative, _limits.Secrets), StringComparer.OrdinalIgnoreCase))
                    continue;
                var entry = EntryName(folder + "/" + relative, written);
                var name = item.Name;
                if (name.Contains(".sav", StringComparison.OrdinalIgnoreCase)
                    || name.EndsWith(".sack", StringComparison.OrdinalIgnoreCase))
                {
                    _skipped.Add(name + " in " + Path.GetFileName(source) + " was left out.");
                    continue;
                }

                if (IsTextName(name))
                {
                    if (item.Length > MaxDumpBytes)
                    {
                        _skipped.Add(name + " in " + Path.GetFileName(source) + " was left out because it is larger than 2 GB.");
                        continue;
                    }

                    using var reader = new StreamReader(item.Open(), Encoding.UTF8);
                    var text = reader.ReadToEnd();
                    var cut = text.Length > _limits.MaxTextBytes;
                    if (cut)
                        text = CutNote + Environment.NewLine + text[^(int)_limits.MaxTextBytes..];
                    if (WriteCleaned(zip, entry, text, cut))
                        written.Add(entry);
                    continue;
                }

                if (_limits.TextOnly)
                {
                    NoteDumpsLeftOut();
                    continue;
                }

                if (item.Length > MaxDumpBytes)
                {
                    _skipped.Add(name + " in " + Path.GetFileName(source) + " was left out because it is larger than 2 GB.");
                    continue;
                }

                var copy = zip.CreateEntry(entry, CompressionLevel.Fastest);
                using (var from = item.Open())
                using (var to = copy.Open())
                    from.CopyTo(to);
                written.Add(entry);
                dump |= name.EndsWith(".dmp", StringComparison.OrdinalIgnoreCase);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            _skipped.Add(Path.GetFileName(source) + " could not be read: " + ex.Message);
        }

        return dump;
    }

    public static bool IsTextName(string name) =>
        name.EndsWith(".txt", StringComparison.OrdinalIgnoreCase)
        || name.EndsWith(".log", StringComparison.OrdinalIgnoreCase)
        || name.EndsWith(".xml", StringComparison.OrdinalIgnoreCase)
        || name.EndsWith(".json", StringComparison.OrdinalIgnoreCase)
        || name.EndsWith(".html", StringComparison.OrdinalIgnoreCase)
        || name.EndsWith(".csv", StringComparison.OrdinalIgnoreCase)
        || name.EndsWith(".ini", StringComparison.OrdinalIgnoreCase)
        || name.EndsWith(".cfg", StringComparison.OrdinalIgnoreCase)
        || name.EndsWith(".md", StringComparison.OrdinalIgnoreCase);

    // An engine log a dedicated server wrote: its command line starts the dedicated custom
    // server. Every client log names DedicatedServer.Windows in its module list, so that
    // name alone does not tell them apart.
    public static bool IsServerEngineLog(string path) =>
        StartContains(path, "/dedicatedcustomserver", 96_000) || StartContains(path, "Win64_Shipping_Server", 96_000);

    // A watchdog log of a dedicated server says "Server Build: True" and the server build name.
    public static bool IsServerWatchdog(string path) =>
        StartContains(path, "Win64_Shipping_Server", 16_000) || StartContains(path, "/dedicatedcustomserver", 16_000);

    // Cleans the text, writes it and notes for the crash report manifest whether the cleaning
    // changed anything and whether only the end of the file was kept.
    // A file a cleaning rule takes too long on is left out (false) and noted, never packed half cleaned.
    private bool WriteCleaned(ZipArchive zip, string name, string text, bool truncated)
    {
        if (!TryClean(text, _limits.Secrets, out var cleaned))
        {
            _skipped.Add(Path.GetFileName(name) + " could not be cleaned in time, so it was left out.");
            return false;
        }

        // The same log found in two places (a crash folder and the engine logs) goes in once.
        if (!name.Equals("NOTES.txt", StringComparison.OrdinalIgnoreCase)
            && !_textHashes.Add(Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(cleaned)))))
        {
            _duplicates++;
            return false;
        }

        _limits.Trace?.Mark(name, !string.Equals(cleaned, text, StringComparison.Ordinal), truncated);
        WriteText(zip, name, cleaned);
        return true;
    }

    // The name a file gets in the zip: cleaned like the logs (no user or PC name, Steam id or
    // password), plain characters only, and not the same as a name already written.
    private string EntryName(string entry, IReadOnlyCollection<string> written)
    {
        var name = CleanEntryName(entry, _limits.Secrets);
        if (name.Length == 0)
            name = "file";
        var unique = name;
        for (var i = 2; written.Contains(unique, StringComparer.OrdinalIgnoreCase); i++)
            unique = Path.ChangeExtension(name, null) + "-" + i + Path.GetExtension(name);
        return unique;
    }

    /// <summary>
    /// A zip entry name as it may be sent: folders kept, no drive, backslash or .. part, each part
    /// cleaned like the logs (the PC's user and PC names, Steam ids, passwords) and then reduced to
    /// letters, digits, spaces and _ . - + ( ) [ ]. At most 200 characters, the end kept.
    /// </summary>
    public static string CleanEntryName(string? name, IEnumerable<string>? secrets = null)
    {
        var secretList = secrets?.ToList();
        // A Steam id becomes the plain word steamid first, so a folder named after one stays one word.
        var whole = Regex.Replace((name ?? "").Replace('\\', '/'), @"7656119\d{10}", "steamid");
        // The whole path is cleaned at once, so a Users/Admin path loses the user name even when
        // the name is a common word only the path rule takes out.
        if (!TryClean(whole, secretList, out var cleanedWhole))
            cleanedWhole = "file";
        var parts = cleanedWhole.Split('/', StringSplitOptions.RemoveEmptyEntries)
            .Where(part => part != "." && part != "..")
            .Select(part => Regex.Replace(part, @"[^\p{L}\p{N}_.\-+ ()\[\]]", "_"))
            .Where(part => part.Length > 0 && part != "." && part != "..");
        var clean = string.Join("/", parts);
        clean = Regex.Replace(clean, @"7656119\d{10}", "steamid");
        return clean.Length > 200 ? clean[^200..].TrimStart('/') : clean;
    }

    private static void WriteText(ZipArchive zip, string name, string text)
    {
        var entry = zip.CreateEntry(name, CompressionLevel.Optimal);
        using var writer = new StreamWriter(entry.Open(), new UTF8Encoding(false));
        writer.Write(text);
    }
}
