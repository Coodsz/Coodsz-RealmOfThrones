// Ported from CoopKeep (https://github.com/Aderimo/bannerlord-coopkeep), Saves/BackupService.cs,
// and changed for the Coodsz tools (.launcher.json kept with the save, Coop's own .backup copies
// restored the same way, backups kept outside Game Saves).
//
// Copyright (c) 2026 aderimo
//
// Permission is hereby granted, free of charge, to any person obtaining a copy
// of this software and associated documentation files (the "Software"), to deal
// in the Software without restriction, including without limitation the rights
// to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
// copies of the Software, and to permit persons to whom the Software is
// furnished to do so, subject to the following conditions:
//
// The above copyright notice and this permission notice shall be included in all
// copies or substantial portions of the Software.
//
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
// IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
// FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
// AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
// LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
// OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
// SOFTWARE.

using System.Globalization;
using System.IO.Compression;
using System.Text.RegularExpressions;

namespace BannerlordCoopLauncher.Core.Saves;

/// <summary>How many backups are kept.</summary>
/// <param name="MaxCount">Most backups kept per save. 0 means no count limit.</param>
/// <param name="MaxAgeDays">Backups older than this are removed. 0 means no age limit.</param>
/// <remarks>
/// Both limits apply: a count alone lets one busy night push out a week of history,
/// an age alone lets the folder grow without end.
/// </remarks>
public sealed record BackupPolicy(int MaxCount = 10, int MaxAgeDays = 30)
{
    public static BackupPolicy Default { get; } = new();
}

/// <summary>Why a backup was made. It is the end of the file name (_auto, _manual and so on).</summary>
public enum BackupReason { Unknown, Manual, Autosave, Shutdown, BeforeRestore, BeforeRescue }

/// <summary>One backup on disk.</summary>
public sealed record BackupEntry(string FilePath, string SaveName, DateTime CreatedUtc, long SizeBytes)
{
    public DateTime CreatedLocal => CreatedUtc.ToLocalTime();

    public BackupReason Reason { get; init; }

    internal static readonly (BackupReason Reason, string Suffix)[] Suffixes =
    {
        (BackupReason.Manual, "_manual"),
        (BackupReason.Autosave, "_auto"),
        (BackupReason.Shutdown, "_shutdown"),
        (BackupReason.BeforeRestore, "_prerestore"),
        (BackupReason.BeforeRescue, "_prerescue"),
    };

    internal static string SuffixOf(BackupReason reason) =>
        Suffixes.FirstOrDefault(item => item.Reason == reason).Suffix ?? "";

    internal static BackupReason ReasonFromFileName(string path)
    {
        var stem = Path.GetFileNameWithoutExtension(path);
        foreach (var (reason, suffix) in Suffixes)
        {
            if (stem.EndsWith(suffix, StringComparison.Ordinal))
                return reason;
        }

        return BackupReason.Unknown;
    }
}

/// <summary>
/// Makes, keeps and restores zip backups of dedicated server saves.
/// </summary>
/// <remarks>
/// <para>
/// Backups live in <c>&lt;backup root&gt;\&lt;save name&gt;\&lt;name&gt;_&lt;utc stamp&gt;_&lt;reason&gt;.zip</c>,
/// outside Game Saves so the server never lists them. Coop's own .backup1 and .backup2
/// copies are left alone, except by a restore. Putting back a backup without a .json removes
/// them, and putting back a backup that holds copies puts those in their place. Either way the
/// backup made just before the restore keeps the copies that were there, even when the .sav
/// itself is missing. They cannot be trusted alone either, because a stop with F10 can leave
/// all three with the same content (Coop issue #3341).
/// </para>
/// <para>
/// A backup holds the .sav with its .json, plus the .launcher.json when there is one.
/// A .sav the server never saved (a single player save just imported) has no .json yet;
/// its backup holds a marker instead, and putting it back removes any .json the server
/// wrote since. A save that had a .json and lost it is not backed up: a .sav put back
/// alone breaks the player mapping.
/// </para>
/// </remarks>
public sealed class BackupService
{
    private readonly BackupPolicy _policy;

    public BackupService(string gameSavesDirectory, string backupRoot, BackupPolicy? policy = null)
    {
        GameSavesDirectory = Path.GetFullPath(gameSavesDirectory);
        BackupRoot = Path.GetFullPath(backupRoot);
        _policy = policy ?? BackupPolicy.Default;
    }

    public string GameSavesDirectory { get; }

    public string BackupRoot { get; }

    public string FolderFor(string saveName)
    {
        if (!SaveSet.IsValidSaveName(saveName))
            throw new ArgumentException("That is not a save name that can be used as a file name.", nameof(saveName));
        return Path.Combine(BackupRoot, saveName);
    }

    private string LivePath(string saveName, string extension) => Path.Combine(GameSavesDirectory, saveName + extension);

    /// <summary>Runs after the save files are copied into the zip and before they are checked again. Tests only.</summary>
    internal Action? AfterCopy { get; set; }

    /// <summary>Makes a backup of one save now.</summary>
    /// <remarks>
    /// Call it after the server said the save was written. A backup made while the
    /// server writes can hold half a file. The zip is written to a temporary name first,
    /// so a cut off write never shows up as a backup. The server writes the .sav in place,
    /// so a .sav that changed while it was read throws an IOException and the caller tries
    /// again once the newer save is done. An autosave or shutdown backup also throws one
    /// while the server's save marker is there.
    /// </remarks>
    public BackupEntry Create(string saveName, BackupReason reason = BackupReason.Unknown) =>
        Create(saveName, reason, prune: true);

    // withServerCopies: the server's .backup1 and .backup2 pairs go in too (the backup before a
    // restore that removes them). Their .json keeps the players, so a lost live .json is no refusal then,
    // and with no live .sav the backup holds just the copies.
    // withoutSave: the backup before a restore over a missing .sav, so a lone .json or .launcher.json
    // left there is kept too. It gets a marker so only such a backup goes back without a .sav.
    private BackupEntry Create(string saveName, BackupReason reason, bool prune, bool withServerCopies = false, bool withoutSave = false)
    {
        var folder = FolderFor(saveName);
        var sav = LivePath(saveName, SaveSet.SaveExtension);
        var json = LivePath(saveName, SaveSet.CompanionExtension);
        var noSav = !File.Exists(sav);
        if (noSav && !(withServerCopies && HasServerCopies(saveName)) && !(withoutSave && HasSetFile(saveName)))
            throw new FileNotFoundException("There is no " + saveName + ".sav to back up.", sav);
        var noJson = !File.Exists(json);
        if (noJson && LostCompanion(saveName) && !withServerCopies)
            throw new InvalidOperationException(saveName + ".json is missing, but the server saved this campaign with one before. A backup without it would lose which player has which hero, so none was made.");
        // Only these run while the server saves. A marker left by a crash must not block the
        // backups made with the server stopped (before a restore, a rescue or a start).
        var live = reason is BackupReason.Autosave or BackupReason.Shutdown;
        if (live && SaveSet.BeingSaved(GameSavesDirectory, saveName))
            throw new IOException(saveName + " is still being saved. The backup waits for the save to finish.");

        Directory.CreateDirectory(folder);
        // Milliseconds keep two backups of the same second apart and in order by name.
        var stamp = DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fff", CultureInfo.InvariantCulture);
        var suffix = BackupEntry.SuffixOf(reason);
        var target = Path.Combine(folder, saveName + "_" + stamp + suffix + ".zip");
        for (var n = 1; File.Exists(target); n++)
            target = Path.Combine(folder, saveName + "_" + stamp + "-" + n + suffix + ".zip");

        var temp = target + ".tmp";
        try
        {
            var before = Stamp(sav);
            using (var zip = ZipFile.Open(temp, ZipArchiveMode.Create))
            {
                foreach (var extension in withServerCopies ? SaveSet.Extensions.Concat(ServerCopyExtensions) : SaveSet.Extensions)
                {
                    var path = LivePath(saveName, extension);
                    if (File.Exists(path))
                        AddShared(zip, path, saveName + extension);
                }

                if (noJson)
                    zip.CreateEntry(saveName + NoCompanionMarker).LastWriteTime = DateTimeOffset.Now;
                if (noSav)
                    zip.CreateEntry(saveName + NoSaveMarker).LastWriteTime = DateTimeOffset.Now;
            }

            AfterCopy?.Invoke();
            // A newer save started while the zip read the .sav: the zip holds part of each.
            if (Stamp(sav) != before || (live && SaveSet.BeingSaved(GameSavesDirectory, saveName)))
                throw new IOException(saveName + ".sav changed while it was backed up. The backup waits for the newer save to finish.");

            File.Move(temp, target);
        }
        catch
        {
            TryDelete(temp);
            throw;
        }

        if (prune)
            Prune(saveName);
        var info = new FileInfo(target);
        return new BackupEntry(target, saveName, info.LastWriteTimeUtc, info.Length) { Reason = reason };
    }

    /// <summary>
    /// The zip entry that says the save had no .json when it was backed up. Any backup can hold it,
    /// the backup made before a restore too.
    /// </summary>
    internal const string NoCompanionMarker = ".no-json";

    /// <summary>
    /// The zip entry that says the save had no .sav when it was backed up (only a backup made before
    /// a restore). Putting such a backup back returns that state, so the live .sav is removed.
    /// </summary>
    internal const string NoSaveMarker = ".no-sav";

    // The dedicated server copies a save into .backup1 and .backup2 only as a pair with its .json
    // (SaveFileHistory.OpenPair needs both), so a .json there means this save had one before.
    // A .sav that never had one (imported, not saved by the server yet) has no such copies.
    private bool LostCompanion(string saveName) =>
        File.Exists(LivePath(saveName, ".backup1" + SaveSet.CompanionExtension))
        || File.Exists(LivePath(saveName, ".backup2" + SaveSet.CompanionExtension));

    // The server's own copies (SaveFileHistory names them <name>.backup1 and .backup2, each a .sav
    // and a .json). A save put back from a backup without a .json is older than all of them, so
    // the restore removes them too; left there they would make LostCompanion refuse every backup.
    // The backup made before that restore keeps them (just them when the live .sav is missing),
    // and putting it back returns them.
    private static readonly string[] ServerCopyExtensions =
    {
        ".backup1" + SaveSet.SaveExtension, ".backup1" + SaveSet.CompanionExtension,
        ".backup2" + SaveSet.SaveExtension, ".backup2" + SaveSet.CompanionExtension,
    };

    private bool HasServerCopies(string saveName) =>
        ServerCopyExtensions.Any(extension => File.Exists(LivePath(saveName, extension)));

    private bool HasSetFile(string saveName) =>
        SaveSet.Extensions.Any(extension => File.Exists(LivePath(saveName, extension)));

    // Create would refuse this save (no .sav, or a .json that went missing).
    private bool CanBackUp(string saveName) =>
        File.Exists(LivePath(saveName, SaveSet.SaveExtension))
        && (File.Exists(LivePath(saveName, SaveSet.CompanionExtension)) || !LostCompanion(saveName));

    private static (long Length, DateTime WrittenUtc) Stamp(string path)
    {
        var info = new FileInfo(path);
        return info.Exists ? (info.Length, info.LastWriteTimeUtc) : (-1, DateTime.MinValue);
    }

    // The server may still hold the save open for reading; share it instead of failing.
    private static void AddShared(ZipArchive zip, string path, string entryName)
    {
        using var source = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        var entry = zip.CreateEntry(entryName, CompressionLevel.Optimal);
        entry.LastWriteTime = File.GetLastWriteTime(path);
        using var target = entry.Open();
        source.CopyTo(target);
    }

    /// <summary>The backups of every save, newest first.</summary>
    public IReadOnlyList<BackupEntry> ListAll()
    {
        if (!Directory.Exists(BackupRoot))
            return Array.Empty<BackupEntry>();
        return Directory.EnumerateDirectories(BackupRoot)
            .Select(Path.GetFileName)
            .Where(SaveSet.IsValidSaveName)
            .SelectMany(name => List(name!))
            .OrderByDescending(entry => entry.CreatedUtc)
            .ThenByDescending(entry => NameOrder(entry.FilePath))
            .ThenByDescending(entry => entry.FilePath, StringComparer.Ordinal)
            .ToList();
    }

    /// <summary>The backups of one save, newest first.</summary>
    public IReadOnlyList<BackupEntry> List(string saveName)
    {
        var folder = FolderFor(saveName);
        if (!Directory.Exists(folder))
            return Array.Empty<BackupEntry>();
        return Directory.EnumerateFiles(folder, "*.zip")
            .Select(path => new FileInfo(path))
            .Select(file => new BackupEntry(file.FullName, saveName, file.LastWriteTimeUtc, file.Length)
            {
                Reason = BackupEntry.ReasonFromFileName(file.Name),
            })
            .OrderByDescending(entry => entry.CreatedUtc)
            .ThenByDescending(entry => NameOrder(entry.FilePath))
            .ThenByDescending(entry => entry.FilePath, StringComparer.Ordinal)
            .ToList();
    }

    // Two backups written in the same millisecond (a file system that keeps coarse times shows them
    // as equal) are told apart by the name Create gave them: the stamp, then the -n it adds when that
    // name was taken. Ordinal order alone puts "-483.zip" after "-483-1.zip", the wrong way round.
    // Anchored to the end (stamp, -n, then the reason suffix like _auto), so a save named like a stamp
    // (World_20261027-120000-000) is not read as the backup stamp.
    private static readonly Regex StampInName = new(@"_(\d{8}-\d{6}-\d{3})(?:-(\d+))?(?:_[a-z]+)?\.zip$", RegexOptions.CultureInvariant);

    private static (string Stamp, int Taken) NameOrder(string path)
    {
        var match = StampInName.Match(Path.GetFileName(path));
        if (!match.Success)
            return ("", 0);
        var taken = match.Groups[2].Success && int.TryParse(match.Groups[2].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var n) ? n : 0;
        return (match.Groups[1].Value, taken);
    }

    /// <summary>Puts a zip backup back over its save. The server must be stopped.</summary>
    /// <remarks>
    /// The save as it is now gets its own backup first, so a wrong pick can be undone.
    /// The files are unpacked next to the backups, swapped in, and on any error the old
    /// files go back. The save then has exactly the files the backup holds, so a backup of a
    /// .sav without a .json removes the .json the server wrote later, and the server's
    /// .backup1 and .backup2 copies of those later saves. Those copies go into the backup made
    /// before the restore (just the copies when the .sav is missing), and putting that backup
    /// back returns them. A backup that holds its own copies puts exactly those in, the same
    /// way over a missing .sav: the live copies go into the backup before the restore, and a
    /// live copy the zip does not have is removed. A lone .json or .launcher.json left by a
    /// missing .sav goes into the backup before the restore too.
    /// </remarks>
    /// <returns>The backup made of the save before the restore, or null when there was nothing to back up.</returns>
    public BackupEntry? Restore(BackupEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        if (!File.Exists(entry.FilePath))
            throw new FileNotFoundException("The backup file was not found.", entry.FilePath);

        var staging = NewStaging(entry.SaveName);
        var noJson = false;
        var noSav = false;
        var serverCopies = false;
        try
        {
            using (var zip = ZipFile.OpenRead(entry.FilePath))
            {
                // Only the save's own file names are read from the zip, so a crafted entry
                // name cannot write anywhere else.
                foreach (var extension in SaveSet.Extensions)
                    zip.GetEntry(entry.SaveName + extension)?.ExtractToFile(Path.Combine(staging, entry.SaveName + extension));
                noJson = zip.GetEntry(entry.SaveName + SaveSet.CompanionExtension) == null
                    && zip.GetEntry(entry.SaveName + NoCompanionMarker) != null;
                noSav = zip.GetEntry(entry.SaveName + SaveSet.SaveExtension) == null
                    && zip.GetEntry(entry.SaveName + NoSaveMarker) != null;
                foreach (var extension in ServerCopyExtensions)
                {
                    if (zip.GetEntry(entry.SaveName + extension) is not { } copy)
                        continue;
                    copy.ExtractToFile(Path.Combine(staging, entry.SaveName + extension));
                    serverCopies = true;
                }
            }

            // Swap moves every listed live file aside and puts back only the staged ones, so the
            // server's copies are replaced by the zip's (or removed) on success and come back on an error.
            // Swap backs them up first, also when the live .sav is missing.
            serverCopies |= noJson;
            return Swap(entry.SaveName, staging, serverCopies ? SaveSet.Extensions.Concat(ServerCopyExtensions).ToList() : SaveSet.Extensions, noJson, serverCopies, noSav);
        }
        finally
        {
            TryDeleteFolder(staging);
        }
    }

    /// <summary>
    /// Puts one of Coop's own copies (<c>&lt;name&gt;.backup1.sav</c> and its .json) back
    /// over the save, the same safe way as <see cref="Restore"/>. The .launcher.json stays.
    /// </summary>
    public void RestoreCopy(string saveName, string copySavPath)
    {
        if (!File.Exists(copySavPath))
            throw new FileNotFoundException("The backup file was not found.", copySavPath);
        var copyJson = Path.ChangeExtension(copySavPath, SaveSet.CompanionExtension);
        if (!File.Exists(copyJson))
            throw new InvalidOperationException(Path.GetFileName(copyJson) + " is missing, so that copy is not complete. It was not put back.");

        var staging = NewStaging(saveName);
        try
        {
            File.Copy(copySavPath, Path.Combine(staging, saveName + SaveSet.SaveExtension));
            File.Copy(copyJson, Path.Combine(staging, saveName + SaveSet.CompanionExtension));
            Swap(saveName, staging, new[] { SaveSet.SaveExtension, SaveSet.CompanionExtension }, noJson: false, serverCopies: false);
        }
        finally
        {
            TryDeleteFolder(staging);
        }
    }

    private string NewStaging(string saveName)
    {
        var staging = Path.Combine(FolderFor(saveName), ".restore-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(staging);
        return staging;
    }

    private BackupEntry? Swap(string saveName, string staging, IReadOnlyList<string> extensions, bool noJson, bool serverCopies, bool noSav = false)
    {
        // A backup made before a restore over a missing .sav holds what was left (the server's
        // copies, a lone .json or .launcher.json), and putting it back returns that state: no .sav.
        var copiesOnly = serverCopies
            && !File.Exists(Path.Combine(staging, saveName + SaveSet.SaveExtension))
            && ServerCopyExtensions.Any(extension => File.Exists(Path.Combine(staging, saveName + extension)));
        var withoutSave = copiesOnly || noSav;
        if (!withoutSave && !File.Exists(Path.Combine(staging, saveName + SaveSet.SaveExtension)))
            throw new InvalidDataException("That backup has no " + saveName + ".sav in it.");
        if (!withoutSave && !noJson && !File.Exists(Path.Combine(staging, saveName + SaveSet.CompanionExtension)))
            throw new InvalidDataException("That backup has no " + saveName + ".json in it.");

        // The save as it is now, for the way back. Not pruned here, so the backup being
        // restored cannot be removed under the restore.
        // A save that lost its .json still gets one, with the server's copies (their .json keeps
        // the players), so every restore can be undone. With no live .sav it holds whatever of the
        // set is left, since the swap below removes a lone .json or .launcher.json as well.
        BackupEntry? before = null;
        var liveJson = File.Exists(LivePath(saveName, SaveSet.CompanionExtension));
        if (File.Exists(LivePath(saveName, SaveSet.SaveExtension)))
            before = Create(saveName, BackupReason.BeforeRestore, prune: false, withServerCopies: serverCopies || !CanBackUp(saveName));
        else if (extensions.Any(extension => File.Exists(LivePath(saveName, extension))))
            before = Create(saveName, BackupReason.BeforeRestore, prune: false, withServerCopies: serverCopies || (!liveJson && LostCompanion(saveName)), withoutSave: true);

        Directory.CreateDirectory(GameSavesDirectory);
        var moved = new List<(string Live, string Aside)>();
        var placed = new List<string>();
        try
        {
            foreach (var extension in extensions)
            {
                var live = LivePath(saveName, extension);
                if (!File.Exists(live))
                    continue;
                var aside = live + ".rollback";
                File.Move(live, aside, overwrite: true);
                moved.Add((live, aside));
            }

            foreach (var extension in extensions)
            {
                var staged = Path.Combine(staging, saveName + extension);
                if (!File.Exists(staged))
                    continue;
                var live = LivePath(saveName, extension);
                File.Move(staged, live, overwrite: true);
                placed.Add(live);
            }
        }
        catch
        {
            foreach (var live in placed)
                TryDelete(live);
            foreach (var (live, aside) in moved)
            {
                try { File.Move(aside, live, overwrite: true); }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            }

            throw;
        }

        foreach (var (_, aside) in moved)
            TryDelete(aside);
        return before;
    }

    /// <summary>Removes backups past the limits and says how many went.</summary>
    public int Prune(string saveName)
    {
        var all = List(saveName);
        if (all.Count == 0)
            return 0;

        var doomed = new List<BackupEntry>();
        if (_policy.MaxAgeDays > 0)
        {
            var cutoff = DateTime.UtcNow.AddDays(-_policy.MaxAgeDays);
            doomed.AddRange(all.Where(entry => entry.CreatedUtc < cutoff));
        }

        if (_policy.MaxCount > 0 && all.Count > _policy.MaxCount)
        {
            // The newest backup from before Coop Save Rescue changed the save stays, so a long
            // night of autosaves cannot push out the save as it was before the fixes.
            var pinned = all.FirstOrDefault(entry => entry.Reason == BackupReason.BeforeRescue);
            doomed.AddRange(all.Where(entry => !ReferenceEquals(entry, pinned)).Skip(pinned == null ? _policy.MaxCount : _policy.MaxCount - 1));
        }

        var removed = 0;
        foreach (var entry in doomed.DistinctBy(entry => entry.FilePath))
        {
            try
            {
                File.Delete(entry.FilePath);
                removed++;
            }
            catch (IOException)
            {
                // Locked; the next backup tries again.
            }
            catch (UnauthorizedAccessException)
            {
            }
        }

        return removed;
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    private static void TryDeleteFolder(string path)
    {
        try
        {
            if (Directory.Exists(path))
                Directory.Delete(path, recursive: true);
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}
