using System.Globalization;
using System.Text.RegularExpressions;

namespace BannerlordCoopLauncher.Core.Saves;

/// <summary>
/// A copy of a campaign save made before it is used with a different mod list for the first time,
/// and the way back. Backups are named &lt;save&gt;-&lt;yyyyMMdd-HHmmss&gt;.sav in their own folder (not in Game
/// Saves, so the server does not list them); a .json next to the save is copied with it.
/// </summary>
public static class SaveBackup
{
    private static readonly Regex Stamp = new(@"^(?<name>.+)-(?<stamp>\d{8}-\d{6})(-\d+)?$", RegexOptions.Compiled, TimeSpan.FromSeconds(1));

    public static string Create(string savePath, string backupFolder, DateTime? now = null)
    {
        if (!File.Exists(savePath))
            throw new InvalidOperationException("The save to back up was not found.");
        Directory.CreateDirectory(backupFolder);
        var name = Path.GetFileNameWithoutExtension(savePath);
        var stamp = (now ?? DateTime.Now).ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);
        var target = Path.Combine(backupFolder, name + "-" + stamp + ".sav");
        for (var n = 2; File.Exists(target); n++)
            target = Path.Combine(backupFolder, name + "-" + stamp + "-" + n + ".sav");
        File.Copy(savePath, target);
        var json = Path.ChangeExtension(savePath, ".json");
        if (File.Exists(json))
            File.Copy(json, Path.ChangeExtension(target, ".json"), overwrite: true);
        return target;
    }

    /// <summary>The save name a backup belongs to, or null when the file name is not a backup.</summary>
    public static string? SaveNameOf(string backupPath)
    {
        var match = Stamp.Match(Path.GetFileNameWithoutExtension(backupPath));
        return match.Success ? match.Groups["name"].Value : null;
    }

    /// <summary>Backups of one save, newest first.</summary>
    public static IReadOnlyList<string> List(string backupFolder, string saveName)
    {
        if (!Directory.Exists(backupFolder))
            return Array.Empty<string>();
        return Directory.EnumerateFiles(backupFolder, "*.sav")
            .Where(path => string.Equals(SaveNameOf(path), saveName, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(File.GetLastWriteTimeUtc)
            .ToList();
    }

    /// <summary>Puts a backup back as the save it came from. The save as it is now is backed up first,
    /// so a restore can be undone. Returns that new backup, or null when there was no save.</summary>
    public static string? Restore(string backupPath, string savesFolder, string backupFolder)
    {
        var name = SaveNameOf(backupPath) ?? throw new InvalidOperationException("That file is not a save backup.");
        var save = Path.Combine(savesFolder, name + ".sav");
        var undo = File.Exists(save) ? Create(save, backupFolder) : null;
        Directory.CreateDirectory(savesFolder);
        File.Copy(backupPath, save, overwrite: true);
        var json = Path.ChangeExtension(backupPath, ".json");
        if (File.Exists(json))
            File.Copy(json, Path.ChangeExtension(save, ".json"), overwrite: true);
        return undo;
    }
}
