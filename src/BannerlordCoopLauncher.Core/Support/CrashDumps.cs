using System.IO.Compression;
using System.Text;

namespace BannerlordCoopLauncher.Core.Support;

/// <summary>The one crash dump a report can carry: where it is, its name in the zip, its size and its size compressed.</summary>
public sealed record CrashDumpPick(string Path, string Entry, long Size, long CompressedSize)
{
    /// <summary>The NOTES.txt line that says why this smaller dump went instead of the full one (CrashDumps.Pick), or null.</summary>
    public string? Why { get; init; }
}

/// <summary>What TryAdd or Write did: the dump is in the zip (Pick), or Note says why it is not (null when there was nothing to add).</summary>
public sealed record CrashDumpAddResult(CrashDumpPick? Pick, string? Note)
{
    public bool Added => Pick != null;
}

/// <summary>
/// The crash dump of Send crash report, Send server crash report and Send my logs: only the newest
/// dump of this crash, only of the game (client tools) or the dedicated server (server tools), and
/// only when it compresses into the dump budget. A dump is a memory snapshot and cannot be cleaned,
/// so it goes in only while Add the crash dump is ticked (CrashReportRequest.IncludeDump).
/// </summary>
public static class CrashDumps
{
    /// <summary>The most the compressed dump (with the screenshots) may take. The logs keep the rest of CrashReports.MaxZipBytes, at least 2.5 MB.</summary>
    public const long Budget = 5_000_000;

    /// <summary>Room kept for the dump's zip headers and report.json lines.</summary>
    public const long EntryOverhead = 64_000;

    /// <summary>The biggest dump that is measured at all (512 MB). Bannerlord's minidumps are far smaller, so a bigger one is too big.</summary>
    public const long MaxRawBytes = 512L * 1024 * 1024;

    /// <summary>A dump older than this before the crash (or before Send my logs) is from another problem.</summary>
    public static readonly TimeSpan Window = TimeSpan.FromMinutes(30);

    /// <summary>A dump written this long after the crash was seen is from a later session, not this crash.</summary>
    public static readonly TimeSpan After = TimeSpan.FromMinutes(10);

    public const string Folder = "crash-dumps/";

    public const string CheckboxText = "Add the crash dump";
    public const string CheckboxTip = "Sends the crash dump of this crash with the logs, so Coodsz can see where it crashed. Untick it to send the logs without it.";
    public const string CheckboxNote = "A crash dump is a snapshot of the game's memory and is not cleaned. It can hold your Windows user name, Steam id, the join password, player addresses and anything the game had open. Only Coodsz can open it.";
    public const string TooBigText = "The crash dump was too big to send, so it was left out.";
    public const string TooBigWithScreenshotsText = "The crash dump and the screenshots are too big to send together, so the dump is left out. Remove a screenshot to send it.";
    public const string LeftOutText = "The crash dump could not be added, so it was left out.";

    // NOTES.txt lines for Coodsz, English only like the rest of NOTES.txt.
    public const string AddedNote = "The crash dump {0} is included because Add the crash dump was ticked. It is a memory snapshot and is not cleaned.";
    public const string SmallerNote = "It is the small stack only dump; the full dump {0} was too big to send.";
    public const string BusyNote = "It is the small stack only dump; the full dump {0} was still being written.";

    /// <summary>
    /// The oldest write time a dump may have: for a crash, 30 minutes before the crash was seen
    /// (the tool raised its crash bar); for Send my logs, 30 minutes before now.
    /// </summary>
    public static DateTime Since(string reason, DateTime? crashedAtUtc, DateTime nowUtc) =>
        (reason == CrashReason.Logs ? nowUtc : crashedAtUtc ?? nowUtc) - Window;

    /// <summary>
    /// The newest write time a dump may have: for a crash, 10 minutes after the crash was seen, so a
    /// crash bar left open does not send the dump of a later session; for Send my logs, no limit.
    /// </summary>
    public static DateTime Until(string reason, DateTime? crashedAtUtc) =>
        reason == CrashReason.Logs || crashedAtUtc == null ? DateTime.MaxValue : crashedAtUtc.Value + After;

    /// <summary>
    /// The newest .dmp of these written between sinceUtc and untilUtc that is not empty, or null. Its
    /// size is judged after (TryMeasure), so a newest dump that is too big is called too big instead
    /// of an older one going in its place.
    /// </summary>
    public static string? Latest(IEnumerable<string> dumps, DateTime sinceUtc, DateTime untilUtc) =>
        dumps.Where(path => path.EndsWith(".dmp", StringComparison.OrdinalIgnoreCase))
            .Select(path => (Path: path, Time: LogZipBuilder.SafeWriteTime(path)))
            .Where(dump => dump.Time >= sinceUtc && dump.Time <= untilUtc && SizeOf(dump.Path) > 0)
            .OrderByDescending(dump => dump.Time)
            .Select(dump => dump.Path)
            .FirstOrDefault();

    /// <summary>
    /// The game's dumps: the .dmp files in the game's crash folders (ProgramData\Mount and Blade II
    /// Bannerlord\crashes) that a dedicated server did not write, and the Windows dumps of Bannerlord.exe.
    /// </summary>
    public static IEnumerable<string> ClientDumps(string? engineCrashRoot, string? windowsDumpRoot)
    {
        var server = new HashSet<string>(LogZipBuilder.ServerCrashFolders(engineCrashRoot), StringComparer.OrdinalIgnoreCase);
        return CrashFolders(engineCrashRoot)
            .Where(folder => !server.Contains(folder))
            .SelectMany(folder => LogZipBuilder.Files(folder, "*.dmp", recursive: true, skipLinks: true))
            .Concat(LogZipBuilder.Files(windowsDumpRoot, "Bannerlord.exe*.dmp", skipLinks: true));
    }

    /// <summary>
    /// The dedicated server's dumps: its own crash folders, the Windows dumps of BannerlordCoopServer.exe
    /// (LogZipBuilder.ServerDumps), and the Windows dumps of the engine itself (EngineDotnetDumps).
    /// </summary>
    public static IEnumerable<string> ServerDumps(string? engineCrashRoot, string? windowsDumpRoot, string? engineLogRoot = null) =>
        LogZipBuilder.ServerDumps(LogZipBuilder.ServerCrashFolders(engineCrashRoot), windowsDumpRoot, int.MaxValue)
            .Concat(EngineDotnetDumps(windowsDumpRoot, engineLogRoot));

    /// <summary>
    /// The server engine runs as engine\dotnet\dotnet.exe, so Windows names its dumps dotnet.exe.(pid).dmp,
    /// like any other .NET program's. Only a dump whose process id also wrote a server engine log
    /// (rgl_log_(pid).txt in engineLogRoot, LogZipBuilder.IsServerEngineLog) within After of the dump is the
    /// server's; the game writes logs there too, and Windows can give its pid to another .NET program later.
    /// </summary>
    public static IEnumerable<string> EngineDotnetDumps(string? windowsDumpRoot, string? engineLogRoot)
    {
        if (string.IsNullOrWhiteSpace(engineLogRoot))
            return [];
        return LogZipBuilder.Files(windowsDumpRoot, "dotnet.exe.*.dmp", skipLinks: true)
            .Where(dump =>
            {
                var pid = Path.GetFileNameWithoutExtension(dump)["dotnet.exe.".Length..];
                if (pid.Length == 0 || !pid.All(char.IsAsciiDigit))
                    return false;
                var log = Path.Combine(engineLogRoot, "rgl_log_" + pid + ".txt");
                return File.Exists(log) && (LogZipBuilder.SafeWriteTime(dump) - LogZipBuilder.SafeWriteTime(log)).Duration() <= After
                    && LogZipBuilder.IsServerEngineLog(log);
            });
    }

    /// <summary>The game's newest dump written between sinceUtc and untilUtc (Since and Until), or null.</summary>
    public static string? LatestClientDump(string? engineCrashRoot, string? windowsDumpRoot, DateTime sinceUtc, DateTime untilUtc) =>
        Latest(ClientDumps(engineCrashRoot, windowsDumpRoot), sinceUtc, untilUtc);

    /// <summary>The dedicated server's newest dump written between sinceUtc and untilUtc (Since and Until), or null.</summary>
    public static string? LatestServerDump(string? engineCrashRoot, string? windowsDumpRoot, DateTime sinceUtc, DateTime untilUtc, string? engineLogRoot = null) =>
        Latest(ServerDumps(engineCrashRoot, windowsDumpRoot, engineLogRoot), sinceUtc, untilUtc);

    /// <summary>The dump's name in the zip: crash-dumps/ and its folder and file name, cleaned like every entry name.</summary>
    public static string EntryName(string path)
    {
        var parent = Path.GetFileName(Path.GetDirectoryName(path) ?? "");
        return LogZipBuilder.CleanEntryName(Folder + (parent.Length > 0 ? parent + "-" : "") + Path.GetFileName(path));
    }

    /// <summary>
    /// The dump with its compressed size, or null when it is over MaxRawBytes, compresses to more
    /// than budget, is gone or cannot be read. The compressing stops as soon as the budget is
    /// passed, so a big dump costs no more than a dump that just fits.
    /// </summary>
    public static CrashDumpPick? Measure(string path, long budget = Budget) => TryMeasure(path, budget, out var pick) ? pick : null;

    /// <summary>Measure, and false when the dump cannot be read at all (gone, locked or empty), so it is not called too big.</summary>
    public static bool TryMeasure(string path, long budget, out CrashDumpPick? pick)
    {
        pick = null;
        try
        {
            using var input = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            if (input.Length == 0)
                return false;
            if (input.Length > MaxRawBytes)
                return true;
            var sink = new CountingSink(budget);
            using (var deflate = new DeflateStream(sink, CompressionLevel.Optimal, leaveOpen: true))
            {
                var buffer = new byte[1 << 20];
                int read;
                while (!sink.Over && (read = input.Read(buffer, 0, buffer.Length)) > 0)
                    deflate.Write(buffer, 0, read);
            }

            if (!sink.Over)
                pick = new CrashDumpPick(path, EntryName(path), input.Length, sink.Count);
            return true;
        }
        catch (Exception)
        {
            // Gone, locked or anything else: a dump that cannot be measured never stops the report.
            return false;
        }
    }

    /// <summary>
    /// The dumps of the same crash, newest first: the dump found, then the other dump*.dmp files of its
    /// crash folder. Bannerlord's watchdog writes a small stack only dump.dmp first and the full dump
    /// (with the managed heap) after it into the same folder, so an older one there is the small one.
    /// A Windows dump (Bannerlord.exe.(pid).dmp) shares its folder with other crashes, so it has none.
    /// </summary>
    public static IEnumerable<string> SameCrash(string newest)
    {
        yield return newest;
        if (!Path.GetFileName(newest).StartsWith("dump", StringComparison.OrdinalIgnoreCase))
            yield break;
        foreach (var other in LogZipBuilder.Files(Path.GetDirectoryName(newest), "dump*.dmp", skipLinks: true)
                     .Where(path => !string.Equals(Path.GetFullPath(path), Path.GetFullPath(newest), StringComparison.OrdinalIgnoreCase) && SizeOf(path) > 0)
                     .OrderByDescending(LogZipBuilder.SafeWriteTime))
            yield return other;
    }

    /// <summary>
    /// The dump of this crash that goes, measured: the newest of SameCrash(newest) that fits budget.
    /// A dump still being written (Busy, or its size changed while it was measured) or one that cannot
    /// be read is passed over. A smaller dump that goes in place of the full one carries Why. TooBig
    /// is true when nothing fits and a dump was too big, so the too big line is only written then.
    /// </summary>
    public static (CrashDumpPick? Pick, bool TooBig) Pick(string newest, long budget = Budget)
    {
        string? passed = null;
        var tooBig = false;
        foreach (var path in SameCrash(newest))
        {
            if (Busy(path) || !TryMeasure(path, budget, out var pick) || (pick != null && SizeOf(path) != pick.Size))
            {
                passed ??= path;
                continue;
            }

            if (pick == null)
            {
                passed ??= path;
                tooBig = true;
                continue;
            }

            return passed == null ? (pick, false)
                : (pick with { Why = string.Format(System.Globalization.CultureInfo.InvariantCulture, tooBig ? SmallerNote : BusyNote, EntryName(passed)) }, false);
        }

        return (null, tooBig);
    }

    /// <summary>
    /// True when another program has the dump open for writing, like the watchdog while it still
    /// writes the full dump (it shares it for reading only). A dump that is gone counts as busy too.
    /// </summary>
    public static bool Busy(string path)
    {
        try
        {
            using var probe = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete);
            return false;
        }
        catch (IOException)
        {
            return true;
        }
        catch (UnauthorizedAccessException)
        {
            // Not readable at all: TryMeasure says so.
            return false;
        }
    }

    /// <summary>The NOTES.txt line of a dump that went in: AddedNote, and Why when it is the small dump.</summary>
    public static string AddedLine(CrashDumpPick dump)
    {
        var line = string.Format(System.Globalization.CultureInfo.InvariantCulture, AddedNote, dump.Entry);
        return dump.Why == null ? line : line + " " + dump.Why;
    }

    /// <summary>True when the dump and the player's screenshots and note (extraBytes) fit the dump budget together.</summary>
    public static bool FitsWith(CrashDumpPick dump, long extraBytes) => dump.CompressedSize + Math.Max(0, extraBytes) <= Budget;

    /// <summary>
    /// The most a measured dump may compress to when it is written: what the caller has left
    /// (budget) and no more than the room kept for it, its measured size and half the overhead.
    /// </summary>
    public static long RoomFor(CrashDumpPick dump, long budget) => Math.Min(budget, dump.CompressedSize + EntryOverhead / 2);

    /// <summary>
    /// Adds the measured dump to a written zip without holding it in memory: the zip's entries are
    /// copied into a new zip, the dump is streamed in after them (only its measured length) and the
    /// new zip takes the old one's place. A dump whose length changed since it was measured is
    /// measured again and must still fit room. Any failure leaves the zip as it was and the dump
    /// out, with the line for the notes in Note (TooBigText or LeftOutText).
    /// </summary>
    public static CrashDumpAddResult Write(string zipPath, CrashDumpPick dump, long room)
    {
        var temp = zipPath + ".dump.tmp";
        try
        {
            if (SizeOf(dump.Path) != dump.Size)
            {
                if (!TryMeasure(dump.Path, room, out var again))
                    return new CrashDumpAddResult(null, LeftOutText);
                if (again == null)
                    return new CrashDumpAddResult(null, TooBigText);
                dump = again with { Why = dump.Why };
            }

            File.Delete(temp);
            using (var input = new FileStream(dump.Path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
            using (var target = ZipFile.Open(temp, ZipArchiveMode.Create))
            {
                using (var source = ZipFile.OpenRead(zipPath))
                    foreach (var entry in source.Entries.Where(entry => entry.FullName != dump.Entry))
                    {
                        // A picture is stored as it is, like ReportExtras wrote it.
                        var copy = target.CreateEntry(entry.FullName, entry.Length > 0 && entry.CompressedLength >= entry.Length ? CompressionLevel.NoCompression : CompressionLevel.Optimal);
                        copy.LastWriteTime = entry.LastWriteTime;
                        using var from = entry.Open();
                        using var to = copy.Open();
                        from.CopyTo(to);
                    }

                using var output = target.CreateEntry(dump.Entry, CompressionLevel.Optimal).Open();
                CopyExactly(input, output, dump.Size);
            }

            File.Move(temp, zipPath, overwrite: true);
            return new CrashDumpAddResult(dump, null);
        }
        catch (Exception)
        {
            // Out of memory, a locked or cut dump, a full disk: the report goes without the dump.
            try
            {
                File.Delete(temp);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
            }

            return new CrashDumpAddResult(null, LeftOutText);
        }
    }

    /// <summary>
    /// For a tool's own packer: when includeDump is true (the player ticked Add the crash dump,
    /// CrashReportRequest.IncludeDump), measures the dump and adds it to a written zip when it fits
    /// the budget (or the small dump of the same crash, Pick), with a line in NOTES.txt either way. Nothing
    /// is added or noted when includeDump is false or the path is null. The dump is streamed (Write), so a
    /// big dump never sits in memory.
    /// </summary>
    public static CrashDumpAddResult TryAdd(string zipPath, string? dumpPath, bool includeDump, long budget = Budget)
    {
        if (!includeDump || string.IsNullOrWhiteSpace(dumpPath) || !File.Exists(dumpPath))
            return new CrashDumpAddResult(null, null);
        var (pick, tooBig) = Pick(dumpPath, budget);
        if (pick == null && !tooBig)
            return new CrashDumpAddResult(null, null);
        var result = pick == null ? new CrashDumpAddResult(null, TooBigText) : Write(zipPath, pick, RoomFor(pick, budget));
        AppendNote(zipPath, result.Pick is { } added ? AddedLine(added) : result.Note!);
        return result;
    }

    /// <summary>Adds a line at the end of the zip's NOTES.txt, or makes NOTES.txt with it.</summary>
    public static void AppendNote(string zipPath, string line)
    {
        using var zip = ZipFile.Open(zipPath, ZipArchiveMode.Update);
        var old = "";
        if (zip.GetEntry("NOTES.txt") is { } entry)
        {
            using (var reader = new StreamReader(entry.Open(), Encoding.UTF8))
                old = reader.ReadToEnd();
            entry.Delete();
        }

        using var writer = new StreamWriter(zip.CreateEntry("NOTES.txt", CompressionLevel.Optimal).Open(), new UTF8Encoding(false));
        writer.Write(old.Length == 0 ? line + Environment.NewLine : old.TrimEnd() + Environment.NewLine + line + Environment.NewLine);
    }

    // Copies exactly length bytes, the length that was measured; a dump cut shorter since then throws.
    private static void CopyExactly(Stream input, Stream output, long length)
    {
        var buffer = new byte[81920];
        while (length > 0)
        {
            var read = input.Read(buffer, 0, (int)Math.Min(buffer.Length, length));
            if (read == 0)
                throw new EndOfStreamException("The crash dump got shorter after it was measured.");
            output.Write(buffer, 0, read);
            length -= read;
        }
    }

    // The crash folders, without junctions or symbolic links (LogZipBuilder.NoLinks).
    private static IEnumerable<string> CrashFolders(string? root)
    {
        if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root))
            return Array.Empty<string>();
        try
        {
            return Directory.GetDirectories(root, "*", LogZipBuilder.NoLinks(recursive: false));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return Array.Empty<string>();
        }
    }

    internal static long SizeOf(string path)
    {
        try
        {
            return new FileInfo(path).Length;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return 0;
        }
    }

    // Counts the compressed bytes and drops them; Over once the budget is passed.
    private sealed class CountingSink(long limit) : Stream
    {
        public long Count { get; private set; }
        public bool Over => Count > limit;

        public override void Write(byte[] buffer, int offset, int count) => Count += count;
        public override void Write(ReadOnlySpan<byte> buffer) => Count += buffer.Length;
        public override bool CanRead => false;
        public override bool CanSeek => false;
        public override bool CanWrite => true;
        public override long Length => Count;
        public override long Position { get => Count; set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
    }
}
