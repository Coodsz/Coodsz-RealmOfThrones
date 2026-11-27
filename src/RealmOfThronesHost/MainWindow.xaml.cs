using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using BannerlordCoopLauncher.Core.IO;
using BannerlordCoopLauncher.Core.Launch;
using BannerlordCoopLauncher.Core.Modules;
using BannerlordCoopLauncher.Core.Saves;
using BannerlordCoopLauncher.Core.Steam;
using BannerlordCoopLauncher.Core.Support;
using BannerlordCoopLauncher.Core.Updates;
using BannerlordCoopLauncher.Core.Server;
using Microsoft.Win32;
using RealmOfThrones.Core;
using TaomCoop.Core;

namespace RealmOfThronesHost;

public partial class MainWindow : Window
{
    private readonly string _settingsPath = ToolData.File(RotServerSettings.FileName);
    private const int MaxShownReplyLines = 250;
    private const int MaxReplyReadBytes = 96_000;
    private const string ReplyCutNotice = "Showing the first 250 lines. The rest stayed in the server log so this window would not freeze.";
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(2) };
    private readonly DispatcherTimer _unblockPulse = new() { Interval = TimeSpan.FromMilliseconds(450) };
    private bool _unblockBright;
    private RotServerSettings _settings = new();
    private Process? _process;
    // CPU and memory of the server engine, on the overview.
    private readonly ServerLoad _load = new();
    private string? _gameRoot;
    private string? _gameSearchReason;
    private bool _gameSearchFailureLogged;
    private bool _busy;
    private bool _autosaveHeld;
    private bool _spareUntilOnMap;
    private bool _saving;
    // Counts the server's "world saved to 'x.sav'" lines, so Stop waits for the whole save.
    private int _worldSavedCount;
    private volatile string _lastWorldSavedFile = "";
    private readonly ServerLogTail _logTail = new(200_000);
    private string _cachedLogText = "";
    private string _cachedLogPath = "";
    private long _cachedLogLength = -1;
    private string _followedLogPath = "";
    private long _followedLogLength;
    private DateTime _cachedLogWrite = DateTime.MinValue;
    private bool _seenServing;
    // The fatal @DS@ state event of this start, kept after the server exits until the next start.
    private ServerState? _fatalState;
    private readonly HashSet<string> _joinAdvice = new(StringComparer.Ordinal);
    private readonly BannerlordCoopLauncher.Core.Server.SharedSteamIdWatch _sharedSteamIds = new();
    private bool _blockedDllNoted;
    // The statuses of this run's reports that do not fail, each shown once.
    private readonly HashSet<string> _infoReportsNoted = new(StringComparer.Ordinal);
    private string _servingCheckedPath = "";
    private long _servingScanOffset;
    private string _playerListSignature = "";
    private string _playerSourcePath = "";
    private string _playerSourceText = "";
    private long _playerSourceLength = -1;
    private TextBox? _battleSizeBox;
    private List<HostPlayer> _onlinePlayers = [];
    private readonly Dictionary<string, DateTime> _banKicks = new(StringComparer.OrdinalIgnoreCase);
    private HostActionLog _actions = null!;

    public MainWindow()
    {
        InitializeComponent();
        // A window bigger than the screen (1366x768 at 150 percent) opened with its right side and the Start server card off screen.
        var fit = WindowFit.Fit(new WindowSize(Width, Height, MinWidth, MinHeight), SystemParameters.WorkArea.Width, SystemParameters.WorkArea.Height);
        (Width, Height, MinWidth, MinHeight) = (fit.Width, fit.Height, fit.MinWidth, fit.MinHeight);
        // Under 1000 px the sidebar shows icons only.
        Coodsz.Ui.NavRail.Fit(this, NavColumn, changed: ShowCompactSidebar);
        StartDashboard();
        StartModsPage();
        ShowEncyclopedia();
        // With the RoT build of Coop carried, Prepare server makes the server itself (RotServerBuild), so the Saves page has
        // no Choose Coop server step. Set before the window loads, so the language pass translates these lines too.
        if (CarriesServerCoop())
        {
            ChooseServerCard.Visibility = Visibility.Collapsed;
            PrepareStepNumber.Text = "1";
            CampaignStepNumber.Text = "2";
            SavesStepsIntro.Text = "The steps are the same as on the Dashboard. Do 1 and 2 here, then click Start server on the Dashboard. ";
            PrepareStepText.Text = "Makes the server from the Steam Workshop Coop server, War Sails from your game and the RoT build of Coop this tool carries, then copies Realm of Thrones and the mods into it and makes the server map. If it says something is missing, follow that line.";
        }

        foreach (var box in new[] { LogFileBox, FastForwardBox, BattleSpeedLimitBox })
            ShowOnOff(box);
        LanguageSlot.Content = Coodsz.Ui.UiLanguage.MakeButton();
        WhatsNewSlot.Content = Coodsz.Ui.UiLanguage.MakeWhatsNewButton();
        DonateSlot.Content = Coodsz.Ui.UiLanguage.MakeDonateButton();
        SetUpCrashReports();
        Coodsz.Ui.NewBadges.Attach(GameSpeedButton, NewFeatures.GameSpeed);
        Coodsz.Ui.NewBadges.Attach(GameSpeedBattleButton, NewFeatures.GameSpeed);
        _actions = new HostActionLog(() =>
            string.IsNullOrWhiteSpace(_settings.DataRoot) ? null : Path.Combine(_settings.DataRoot, "logs"));
        _unblockPulse.Tick += (_, _) => PulseUnblockButton();
        LoadSettings();
        // The server's data folder (--data-dir) is always the tool's own, so nothing is shared with another tool.
        _settings.DataRoot = NightlyServer.DataDir(CoopDataRoot);
        FindGameRoot();
        ShowModSetups();
        SetUpModReports();
        Closing += HostModSetups_Closing;

        LoadConfigIntoFields();
        LoadTimePermissions();
        RefreshGameSpeed();
        ReloadSaves();
        ShowBannedPlayers();
        ServerAddressBox.Text = _settings.ServerAddress ?? LocalAddress();
        ShowServerPath();
        BuildGameplaySettings();
        StartServerRules();
        StartCheats();
        StartConsole();
        _timer.Tick += (_, _) => RefreshState();
        _timer.Start();
        SeedLogs();
        StartJoining();
        RefreshState();
        ShowPage(StartPage);
        NoteGameSearchFailure();
        // The update check watches the release repo (RotRelease); a placeholder repo turns it off and the log says so.
        if (RotRelease.UpdatesReady)
            _ = WatchForUpdateAsync(RotRelease.Repo, RotRelease.Version);
        else
            NoteLog(RotRelease.UpdatesNotSetUp);
    }

    // The tool's own COOP_DATA_DIR folder: mod-config.json, and the server's data folder (DedicatedServer) in it.
    private static string CoopDataRoot => Path.Combine(ToolData.Folder, "CoopData");

    // The saved pick first, then the Steam libraries. Prepare server and Install mods ask again, so a
    // game installed after the tool opened is still found.
    private string? FindGameRoot()
    {
        if (_gameRoot != null)
            return _gameRoot;
        var saved = GameFolderPick.CheckSaved(_settings.GamePath);
        if (saved.Result == GameFolderPickResult.Ok)
        {
            ApplyGameFound(saved.Root!);
            return _gameRoot;
        }

        var search = BannerlordFinder.FindGameWithReason();
        if (search.Root != null)
        {
            ApplyGameFound(search.Root);
            return _gameRoot;
        }

        FindBannerlordButton.Visibility = Visibility.Visible;
        ShowServerPath();
        _gameSearchReason = search.Reason ?? GameSearch.GameMissingReason;
        // At startup the reason waits for SeedLogs, which replaces the log text.
        if (IsLoaded)
            NoteGameSearchFailure();
        return null;
    }

    private void NoteGameSearchFailure()
    {
        if (_gameSearchFailureLogged || _gameSearchReason == null)
            return;
        NoteLog(_gameSearchReason);
        _gameSearchFailureLogged = true;
    }

    private void ShowServerPath() =>
        PathText.Text = FindBannerlordButton.Visibility == Visibility.Visible
            ? GameFolderPick.NotFoundText
            : string.IsNullOrWhiteSpace(_settings.ServerRoot)
                ? CarriesServerCoop() ? NoServerYet : "No Coop server is chosen yet. Click Choose Coop server."
                : "Server folder: " + _settings.ServerRoot;

    // Only a folder the player picked is saved as GamePath; a Steam found one is looked up again next time.
    private void ApplyGameFound(string root)
    {
        // The mod catalogue reads the game's Modules folder, so one scanned before this game folder scans again.
        if (!string.Equals(_gameRoot, root, StringComparison.OrdinalIgnoreCase))
            _hostMods = null;
        _gameRoot = root;
        // The Made for card and the newer game line read this game at the next Dashboard refresh, not 10 seconds later.
        _madeForRead = DateTime.MinValue;
        FindBannerlordButton.Visibility = Visibility.Collapsed;
        ShowServerPath();
    }

    private void FindBannerlord_Click(object sender, RoutedEventArgs e)
    {
        // A pick can set the server folder, so it waits like every other server folder action.
        if (_busy || _startingServer)
        {
            SetControl("Wait for the current action to finish.", failed: true);
            return;
        }

        var dialog = new OpenFolderDialog { Title = BannerlordCoopLauncher.Core.Localization.Translator.T("Pick the Mount & Blade II Bannerlord folder"), InitialDirectory = GameFolderPick.DialogStartFolder(_settings.GamePath), Multiselect = false };
        if (dialog.ShowDialog(this) != true)
            return;
        var check = GameFolderPick.Use(dialog.FolderName, root =>
        {
            var hadServerRoot = !string.IsNullOrWhiteSpace(_settings.ServerRoot);
            _settings.GamePath = root;
            ApplyGameFound(root);
            SaveSettings();
            // The mod lists read the game's Modules folder, so they load again on every pick.
            ShowModSetups();
            // The default server folder was just made, so the server copies of the saves show now.
            if (!hadServerRoot)
                ReloadSaves();
            NoteLog(GameFolderPick.RememberedText);
        });
        if (check.Result != GameFolderPickResult.Ok)
            Coodsz.Ui.UiLanguage.Message(check.Message, "Find Bannerlord", MessageBoxButton.OK, MessageBoxImage.Warning, this);
    }

    private void Nav_Click(object sender, RoutedEventArgs e) => ShowPage((sender as Button)?.Tag as string ?? StartPage);

    /// <summary>The page the window opens on.</summary>
    private const string StartPage = "Dashboard";

    private string _page = StartPage;

    // Every page of the window by its nav Tag. A new page is one line here plus its nav button in NavButtons.
    private IReadOnlyList<(string Name, FrameworkElement Page)> Pages => new (string, FrameworkElement)[]
    {
        ("Dashboard", DashboardPage),
        // The Saves page keeps the key Server, so every ShowPage("Server") still opens it.
        ("Server", ServerPage),
        ("Realm", RealmPage),
        ("Modules", ModulesPage),
        ("Settings", SettingsPage),
        ("Cheats", CoopCheats),
        ("Console", ConsolePage),
        ("Logs", LogsPage),
        ("Encyclopedia", EncyclopediaPage),
        ("Guides", GuidesPage),
    };

    private Button[] NavButtons => new[] { NavDashboard, NavServer, NavModules, NavSettings, NavCheats, NavConsole, NavLogs, NavEncyclopedia, NavGuides };

    private void ShowPage(string page)
    {
        _page = page;
        foreach (var (name, element) in Pages)
            element.Visibility = name == page ? Visibility.Visible : Visibility.Collapsed;
        // The selected look (Keep.Nav) and the header title follow the page.
        Coodsz.Ui.NavRail.Select(NavButtons, page);
        PageTitle.Text = PageTitleOf(page);
        if (page == "Dashboard")
            RefreshDashboard();
        if (page == "Settings")
        {
            LoadDifficulty(onlyGreyed: true);
            ShowKingdomProtection();
            ShowModSettings();
        }
        if (page == "Guides" && GuidesPage.Content == null)
            GuidesPage.Content = new Coodsz.Ui.TaomGuidesPage(BannerlordCoopLauncher.Core.Keep.GuideLibrary.For(BannerlordCoopLauncher.Core.Keep.GuideSet.RotServer),
                "Short help for hosting Realm of Thrones with this tool. Pick a guide, or search.");
        if (page == "Logs")
            LogBox.ScrollToEnd();
        if (page == "Cheats")
        {
            var names = CurrentPlayerNames();
            CoopCheats.Shown(names);
        }
        if (page == "Console")
            ConsoleHint.Text = CommandPipeReady() ? "Commands go to the running server." : CheatBlockReason();
    }

    // The header title is the page button's word; Realm has no page button, only the footer link and the Dashboard card.
    private static string PageTitleOf(string page) => page switch
    {
        "Modules" => "Mods",
        "Server" => "Saves",
        "Realm" => "Join my server",
        _ => page,
    };

    private ReleaseOffer? _updateOffer;

    // The Keep text colours of the Settings rows the code builds (Keep colours: TextMain, GoldInk, TextDim).
    private static Brush Ivory => Coodsz.Ui.FrameBrushes.Get("Keep.TextMain", "#D5DEE6");
    private static Brush SettingHeader => Coodsz.Ui.FrameBrushes.Get("Keep.GoldInk", "#8EB4D4");
    private static Brush SettingName => Coodsz.Ui.FrameBrushes.Get("Keep.TextMain", "#D5DEE6");
    private static Brush SettingHint => Coodsz.Ui.FrameBrushes.Get("Keep.TextDim", "#8EA0AE");

    private sealed class SealedModuleRow
    {
        public SealedModuleRow(int? number, string name, string note, Brush noteBrush)
        {
            Number = number?.ToString() ?? "";
            Name = name;
            Note = note;
            NoteBrush = noteBrush;
        }

        public string Number { get; }
        public string Name { get; }
        // Note stays English; this is what the row shows.
        public string Note { get; }
        public string NoteText => BannerlordCoopLauncher.Core.Localization.Translator.T(Note);
        public Brush NoteBrush { get; }

        // The list status chip of Coodsz Coop and Friends' mods page (an extra mod only), from the Realm of Thrones list.
        public RotStatusChip? Status { get; init; }
        public string BadgeText => Status == null ? "" : BannerlordCoopLauncher.Core.Localization.Translator.T(Status.Text);
        // Line by line: the tip joins the note, the source line and the counts line.
        public string? BadgeNote => Status == null ? null
            : string.Join("\n", (Status.Text + "\n" + Status.Tip).Split('\n').Select(BannerlordCoopLauncher.Core.Localization.Translator.T));
        public Visibility BadgeVisibility => Status == null ? Visibility.Collapsed : Visibility.Visible;
        public Brush BadgeInk => Status?.Kind switch
        {
            RotModChips.Works => Chip(0x8F, 0xBF, 0x86),
            RotModChips.Broken => Chip(0xE0, 0x7A, 0x6E),
            RotModChips.Risky => Chip(0xE2, 0xC2, 0x7A),
            RotModChips.Reported => Chip(0x8E, 0xB4, 0xD4),
            _ => Chip(0x8E, 0xA0, 0xAE)
        };
        public Brush BadgeBack => Status?.Kind switch
        {
            RotModChips.Works => Chip(0x1A, 0x33, 0x28),
            RotModChips.Broken => Chip(0x3A, 0x24, 0x22),
            RotModChips.Risky => Chip(0x3A, 0x34, 0x20),
            RotModChips.Reported => Chip(0x1C, 0x2A, 0x3A),
            _ => Chip(0x24, 0x30, 0x3A)
        };

        // What players reported and the Report link (MainWindow.ReportChips).
        public FrameworkElement? Reports { get; init; }

        private static SolidColorBrush Chip(byte r, byte g, byte b) => new(Color.FromRgb(r, g, b));

        // This PC's own result (mod-results.json): Passed on your server, Worked for you, and so on.
        public BannerlordCoopLauncher.Core.Modules.ModResultPill? Mine { get; init; }
        public Visibility MineVisibility => Mine == null ? Visibility.Collapsed : Visibility.Visible;
        public Brush MineInk => Mine?.Good == false ? Coodsz.Ui.FrameBrushes.Get("Keep.Red", "#E08B84") : Coodsz.Ui.FrameBrushes.Get("Keep.Success", "#8FCB9B");
        public Brush MineBackground => Mine?.Good == false ? Coodsz.Ui.FrameBrushes.Get("Keep.BadSoft", "#3A2422") : Coodsz.Ui.FrameBrushes.Get("Keep.SuccessSoft", "#1E3326");
        public double MineOpacity => Mine?.Older == true ? 0.5 : 1;

        // An extra mod row of an unlocked setup: the on and off switch and the move and remove buttons.
        public string? ModuleId { get; init; }
        public bool Editable { get; init; }
        public bool IsOn { get; init; }
        public Visibility EditVisibility => Editable ? Visibility.Visible : Visibility.Collapsed;
        // False while the server runs or starts (ModListBlock): the switch and the move and remove buttons stay off.
        public bool Blocked { get; init; }
        public bool CanEdit => Editable && !Blocked;
        public Visibility OrderActionsVisibility => ModuleId == null ? Visibility.Collapsed : Visibility.Visible;
        public bool CanMove => !Blocked && ModuleId != null && !RotLoadOrder.LoadsAfterHarmony(ModuleId);

        // The hover text of the whole row (a Realm of Thrones core row says it always loads in this order).
        public string? Tip { get; init; }
        // The full mod name comes first, since a long name is cut off in its column; the name has no tooltip of its own,
        // which would hide this one.
        public string RowTip => Tip == null ? Name : Name + "\n" + BannerlordCoopLauncher.Core.Localization.Translator.T(Tip);
    }
}

public sealed class SaveChoice
{
    public SaveChoice(string fullPath, string place)
    {
        FullPath = fullPath;
        Place = place;
    }

    public string FullPath { get; }
    public string Name => Path.GetFileNameWithoutExtension(FullPath);
    public string Place { get; }
    // Place stays English for sorting; this is what the dropdown shows.
    public string PlaceText => BannerlordCoopLauncher.Core.Localization.Translator.T(Place);
    public override string ToString() => Name + "   " + Place;
    public int PlaceRank => Place == "Server copy" ? 0 : Place == "Single player copy" ? 1 : 2;
    // The save's campaign day from its header, so two saves with the same name can be told apart. Read once, when the
    // box first shows the row; empty when the header has no day.
    public string DayText => _dayText ??= BannerlordCoopLauncher.Core.Localization.Translator.T(
        TaomCoop.Core.SaveSwitchWarning.BoxDay(BannerlordCoopLauncher.Core.Saves.CampaignSaveInfoReader.Read(FullPath)?.Day));
    private string? _dayText;
}
