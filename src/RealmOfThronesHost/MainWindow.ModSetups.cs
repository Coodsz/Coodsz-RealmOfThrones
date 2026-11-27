using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using BannerlordCoopLauncher.Core.IO;
using BannerlordCoopLauncher.Core.Launch;
using BannerlordCoopLauncher.Core.Modules;
using BannerlordCoopLauncher.Core.Profile;
using BannerlordCoopLauncher.Core.Saves;
using BannerlordCoopLauncher.Core.Server;
using BannerlordCoopLauncher.Core.Steam;
using RealmOfThrones.Core;
using RealmOfThrones.Ui;
using Microsoft.Win32;
using TaomCoop.Core;

namespace RealmOfThronesHost;

// The host side of the Realm of Thrones mod setups. Every setup starts the same way: the Coop nightly
// dedicated server the host picked, with the setup's server list (RotLoadOrder.ServerOrder) staged into
// engine\Modules (RotServerPath.Stage) and written into server-config.json (NightlyServer.ApplyConfig).
// Each extra mod row carries its status from the Realm of Thrones list (RotCompatList).
public partial class MainWindow
{
    private static Brush RequiredBrush => Coodsz.Ui.FrameBrushes.Get("Keep.Teal", "#7DAAA4");
    private static Brush ExtraBrush => Coodsz.Ui.FrameBrushes.Get("Keep.Success", "#8FBF86");
    private static Brush NotLoadedBrush => Coodsz.Ui.FrameBrushes.Get("Keep.Amber", "#D2A36A");
    private bool _fillingModSetups;
    private IReadOnlyList<FoundMod>? _hostMods;

    private TaomSetup HostSetup => _settings.ModSetups.Current();

    // ----- The Realm of Thrones compatibility list -----

    private RotCompatList _compat = RotCompatList.BuiltIn();

    private static string CompatCachePath => ToolData.File(RotCompatList.FileName);

    private void LoadCompatList()
    {
        _compat = RotCompatList.Load(CompatCachePath);
        _ = RefreshCompatListAsync();
    }

    private async Task RefreshCompatListAsync()
    {
        using var http = new System.Net.Http.HttpClient { Timeout = TimeSpan.FromSeconds(15) };
        var online = await RotCompatList.DownloadAsync(http, CompatCachePath, CancellationToken.None);
        if (online == null)
            return;
        _compat = online;
        Dispatcher.Invoke(ShowModSetups);
    }

    internal static Color CompatColor(CompatTag tag) => RotCompatList.BadgeColor(tag) switch
    {
        "green" => Color.FromRgb(0x8F, 0xBF, 0x86),
        "red" => Color.FromRgb(0xE0, 0x7A, 0x6E),
        "blue" => Color.FromRgb(0x8E, 0xB4, 0xD4),
        _ => Color.FromRgb(0xE2, 0xC2, 0x7A)
    };

    private static string ManifestVersion(string? directory)
    {
        if (string.IsNullOrWhiteSpace(directory))
            return "";
        try
        {
            return Directory.Exists(directory) ? BannerlordCoopLauncher.Core.Parsing.ModuleManifestReader.Read(directory)?.Version ?? "" : "";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Xml.XmlException)
        {
            return "";
        }
    }

    // Mods on this PC the server can be given: Steam Workshop items and the game's Modules folder.
    private IReadOnlyList<FoundMod> HostMods(bool rescan = false)
    {
        if (_hostMods != null && !rescan)
            return _hostMods;
        try
        {
            var workshops = SteamInstallFinder.ReadWorkshopLibraries(BannerlordFinder.SteamLibraries(), new PhysicalFileSystem());
            _hostMods = CustomModCatalog.Scan(_gameRoot == null ? "" : Path.Combine(_gameRoot, "Modules"), workshops);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or FormatException or InvalidOperationException)
        {
            _hostMods = Array.Empty<FoundMod>();
        }

        return _hostMods;
    }

    // What the server needs for a setup: the server list and where each module comes from on this PC.
    // Null when the setup breaks the core order.
    private RotServerPlan? ServerPlan(TaomSetup setup, out string? problem)
    {
        problem = null;
        try
        {
            return RotServerPath.Plan(setup, CoreSources(), HostMods());
        }
        catch (InvalidOperationException ex)
        {
            problem = ex.Message;
            return null;
        }
    }

    // The extras of a plan this PC has, by module id.
    private static RotServerModule? StagedExtra(RotServerPlan? plan, string moduleId) =>
        plan?.Stage.FirstOrDefault(module => module.Source == RotModuleSource.Extra && module.SourceDirectory != null
            && module.ModuleId.Equals(moduleId, StringComparison.OrdinalIgnoreCase));

    // The name a core row shows.
    private static string CoreName(string id)
    {
        if (id.Equals(RotStack.CoopModuleId, StringComparison.OrdinalIgnoreCase))
            return "Bannerlord Coop";
        if (id.Equals(RotStack.CompatModuleId, StringComparison.OrdinalIgnoreCase))
            return RotStack.CompatName;
        return RotStack.Libraries.FirstOrDefault(mod => mod.ModuleId.Equals(id, StringComparison.OrdinalIgnoreCase))?.Name ?? id;
    }

    private void ShowModSetups()
    {
        _settings.ModSetups ??= new RotSetupBook();
        _settings.ModSetups.Ensure();
        _fillingModSetups = true;
        ModSetupBox.Items.Clear();
        foreach (var setup in _settings.ModSetups.Setups)
        {
            var item = new ComboBoxItem { Content = setup.Name, Tag = setup.Name };
            ModSetupBox.Items.Add(item);
            if (setup.Name.Equals(_settings.ModSetups.Active, StringComparison.OrdinalIgnoreCase))
                ModSetupBox.SelectedItem = item;
        }

        _fillingModSetups = false;
        // The list shows the working copy: the host's changes show at once, and Save mods keeps them.
        var current = EditingSetup;
        var locked = current.Locked;
        DeleteModSetupButton.IsEnabled = !locked;
        AutoSortHostButton.IsEnabled = !locked;
        RenameHostSetupButton.IsEnabled = !locked;
        AddHostModsButton.IsEnabled = !locked;
        SaveHostModsButton.IsEnabled = !locked;
        if (locked)
            RenameHostSetupPanel.Visibility = Visibility.Collapsed;
        LockedSetupTag.Visibility = locked ? Visibility.Visible : Visibility.Collapsed;
        LockedSetupText.Visibility = locked ? Visibility.Visible : Visibility.Collapsed;
        LockedSetupText.Text = RotSetupBook.LockedNote(current.Name);
        // The server runs or starts: the mod list waits until it stops (ModListBlock).
        ShowModListBlock(locked);
        var blocked = ModListRunning;
        var unsaved = HasUnsavedHostMods;
        HostModsSaveHint.Visibility = unsaved ? Visibility.Visible : Visibility.Collapsed;
        ShareModListHint.Visibility = !locked && (unsaved || current.Name.Equals(_hostEditedSetup, StringComparison.OrdinalIgnoreCase))
            ? Visibility.Visible
            : Visibility.Collapsed;

        OrderList.Items.Clear();
        var plan = ServerPlan(current, out var orderProblem);
        ExtrasNotLoadedBar.Visibility = orderProblem != null ? Visibility.Visible : Visibility.Collapsed;
        ExtrasNotLoadedText.Text = orderProblem ?? "";
        // The extras keep their places, so a row's index still points at the mod in the working copy.
        var shown = current;
        var number = 1;
        // Each mod's version is read once per redraw (RowVersion), and the reports carry the mods the server
        // loads for the setup on screen, in order, with those versions.
        _rowVersions.Clear();
        var loadOrder = RotModChips.ServerReportOrder(shown, id => StagedExtra(plan, id) != null, RowVersion);
        // The side Mods card counts the loaded mods: the core is Always on, an extra Works when the list says so.
        var tally = new List<BannerlordCoopLauncher.Core.Modules.TallyRow>();
        foreach (var row in RotLoadOrder.ServerRows(shown))
        {
            var id = row.ModuleId;
            if (!row.IsCore)
            {
                var extra = shown.Extras[row.ExtraIndex];
                var own = current.Extras[row.ExtraIndex];
                var staged = extra.Enabled ? StagedExtra(plan, id) : null;
                var entry = _compat.Find(extra.ModuleId, extra.WorkshopId, extra.Label);
                tally.Add(new BannerlordCoopLauncher.Core.Modules.TallyRow(extra.ModuleId, staged != null, false, TaomCompatList.Works(entry?.Kind)));
                var note = !extra.Enabled
                    ? "Off"
                    : staged == null
                        ? "Not on this PC"
                        : TaomServerPath.RowNote(staged.Code ?? ServerCode.ClientCodeOnly);
                var version = RowVersion(extra.ModuleId);
                OrderList.Items.Add(new SealedModuleRow(staged == null ? null : number++, extra.Label, note, staged == null ? NotLoadedBrush : ExtraBrush)
                {
                    // No click tip: the chip here does not open the report window, the Report link next to it does.
                    Status = RotModChips.Status(_compat, extra.ModuleId, extra.WorkshopId, extra.Label, reportTip: false, BannerlordCoopLauncher.Core.Localization.Translator.T),
                    Reports = ReportChips(extra.ModuleId, extra.WorkshopId, extra.Label, version, loadOrder, staged?.Code is ServerCode.DataOnly or ServerCode.ServerBuild),
                    ModuleId = own.ModuleId,
                    Editable = !locked,
                    Blocked = blocked,
                    IsOn = own.Enabled
                });
                continue;
            }

            var coreVersion = RowVersion(id);
            tally.Add(new BannerlordCoopLauncher.Core.Modules.TallyRow(id, true, true));
            OrderList.Items.Add(new SealedModuleRow(number++, CoreName(id), BannerlordCoopLauncher.Core.Modules.ModChips.AlwaysOn, RequiredBrush)
            {
                ModuleId = id == RotStack.CoreModuleId && !locked ? id : null,
                Blocked = blocked,
                Tip = RotSetupBook.CoreRowTip,
                Reports = ReportChips(id, null, CoreName(id), coreVersion, loadOrder, serverRunsCode: true)
            });
        }

        ModsSummaryCard.Show(BannerlordCoopLauncher.Core.Modules.ModTally.Count(tally));
        ModuleSummaryText.Text = (number - 1).ToString();
    }

    private void ModSetupBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_fillingModSetups || ModSetupBox.SelectedItem is not ComboBoxItem { Tag: string name })
            return;
        if (name.Equals(_settings.ModSetups.Active, StringComparison.OrdinalIgnoreCase))
            return;
        // While the server only runs, a pick is for the next Start; while it starts or an action runs, the box goes back.
        if (RefuseSetupChangeWhileBusy() || !ConfirmHostModChanges())
        {
            ShowModSetups();
            return;
        }

        _settings.ModSetups.Switch(name);
        _hostEditing = null;
        SaveSettings();
        ShowModSetups();
        var extras = HostSetup.EnabledExtras.ToList();
        NoteLog(extras.Count > 0
            ? HostSetup.Name + " is picked. Start server starts Realm of Thrones plus " + string.Join(", ", extras.Select(mod => mod.Label)) + "."
            : HostSetup.Name + " is picked. Players join with the Realm of Thrones core only.");
    }

    private void DeleteModSetup_Click(object sender, RoutedEventArgs e)
    {
        var setup = HostSetup;
        if (setup.Locked || RefuseSetupChangeWhileBusy())
            return;
        if (Coodsz.Ui.UiLanguage.Message("Delete " + setup.Name + " from the server tool?", "Delete setup", MessageBoxButton.OKCancel, MessageBoxImage.Warning, this) != MessageBoxResult.OK)
            return;
        _settings.ModSetups.Delete(setup.Name);
        _hostEditing = null;
        SaveSettings();
        ShowModSetups();
        NoteLog(setup.Name + " was deleted. " + HostSetup.Name + " is picked.");
    }

    private (string Name, string Version, string? Directory)? DescribeServerModule(string moduleId)
    {
        var modules = string.IsNullOrWhiteSpace(_settings.ServerRoot) ? null : Path.Combine(_settings.ServerRoot, "engine", "Modules");
        var xml = modules == null ? null : FindServerModuleXml(modules, moduleId);
        if (xml == null && _gameRoot != null)
            xml = FindServerModuleXml(Path.Combine(_gameRoot, "Modules"), moduleId);
        if (xml == null)
            return null;
        return (moduleId, ModuleIdentityReader.Read(xml).Version, Path.GetDirectoryName(xml));
    }

    private void ShareModList_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new SaveFileDialog { Filter = "Mod list|*.json", FileName = "coodsz-rot-mod-list.json" };
        if (dialog.ShowDialog(this) != true)
            return;
        try
        {
            SharedModList.Write(dialog.FileName, RotSharedList.Entries(HostSetup, DescribeServerModule));
            NoteLog("Shared mod list saved for " + HostSetup.Name + ". Players click Import mod list in the client launcher and pick that file.");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            NoteLog(ex.Message);
        }
    }

    private void ImportModList_Click(object sender, RoutedEventArgs e)
    {
        // The server runs or starts: the Mods page banner says to stop it first (ModListBlock).
        if (RefuseWhileModListRuns())
            return;
        if (!ConfirmHostModChanges())
            return;
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
            NoteLog("That file is not a Coodsz mod list.");
            return;
        }

        // A mod list from another overhaul (TAOM) is refused before any setup is made for it.
        var foreign = RotSharedList.ForeignGame(shared);
        if (foreign != null)
        {
            NoteLog(foreign);
            Coodsz.Ui.UiLanguage.Message(foreign, "Import mod list", MessageBoxButton.OK, MessageBoxImage.Warning, this);
            return;
        }

        IReadOnlyList<FoundMod> onThisPc;
        try
        {
            var workshops = SteamInstallFinder.ReadWorkshopLibraries(BannerlordFinder.SteamLibraries(), new PhysicalFileSystem());
            onThisPc = CustomModCatalog.Scan(_gameRoot == null ? "" : Path.Combine(_gameRoot, "Modules"), workshops);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or FormatException or InvalidOperationException)
        {
            onThisPc = Array.Empty<FoundMod>();
        }

        var summary = new List<string>();
        var setup = HostSetup;
        if (setup.Locked)
        {
            var from = setup.Name;
            setup = _settings.ModSetups.Create();
            summary.Add("Made " + setup.Name + " for this list, because " + from + " is locked.");
        }

        TaomImportResult result;
        try
        {
            result = RotSharedList.Import(setup, shared, onThisPc);
        }
        catch (InvalidOperationException ex)
        {
            NoteLog(ex.Message);
            Coodsz.Ui.UiLanguage.Message(ex.Message, "Import mod list", MessageBoxButton.OK, MessageBoxImage.Warning, this);
            return;
        }

        if (result.Added.Count > 0)
            summary.Add("Added " + string.Join(", ", result.Added) + " to " + setup.Name + ".");
        if (result.TurnedOff.Count > 0)
            summary.Add("Turned off " + string.Join(", ", result.TurnedOff) + ", because the list does not have them.");
        if (result.Refused.Count > 0)
            summary.Add("Left out " + string.Join(", ", result.Refused) + ", because Realm of Thrones already has them or they do not work with Realm of Thrones.");
        if (result.NoLink.Count > 0)
            summary.Add("Not on this PC and the list has no Workshop link, so get these yourself: " + string.Join(", ", result.NoLink) + ".");
        // Coop is compared by its build, as the list writes it (RotSharedList.InstalledVersion).
        foreach (var entry in shared)
        {
            if (RotSharedList.InstalledVersion(entry, DescribeServerModule(entry.Id), null) is not { } have)
                continue;
            var warning = SharedModList.VersionWarning(entry.Name, entry.Version, have);
            if (warning != null)
                summary.Add(warning);
        }

        if (setup.HasEnabledExtras)
            summary.Add("Start server starts Realm of Thrones plus these mods. Players join with the same setup.");
        else if (summary.Count == 0)
            summary.Add("This setup already matches that list.");

        _hostEditing = null;
        SaveSettings();
        ShowModSetups();
        foreach (var line in summary)
            NoteLog(line);
        Coodsz.Ui.UiLanguage.Message(string.Join("\n\n", summary), "Import mod list", MessageBoxButton.OK, MessageBoxImage.Information, this);
    }

    // ----- Auto sort and checks -----

    private IReadOnlyDictionary<string, TaomModFact> HostModFacts()
    {
        var roots = new List<string>();
        try
        {
            roots.AddRange(SteamInstallFinder.ReadWorkshopLibraries(BannerlordFinder.SteamLibraries(), new PhysicalFileSystem()).Select(library => library.ContentDirectory));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or FormatException or InvalidOperationException)
        {
        }

        if (_gameRoot != null)
            roots.Add(Path.Combine(_gameRoot, "Modules"));
        return TaomModFacts.Gather(roots);
    }

    private void AutoSortHost_Click(object sender, RoutedEventArgs e)
    {
        // The server runs or starts: the Mods page banner says to stop it first (ModListBlock).
        if (RefuseWhileModListRuns())
            return;
        // The same sort as the client launcher, on the working copy. Save mods keeps the new order.
        var setup = EditingSetup;
        if (setup.Locked)
        {
            ModStatus(setup.Name + " is locked. Click New setup or Copy to make a setup you can add mods to.");
            return;
        }

        var manifests = HostModFacts().Where(pair => pair.Value.Manifest != null)
            .ToDictionary(pair => pair.Key, pair => pair.Value.Manifest!, StringComparer.OrdinalIgnoreCase);
        var result = RotAutoSort.Sort(setup, manifests);
        ShowModSetups();
        if (result.Lines.Count == 0)
        {
            ModStatus("Auto sort found nothing to change. The extra mods already load in a good order.");
            return;
        }

        foreach (var line in result.Lines)
            NoteLog(line);
        ModStatus(result.Changed
            ? "Auto sort changed the order. The Logs page lists each move. Click Save mods to keep the new order."
            : string.Join(Environment.NewLine, result.Lines));
    }

    // ----- Install mods on the host -----

    // Install mods shows each step on the Mods page itself (and on Logs), so a click is never silent.
    /// <summary>True (and nothing changes) while Start server prepares or another action holds the tool, since that action
    /// reads the active setup. The header picker is on every page, so the Dashboard line says it too.</summary>
    private bool RefuseSetupChangeWhileBusy()
    {
        if (!_busy && !_startingServer)
            return false;
        ModStatus(ModListBlock.BusyLine, failed: true);
        SetControl(ModListBlock.BusyLine, failed: true);
        return true;
    }

    private void ModStatus(string line, bool failed = false)
    {
        ModSetupStatus.Text = line;
        ModSetupStatus.Foreground = (failed ? Coodsz.Ui.FrameBrushes.Get("Keep.Red", "#E07A6E") : Coodsz.Ui.FrameBrushes.Get("Keep.Teal", "#7DAAA4"));
        ModSetupStatus.Visibility = Visibility.Visible;
        NoteLog(line);
    }

    // The extras of a setup that are not on this PC, plus the library mods that are not. A library goes to Steam
    // by its Workshop id; MCM's official Workshop item was removed, so it is one to get by hand.
    private List<TaomExtraMod> MissingForInstall(TaomSetup setup)
    {
        FindGameRoot();
        var missing = new List<TaomExtraMod>();
        var plan = ServerPlan(setup, out _);
        foreach (var module in plan?.Stage.Where(module => module.SourceDirectory == null) ?? [])
        {
            if (module.Source == RotModuleSource.Extra)
            {
                if (setup.EnabledExtras.FirstOrDefault(mod => mod.ModuleId.Equals(module.ModuleId, StringComparison.OrdinalIgnoreCase)) is { } extra)
                    missing.Add(extra);
            }
            else if (module.Source == RotModuleSource.Library)
            {
                var library = RotStack.Libraries.First(mod => mod.ModuleId.Equals(module.ModuleId, StringComparison.OrdinalIgnoreCase));
                missing.Add(new TaomExtraMod { Name = library.Name, ModuleId = library.ModuleId, WorkshopId = library.WorkshopId });
            }
        }

        return missing;
    }

    private async void InstallHostMods_Click(object sender, RoutedEventArgs e)
    {
        if (_busy || _startingServer)
        {
            ModStatus("Install mods waits until the tool finishes what it is doing now. Click it again in a moment.");
            return;
        }

        // The server runs or starts: the Mods page banner says to stop it first (ModListBlock).
        if (RefuseWhileModListRuns())
            return;

        ModStatus("Looking for the mods of " + EditingSetup.Name + " on this PC.");
        HostMods(rescan: true);
        var missing = MissingForInstall(EditingSetup);
        if (missing.Count == 0)
        {
            ShowModSetups();
            ModStatus(EditingSetup.HasEnabledExtras
                ? "Every mod on " + EditingSetup.Name + " is already on this PC."
                : EditingSetup.Name + " has no extra mods to install, and the four helper mods (Harmony, ButterLib, UIExtenderEx, MCM) are on this PC.");
            return;
        }

        var manual = missing.Where(mod => !SharedModList.IsWorkshopId(mod.WorkshopId)).Select(mod => mod.Label).ToList();
        if (manual.Count > 0)
            ModStatus("These mods are not on the Steam Workshop. Get " + string.Join(", ", manual) + " from Nexus Mods or ModDB and put the folders in the game's Modules folder.", failed: true);
        var names = new Dictionary<ulong, string>();
        foreach (var mod in missing)
        {
            if (SharedModList.IsWorkshopId(mod.WorkshopId) && ulong.TryParse(mod.WorkshopId, out var id))
                names.TryAdd(id, mod.Label);
        }

        if (names.Count == 0)
        {
            if (manual.Count == 0)
                ModStatus("Install mods found nothing it can download. The missing mods have no Steam Workshop link.", failed: true);
            return;
        }

        // With Steam closed nothing is asked or opened: a steam: link would start Steam on pages the
        // host did not ask for, or fail on a PC with no app for steam: links.
        if (!RotSteamPage.SteamRunning())
        {
            ModStatus("Steam is needed for " + string.Join(", ", names.Values) + ". " + RotSteamPage.SteamNotRunning, failed: true);
            return;
        }

        // The game folder, found the same way the client launcher finds it (the Steam libraries).
        if (FindGameRoot() == null)
        {
            ModStatus("Bannerlord was not found in the Steam libraries on this PC, so Install mods cannot ask Steam for the mods. Subscribe to " + string.Join(", ", names.Values) + " on the Steam Workshop yourself, then click Install mods again.", failed: true);
            await RotSteamPage.OpenAsync(names.Keys.Select(id => id.ToString(System.Globalization.CultureInfo.InvariantCulture)), line => ModStatus(line, failed: true));
            return;
        }

        _busy = true;
        try
        {
            ModStatus("Asking Steam to subscribe and download " + string.Join(", ", names.Values) + ".");
            var download = await WorkshopDownload.RunAsync(_gameRoot!, names, line => Dispatcher.Invoke(() => ModStatus(line)), CancellationToken.None);
            if (download.SteamProblem != null)
                ModStatus("Could not subscribe through Steam by itself. " + download.SteamProblem, failed: true);
            var open = download.OpenByHand(names.Keys.ToList());
            if (open.Count > 0)
            {
                ModStatus("Opening the Workshop pages. Subscribe there and wait for the download in Steam.");
                await RotSteamPage.OpenAsync(open.Select(id => id.ToString(System.Globalization.CultureInfo.InvariantCulture)), line => ModStatus(line, failed: true));
            }
        }
        finally
        {
            _busy = false;
        }

        HostMods(rescan: true);
        ShowModSetups();
        var left = MissingForInstall(EditingSetup);
        var still = left.Where(mod => SharedModList.IsWorkshopId(mod.WorkshopId)).Select(mod => mod.Label).ToList();
        if (still.Count > 0)
            ModStatus("Steam is still finishing " + string.Join(", ", still) + ". Click Install mods again in a minute.");
        else
            ModStatus("Install mods finished. Those mods are installed.");
    }

    // ----- Start server -----

    // A save remembers the mods it was made with. Adding mods is fine for Bannerlord (they start
    // fresh in the campaign); leaving out a mod the save was made with can break the campaign.
    // A world belongs to the setup it was made with (RotServerPath.SaveChange).
    private bool ConfirmSaveMatchesSetup(string savePath)
    {
        SaveModChange change;
        try
        {
            change = RotServerPath.SaveChange(savePath, HostSetup);
        }
        catch (InvalidOperationException ex)
        {
            SetControl(ex.Message, failed: true);
            return false;
        }

        if (change.Action == SaveModAction.Start)
            return true;
        if (change.Action == SaveModAction.AskToAdd)
        {
            NoteLog("This setup adds " + string.Join(", ", change.Added) + " to the campaign.");
            var rows = change.AddedRows(id => HostSetup.Extras.FirstOrDefault(mod => mod.ModuleId.Equals(id, StringComparison.OrdinalIgnoreCase))?.Name);
            if (!ConfirmWindow.AskList(this, SaveModChange.AskTitle, change.AskQuestion, rows, SaveModChange.AskBackupNote, change.AskWarning,
                    SaveModChange.AskButton, SaveModChange.AskButtonTip))
                return false;
        }
        else
        {
            NoteLog(change.RefuseText);
            var card = SaveModsCardFor(savePath, change);
            switch (SaveModsWindow.Ask(this, card))
            {
                case "fix" when card.Fix == SaveModFix.UseSetup && _settings.ModSetups.Find(card.SetupName) != null:
                    // The same switch as the setup box, then Start server runs again with that setup.
                    _settings.ModSetups.Switch(card.SetupName);
                    _hostEditing = null;
                    SaveSettings();
                    ShowModSetups();
                    NoteLog(card.SetupName + " is picked. It has every mod " + card.SaveName + " was made with.");
                    _startAgain = true;
                    return false;
                case "othersave":
                    ShowPage("Server");
                    SaveBox.Focus();
                    SaveBox.IsDropDownOpen = true;
                    return false;
                default:
                    return false;
            }
        }

        // The first start with a new mod list keeps a copy of the save, so Restore save backup can undo it.
        try
        {
            var backup = SaveBackup.Create(savePath, SaveBackupFolder);
            NoteLog("Kept a copy of the save before it runs with other mods. Restore save backup on the Saves page puts it back. " + backup);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            SetControl("Could not keep a copy of the save, so the server was not started. " + ex.Message, failed: true);
            return false;
        }
    }

    // Start server runs again once, after the save mods box switched to the setup the save was made with.
    private bool _startAgain;

    // The save mods card: friendly names from the setups and the mods on this PC, and the other
    // setups that load every mod the save was made with. Realm of Thrones setups have no save of their
    // own, so a setup is offered when it loads exactly the save's mods.
    private SaveModCard SaveModsCardFor(string savePath, SaveModChange change)
    {
        var saveModules = CampaignSaveInfoReader.Read(savePath)?.Modules ?? Array.Empty<string>();
        var others = new List<SaveModSetup>();
        foreach (var setup in _settings.ModSetups.Setups.Where(setup => !setup.Name.Equals(HostSetup.Name, StringComparison.OrdinalIgnoreCase)))
        {
            try
            {
                others.Add(new SaveModSetup(setup.Name, "", RotServerPath.FullServerOrder(setup).ToList()));
            }
            catch (InvalidOperationException)
            {
            }
        }

        string? NameOf(string id) =>
            _settings.ModSetups.Setups.SelectMany(setup => setup.Extras)
                .FirstOrDefault(mod => mod.ModuleId.Equals(id, StringComparison.OrdinalIgnoreCase) && mod.Name.Trim().Length > 0)?.Name
            ?? HostMods().FirstOrDefault(mod => mod.ModuleId.Equals(id, StringComparison.OrdinalIgnoreCase))?.Name;
        return SaveModCard.Build(Path.GetFileNameWithoutExtension(savePath), saveModules, change, NameOf,
            id => HostMods().Any(mod => mod.ModuleId.Equals(id, StringComparison.OrdinalIgnoreCase)),
            others, canTurnOn: false, canAddMods: false);
    }

    private string SaveBackupFolder => Path.Combine(_settings.DataRoot, "save-backups");

    private void RestoreSaveBackup_Click(object sender, RoutedEventArgs e)
    {
        if (_busy || _startingServer)
        {
            NoteLog("Wait for the current action to finish.");
            return;
        }
        if (ServerIsRunning())
        {
            NoteLog("Stop the server before a save backup is put back.");
            return;
        }

        Directory.CreateDirectory(SaveBackupFolder);
        var dialog = new OpenFileDialog { Filter = "Save backup|*.sav;*.zip", InitialDirectory = SaveBackupFolder };
        if (dialog.ShowDialog(this) != true)
            return;
        var zip = AutoBackupEntry(dialog.FileName);
        var name = zip?.SaveName ?? SaveBackup.SaveNameOf(dialog.FileName);
        if (name == null)
        {
            NoteLog("That file is not a save backup.");
            return;
        }

        if (!ConfirmWindow.Ask(this, "Restore save backup", "Put this backup back as " + name + "? The save as it is now is kept as another backup first.", null, "Restore"))
            return;
        try
        {
            // An automatic zip goes back the safe way: the save as it is now is zipped first,
            // and on any error the old files are put back.
            var undo = zip != null
                ? AutoBackups().Restore(zip) != null ? AutoBackupFolder : null
                : SaveBackup.Restore(dialog.FileName, ServerSavesDirectory(), SaveBackupFolder);
            ReloadSaves();
            _actions.Note("Backup", "Restored " + name + " from " + Path.GetFileName(dialog.FileName));
            NoteLog("Put the backup back as " + name + "." + (undo == null ? "" : " The save it replaced is kept as a backup too."));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or InvalidDataException or ArgumentException)
        {
            _actions.Note("Backup", "Restore of " + name + " failed: " + ex.Message);
            NoteLog(ex.Message);
        }
    }

    // Lines about the server folder from before this start (the staged modules, the map, the load order).
    // They show on Logs and also go into the run's host-console log.
    private readonly List<string> _startNotes = [];

    private void NoteStart(string line)
    {
        NoteLog(line);
        _startNotes.Add(line);
    }

    // The add-on this exe carries (its ServerAddOns payload, written out on first use).
    private static string CompatAddOn => Path.Combine(ToolPayload.Folder("ServerAddOns"), RotStack.CompatModuleId);

    // Coodsz Server Tools in the same payload (RotServerTools).
    private static string ServerToolsAddOn => Path.Combine(ToolPayload.Folder(RotServerTools.Payload), ServerToolsModule.ModuleId);

    // Realm of Thrones Coop Compat from the payload, or "" when this build has none (the plan then names it as missing).
    private static string CompatModuleSource() => File.Exists(Path.Combine(CompatAddOn, "SubModule.xml")) ? CompatAddOn : "";

    private RotCoreSources CoreSources() =>
        new(_gameRoot, BannerlordFinder.SteamLibraries().ToList(), CompatModuleSource());

    // The tool's own folder for the staged server map (RotServerPath.MapStageFolder).
    private static string ServerMapRoot => ToolData.Folder;

    /// <summary>
    /// The server side of Start server and Prepare server, in the order the start needs: the problems first
    /// (RotServerPath.Problems and the mod checks, each a plain line, the first one under Start server), then
    /// the modules staged into the server folder, then the server map (Stage-RotServerMap.ps1 when the staged map
    /// is missing, then ApplyMap). Returns the plan, or null with the reason shown.
    /// </summary>
    private async Task<RotServerPlan?> PrepareRotServerAsync(string serverRoot)
    {
        if (FindGameRoot() == null)
        {
            SetControl(GameMissingLine, failed: true);
            ShowPage("Dashboard");
            return null;
        }

        var plan = ServerPlan(HostSetup, out var orderProblem);
        if (plan == null)
        {
            SetControl(orderProblem ?? "The mod order of this setup is not one the server can load.", failed: true);
            return null;
        }

        var problems = RotServerPath.Problems(plan, serverRoot, CarriesServerCoop()).ToList();
        var issues = RotModChecks.Run(plan.Setup, HostModFacts());
        foreach (var issue in issues.Where(issue => !issue.Blocks))
            NoteStart(issue.Text);
        problems.AddRange(issues.Where(issue => issue.Blocks).Select(issue => issue.Text));
        if (problems.Count > 0)
        {
            foreach (var problem in problems)
                NoteLog(problem);
            SetControl(RotUiText.FirstProblem(problems), failed: true);
            return null;
        }

        // The map is made before the server folder changes, so a map that cannot be made leaves the server's
        // own copy (one made by hand, say) where it is.
        var map = await MakeServerMapAsync(plan);
        if (map == null)
            return null;

        SetControl("Copying Realm of Thrones and the mods to the server.");
        try
        {
            var lines = await Task.Run(() => RotServerPath.Stage(serverRoot, plan));
            foreach (var line in lines)
                NoteStart(line);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            SetControl(ex.Message, failed: true);
            return null;
        }

        if (!await ApplyServerMapAsync(serverRoot, plan, map))
            return null;
        // Coodsz Server Tools for the server rules and the mod settings for players. Without it the server still runs, only
        // without those, so a copy that fails is a log line and never stops the start.
        try
        {
            if (RotServerTools.Install(serverRoot, ServerToolsAddOn) is { } toolsLine)
                NoteStart(toolsLine);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            NoteStart(ServerToolsModule.Name + ": " + ex.Message);
        }

        // The mod settings players get: the server's own copy takes this PC's files it is missing, and the looks mods
        // go into recipes.json so players keep their own values for them (ServerModSettings).
        try
        {
            var copied = ServerModSettings.Prepare(_settings.DataRoot, ServerToolsModule.ModuleFolder(serverRoot), ServerModSettings.DocumentsRoot, CommunityCompat.Current, line => NoteStart("Mod settings for players: " + line));
            if (copied > 0)
                NoteStart("Copied " + copied + " mod settings files from this PC to the server, so players get them too.");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            NoteStart("Mod settings for players: " + ex.Message);
        }

        var modules = ServerToolsModule.Installed(serverRoot) ? RotServerTools.ServerModules(plan.Modules) : plan.Modules;
        NoteStart("Server load order: " + string.Join(", ", NightlyServer.FullOrder(RotStack.CoopModuleId, modules, warSails: true)) + ".");
        ShowKingdomProtection();
        return plan;
    }

    private const string GameMissingLine =
        "Bannerlord was not found, so Realm of Thrones cannot be copied to the server. Click Find Bannerlord on the Dashboard and pick the game folder.";

    private sealed record ServerMap(string Stage, bool MadeNow);

    // The last map line (RotServerPath.MapLine), shown with "Server prepared." too.
    private string _mapLine = "";

    // The server cannot read Realm of Thrones' full campaign map. The compat module's Stage-RotServerMap.ps1 makes
    // the server's map from the host's ROT_Map once per map version, into the tool's own folder (MakeServerMapAsync);
    // ApplyMap puts it into the server copies on every start, after Stage (ApplyServerMapAsync). Each says one line
    // about the map: made now, already made or already in the server, or missing and why.
    private async Task<bool> ApplyServerMapAsync(string serverRoot, RotServerPlan plan, ServerMap map)
    {
        try
        {
            var copied = await Task.Run(() => RotServerPath.ApplyMap(serverRoot, plan, map.Stage));
            _mapLine = RotServerPath.MapLine(map.MadeNow, copied);
            NoteStart(_mapLine);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            SetControl(ex.Message, failed: true);
            return false;
        }
    }

    // Null with the reason shown when the map could not be made.
    private async Task<ServerMap?> MakeServerMapAsync(RotServerPlan plan)
    {
        var map = plan.Stage.First(module => module.ModuleId.Equals(RotStack.MapModuleId, StringComparison.OrdinalIgnoreCase));
        var compat = plan.Stage.First(module => module.Source == RotModuleSource.Compat);
        string stage;
        try
        {
            stage = RotServerPath.MapStageFolder(ServerMapRoot, map.Version ?? "");
        }
        catch (InvalidOperationException ex)
        {
            SetControl(ex.Message, failed: true);
            return null;
        }

        if (RotServerPath.MapProblem(stage) == null)
            return new ServerMap(stage, MadeNow: false);
        if (map.SourceDirectory == null || compat.SourceDirectory == null)
        {
            SetControl(RotServerPath.MapProblem(stage) + " " + RotServerPath.MapSourceMissing, failed: true);
            return null;
        }

        SetControl("Making the Realm of Thrones map for the server. The first time takes a few minutes. Please wait and keep the tool open.");
        // The script needs a new or empty folder; a folder left half written by an earlier run goes first.
        // The moving bar shows it still works (RefreshState leaves it up while _makingMap).
        MapScriptResult result;
        _makingMap = true;
        ShowStartProgress(null);
        try
        {
            result = await Task.Run(() => RunMapScript(stage, RotServerPath.MapStageStart(compat.SourceDirectory, map.SourceDirectory, stage)));
        }
        finally
        {
            _makingMap = false;
            HideStartProgress();
        }
        foreach (var line in result.Lines)
            NoteStart(line);
        if (RotServerPath.MapProblem(stage) is { } stillMissing)
        {
            SetControl(stillMissing + " " + result.Failure, failed: true);
            return null;
        }
        return new ServerMap(stage, MadeNow: true);
    }

    private sealed record MapScriptResult(IReadOnlyList<string> Lines, string Failure);

    private bool _makingMap;

    private static readonly TimeSpan MapScriptTimeout = TimeSpan.FromMinutes(30);

    private const string MapScriptFailed = "Making the map failed. Open the Logs page for the lines, then click Prepare server again.";

    // Runs Stage-RotServerMap.ps1 hidden and waits for it. Its output lines go to the log.
    private static MapScriptResult RunMapScript(string stage, BannerlordCoopLauncher.Core.Server.NightlyServerStart start)
    {
        var lines = new List<string>();
        try
        {
            var root = Path.GetFullPath(Path.Combine(ServerMapRoot, "ServerMap"));
            if (Directory.Exists(stage) && Path.GetFullPath(stage).StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                Directory.Delete(stage, recursive: true);
            Directory.CreateDirectory(stage);
            var info = new ProcessStartInfo(start.FileName)
            {
                WorkingDirectory = start.WorkingDirectory,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };
            foreach (var argument in start.Arguments)
                info.ArgumentList.Add(argument);
            using var process = Process.Start(info);
            if (process == null)
                return new MapScriptResult(lines, "PowerShell did not start.");
            process.OutputDataReceived += (_, args) => { if (!string.IsNullOrWhiteSpace(args.Data)) lock (lines) lines.Add(args.Data.Trim()); };
            process.ErrorDataReceived += (_, args) => { if (!string.IsNullOrWhiteSpace(args.Data)) lock (lines) lines.Add(args.Data.Trim()); };
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();
            // A script that hangs would hold Start, Stop and Prepare server until the tool closes.
            if (!process.WaitForExit(MapScriptTimeout))
            {
                try { process.Kill(entireProcessTree: true); }
                catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception) { }
                return new MapScriptResult(lines, MapScriptFailed);
            }

            process.WaitForExit();
            if (process.ExitCode != 0)
                lock (lines)
                    lines.Add("Stage-RotServerMap.ps1 exit code " + process.ExitCode + ".");
            return new MapScriptResult(lines, process.ExitCode == 0 ? "" : MapScriptFailed);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            return new MapScriptResult(lines, ex.Message);
        }
    }
}
