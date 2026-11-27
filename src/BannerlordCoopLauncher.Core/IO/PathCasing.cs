namespace BannerlordCoopLauncher.Core.IO;

public static class PathCasing
{
    public static string OnDisk(string path)
    {
        var full = Path.GetFullPath(path);
        var root = Path.GetPathRoot(full);
        if (string.IsNullOrEmpty(root))
            return full;

        if (root.Length >= 2 && root[1] == ':')
            root = char.ToUpperInvariant(root[0]) + root[1..];

        var current = root;
        var rest = full.Length > root.Length ? full[root.Length..] : "";
        foreach (var part in rest.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar))
        {
            if (part.Length == 0)
                continue;

            string? found = null;
            if (Directory.Exists(current))
            {
                try
                {
                    found = Directory.EnumerateFileSystemEntries(current, part).FirstOrDefault();
                }
                catch (IOException)
                {
                }
                catch (UnauthorizedAccessException)
                {
                }
            }

            current = found ?? Path.Combine(current.TrimEnd(Path.DirectorySeparatorChar), part);
        }

        return current;
    }
}
