using System.Diagnostics;
using System.Reflection.PortableExecutable;

namespace TaomCoop.Core;

public static class Taom157Runtime
{
    public const string DownloadUrl = "https://aka.ms/vs/17/release/vc_redist.x64.exe";
    public const string Missing = "{0} is missing. Install the Microsoft Visual C++ Redistributable x64, restart Windows, then start the server again.";
    public const string Unreadable = "The tool could not read the version of {0}. Repair the server files if this DLL is in the server folder, or repair the Microsoft Visual C++ Redistributable x64.";
    public const string TooOld = "{0} is version {1}. This server needs {2} or newer. Install the Microsoft Visual C++ Redistributable x64 and restart Windows. If the DLL is in the server folder, repair the server files too.";
    private static readonly Version Minimum = new(14, 40);
    private static readonly string[] RuntimeFiles = ["msvcp140.dll", "vcruntime140.dll", "vcruntime140_1.dll"];

    public static string? Problem(string serverRoot) =>
        Problem(Path.Combine(serverRoot, "engine", "bin", "Win64_Shipping_Server"),
            Environment.GetFolderPath(Environment.SpecialFolder.System),
            Environment.GetFolderPath(Environment.SpecialFolder.Windows),
            Environment.GetEnvironmentVariable("PATH"), ReadFileVersion);

    public static string? Problem(string engineBin, string systemDirectory, string windowsDirectory,
        string? path, Func<string, Version?> readVersion)
    {
        var minimum = RequiredVersion(Path.Combine(engineBin, "Rgl.dll"));
        var directories = new[] { engineBin, systemDirectory, windowsDirectory }
            .Concat((path ?? "").Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
                .Select(entry => Environment.ExpandEnvironmentVariables(entry.Trim().Trim('"'))))
            .Where(Path.IsPathFullyQualified);
        foreach (var name in RuntimeFiles)
        {
            var file = directories.Select(directory => Path.Combine(directory, name)).FirstOrDefault(File.Exists);
            if (file == null)
                return string.Format(Missing, name);
            if (readVersion(file) is not { } version)
                return string.Format(Unreadable, file);
            if (new Version(version.Major, version.Minor) < minimum)
                return string.Format(TooOld, file, version, minimum);
        }
        return null;
    }

    public static Version RequiredVersion(string engineDll)
    {
        try
        {
            using var stream = File.OpenRead(engineDll);
            using var pe = new PEReader(stream);
            if (pe.PEHeaders.PEHeader is { } header)
            {
                var linker = new Version(header.MajorLinkerVersion, header.MinorLinkerVersion);
                return linker > Minimum ? linker : Minimum;
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or BadImageFormatException)
        {
            // Keep the known minimum when the engine header cannot be read.
        }
        return Minimum;
    }

    private static Version? ReadFileVersion(string file)
    {
        try
        {
            using var stream = File.OpenRead(file);
            using var pe = new PEReader(stream);
            if (pe.PEHeaders.CoffHeader.Machine != Machine.Amd64)
                return null;
            var info = FileVersionInfo.GetVersionInfo(file);
            return string.IsNullOrEmpty(info.FileVersion) ? null : new Version(info.FileMajorPart,
                info.FileMinorPart, info.FileBuildPart, info.FilePrivatePart);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or BadImageFormatException)
        {
            return null;
        }
    }
}
