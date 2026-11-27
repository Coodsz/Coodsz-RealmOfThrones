using System.ComponentModel;
using System.Diagnostics;
using System.Text.RegularExpressions;

namespace BannerlordCoopLauncher.Core.Support;

/// <summary>
/// A mod's map shaders in C:\ProgramData\Mount and Blade II Bannerlord\Shaders\TerrainShaders\&lt;module&gt;. On a map
/// load the game clears them first; when it can't delete them (files an elevated game run made, or read only ones) it
/// logs "failure on remove_all_files_with_extension" for each, then crashes on the map with "Missing shader from sack:
/// pbr_terrain" (AD1259 CR-TXY4V, CR-7ZKZ5, CR-VG6VV). The launchers set such a folder aside before the game starts,
/// and the game builds new shaders.
/// </summary>
public static class TerrainShaderCache
{
    public enum Outcome { SetAside, Gone, Link, AccessDenied, Failed }

    public readonly record struct Report(string Name, DateTime LogTimeUtc);

    public readonly record struct Result(string Name, string Path, Outcome Outcome, string? Error);

    public static string Root => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
        "Mount and Blade II Bannerlord", "Shaders", "TerrainShaders");

    public static string LogsDirectory => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
        "Mount and Blade II Bannerlord", "logs");

    public const string AskTitle = "Old map shader files";

    public static string Ask(string names) =>
        "Bannerlord can't remove its old " + names + " map shader files, most likely because the game once ran as administrator, and the game can crash on the map until they're gone. Remove them now? Windows will ask for permission, because only an administrator can remove them.";

    // Every elevated start goes through here and gives back the exit code. The tests swap it (SafeToolStart) so no
    // test pops UAC.
    internal static Func<ProcessStartInfo, int> ElevatedStart = StartAndWait;

    // The elevated cmd's exit codes when its own check finds a link on the way, or finds no fsutil.exe to check with
    // (see RemoveCommand). Rare ones, so they don't read as a Windows error like 5 (access denied).
    internal const int LinkExitCode = 75;
    internal const int NoFsutilExitCode = 76;

    private static readonly Regex Failure = new(@"failure on remove_all_files_with_extension\s*:\s*([^\r\n]+)", RegexOptions.Compiled);
    private static readonly Regex PlainName = new(@"^[A-Za-z0-9_.\-]+$", RegexOptions.Compiled);

    // Never into a junction or symlink: those lead out of the shader folder.
    private static readonly EnumerationOptions NoLinks = new() { RecurseSubdirectories = true, AttributesToSkip = FileAttributes.ReparsePoint };

    /// <summary>TerrainShaders folders the newest engine logs could not clear, with the newest such log's time.</summary>
    public static IReadOnlyList<Report> Reported(string engineLogsDirectory, int newestLogs = 5)
    {
        var newest = new Dictionary<string, DateTime>(StringComparer.OrdinalIgnoreCase);
        IEnumerable<string> logs;
        try
        {
            logs = Directory.EnumerateFiles(engineLogsDirectory, "rgl_log_*.txt")
                .Where(path => !Path.GetFileName(path).StartsWith("rgl_log_errors", StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(File.GetLastWriteTimeUtc)
                .Take(newestLogs)
                .ToList();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return [];
        }

        foreach (var log in logs)
        {
            string text;
            DateTime written;
            try
            {
                text = File.ReadAllText(log);
                written = File.GetLastWriteTimeUtc(log);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                continue;
            }

            foreach (Match match in Failure.Matches(text))
            {
                // C:\ProgramData/Mount and Blade II Bannerlord/Shaders/TerrainShaders/AD1259/Main_map/D3D11/0p.sacx
                var parts = match.Groups[1].Value.Trim().Split('\\', '/');
                var at = Array.FindIndex(parts, part => part.Equals("TerrainShaders", StringComparison.OrdinalIgnoreCase));
                if (at < 1 || !parts[at - 1].Equals("Shaders", StringComparison.OrdinalIgnoreCase)
                    || at + 2 >= parts.Length || !IsPlainName(parts[at + 1]))
                    continue;
                if (!newest.TryGetValue(parts[at + 1], out var seen) || written > seen)
                    newest[parts[at + 1]] = written;
            }
        }

        return newest.Select(pair => new Report(pair.Key, pair.Value)).ToList();
    }

    /// <summary>
    /// Renames each reported folder to .coodsz-old, the way ModuleShaderCache does. A folder with a file written after
    /// the log that reported it holds the game's new shaders and stays.
    /// </summary>
    public static IReadOnlyList<Result> SetAside(string root, IEnumerable<Report> reports)
    {
        var results = new List<Result>();
        foreach (var report in reports)
        {
            if (FolderIn(root, report.Name) is not { } folder)
                continue;
            try
            {
                if (OnLink(root, folder))
                {
                    results.Add(new Result(report.Name, folder, Outcome.Link, null));
                    continue;
                }

                if (!Directory.Exists(folder))
                {
                    results.Add(new Result(report.Name, folder, Outcome.Gone, null));
                    continue;
                }

                if (new DirectoryInfo(folder).EnumerateFiles("*", NoLinks).Any(file => file.LastWriteTimeUtc > report.LogTimeUtc))
                    continue;
                var aside = folder + ".coodsz-old";
                ClearReadOnly(folder);
                if (Directory.Exists(aside))
                {
                    ClearReadOnly(aside);
                    Directory.Delete(aside, recursive: true);
                }

                Directory.Move(folder, aside);
                results.Add(new Result(report.Name, folder, Outcome.SetAside, null));
            }
            catch (Exception ex) when (ex is UnauthorizedAccessException || (ex is IOException && ex.HResult == unchecked((int)0x80070005)))
            {
                results.Add(new Result(report.Name, folder, Outcome.AccessDenied, ex.Message));
            }
            catch (Exception ex)
            {
                // One folder's problem must not lose the outcomes of the others.
                results.Add(new Result(report.Name, folder, Outcome.Failed, ex.Message));
            }
        }

        return results;
    }

    /// <summary>
    /// Sets the folders the logs report aside and logs each outcome. Folders Windows won't let this account touch are
    /// removed by one elevated rd after one yes from <paramref name="ask"/>. The logs, the files and the elevated wait
    /// stay off the caller's thread; only the ask and the log lines come back to it. Nothing here stops Connect.
    /// </summary>
    public static async Task ClearAsync(Func<string, bool> ask, Action<string> log)
    {
        try
        {
            await ClearAsync(Root, () => Reported(LogsDirectory), ask, log, ElevatedStart);
        }
        catch (Exception ex)
        {
            log("Could not check the old map shader files: " + ex.Message);
        }
    }

    internal static async Task ClearAsync(string root, Func<IEnumerable<Report>> reports, Func<string, bool> ask, Action<string> log, Func<ProcessStartInfo, int> elevatedStart, string? fsutil = null)
    {
        var denied = new List<Result>();
        foreach (var result in await Task.Run(() => SetAside(root, reports())))
        {
            switch (result.Outcome)
            {
                case Outcome.SetAside:
                    log("Set aside the old " + result.Name + " map shader files (renamed to .coodsz-old). Bannerlord could not remove them, and the game can crash on the map until they're gone. The game builds new ones.");
                    break;
                case Outcome.Gone:
                    // Silent: an old log keeps naming it on every Connect while that log is among the newest.
                    break;
                case Outcome.Link:
                    log(LinkLine(result.Name));
                    break;
                case Outcome.Failed:
                    log("Could not set aside the old " + result.Name + " map shader files: " + result.Error);
                    break;
                default:
                    denied.Add(result);
                    break;
            }
        }

        if (denied.Count == 0)
            return;
        var names = string.Join(", ", denied.Select(result => result.Name));
        string? error = null;
        try
        {
            if (ask(names))
            {
                // Checked again: a folder could have become a link while the question was up.
                foreach (var result in denied.Where(result => OnLink(root, result.Path)).ToList())
                {
                    log(LinkLine(result.Name));
                    denied.Remove(result);
                }

                var exitCode = 0;
                if (denied.Count > 0)
                {
                    var command = RemoveCommand(root, denied.Select(result => result.Path), fsutil);
                    exitCode = command == null ? NoFsutilExitCode : await Task.Run(() => elevatedStart(command));
                }

                foreach (var result in denied)
                {
                    // The cmd stopped at a link or found no fsutil: this folder and any after it are left as they were.
                    log(!Directory.Exists(result.Path)
                        ? "Removed the old " + result.Name + " map shader files with administrator permission. The game builds new ones."
                        : exitCode == LinkExitCode
                            ? LinkLine(result.Name)
                            : exitCode == NoFsutilExitCode
                                ? "Could not remove the old " + result.Name + " map shader files, because Windows' fsutil.exe, which checks them for links first, can't be used."
                                : "Could not remove the old " + result.Name + " map shader files, even with administrator permission. The game can crash on the map until they're gone.");
                    // The stop came after the module folder's rd, at its .coodsz-old copy.
                    if (exitCode == LinkExitCode && !Directory.Exists(result.Path) && Directory.Exists(result.Path + ".coodsz-old"))
                        log(LinkLine(result.Name));
                }

                return;
            }
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == 1223)
        {
            // The player said no on the Windows permission prompt.
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException)
        {
            error = ex.Message;
        }

        log(error == null
            ? "Kept the old " + names + " map shader files. The game can crash on the map until they're gone. Connect asks again next time."
            : "Could not remove the old " + names + " map shader files: " + error);
    }

    // One cmd that removes each folder and the .coodsz-old copy beside it. Names passed IsPlainName and the folders
    // sit right under Root, so nothing in them can break out of the quotes. The UAC prompt shows inside Process.Start,
    // after the C# checks, so the cmd checks the folders on the way again right before each rd and stops with
    // LinkExitCode at a link, and with NoFsutilExitCode before anything when fsutil.exe is missing. That cmd check is
    // best effort: fsutil exits 0 only for a junction or symlink, but 1 both for a plain folder and for a query error,
    // so it can't fail closed. The C# check just before the start is the main one. Null when the fsutil path itself
    // could break the quotes.
    internal static ProcessStartInfo? RemoveCommand(string root, IEnumerable<string> folders, string? fsutil = null)
    {
        fsutil ??= Path.Combine(Environment.SystemDirectory, "fsutil.exe");
        if (fsutil.Contains('%') || fsutil.Contains('"'))
            return null;
        string Guard(IEnumerable<string> paths) => string.Concat(paths.Select(path =>
            string.Format("\"{0}\" reparsepoint query \"{1}\" >nul 2>&1 && exit /b {2} & ", fsutil, path, LinkExitCode)));
        var ancestors = Ancestors(root).ToList();
        return new ProcessStartInfo
        {
            FileName = Path.Combine(Environment.SystemDirectory, "cmd.exe"),
            Arguments = "/d /c " + string.Format("(if not exist \"{0}\" exit /b {1}) & ", fsutil, NoFsutilExitCode)
                + string.Join(" & ", folders.Select(folder =>
                    Guard(ancestors.Append(folder)) + string.Format("rd /s /q \"{0}\" & ", folder)
                    + Guard(ancestors.Append(folder + ".coodsz-old")) + string.Format("(if exist \"{0}.coodsz-old\" rd /s /q \"{0}.coodsz-old\")", folder))),
            Verb = "runas",
            UseShellExecute = true,
            WindowStyle = ProcessWindowStyle.Hidden
        };
    }

    private static int StartAndWait(ProcessStartInfo start)
    {
        using var process = Process.Start(start) ?? throw new InvalidOperationException("Windows did not start the removal.");
        process.WaitForExit();
        return process.ExitCode;
    }

    private static bool IsPlainName(string name) => PlainName.IsMatch(name) && !name.Contains("..");

    private static string? FolderIn(string root, string name)
    {
        if (!IsPlainName(name))
            return null;
        var full = Path.GetFullPath(Path.Combine(root, name));
        return string.Equals(Path.GetDirectoryName(full), Path.GetFullPath(root).TrimEnd('\\', '/'), StringComparison.OrdinalIgnoreCase) ? full : null;
    }

    private static string LinkLine(string name) =>
        "Left the old " + name + " map shader files alone, because a folder on their path is a link (junction or symlink) to another place.";

    // A junction or symlink anywhere from the Bannerlord folder in ProgramData down to the module folder (or its
    // .coodsz-old copy) would take the rename or the elevated rd into another place.
    private static bool OnLink(string root, string folder) =>
        Ancestors(root).Append(folder).Append(folder + ".coodsz-old").Any(IsLink);

    // Mount and Blade II Bannerlord, Shaders and TerrainShaders.
    private static IEnumerable<string> Ancestors(string root)
    {
        var terrain = Path.GetFullPath(root).TrimEnd('\\', '/');
        var shaders = Path.GetDirectoryName(terrain);
        var bannerlord = shaders == null ? null : Path.GetDirectoryName(shaders);
        return new[] { bannerlord, shaders, terrain }.OfType<string>();
    }

    private static bool IsLink(string path)
    {
        try
        {
            return File.GetAttributes(path).HasFlag(FileAttributes.ReparsePoint);
        }
        catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException)
        {
            return false;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Can't tell, so it counts as one.
            return true;
        }
    }

    private static void ClearReadOnly(string folder)
    {
        foreach (var entry in new DirectoryInfo(folder).EnumerateFileSystemInfos("*", NoLinks).Prepend(new DirectoryInfo(folder)))
        {
            if (entry.Attributes.HasFlag(FileAttributes.ReadOnly))
                entry.Attributes &= ~FileAttributes.ReadOnly;
        }
    }
}
