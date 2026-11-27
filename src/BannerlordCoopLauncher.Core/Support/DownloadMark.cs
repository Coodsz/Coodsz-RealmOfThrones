namespace BannerlordCoopLauncher.Core.Support;

public static class DownloadMark
{
    public static int Clear(string directory)
    {
        if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory))
            throw new InvalidOperationException("That folder was not found.");

        var full = Path.GetFullPath(directory).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var root = Path.GetPathRoot(full);
        if (root != null && string.Equals(full, root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar), StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("That folder is too broad to unblock.");

        // Junctions and symbolic links are skipped, folders and files alike: following one would clear the marks of
        // whatever it points at, outside this folder. A folder that can't be read is skipped, and one locked
        // Zone.Identifier doesn't end the sweep. Skipping reparse points also skips OneDrive cloud placeholders (files not
        // downloaded yet), so a mark on one of those can stay.
        var options = new EnumerationOptions { RecurseSubdirectories = true, AttributesToSkip = FileAttributes.ReparsePoint, IgnoreInaccessible = true };
        var cleared = 0;
        foreach (var file in Directory.EnumerateFiles(full, "*", options))
        {
            var zone = file + ":Zone.Identifier";
            try
            {
                if (!File.Exists(zone))
                    continue;
                File.Delete(zone);
                cleared++;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
            }
        }

        return cleared;
    }
}
