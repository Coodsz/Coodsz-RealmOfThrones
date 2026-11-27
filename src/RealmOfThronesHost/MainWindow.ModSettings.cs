using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using BannerlordCoopLauncher.Core.Localization;
using BannerlordCoopLauncher.Core.Modules;
using BannerlordCoopLauncher.Core.Server;
using Microsoft.Win32;
using RealmOfThrones.Core;
using TaomCoop.Core;

namespace RealmOfThronesHost;

// Mod settings for players on the Settings page, in the closed Advanced: mod settings for players fold. The server's own
// copy of the host's MCM files (ServerModSettings) is what the sync sends, so a Save here reaches every player within a few
// seconds, with no restart. Opening the page copies in this PC's files the server is missing. Looks mods are tagged
// "Each player's own": players keep their own values for those (recipes.json, written at Prepare and Start).
public partial class MainWindow
{
    private List<ServerModFile> _modFiles = new();

    // The value editors of the shown mod, by value name.
    private readonly Dictionary<string, FrameworkElement> _modEditors = new(StringComparer.Ordinal);

    private string ModSettingsRoot => ServerModSettings.ServerRoot(_settings.DataRoot);

    // Called on every Settings page open; keeps the shown mod when it is still there.
    private void ShowModSettings(string? keep = null)
    {
        keep ??= (ModSettingsMods.SelectedItem as ListBoxItem)?.Tag as string;
        var failed = new List<string>();
        try
        {
            var twins = new List<string>();
            ServerModSettings.Seed(ModSettingsRoot, ServerModSettings.DocumentsRoot, failed, twins);
            foreach (var line in twins)
                NoteLog("Mod settings for players: " + line);
            if (failed.Count > 0)
                NoteLog("Mod settings for players: " + failed.Count + " files on this PC could not be copied now. They are tried again next time.");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            ModSettingsStatus.Text = ex.Message;
        }

        try
        {
            _modFiles = ServerModSettings.Load(ModSettingsRoot, CommunityCompat.Current);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            ModSettingsStatus.Text = ex.Message;
            _modFiles = new List<ServerModFile>();
        }

        // The looks list follows every seed, save, reset and import, not only Prepare and Start.
        try
        {
            if (!string.IsNullOrWhiteSpace(_settings.ServerRoot))
                ServerModSettings.WritePlayersOwn(ServerToolsModule.ModuleFolder(_settings.ServerRoot), _modFiles, line => NoteLog("Mod settings for players: " + line));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            NoteLog("Mod settings for players: " + ex.Message);
        }

        var any = _modFiles.Count > 0;
        // The sync sends the first 256 ids only; past that, some mods never reach players.
        var ids = _modFiles.Select(file => file.Id).Distinct(StringComparer.Ordinal).Count();
        // Its own line, so an import's result next to it stays.
        ModSettingsWarning.Text = ids > ServerModSettings.MaxIds ? "The server has " + ids + " mod settings files, more than the 256 players can get, so some mods do not reach players." : "";
        ModSettingsWarning.Visibility = ids > ServerModSettings.MaxIds ? Visibility.Visible : Visibility.Collapsed;
        ModSettingsEmpty.Visibility = any ? Visibility.Collapsed : Visibility.Visible;
        ModSettingsEditor.Visibility = any ? Visibility.Visible : Visibility.Collapsed;
        ModSettingsButtons.Visibility = any ? Visibility.Visible : Visibility.Collapsed;
        ModSettingsMods.Items.Clear();
        foreach (var file in _modFiles)
        {
            var row = new StackPanel();
            row.Children.Add(new TextBlock { Text = file.Label, Foreground = Ivory, FontSize = 15, TextWrapping = TextWrapping.Wrap });
            if (file.Looks)
                row.Children.Add(new TextBlock { Text = Translator.T("Each player's own"), Foreground = SettingHint, FontSize = 12 });
            ModSettingsMods.Items.Add(new ListBoxItem
            {
                Content = row,
                Tag = file.Path,
                ToolTip = Translator.T(file.Looks
                    ? "A looks mod (interface, graphics or sound). Each player keeps their own values for it."
                    : "Players use the values you set here.")
            });
        }

        var index = _modFiles.FindIndex(file => file.Path.Equals(keep, StringComparison.OrdinalIgnoreCase));
        ModSettingsMods.SelectedIndex = any ? Math.Max(0, index) : -1;
        ShowModValues();
    }

    private void ModSettingsMods_SelectionChanged(object sender, SelectionChangedEventArgs e) => ShowModValues();

    private ServerModFile? SelectedModFile() =>
        ModSettingsMods.SelectedIndex >= 0 && ModSettingsMods.SelectedIndex < _modFiles.Count ? _modFiles[ModSettingsMods.SelectedIndex] : null;

    // The editor follows the JSON value: on or off is a checkbox, a number a number box (a dropdown is its place in the
    // list), text a text box. Only the type is checked here; each player's MCM holds the value to the mod's limits.
    private void ShowModValues()
    {
        ModSettingsValues.Children.Clear();
        _modEditors.Clear();
        var file = SelectedModFile();
        if (file == null)
            return;
        // The sync sends a mod's first 512 values by its own rules (ServerModSettings.SyncedNames), so the rows stop at the last of those.
        var shown = 0;
        foreach (var value in file.Values)
        {
            if (file.More && shown == file.Synced.Count)
                break;
            var synced = file.Synced.Contains(value.Name);
            if (synced)
                shown++;

            FrameworkElement editor;
            string hint;
            switch (value.Kind)
            {
                case JsonValueKind.True or JsonValueKind.False:
                    editor = new CheckBox { IsChecked = value.Kind == JsonValueKind.True, FontSize = 16 };
                    hint = "On or off.";
                    break;
                case JsonValueKind.Number:
                    var box = NumberBox();
                    box.Text = value.Text;
                    editor = box;
                    hint = value.Whole ? "A whole number. For a list choice it is the place in the list, from 0." : "A number.";
                    break;
                case JsonValueKind.String:
                    var text = NumberBox();
                    text.Width = 240;
                    text.Text = value.Text;
                    editor = text;
                    hint = "Text.";
                    break;
                default:
                    editor = new TextBlock { Text = Translator.T("Change it in MCM in your own game."), Foreground = SettingHint, FontSize = 14 };
                    hint = "A list of values.";
                    break;
            }

            // A hotkey, interface or private value the sync leaves out stays each player's own.
            if (!synced && editor is CheckBox or TextBox)
                hint = "The server never sends this one, so each player keeps their own.";
            if (editor is CheckBox or TextBox)
                _modEditors[value.Name] = editor;
            ModSettingsValues.Children.Add(SettingRow(value.Name, Translator.T(hint), editor));
        }

        if (file.More)
            ModSettingsValues.Children.Add(new TextBlock { Text = Translator.T("This mod has more than 512 values. Players get the first 512, and only those are shown."), Foreground = SettingHint, FontSize = 14, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 0) });
    }

    private void SaveModSettings_Click(object sender, RoutedEventArgs e)
    {
        var file = SelectedModFile();
        if (file == null)
            return;
        var changes = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var (name, editor) in _modEditors)
            changes[name] = editor is CheckBox check ? (check.IsChecked == true ? "true" : "false") : ((TextBox)editor).Text;
        try
        {
            var problem = ServerModSettings.Save(ModSettingsRoot, file.Path, changes);
            ModSettingsStatus.Text = problem ?? "Saved. Players get it within a few seconds.";
            if (problem == null)
            {
                _actions.Note("Mod settings", "Saved " + file.Label);
                ShowModSettings(file.Path);
            }
        }
        // ArgumentException: a hand edited or imported file that names one value twice.
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or InvalidDataException or ArgumentException)
        {
            ModSettingsStatus.Text = ex.Message;
        }
    }

    // A deleted file sends players nothing, so while the server runs a mod with no file on this PC is never removed:
    // the players on the server and the ones who join later would end up on different values.
    private bool ModSettingsServerUp => _startingServer || OwnsServerProcess() || ServerIsRunning();

    private static string StopFirstLine(string mod) =>
        "Stop the server first to remove " + mod + "'s settings, so every player gets the same values.";

    private void ResetModSettings_Click(object sender, RoutedEventArgs e)
    {
        var file = SelectedModFile();
        if (file == null)
            return;
        // A mod with no file on this PC (an imported one) is removed, so that waits for a stopped server and asks first.
        bool hasCopy;
        try
        {
            hasCopy = ServerModSettings.HasPcCopy(ModSettingsRoot, ServerModSettings.DocumentsRoot, file.Path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            ModSettingsStatus.Text = ex.Message;
            return;
        }

        if (!hasCopy)
        {
            if (ModSettingsServerUp)
            {
                ModSettingsStatus.Text = StopFirstLine(file.Label);
                return;
            }
            if (!ConfirmWindow.Ask(this, "Mod settings", "This PC has no MCM file for " + file.Label + ", so it is removed from the server. After that every player uses their own values for it. Remove it?", okText: "Remove it"))
                return;
        }
        ResetModSettings(file.Path, file.Path);
    }

    private void ResetAllModSettings_Click(object sender, RoutedEventArgs e)
    {
        if (ConfirmWindow.Ask(this, "Mod settings", "Put every mod back to the values MCM saved in your own game on this PC? Mods you imported that this PC has no file for are removed.", okText: "Put them back"))
            ResetModSettings(null, (ModSettingsMods.SelectedItem as ListBoxItem)?.Tag as string);
    }

    private void ResetModSettings(string? path, string? keep)
    {
        try
        {
            var before = _modFiles.Count;
            var existed = path != null && File.Exists(path);
            var kept = new List<string>();
            var restored = ServerModSettings.Reset(ModSettingsRoot, ServerModSettings.DocumentsRoot, path, ModSettingsServerUp, kept);
            _actions.Note("Mod settings", path == null ? "Every mod back to this PC's values" : "Back to this PC's values: " + Path.GetFileName(path));
            ShowModSettings(keep);
            var removed = path != null ? (existed && restored == 0 && kept.Count == 0 ? 1 : 0) : Math.Max(0, before - _modFiles.Count);
            var lines = new List<string>
            {
                removed == 0 ? "Back to this PC's values. Players get them within a few seconds."
                    : removed == 1 ? "Back to this PC's values. 1 mod had no file on this PC and was removed."
                    : "Back to this PC's values. " + removed + " mods had no file on this PC and were removed."
            };
            lines.AddRange(kept.Select(StopFirstLine));
            ModSettingsStatus.Text = string.Join("\n", lines);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            ModSettingsStatus.Text = ex.Message;
        }
    }


    private void ImportModSettingsZip_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog { Title = Translator.T("Pick the .zip a player sent you"), Filter = "Zip (*.zip)|*.zip" };
        if (dialog.ShowDialog(this) == true)
            ImportModSettings(dialog.FileName);
    }

    private void ImportModSettingsFolder_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog { Title = Translator.T("Pick the folder a player sent you"), Multiselect = false };
        if (dialog.ShowDialog(this) == true)
            ImportModSettings(dialog.FolderName);
    }

    // The App's net for an exception no handler caught (App.xaml.cs).
    // On the status line, as failed, so a host sees that a Start or another action did not finish; SetControl logs it too.
    internal void NoteUnexpected(Exception ex)
    {
        SetControl("Something went wrong, and the tool kept running: " + ex.Message, failed: true);
    }

    // The mods this server has: the setup's mods and the module folders in the server.
    private List<string> ServerModNames()
    {
        var names = RotLoadOrder.ServerOrder(HostSetup).Concat(HostSetup.Extras.SelectMany(mod => new[] { mod.ModuleId, mod.Name })).ToList();
        var modules = string.IsNullOrWhiteSpace(_settings.ServerRoot) ? "" : ServerModuleInstall.ModulesRoot(_settings.ServerRoot);
        if (modules.Length > 0 && Directory.Exists(modules))
            names.AddRange(Directory.GetDirectories(modules).Select(folder => Path.GetFileName(folder)));
        return names;
    }

    private void ImportModSettings(string source)
    {
        try
        {
            var result = ServerModSettings.Import(source, ModSettingsRoot, ServerModSettings.DocumentsRoot, ServerModNames());
            foreach (var line in result.Skipped)
                NoteLog("Import mod settings: " + line);
            var lines = new List<string>
            {
                result.Added == 1 ? "Imported 1 mod settings file. Players get it within a few seconds."
                    : "Imported " + result.Added + " mod settings files. Players get them within a few seconds."
            };
            if (result.Mods.Count > 0)
                lines.Add(result.Mods.Count > 20
                    ? "Mods changed: " + string.Join(", ", result.Mods.Take(20)) + " and " + (result.Mods.Count - 20) + " more."
                    : "Mods changed: " + string.Join(", ", result.Mods) + ".");
            lines.AddRange(result.Skipped.Take(10));
            if (result.Skipped.Count > 10)
                lines.Add("The other skipped files are listed in the log.");
            ModSettingsStatus.Text = string.Join("\n", lines);
            _actions.Note("Mod settings", "Imported " + result.Added + " file(s) from " + ServerModSettings.Shown(Path.GetFileName(source)) + ", skipped " + result.Skipped.Count);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            ModSettingsStatus.Text = ex.Message;
        }
        finally
        {
            // Whatever was written before a failure shows, with its count in the list.
            ShowModSettings();
        }
    }
}
