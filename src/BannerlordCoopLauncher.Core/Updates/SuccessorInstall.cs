using System.Security.Cryptography;

namespace BannerlordCoopLauncher.Core.Updates;

/// <summary>A payload file the install copies, from the extracted zip to the install folder.</summary>
public sealed record UpdateFile(string From, string To);

/// <summary>
/// An install from a two exe zip. The successor exe (this tool's role under its new name) and the
/// payload files beside it are copied, but never the other program's exe, so a running sibling or
/// another install folder is never touched. After the copy the old exe this tool started from and
/// its loose files go, unless a shortcut still points at the old exe. Then both stay, and the new exe moves
/// the shortcut and removes the old exe, and a later start its loose files (ToolUpdate.FirstStartFiles).
/// </summary>
public sealed record SuccessorInstall(
    string SourceExe,
    string InstallDirectory,
    string OldExePath,
    string NewExePath,
    IReadOnlyList<string> RemoveAfterCopy,
    string SourceSha256 = "",
    IReadOnlyList<UpdateFile>? Files = null,
    bool KeepOldExe = false)
{
    /// <summary>The file the next start of the new exe reads to fix shortcuts once.</summary>
    public const string RenameMarker = "rename-cleanup.pending";

    public IReadOnlyList<UpdateFile> OtherFiles => Files ?? [];

    public static SuccessorInstall Prepare(string payloadDirectory, string successorExeName, string installDirectory, string oldExePath, bool keepOldExe = false)
    {
        var source = Path.Combine(payloadDirectory, successorExeName);
        var newExe = Path.Combine(installDirectory, successorExeName);
        var oldName = Path.GetFileName(oldExePath);
        var payloadTop = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var files = new List<UpdateFile>();
        var siblings = ToolUpdate.SiblingBaseNames(successorExeName);
        var root = Path.GetFullPath(payloadDirectory).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        if (Directory.Exists(payloadDirectory))
        {
            foreach (var path in Directory.EnumerateFiles(payloadDirectory, "*", SearchOption.AllDirectories).OrderBy(path => path, StringComparer.OrdinalIgnoreCase))
            {
                var relative = Path.GetFullPath(path)[root.Length..];
                var name = Path.GetFileName(path);
                var topLevel = !relative.Contains(Path.DirectorySeparatorChar);
                if (topLevel)
                    payloadTop.Add(name);
                // The successor exe goes last, on its own, so a failed copy leaves the old exe whole.
                if (topLevel && name.Equals(successorExeName, StringComparison.OrdinalIgnoreCase))
                    continue;
                // The other program in the zip and anything named after it belong to the sibling.
                if (topLevel && siblings.Contains(ProgramBase(name)))
                    continue;
                if (!ToolUpdate.ShouldReplace(relative))
                    continue;
                files.Add(new UpdateFile(path, Path.Combine(installDirectory, relative)));
            }
        }

        // Same rules as the first start of a single file program, worked out now while this process
        // still knows its folder. The new exe is single file, so the old loose files (TAOM) go too, but
        // only files this tool owns under its new or old exe name. An old exe kept for a shortcut must still
        // start, so it keeps every loose file it loads; the new exe's first start removes them once the
        // shortcut has moved and the old exe is gone.
        var remove = keepOldExe
            ? []
            : ToolUpdate.PreviousVersionFiles(installDirectory, successorExeName, singleFile: true, [oldName])
                .Where(path => !Path.GetFileName(path).Equals(oldName, StringComparison.OrdinalIgnoreCase)
                    && !payloadTop.Contains(Path.GetFileName(path)))
                .ToList();
        var sha = File.Exists(source) ? Hash(source) : "";
        return new SuccessorInstall(source, installDirectory, oldExePath, newExe, remove, sha, files, keepOldExe);
    }

    public bool Renames => !string.Equals(Path.GetFullPath(OldExePath), Path.GetFullPath(NewExePath), StringComparison.OrdinalIgnoreCase);

    public static string Hash(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        return Convert.ToHexString(SHA256.HashData(stream));
    }

    private static string ProgramBase(string fileName)
    {
        foreach (var suffix in new[] { ".deps.json", ".runtimeconfig.json", ".dll", ".pdb", ".exe" })
        {
            if (fileName.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
                return fileName[..^suffix.Length];
        }

        return fileName;
    }
}
