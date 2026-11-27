using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using BannerlordCoopLauncher.Core.IO;
using BannerlordCoopLauncher.Core.Modules;
using BannerlordCoopLauncher.Core.Nexus;
using BannerlordCoopLauncher.Core.Profile;
using BannerlordCoopLauncher.Core.Steam;
using BannerlordCoopLauncher.Core.Support;
using Microsoft.Win32;
using RealmOfThrones.Core;
using TaomCoop.Core;

namespace RealmOfThronesLauncher;

// The Mods page edits a working copy in the same order as the Server Tool.
public partial class MainWindow
{
    private const string LauncherFolderSource = "From Nexus or ModDB";

    private TaomSetup _editing = new();
    private bool _applyingSetup;
    private Dictionary<string, FoundMod> _installedExtras = new(StringComparer.OrdinalIgnoreCase);
    private Point _dragStart;
    private string? _dragId;

    private TaomSetup SavedSetup => _settings.ModSetups.Current();

    private bool HasUnsavedMods => !_editing.Name.Equals(SavedSetup.Name, StringComparison.OrdinalIgnoreCase) || !_editing.SameMods(SavedSetup);

    private void InitSetups()
    {
        _settings.ModSetups.Ensure();
        LoadCompatList();
        // A SubModule.xml left changed by the prefabs fix of an earlier launch is put back.
        if (System.Diagnostics.Process.GetProcessesByName("Bannerlord").Length == 0)
            RestorePrefabsFix();
        _editing = SavedSetup.Clone();
        FillSetupList();
        ShowSetupControls();
        Closing += MainWindow_Closing;
    }

    private void FillSetupList()
    {
        _applyingSetup = true;
        SetupBox.Items.Clear();
        foreach (var setup in _settings.ModSetups.Setups)
        {
            var item = new ComboBoxItem { Content = setup.Name, Tag = setup.Name };
            SetupBox.Items.Add(item);
            if (setup.Name.Equals(_settings.ModSetups.Active, StringComparison.OrdinalIgnoreCase))
                SetupBox.SelectedItem = item;
        }

        _applyingSetup = false;
    }

    private void ShowSetupControls()
    {
        var locked = _editing.Locked;
        _applyingSetup = true;
        SetupNameBox.Text = _editing.Name;
        _applyingSetup = false;
        SetupNameBox.IsReadOnly = locked;
        RenameSetupButton.IsEnabled = !locked;
        DeleteSetupButton.IsEnabled = !locked;
        AddModsButton.IsEnabled = !locked;
        AutoSortItem.IsEnabled = !locked;
        SaveModsButton.IsEnabled = !locked;
        SaveModsItem.IsEnabled = !locked;
        InstallModsButton.IsEnabled = !locked;
        // A greyed button on the locked setup says why when the mouse rests on it, and Add mods is not gold then.
        AddModsButton.Style = (Style)FindResource(locked ? "Ghost" : "Primary");
        foreach (var (button, tip) in new[]
                 {
                     (AddModsButton, "Picks mods already on this PC, from the Steam Workshop or the game's Modules folder."),
                     (InstallModsButton, "Downloads the extra Workshop mods of this setup that are not on this PC yet.")
                 })
        {
            ToolTipService.SetShowOnDisabled(button, true);
            button.ToolTip = BannerlordCoopLauncher.Core.Localization.Translator.T(locked ? "This setup is locked. Click Copy on the Mods page to make one you can change." : tip);
        }
        LockedSetupText.Visibility = locked ? Visibility.Visible : Visibility.Collapsed;
        // The small Locked tag by the setup picker in the header; the Mods page line says why.
        LockedSetupTag.Visibility = LockedSetupText.Visibility;
        LockedSetupText.Text = RotSetupBook.LockedNote(_editing.Name);
        HostNeedsModsText.Visibility = _editing.HasEnabledExtras ? Visibility.Visible : Visibility.Collapsed;
        ModuleSaveHint.Visibility = HasUnsavedMods ? Visibility.Visible : Visibility.Collapsed;
        // Save mods shows only while a change waits to be saved; it is also in the More menu.
        SaveModsButton.Visibility = ModuleSaveHint.Visibility;
    }

    private void ChangedMods()
    {
        ModOrderNotice.Visibility = Visibility.Collapsed;
        ShowSetupControls();
        ShowModuleList();
    }

    // ----- The list -----

    private void ShowModuleList()
    {
        ModulePanel.Children.Clear();
        var number = 1;
        foreach (var row in RotLoadOrder.ClientRows(_editing))
        {
            if (row.IsCore)
            {
                ModulePanel.Children.Add(Droppable(CoreRow(number++, row.ModuleId), row.ModuleId));
                continue;
            }

            var extra = _editing.Extras[row.ExtraIndex];
            ModulePanel.Children.Add(ExtraRow(extra, extra.Enabled ? number++ : null));
        }

        if (!_editing.Locked && _editing.Extras.Count == 0)
        {
            ModulePanel.Children.Add(new TextBlock
            {
                Text = "No extra mods yet. Click Add mods to pick mods on this PC, or Import mod list.",
                FontSize = 15,
                Foreground = Coodsz.Ui.FrameBrushes.Get("Keep.Teal", "#7DAAA4"),
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 10, 0, 0)
            });
        }
    }

    // Drops use the same placement rules as the move buttons.
    private Border Droppable(Border row, string id)
    {
        row.Tag = id;
        row.AllowDrop = !_editing.Locked;
        row.Drop += ModuleRow_Drop;
        return row;
    }

    private Border ExtraRow(TaomExtraMod mod, int? number)
    {
        var found = _installedExtras.TryGetValue(mod.ModuleId, out var byId)
            ? byId
            : _installedExtras.Values.FirstOrDefault(item => SharedModList.IsWorkshopId(mod.WorkshopId) && item.WorkshopId == mod.WorkshopId);
        var installed = found != null;
        var locked = _editing.Locked;
        var movable = !locked && !RotLoadOrder.LoadsAfterHarmony(mod.ModuleId);
        var row = new DockPanel { LastChildFill = true };
        // The status column of Coodsz Coop and Friends: the Ready pill, then the list status, count chips and Report link
        // (CompatBadge) in a fixed chip column, so the buttons stay in line with the other rows.
        var badge = CompatBadge(mod.ModuleId, mod.WorkshopId, mod.Label) ?? BadgeColumn();
        DockPanel.SetDock(badge, Dock.Right);
        row.Children.Add(badge);
        var (mark, kind) = !mod.Enabled ? (ModChips.Off, ModChips.Off) : installed ? (ModChips.Ready, ModChips.Ready) : (ModChips.Missing, ModChips.Missing);
        var status = StatusPill(mark, kind);
        DockPanel.SetDock(status, Dock.Right);
        row.Children.Add(status);

        // The button slot keeps its width on a locked setup and on the core rows, so the chips and the status line up (ModRowLayout).
        StackPanel? buttons = null;
        if (!locked)
        {
            buttons = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) };
            // The shared Move up and Move down pair (MoveButtons), which says the mod's name to a screen reader.
            var move = new Coodsz.Ui.MoveButtons { ItemName = mod.Label, VerticalAlignment = VerticalAlignment.Center };
            move.MoveUp += (_, _) => MoveExtra(mod.ModuleId, -1);
            move.MoveDown += (_, _) => MoveExtra(mod.ModuleId, 1);
            if (movable)
                buttons.Children.Add(move);
            buttons.Children.Add(SmallButton("✕", "Remove from this setup. The mod stays installed.", () => RemoveExtra(mod)));
        }

        var actions = Coodsz.Ui.ModRowLayout.Slot(Coodsz.Ui.ModRowLayout.ClientActionColumn, buttons);
        DockPanel.SetDock(actions, Dock.Right);
        row.Children.Add(actions);

        var index = new TextBlock
        {
            Text = number?.ToString() ?? "",
            Width = 28,
            Foreground = Coodsz.Ui.FrameBrushes.Get("Keep.Amber", "#D2A36A"),
            FontSize = 16,
            VerticalAlignment = VerticalAlignment.Center
        };
        DockPanel.SetDock(index, Dock.Left);
        row.Children.Add(index);
        var toggle = new CheckBox
        {
            Style = (Style)FindResource("Keep.Switch"),
            IsChecked = mod.Enabled,
            IsEnabled = !locked,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 4, 0),
            ToolTip = "On loads this mod. Off keeps it on the setup without loading it.",
            Cursor = Cursors.Hand
        };
        toggle.Click += (_, _) =>
        {
            RotLoadOrder.SetEnabled(_editing, mod.ModuleId, toggle.IsChecked == true);
            ChangedMods();
        };
        var switchSlot = Coodsz.Ui.ModRowLayout.Slot(Coodsz.Ui.ModRowLayout.SwitchColumn, toggle);
        DockPanel.SetDock(switchSlot, Dock.Left);
        row.Children.Add(switchSlot);

        var version = found == null ? "" : VersionIn(found.Directory);
        var detail = !installed
            ? SharedModList.IsWorkshopId(mod.WorkshopId) ? "Not on this PC. Install mods downloads it." : "Not on this PC. Get it from Nexus Mods or ModDB."
            : (version.Length > 0 ? version + "  " : "") + (found!.Source == "Workshop" ? "Workshop" : "Modules folder");
        row.Children.Add(new StackPanel
        {
            Margin = new Thickness(8, 0, 8, 0),
            Children =
            {
                new TextBlock { Text = mod.Label, FontSize = 16, FontWeight = FontWeights.SemiBold, Foreground = Coodsz.Ui.FrameBrushes.Get("Keep.TextMain", "#E8E6E1"), TextWrapping = TextWrapping.Wrap },
                new TextBlock { Text = detail, FontSize = 14, Foreground = Coodsz.Ui.FrameBrushes.Get("Keep.Teal", "#7DAAA4"), TextWrapping = TextWrapping.Wrap }
            }
        });

        var border = new Border
        {
            BorderBrush = Coodsz.Ui.FrameBrushes.Get("Keep.BorderDim", "#3A3E44"),
            BorderThickness = new Thickness(0, 0, 0, 1),
            Background = new SolidColorBrush(Color.FromArgb(0x30, 0x31, 0x42, 0x36)),
            Padding = new Thickness(0, 8, 0, 8),
            Child = row,
            Cursor = movable ? Cursors.SizeNS : null
        };
        Coodsz.Ui.ModRowLayout.FixHeight(border);
        if (movable)
        {
            border.PreviewMouseLeftButtonDown += (_, args) =>
            {
                _dragStart = args.GetPosition(ModulePanel);
                _dragId = args.OriginalSource is DependencyObject source && FindParent<ButtonBase>(source) != null ? null : mod.ModuleId;
            };
            border.PreviewMouseMove += (_, args) =>
            {
                if (_dragId != mod.ModuleId || args.LeftButton != MouseButtonState.Pressed)
                    return;
                var now = args.GetPosition(ModulePanel);
                if (Math.Abs(now.Y - _dragStart.Y) < SystemParameters.MinimumVerticalDragDistance)
                    return;
                _dragId = null;
                DragDrop.DoDragDrop(border, new DataObject(typeof(string), mod.ModuleId), DragDropEffects.Move);
            };
        }

        return Droppable(border, mod.ModuleId);
    }

    private Button SmallButton(string glyph, string tip, Action click)
    {
        var button = new Button
        {
            Content = glyph,
            ToolTip = tip,
            Style = (Style)FindResource("Ghost"),
            MinHeight = 30,
            Padding = new Thickness(9, 2, 9, 2),
            Margin = new Thickness(4, 0, 0, 0),
            FontSize = 13
        };
        button.Click += (_, _) => click();
        return button;
    }

    private static T? FindParent<T>(DependencyObject? node) where T : DependencyObject
    {
        while (node != null)
        {
            if (node is T found)
                return found;
            node = node is Visual ? VisualTreeHelper.GetParent(node) : LogicalTreeHelper.GetParent(node);
        }

        return null;
    }

    private void ModuleRow_Drop(object sender, DragEventArgs e)
    {
        e.Handled = true;
        if (_editing.Locked || sender is not Border { Tag: string target } row || e.Data.GetData(typeof(string)) is not string id)
            return;
        MoveExtra(() => RotLoadOrder.MoveNextTo(_editing, id, target, e.GetPosition(row).Y >= row.ActualHeight / 2));
    }

    private void MoveExtra(string moduleId, int delta) =>
        MoveExtra(() => RotLoadOrder.Move(_editing, moduleId, delta));

    private void MoveExtra(Func<bool> move)
    {
        var original = _editing.Clone();
        if (!move())
            return;
        if (RotLoadOrder.MoveProblem(original, _editing, GatherModFacts()) is { } problem)
        {
            _editing.Extras = original.Extras;
            ChangedMods();
            ShowModOrderNotice(problem);
            return;
        }
        ChangedMods();
    }

    private void ShowModOrderNotice(string message)
    {
        ModOrderNotice.Text = string.Join(Environment.NewLine, message.Split(Environment.NewLine)
            .Select(BannerlordCoopLauncher.Core.Localization.Translator.T));
        ModOrderNotice.Visibility = Visibility.Visible;
        Log(message);
    }

    private void RemoveExtra(TaomExtraMod mod)
    {
        RotLoadOrder.Remove(_editing, mod.ModuleId);
        Log(mod.Label + " was taken off " + _editing.Name + ". It is still installed. Click Save mods to keep the change.");
        ChangedMods();
    }

    private static string VersionIn(string? directory)
    {
        if (string.IsNullOrWhiteSpace(directory))
            return "";
        var xml = Path.Combine(directory, "SubModule.xml");
        try
        {
            return File.Exists(xml) ? ModuleIdentityReader.Read(xml).Version : "";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return "";
        }
    }

    // ----- Finding mods on this PC -----

    private static IReadOnlyList<WorkshopLibrary> Workshops()
    {
        try
        {
            return SteamInstallFinder.ReadWorkshopLibraries(BannerlordFinder.SteamLibraries(), new PhysicalFileSystem());
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or FormatException or InvalidOperationException)
        {
            return Array.Empty<WorkshopLibrary>();
        }
    }

    private string? GameModulesFolder() => _gameRoot == null ? null : Path.Combine(_gameRoot, "Modules");

    // Mods the game can load: Steam Workshop items and the game's Modules folder.
    private void ScanInstalledExtras()
    {
        ScanWorkshopModules();
        var found = new Dictionary<string, FoundMod>(StringComparer.OrdinalIgnoreCase);
        var modules = GameModulesFolder();
        try
        {
            foreach (var mod in CustomModCatalog.Scan(modules ?? "", Workshops()))
                found.TryAdd(mod.ModuleId, mod);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }

        _installedExtras = found;
    }

    // Everything Add mods can offer, the launcher folder included, without the Realm of Thrones core.
    private IReadOnlyList<FoundMod> ScanForAdding()
    {
        try
        {
            return CustomModCatalog.Scan(AppContext.BaseDirectory, Workshops(), GameModulesFolder())
                .Where(mod => RotLoadOrder.RefusalReason(mod.ModuleId) == null)
                .ToList();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return Array.Empty<FoundMod>();
        }
    }

    // A mod in a folder next to the launcher is copied into Modules, so the game can load it.
    private void CopyIntoModules(FoundMod mod)
    {
        if (_gameRoot == null || mod.Source != LauncherFolderSource || SharedModList.IsWorkshopId(mod.WorkshopId))
            return;
        var destination = Path.Combine(_gameRoot, "Modules", mod.ModuleId);
        if (Directory.Exists(destination))
            return;
        ModuleZipInstaller.InstallDirectory(mod.Directory, _gameRoot, mod.ModuleId);
        DownloadMark.Clear(destination);
        Log("Copied " + mod.Name + " into the game's Modules folder.");
    }

    // ----- Setups -----

    // True when it is fine to go on: nothing unsaved, or the player saved or dropped the changes.
    private bool ConfirmModChanges()
    {
        if (!HasUnsavedMods)
            return true;
        var answer = Coodsz.Ui.UiLanguage.Message(
            "You changed the mods on " + _editing.Name + " and did not click Save mods.\n\nYes saves them. No drops the changes.",
            "Save mods",
            MessageBoxButton.YesNoCancel,
            MessageBoxImage.Question,
            this);
        if (answer == MessageBoxResult.Cancel || answer == MessageBoxResult.None)
            return false;
        if (answer == MessageBoxResult.Yes)
            SaveMods();
        else
        {
            _editing = SavedSetup.Clone();
            ChangedMods();
            Log("The mod changes on " + _editing.Name + " were dropped.");
        }

        return true;
    }

    private void MainWindow_Closing(object? sender, CancelEventArgs e)
    {
        // The close after the window waited for Bannerlord and took the Coop out, and a Windows log off, which asks nothing
        // and leaves the marker (MainWindow.CoopBuild).
        if (_sessionEnding || (_closingAfterGame && _closeWait == null))
            return;
        // The player closed the waiting window again: the Coop comes out when the game is gone, or stays for the next start.
        if (_closingAfterGame)
        {
            TakeOutOnClose();
            return;
        }
        if (!ConfirmModChanges())
        {
            // The window stays open with the Coop in, and the next close takes it out.
            _actions.Note("Close", "Cancelled at the unsaved mod changes question, the launcher stays open.");
            e.Cancel = true;
            return;
        }

        // Our Coop comes out of Modules and the parked one goes back (MainWindow.CoopBuild).
        if (!TakeOutOnClose())
            e.Cancel = true;
    }

    private void ShowSetup(string note)
    {
        _editing = SavedSetup.Clone();
        FillSetupList();
        ShowSetupControls();
        SaveSettings();
        RefreshModules();
        Log(note);
    }

    private void SetupBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_applyingSetup || !IsLoaded || SetupBox.SelectedItem is not ComboBoxItem { Tag: string name })
            return;
        if (name.Equals(_editing.Name, StringComparison.OrdinalIgnoreCase))
            return;
        if (!ConfirmModChanges())
        {
            FillSetupList();
            return;
        }

        _settings.ModSetups.Switch(name);
        var setup = SavedSetup;
        ShowSetup(setup.HasEnabledExtras
            ? setup.Name + " is picked. Connect starts Realm of Thrones with " + string.Join(", ", setup.EnabledExtras.Select(mod => mod.Label)) + "."
            : setup.Name + " is picked. Connect starts Realm of Thrones with only the Realm of Thrones core.");
    }

    private void NewSetup_Click(object sender, RoutedEventArgs e)
    {
        if (!ConfirmModChanges())
            return;
        var setup = _settings.ModSetups.Create();
        ShowSetup(setup.Name + " is a new setup with only the Realm of Thrones core. Click Add mods to put mods on it.");
    }

    private void CopySetup_Click(object sender, RoutedEventArgs e)
    {
        if (!ConfirmModChanges())
            return;
        var from = SavedSetup.Name;
        var copy = _settings.ModSetups.CopyOf(from);
        ShowSetup(copy.Name + " is a copy of " + from + ". Change it without changing " + from + ".");
    }

    private void RenameSetup_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var from = SavedSetup.Name;
            var to = SetupNameBox.Text.Trim();
            _settings.ModSetups.Rename(from, to);
            _editing.Name = SavedSetup.Name;
            FillSetupList();
            ShowSetupControls();
            SaveSettings();
            Log("Renamed " + from + " to " + SavedSetup.Name + ".");
        }
        catch (InvalidOperationException ex)
        {
            Log(ex.Message);
        }
    }

    private void DeleteSetup_Click(object sender, RoutedEventArgs e)
    {
        var setup = SavedSetup;
        if (setup.Locked)
        {
            Log(setup.Name + " is locked. It cannot be deleted.");
            return;
        }

        if (Coodsz.Ui.UiLanguage.Message("Delete " + setup.Name + "? Its mods stay installed. Only the setup is removed.", "Delete setup", MessageBoxButton.OKCancel, MessageBoxImage.Warning, this) != MessageBoxResult.OK)
            return;
        _settings.ModSetups.Delete(setup.Name);
        ShowSetup(setup.Name + " was deleted. " + SavedSetup.Name + " is picked.");
    }

    // ----- Mods -----

    private void AddMods_Click(object sender, RoutedEventArgs e)
    {
        if (_editing.Locked)
        {
            Log(_editing.Name + " is locked. Click New setup or Copy to make a setup you can add mods to.");
            return;
        }

        var picked = BannerlordCoopLauncher.CustomModWindow.Choose(this, () => ScanForAdding()
            .Where(mod => !_editing.Extras.Any(extra => extra.ModuleId.Equals(mod.ModuleId, StringComparison.OrdinalIgnoreCase)))
            .ToList(), PickerBadge);
        if (picked.Count == 0)
            return;

        var added = new List<string>();
        foreach (var mod in picked)
        {
            if (!ConfirmBrokenMod(mod.Name, mod.ModuleId, mod.WorkshopId))
                continue;
            var refusal = RotLoadOrder.Add(_editing, mod.Name, mod.ModuleId, mod.WorkshopId);
            if (refusal != null)
            {
                Log(refusal);
                continue;
            }

            try
            {
                CopyIntoModules(mod);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                Log("Could not copy " + mod.Name + " into the Modules folder. " + ex.Message);
            }

            added.Add(mod.Name);
        }

        if (added.Count == 0)
            return;
        ScanInstalledExtras();
        ChangedMods();
        Log(string.Format(BannerlordCoopLauncher.Core.Localization.Translator.T("Added {0} to {1}. Click Save mods to keep them."), string.Join(", ", added), _editing.Name));
    }

    private void SaveMods_Click(object sender, RoutedEventArgs e) => SaveMods();

    private void SaveMods()
    {
        var saved = SavedSetup;
        if (saved.Locked)
            return;
        saved.Extras = _editing.Extras.Select(mod => mod.Copy()).ToList();
        RotLoadOrder.Normalize(saved);
        _editing = saved.Clone();
        SaveSettings();
        RefreshModules();
        ShowSetupControls();
        Log(saved.HasEnabledExtras
            ? "Saved " + saved.Name + ". Connect starts Realm of Thrones with " + string.Join(", ", saved.EnabledExtras.Select(mod => mod.Label)) + "."
            : "Saved " + saved.Name + ". Connect starts Realm of Thrones with only the Realm of Thrones core.");
    }

    private async void InstallMods_Click(object sender, RoutedEventArgs e)
    {
        if (_busy)
        {
            Log("Wait for the current action to finish.");
            return;
        }

        ScanInstalledExtras();
        var missing = RotSharedList.Missing(_editing, _installedExtras.Keys);
        if (missing.Count == 0)
        {
            UnblockModDlls(_editing);
            ChangedMods();
            Log(_editing.HasEnabledExtras
                ? "Every extra mod on " + _editing.Name + " is already on this PC."
                : _editing.Name + " has no extra mods to install.");
            return;
        }

        await DownloadExtrasAsync(missing);
        UnblockModDlls(_editing);
    }

    // Workshop mods are subscribed and downloaded through Steam; the others need a manual download.
    private async Task<IReadOnlyList<string>> DownloadExtrasAsync(IReadOnlyList<TaomExtraMod> missing)
    {
        // Mods the launcher carries (a BundledMods payload) are copied into Modules, like Coop and Friends does.
        var bundled = new List<string>();
        var carried = await ToolPayload.FolderAsync(ToolPayload.BundledMods);
        foreach (var mod in missing.Where(mod => !SharedModList.IsWorkshopId(mod.WorkshopId)))
        {
            var source = Path.Combine(carried, mod.ModuleId);
            if (_gameRoot == null || !File.Exists(Path.Combine(source, "SubModule.xml")))
                continue;
            try
            {
                ModuleZipInstaller.InstallDirectory(source, _gameRoot, mod.ModuleId);
                DownloadMark.Clear(Path.Combine(_gameRoot, "Modules", mod.ModuleId));
                bundled.Add(mod.ModuleId);
                Log("Copied " + mod.Label + " into the game's Modules folder.");
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                Log("Could not copy " + mod.Label + " into the Modules folder. " + ex.Message);
            }
        }

        if (bundled.Count > 0)
            ScanInstalledExtras();
        var manual = missing.Where(mod => !SharedModList.IsWorkshopId(mod.WorkshopId) && !bundled.Contains(mod.ModuleId)).Select(mod => mod.Label).ToList();
        if (manual.Count > 0)
            Log("These mods are not on the Steam Workshop. Get " + string.Join(", ", manual) + " from Nexus Mods or ModDB and put the folders in the game's Modules folder.");

        var names = new Dictionary<ulong, string>();
        foreach (var mod in missing)
        {
            if (SharedModList.IsWorkshopId(mod.WorkshopId) && ulong.TryParse(mod.WorkshopId, out var id))
                names.TryAdd(id, mod.Label);
        }

        if (names.Count == 0)
            return Array.Empty<string>();
        // With Steam closed nothing is asked for, so nothing is reported as requested or still downloading.
        if (!RotSteamPage.SteamRunning())
        {
            Log("Steam is needed for " + string.Join(", ", names.Values) + ". " + RotSteamPage.SteamNotRunning);
            return Array.Empty<string>();
        }

        _busy = true;
        try
        {
            await SubscribeWorkshopAsync(names);
        }
        finally
        {
            _busy = false;
        }

        RefreshModules();
        var still = RotSharedList.Missing(_editing, _installedExtras.Keys).Where(mod => SharedModList.IsWorkshopId(mod.WorkshopId)).Select(mod => mod.Label).ToList();
        Log(still.Count == 0
            ? "Install mods finished. Those mods are installed."
            : "Steam is still finishing " + string.Join(", ", still) + ". Click Install mods again in a minute.");
        return names.Values.ToList();
    }

    // ----- Share and import -----

    private (string Name, string Version, string? Directory)? Describe(string moduleId)
    {
        if (_installedExtras.TryGetValue(moduleId, out var found))
            return (found.Name, VersionIn(found.Directory), found.Directory);
        // Coop is the copy this launcher carries, as Connect loads it (CoreModuleXml), so a list shared or imported before
        // the first Connect has its build.
        var xml = CoreModuleXml(moduleId);
        if (xml == null)
            return null;
        return (moduleId, VersionIn(Path.GetDirectoryName(xml)), Path.GetDirectoryName(xml));
    }

    private void ShareList_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new SaveFileDialog { Filter = "Mod list|*.json", FileName = "coodsz-mod-list.json" };
        if (dialog.ShowDialog(this) != true)
            return;
        try
        {
            SharedModList.Write(dialog.FileName, RotSharedList.Entries(_editing, Describe));
            Log("Shared mod list saved for " + _editing.Name + ". The host or a friend clicks Import mod list and picks that file.");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            Log(ex.Message);
        }
    }

    // Import a friend's or the host's list: a locked setup stays as it is, so a new setup is made for
    // a list with extra mods; the list's extra mods are added, missing Workshop ones are downloaded, and the
    // order and on and off switches are matched. The list is kept, so Connect compares this PC with it.
    // A window at the end says what happened.
    private async void ImportList_Click(object sender, RoutedEventArgs e)
    {
        if (_busy)
        {
            Log("Wait for the current action to finish.");
            return;
        }

        var dialog = new OpenFileDialog { Filter = "Mod list|*.json" };
        if (dialog.ShowDialog(this) != true)
            return;

        IReadOnlyList<SharedModEntry> shared;
        try
        {
            shared = SharedModList.Read(File.ReadAllText(dialog.FileName));
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.Text.Json.JsonException or IOException)
        {
            Log("That file is not a Coodsz mod list.");
            return;
        }

        // A list of another overhaul (TAOM, Burning Empires, Chaos in Calradia) is refused; nothing changes.
        var foreign = RotSharedList.ForeignGame(shared);
        if (foreign != null)
        {
            Log(foreign);
            Coodsz.Ui.UiLanguage.Message(foreign, "Import mod list", MessageBoxButton.OK, MessageBoxImage.Warning, this);
            return;
        }

        // A list with nothing to add to the locked setup (a host on Plain Realm of Thrones) keeps it: the list goes into
        // a copy that is thrown away, only for the summary.
        var onThisPc = ScanForAdding();
        var keepLocked = _editing.Locked && !RotSharedList.Import(_editing.Copy(_editing.Name), shared, onThisPc).Changed;
        var summary = new List<string>();
        if (_editing.Locked && !keepLocked)
        {
            if (!ConfirmModChanges())
                return;
            var from = _editing.Name;
            var made = _settings.ModSetups.Create();
            _editing = made.Clone();
            FillSetupList();
            SaveSettings();
            summary.Add("Made " + made.Name + " for this list, because " + from + " is locked.");
        }

        // Kept only once the import goes ahead, so a cancelled one leaves the stored list and the drift note as they were.
        RememberHostList(shared);
        // The imported list is what Connect compares from now on, so the mods servers named before are dropped. A mod
        // list file does not name its host, so every host's entry goes, not only the one this list came from.
        _settings.ServerNeeds.Clear();
        _hostListImported = true;
        ShowVersionDriftNote(HostProblems().Count);
        UpdateConnectButton();

        var result = RotSharedList.Import(keepLocked ? _editing.Copy(_editing.Name) : _editing, shared, onThisPc);
        foreach (var mod in _editing.Extras)
        {
            var found = onThisPc.FirstOrDefault(item => item.ModuleId.Equals(mod.ModuleId, StringComparison.OrdinalIgnoreCase));
            if (found != null && result.Added.Contains(mod.Label))
            {
                try
                {
                    CopyIntoModules(found);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    Log("Could not copy " + found.Name + " into the Modules folder. " + ex.Message);
                }
            }
        }

        ScanInstalledExtras();
        ChangedMods();
        if (result.Added.Count > 0)
            summary.Add("Added " + string.Join(", ", result.Added) + " to " + _editing.Name + ".");
        if (result.TurnedOn.Count > 0)
            summary.Add("Turned on " + string.Join(", ", result.TurnedOn) + ".");
        if (result.TurnedOff.Count > 0)
            summary.Add("Turned off " + string.Join(", ", result.TurnedOff) + ", because the list does not have them.");
        if (result.Refused.Count > 0)
            summary.Add("Left out " + string.Join(", ", result.Refused) + ", because the Realm of Thrones core already has them or they do not work with Realm of Thrones.");
        if (result.NoLink.Count > 0)
            summary.Add("Not on this PC and the list has no Workshop link, so get these yourself: " + string.Join(", ", result.NoLink) + ".");
        if (result.MissingCore.Count > 0)
            summary.Add("The list leaves out " + string.Join(", ", result.MissingCore) + " from the Realm of Thrones core, so it may come from a setup without Realm of Thrones.");

        // Each mod on the list at another version here, by the rule Connect uses (RotJoinCheck.HostProblems).
        foreach (var problem in RotSharedList.VersionProblems(shared, InstalledVersions(shared))
                     .Where(problem => problem.Kind == RotVersionProblemKind.Different && !CoopJoinRules.IsIgnored(problem.ModuleId)))
            summary.Add(problem.Text);

        var missing = RotSharedList.Missing(_editing, _installedExtras.Keys).Where(mod => SharedModList.IsWorkshopId(mod.WorkshopId)).ToList();
        if (missing.Count > 0)
        {
            var got = await DownloadExtrasAsync(missing);
            if (got.Count > 0)
                summary.Add("Asked Steam to download " + string.Join(", ", got) + ".");
        }

        foreach (var line in summary)
            Log(line);
        if (HasUnsavedMods)
        {
            summary.Add("Click Save mods to keep it.");
            Log("Click Save mods to keep the imported list. It is not kept until you do.");
        }
        else if (summary.Count == 0)
            summary.Add("This setup already matches that list.");

        Coodsz.Ui.UiLanguage.Message(string.Join("\n\n", summary), "Import mod list", MessageBoxButton.OK, MessageBoxImage.Information, this);
    }

    // Subscribes and downloads through Steam itself, then waits for the downloads.
    // Falls back to the Workshop pages when Steam cannot be used. With Steam closed it only says so:
    // a steam: link would start Steam on pages the player did not ask for, or fail on a PC with no
    // app for steam: links.
    private async Task<WorkshopSubscribeResult?> SubscribeWorkshopAsync(IReadOnlyDictionary<ulong, string> names)
    {
        var gameRoot = _gameRoot;
        if (gameRoot == null || names.Count == 0)
            return null;

        if (!RotSteamPage.SteamRunning())
        {
            Log("Steam is needed for " + string.Join(", ", names.Values) + ". " + RotSteamPage.SteamNotRunning);
            return null;
        }

        Log("Asking Steam to subscribe and download " + string.Join(", ", names.Values) + ".");
        var download = await WorkshopDownload.RunAsync(gameRoot, names, line => Dispatcher.Invoke(() => Log(line)), CancellationToken.None);
        if (download.SteamProblem != null)
            Log("Could not subscribe through Steam by itself. " + download.SteamProblem);
        var open = download.OpenByHand(names.Keys.ToList());
        if (open.Count > 0)
        {
            Log("Opening the Workshop pages. Subscribe there and wait for the download in Steam.");
            await RotSteamPage.OpenAsync(open.Select(id => id.ToString(System.Globalization.CultureInfo.InvariantCulture)), Log);
        }

        var result = download.Result;
        return result;
    }
}
