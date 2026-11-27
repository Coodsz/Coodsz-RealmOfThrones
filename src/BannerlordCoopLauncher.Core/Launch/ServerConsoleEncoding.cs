using System.Runtime.InteropServices;
using System.Text;

namespace BannerlordCoopLauncher.Core.Launch;

/// <summary>
/// What the dedicated server writes its console in. It is a .NET console app, and with its output
/// sent to a pipe it writes the OEM code page of the PC (857 on a Turkish Windows, 437 on an English
/// one), through Start-ModdedServer.ps1 too. A host with no console of its own reads a pipe as the
/// ANSI code page unless told otherwise, which turned YÜZÜKLERİN into YšZšKLER˜N (CR-7W2FC).
/// </summary>
public static class ServerConsoleEncoding
{
    /// <summary>The OEM code page of this PC, or null (keep the default) when it cannot be made.</summary>
    public static Encoding? Oem() => OperatingSystem.IsWindows() ? ForCodePage((int)GetOEMCP()) : null;

    /// <summary>
    /// What the server reads its console commands in. Its ServerConsole reads a redirected stdin as UTF-8 whatever the
    /// code page, so a say written in the OEM page reached players as Gr□e (one bad byte per letter). No BOM, so the
    /// first command never starts with one.
    /// </summary>
    public static Encoding Input() => new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);

    /// <summary>
    /// How Coodsz Host reads the pipe: as Latin-1, which keeps every byte as one character, so each line is decoded by
    /// Line. The Coop 0.1.8 server (nightly and official) writes UTF-8 (its DedicatedServer.Windows sets the console to
    /// it), while the start script and the 1.4.8 Workshop server write the OEM code page, all in one pipe.
    /// </summary>
    public static Encoding Pipe => Encoding.Latin1;

    private static readonly Encoding StrictUtf8 = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    /// <summary>A line read through Pipe in the encoding it was written in. With <paramref name="utf8"/> (a Coop 0.1.8 or
    /// newer server) UTF-8 when its bytes are valid UTF-8, else <paramref name="oem"/> (Latin-1 when that is null). Every
    /// other server is <paramref name="oem"/> alone (UTF-8 when that is null), as before the 0.1.8 engine: on a double
    /// byte Windows a short OEM name can be valid UTF-8 by chance (GBK C3 A8 is one letter, in UTF-8 it is è). Plain
    /// ASCII lines come back as they are.</summary>
    public static string Line(string pipeLine, Encoding? oem, bool utf8)
    {
        if (pipeLine.All(letter => letter < 0x80))
            return pipeLine;
        var bytes = Encoding.Latin1.GetBytes(pipeLine);
        if (!utf8)
            return (oem ?? new UTF8Encoding(encoderShouldEmitUTF8Identifier: false)).GetString(bytes);
        try
        {
            return StrictUtf8.GetString(bytes);
        }
        catch (DecoderFallbackException)
        {
            return (oem ?? Encoding.Latin1).GetString(bytes);
        }
    }

    internal static Encoding? ForCodePage(int codePage)
    {
        if (codePage == 65001)
            return new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        try
        {
            return Encoding.GetEncoding(codePage);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException)
        {
            return null;
        }
    }

    [DllImport("kernel32")]
    private static extern uint GetOEMCP();
}
