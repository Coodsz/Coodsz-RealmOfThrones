using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using BannerlordCoopLauncher.Core.IO;
using BannerlordCoopLauncher.Core.Modules;
using BannerlordCoopLauncher.Core.Profile;
using BannerlordCoopLauncher.Core.Steam;
using RealmOfThrones.Core;
using TaomCoop.Core;

namespace RealmOfThronesHost;

// The host edits mod setups the way players do in the Realm of Thrones client launcher: New setup, Copy,
// Rename, Add mods, move up and down, remove, on and off, and Save mods. The list on screen is a
// working copy of the picked setup; Start server uses the saved one. Every change goes through
// RotLoadOrder and RotSetupBook in RealmOfThrones.Core, the same rules the client uses, so a locked
// setup (Realm of Thrones) refuses every change and the
// Realm of Thrones core never moves.
public partial class MainWindow
{
    private const string ToolFolderSource = "From Nexus or ModDB";

    private TaomSetup? _hostEditing;
    private TaomSetup? _hostEditingOf;
    // The setup the host saved changes to, so the Share mod list hint stays up after Save mods.
    private string? _hostEditedSetup;

    // The working copy of the picked setup. Picking, making or deleting a setup starts a new one.
    private TaomSetup EditingSetup
    {
        get
        {
            var saved = HostSetup;
            if (_hostEditing == null || !ReferenceEquals(_hostEditingOf, saved))
            {
                _hostEditing = saved.Clone();
                _hostEditingOf = saved;
            }

            return _hostEditing;
        }
    }

    private bool HasUnsavedHostMods => _hostEditing != null && ReferenceEquals(_hostEditingOf, HostSetup) && !_hostEditing.SameMods(HostSetup);

    // True when it is fine to go on: nothing unsaved, or the host saved or dropped the changes.
    private bool ConfirmHostModChanges()
    {
        if (!HasUnsavedHostMods)
            return true;
        var name = EditingSetup.Name;
        var answer = Coodsz.Ui.UiLanguage.Message(
            "You changed the mods on " + name + " and did not click Save mods.\n\nYes saves them. No drops the changes.",
            "Save mods",
            MessageBoxButton.YesNoCancel,
            MessageBoxImage.Question,
            this);
        if (answer == MessageBoxResult.Cancel || answer == MessageBoxResult.None)
            return false;
        if (answer == MessageBoxResult.Yes)
            SaveHostMods();
        else
        {
            _hostEditing = null;
            ShowModSetups();
            NoteLog("The mod changes on " + name + " were dropped.");
        }

        return true;
    }

    private void HostModSetups_Closing(object? sender, CancelEventArgs e)
    {
        if (!ConfirmHostModChanges())
            e.Cancel = true;
    }

    // ----- Setups -----

    private void NewHostSetup_Click(object sender, RoutedEventArgs e)
    {
        if (RefuseSetupChangeWhileBusy() || !ConfirmHostModChanges())
            return;
        var setup = _settings.ModSetups.Create();
        _hostEditing = null;
        SaveSettings();
        ShowModSetups();
        NoteLog(setup.Name + " is a new setup with only the Realm of Thrones core. Click Add mods to put mods on it.");
    }

    private void CopyHostSetup_Click(object sender, RoutedEventArgs e)
    {
        if (RefuseSetupChangeWhileBusy() || !ConfirmHostModChanges())
            return;
        var from = HostSetup.Name;
        var copy = _settings.ModSetups.CopyOf(from);
        _hostEditing = null;
        SaveSettings();
        ShowModSetups();
        NoteLog(copy.Name + " is a copy of " + from + ". Change it without changing " + from + ".");
    }

    private void RenameHostSetup_Click(object sender, RoutedEventArgs e)
    {
        if (RefuseSetupChangeWhileBusy())
            return;
        if (HostSetup.Locked)
        {
            ModStatus(HostSetup.Name + " is locked, so it keeps its name. Click Copy to make a setup you can rename.");
            return;
        }
        HostSetupNameBox.Text = HostSetup.Name;
        RenameHostSetupPanel.Visibility = Visibility.Visible;
        ModStatus("Type the new name, then click Save name.");
        HostSetupNameBox.Focus();
        HostSetupNameBox.SelectAll();
    }

    private void CancelRenameHostSetup_Click(object sender, RoutedEventArgs e) =>
        RenameHostSetupPanel.Visibility = Visibility.Collapsed;

    private void HostSetupNameBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            e.Handled = true;
            SaveHostSetupName();
        }
        else if (e.Key == Key.Escape)
        {
            e.Handled = true;
            RenameHostSetupPanel.Visibility = Visibility.Collapsed;
        }
    }

    private void SaveHostSetupName_Click(object sender, RoutedEventArgs e) => SaveHostSetupName();

    private void SaveHostSetupName()
    {
        if (RefuseSetupChangeWhileBusy())
            return;
        var from = HostSetup.Name;
        try
        {
            _settings.ModSetups.Rename(from, HostSetupNameBox.Text.Trim());
        }
        catch (InvalidOperationException ex)
        {
            ModStatus(ex.Message, failed: true);
            return;
        }

        // The working copy keeps its unsaved changes under the new name.
        if (_hostEditing != null)
            _hostEditing.Name = HostSetup.Name;
        if (from.Equals(_hostEditedSetup, StringComparison.OrdinalIgnoreCase))
            _hostEditedSetup = HostSetup.Name;
        RenameHostSetupPanel.Visibility = Visibility.Collapsed;
        SaveSettings();
        ShowModSetups();
        ModStatus("Renamed " + from + " to " + HostSetup.Name + ".");
    }

    // ----- Mods -----

    // Everything Add mods can offer, the tool folder included, without the Realm of Thrones core.
    private IReadOnlyList<FoundMod> HostModsForAdding()
    {
        try
        {
            var workshops = SteamInstallFinder.ReadWorkshopLibraries(BannerlordFinder.SteamLibraries(), new PhysicalFileSystem());
            return CustomModCatalog.Scan(AppContext.BaseDirectory, workshops, _gameRoot == null ? null : Path.Combine(_gameRoot, "Modules"))
                .Where(mod => RotLoadOrder.RefusalReason(mod.ModuleId) == null)
                .ToList();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or FormatException or InvalidOperationException)
        {
            return Array.Empty<FoundMod>();
        }
    }

    // The list badge of a mod in the Add mods window: Unknown, not tested when the Realm of Thrones list does not know it.
    private BannerlordCoopLauncher.ModBadge? HostPickerBadge(FoundMod mod)
    {
        if (RotModChips.ShowsUnknown(_compat, mod.ModuleId, mod.WorkshopId, mod.Name))
            return BannerlordCoopLauncher.ModPills.Unknown(RotModChips.UnknownTip, null);
        var entry = _compat.Find(mod.ModuleId, mod.WorkshopId, mod.Name);
        var text = entry == null ? null : RotCompatList.BadgeText(entry.Kind);
        return entry == null || text == null
            ? null
            : new BannerlordCoopLauncher.ModBadge(text, CompatColor(entry.Kind), RotCompatList.Hover(entry, BannerlordCoopLauncher.Core.Localization.Translator.T));
    }

    // A known broken mod is only added when the host says so.
    private bool ConfirmBrokenHostMod(string name, string moduleId, string? workshopId)
    {
        var entry = _compat.Find(moduleId, workshopId, name);
        if (entry is not { Kind: CompatTag.Broken })
            return true;
        return Coodsz.Ui.UiLanguage.Message(RotCompatList.BrokenWarning(name, entry) + "\n\nAdd it anyway?", "Known broken mod",
            MessageBoxButton.YesNo, MessageBoxImage.Warning, this) == MessageBoxResult.Yes;
    }

    // A mod in a folder next to the tool is copied into the game's Modules folder, so Start server
    // finds it there like the client launcher does.
    private void CopyIntoHostModules(FoundMod mod)
    {
        if (_gameRoot == null || mod.Source != ToolFolderSource || SharedModList.IsWorkshopId(mod.WorkshopId))
            return;
        var destination = Path.Combine(_gameRoot, "Modules", mod.ModuleId);
        if (Directory.Exists(destination))
            return;
        BannerlordCoopLauncher.Core.Nexus.ModuleZipInstaller.InstallDirectory(mod.Directory, _gameRoot, mod.ModuleId);
        BannerlordCoopLauncher.Core.Support.DownloadMark.Clear(destination);
        NoteLog("Copied " + mod.Name + " into the game's Modules folder.");
    }

    private void AddHostMods_Click(object sender, RoutedEventArgs e)
    {
        // The server runs or starts: the Mods page banner says to stop it first (ModListBlock).
        if (RefuseWhileModListRuns())
            return;
        var editing = EditingSetup;
        if (editing.Locked)
        {
            ModStatus(RotSetupBook.LockedNote(editing.Name), failed: true);
            return;
        }

        var picked = BannerlordCoopLauncher.CustomModWindow.Choose(this, () => HostModsForAdding()
            .Where(mod => !editing.Extras.Any(extra => extra.ModuleId.Equals(mod.ModuleId, StringComparison.OrdinalIgnoreCase)))
            .ToList(), HostPickerBadge);
        if (picked.Count == 0)
            return;

        var added = new List<string>();
        foreach (var mod in picked)
        {
            if (!ConfirmBrokenHostMod(mod.Name, mod.ModuleId, mod.WorkshopId))
                continue;
            var refusal = RotLoadOrder.Add(editing, mod.Name, mod.ModuleId, mod.WorkshopId);
            if (refusal != null)
            {
                NoteLog(refusal);
                continue;
            }

            try
            {
                CopyIntoHostModules(mod);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                NoteLog("Could not copy " + mod.Name + " into the Modules folder. " + ex.Message);
            }

            added.Add(mod.Name);
        }

        if (added.Count == 0)
            return;
        HostMods(rescan: true);
        ShowModSetups();
        NoteLog(string.Format(BannerlordCoopLauncher.Core.Localization.Translator.T("Added {0} to {1}. Click Save mods to keep them."), string.Join(", ", added), editing.Name));
    }

    private static string? RowModuleId(object sender) =>
        sender is FrameworkElement { DataContext: SealedModuleRow { Editable: true, ModuleId: { } id } } ? id : null;

    private void RemoveHostMod_Click(object sender, RoutedEventArgs e)
    {
        // The server runs or starts: the Mods page banner says to stop it first (ModListBlock).
        if (RefuseWhileModListRuns())
            return;
        var id = RowModuleId(sender);
        var editing = EditingSetup;
        var mod = id == null ? null : editing.Extras.FirstOrDefault(item => item.ModuleId.Equals(id, StringComparison.OrdinalIgnoreCase));
        if (mod == null || editing.Locked)
            return;
        RotLoadOrder.Remove(editing, mod.ModuleId);
        ShowModSetups();
        NoteLog(mod.Label + " was taken off " + editing.Name + ". It is still installed. Click Save mods to keep the change.");
    }

    private void ToggleHostMod_Click(object sender, RoutedEventArgs e)
    {
        // The server runs or starts: the Mods page banner says to stop it first (ModListBlock).
        if (RefuseWhileModListRuns())
            return;
        var id = RowModuleId(sender);
        if (id == null || EditingSetup.Locked || sender is not CheckBox box)
            return;
        RotLoadOrder.SetEnabled(EditingSetup, id, box.IsChecked == true);
        ShowModSetups();
    }

    private void SaveHostMods_Click(object sender, RoutedEventArgs e)
    {
        // The server runs or starts: the Mods page banner says to stop it first (ModListBlock).
        if (RefuseWhileModListRuns())
            return;
        SaveHostMods();
    }

    private void SaveHostMods()
    {
        var saved = HostSetup;
        if (saved.Locked)
            return;
        saved.Extras = EditingSetup.Extras.Select(mod => mod.Copy()).ToList();
        RotLoadOrder.Normalize(saved);
        _hostEditing = null;
        _hostEditedSetup = saved.Name;
        SaveSettings();
        ShowModSetups();
        NoteLog(saved.HasEnabledExtras
            ? "Saved " + saved.Name + ". Start server starts Realm of Thrones plus " + string.Join(", ", saved.EnabledExtras.Select(mod => mod.Label)) + "."
            : "Saved " + saved.Name + ". Start server starts Realm of Thrones with only its core.");
        if (ServerIsRunning())
            NoteLog("The server that is running still has the old mods. Restart it to load the saved list.");
    }
}
