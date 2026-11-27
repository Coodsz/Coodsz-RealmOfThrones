using System.IO;

namespace TaomCoop.Core;

public static class PlaytestZipFinder
{
    public const string HostOperatorPattern = TaomStack.HostKitPattern;
    public static IReadOnlyList<string> CommonFolders()
    {
        var folders = new List<string>();
        Add(folders, Environment.GetFolderPath(Environment.SpecialFolder.Desktop));
        Add(folders, Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory));
        Add(folders, Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads"));
        Add(folders, Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments));
        Add(folders, Environment.GetEnvironmentVariable("OneDrive"));
        Add(folders, Environment.GetEnvironmentVariable("OneDriveConsumer"));
        Add(folders, Environment.GetEnvironmentVariable("OneDriveCommercial"));
        return folders;
    }

    public static string? NewestInCommonFolders(string pattern) => Newest(pattern, CommonFolders());

    public static string? NewestOnThisPc(string pattern)
    {
        var found = NewestInCommonFolders(pattern);
        if (found != null)
            return found;

        return Newest(pattern, FixedDrives());
    }

    public static IEnumerable<string> FixedDrives() =>
        DriveInfo.GetDrives()
            .Where(drive => drive.IsReady && drive.DriveType == DriveType.Fixed)
            .Select(drive => drive.RootDirectory.FullName);

    public static string? Newest(string pattern, IEnumerable<string> roots) =>
        Matches(pattern, roots).FirstOrDefault();

    public static IEnumerable<string> Matches(string pattern, IEnumerable<string> roots) =>
        Matches(pattern, roots, ListFiles, ListFolders);

    /// <summary>
    /// <see cref="Matches(string, IEnumerable{string})"/> with the folder listing passed in, so a test can make
    /// one folder fail. A folder that cannot be read (no access, or a damaged folder that says "The file or
    /// directory is corrupted and unreadable") is skipped and the search goes on with the next one.
    /// </summary>
    public static IEnumerable<string> Matches(string pattern, IEnumerable<string> roots,
        Func<string, string, IEnumerable<string>> listFiles, Func<string, IEnumerable<string>> listFolders)
    {
        var found = new List<(string Path, DateTime Written)>();
        foreach (var root in roots)
        {
            if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root))
                continue;
            foreach (var file in Walk(root, pattern, listFiles, listFolders))
                found.Add((file, LastWritten(file)));
        }

        return found
            .OrderByDescending(file => file.Written)
            .Select(file => file.Path);
    }

    private static void Add(List<string> folders, string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return;
        if (folders.Any(folder => folder.Equals(path, StringComparison.OrdinalIgnoreCase)))
            return;
        folders.Add(path);
    }

    // Like the old Directory.EnumerateFiles(directory, pattern) (Win32 wildcards, hidden folders searched), but a
    // child the folder lists and then refuses is skipped instead of throwing.
    private static readonly EnumerationOptions Listing = new()
    {
        MatchType = MatchType.Win32,
        AttributesToSkip = 0,
        IgnoreInaccessible = true
    };

    private static IEnumerable<string> ListFiles(string directory, string pattern) =>
        Directory.EnumerateFiles(directory, pattern, Listing);

    private static IEnumerable<string> ListFolders(string directory) =>
        Directory.EnumerateDirectories(directory, "*", Listing);

    private static DateTime LastWritten(string file)
    {
        try
        {
            return File.GetLastWriteTimeUtc(file);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return DateTime.MinValue;
        }
    }

    // The listings are lazy and a damaged folder throws while one is read, not when it is made, so each is
    // read in full inside its try.
    private static IEnumerable<string> Walk(string root, string pattern,
        Func<string, string, IEnumerable<string>> listFiles, Func<string, IEnumerable<string>> listFolders)
    {
        var pending = new Stack<string>();
        pending.Push(root);
        while (pending.Count > 0)
        {
            var directory = pending.Pop();
            List<string> files;
            try
            {
                files = listFiles(directory, pattern).ToList();
            }
            catch (IOException)
            {
                continue;
            }
            catch (UnauthorizedAccessException)
            {
                continue;
            }

            foreach (var file in files)
                yield return file;

            List<string> children;
            try
            {
                children = listFolders(directory).ToList();
            }
            catch (IOException)
            {
                continue;
            }
            catch (UnauthorizedAccessException)
            {
                continue;
            }

            foreach (var child in children)
            {
                if (Skip(child))
                    continue;
                pending.Push(child);
            }
        }
    }

    private static bool Skip(string directory)
    {
        var name = Path.GetFileName(directory);
        if (name.Equals("Windows", StringComparison.OrdinalIgnoreCase)
            || name.Equals("$Recycle.Bin", StringComparison.OrdinalIgnoreCase)
            || name.Equals("System Volume Information", StringComparison.OrdinalIgnoreCase)
            || name.Equals("WinSxS", StringComparison.OrdinalIgnoreCase))
            return true;

        try
        {
            return (File.GetAttributes(directory) & FileAttributes.ReparsePoint) != 0;
        }
        catch (IOException)
        {
            return true;
        }
        catch (UnauthorizedAccessException)
        {
            return true;
        }
    }
}
