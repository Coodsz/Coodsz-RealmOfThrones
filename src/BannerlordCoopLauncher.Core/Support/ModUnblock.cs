namespace BannerlordCoopLauncher.Core.Support;

/// <summary>The Windows download mark (Mark of the Web) on a file: its Zone.Identifier stream.</summary>
public interface IZoneMarks
{
    bool Has(string file);
    void Remove(string file);
}

/// <summary>NTFS: the mark is the alternate data stream "file:Zone.Identifier".</summary>
public sealed class FileZoneMarks : IZoneMarks
{
    public static readonly FileZoneMarks Instance = new();

    public bool Has(string file) => File.Exists(file + ":Zone.Identifier");

    public void Remove(string file) => File.Delete(file + ":Zone.Identifier");
}

/// <summary>
/// Unblocks the DLLs of a mod. Windows marks files from a downloaded zip (Nexus Mods, ModDB) as
/// coming from the internet, and the game cannot load a marked DLL; the TAOM Discord fixes Armies of
/// Middle Earth, Party AI Controls, Limitless and many startup crashes this way, and the mark comes
/// back when a mod is updated from a new zip. Only .dll files under the mod's bin folder are touched.
/// </summary>
public static class ModUnblock
{
    public static IReadOnlyList<string> BlockedDlls(string moduleDirectory, IZoneMarks? marks = null)
    {
        marks ??= FileZoneMarks.Instance;
        var bin = Path.Combine(moduleDirectory, "bin");
        if (!Directory.Exists(bin))
            return Array.Empty<string>();
        try
        {
            return Directory.EnumerateFiles(bin, "*.dll", SearchOption.AllDirectories).Where(marks.Has).ToList();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return Array.Empty<string>();
        }
    }

    /// <summary>Removes the mark from every marked DLL of the mod. Returns how many were unblocked.</summary>
    public static int UnblockDlls(string moduleDirectory, IZoneMarks? marks = null)
    {
        marks ??= FileZoneMarks.Instance;
        var count = 0;
        foreach (var dll in BlockedDlls(moduleDirectory, marks))
        {
            marks.Remove(dll);
            count++;
        }

        return count;
    }

    public static string Line(string label, int count) => count == 1
        ? "Unblocked 1 DLL file in " + label + " that Windows had marked as downloaded from the internet."
        : "Unblocked " + count + " DLL files in " + label + " that Windows had marked as downloaded from the internet.";
}
