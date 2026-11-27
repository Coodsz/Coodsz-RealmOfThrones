using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;

namespace BannerlordCoopLauncher.Core.Updates;

/// <summary>The file and process access the rename cleanup needs, so tests can pass a fake.</summary>
public interface IRenameHost
{
    bool FileExists(string path);
    bool TryCopy(string from, string to);
    bool TryDelete(string path);
    bool IsRunning(string exePath);
    bool TryStart(string exePath, IReadOnlyList<string> arguments);
    Version? FileVersion(string path);
    /// <summary>
    /// Adds one line to update-install.log, tagged with key. With once the line is written a single
    /// time, otherwise at most once a day, so a case that never clears does not grow the log forever.
    /// </summary>
    void Log(string folder, string key, string line, bool once);
}

/// <summary>Reads and repoints .lnk shortcuts.</summary>
public interface IShortcutStore
{
    /// <summary>Shortcut files that may point at one of these file names. A fake may return them all.</summary>
    IEnumerable<string> Candidates(IReadOnlyCollection<string> fileNames);

    /// <summary>False when the shortcut could not be read at all (no Windows Shell, no rights).</summary>
    bool TryTarget(string shortcutPath, out string target);

    /// <summary>True when the shortcut file's bytes hold this full path, for a shortcut that cannot be read.</summary>
    bool MentionsPath(string shortcutPath, string path);

    /// <summary>False when the Windows Shell (WScript.Shell) is missing, so no shortcut can be read.</summary>
    bool ShellAvailable { get; }

    bool TryRetarget(string shortcutPath, string oldTarget, string newTarget);
}

/// <summary>Shortcuts found pointing at an old exe: how many moved, and how many could not be read or moved.</summary>
public readonly record struct ShortcutResult(int Changed, int Failed);

/// <summary>
/// The first start after the exe names changed. A tool under its new name removes the old exe of its
/// own role from its folder and repoints shortcuts to it. A new build that still carries an old name
/// (a legacy zip, for players who skip the bridge release) copies itself to the new name and starts that.
/// </summary>
public static class ToolRename
{
    /// <summary>
    /// Runs early in every tool's startup. True means this process should exit now, because a copy
    /// under the new name took over or already runs. The cleanup itself runs in the background.
    /// </summary>
    public static bool OnStartup()
    {
        try
        {
            var processPath = Environment.ProcessPath;
            var identity = Assembly.GetEntryAssembly()?.GetName().Name;
            if (string.IsNullOrEmpty(processPath) || string.IsNullOrEmpty(identity))
                return false;
            var host = new PhysicalRenameHost();
            if (TrySelfRename(host, processPath, identity, Environment.GetCommandLineArgs().Skip(1).ToList()))
                return true;
            var folder = Path.GetDirectoryName(processPath) ?? "";
            var exe = Path.GetFileName(processPath);
            if (NeedsCleanup(host, folder, exe))
            {
                _ = Task.Run(() =>
                {
                    try { Cleanup(host, new WindowsShortcutStore(), folder, exe, TimeSpan.FromSeconds(20)); }
                    catch (Exception) { }
                });
            }
        }
        catch (Exception)
        {
            // The rename cleanup is best effort. It must never stop a tool from starting.
        }

        return false;
    }

    /// <summary>
    /// The new name this process should run under, or null. Only when the build itself is a new name
    /// tool (its assembly name) and the exe file still carries an old name of that same tool.
    /// </summary>
    public static string? SelfRenameTarget(string processPath, string identityName)
    {
        var running = Path.GetFileName(processPath);
        var tool = ToolNames.Find(running);
        if (tool == null || !ToolNames.IsOldName(running))
            return null;
        if (!tool.NewExeName.Equals(identityName + ".exe", StringComparison.OrdinalIgnoreCase))
            return null;
        return Path.Combine(Path.GetDirectoryName(processPath) ?? "", tool.NewExeName);
    }

    /// <summary>
    /// Hands over to the exe under the new name. True means the caller exits. When the new exe is
    /// already running this exits without starting a second copy. An existing new exe is only
    /// overwritten when this build has a higher file version.
    /// </summary>
    public static bool TrySelfRename(IRenameHost host, string processPath, string identityName, IReadOnlyList<string> arguments)
    {
        var target = SelfRenameTarget(processPath, identityName);
        if (target == null)
            return false;
        if (!host.FileExists(target))
            return host.TryCopy(processPath, target) && host.TryStart(target, arguments);
        if (host.IsRunning(target))
        {
            host.Log(Path.GetDirectoryName(target) ?? "", "already-running", Path.GetFileName(processPath)
                + " closed because " + Path.GetFileName(target) + " is already running.", once: false);
            return true;
        }
        var mine = host.FileVersion(processPath);
        var theirs = host.FileVersion(target);
        if (mine != null && theirs != null && mine > theirs)
            host.TryCopy(processPath, target);
        return host.TryStart(target, arguments);
    }

    /// <summary>The old exe paths of the same tool in its folder, whether they still exist or not.</summary>
    public static IReadOnlyList<string> OldExePaths(string folder, string currentExeName)
    {
        if (!ToolNames.IsNewName(currentExeName) || ToolNames.Find(currentExeName) is not { } tool)
            return [];
        return tool.OldExeNames.Select(name => Path.Combine(folder, name)).ToList();
    }

    public static bool NeedsCleanup(IRenameHost host, string folder, string currentExeName)
    {
        var old = OldExePaths(folder, currentExeName);
        return old.Count > 0 && (old.Any(host.FileExists) || host.FileExists(Path.Combine(folder, SuccessorInstall.RenameMarker)));
    }

    /// <summary>
    /// Repoints shortcuts first (they may point at an old exe the install already removed),
    /// then deletes each old exe once nothing runs it, then the marker the install left. When a
    /// shortcut to an old exe could not be moved, the old exe and the marker stay so no shortcut is
    /// left pointing at nothing, one line goes to update-install.log, and the next start tries again.
    /// </summary>
    public static void Cleanup(IRenameHost host, IShortcutStore shortcuts, string folder, string currentExeName, TimeSpan wait)
    {
        var old = OldExePaths(folder, currentExeName);
        if (old.Count == 0)
            return;
        var newExe = Path.Combine(folder, currentExeName);
        // Without the Windows Shell no shortcut can be read or moved, so the old exe stays for good.
        // That goes to the log once, not on every start.
        if (!shortcuts.ShellAvailable)
        {
            host.Log(folder, "no-shell", "Windows Script Host (WScript.Shell) is missing, so shortcuts to the old program cannot be moved. The old program stays.", once: true);
            return;
        }

        var result = RetargetShortcuts(shortcuts, old, newExe);
        if (result.Failed > 0)
        {
            host.Log(folder, "shortcut-failed", result.Failed + " shortcut(s) to the old program could not be moved to "
                + newExe + ". The old program stays and the next start tries again.", once: false);
            return;
        }

        var allGone = true;
        foreach (var path in old.Where(host.FileExists))
        {
            var until = DateTime.UtcNow + wait;
            while (host.IsRunning(path) && DateTime.UtcNow < until)
                Thread.Sleep(250);
            if (host.IsRunning(path) || !host.TryDelete(path))
                allGone = false;
        }

        if (allGone)
            host.TryDelete(Path.Combine(folder, SuccessorInstall.RenameMarker));
    }

    /// <summary>
    /// Repoints every shortcut whose target is one of the old exe paths. Others are never touched. A
    /// match that cannot be saved counts as failed, and so does a shortcut that cannot be read but
    /// whose bytes hold the full old exe path. Other unreadable shortcuts are ignored.
    /// </summary>
    public static ShortcutResult RetargetShortcuts(IShortcutStore shortcuts, IReadOnlyList<string> oldExePaths, string newExePath)
    {
        var names = oldExePaths.Select(path => Path.GetFileName(path)).ToList();
        var changed = 0;
        var failed = 0;
        foreach (var shortcut in shortcuts.Candidates(names))
        {
            if (!shortcuts.TryTarget(shortcut, out var target))
            {
                if (oldExePaths.Any(path => shortcuts.MentionsPath(shortcut, path)))
                    failed++;
                continue;
            }

            var match = oldExePaths.FirstOrDefault(path => SamePath(path, target));
            if (match == null)
                continue;
            if (shortcuts.TryRetarget(shortcut, match, newExePath))
                changed++;
            else
                failed++;
        }

        return new ShortcutResult(changed, failed);
    }

    /// <summary>
    /// True when a shortcut points at this exe, or may (it could not be read but holds the full path,
    /// or there is no Windows Shell to read it with). The install then leaves the old exe for
    /// the new exe's first start, which moves the shortcut before removing it.
    /// </summary>
    public static bool ShortcutsMayPointAt(IShortcutStore shortcuts, string exePath)
    {
        try
        {
            if (!shortcuts.ShellAvailable)
                return true;
            foreach (var shortcut in shortcuts.Candidates([Path.GetFileName(exePath)]))
            {
                if (shortcuts.TryTarget(shortcut, out var target) ? SamePath(target, exePath) : shortcuts.MentionsPath(shortcut, exePath))
                    return true;
            }

            return false;
        }
        catch (Exception)
        {
            return true;
        }
    }

    public static bool SamePath(string left, string right)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(left) || string.IsNullOrWhiteSpace(right))
                return false;
            return string.Equals(
                Path.GetFullPath(Environment.ExpandEnvironmentVariables(left.Trim().Trim('"'))),
                Path.GetFullPath(Environment.ExpandEnvironmentVariables(right.Trim().Trim('"'))),
                StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception)
        {
            return false;
        }
    }
}

public sealed class PhysicalRenameHost : IRenameHost
{
    public bool FileExists(string path) => File.Exists(path);

    public bool TryCopy(string from, string to)
    {
        try
        {
            File.Copy(from, to, overwrite: true);
            return new FileInfo(to).Length == new FileInfo(from).Length;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    public bool TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
                File.Delete(path);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    public bool IsRunning(string exePath)
    {
        foreach (var process in Process.GetProcessesByName(Path.GetFileNameWithoutExtension(exePath)))
        {
            using (process)
            {
                try
                {
                    if (process.MainModule?.FileName is { } file && ToolRename.SamePath(file, exePath))
                        return true;
                }
                catch (Exception)
                {
                    // An instance this process cannot read could be the old exe. Leave it for the next start.
                    return true;
                }
            }
        }

        return false;
    }

    public bool TryStart(string exePath, IReadOnlyList<string> arguments)
    {
        try
        {
            var start = new ProcessStartInfo(exePath)
            {
                UseShellExecute = false,
                WorkingDirectory = Path.GetDirectoryName(exePath) ?? ""
            };
            foreach (var argument in arguments)
                start.ArgumentList.Add(argument);
            using var process = Process.Start(start);
            return process != null;
        }
        catch (Exception)
        {
            return false;
        }
    }

    public Version? FileVersion(string path)
    {
        try
        {
            var info = FileVersionInfo.GetVersionInfo(path);
            return new Version(info.FileMajorPart, info.FileMinorPart, info.FileBuildPart, info.FilePrivatePart);
        }
        catch (Exception)
        {
            return null;
        }
    }

    public void Log(string folder, string key, string line, bool once)
    {
        try
        {
            var path = Path.Combine(folder, "update-install.log");
            var now = DateTime.Now;
            if (!ShouldLog(File.Exists(path) ? File.ReadLines(path) : [], key, once, now))
                return;
            File.AppendAllText(path, LogLine(key, line, now) + Environment.NewLine);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // The log is best effort.
        }
    }

    public static string LogLine(string key, string line, DateTime now) =>
        now.ToString("yyyy-MM-dd HH:mm:ss") + " [" + key + "] " + line;

    /// <summary>False when the log already has this key: ever with once, otherwise today.</summary>
    public static bool ShouldLog(IEnumerable<string> existing, string key, bool once, DateTime now)
    {
        var tag = " [" + key + "] ";
        var today = now.ToString("yyyy-MM-dd");
        return !existing.Any(line => line.Contains(tag, StringComparison.Ordinal) && (once || line.StartsWith(today, StringComparison.Ordinal)));
    }
}

/// <summary>
/// Shortcuts on the Desktop, in the Start Menu and pinned to the taskbar, read and saved through
/// the Windows Shell (WScript.Shell).
/// </summary>
public sealed class WindowsShortcutStore : IShortcutStore
{
    public static IReadOnlyList<string> Folders()
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        var pinned = Path.Combine(appData, "Microsoft", "Internet Explorer", "Quick Launch", "User Pinned");
        return new[]
        {
            // DesktopDirectory follows a Desktop that OneDrive moved.
            Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
            Environment.GetFolderPath(Environment.SpecialFolder.CommonDesktopDirectory),
            Environment.GetFolderPath(Environment.SpecialFolder.Programs),
            Environment.GetFolderPath(Environment.SpecialFolder.CommonPrograms),
            Path.Combine(pinned, "TaskBar"),
            Path.Combine(pinned, "ImplicitAppShortcuts")
        }.Where(folder => !string.IsNullOrEmpty(folder)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    public IEnumerable<string> Candidates(IReadOnlyCollection<string> fileNames)
    {
        // A shortcut stores its target path as plain or wide text, so a byte search skips the
        // shortcuts that cannot match before the slower Shell call.
        var needles = fileNames.SelectMany(name => new[] { Encoding.Default.GetBytes(name), Encoding.Unicode.GetBytes(name) }).ToList();
        foreach (var folder in Folders())
        {
            IEnumerable<string> files;
            try
            {
                if (!Directory.Exists(folder))
                    continue;
                files = Directory.EnumerateFiles(folder, "*.lnk", new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true }).ToList();
            }
            catch (Exception)
            {
                continue;
            }

            foreach (var file in files)
            {
                byte[] bytes;
                try
                {
                    bytes = File.ReadAllBytes(file);
                }
                catch (Exception)
                {
                    continue;
                }

                if (needles.Any(needle => Contains(bytes, needle)))
                    yield return file;
            }
        }
    }

    public bool ShellAvailable => Type.GetTypeFromProgID("WScript.Shell") != null;

    public bool MentionsPath(string shortcutPath, string path)
    {
        try
        {
            var bytes = File.ReadAllBytes(shortcutPath);
            return Contains(bytes, Encoding.Default.GetBytes(path)) || Contains(bytes, Encoding.Unicode.GetBytes(path));
        }
        catch (Exception)
        {
            return false;
        }
    }

    public bool TryTarget(string shortcutPath, out string target)
    {
        string? found = null;
        var read = WithShortcut(shortcutPath, link =>
        {
            found = Get(link, "TargetPath") as string;
            return found != null;
        });
        target = found ?? "";
        return read;
    }

    public bool TryRetarget(string shortcutPath, string oldTarget, string newTarget) =>
        WithShortcut(shortcutPath, link =>
        {
            if (Get(link, "TargetPath") is not string target || !ToolRename.SamePath(target, oldTarget))
                return false;
            Set(link, "TargetPath", newTarget);
            if (Get(link, "IconLocation") is string icon && icon.StartsWith(oldTarget, StringComparison.OrdinalIgnoreCase))
                Set(link, "IconLocation", newTarget + icon[oldTarget.Length..]);
            link.GetType().InvokeMember("Save", BindingFlags.InvokeMethod, null, link, null);
            return true;
        });

    // Opens one shortcut through WScript.Shell and always releases both COM objects after.
    private static bool WithShortcut(string shortcutPath, Func<object, bool> use)
    {
        object? shell = null;
        object? link = null;
        try
        {
            var type = Type.GetTypeFromProgID("WScript.Shell");
            if (type == null)
                return false;
            shell = Activator.CreateInstance(type);
            if (shell == null)
                return false;
            link = type.InvokeMember("CreateShortcut", BindingFlags.InvokeMethod, null, shell, [shortcutPath]);
            return link != null && use(link);
        }
        catch (Exception)
        {
            return false;
        }
        finally
        {
            Release(link);
            Release(shell);
        }
    }

    private static void Release(object? value)
    {
        if (value != null && OperatingSystem.IsWindows() && Marshal.IsComObject(value))
            Marshal.FinalReleaseComObject(value);
    }

    private static object? Get(object link, string name) =>
        link.GetType().InvokeMember(name, BindingFlags.GetProperty, null, link, null);

    private static void Set(object link, string name, object value) =>
        link.GetType().InvokeMember(name, BindingFlags.SetProperty, null, link, [value]);

    private static bool Contains(byte[] haystack, byte[] needle)
    {
        if (needle.Length == 0 || haystack.Length < needle.Length)
            return false;
        for (var i = 0; i <= haystack.Length - needle.Length; i++)
        {
            var hit = true;
            for (var j = 0; j < needle.Length; j++)
            {
                if (char.ToLowerInvariant((char)haystack[i + j]) != char.ToLowerInvariant((char)needle[j]))
                {
                    hit = false;
                    break;
                }
            }

            if (hit)
                return true;
        }

        return false;
    }
}
