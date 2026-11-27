using System.Text.RegularExpressions;
using BannerlordCoopLauncher.Core.Saves;

namespace TaomCoop.Core;

/// <summary>
/// The line the dedicated server writes once a save is fully on disk, for example
/// "[DedicatedServer] operator save: world saved to 'taomtest.sav' (2058 ms)". The .json next
/// to the save and the .autosave-transaction marker are written when the save starts, so a
/// newer .json does not mean the .sav was written. A server stopped before this line rolls the
/// save back to the copy from before that save on its next start.
/// </summary>
public static class WorldSavedLine
{
    private static readonly Regex SavedPattern = new(@"\[DedicatedServer\][^\r\n]*world saved to '(?<file>[^']+)'", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    /// <summary>The file the server says it saved ("taomtest.sav"), or null for any other line.</summary>
    public static string? SavedFile(string? line)
    {
        if (string.IsNullOrEmpty(line))
            return null;
        var match = SavedPattern.Match(line);
        return match.Success ? match.Groups["file"].Value : null;
    }

    /// <summary>True when the line says the named save (with or without .sav) is written.</summary>
    public static bool Saved(string? line, string saveName) => SameSave(SavedFile(line), saveName);

    /// <summary>True when a saved file name ("taomtest.sav") is the named save.</summary>
    public static bool SameSave(string? savedFile, string saveName)
    {
        if (string.IsNullOrWhiteSpace(savedFile) || string.IsNullOrWhiteSpace(saveName))
            return false;
        var name = saveName.EndsWith(".sav", StringComparison.OrdinalIgnoreCase) ? saveName[..^4] : saveName;
        return Path.GetFileNameWithoutExtension(savedFile).Equals(name, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>The marker the server keeps while a save is being written. It is a folder.</summary>
    public static string TransactionMarker(string savesDirectory, string saveName) =>
        SaveSet.TransactionMarker(savesDirectory, saveName);
}
