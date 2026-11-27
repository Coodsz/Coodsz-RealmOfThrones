using System.IO;
using BannerlordCoopLauncher.Core.Saves;
using BannerlordCoopLauncher.Core.Server;
using RealmOfThrones.Core;
using TaomCoop.Core;

namespace RealmOfThronesHost;

// Automatic save backups, as in Coodsz Host: after every "world saved to 'name.sav'"
// line from the server this window started, the save gets a zip backup in
// save-backups\automatic\<save>. 10 are kept per save, none older than 30 days.
// Restore save backup on the Saves page takes these zips as well as the copies kept
// before a save runs with other mods.
public partial class MainWindow
{
    private readonly AutoBackupSchedule _autoBackups = new();

    private string AutoBackupFolder => Path.Combine(SaveBackupFolder, "automatic");

    private BackupService AutoBackups() => new(ServerSavesDirectory(), AutoBackupFolder);

    // Runs on the thread that reads the server output.
    private void NoteSavedWorld(string line)
    {
        var name = KnownLogLines.SavedWorldName(line);
        if (name == null || string.IsNullOrWhiteSpace(_settings.DataRoot))
            return;
        // A save name the console bent is looked up on disk (only when it is not there as it came).
        var found = SaveSet.ResolveLoggedName(ServerSavesDirectory(), name, DateTime.UtcNow, out var refusal);
        if (found == null)
        {
            _actions.Note("Backup", refusal!);
            NoteLog(refusal!);
            return;
        }

        name = found;
        if (!SaveSet.IsValidSaveName(name))
            return;
        // The new campaign's first save: from now on it is a save like any other, picked in the box,
        // so the save card stops saying Start server makes it and the next start hosts it.
        if (name.Equals(_settings.NewCampaignName.Trim(), StringComparison.OrdinalIgnoreCase))
            Dispatcher.BeginInvoke(() => { PendingNewCampaign(); });
        // Restart saves again a few seconds after Save game: a waiting backup moves after the newest save.
        var reason = line.Contains("shutdown", StringComparison.OrdinalIgnoreCase) ? BackupReason.Shutdown : BackupReason.Autosave;
        if (!_autoBackups.Saved(name, reason, DateTime.UtcNow))
            return;
        // The server just wrote this save. After a failed load or with a broken world the older backups stay as they are.
        var saveRefusal = SaveRefusal();
        if (saveRefusal != null)
        {
            _autoBackups.Cancel(name);
            NoteLog(saveRefusal == BrokenWorldGuard.RefusalText
                ? "Did not back up " + name + ". Your hero has no party to rejoin in this world, so the older backups are kept as they are."
                : BrokenWorldGuard.FailedLoadBackupText);
            return;
        }

        var backups = AutoBackups();
        _ = Task.Run(async () =>
        {
            for (var attempt = 1; ; attempt++)
            {
                // Coop writes the .json right after the .sav.
                TimeSpan wait;
                while ((wait = _autoBackups.Wait(name, DateTime.UtcNow)) > TimeSpan.Zero)
                    await Task.Delay(wait);
                var started = DateTime.UtcNow;
                try
                {
                    var entry = backups.Create(name, _autoBackups.Reason(name));
                    NoteLog("Backed up " + name + ". Restore save backup puts it back.");
                    if (_autoBackups.Done(name, started))
                        return;
                    attempt = 0;
                }
                catch (IOException) when (attempt < AutoBackupSchedule.Attempts)
                {
                    await Task.Delay(AutoBackupSchedule.RetryDelay);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or ArgumentException)
                {
                    if (!_autoBackups.Done(name, started))
                    {
                        attempt = 0;
                        continue;
                    }

                    _actions.Note("Backup", "Failed for " + name + ": " + ex.Message);
                    NoteLog("Could not back up " + name + ". " + ex.Message);
                    return;
                }
            }
        });
    }

    // A zip from save-backups\automatic\<save>, or null when the file is not one.
    private BackupEntry? AutoBackupEntry(string path)
    {
        if (!path.EndsWith(".zip", StringComparison.OrdinalIgnoreCase) || !SaveSlot.IsInsideDirectory(path, AutoBackupFolder))
            return null;
        var name = Path.GetFileName(Path.GetDirectoryName(path));
        if (!SaveSet.IsValidSaveName(name))
            return null;
        var info = new FileInfo(path);
        return new BackupEntry(info.FullName, name!, info.LastWriteTimeUtc, info.Length);
    }
}
