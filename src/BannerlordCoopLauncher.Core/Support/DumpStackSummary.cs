using System.Buffers.Binary;
using System.Collections.Immutable;
using System.Globalization;
using System.Reflection.PortableExecutable;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Diagnostics.Runtime;

namespace BannerlordCoopLauncher.Core.Support;

/// <summary>What DumpStackSummary read out of one dump. Every part can be missing; Format says so.</summary>
public sealed class DumpStackInfo
{
    public string DumpName { get; set; } = "";
    public long DumpSize { get; set; }
    public DateTime? WrittenUtc { get; set; }

    public uint? ExceptionCode { get; set; }
    public ulong? ExceptionAddress { get; set; }
    public IReadOnlyList<ulong> ExceptionParameters { get; set; } = Array.Empty<ulong>();
    public uint? ThreadId { get; set; }

    /// <summary>The module the exception address is in, as name+0xoffset, or null.</summary>
    public string? FaultLocation { get; set; }

    /// <summary>The managed frames of the faulting thread, newest first, already formatted.</summary>
    public IReadOnlyList<string>? ManagedFrames { get; set; }

    /// <summary>Why there are no managed frames (no .NET runtime, no matching DAC on this PC), or null.</summary>
    public string? ManagedUnavailable { get; set; }

    /// <summary>The likely return addresses on the faulting thread's stack, as module+0xoffset or a managed method.</summary>
    public IReadOnlyList<string>? StackScan { get; set; }

    /// <summary>The current managed exception (type: message), and its inner exceptions after it.</summary>
    public IReadOnlyList<string> ManagedExceptions { get; set; } = Array.Empty<string>();

    /// <summary>
    /// The thread ManagedExceptions came from when it is not the faulting thread (a native crash, or a
    /// dump with no exception record), or null. ClrMD's current exception can be an old one, so that
    /// thread is listed on its own and never called the crash.
    /// </summary>
    public string? ExceptionThread { get; set; }

    /// <summary>The managed frames of ExceptionThread, newest first, already formatted.</summary>
    public IReadOnlyList<string> ExceptionThreadFrames { get; set; } = Array.Empty<string>();

    public IReadOnlyList<(string Thread, IReadOnlyList<string> Frames)> OtherThreads { get; set; } = Array.Empty<(string, IReadOnlyList<string>)>();
    public IReadOnlyList<(string Name, string? Version)> Modules { get; set; } = Array.Empty<(string, string?)>();

    /// <summary>How much of the dump is there when the file is shorter than its own directory says (a cut copy), or null.</summary>
    public string? CutShort { get; set; }

    /// <summary>Why the dump could not be opened at all past its header, or null.</summary>
    public string? OpenError { get; set; }

    /// <summary>Why the reader process gave nothing past the exception record (ReadInChild), or null.</summary>
    public string? Unavailable { get; set; }

    public bool TimedOut { get; set; }
    public TimeSpan Limit { get; set; }
}

/// <summary>
/// crash-stack.txt: a text summary of a crash dump that is already on this PC (a watchdog dump in the
/// game's crash folder or a Windows dump of Bannerlord.exe or the server's dotnet.exe), so a report
/// says where it crashed even when the dump is far too big to send. It only reads dump files; it never
/// opens, attaches to or dumps a running process. ClrMD reads the dump and the managed frames with the
/// .NET runtime's own DAC from this PC, never from a symbol server. No raw memory goes into the text,
/// only exception codes, module names with offsets, method names, the exception message and versions.
/// The caller cleans the text like the logs before packing it.
/// </summary>
public static partial class DumpStackSummary
{
    public const string FileName = "crash-stack.txt";

    /// <summary>The most a read may take. The report goes on with what was read by then.</summary>
    public static readonly TimeSpan Limit = TimeSpan.FromSeconds(20);

    // Written before the player picks whether the dump goes, so it does not say; the dump's own line in NOTES.txt does.
    public const string Note = "crash-stack.txt is a text summary read from the crash dump on this PC, sent with or without the dump.";

    /// <summary>The first line of every crash-stack.txt.</summary>
    internal const string SummaryTitle = "Crash stack summary read from the crash dump on this PC.";

    /// <summary>How the line starts when the reader process gave nothing past the exception record.</summary>
    internal const string UnavailableLine = "(crash stack unavailable: ";

    /// <summary>How the line starts when the dump file is shorter than its own directory says.</summary>
    internal const string CutShortLine = "The dump file is cut short, so parts of it are missing (";

    /// <summary>How the line starts when the read hit its time limit.</summary>
    internal const string StoppedLine = "The read stopped after ";

    private const string FaultingFramesTitle = "Faulting thread, managed frames (newest first):";
    private const string ScanTitle = "Faulting thread, stack scan (likely return addresses, newest first; some can be left over from earlier calls):";
    private const string ScanNoneLine = "  (none found)";

    /// <summary>
    /// True when the text has no stack: the reader gave nothing past the exception record (UnavailableLine),
    /// or the read hit its time limit before any frames (the faulting thread's, another thread's, or a stack
    /// scan address). CrashReports.AddCrashStackSummary then tries one more dump of the crash.
    /// </summary>
    internal static bool NoStack(string text) =>
        text.Contains(UnavailableLine, StringComparison.Ordinal)
        || (text.Contains(StoppedLine, StringComparison.Ordinal)
            && !text.Contains(FaultingFramesTitle, StringComparison.Ordinal)
            // The exception thread's and the other threads' frames are the only lines indented by four.
            && !text.Contains("\r\n    ", StringComparison.Ordinal)
            && !(text.Contains(ScanTitle, StringComparison.Ordinal) && !text.Contains(ScanNoneLine, StringComparison.Ordinal)));

    private const uint ClrExceptionCode = 0xE0434352; // a .NET exception
    private const uint Signature = 0x504D444D; // "MDMP"
    private const uint ExceptionStream = 6;
    private const uint SystemInfoStream = 7;
    private const uint ModuleListStream = 4;
    private const uint MemoryListStream = 5;
    private const uint Memory64ListStream = 9;
    private const uint MaxMemoryRanges = 1 << 20;
    private const ushort Amd64 = 9;
    private const int MaxFaultFrames = 60;
    private const int MaxScanHits = 40;
    private const int ScanBytes = 64 * 1024;
    private const int MaxManagedLookups = 4096;
    private const int MaxOtherThreads = 10;
    private const int OtherThreadFrames = 5;
    private const int ExceptionThreadFrames = 15;
    private const int MaxModules = 500;
    private const int MaxMessage = 500;

    /// <summary>
    /// Reads the dump and formats crash-stack.txt, or null when the file is gone or is not a
    /// minidump at all. The read runs on its own thread and stops at limit (Limit by default): the
    /// text then holds what was read so far and says it stopped. stage is for tests, called with the
    /// name of each step before it runs.
    /// </summary>
    public static string? Read(string? path, TimeSpan? limit = null, Action<string>? stage = null, IReadOnlyList<string>? serverFolders = null)
    {
        var info = ReadInfo(path, limit ?? Limit, stage, serverFolders);
        return info == null ? null : Format(info);
    }

    /// <summary>Read without the formatting. Null when the file is gone or is not a minidump.</summary>
    public static DumpStackInfo? ReadInfo(string? path, TimeSpan limit, Action<string>? stage = null, IReadOnlyList<string>? serverFolders = null)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            return null;
        var info = new DumpStackInfo { DumpName = Path.GetFileName(path), Limit = limit };
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            info.DumpSize = stream.Length;
            var listed = new List<(ulong Base, uint Size, string Name)>();
            if (!ReadHeader(stream, info, out var arch, out var context, listed))
                return null;
            var rsp = arch == Amd64 && context != null ? ReadRsp(stream, context.Value) : 0UL;

            // ClrMD looks up the dump's module names on disk itself. A dump that names a network or device
            // path is read no further than its own lists here, so nothing reaches that host.
            if (listed.Any(module => !DacTrust.IsSafeModuleName(module.Name)))
            {
                info.Modules = listed.OrderBy(module => module.Base).Take(MaxModules).Select(module => (ModuleName(module.Name), (string?)null)).ToList();
                info.FaultLocation = info.ExceptionAddress is { } address
                    && listed.FirstOrDefault(module => address >= module.Base && address - module.Base < module.Size) is { Name: not null } hit
                        ? ModuleName(hit.Name) + "+0x" + (address - hit.Base).ToString("X", CultureInfo.InvariantCulture)
                        : null;
                info.ManagedUnavailable = "the dump names a file outside this PC's own drives, so it was not read further";
                return info;
            }

            using var cancel = new CancellationTokenSource();
            var worker = new Thread(() => ReadWithClrMd(path, info, rsp, cancel.Token, stage, serverFolders ?? Array.Empty<string>()))
            {
                IsBackground = true,
                Name = "Coodsz crash stack"
            };
            worker.Start();
            if (!worker.Join(limit))
            {
                // The worker sees the cancel at its next step and closes the dump; the report does not wait for it.
                cancel.Cancel();
                lock (info)
                    info.TimedOut = true;
            }
            return info;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    // ----- The minidump header and its exception stream (ClrMD does not expose it) -----

    // listed (when given) gets the dump's module list: base, size and the name as the dump spells it.
    private static bool ReadHeader(Stream stream, DumpStackInfo info, out ushort arch, out (uint Size, uint Rva)? context,
        List<(ulong Base, uint Size, string Name)>? listed = null)
    {
        arch = 0;
        context = null;
        var header = new byte[32];
        if (!ReadAt(stream, 0, header) || BinaryPrimitives.ReadUInt32LittleEndian(header) != Signature)
            return false;
        var count = BinaryPrimitives.ReadUInt32LittleEndian(header.AsSpan(8));
        var directory = BinaryPrimitives.ReadUInt32LittleEndian(header.AsSpan(12));
        var stamp = BinaryPrimitives.ReadUInt32LittleEndian(header.AsSpan(20));
        if (count == 0 || count > 4096 || directory + (long)count * 12 > stream.Length)
            return false;
        if (stamp != 0)
            info.WrittenUtc = DateTimeOffset.FromUnixTimeSeconds(stamp).UtcDateTime;

        var entries = new byte[count * 12];
        if (!ReadAt(stream, directory, entries))
            return false;
        var listedEnd = 0L;
        for (var i = 0; i < count; i++)
        {
            var type = BinaryPrimitives.ReadUInt32LittleEndian(entries.AsSpan(i * 12));
            var size = BinaryPrimitives.ReadUInt32LittleEndian(entries.AsSpan(i * 12 + 4));
            var rva = BinaryPrimitives.ReadUInt32LittleEndian(entries.AsSpan(i * 12 + 8));
            listedEnd = Math.Max(listedEnd, Math.Max(rva + (long)size, MemoryEnd(stream, type, rva, size)));
            if (type == ModuleListStream && listed != null && size >= 4)
            {
                ReadModuleList(stream, rva, listed);
            }
            else if (type == SystemInfoStream && size >= 2)
            {
                var system = new byte[2];
                if (ReadAt(stream, rva, system))
                    arch = BinaryPrimitives.ReadUInt16LittleEndian(system);
            }
            else if (type == ExceptionStream && size >= 168)
            {
                // MINIDUMP_EXCEPTION_STREAM: ThreadId, alignment, MINIDUMP_EXCEPTION (code, flags, record,
                // address, parameter count, alignment, 15 parameters), then the thread context's location.
                var data = new byte[168];
                if (!ReadAt(stream, rva, data))
                    continue;
                info.ThreadId = BinaryPrimitives.ReadUInt32LittleEndian(data);
                info.ExceptionCode = BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(8));
                info.ExceptionAddress = BinaryPrimitives.ReadUInt64LittleEndian(data.AsSpan(24));
                var parameters = (int)Math.Min(15, BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(32)));
                info.ExceptionParameters = Enumerable.Range(0, parameters).Select(p => BinaryPrimitives.ReadUInt64LittleEndian(data.AsSpan(40 + p * 8))).ToList();
                var contextSize = BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(160));
                var contextRva = BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(164));
                if (contextSize >= 0x100 && contextRva + (long)contextSize <= stream.Length)
                    context = (contextSize, contextRva);
            }
        }

        if (listedEnd > stream.Length)
            info.CutShort = CrashReports.SizeText(stream.Length) + " of the " + CrashReports.SizeText(listedEnd) + " its own directory lists";
        return true;
    }

    // Where the memory a memory list stream points to ends in the file, or 0. The memory is most of a
    // dump, so a dump cut anywhere past its header ends before this.
    private static long MemoryEnd(Stream stream, uint type, uint rva, uint size)
    {
        if (type == MemoryListStream && size >= 4)
        {
            // MINIDUMP_MEMORY_LIST: a count, then 16 byte descriptors (start, data size, data rva).
            var head = new byte[4];
            if (!ReadAt(stream, rva, head))
                return 0;
            var count = Math.Min(BinaryPrimitives.ReadUInt32LittleEndian(head), Math.Min((size - 4) / 16, MaxMemoryRanges));
            var list = new byte[count * 16];
            if (!ReadAt(stream, rva + 4L, list))
                return 0;
            var end = 0L;
            for (var i = 0; i < count; i++)
                end = Math.Max(end, BinaryPrimitives.ReadUInt32LittleEndian(list.AsSpan(i * 16 + 12)) + (long)BinaryPrimitives.ReadUInt32LittleEndian(list.AsSpan(i * 16 + 8)));
            return end;
        }

        if (type == Memory64ListStream && size >= 16)
        {
            // MINIDUMP_MEMORY64_LIST: a count, the rva where all the data starts, then 16 byte descriptors (start, data size).
            var head = new byte[16];
            if (!ReadAt(stream, rva, head))
                return 0;
            var count = (uint)Math.Min(BinaryPrimitives.ReadUInt64LittleEndian(head), Math.Min((size - 16) / 16, MaxMemoryRanges));
            var list = new byte[count * 16];
            if (!ReadAt(stream, rva + 16L, list))
                return 0;
            var end = (long)Math.Min(BinaryPrimitives.ReadUInt64LittleEndian(head.AsSpan(8)), long.MaxValue / 2);
            for (var i = 0; i < count; i++)
                end += (long)Math.Min(BinaryPrimitives.ReadUInt64LittleEndian(list.AsSpan(i * 16 + 8)), uint.MaxValue);
            return end;
        }

        return 0;
    }

    // MINIDUMP_MODULE_LIST: a count, then 108 byte MINIDUMP_MODULEs (base, size, checksum, timestamp, name
    // rva, ...); each name is a MINIDUMP_STRING (its byte length, then UTF-16).
    private static void ReadModuleList(Stream stream, uint rva, List<(ulong Base, uint Size, string Name)> listed)
    {
        var head = new byte[4];
        if (!ReadAt(stream, rva, head))
            return;
        var count = Math.Min(BinaryPrimitives.ReadUInt32LittleEndian(head), 4096u);
        var module = new byte[108];
        var length = new byte[4];
        for (var i = 0; i < count; i++)
        {
            if (!ReadAt(stream, rva + 4 + (long)i * 108, module))
                return;
            var nameRva = BinaryPrimitives.ReadUInt32LittleEndian(module.AsSpan(20));
            var name = "";
            if (ReadAt(stream, nameRva, length))
            {
                var bytes = new byte[Math.Min(BinaryPrimitives.ReadUInt32LittleEndian(length), 2048u) & ~1u];
                if (ReadAt(stream, nameRva + 4L, bytes))
                    name = Encoding.Unicode.GetString(bytes);
            }

            listed.Add((BinaryPrimitives.ReadUInt64LittleEndian(module), BinaryPrimitives.ReadUInt32LittleEndian(module.AsSpan(8)), name));
        }
    }

    // Rsp of the x64 CONTEXT at the exception, where the stack scan starts.
    private static ulong ReadRsp(Stream stream, (uint Size, uint Rva) context)
    {
        var data = new byte[0x100];
        return ReadAt(stream, context.Rva, data) ? BinaryPrimitives.ReadUInt64LittleEndian(data.AsSpan(0x98)) : 0;
    }

    private static bool ReadAt(Stream stream, long offset, byte[] buffer)
    {
        if (offset < 0 || offset + buffer.Length > stream.Length)
            return false;
        stream.Position = offset;
        stream.ReadExactly(buffer);
        return true;
    }

    // ----- ClrMD -----

    private static void ReadWithClrMd(string path, DumpStackInfo info, ulong rsp, CancellationToken cancel, Action<string>? stage, IReadOnlyList<string> serverFolders)
    {
        try
        {
            stage?.Invoke("open");
            // Our own stream, so the dump is closed even when ClrMD throws, and the player can still delete it meanwhile.
            using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var target = DataTarget.LoadDump(path, file, new CacheOptions(), leaveOpen: true);
            // Module images only from this PC's own disk, never from a symbol server.
            target.FileLocator = new LocalFilesOnly();
            cancel.ThrowIfCancellationRequested();

            var modules = target.EnumerateModules().OrderBy(module => module.ImageBase).ToList();
            var names = modules.Take(MaxModules).Select(module => (Name: ModuleName(module.FileName), Version: VersionOf(module))).ToList();
            var fault = info.ExceptionAddress is { } address ? Locate(modules, address) : null;
            lock (info)
            {
                info.Modules = names;
                info.FaultLocation = fault;
            }

            stage?.Invoke("runtime");
            cancel.ThrowIfCancellationRequested();
            using var runtime = CreateRuntime(target, serverFolders, out var why);
            lock (info)
                info.ManagedUnavailable = runtime == null ? why : null;

            if (rsp != 0)
            {
                stage?.Invoke("scan");
                var scan = Scan(target, runtime, modules, rsp, cancel);
                lock (info)
                    info.StackScan = scan;
            }

            if (runtime == null)
                return;
            stage?.Invoke("managed");
            cancel.ThrowIfCancellationRequested();
            ReadManaged(runtime, modules, info, cancel);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            // A broken or cut dump: what was read stays, the rest says why.
            lock (info)
                info.OpenError ??= Short(ex);
        }
    }

    /// <summary>
    /// The runtime of the dump with the DAC of the same .NET version on this PC: .NET Framework's
    /// mscordacwks.dll for the game, mscordaccore.dll next to the dump's own coreclr.dll (or in the
    /// installed .NET) for the server. A DAC of another version is never loaded, and one outside the
    /// .NET folders or without Microsoft's signature is refused (DacTrust). Null with the reason.
    /// </summary>
    private static ClrRuntime? CreateRuntime(DataTarget target, IReadOnlyList<string> serverFolders, out string why)
    {
        why = "no .NET runtime in this dump";
        foreach (var clr in target.ClrVersions)
        {
            var reasons = new List<string>();
            foreach (var (dac, sameFiles) in DacCandidates(clr, serverFolders))
            {
                // Held open until the runtime is made, so the checked file is the one that loads.
                using var hold = DacTrust.OpenTrusted(dac, serverFolders, out var refusal);
                if (hold == null)
                {
                    reasons.Add(refusal ?? "the DAC was refused");
                    continue;
                }

                try
                {
                    // A small dump has no version for its runtime (0.0), so ClrMD calls every DAC a mismatch.
                    // The DAC next to the very coreclr.dll the dump loaded (same timestamp and size) is the right one.
                    return clr.CreateRuntime(dac, ignoreMismatch: sameFiles && !Known(clr.Version));
                }
                catch (Exception ex)
                {
                    reasons.Add(Short(ex));
                }
            }

            why = reasons.Count == 0
                ? "no DAC for .NET " + (Known(clr.Version) ? clr.Version.ToString() : "of this dump") + " on this PC"
                : "the DAC on this PC did not load (" + string.Join("; ", reasons.Distinct()) + ")";
        }

        return null;
    }

    // The DACs to try, and whether the runtime file next to it on disk is the one in the dump.
    private static IEnumerable<(string Dac, bool SameFiles)> DacCandidates(ClrInfo clr, IReadOnlyList<string> serverFolders)
    {
        var found = new List<(string, bool)>();
        var runtimeFile = clr.ModuleInfo.FileName ?? "";
        var own = Path.GetDirectoryName(runtimeFile);
        var same = MatchesOnDisk(runtimeFile, clr.ModuleInfo);
        var name = clr.Flavor == ClrFlavor.Desktop ? "mscordacwks.dll" : "mscordaccore.dll";
        if (!string.IsNullOrEmpty(own))
            found.Add((Path.Combine(own, name), same));
        if (clr.Flavor == ClrFlavor.Desktop)
            found.Add((Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Microsoft.NET",
                Environment.Is64BitProcess ? "Framework64" : "Framework", "v4.0.30319", name), false));
        else if (Known(clr.Version))
            found.Add((Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "dotnet", "shared", "Microsoft.NETCore.App",
                clr.Version.ToString(3), name), false));

        // A refused folder goes on without a look on disk, so CreateRuntime can say why; only allowed ones are looked for.
        return found.Where(dac => DacTrust.FolderRefusal(dac.Item1, serverFolders) != null || File.Exists(dac.Item1))
            .DistinctBy(dac => dac.Item1, StringComparer.OrdinalIgnoreCase);
    }

    private static bool Known(Version? version) => version != null && version > new Version(0, 0);

    // The module's file version from the dump, or from the same file on this PC when the dump has none.
    private static string? VersionOf(ModuleInfo module)
    {
        if (Known(module.Version))
            return module.Version.ToString();
        var path = module.FileName ?? "";
        if (!MatchesOnDisk(path, module))
            return null;
        try
        {
            var info = System.Diagnostics.FileVersionInfo.GetVersionInfo(path);
            var version = new Version(info.FileMajorPart, info.FileMinorPart, info.FileBuildPart, info.FilePrivatePart);
            return Known(version) ? version.ToString() : null;
        }
        catch (Exception)
        {
            return null;
        }
    }

    // True when the file at path on this PC is the image the dump loaded: the same PE timestamp and size.
    private static bool MatchesOnDisk(string path, ModuleInfo module) =>
        module.IndexTimeStamp != 0 && LocalFilesOnly.Matches(path, module.IndexTimeStamp, module.IndexFileSize);

    private static void ReadManaged(ClrRuntime runtime, List<ModuleInfo> modules, DumpStackInfo info, CancellationToken cancel)
    {
        var threads = runtime.Threads;
        var (faulting, excepting) = PickThreads(threads, info.ThreadId, thread => thread.OSThreadId, thread => thread.CurrentException != null);
        if (faulting == null)
        {
            // With no exception record there is no faulting thread to speak of; the other threads still show.
            // A faulting thread with no managed match crashed in native code: no frames, said as such.
            if (threads.Length == 0)
                lock (info)
                    info.ManagedUnavailable = "the dump holds no managed threads (too little memory in it)";
            else if (info.ThreadId != null)
                lock (info)
                    info.ManagedFrames = Array.Empty<string>();
        }
        else
        {
            var frames = new List<string>();
            foreach (var frame in faulting.EnumerateStackTrace())
            {
                cancel.ThrowIfCancellationRequested();
                frames.Add(Frame(frame, modules));
                if (frames.Count >= MaxFaultFrames)
                    break;
            }

            lock (info)
                info.ManagedFrames = frames;
        }

        var exception = faulting?.CurrentException ?? excepting?.CurrentException;
        List<string>? lines = null;
        if (exception != null)
        {
            lines = new List<string>();
            for (var current = exception; current != null && lines.Count < 4; current = current.Inner)
                lines.Add((current.Type?.Name ?? "unknown type") + ": " + Message(current.Message));
        }

        AddException(info, lines, excepting == null ? null : ThreadName(excepting), excepting?.EnumerateStackTrace(),
            frame => frame.Method == null ? null : Frame(frame, modules), cancel);

        var others = new List<(string, IReadOnlyList<string>)>();
        foreach (var thread in threads)
        {
            cancel.ThrowIfCancellationRequested();
            if (thread == faulting || thread == excepting || others.Count >= MaxOtherThreads)
                continue;
            var top = thread.EnumerateStackTrace().Where(frame => frame.Method != null).Take(OtherThreadFrames).Select(frame => Frame(frame, modules)).ToList();
            if (top.Count > 0)
                others.Add((ThreadName(thread), top));
        }

        lock (info)
        {
            info.OtherThreads = others;
            // No exception record and not one managed frame on any thread (a cut dump whose stacks were in
            // the missing part, say): the text says so rather than showing nothing past the module list.
            if (faulting == null && info.ThreadId == null && threads.Length > 0 && others.Count == 0 && info.ExceptionThreadFrames.Count == 0)
                info.ManagedUnavailable = NoFramesReason;
        }
    }

    /// <summary>Why a dump with managed threads shows no frames at all.</summary>
    internal const string NoFramesReason = "no thread in this dump has managed frames (the dump may be cut, or hold too little memory)";

    /// <summary>
    /// The faulting thread is only the managed thread with the exception record's OS thread id: a
    /// thread that merely has a current exception is never taken for it, since ClrMD's current
    /// exception can be an old one. Excepting is a thread with a current exception when the faulting
    /// thread has none (or there is no faulting thread), listed apart from the crash.
    /// </summary>
    internal static (T? Faulting, T? Excepting) PickThreads<T>(IReadOnlyList<T> threads, uint? faultingId, Func<T, uint> osId, Func<T, bool> hasException)
        where T : class
    {
        var faulting = faultingId is { } id ? threads.FirstOrDefault(thread => osId(thread) == id) : null;
        var excepting = faulting != null && hasException(faulting) ? null : threads.FirstOrDefault(thread => thread != faulting && hasException(thread));
        return (faulting, excepting);
    }

    /// <summary>
    /// The exception lines and the thread they came from (null for the faulting thread) go in under one
    /// lock before that thread's frames are read, so a read stopped at the time limit never shows
    /// another thread's exception as the crash. frames are that thread's stack; format gives null for a
    /// frame that is left out (no managed method). The limit is checked on every frame.
    /// </summary>
    internal static void AddException<TFrame>(DumpStackInfo info, IReadOnlyList<string>? lines, string? exceptionThread,
        IEnumerable<TFrame>? frames, Func<TFrame, string?> format, CancellationToken cancel)
    {
        lock (info)
        {
            if (lines != null)
                info.ManagedExceptions = lines;
            info.ExceptionThread = exceptionThread;
        }

        if (frames == null)
            return;
        var top = new List<string>();
        foreach (var frame in frames)
        {
            cancel.ThrowIfCancellationRequested();
            if (format(frame) is not { } text)
                continue;
            top.Add(text);
            if (top.Count >= ExceptionThreadFrames)
                break;
        }

        lock (info)
            info.ExceptionThreadFrames = top;
    }

    private static string ThreadName(ClrThread thread) =>
        "Thread " + thread.OSThreadId.ToString(CultureInfo.InvariantCulture) + " (managed " + thread.ManagedThreadId.ToString(CultureInfo.InvariantCulture) + ")";

    // The faulting thread's stack from Rsp up: every value that points into a module or a managed
    // method is a likely return address. Only those names go out, never the values themselves.
    private static List<string> Scan(DataTarget target, ClrRuntime? runtime, List<ModuleInfo> modules, ulong rsp, CancellationToken cancel)
    {
        var hits = new List<string>();
        var lookups = 0;
        var page = new byte[4096];
        for (var offset = 0UL; offset < ScanBytes && hits.Count < MaxScanHits; offset += (ulong)page.Length)
        {
            cancel.ThrowIfCancellationRequested();
            var read = target.DataReader.Read(rsp + offset, page);
            if (read < 8)
                break;
            for (var i = 0; i + 8 <= read && hits.Count < MaxScanHits; i += 8)
            {
                var value = BinaryPrimitives.ReadUInt64LittleEndian(page.AsSpan(i));
                if (value < 0x10000)
                    continue;
                // A pointer into a module's first page is its PE header (like a module handle), not code.
                var name = Locate(modules, value, minOffset: 0x1000);
                if (name == null && runtime != null && lookups++ < MaxManagedLookups && runtime.GetMethodByInstructionPointer(value) is { } method)
                    name = MethodName(method);
                if (name != null && (hits.Count == 0 || hits[^1] != name))
                    hits.Add(name);
            }

            if (read < page.Length)
                break;
        }

        return hits;
    }

    private static string Frame(ClrStackFrame frame, List<ModuleInfo> modules)
    {
        if (frame.Method is { } method)
            return MethodName(method);
        if (frame.Kind == ClrStackFrameKind.Runtime)
            return "[" + (frame.FrameName ?? "runtime frame") + "]";
        return Locate(modules, frame.InstructionPointer) ?? "[unknown frame]";
    }

    private static string MethodName(ClrMethod method) =>
        method.Signature ?? ((method.Type?.Name ?? "?") + "." + (method.Name ?? "?"));

    /// <summary>The module an address is in, as name+0xoffset, or null. modules is sorted by ImageBase.</summary>
    private static string? Locate(List<ModuleInfo> modules, ulong address, ulong minOffset = 0)
    {
        int low = 0, high = modules.Count - 1;
        while (low <= high)
        {
            var middle = (low + high) / 2;
            var module = modules[middle];
            if (address < module.ImageBase)
                high = middle - 1;
            else if (address >= module.ImageBase + (ulong)Math.Max(0, module.ImageSize))
                low = middle + 1;
            else
                return address - module.ImageBase < minOffset ? null
                    : ModuleName(module.FileName) + "+0x" + (address - module.ImageBase).ToString("X", CultureInfo.InvariantCulture);
        }

        return null;
    }

    private static string ModuleName(string? path)
    {
        var name = (path ?? "").Replace('/', '\\');
        var slash = name.LastIndexOf('\\');
        name = slash >= 0 ? name[(slash + 1)..] : name;
        return name.Length == 0 ? "unknown module" : name;
    }

    private static string Message(string? message)
    {
        var text = string.Join(" ", (message ?? "").Split(['\r', '\n', '\t'], StringSplitOptions.RemoveEmptyEntries)).Trim();
        if (text.Length == 0)
            return "(no message)";
        return text.Length > MaxMessage ? text[..(MaxMessage - 3)] + "..." : text;
    }

    internal static string Short(Exception ex)
    {
        // Folders can hold the player's name: only the file name stays, as in the module list.
        var message = System.Text.RegularExpressions.Regex.Replace(ex.Message, @"(?:[A-Za-z]:[\\/]|\\\\)[^'""\r\n<>|]*[\\/]", "");
        var text = ex.GetType().Name + ": " + message.Replace("\r", " ").Replace("\n", " ").Trim();
        return text.Length > 200 ? text[..197] + "..." : text;
    }

    // ----- The text -----

    /// <summary>The well known exception codes by name.</summary>
    public static string? CodeName(uint code) => code switch
    {
        0xC0000005 => "EXCEPTION_ACCESS_VIOLATION",
        0xC00000FD => "EXCEPTION_STACK_OVERFLOW",
        0xC0000409 => "STATUS_STACK_BUFFER_OVERRUN (fail fast)",
        0xC0000374 => "STATUS_HEAP_CORRUPTION",
        0xC000001D => "EXCEPTION_ILLEGAL_INSTRUCTION",
        0xC0000094 => "EXCEPTION_INT_DIVIDE_BY_ZERO",
        0xC0000096 => "EXCEPTION_PRIV_INSTRUCTION",
        0xC0000006 => "EXCEPTION_IN_PAGE_ERROR",
        0xC0000017 => "STATUS_NO_MEMORY",
        0x80000003 => "EXCEPTION_BREAKPOINT",
        ClrExceptionCode => ".NET exception",
        0xE06D7363 => "C++ exception",
        _ => null
    };

    /// <summary>
    /// crash-stack.txt from what was read. Not cleaned yet: the caller cleans it like the logs.
    /// It stays under MaxBytes so FitToSize never cuts its top: the other threads go first, then the
    /// module list, then the stack scan, and the faulting thread's oldest frames only as a last step.
    /// </summary>
    public static string Format(DumpStackInfo info)
    {
        lock (info)
        {
            var keep = new Keep(info.OtherThreads.Count, info.Modules.Count, info.StackScan?.Count ?? 0, info.ManagedFrames?.Count ?? 0);
            var text = Render(info, keep);
            if (Encoding.UTF8.GetByteCount(text) <= MaxBytes)
                return text;
            // Each part in turn keeps the most entries that still fit.
            foreach (var (full, part) in new (int, Func<Keep, int, Keep>)[]
                     {
                         (keep.Threads, (k, n) => k with { Threads = n }),
                         (keep.Modules, (k, n) => k with { Modules = n }),
                         (keep.Scan, (k, n) => k with { Scan = n }),
                         (keep.Frames, (k, n) => k with { Frames = n })
                     })
            {
                int low = 0, high = full;
                while (low < high)
                {
                    var mid = (low + high + 1) / 2;
                    if (Encoding.UTF8.GetByteCount(Render(info, part(keep, mid))) <= MaxBytes)
                        low = mid;
                    else
                        high = mid - 1;
                }

                keep = part(keep, low);
                text = Render(info, keep);
                if (Encoding.UTF8.GetByteCount(text) <= MaxBytes)
                    break;
            }

            return text;
        }
    }

    /// <summary>The most crash-stack.txt holds, well under the 40 KB the log cutter keeps of a file.</summary>
    internal const int MaxBytes = 32 * 1024;

    // How many other threads, modules, stack scan hits and faulting frames the text keeps.
    private sealed record Keep(int Threads, int Modules, int Scan, int Frames);

    private static string Render(DumpStackInfo info, Keep keep)
    {
        var text = new StringBuilder();
        void Line(string line = "") => text.Append(line).Append("\r\n");
        // The line that says a part was cut to keep the text under MaxBytes.
        void Trimmed(int more, string what)
        {
            if (more > 0)
                Line("  (trimmed: " + more.ToString(CultureInfo.InvariantCulture) + " more " + what + ")");
        }

        Line(SummaryTitle);
        Line("Dump: " + info.DumpName + ", " + CrashReports.SizeText(info.DumpSize)
             + (info.WrittenUtc is { } written ? ", written " + written.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture) + " UTC" : ""));
        Line();

        if (info.ExceptionCode is { } code)
        {
            Line("Exception: 0x" + code.ToString("X8", CultureInfo.InvariantCulture) + (CodeName(code) is { } name ? " " + name : "") + AccessText(code, info.ExceptionParameters));
            if (info.ExceptionAddress is { } address)
                Line("Exception address: 0x" + address.ToString("X16", CultureInfo.InvariantCulture));
            Line("Faulting module: " + (info.FaultLocation ?? "not in a loaded module"));
            if (info.ThreadId is { } thread)
                Line("Faulting thread: " + thread.ToString(CultureInfo.InvariantCulture));
        }
        else
        {
            Line("Exception: none recorded in this dump.");
        }

        if (info.CutShort != null)
            Line(CutShortLine + info.CutShort + ").");
        if (info.OpenError != null)
            Line("The dump could not be read past this point: " + info.OpenError);
        if (info.Unavailable != null)
            Line(UnavailableLine + info.Unavailable + ")");
        Line();

        // The faulting thread's own exception is the crash only for a .NET exception code or one this
        // does not know; after a native fault (an access violation, say) it can be one caught earlier.
        if (info.ManagedExceptions.Count > 0 && info.ExceptionThread == null)
        {
            if (info.ExceptionCode is not { } crashCode || crashCode == ClrExceptionCode || CodeName(crashCode) == null)
                Line("Managed exception: " + info.ManagedExceptions[0]);
            else
            {
                Line("Faulting thread's managed exception (the crash is native code, and the exception can be an old one):");
                Line("  Exception: " + info.ManagedExceptions[0]);
            }

            foreach (var inner in info.ManagedExceptions.Skip(1))
                Line("  Inner: " + inner);
            Line();
        }

        if (info.ManagedUnavailable != null)
            Line("Managed frames unavailable: " + info.ManagedUnavailable);
        else if (info.ManagedFrames is { Count: 0 })
            Line("Faulting thread: native code, no managed frames.");
        else if (info.ManagedFrames is { } frames)
        {
            Line(FaultingFramesTitle);
            foreach (var frame in frames.Take(keep.Frames))
                Line("  " + frame);
            Trimmed(frames.Count - keep.Frames, "older frames");
        }

        // Not the crash: ClrMD's current exception of another thread can be an old one.
        if (info.ExceptionThread != null)
        {
            Line();
            Line("Thread with a managed exception (not the faulting thread, and the exception can be an old one): " + info.ExceptionThread);
            if (info.ManagedExceptions.Count > 0)
                Line("  Exception: " + info.ManagedExceptions[0]);
            foreach (var inner in info.ManagedExceptions.Skip(1))
                Line("  Inner: " + inner);
            foreach (var frame in info.ExceptionThreadFrames)
                Line("    " + frame);
        }

        if (info.StackScan is { } scan)
        {
            Line();
            Line(ScanTitle);
            foreach (var hit in scan.Take(keep.Scan))
                Line("  " + hit);
            Trimmed(scan.Count - keep.Scan, "addresses");
            if (scan.Count == 0)
                Line(ScanNoneLine);
        }

        if (info.OtherThreads.Count > 0)
        {
            Line();
            Line("Other threads with managed frames (top " + OtherThreadFrames + " each):");
            foreach (var (thread, top) in info.OtherThreads.Take(keep.Threads))
            {
                Line("  " + thread + ":");
                foreach (var frame in top)
                    Line("    " + frame);
            }

            Trimmed(info.OtherThreads.Count - keep.Threads, "threads");
        }

        if (info.TimedOut)
        {
            Line();
            Line(StoppedLine + info.Limit.TotalSeconds.ToString("0.#", CultureInfo.InvariantCulture) + " s; the parts above are all it got.");
        }

        if (info.Modules.Count > 0)
        {
            Line();
            Line("Loaded modules (" + info.Modules.Count + "):");
            foreach (var (name, version) in info.Modules.Take(keep.Modules))
                Line("  " + name + (version == null ? "" : "  version " + version));
            Trimmed(info.Modules.Count - keep.Modules, "modules");
        }

        return text.ToString();
    }

    // An access violation says whether it read, wrote or ran the address.
    private static string AccessText(uint code, IReadOnlyList<ulong> parameters)
    {
        if (code != 0xC0000005 || parameters.Count < 2)
            return "";
        var kind = parameters[0] switch { 0 => "reading", 1 => "writing", 8 => "running", _ => "at" };
        return " (" + kind + " address 0x" + parameters[1].ToString("X", CultureInfo.InvariantCulture) + ")";
    }

    // Module images from this PC's disk only, and only when their timestamp and size match the dump's.
    private sealed class LocalFilesOnly : IFileLocator
    {
        public string? FindPEImage(string fileName, int buildTimeStamp, int imageSize, bool checkProperties) =>
            Matches(fileName, buildTimeStamp, imageSize) ? fileName : null;

        public static bool Matches(string fileName, int buildTimeStamp, int imageSize)
        {
            try
            {
                // The name comes from the dump: a network or device path is never looked up.
                if (!DacTrust.IsLocalDrivePath(fileName) || !File.Exists(fileName))
                    return false;
                using var stream = new FileStream(fileName, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                using var pe = new PEReader(stream);
                var headers = pe.PEHeaders;
                return headers.CoffHeader.TimeDateStamp == buildTimeStamp && headers.PEHeader?.SizeOfImage == imageSize;
            }
            catch (Exception)
            {
                return false;
            }
        }

        public string? FindPEImage(string fileName, SymbolProperties archivedUnder, ImmutableArray<byte> buildIdOrUUID, OSPlatform originalPlatform, bool checkProperties) => null;
        public string? FindElfImage(string fileName, SymbolProperties archivedUnder, ImmutableArray<byte> buildId, bool checkProperties) => null;
        public string? FindMachOImage(string fileName, SymbolProperties archivedUnder, ImmutableArray<byte> uuid, bool checkProperties) => null;
    }
}
