using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace BannerlordCoopLauncher.Core.Support;

public static class LogFolderOpen
{
    public const string ActionLogName = "host-action.log";
    public const string ClientActionLogName = "client-action.log";
    public const string ServerLogPattern = "coop-server-*.log";

    public static IReadOnlyList<string> FilesToHighlight(
        string directory,
        string logPattern = ServerLogPattern,
        string actionLogName = ActionLogName)
    {
        if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory))
            return Array.Empty<string>();

        var paths = new List<string>();
        try
        {
            if (!string.IsNullOrWhiteSpace(logPattern))
            {
                var newest = new DirectoryInfo(directory)
                    .GetFiles(logPattern, SearchOption.TopDirectoryOnly)
                    .OrderByDescending(file => file.LastWriteTimeUtc)
                    .ThenByDescending(file => file.Name, StringComparer.OrdinalIgnoreCase)
                    .FirstOrDefault();
                if (newest != null)
                    paths.Add(newest.FullName);
            }

            if (!string.IsNullOrWhiteSpace(actionLogName))
            {
                var action = Path.Combine(directory, actionLogName);
                var share = ShareCopy(action) ?? (File.Exists(action) ? action : null);
                if (share != null && !paths.Contains(share, StringComparer.OrdinalIgnoreCase))
                    paths.Add(share);
            }
        }
        catch
        {
        }

        return paths;
    }

    /// <summary>
    /// Copies the live action log to a dated, machine-tagged file so Discord/browser
    /// downloads do not all land as host-action (1).log from every player.
    /// Keeps writing to the original host-action.log / client-action.log.
    /// </summary>
    public static string? ShareCopy(string? livePath)
    {
        if (string.IsNullOrWhiteSpace(livePath) || !File.Exists(livePath))
            return null;

        try
        {
            var directory = Path.GetDirectoryName(livePath);
            if (string.IsNullOrWhiteSpace(directory))
                return null;

            var liveName = Path.GetFileName(livePath);
            if (!IsLiveActionLogName(liveName))
                return Path.GetFullPath(livePath);

            var stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss");
            var stem = Path.GetFileNameWithoutExtension(liveName);
            var extension = Path.GetExtension(liveName);
            var machine = SafeToken(Environment.MachineName);
            var baseName = string.IsNullOrEmpty(machine)
                ? stem + "-" + stamp + extension
                : stem + "-" + machine + "-" + stamp + extension;

            var destination = Path.Combine(directory, baseName);
            for (var attempt = 2; File.Exists(destination) && attempt < 100; attempt++)
            {
                var tagged = Path.GetFileNameWithoutExtension(baseName) + "-" + attempt + extension;
                destination = Path.Combine(directory, tagged);
            }

            File.Copy(livePath, destination, overwrite: false);
            return Path.GetFullPath(destination);
        }
        catch
        {
            return File.Exists(livePath) ? Path.GetFullPath(livePath) : null;
        }
    }

    public static void Open(string directory)
    {
        if (string.IsNullOrWhiteSpace(directory))
            return;

        Directory.CreateDirectory(directory);
        var highlight = FilesToHighlight(directory);
        if (highlight.Count > 0 && SelectInExplorer(directory, highlight))
            return;

        OpenDirectory(directory);
    }

    public static void Select(IEnumerable<string?> files)
    {
        var existing = files
            .Select(path => ShareCopy(path) ?? path)
            .Where(path => !string.IsNullOrWhiteSpace(path) && File.Exists(path))
            .Select(path => Path.GetFullPath(path!))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (existing.Count == 0)
            return;

        foreach (var group in existing.GroupBy(path => Path.GetDirectoryName(path)!, StringComparer.OrdinalIgnoreCase))
        {
            if (string.IsNullOrWhiteSpace(group.Key))
                continue;
            if (!SelectInExplorer(group.Key, group.ToList()))
                OpenDirectory(group.Key);
        }
    }

    private static bool IsLiveActionLogName(string fileName) =>
        fileName.Equals(ActionLogName, StringComparison.OrdinalIgnoreCase)
        || fileName.Equals(ClientActionLogName, StringComparison.OrdinalIgnoreCase);

    private static string SafeToken(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return "";

        var builder = new StringBuilder(value.Length);
        foreach (var ch in value.Trim())
        {
            if (char.IsAsciiLetterOrDigit(ch) || ch is '-' or '_')
                builder.Append(ch);
            else if (char.IsWhiteSpace(ch) || ch is '.' or ' ')
                builder.Append('-');
        }

        var token = builder.ToString().Trim('-');
        return token.Length > 32 ? token[..32] : token;
    }

    private static void OpenDirectory(string directory)
    {
        Process.Start(new ProcessStartInfo
        {
            FileName = "explorer.exe",
            Arguments = "\"" + directory + "\"",
            UseShellExecute = true
        });
    }

    private static bool SelectInExplorer(string directory, IReadOnlyList<string> files)
    {
        IntPtr folder = IntPtr.Zero;
        IntPtr[] items = Array.Empty<IntPtr>();
        try
        {
            if (SHParseDisplayName(directory, IntPtr.Zero, out folder, 0, out _) != 0 || folder == IntPtr.Zero)
                return false;

            items = new IntPtr[files.Count];
            var count = 0;
            for (var i = 0; i < files.Count; i++)
            {
                if (SHParseDisplayName(files[i], IntPtr.Zero, out var item, 0, out _) != 0 || item == IntPtr.Zero)
                    continue;
                items[count++] = item;
            }

            if (count == 0)
                return false;

            if (count != items.Length)
                Array.Resize(ref items, count);

            return SHOpenFolderAndSelectItems(folder, (uint)count, items, 0) == 0;
        }
        catch
        {
            return false;
        }
        finally
        {
            if (folder != IntPtr.Zero)
                Marshal.FreeCoTaskMem(folder);
            foreach (var item in items)
            {
                if (item != IntPtr.Zero)
                    Marshal.FreeCoTaskMem(item);
            }
        }
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SHParseDisplayName(
        string pszName,
        IntPtr pbc,
        out IntPtr ppidl,
        uint sfgaoIn,
        out uint psfgaoOut);

    [DllImport("shell32.dll")]
    private static extern int SHOpenFolderAndSelectItems(IntPtr pidlFolder, uint cidl, IntPtr[] apidl, uint dwFlags);
}
