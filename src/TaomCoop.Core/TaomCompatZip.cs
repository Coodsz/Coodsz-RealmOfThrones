using System.Reflection;
using BannerlordCoopLauncher.Core.IO;

namespace TaomCoop.Core;

/// <summary>
/// One TAOM-Coop CompatOnly zip: the CLIENT zip the client launcher installs TAOM.CoopCompat from, or the
/// HOST-OPERATOR zip the server tool unpacks its host kit from.
/// </summary>
public sealed record TaomCompatKit(string Name, string Sha256, string Pattern)
{
    public static readonly TaomCompatKit Client = new(TaomStack.ClientKitName, TaomStack.ClientKitSha256, TaomStack.ClientKitPattern);
    public static readonly TaomCompatKit Host = new(TaomStack.HostKitName, TaomStack.HostKitSha256, TaomStack.HostKitPattern);

    /// <summary>The resource a release build carries this zip in (src\TaomCompatZip.targets).</summary>
    public string ResourceName => TaomCompatZip.ResourcePrefix + Name;
}

public enum CompatZipFrom
{
    ThisPc,
    Embedded,
    // The exe carries the zip but could not write it out (Problem says why), and no copy on this PC fits.
    WriteFailed,
    Missing
}

public sealed record CompatZipPick(CompatZipFrom From, string? Path, string? Problem = null)
{
    public static readonly CompatZipPick Missing = new(CompatZipFrom.Missing, null);
}

/// <summary>What writing out the carried zip gave: its path, or why it could not be written.</summary>
public sealed record CompatZipWrite(string? Path, string? Problem);

/// <summary>
/// Finds the CompatOnly zip a TAOM tool needs. A copy on this PC is used first, like before. Only when there
/// is none, a release build writes out the copy it carries inside it to the tool's own folder under
/// %LOCALAPPDATA%\Coodsz\TAOM. Either way the zip must have the SHA-256 pinned in TaomStack; a copy with any
/// other hash is never used.
/// </summary>
public static class TaomCompatZip
{
    public const string ResourcePrefix = "Coodsz.TaomCompat.";
    public const string FolderName = "CompatOnly";

    /// <summary>
    /// The first copy on this PC with the expected hash, else the embedded copy when it has that hash, else
    /// missing. The embedded copy is only asked for (and written out) when no copy on this PC fits. When it
    /// could not be written, <paramref name="afterFailedWrite"/> is searched like a build without it would,
    /// and WriteFailed comes back with the reason when that finds nothing either.
    /// </summary>
    public static CompatZipPick Choose(IEnumerable<string> onThisPc, Func<string, string> sha256, string expected,
        Func<CompatZipWrite?> embedded, IEnumerable<string>? afterFailedWrite = null)
    {
        var found = FirstMatch(onThisPc, sha256, expected);
        if (found != null)
            return new CompatZipPick(CompatZipFrom.ThisPc, found);

        var carried = embedded();
        if (carried?.Path != null && Matches(sha256(carried.Path), expected))
            return new CompatZipPick(CompatZipFrom.Embedded, carried.Path);
        if (carried?.Problem == null)
            return CompatZipPick.Missing;

        found = FirstMatch(afterFailedWrite ?? [], sha256, expected);
        return found != null
            ? new CompatZipPick(CompatZipFrom.ThisPc, found)
            : new CompatZipPick(CompatZipFrom.WriteFailed, null, carried.Problem);
    }

    /// <summary>
    /// Looks in Downloads, the Desktop, Documents and OneDrive, and with <paramref name="wholePc"/> every fixed
    /// drive too. A tool that carries the zip skips the drive search: any copy with the pinned hash is the same
    /// file, so its own copy does just as well and the slow search is not needed. It still searches the drives
    /// when its own copy could not be written out.
    /// </summary>
    public static CompatZipPick Find(TaomCompatKit kit, bool wholePc, bool useEmbedded = true)
    {
        var carries = useEmbedded && Carries(kit);
        return Choose(
            OnThisPc(kit.Pattern, wholePc && !carries),
            FileHash.TrySha256,
            kit.Sha256,
            () => carries ? WriteOut(kit, () => Open(kit), Folder()) : null,
            wholePc && carries ? PlaytestZipFinder.Matches(kit.Pattern, PlaytestZipFinder.FixedDrives()) : null);
    }

    public static bool Carries(TaomCompatKit kit) =>
        Assembly.GetEntryAssembly()?.GetManifestResourceInfo(kit.ResourceName) != null;

    /// <summary>Where the embedded copy goes: CompatOnly in the tool's own settings folder.</summary>
    public static string Folder() =>
        ToolData.Current != null
            ? Path.Combine(ToolData.Current.Folder, FolderName)
            : Path.Combine(ToolData.DefaultRoot, ToolData.Taom, FolderName);

    /// <summary>
    /// Writes the carried zip to <paramref name="folder"/> unless a copy with the right hash is already
    /// there (a copy with another hash is replaced). Null when the exe carries none or its copy does not
    /// match; a Problem with the IO or access reason when it could not be written.
    /// </summary>
    public static CompatZipWrite? WriteOut(TaomCompatKit kit, Func<Stream?> open, string folder)
    {
        var target = Path.Combine(folder, kit.Name);
        try
        {
            if (File.Exists(target) && Matches(FileHash.Sha256(target), kit.Sha256))
                return new CompatZipWrite(target, null);
            using var zip = open();
            if (zip == null)
                return null;
            Directory.CreateDirectory(folder);
            // A name of this process's own, so a server tool and a client launcher never write the same file.
            var fresh = target + ".new-" + Environment.ProcessId + "-" + Guid.NewGuid().ToString("N")[..8];
            try
            {
                using (var file = File.Create(fresh))
                    zip.CopyTo(file);
                if (!Matches(FileHash.Sha256(fresh), kit.Sha256))
                    return null;
                File.Move(fresh, target, overwrite: true);
                return new CompatZipWrite(target, null);
            }
            finally
            {
                if (File.Exists(fresh))
                    File.Delete(fresh);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return new CompatZipWrite(null, ex.Message);
        }
    }

    // The search is lazy, so a folder it cannot read throws here. That counts as not found, so the player gets
    // the usual "not found" text instead of the raw Windows error.
    private static string? FirstMatch(IEnumerable<string> paths, Func<string, string> sha256, string expected)
    {
        try
        {
            return paths.FirstOrDefault(path => Matches(sha256(path), expected));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static Stream? Open(TaomCompatKit kit) =>
        Assembly.GetEntryAssembly()?.GetManifestResourceStream(kit.ResourceName);

    private static bool Matches(string sha256, string expected) =>
        sha256.Length > 0 && string.Equals(sha256, expected, StringComparison.OrdinalIgnoreCase);

    // Newest first in the usual folders, then (only when asked) on every fixed drive. Lazy, so the drives
    // are only searched when no copy in the usual folders has the right hash.
    private static IEnumerable<string> OnThisPc(string pattern, bool wholePc)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var file in PlaytestZipFinder.Matches(pattern, PlaytestZipFinder.CommonFolders()))
        {
            if (seen.Add(file))
                yield return file;
        }

        if (!wholePc)
            yield break;
        foreach (var file in PlaytestZipFinder.Matches(pattern, PlaytestZipFinder.FixedDrives()))
        {
            if (seen.Add(file))
                yield return file;
        }
    }
}
