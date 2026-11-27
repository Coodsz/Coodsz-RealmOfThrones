using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Text;

namespace BannerlordCoopLauncher.Core.Support;

/// <summary>
/// The reader process. ClrMD and the DAC run native code that can crash or hang on a broken dump, so the
/// tool never reads a dump with them itself: ReadInChild starts a copy of the tool's own exe with
/// ChildArgument, the dump and a text file to write, waits up to Limit and then kills it. That copy reads
/// the dump file only (it never opens a process), writes crash-stack.txt's text and exits. When it dies or
/// runs out of time the report still gets the exception record, read here without ClrMD, and the reason.
/// </summary>
public static partial class DumpStackSummary
{
    public const string ChildArgument = "--coodsz-dump-summary";

    /// <summary>Exit code of a reader that found no minidump in the file: no text at all.</summary>
    public const int NotADumpExit = 3;

    // The reader's own read stops this much before the tool kills it, so it can still write what it has.
    private static readonly TimeSpan ChildMargin = TimeSpan.FromSeconds(5);

    /// <summary>The exe that reads dumps, set when a tool starts (OnStartup). Null: no reader process.</summary>
    internal static string? HelperExe { get; set; }

    /// <summary>A reader run: the dump, the text file to write and the server folders whose DAC may load.</summary>
    internal sealed record ChildJob(string Dump, string Output, IReadOnlyList<string> ServerFolders);

    /// <summary>
    /// Runs first in every tool's startup, like UpdateApply.OnStartup. True means this process was the
    /// reader: it wrote its text, set Environment.ExitCode and should exit now. Otherwise this exe is
    /// noted as the one that reads dumps.
    /// </summary>
    public static bool OnStartup(IReadOnlyList<string> arguments)
    {
        if (ParseChild(arguments) is { } job)
        {
            Environment.ExitCode = RunChild(job);
            return true;
        }

        var exe = Environment.ProcessPath;
        // Run through dotnet.exe (a loose build), the exe would not know the argument.
        if (!string.IsNullOrEmpty(exe) && !Path.GetFileNameWithoutExtension(exe).Equals("dotnet", StringComparison.OrdinalIgnoreCase))
            HelperExe = exe;
        return false;
    }

    /// <summary>The reader run these arguments ask for, or null when they are not ChildArgument's.</summary>
    internal static ChildJob? ParseChild(IReadOnlyList<string> arguments)
    {
        if (arguments.Count < 3 || arguments[0] != ChildArgument || string.IsNullOrWhiteSpace(arguments[1]) || string.IsNullOrWhiteSpace(arguments[2]))
            return null;
        return new ChildJob(arguments[1], arguments[2], arguments.Skip(3).Where(folder => !string.IsNullOrWhiteSpace(folder)).ToList());
    }

    /// <summary>The reader's work: the text goes to job.Output whole (written aside, then moved). Returns the exit code.</summary>
    internal static int RunChild(ChildJob job)
    {
        try
        {
            var text = Read(job.Dump, Limit - ChildMargin, serverFolders: job.ServerFolders);
            if (text == null)
                return NotADumpExit;
            var part = job.Output + ".part";
            File.WriteAllText(part, text, new UTF8Encoding(false));
            File.Move(part, job.Output, overwrite: true);
            return 0;
        }
        catch (Exception)
        {
            return 4;
        }
    }

    /// <summary>
    /// crash-stack.txt read by a reader process (helper, else HelperExe; prefix goes before ChildArgument,
    /// for tests), or null when the file is gone or is not a minidump. A reader that dies, hangs past
    /// limit or cannot start gives the exception record and a "(crash stack unavailable: ...)" line.
    /// </summary>
    public static string? ReadInChild(string? path, IReadOnlyList<string>? serverFolders = null, TimeSpan? limit = null,
        string? helper = null, IReadOnlyList<string>? prefix = null)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            return null;
        var header = new DumpStackInfo { DumpName = Path.GetFileName(path) };
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            header.DumpSize = stream.Length;
            if (!ReadHeader(stream, header, out _, out _))
                return null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }

        helper ??= HelperExe;
        if (string.IsNullOrEmpty(helper))
        {
            header.Unavailable = "this program has no dump reader";
            return Format(header);
        }

        var wait = limit ?? Limit;
        var output = Path.Combine(Path.GetTempPath(), "coodsz-crash-stack-" + Guid.NewGuid().ToString("N") + ".txt");
        try
        {
            var start = new ProcessStartInfo(helper)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                WorkingDirectory = Path.GetTempPath()
            };
            foreach (var argument in prefix ?? Array.Empty<string>())
                start.ArgumentList.Add(argument);
            start.ArgumentList.Add(ChildArgument);
            start.ArgumentList.Add(Path.GetFullPath(path));
            start.ArgumentList.Add(output);
            // Full paths, so the reader compares the same folders DacTrust is told about whatever its working folder.
            foreach (var folder in serverFolders ?? Array.Empty<string>())
                if (FullOrNull(folder) is { } full)
                    start.ArgumentList.Add(full);

            // The job ends the reader too when this tool closes or dies while it waits.
            using var job = KillOnCloseJob();
            using var child = Process.Start(start) ?? throw new InvalidOperationException("no process");
            // When it cannot join (a job that allows no other), the kill below still stops it at the limit.
            if (job != null)
                AssignProcessToJobObject(job, child.Handle);

            if (!child.WaitForExit(wait))
            {
                try
                {
                    child.Kill(entireProcessTree: true);
                    // Its files are closed once it is gone, so the .part file below can be removed.
                    child.WaitForExit(TimeSpan.FromSeconds(3));
                }
                catch (Exception)
                {
                    // It ended on its own meanwhile.
                }

                // A reader that wrote its text and then could not close (a DAC thread stuck in native code)
                // still gave it: the file is only there once whole (moved from .part), and is read as text.
                if (WrittenText(output) is { } written)
                    return written + "(the reader wrote this, then did not close and was stopped after "
                           + wait.TotalSeconds.ToString("0.#", CultureInfo.InvariantCulture) + " s)\r\n";
                header.Unavailable = "the reader took longer than " + wait.TotalSeconds.ToString("0.#", CultureInfo.InvariantCulture) + " s and was stopped";
                return Format(header);
            }

            if (child.ExitCode == NotADumpExit)
                return null;
            if (child.ExitCode == 0 && File.Exists(output))
                return File.ReadAllText(output, Encoding.UTF8);
            header.Unavailable = "the reader stopped with exit code 0x" + child.ExitCode.ToString("X8", CultureInfo.InvariantCulture);
            return Format(header);
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException or IOException or UnauthorizedAccessException)
        {
            header.Unavailable = "the reader did not start (" + ex.GetType().Name + ")";
            return Format(header);
        }
        finally
        {
            TryDelete(output);
            TryDelete(output + ".part", retries: 5);
        }
    }

    // A job object whose processes end when its last handle closes, or null when Windows gives none.
    private static Microsoft.Win32.SafeHandles.SafeFileHandle? KillOnCloseJob()
    {
        if (!OperatingSystem.IsWindows())
            return null;
        var job = CreateJobObject(IntPtr.Zero, null);
        if (job.IsInvalid)
        {
            job.Dispose();
            return null;
        }

        var limits = new ExtendedLimits { Basic = new BasicLimits { LimitFlags = 0x2000 } }; // JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE
        if (SetInformationJobObject(job, 9, ref limits, (uint)System.Runtime.InteropServices.Marshal.SizeOf<ExtendedLimits>()))
            return job;
        job.Dispose();
        return null;
    }

    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
    private struct BasicLimits
    {
        public long PerProcessUserTimeLimit;
        public long PerJobUserTimeLimit;
        public uint LimitFlags;
        public UIntPtr MinimumWorkingSetSize;
        public UIntPtr MaximumWorkingSetSize;
        public uint ActiveProcessLimit;
        public UIntPtr Affinity;
        public uint PriorityClass;
        public uint SchedulingClass;
    }

    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
    private struct ExtendedLimits
    {
        public BasicLimits Basic;
        public ulong ReadOperationCount, WriteOperationCount, OtherOperationCount, ReadTransferCount, WriteTransferCount, OtherTransferCount;
        public UIntPtr ProcessMemoryLimit;
        public UIntPtr JobMemoryLimit;
        public UIntPtr PeakProcessMemoryUsed;
        public UIntPtr PeakJobMemoryUsed;
    }

    [System.Runtime.InteropServices.DllImport("kernel32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode, SetLastError = true)]
    private static extern Microsoft.Win32.SafeHandles.SafeFileHandle CreateJobObject(IntPtr attributes, string? name);

    [System.Runtime.InteropServices.DllImport("kernel32.dll", SetLastError = true)]
    [return: System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.Bool)]
    private static extern bool SetInformationJobObject(Microsoft.Win32.SafeHandles.SafeFileHandle job, int infoClass, ref ExtendedLimits info, uint size);

    [System.Runtime.InteropServices.DllImport("kernel32.dll", SetLastError = true)]
    [return: System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.Bool)]
    private static extern bool AssignProcessToJobObject(Microsoft.Win32.SafeHandles.SafeFileHandle job, IntPtr process);

    private static string? FullOrNull(string? folder)
    {
        if (string.IsNullOrWhiteSpace(folder))
            return null;
        try
        {
            return Path.GetFullPath(folder);
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>
    /// The text a stopped reader left in output, or null: only a file of at most twice MaxBytes that
    /// starts like a summary counts, read as UTF-8 text (anything else in it becomes replacement
    /// characters) and ending in a line break. The .part file of a reader cut off mid write is never read.
    /// </summary>
    internal static string? WrittenText(string output)
    {
        try
        {
            var file = new FileInfo(output);
            if (!file.Exists || file.Length == 0 || file.Length > 2L * MaxBytes)
                return null;
            var text = Encoding.UTF8.GetString(File.ReadAllBytes(output));
            if (!text.StartsWith(SummaryTitle, StringComparison.Ordinal))
                return null;
            return text.EndsWith('\n') ? text : text + "\r\n";
        }
        catch (Exception)
        {
            return null;
        }
    }

    // A killed reader can hold its .part file for a moment, so that one gets a few tries.
    private static void TryDelete(string path, int retries = 0)
    {
        for (var attempt = 0; ; attempt++)
        {
            try
            {
                File.Delete(path);
                return;
            }
            catch (Exception) when (attempt < retries)
            {
                Thread.Sleep(100);
            }
            catch (Exception)
            {
                // A temp file; Windows cleans it up later.
                return;
            }
        }
    }
}
