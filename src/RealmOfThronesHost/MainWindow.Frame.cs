using System.IO;
using System.Windows;
using System.Windows.Controls;
using BannerlordCoopLauncher.Core.Localization;
using BannerlordCoopLauncher.Core.Modules;
using BannerlordCoopLauncher.Core.Server;
using BannerlordCoopLauncher.Core.Steam;
using BannerlordCoopLauncher.Core.Support;
using RealmOfThrones.Core;
using Coodsz.Ui;

namespace RealmOfThronesHost;

// The shared frame of the Coop Server Tool (redesign wave 6): the compact sidebar, the header pill, the Dashboard with
// the one big button and the first run steps, and the running block of the Mods page. Every button runs the code the
// old Server page had before.
public partial class MainWindow
{
    // The server ran at the last state refresh (RefreshState), so the Mods page does not ask Windows again.
    private bool _serverRunning;

    /// <summary>Called once from the constructor: the log card's Open full log goes to the Logs page, and the columns follow the width.</summary>
    private void StartDashboard()
    {
        DashLog.OpenFullLog += () => ShowPage("Logs");
        DashboardPage.SizeChanged += (_, e) => FitDashboard(e.NewSize.Width);
    }

    private const double DashSideWidth = 300;

    // The side column sits beside the main one while both fit, and under it on a narrow window.
    private void FitDashboard(double width)
    {
        var stack = FrameRules.SideColumnWraps(width, DashSideWidth);
        Grid.SetColumnSpan(DashMain, stack ? 2 : 1);
        DashMain.Margin = new Thickness(0, 0, stack ? 0 : 12, 0);
        Grid.SetRow(DashSide, stack ? 1 : 0);
        Grid.SetColumn(DashSide, stack ? 0 : 1);
        Grid.SetColumnSpan(DashSide, stack ? 2 : 1);
        DashSideColumn.Width = new GridLength(stack ? 0 : DashSideWidth);
    }

    /// <summary>From RefreshState: the header pill, the big button, the running block and the Dashboard values.</summary>
    private void ShowFrame(bool running, bool serving, bool failed)
    {
        _serverRunning = running;
        if (failed)
            StatePill.Show(StatusKind.Crashed, "Crashed");
        else if (!running)
            StatePill.Show(StatusKind.Stopped, "Stopped");
        else if (serving)
            StatePill.Show(StatusKind.Online, "Server online");
        else
            StatePill.Show(StatusKind.Starting, "Starting");
        // The pill already says Stopped; the line under it shows the uptime, the start progress or the error.
        StateDetail.Visibility = running || failed ? Visibility.Visible : Visibility.Collapsed;
        ShowServerActions(running);
        ApplyModListBlock();
        RefreshDashboard();
    }

    // The big button (Start server or Stop server in one spot) and the buttons that only work on a running server.
    private void ShowServerActions(bool running)
    {
        StartButton.Visibility = running ? Visibility.Collapsed : Visibility.Visible;
        StopButton.Visibility = running ? Visibility.Visible : Visibility.Collapsed;
        RunningActions.Visibility = running ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>Redraws the Dashboard values while it shows.</summary>
    private void RefreshDashboard()
    {
        if (_page != "Dashboard")
            return;
        var save = SelectedSavePath();
        DashSave.Text = string.IsNullOrWhiteSpace(save) ? "-" : Path.GetFileNameWithoutExtension(save);
        DashSetup.Text = HostSetup.Name;
        DashPlayers.Text = _onlinePlayers.Count.ToString();
        DashPort.Text = "UDP " + JoinPort;
        var hasPassword = !string.IsNullOrWhiteSpace(PasswordBox.Text);
        PasswordChip.Kind = hasPassword ? ChipKind.Gold : ChipKind.Mute;
        PasswordChip.Text = hasPassword ? "Password Set" : "Password Open";
        var steam = SteamBox.IsChecked == true;
        SteamChip.Kind = steam ? ChipKind.Ok : ChipKind.Mute;
        SteamChip.Text = steam ? "Steam lobby On" : "Steam lobby Off";
        ShowMadeFor();
        DashSetupLine.Text = HostSetup.Name;
        ShowFirstRunSteps(save);
    }

    private string _stepsShown = "";

    // Set while the strip shows, cleared when the server runs.
    private bool _stepsStay;

    // First run: the strip shows until a Coop server is chosen, it is prepared and a save is picked, and then
    // stays (with Start server as the last step) until the server runs, so Start server does not jump up under
    // the mouse the moment a campaign name is typed.
    private void ShowFirstRunSteps(string? save)
    {
        // A typed new campaign counts too: Start server makes it, no save needed.
        var hasSave = (!string.IsNullOrWhiteSpace(save) && File.Exists(save)) || NewCampaignBox.Text.Trim().Length > 0 || _settings.NewCampaignName.Trim().Length > 0;
        var hasServer = !string.IsNullOrWhiteSpace(_settings.ServerRoot) && File.Exists(Path.Combine(_settings.ServerRoot, NightlyServer.ExeName));
        // Prepare server (or a Start) staged Realm of Thrones into the server once.
        var prepared = hasServer && File.Exists(Path.Combine(RotServerPath.ModulesRoot(_settings.ServerRoot), RotStack.CoreModuleId, "SubModule.xml"));
        var needed = !hasSave || !prepared;
        _stepsStay = !_serverRunning && (needed || _stepsStay);
        var show = _stepsStay;
        FirstRunCard.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
        var key = hasSave + "|" + hasServer + "|" + prepared + "|" + Translator.Current;
        if (!show || key == _stepsShown)
            return;
        _stepsShown = key;
        var openSaves = Translator.T("Open Saves");
        // With the RoT build of Coop carried, Prepare server makes the server itself (RotServerBuild), so there is nothing to choose.
        FirstRunSteps.Show(new[]
        {
            CarriesServerCoop() ? null : new FrameStep(Translator.T("Choose Coop server"), hasServer, Translator.T("Choose Coop server"),
                Translator.T("Pick the folder of the unpacked Coop server. The server runs from that folder."), () => PickCoopServer_Click(this, new RoutedEventArgs())),
            new FrameStep(Translator.T("Prepare server"), prepared, openSaves, Translator.T("Opens the Saves page, where Prepare server is."), () => ShowPage("Server")),
            new FrameStep(Translator.T("Start a new campaign, or use one you have"), hasSave, openSaves,
                Translator.T("Opens the Saves page, where you pick a save or type a new campaign."), () => ShowPage("Server")),
            new FrameStep(Translator.T("Start server"), false),
        }.OfType<FrameStep>().ToList());
    }

    // The newest lines of the Logs page on the Dashboard's Recent server output card.
    private void PushDashboardLines(string text)
    {
        foreach (var line in text.Split('\n').Select(part => part.TrimEnd('\r')).Where(part => part.Trim().Length > 0).TakeLast(DashLog.MaxLines))
            DashLog.Push(DateTime.Now, "", Translator.T(line));
    }

    // ----- The Encyclopedia page -----

    // A button and our own short line per link (RotEncyclopedia, the same list as the Client Launcher's). The words are
    // plain English here, so the window's language pass translates them like the XAML text.
    private void ShowEncyclopedia()
    {
        EncyclopediaNote.Text = RotEncyclopedia.Note;
        foreach (var link in RotEncyclopedia.Links)
        {
            var open = new Button { Content = link.Title, Tag = link.Url, ToolTip = link.Url, Style = (Style)FindResource("Ghost"), MinWidth = 170, Margin = new Thickness(0, 0, 12, 0) };
            open.Click += EncyclopediaLink_Click;
            var line = new TextBlock { Text = link.Line, FontFamily = new System.Windows.Media.FontFamily("Segoe UI"), FontSize = 14, Foreground = SettingHint, TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center };
            var row = new DockPanel { Margin = new Thickness(0, 0, 0, 8) };
            DockPanel.SetDock(open, Dock.Left);
            row.Children.Add(open);
            row.Children.Add(line);
            EncyclopediaList.Children.Add(row);
        }
    }

    // A PC with no browser set makes the open throw, which would close the tool, so a failure shows the link under the list.
    private void EncyclopediaLink_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is not string url || ShellOpen.TryOpen(url, out _))
            return;
        EncyclopediaStatus.Text = Translator.T(RotEncyclopedia.CouldNotOpen) + " " + url;
        EncyclopediaStatus.Visibility = Visibility.Visible;
    }

    // ----- The compact sidebar -----

    // The 64 px sidebar keeps the brand mark and the icons; Send my logs and the footer buttons get an icon and a More menu.
    private void ShowCompactSidebar(bool compact)
    {
        BrandText.Visibility = compact ? Visibility.Collapsed : Visibility.Visible;
        SendLogsSlot.Visibility = compact ? Visibility.Collapsed : Visibility.Visible;
        SendLogsCompact.Visibility = compact && SendLogsSlot.Content != null ? Visibility.Visible : Visibility.Collapsed;
        FooterFit.Visibility = compact ? Visibility.Collapsed : Visibility.Visible;
        CompactMore.Visibility = compact ? Visibility.Visible : Visibility.Collapsed;
        System.Windows.Automation.AutomationProperties.SetName(SendLogsCompact, Translator.T("Send my logs"));
        System.Windows.Automation.AutomationProperties.SetName(CompactMore, Translator.T("More"));
    }

    // The compact sidebar's More menu clicks the same buttons the wide footer shows.
    private void CompactMore_Click(object sender, RoutedEventArgs e)
    {
        var menu = new ContextMenu { PlacementTarget = CompactMore, Placement = System.Windows.Controls.Primitives.PlacementMode.Right };
        void Add(string header, string tip, Action run)
        {
            var item = new MenuItem { Header = Translator.T(header), ToolTip = Translator.T(tip) };
            item.Click += (_, _) => run();
            menu.Items.Add(item);
        }
        void Press(object? button) => (button as Button)?.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
        if (FindMyReports() is { } reports)
            Add(ReportReplyText.MyReports, ReportReplyText.MyReportsTip, () => Press(reports));
        Add("Language", "Picks the language of this window.", () => Press(LanguageSlot.Content));
        Add("What's new", "Shows what changed in this version.", () => Press(WhatsNewSlot.Content));
        Add("Donate", "Opens Coodsz's donation page in your browser.", () => Press(DonateSlot.Content));
        Add("Join my server", "The Realm Bannerlord, my custom modded server", () => ShowPage("Realm"));
        menu.IsOpen = true;
    }

    // The My reports button under Send my logs, once it shows (a report was sent or answered).
    private Button? FindMyReports()
    {
        static Button? Find(DependencyObject node)
        {
            if (node is Button button && System.Windows.Automation.AutomationProperties.GetAutomationId(button) == "MyReportsButton")
                return button;
            foreach (var child in LogicalTreeHelper.GetChildren(node).OfType<DependencyObject>())
                if (Find(child) is { } found)
                    return found;
            return null;
        }

        if (SendLogsSlot.Content is not DependencyObject content || Find(content) is not { } reports)
            return null;
        for (DependencyObject? node = reports; node != null && !ReferenceEquals(node, SendLogsSlot); node = LogicalTreeHelper.GetParent(node))
            if (node is UIElement { Visibility: Visibility.Collapsed })
                return null;
        return reports;
    }

    // ----- The running block of the Mods page -----

    /// <summary>The server runs, a Start is under way, or another action holds the tool (_busy: the gap of a restart, a save,
    /// a backup, Install mods), like the Coop Server Tool counts _controlBusy (ModListBlock.Running). A mod test runs in its
    /// own window, which holds the tool until it ends, so it never leaves the page open to changes.</summary>
    private bool ModListRunning => ModListBlock.Running(_serverRunning, modTestRunning: false, restartPending: _startingServer || _busy);

    // The banner names the real reason: the server holds the mods, or only an action is under way.
    private string ModListBlockLine => _serverRunning || _startingServer ? ModListBlock.RunningLine : ModListBlock.BusyLine;

    private bool? _modListBlockShown;

    /// <summary>Called once from the constructor.</summary>
    private void StartModsPage()
    {
        ModsRunningBanner.Title = ModListBlockLine;
        ModsRunningBanner.ActionText = "Go to Dashboard";
        ModsRunningBanner.ActionTip = "Opens the Dashboard, where Stop server is.";
        ModsRunningBanner.Action += () => ShowPage("Dashboard");
    }

    /// <summary>From RefreshState: when the server starts or stops, the rows and buttons are drawn again (ShowModSetups).</summary>
    private void ApplyModListBlock()
    {
        var running = ModListRunning;
        ModsRunningBanner.Title = ModListBlockLine;
        if (_modListBlockShown == running)
            return;
        _modListBlockShown = running;
        ShowModSetups();
    }

    /// <summary>From ShowModSetups: while the mod list runs, Add mods, Save mods, Install mods, Import mod list and Auto
    /// sort are off as well; the setup buttons and Share mod list stay on, and a setup picked now is for the next Start.</summary>
    private void ShowModListBlock(bool locked)
    {
        var running = ModListRunning;
        foreach (var control in new Control[] { AddHostModsButton, SaveHostModsButton, AutoSortHostButton })
            control.IsEnabled = !locked && !running;
        // A greyed button on the locked setup says why when the mouse rests on it.
        foreach (var (button, tip) in new[]
                 {
                     (AddHostModsButton, "Picks mods already on this PC, from the Steam Workshop or the game's Modules folder."),
                     (SaveHostModsButton, "Keeps the mod changes on this setup. Start server uses the saved list.")
                 })
        {
            ToolTipService.SetShowOnDisabled(button, true);
            button.ToolTip = Translator.T(locked ? "This setup is locked. Click Copy first, then change the copy." : tip);
        }
        foreach (var control in new Control[] { InstallHostModsButton, ImportListItem })
            control.IsEnabled = !running;
        ModsRunningBanner.Visibility = running ? Visibility.Visible : Visibility.Collapsed;
        SetupRunningNote.Visibility = running ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>True (and nothing changes) while the mod list runs; the banner at the top of the page says why.</summary>
    private bool RefuseWhileModListRuns()
    {
        if (!ModListRunning)
            return false;
        _modListBlockShown = null;
        ApplyModListBlock();
        return true;
    }

    private void ModsMore_Click(object sender, RoutedEventArgs e)
    {
        if (ModsMoreButton.ContextMenu is not { } menu)
            return;
        menu.PlacementTarget = ModsMoreButton;
        menu.Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom;
        menu.IsOpen = true;
    }
    private DateTime _madeForRead;

    // The Made for card: the game, Realm of Thrones and Coop in the server folder (or this PC's game before Prepare server) against
    // what this tool pins (RotStack), read again every 10 seconds, and the notes it had before.
    private void ShowMadeFor()
    {
        if (DateTime.Now - _madeForRead < TimeSpan.FromSeconds(10) && MadeForCard.Pins.Count > 0)
            return;
        _madeForRead = DateTime.Now;
        // The newer game line (NewerGame) reads this PC's game, the one the mods are copied from (the server's own
        // Native is 1.4.8). It goes first among the notes and under Start server.
        var newerGame = NewerGame.Notice(_gameRoot, realmOfThrones: true);
        NewerGameText.Text = newerGame ?? "";
        NewerGameText.Visibility = newerGame == null ? Visibility.Collapsed : Visibility.Visible;
        MadeForCard.Show(new[]
        {
            new VersionPin("Bannerlord", PinIcon.Game, RotStack.GameVersion, ServerModuleVersion("Native"), BaseGame: true),
            new VersionPin(RotStack.Name, PinIcon.Mod, RotStack.RotVersion, ServerModuleVersion(RotStack.CoreModuleId)),
            // No Coop version pin: every nightly says v0.1.8; the exact build is checked before Start (RotServerPath.CoopBuildProblem).
            // The nightly server keeps its Coop in engine\Modules\Coop under the id CoopNightly.
            new VersionPin("Coop", PinIcon.Coop, null, ServerModuleVersion("Coop") is { Length: > 0 } coop ? coop : ServerModuleVersion(RotStack.CoopModuleId)),
        }, RealmOfThrones.Ui.RotUiText.OffInFirstVersion.Prepend(RealmOfThrones.Ui.RotUiText.WarSailsLine).Prepend(newerGame ?? "").ToArray());
    }

    // A module's version in the server folder, else in this PC's game, else from the mod scan; empty when none has it.
    private string ServerModuleVersion(string moduleId)
    {
        foreach (var modules in new[]
                 {
                     string.IsNullOrWhiteSpace(_settings.ServerRoot) ? null : Path.Combine(_settings.ServerRoot, "engine", "Modules"),
                     _gameRoot == null ? null : Path.Combine(_gameRoot, "Modules"),
                 })
            if (modules != null && ManifestVersion(Path.Combine(modules, moduleId)) is { Length: > 0 } version)
                return version;
        return RowVersion(moduleId);
    }
}
