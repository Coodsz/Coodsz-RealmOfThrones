using System.Runtime.InteropServices;

namespace BannerlordCoopLauncher.Core.Joining;

/// <summary>
/// Reads another process's command line through NtQueryInformationProcess
/// (ProcessCommandLineInformation, Windows 8.1 and later). Only limited query access is needed,
/// so it works for a server started by the same user without elevation. Null when it cannot read it.
/// </summary>
public static class ProcessCommandLine
{
    private const int ProcessCommandLineInformation = 60;
    private const uint ProcessQueryLimitedInformation = 0x1000;
    private const int StatusInfoLengthMismatch = unchecked((int)0xC0000004);

    public static string? Read(int processId)
    {
        if (!OperatingSystem.IsWindows())
            return null;
        var handle = OpenProcess(ProcessQueryLimitedInformation, false, processId);
        if (handle == IntPtr.Zero)
            return null;
        try
        {
            var status = NtQueryInformationProcess(handle, ProcessCommandLineInformation, IntPtr.Zero, 0, out var needed);
            if (status != StatusInfoLengthMismatch || needed <= 0 || needed > 1 << 20)
                return null;
            var buffer = Marshal.AllocHGlobal(needed);
            try
            {
                if (NtQueryInformationProcess(handle, ProcessCommandLineInformation, buffer, needed, out _) != 0)
                    return null;
                // A UNICODE_STRING: Length in bytes, MaximumLength, then a pointer to the text.
                var length = (ushort)Marshal.ReadInt16(buffer);
                var text = Marshal.ReadIntPtr(buffer, IntPtr.Size);
                return length == 0 || text == IntPtr.Zero ? null : Marshal.PtrToStringUni(text, length / 2);
            }
            finally
            {
                Marshal.FreeHGlobal(buffer);
            }
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException)
        {
            return null;
        }
        finally
        {
            CloseHandle(handle);
        }
    }

    [DllImport("kernel32", SetLastError = true)]
    private static extern IntPtr OpenProcess(uint access, bool inheritHandle, int processId);

    [DllImport("kernel32", SetLastError = true)]
    private static extern bool CloseHandle(IntPtr handle);

    [DllImport("ntdll")]
    private static extern int NtQueryInformationProcess(IntPtr process, int informationClass, IntPtr information, int length, out int returnLength);
}
