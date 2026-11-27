using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Json;

namespace BannerlordCoopLauncher.Core.Updates;

/// <summary>
/// What the update helper does once the tool has closed. A legacy plan copies the whole payload folder
/// (Source set), a successor plan copies one new exe and its sidecars (Successor set). Every path and list
/// is worked out by the tool before it closes, while it still knows its folder.
/// </summary>
public sealed record UpdatePlan(
    int ProcessId,
    string StageDirectory,
    string LogPath,
    string ExePath,
    string? SourceDirectory = null,
    string? InstallDirectory = null,
    IReadOnlyList<string>? SkipTopFiles = null,
    IReadOnlyList<string>? StaleFiles = null,
    IReadOnlyList<string>? SiblingPrefixes = null,
    SuccessorInstall? Successor = null,
    string? ExeSha256 = null);

/// <summary>
/// Installs a downloaded update without a script. The tool cannot replace its own exe while it runs, so
/// InstallAsync starts a copy of this exe with ApplyArgument and the plan file, then closes. That copy
/// waits for the tool to exit, swaps the files, writes update-install.log and starts the tool again.
/// </summary>
public static class UpdateApply
{
    public const string ApplyArgument = "--coodsz-apply-update";
    public const string PlanFileName = "update-plan.json";
    public const string HelperFolder = "helper";
    public const string SkippedLogName = "update-skipped.log";

    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    /// <summary>
    /// Runs first in every tool's startup. True means this process was the update helper, it has done its
    /// work and should exit now. Otherwise it clears update folders an earlier helper could not remove.
    /// </summary>
    public static bool OnStartup(IReadOnlyList<string> arguments) =>
        OnStartup(arguments, Environment.ProcessPath ?? "");

    /// <summary>OnStartup for the helper exe at helperExe (tests pass a path of their own).</summary>
    internal static bool OnStartup(IReadOnlyList<string> arguments, string helperExe)
    {
        if (arguments.Count >= 2 && arguments[0] == ApplyArgument)
        {
            Run(arguments[1], helperExe);
            return true;
        }

        // A day, so another tool's update that is still downloading is never removed.
        _ = Task.Run(() => RemoveOldStages(Path.GetTempPath(), TimeSpan.FromHours(24)));
        return false;
    }

    /// <summary>
    /// Runs one plan. A plan that cannot be read or fails before the install's own steps still starts the
    /// tool again, and the reason goes to update-error.log in the update folder. A plan that is not this
    /// helper's own is refused and starts nothing.
    /// </summary>
    internal static void Run(string planPath, string helperExe)
    {
        UpdatePlan? plan = null;
        var trusted = false;
        try
        {
            plan = JsonSerializer.Deserialize<UpdatePlan>(File.ReadAllText(planPath), JsonOptions)
                ?? throw new InvalidOperationException("The update plan is empty.");
            var refusal = Refusal(plan, planPath, helperExe);
            if (refusal != null)
                throw new InvalidOperationException(refusal);
            trusted = true;
            var closed = WaitForExit(plan.ProcessId);
            // One update of a folder at a time. Two helpers on the same exe would share its .old and remove
            // each other's backup, which could leave no exe at all.
            FolderLock? taken;
            var skipped = "Another update of this folder is still running, so this one changed nothing.";
            try
            {
                taken = FolderLock.Take(LockFolder(plan), LockWait);
            }
            catch (UnauthorizedAccessException)
            {
                // The mutex is there but made by a helper of another Windows user or one run as administrator.
                taken = null;
                skipped = "Another update of this folder runs as another Windows user or as administrator, so this one changed nothing.";
            }

            using var gate = taken;
            if (gate == null)
            {
                // Its own log, since the update that holds the folder rewrites update-install.log when it ends.
                var existing = plan.Successor?.OldExePath ?? plan.ExePath;
                var skippedLog = Path.Combine(Path.GetDirectoryName(plan.LogPath) ?? "", SkippedLogName);
                TryWrite(skippedLog, "Not installed " + Now() + "\r\n" + skipped + "\r\n", append: true);
                try { Start(existing); }
                catch (Exception ex) { TryWrite(skippedLog, ex.Message + "\r\n", append: true); }
                TryDeleteFolder(plan.StageDirectory);
                return;
            }

            if (plan.Successor != null)
                ApplySuccessor(plan, plan.Successor, closed);
            else
                ApplyLegacy(plan, closed);
        }
        catch (Exception ex)
        {
            TryWrite(Path.Combine(ErrorLogFolder(helperExe), "update-error.log"), Now() + " The update did not run. " + ex.Message + "\r\n", append: true);
            if (trusted && plan != null)
                NotInstalled(plan, ex);
        }
    }

    // A trusted plan that failed before its install: the reason goes to update-install.log, the tool starts
    // again, and its own plan and download go. A refused plan's folder is not this helper's to remove, and it
    // keeps update-error.log. So does a folder whose update-error.log is the only copy of the error, because
    // update-install.log could not be written, and a tool that would not start again is noted there too: a
    // folder that got that note is kept as well, since it holds the only copy of it.
    internal static void NotInstalled(UpdatePlan plan, Exception ex) => NotInstalled(plan, ex, TryWrite);

    internal static void NotInstalled(UpdatePlan plan, Exception ex, Func<string, string, bool, bool> write)
    {
        var logged = write(plan.LogPath, "Not installed " + Now() + "\r\n" + ex.Message + "\r\n", false);
        var noteInStage = false;
        var restart = plan.Successor?.OldExePath ?? plan.ExePath;
        try { Start(restart); }
        catch (Exception restartError)
        {
            var line = "The tool could not be started again. " + restartError.Message + "\r\n";
            if (!logged || !write(plan.LogPath, line, true))
                noteInStage = write(Path.Combine(plan.StageDirectory, "update-error.log"), Now() + " " + line, true);
        }
        if (logged && !noteInStage)
            TryDeleteFolder(plan.StageDirectory);
    }

    /// <summary>
    /// Why this helper must not run the plan, or null. The plan sits in its update folder as
    /// update-plan.json, and the helper is the copy in that folder's helper subfolder. A loose build
    /// runs its own exe as the helper, so there the plan must start that same exe again.
    /// </summary>
    internal static string? Refusal(UpdatePlan plan, string planPath, string helperExe)
    {
        static string Full(string path) => Path.GetFullPath(path).TrimEnd('\\', '/');
        if (!Path.GetFileName(planPath).Equals(PlanFileName, StringComparison.OrdinalIgnoreCase))
            return "The plan file is not " + PlanFileName + ".";
        var stage = Full(Path.GetDirectoryName(Path.GetFullPath(planPath)) ?? "");
        if (string.IsNullOrEmpty(plan.StageDirectory) || !Full(plan.StageDirectory).Equals(stage, StringComparison.OrdinalIgnoreCase))
            return "The plan is not in its own update folder.";
        var helperFolder = Full(Path.GetDirectoryName(Path.GetFullPath(helperExe)) ?? "");
        if (helperFolder.Equals(Full(Path.Combine(stage, HelperFolder)), StringComparison.OrdinalIgnoreCase))
            return null;
        var own = plan.Successor?.OldExePath ?? plan.ExePath;
        if (!string.IsNullOrEmpty(own) && Full(own).Equals(Full(helperExe), StringComparison.OrdinalIgnoreCase))
            return null;
        return "The plan does not belong to this helper.";
    }

    // How long a helper waits for another update of the same folder (tests shorten it).
    internal static TimeSpan LockWait = TimeSpan.FromMinutes(1);

    // The install folder the plan changes.
    private static string LockFolder(UpdatePlan plan) =>
        plan.Successor?.InstallDirectory ?? plan.InstallDirectory ?? Path.GetDirectoryName(plan.ExePath) ?? "";

    /// <summary>
    /// A named mutex per install folder, shared by every helper of this Windows session. Take returns null
    /// when another update still holds it after the wait.
    /// </summary>
    internal sealed class FolderLock : IDisposable
    {
        private readonly Mutex _mutex;

        private FolderLock(Mutex mutex) => _mutex = mutex;

        public static string Name(string folder)
        {
            var full = Path.TrimEndingDirectorySeparator(Path.GetFullPath(folder)).ToUpperInvariant();
            var hash = System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(full));
            return @"Local\Coodsz-update-" + Convert.ToHexString(hash)[..16];
        }

        public static FolderLock? Take(string folder, TimeSpan wait)
        {
            // Throws UnauthorizedAccessException when another user's helper made it (Run logs that).
            var mutex = new Mutex(false, Name(folder));
            try
            {
                if (mutex.WaitOne(wait))
                    return new FolderLock(mutex);
            }
            catch (AbandonedMutexException)
            {
                // The helper that held it was killed. This one owns it now.
                return new FolderLock(mutex);
            }

            mutex.Dispose();
            return null;
        }

        public void Dispose()
        {
            _mutex.ReleaseMutex();
            _mutex.Dispose();
        }
    }

    // The update folder for the copied helper, else the loose build's own folder.
    private static string ErrorLogFolder(string helperExe)
    {
        var folder = Path.GetDirectoryName(Path.GetFullPath(helperExe)) ?? "";
        return Path.GetFileName(folder).Equals(HelperFolder, StringComparison.OrdinalIgnoreCase)
            ? Path.GetDirectoryName(folder) ?? folder
            : folder;
    }

    public static Task WritePlanAsync(string path, UpdatePlan plan, CancellationToken cancellationToken = default) =>
        File.WriteAllTextAsync(path, JsonSerializer.Serialize(plan, JsonOptions), new UTF8Encoding(false), cancellationToken);

    public static string Serialize(UpdatePlan plan) => JsonSerializer.Serialize(plan, JsonOptions);

    /// <summary>
    /// Starts the helper. A single file exe is copied into the update folder first, so the install can
    /// replace and remove the exe in the tool's folder. A loose build runs its own exe; a locked file
    /// then moves aside to .old, and the next start removes it.
    /// </summary>
    public static void StartHelper(string runningExe, string stageDirectory, string planPath, bool singleFile)
    {
        var helper = runningExe;
        if (singleFile)
        {
            var folder = Path.Combine(stageDirectory, HelperFolder);
            Directory.CreateDirectory(folder);
            helper = Path.Combine(folder, Path.GetFileName(runningExe));
            File.Copy(runningExe, helper, overwrite: true);
        }

        var start = new ProcessStartInfo(helper)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = stageDirectory
        };
        start.ArgumentList.Add(ApplyArgument);
        start.ArgumentList.Add(planPath);
        using var process = Process.Start(start) ?? throw new InvalidOperationException("The update could not be started.");
    }

    /// <summary>
    /// Update folders (coodsz-update-*) made more than maxAge ago. The helper cannot remove its own exe,
    /// so its folder stays until a later start of any Coodsz tool clears it.
    /// </summary>
    public static void RemoveOldStages(string tempDirectory, TimeSpan maxAge)
    {
        try
        {
            foreach (var folder in Directory.EnumerateDirectories(tempDirectory, "coodsz-update-*"))
            {
                try
                {
                    if (DateTime.UtcNow - Directory.GetCreationTimeUtc(folder) > maxAge)
                        Directory.Delete(folder, recursive: true);
                }
                catch (Exception) { }
            }
        }
        catch (Exception) { }
    }

    private static void ApplyLegacy(UpdatePlan plan, bool closed)
    {
        var source = Path.GetFullPath(plan.SourceDirectory ?? "");
        if (!source.EndsWith('\\'))
            source += '\\';
        var dest = plan.InstallDirectory ?? "";
        var skipTop = new HashSet<string>(plan.SkipTopFiles ?? [], StringComparer.OrdinalIgnoreCase);
        var copiedTo = new List<string>();
        var exe = Path.GetFullPath(plan.ExePath);
        try
        {
            foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
            {
                var rel = file[source.Length..];
                // Saved settings and the player's json stay; build json is replaced.
                if (!ToolUpdate.ShouldReplace(rel))
                    continue;
                // A sibling tool's own files the zip carries only go in when that sibling is in this folder.
                if (!rel.Contains('\\') && skipTop.Contains(rel))
                    continue;
                var to = Path.Combine(dest, rel);
                // The exe goes through a checked .new and one swap, so a kill mid copy never leaves half of it.
                if (Path.GetFullPath(to).Equals(exe, StringComparison.OrdinalIgnoreCase))
                    ReplaceExe(file, to, plan.ExeSha256 ?? throw new IOException("The update plan has no hash for " + to), StageTag(plan));
                else
                    CopyUpdateFile(file, to);
                copiedTo.Add(to);
            }

            foreach (var path in plan.StaleFiles ?? [])
                RemoveWithRetries(path);
            File.WriteAllText(plan.LogPath, "Installed " + Now() + "\r\n" + (closed ? "" : NotClosed + "\r\n"));
        }
        catch (Exception ex)
        {
            TryWrite(plan.LogPath, "Not installed " + Now() + "\r\n" + (closed ? "" : NotClosed + "\r\n") + ex.Message + "\r\n", append: false);
        }

        try { Start(plan.ExePath); }
        catch (Exception ex) { TryWrite(plan.LogPath, ex.Message + "\r\n", append: true); }
        // Only the .old copies this install made or an earlier one left beside a payload file, never a
        // sibling's, which may still be running.
        foreach (var to in copiedTo)
        {
            var name = Path.GetFileName(to);
            if ((plan.SiblingPrefixes ?? []).Any(prefix => name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)))
                continue;
            TryDelete(to + ".old");
        }

        TryDeleteFolder(plan.StageDirectory);
    }

    // The helper's update folder name, which tells its .new copy apart from another helper's.
    private static string StageTag(UpdatePlan plan) =>
        Path.GetFileName(Path.TrimEndingDirectorySeparator(plan.StageDirectory));

    private const string NotClosed = "The old program did not close within 45 seconds, the update went ahead.";

    private static void ApplySuccessor(UpdatePlan plan, SuccessorInstall install, bool closed)
    {
        var from = install.SourceExe;
        var old = install.OldExePath;
        var @new = install.NewExePath;
        var marker = Path.Combine(install.InstallDirectory, SuccessorInstall.RenameMarker);
        var steps = new List<string>();
        steps.Add(closed ? "The old program closed." : NotClosed);
        var newExisted = File.Exists(@new);
        var copied = false;
        try
        {
            if (!File.Exists(from) || !SameHash(from, install.SourceSha256))
                throw new IOException("The downloaded " + from + " does not match the release");
            foreach (var file in install.OtherFiles)
                CopyUpdateFile(file.From, file.To);
            if (install.OtherFiles.Count > 0)
                steps.Add("Copied " + install.OtherFiles.Count + " other files");
            ReplaceExe(from, @new, install.SourceSha256, StageTag(plan));
            steps.Add("Copied " + @new);
            copied = true;
        }
        catch (Exception ex)
        {
            steps.Add("The copy failed. " + ex.Message);
            if (install.Renames && !newExisted && File.Exists(@new))
                TryDelete(@new);
        }

        string start;
        string head;
        if (copied)
        {
            if (install.Renames)
            {
                var marked = false;
                try
                {
                    File.WriteAllText(marker, old + "\r\n");
                    marked = true;
                }
                catch (Exception ex)
                {
                    steps.Add("Could not write " + SuccessorInstall.RenameMarker + ". " + ex.Message);
                }

                if (install.KeepOldExe)
                    steps.Add("Kept " + old + " because a shortcut points at it. The new program moves the shortcut, then removes it.");
                else if (marked)
                    RemoveLeftover(old, steps);
                else
                    steps.Add("Kept " + old + ", the next start removes it.");
            }

            foreach (var path in install.RemoveAfterCopy)
                RemoveLeftover(path, steps);
            RemoveLeftover(@new + ".old", steps);
            start = @new;
            head = "Installed " + Now();
        }
        else
        {
            steps.Add("Kept " + old);
            start = old;
            head = "Not installed " + Now();
        }

        TryWrite(plan.LogPath, string.Join("\r\n", new[] { head }.Concat(steps)) + "\r\n", append: false);
        try
        {
            Start(start);
            TryWrite(plan.LogPath, "Started " + start + "\r\n", append: true);
        }
        catch (Exception ex)
        {
            TryWrite(plan.LogPath, ex.Message + "\r\n", append: true);
        }

        TryDeleteFolder(plan.StageDirectory);
    }

    // Waits up to 45 seconds for the tool to close. False when it was still running after that.
    private static bool WaitForExit(int processId)
    {
        try
        {
            using var process = Process.GetProcessById(processId);
            return process.WaitForExit(45000);
        }
        catch (Exception)
        {
            // Already gone.
            return true;
        }
    }

    /// <summary>
    /// Copies one file, trying again while it is locked. From the third try a file that is still locked
    /// (a running exe can be renamed, not overwritten) moves aside to .old and the new copy takes its place.
    /// </summary>
    internal static void CopyUpdateFile(string from, string to)
    {
        var folder = Path.GetDirectoryName(to);
        if (!string.IsNullOrEmpty(folder))
            Directory.CreateDirectory(folder);
        for (var attempt = 1; attempt <= 12; attempt++)
        {
            try
            {
                ClearReadOnly(to);
                File.Copy(from, to, overwrite: true);
                return;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                if (attempt >= 3 && File.Exists(to))
                {
                    var incoming = to + ".new";
                    var stale = to + ".old";
                    // Set while the file in place is moved aside, so a failed put back keeps the new copy.
                    var keep = false;
                    try
                    {
                        File.Copy(from, incoming, overwrite: true);
                        TryDelete(stale);
                        File.Move(to, stale);
                        keep = true;
                        try
                        {
                            FaultForTest?.Invoke("copy move");
                            File.Move(incoming, to);
                            keep = false;
                        }
                        catch (Exception)
                        {
                            // The file goes back where it was, so a failed copy never leaves it missing.
                            Restore(stale, to, incoming);
                            keep = false;
                            throw;
                        }
                    }
                    finally
                    {
                        if (!keep)
                            TryDelete(incoming);
                    }

                    return;
                }

                Thread.Sleep(400);
            }
        }

        throw new IOException("The update could not replace " + to);
    }

    /// <summary>
    /// Test only: called with "replace" before File.Replace, with "move" after the exe in place moved
    /// to .old in the fallback, and with "copy move" at the same point of CopyUpdateFile's fallback, so a
    /// test can make any of those steps fail.
    /// </summary>
    // Per thread: every hook runs on the thread that runs the plan, so a test's fault never reaches a plan
    // another test class runs in parallel.
    [ThreadStatic]
    internal static Action<string>? FaultForTest;

    /// <summary>
    /// Puts the new exe in place without ever leaving a half written one. The copy goes to .new beside
    /// it and is checked against the release hash there, so a file swapped or cut short after the first
    /// check never gets in. File.Replace then swaps it in as one step and keeps the old exe as .old, so a
    /// kill mid update never leaves the folder without the exe. Only when Replace fails (another volume,
    /// a file system without it) does the exe in place move to .old and .new take its name; a failure
    /// there moves .old back, and when even that fails .new stays and the error names both files.
    /// </summary>
    internal static void ReplaceExe(string from, string to, string sha256, string tag = "")
    {
        // Named after this update (its stage folder), so one helper never takes another one's copy.
        var incoming = to + (tag.Length > 0 ? "." + tag : "") + ".new";
        var backup = to + ".old";
        // Set while the old exe could not go back, so .new stays as the one working copy of the program.
        var keep = false;
        void PutBack()
        {
            keep = true;
            Restore(backup, to, incoming);
            keep = false;
        }

        try
        {
            CopyUpdateFile(from, incoming);
            if (!SameHash(incoming, sha256))
                throw new IOException("The copy of " + to + " is not complete");
            if (File.Exists(to))
            {
                try
                {
                    TryDelete(backup);
                    FaultForTest?.Invoke("replace");
                    File.Replace(incoming, to, backup, ignoreMetadataErrors: true);
                    return;
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or PlatformNotSupportedException)
                {
                    // File.Replace leaves both files where they were when it fails, so the moves below can run.
                    if (!File.Exists(to) && File.Exists(backup))
                        PutBack();
                }
            }

            for (var attempt = 1; ; attempt++)
            {
                var movedAside = false;
                try
                {
                    if (File.Exists(to))
                    {
                        TryDelete(backup);
                        File.Move(to, backup);
                        movedAside = true;
                    }

                    FaultForTest?.Invoke("move");
                    File.Move(incoming, to);
                    return;
                }
                catch (Exception ex)
                {
                    if (movedAside && !File.Exists(to))
                        PutBack();
                    if (attempt >= 12 || ex is not (IOException or UnauthorizedAccessException))
                        throw;
                    Thread.Sleep(400);
                }
            }
        }
        finally
        {
            if (!keep)
                TryDelete(incoming);
        }
    }

    // Moves the old exe back, trying again while it is locked. When it never goes back the error names
    // the .old file and the kept .new copy, so the log tells the player or a support reply which file to
    // rename.
    private static void Restore(string backup, string to, string incoming)
    {
        for (var attempt = 1; attempt <= 12; attempt++)
        {
            try
            {
                File.Move(backup, to);
                return;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                Thread.Sleep(400);
            }
        }

        throw new IOException("The old " + Path.GetFileName(to) + " could not be put back. Rename " + backup + " to " + Path.GetFileName(to) + " to use it again. The new copy is kept at " + incoming + ".");
    }

    private static void RemoveLeftover(string path, List<string> steps)
    {
        if (!File.Exists(path))
            return;
        if (RemoveWithRetries(path))
            steps.Add("Removed " + path);
        else
            steps.Add("Could not remove " + path + ", the next start tries again.");
    }

    private static bool RemoveWithRetries(string path)
    {
        for (var attempt = 1; attempt <= 8; attempt++)
        {
            try
            {
                if (File.Exists(path))
                {
                    ClearReadOnly(path);
                    File.Delete(path);
                }

                return true;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                Thread.Sleep(400);
            }
        }

        return false;
    }

    private static void ClearReadOnly(string path)
    {
        if (File.Exists(path) && File.GetAttributes(path).HasFlag(FileAttributes.ReadOnly))
            File.SetAttributes(path, File.GetAttributes(path) & ~FileAttributes.ReadOnly);
    }

    private static bool SameHash(string path, string sha256) =>
        SuccessorInstall.Hash(path).Equals(sha256, StringComparison.OrdinalIgnoreCase);

    // Every start of the tool after an update goes through here. The tests swap it (SafeToolStart) so no test
    // ever opens a program it made: a fake exe opened this way made Windows show "Unsupported 16-Bit Application".
    internal static Action<string> Starter = StartWithShell;

    private static void Start(string exePath) => Starter(exePath);

    private static void StartWithShell(string exePath)
    {
        using var process = Process.Start(new ProcessStartInfo(exePath)
        {
            UseShellExecute = true,
            WorkingDirectory = Path.GetDirectoryName(exePath) ?? ""
        });
    }

    private static string Now() => DateTime.Now.ToString(CultureInfo.InvariantCulture);

    private static bool TryWrite(string path, string text, bool append)
    {
        try
        {
            if (append)
                File.AppendAllText(path, text);
            else
                File.WriteAllText(path, text);
            return true;
        }
        catch (Exception) { return false; }
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch (Exception) { }
    }

    // Removes what it can. The running helper exe stays behind; see RemoveOldStages.
    internal static void TryDeleteFolder(string path)
    {
        try
        {
            var folderInfo = new DirectoryInfo(path);
            if (!folderInfo.Exists)
                return;
            // A junction or link is removed itself and never followed, so nothing outside the folder is deleted.
            if (folderInfo.Attributes.HasFlag(FileAttributes.ReparsePoint))
            {
                folderInfo.Delete();
                return;
            }

            foreach (var file in Directory.EnumerateFiles(path))
                TryDelete(file);
            foreach (var folder in Directory.EnumerateDirectories(path))
                TryDeleteFolder(folder);
            Directory.Delete(path);
        }
        catch (Exception) { }
    }
}
