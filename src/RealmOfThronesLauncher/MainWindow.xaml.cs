using System.Diagnostics;
using System.IO;
using System.Windows;
using Microsoft.Win32;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using BannerlordCoopLauncher.Core.IO;
using BannerlordCoopLauncher.Core.Launch;
using BannerlordCoopLauncher.Core.Modules;
using BannerlordCoopLauncher.Core.Parsing;
using BannerlordCoopLauncher.Core.Support;
using BannerlordCoopLauncher.Core.Updates;
using BannerlordCoopLauncher.Core.Network;
using BannerlordCoopLauncher.Core.Steam;
using RealmOfThrones.Core;
using RealmOfThrones.Ui;
using TaomCoop.Core;

namespace RealmOfThronesLauncher;

// The Realm of Thrones Client Launcher, a copy of the Burning Empires one on the Coop nightly. Realm of Thrones is put in
// by hand, so there is no Install or repair and no Workshop Coop button; Connect checks it. Coop and the compat module come
// with this launcher, and Connect puts them in (MainWindow.CoopBuild, InstallCompat).
public partial class MainWindow : Window
{
    private readonly string _settingsPath = ToolData.File(RotClientSettings.FileName);
    private RotClientSettings _settings = new();
    private string? _gameRoot;
    private bool _busy;
    private bool _passwordVisible;
    private bool _loadingForm = true;
    private bool _connectReady;
    // Steam can start or close while this window is open, so its status is read again.
    private readonly DispatcherTimer _steamPoll = new() { Interval = TimeSpan.FromSeconds(10) };
    private bool _unblockNoted;
    private readonly HostActionLog _actions = new(() => ToolData.Folder, LogFolderOpen.ClientActionLogName);

    public MainWindow()
    {
        InitializeComponent();
        // A window bigger than the screen opened with its right side, and Install update, off screen.
        var fit = WindowFit.Fit(new WindowSize(Width, Height, MinWidth, MinHeight), SystemParameters.WorkArea.Width, SystemParameters.WorkArea.Height);
        (Width, Height, MinWidth, MinHeight) = (fit.Width, fit.Height, fit.MinWidth, fit.MinHeight);
        LanguageSlot.Content = Coodsz.Ui.UiLanguage.MakeButton();
        WhatsNewSlot.Content = Coodsz.Ui.UiLanguage.MakeWhatsNewButton();
        DonateSlot.Content = Coodsz.Ui.UiLanguage.MakeDonateButton();
        StartFrame();
        ReadOwnServerFromRotData();
        LoadSettings();
        HostBox.Text = _settings.Host;
        PortBox.Text = string.IsNullOrWhiteSpace(_settings.Port) ? RotStack.DirectPort : _settings.Port;
        PasswordHidden.Password = _settings.Password;
        PasswordBox.Text = _settings.Password;
        SteamBox.Text = _settings.SteamName;
        SetJoinMode(_settings.UseSteam);
        UpdateSteamStatus();
        Activated += (_, _) => UpdateSteamStatus();
        _steamPoll.Tick += (_, _) => UpdateSteamStatus();
        _steamPoll.Start();
        GameSearch? search = null;
        _gameRoot = GameFolderPick.ResolveSavedPath(_settings.GamePath, () => (search = BannerlordFinder.FindGameWithReason()).Root);
        ShowGameFolder();
        if (_gameRoot == null)
            Log(search?.Reason ?? GameSearch.GameMissingReason);
        // Our Coop left in Modules by a launcher that crashed comes out first, then the carried one is unpacked.
        TakeOutAfterCrash();
        Application.Current.SessionEnding += App_SessionEnding;
        _ = PrepareCarriedCoopAsync();
        // The setups and the mod list fill even with no game, so a folder picked later only adds the game.
        InitSetups();
        RefreshModules();
        _loadingForm = false;
        UpdateConnectButton();
        // The notes sit on the Made for card (ShowMadeFor); the first join wait goes to the log at launch.
        // The update check watches the release repo (RotRelease); a placeholder repo turns it off.
        if (RotRelease.UpdatesReady)
            _ = WatchForUpdateAsync(RotRelease.Repo, RotRelease.Version);
        else
            Log(RotRelease.UpdatesNotSetUp);
        SetUpCrashReports();
        Loaded += async (_, _) =>
        {
            await ShowOwnServerHintAsync(fill: UseDirect);
            await CheckMemoryAsync();
        };
    }

    private void ShowGameFolder()
    {
        GameText.Text = _gameRoot ?? GameFolderPick.NotFoundText;
        FindBannerlordButton.Visibility = _gameRoot == null ? Visibility.Visible : Visibility.Collapsed;
    }

    // A folder picked after startup: the game dependent work that had no game at startup runs again.
    // Connect does not search again, so this is the only way a late pick reaches it.
    private void ApplyGameFound(string root)
    {
        _gameRoot = root;
        ShowGameFolder();
        if (Process.GetProcessesByName("Bannerlord").Length == 0)
            RestorePrefabsFix();
        RefreshModules();
        UpdateConnectButton();
    }

    private void FindBannerlord_Click(object sender, RoutedEventArgs e)
    {
        // A pick swaps the game folder, so it waits for a download or a join check to finish.
        if (_busy || _joinChecking)
        {
            Log("Wait for the current action to finish.");
            return;
        }

        var dialog = new OpenFolderDialog { Title = BannerlordCoopLauncher.Core.Localization.Translator.T("Pick the Mount & Blade II Bannerlord folder"), InitialDirectory = GameFolderPick.DialogStartFolder(_settings.GamePath), Multiselect = false };
        if (dialog.ShowDialog(this) != true)
            return;
        var check = GameFolderPick.Use(dialog.FolderName, root =>
        {
            _gameRoot = root;
            SaveSettings();
            ApplyGameFound(root);
            Log(GameFolderPick.RememberedText);
        });
        if (check.Result != GameFolderPickResult.Ok)
            Coodsz.Ui.UiLanguage.Message(check.Message, "Find Bannerlord", MessageBoxButton.OK, MessageBoxImage.Warning, this);
    }

    private ReleaseOffer? _updateOffer;

    private void ShowUpdateNotice(ReleaseOffer offer)
    {
        _updateOffer = offer;
        UpdateNotice.Text = offer.Message;
        UpdateNoticeBar.Visibility = Visibility.Visible;
        Log(offer.Message);
    }

    private async void InstallUpdate_Click(object sender, RoutedEventArgs e)
    {
        if (_updateOffer == null)
            return;
        InstallUpdateButton.IsEnabled = false;
        InstallUpdateButton.Opacity = 1;
        InstallUpdateButton.Content = "Installing...";
        ShowUpdateProgress("Installing the update. This window stays open until the download finishes.", null);
        await Task.Yield();
        try
        {
            var exe = Environment.ProcessPath;
            if (string.IsNullOrWhiteSpace(exe))
                throw new InvalidOperationException("This copy cannot update itself.");
            var progress = new Progress<UpdateStep>(step => ShowUpdateProgress(step.Message, step.Fraction));
            await ToolUpdate.InstallAsync(_updateOffer, AppContext.BaseDirectory, exe, Environment.ProcessId, progress, CancellationToken.None);
            Application.Current.Shutdown();
        }
        catch (Exception ex)
        {
            UpdateNotice.Text = ex.Message;
            UpdateProgress.Visibility = Visibility.Collapsed;
            InstallUpdateButton.Content = "Install update";
            InstallUpdateButton.ClearValue(OpacityProperty);
            InstallUpdateButton.IsEnabled = true;
        }
    }

    private void ShowUpdateProgress(string message, double? fraction)
    {
        UpdateNotice.Text = message;
        UpdateProgress.Visibility = Visibility.Visible;
        if (fraction is double value)
        {
            UpdateProgress.IsIndeterminate = false;
            UpdateProgress.Value = value;
        }
        else
        {
            UpdateProgress.IsIndeterminate = true;
        }
    }

    private async Task WatchForUpdateAsync(string repo, string version)
    {
        try
        {
            var offer = await ReleaseNotice.CheckOfferAsync(repo, version);
            if (offer != null)
                Dispatcher.Invoke(() => ShowUpdateNotice(offer));
        }
        catch
        {
        }
    }

    private async void Direct_Click(object sender, RoutedEventArgs e)
    {
        SetJoinMode(false);
        await ShowOwnServerHintAsync(fill: true);
    }

    private void Steam_Click(object sender, RoutedEventArgs e)
    {
        SetJoinMode(true);
        UpdateSteamStatus();
    }

    private void CheckMods_Click(object sender, RoutedEventArgs e)
    {
        RefreshModules();
        var problems = ModuleProblems();
        var setup = _settings.ModSetups.Current();
        if (problems.Count > 0)
        {
            ShowJoin(problems[0]);
            foreach (var line in problems.Skip(1))
                Log(line);
        }
        else
        {
            var message = setup.HasEnabledExtras
                ? "Every module on " + setup.Name + " is ready. Check mods looks in the game's Modules folder and the Steam Workshop. It does not download anything."
                : "The Realm of Thrones module list is ready. Check mods looks in the game's Modules folder and the Steam Workshop. It does not download anything.";
            JoinStatus.Text = message;
            Log(message);
        }

        if (UIExtenderExPrefabsFix.InLaunch(RotLoadOrder.LaunchModules(setup, joinPassword: false)))
        {
            foreach (var line in UIExtenderExPrefabsFix.HealthProblems(ModRoots()))
                Log(line);
        }

        if (problems.Count == 0)
            ModChecksPass(setup);
    }

    private void ShowPassword_Click(object sender, RoutedEventArgs e)
    {
        _passwordVisible = !_passwordVisible;
        if (_passwordVisible)
            PasswordBox.Text = PasswordHidden.Password;
        else
            PasswordHidden.Password = PasswordBox.Text;
        PasswordBox.Visibility = _passwordVisible ? Visibility.Visible : Visibility.Collapsed;
        PasswordHidden.Visibility = _passwordVisible ? Visibility.Collapsed : Visibility.Visible;
        ShowPasswordButton.Content = _passwordVisible ? "Hide" : "Show";
    }

    private void Unblock_Click(object sender, RoutedEventArgs e)
    {
        if (_gameRoot == null)
        {
            Log("Bannerlord was not found.");
            return;
        }

        var modules = Path.Combine(_gameRoot, "Modules");
        if (!Directory.Exists(modules))
        {
            Log("The Modules folder was not found.");
            return;
        }

        try
        {
            var cleared = DownloadMark.Clear(modules);
            var detail = cleared == 0
                ? "No files in Modules were blocked. Connect can start the game."
                : "Cleared the download block on " + cleared + " files in Modules. Connect can start the game.";
            _actions.Note("Unblock DLLs", detail);
            Log(detail);
            _settings.DllsUnblocked = true;
            SaveSettings();
        }
        catch (Exception ex)
        {
            Log(ex.Message);
        }
    }

    // Windows marks downloaded files, and the game cannot load a marked DLL. The launcher clears
    // that mark on the Modules folder before every launch, so nobody has to remember Unblock DLLs.
    private async Task UnblockBeforeLaunchAsync()
    {
        if (_gameRoot == null)
            return;
        var modules = Path.Combine(_gameRoot, "Modules");
        if (!Directory.Exists(modules))
            return;
        try
        {
            var cleared = await Task.Run(() => DownloadMark.Clear(modules));
            if (cleared > 0)
            {
                var detail = "Cleared the Windows download block on " + cleared + " files in Modules before the launch.";
                _actions.Note("Unblock DLLs", detail);
                Log(detail);
            }

            if (!_settings.DllsUnblocked)
            {
                _settings.DllsUnblocked = true;
                SaveSettings();
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            Log("Could not clear the Windows download block in Modules (" + ex.Message + "). Open Settings and click Unblock DLLs if the game cannot load a DLL.");
        }
    }

    private void NoteBlockedDll(string? text)
    {
        if (_unblockNoted || !BlockedDllNotice.Matches(text))
            return;
        _unblockNoted = true;
        Log("Windows blocked a DLL. The launcher clears that block before every launch, so click Connect again. If it comes back, open Settings and click Unblock DLLs.");
    }

    // The More buttons open their menu on a normal click, under the button.
    private void OpenMenu_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { ContextMenu: { } menu } button)
            return;
        menu.PlacementTarget = button;
        menu.Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom;
        menu.IsOpen = true;
    }

    private void ClearShaders_Click(object sender, RoutedEventArgs e)
    {
        var programData = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "Mount and Blade II Bannerlord");
        try
        {
            var deleted = ShaderCache.Clear(programData, Process.GetProcessesByName("Bannerlord").Length > 0);
            Log(deleted == 0
                ? "There was no shader cache to delete."
                : "Deleted the shader cache. The game builds it again the next time it starts. Saves were not touched.");
        }
        catch (Exception ex)
        {
            Log(ex.Message);
        }
    }

    private void ModuleFolder_Click(object sender, RoutedEventArgs e)
    {
        if (_gameRoot == null)
        {
            Log("Bannerlord was not found.");
            return;
        }

        var modules = Path.Combine(_gameRoot, "Modules");
        if (!Directory.Exists(modules))
        {
            Log("The Modules folder was not found.");
            return;
        }

        if (OpenFolder(modules))
            Log("Opened the Modules folder.");
    }

    // A folder Windows can't open is a log line, not a throw that would close the tool.
    private bool OpenFolder(string folder)
    {
        if (ShellOpen.TryOpen(folder, out var problem))
            return true;
        Log("Could not open " + folder + ". " + problem);
        return false;
    }

    private void OpenClientLogs_Click(object sender, RoutedEventArgs e)
    {
        var clientLog = _gameRoot == null
            ? null
            : Path.Combine(_gameRoot, "bin", "Win64_Shipping_Client", "Coop_client.log");
        var actionLog = _actions.FilePath;
        _actions.Note("Open client logs", "Opened Coop_client.log and client-action.log.");
        if ((clientLog != null && File.Exists(clientLog)) || (actionLog != null && File.Exists(actionLog)))
        {
            LogFolderOpen.Select(new[] { clientLog, actionLog });
            Log("Opened Coop_client.log and client-action.log.");
        }
        else if (_gameRoot != null)
        {
            var bin = Path.Combine(_gameRoot, "bin", "Win64_Shipping_Client");
            Directory.CreateDirectory(bin);
            if (OpenFolder(bin))
                Log("Opened the game log folder.");
        }
        else
        {
            var reports = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                "Mount and Blade II Bannerlord",
                "Coop Crash Reports");
            Directory.CreateDirectory(reports);
            if (OpenFolder(reports))
                Log("Bannerlord was not found. Opened Coop Crash Reports.");
        }
    }

    private async void Probe_Click(object sender, RoutedEventArgs e)
    {
        var address = HostBox.Text.Trim();
        if (!ClientJoin.IsDirectAddress(address))
        {
            ShowJoin(SteamPanel.Visibility == Visibility.Visible
                ? "A Steam name cannot be checked this way. Fill the address and UDP port, then click Check server."
                : ClientJoin.IpRequired);
            return;
        }

        if (!int.TryParse(PortBox.Text.Trim(), out var port) || port is < 1 or > 65535)
        {
            ShowJoin("Enter the UDP port the host set.");
            return;
        }

        var probe = await UdpServerProbe.ProbeAsync(address, port, UdpServerProbe.DefaultTimeout, CancellationToken.None);
        ShowJoin(ClientJoin.DescribeProbe(probe, address), probe.Reachability != ServerReachability.Online);
    }

    // Harmony, ButterLib and UIExtenderEx come from the Steam Workshop. Missing ones are subscribed and
    // downloaded through Steam; when Steam can't do it by itself their Workshop pages open, and with Steam
    // closed it only says to open Steam (SubscribeWorkshopAsync). MCM comes from Nexus Mods into the game's
    // Modules folder (its official Workshop item was removed), so it is never subscribed. True when all four
    // are on this PC.
    private async Task<bool> GetLibrariesAsync()
    {
        ScanWorkshopModules();
        var missing = MissingLibraries();
        if (missing.Count == 0)
            return true;
        var names = new Dictionary<ulong, string>();
        foreach (var mod in missing.Where(mod => !RotJoinCheck.FromNexus(mod)))
        {
            if (ulong.TryParse(mod.WorkshopId, out var id))
                names.TryAdd(id, mod.Name);
        }

        if (names.Count > 0)
        {
            _busy = true;
            try
            {
                await SubscribeWorkshopAsync(names);
            }
            finally
            {
                _busy = false;
            }
        }

        ScanWorkshopModules();
        var left = MissingLibraries();
        if (left.Any(RotJoinCheck.FromNexus))
            Log(RotJoinCheck.McmMissing);
        var downloading = left.Where(mod => !RotJoinCheck.FromNexus(mod)).Select(mod => mod.Name).ToList();
        if (left.Count == 0)
            Log("Harmony, ButterLib, UIExtenderEx and MCM are on this PC.");
        // With Steam closed SubscribeWorkshopAsync already said to open Steam.
        else if (downloading.Count > 0 && RotSteamPage.SteamRunning())
            Log("Steam is still finishing " + string.Join(", ", downloading) + ". Wait for the download in Steam, then try again.");
        return left.Count == 0;
    }

    // Any Workshop copy of MCM counts here on purpose: the client game loads it. ParkRetiredMcm turns it off when
    // the game's Modules folder has MCM too.
    private List<RotLibrary> MissingLibraries() =>
        _gameRoot == null ? new List<RotLibrary>() : RotStack.Libraries.Where(mod => FindModuleXml(_gameRoot, mod.ModuleId) == null).ToList();

    // Realm of Thrones Coop Compat comes with this launcher (ClientAddOns) and goes into the game's Modules folder on
    // every Connect, so the player always has the version the host's tools carry. A launcher built without it leaves the
    // Modules folder as it is, and the core check names a missing module. False when the copy failed: the core check reads
    // the carried copy, so it would pass while the game loads an old or half written module.
    private bool InstallCompat()
    {
        if (_gameRoot == null || CarriedCompatXml() == null)
            return true;
        if (ClientAddOns.TryInstall(_gameRoot, RotStack.CompatModuleId, RotStack.CompatName, out var error, Log))
            return true;
        ShowJoin("Could not put " + RotStack.CompatName + " in the Modules folder, so the game was not started. Close the game if it is open, click Fix antivirus on the Logs and help page, then click Connect again.");
        if (!string.IsNullOrWhiteSpace(error))
            Log(error);
        return false;
    }

    // The SubModule.xml of the compat module this launcher carries, or null when it was built without it.
    private static string? CarriedCompatXml()
    {
        var xml = Path.Combine(ClientAddOns.Payload(RotStack.CompatModuleId), "SubModule.xml");
        return File.Exists(xml) ? xml : null;
    }

    // A core module as Connect loads it. For the compat module and Coop that is the launcher's own copy when it carries one,
    // since InstallCompat and PutInCoopAsync put it in before the game starts, so a first Connect is not held back by a
    // module not put in yet.
    private string? CoreModuleXml(string id) =>
        _gameRoot == null ? null
        : id.Equals(RotStack.CompatModuleId, StringComparison.OrdinalIgnoreCase) && CarriedCompatXml() is { } carried ? carried
        : id.Equals(RotStack.CoopModuleId, StringComparison.OrdinalIgnoreCase) && _carriedCoopXml is { } coop ? coop
        : FindModuleXml(_gameRoot, id);

    private async void Connect_Click(object sender, RoutedEventArgs e) => await ConnectAsync();

    // A launcher built without the carried Coop (PutInCoopAsync): the Burning Empires and TAOM launchers move a Coop nightly
    // out of Modules as Coop-kept-0.1.8 (RotCoopRestore).
    // When Modules has no Coop of the pinned build and a kept folder has it, it is moved back, the other Coop parked
    // first; one line says what was done. False (with the line under Connect) while the game runs or a move failed.
    private bool RestoreKeptCoop()
    {
        if (_gameRoot == null)
            return true;
        try
        {
            var plan = RotCoopRestore.Decide(RotCoopRestore.InModules(_gameRoot), RotCoopRestore.Kept(_gameRoot), ClientLaunchGuard.IsGameRunning());
            if (RotCoopRestore.Apply(_gameRoot, plan) is not { } line)
                return true;
            if (plan.Action == RotRestoreAction.GameRunning)
            {
                ShowJoin(line);
                return false;
            }
            Log(line);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            ShowJoin("The launcher could not move the Coop folders back. " + ex.Message + " Close anything that uses the game's Modules folder, then click Connect again.");
            return false;
        }
    }

    private async Task ConnectAsync()
    {
        if (_busy)
        {
            Log("Wait for the current action to finish.");
            return;
        }
        // Not searched again here: the game found at startup also sets up the mods.
        if (_gameRoot == null)
        {
            Log("Bannerlord was not found.");
            return;
        }
        // Before the checks: the RoT build of Coop this launcher carries goes into Modules (MainWindow.CoopBuild).
        if (!await PutInCoopAsync())
            return;
        // Connect starts the saved setup; changes on screen are kept or dropped first.
        if (!ConfirmModChanges())
            return;

        // The server needs the library mods and the setup's extra mods. When some are not on this PC,
        // one prompt gets them and connects; with nothing missing Connect goes on as before.
        bool ready;
        try
        {
            ready = await GetServerModsFirstAsync();
        }
        catch (Exception ex)
        {
            // Steam links and Workshop downloads can throw; Connect stops here instead of closing the tool.
            Log("Getting the mods the server needs stopped, so Connect did not go on. " + ex.Message);
            return;
        }
        if (!ready)
            return;

        // Then the core on this PC (RotJoinCheck.CoreProblems), then the host's list against this PC (HostListMatches).
        if (!InstallCompat())
            return;
        RefreshModules();
        var problems = ModuleProblems();
        if (problems.Count > 0)
        {
            ShowJoin(problems[0]);
            foreach (var line in problems.Skip(1))
                Log(line);
            return;
        }

        if (!HostListMatches())
            return;

        if (!ModChecksPass(LaunchSetup))
            return;

        SaveSettings();
        var steam = SteamPanel.Visibility == Visibility.Visible;
        _actions.Note("Connect", steam ? "Steam lobby join." : "Direct join.");
        if (steam && !SteamHostName.IsValid(SteamBox.Text))
        {
            ShowJoin(string.IsNullOrWhiteSpace(SteamBox.Text)
                ? "Enter the host's Steam profile name, the name friends see in Steam (not the login name)."
                : "The host's Steam profile name can not contain a double quote.");
            return;
        }

        if (!steam && !ClientJoin.IsDirectAddress(HostBox.Text))
        {
            ShowJoin(ClientJoin.IpRequired);
            return;
        }

        if (!steam && (!int.TryParse(PortBox.Text.Trim(), out var port) || port is < 1 or > 65535))
        {
            ShowJoin("Enter the UDP port the host set.");
            return;
        }

        // The server's mods this PC knows of against this setup: a ping does not carry them, so a missing one
        // used to start the game into Coop's refusal (ServerModsLoadedHereAsync).
        var joinKey = JoinMemory.Key(steam, SteamBox.Text, HostBox.Text, PortBox.Text);
        if (!await ServerModsLoadedHereAsync(steam, joinKey))
            return;

        // Direct: a wrong address used to start the game into a connect screen with no end, so the
        // server has to answer first. An answer goes straight on with no extra click.
        if (!steam && !await CheckDirectJoinAsync(HostBox.Text.Trim(), int.Parse(PortBox.Text.Trim())))
            return;

        try
        {
            if (!await Coodsz.Ui.GameAlreadyOpen.ClearAsync(this, ShowJoin))
                return;
            foreach (var note in RotServerNotes.ModNotes(LaunchSetup))
                Log(note);
            ParkRetiredMcm();
            WarnBigBattles(LaunchSetup);
            await CheckMemoryAsync();
            await UnblockBeforeLaunchAsync();
            RaiseLowTerrain();
            // Map shaders the game could not remove crash it on the map (AD1259 CR-TXY4V).
            await TerrainShaderCache.ClearAsync(names => Coodsz.Ui.UiLanguage.Message(TerrainShaderCache.Ask(names), TerrainShaderCache.AskTitle,
                MessageBoxButton.YesNo, MessageBoxImage.Warning, this) == MessageBoxResult.Yes, Log);
            ulong? lobbyId = null;
            if (steam)
            {
                ClearStoredAddress();
                var steamName = SteamBox.Text.Trim();
                // Keeps looking for up to 30 seconds; a second Connect click stops it and starts over.
                _lobbySearch?.Cancel();
                _lobbySearch = new CancellationTokenSource();
                lobbyId = await SteamLobbyFinder.FindWithRetryAsync(_gameRoot, steamName, Log, _lobbySearch.Token);
                Log("Found that Steam lobby. The game will join it. Lobby " + lobbyId + ".");
            }
            else
            {
                WriteDirectAddress(HostBox.Text.Trim());
            }
            // The Realm of Thrones core in its sealed order with the setup's extra mods before the compat module
            // (RotLoadOrder.LaunchModules). The locked setup gives exactly the core.
            var setup = LaunchSetup;
            var modules = RotLoadOrder.LaunchModules(setup, InstallJoinPassword());
            var lastJoin = JoinMemory.Problem(_settings.LastJoins.GetValueOrDefault(joinKey), modules);
            if (lastJoin != null)
                ShowJoin(lastJoin);
            if (setup.HasEnabledExtras)
                Log(setup.Name + " adds " + string.Join(", ", setup.EnabledExtras.Select(mod => mod.Label)) + ". The host has to load the same mods, even interface mods, or the server turns the join down.");
            var arguments = TaomLaunch.Arguments(connect: true, steam, CurrentPassword(), lobbyId, steam ? null : HostBox.Text.Trim() + ":" + PortBox.Text.Trim(), modules);
            var start = new ProcessStartInfo
            {
                FileName = GameRoot.ExecutablePath(_gameRoot),
                WorkingDirectory = GameRoot.WorkingDirectory(_gameRoot),
                Arguments = arguments,
                UseShellExecute = false
            };
            start.Environment[CoopPasswordPatch.VariableName] = CurrentPassword();
            if (!steam && SteamRegistry.GetActiveProcessId() <= 0)
                Log("Steam is not running. Direct connect can still start the game.");
            if (NonSteamCopy.Note(_gameRoot) is { } nonSteam)
                Log(nonSteam);
            // A Common.dll or 0Harmony.dll another mod left in the game's bin folder: a warning, the game still starts.
            foreach (var leftover in GameBinLeftovers.Notes(_gameRoot))
                Log(leftover);
            // The Activity box can be seen on a stream or screenshot, so the join password is cleaned out.
            Log(TaomLaunch.Shown(arguments, CurrentPassword()));
            var started = StartWithPrefabsFix(setup, start);
            Log("Bannerlord started.");
            Log(RotUiText.FirstJoinWait);
            RestoreWhenClosed(started);
            // The header pill says Joining until the join result, then In game or Refused until the game closes.
            SetJoinState(ClientNextStep.Joining);
            try
            {
                await WatchJoinAsync(started, _gameRoot, () => RememberJoin(joinKey, modules), log => RememberServerNeeds(joinKey, modules, log));
            }
            finally
            {
                // After the end state, so a game that closed fast can't clear the pill before In game or Refused is set.
                ClearJoinStateWhenClosed(started);
            }
        }
        catch (OperationCanceledException ex) when (ex.CancellationToken.IsCancellationRequested)
        {
            Log("Stopped looking for the Steam lobby.");
        }
        catch (Exception ex)
        {
            Log(ex.Message);
        }
    }

    private CancellationTokenSource? _lobbySearch;

    // The pinned Coop's Direct join (ServerLaunchArguments) builds its config from the address alone, so
    // a password server answers "incorrect password". This module fills it from the launcher without
    // editing Coop.Core.dll. Its id starts with DedicatedServer., which Coop's join check skips, so the
    // server does not load it.
    private bool InstallJoinPassword()
    {
        if (_gameRoot == null)
            return false;
        try
        {
            // A DLL the last Connect put there and is gone now was most likely taken by antivirus.
            if (ClientAddOns.Install(_gameRoot, ClientAddOns.JoinPasswordId, ClientAddOns.JoinPasswordName) is { } taken)
                Log(taken);
            DownloadMark.Clear(Path.Combine(_gameRoot, "Modules", ClientAddOns.JoinPasswordId));
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            Log("Could not add " + ClientAddOns.JoinPasswordName + " (" + ex.Message + "). A password server may show \"incorrect password\" once before it lets you in.");
            return false;
        }
    }

    private async Task WatchJoinAsync(Process started, string gameRoot, Action? joined = null, Action<string?>? refused = null)
    {
        try
        {
            var joinLog = new ClientJoinLog(gameRoot, started.Id);
            var deadline = DateTime.UtcNow.AddMinutes(3);
            while (DateTime.UtcNow < deadline)
            {
                var exited = !ClientJoinLog.StillRunning(started);
                var text = joinLog.Read();
                if (JoinResultExplain.Succeeded(text))
                {
                    Log("Connected. The game is joining the Realm of Thrones server.");
                    joined?.Invoke();
                    SetJoinState(ClientNextStep.InGame);
                    return;
                }

                NoteBlockedDll(text);

                var lines = JoinResultExplain.Explain(text, warSailsSetup: true);
                if (lines.Count > 0)
                {
                    foreach (var line in lines)
                        Log(line);
                    refused?.Invoke(text);
                    SetJoinState(ClientNextStep.Refused);
                    return;
                }

                if (exited)
                {
                    Log("The game closed before it joined the server.");
                    await ExplainEarlyExitAsync(started);
                    return;
                }

                await Task.Delay(2000);
            }

            // Three minutes without a result: the pill stays Joining until the game closes, or says Game open
            // when Coop wrote nothing to either client log.
            if (joinLog.TimedOutState(ClientJoinLog.StillRunning(started)) == ClientNextStep.GameOpen)
            {
                Log(ClientJoinLog.EmptyLogNote);
                SetJoinState(ClientNextStep.GameOpen);
            }
        }
        catch (Exception ex)
        {
            Log("Could not read the join result. " + ex.Message);
        }
    }

    private void RefreshModules()
    {
        RestoreRetiredMcm();
        ScanInstalledExtras();
        ShowModuleList();
        // The Next step card reads the modules again (ShowNextStep).
        _attention = null;
        NoteWorkshopTwins();
        NoteRetiredMcm();
        if (!_loadingForm)
            UpdateConnectButton();
    }

    // One locked core row, the same look as the Burning Empires launcher's.
    private Border CoreRow(int number, string id)
    {
        var xml = CoreModuleXml(id);
        string version;
        var found = xml != null;
        var rightVersion = true;
        var library = RotStack.Libraries.FirstOrDefault(mod => mod.ModuleId.Equals(id, StringComparison.OrdinalIgnoreCase));
        if (xml == null)
        {
            version = library == null ? "missing"
                : RotJoinCheck.FromNexus(library) ? "missing. Get it from Nexus Mods and put it in the game's Modules folder."
                : "missing. Connect gets it from the Steam Workshop.";
        }
        else
        {
            version = ModuleIdentityReader.Read(xml).Version;
            // The same rule as the Connect check: a wrong Realm of Thrones or compat version, or an MCM that is too old.
            rightVersion = RotJoinCheck.ModuleProblem(id, version) == null;
        }

        var row = new DockPanel { Margin = new Thickness(0, 0, 0, 0), LastChildFill = true };
        // The Ready pill in the same column as the extra rows', with the chip column after it: what players reported
        // and the Report link (CoreReportChips).
        var chips = CoreReportChips(id, RotJoinCheck.Name(id), found ? version : "") ?? BadgeColumn();
        DockPanel.SetDock(chips, Dock.Right);
        var (mark, kind) = !found ? (ModChips.Missing, ModChips.Missing) : rightVersion ? (ModChips.Ready, ModChips.Ready) : (ModChips.WrongVersion, ModChips.WrongVersion);
        var status = StatusPill(mark, kind);
        DockPanel.SetDock(status, Dock.Right);
        // Always on, the Mods page word for a module the tool keeps on and in its place (ModChips.Role).
        var locked = new Coodsz.Ui.Chip
        {
            Text = ModChips.AlwaysOn,
            Kind = Coodsz.Ui.ChipKind.Mute,
            Margin = new Thickness(0, 1, 6, 1),
            Tip = RotSetupBook.CoreRowTip
        };
        row.Children.Add(chips);
        row.Children.Add(status);
        // Always on goes first in the chip column, and the empty button and switch slots keep this core row in line
        // with the extra mod rows (ModRowLayout).
        chips.Children.Insert(0, locked);
        Coodsz.Ui.MoveButtons? move = null;
        if (!_editing.Locked && id == RotStack.CoreModuleId)
        {
            move = new Coodsz.Ui.MoveButtons { ItemName = "Realm of Thrones" };
            move.MoveUp += (_, _) => MoveExtra(id, -1);
            move.MoveDown += (_, _) => MoveExtra(id, 1);
        }
        var actions = Coodsz.Ui.ModRowLayout.Slot(Coodsz.Ui.ModRowLayout.ClientActionColumn, move);
        DockPanel.SetDock(actions, Dock.Right);
        row.Children.Add(actions);
        var index = new TextBlock
        {
            Text = number.ToString(),
            Width = 28,
            Foreground = Coodsz.Ui.FrameBrushes.Get("Keep.Amber", "#D2A36A"),
            FontSize = 16,
            VerticalAlignment = VerticalAlignment.Center
        };
        DockPanel.SetDock(index, Dock.Left);
        row.Children.Add(index);
        var switchSlot = Coodsz.Ui.ModRowLayout.Slot(Coodsz.Ui.ModRowLayout.SwitchColumn);
        DockPanel.SetDock(switchSlot, Dock.Left);
        row.Children.Add(switchSlot);
        row.Children.Add(new StackPanel
        {
            Margin = new Thickness(8, 0, 8, 0),
            Children =
            {
                new TextBlock { Text = RotJoinCheck.Name(id), FontSize = 16, FontWeight = FontWeights.SemiBold, Foreground = Coodsz.Ui.FrameBrushes.Get("Keep.TextMain", "#E8E6E1") },
                new TextBlock { Text = version, FontSize = 14, Foreground = Coodsz.Ui.FrameBrushes.Get("Keep.Teal", "#7DAAA4") }
            }
        });
        var border = new Border
        {
            BorderBrush = Coodsz.Ui.FrameBrushes.Get("Keep.BorderDim", "#3A3E44"),
            BorderThickness = new Thickness(0, 0, 0, 1),
            Padding = new Thickness(0, 8, 0, 8),
            Background = Brushes.Transparent,
            Child = row
        };
        Coodsz.Ui.ModRowLayout.FixHeight(border);
        return border;
    }

    // The version of a core module on this PC, "" when its SubModule.xml names none, or null when it is not there.
    private string? CoreVersionOrNull(string id)
    {
        var xml = CoreModuleXml(id);
        return xml == null ? null : ModuleIdentityReader.Read(xml).Version;
    }

    // The version of a core module on this PC, or "".
    private string CoreVersion(string id) => CoreVersionOrNull(id) ?? "";

    // The exact Coop build first (RotJoinCheck.CoopBuildProblem), then the Realm of Thrones core (RotJoinCheck.CoreProblems),
    // then the extra mods of the saved setup Connect starts that are not on this PC. One plain line each.
    private List<string> ModuleProblems()
    {
        var problems = CoreProblems();
        if (_gameRoot == null)
            return problems;
        if (CoreModuleXml(RotStack.MapModuleId) is { } mapXml
            && RotJoinCheck.PlayerMapProblem(Path.GetDirectoryName(mapXml)!) is { } mapProblem)
            problems.Add(mapProblem);
        // A missing Coop is a CoreProblems line; only a Coop that is there has a build to compare. The same build as the
        // host's list goes to the log.
        if (CoreModuleXml(RotStack.CoopModuleId) is { } coop)
        {
            var thisPc = CoopBuildId.FromModule(Path.GetDirectoryName(coop));
            var host = ReadHostList();
            if (RotJoinCheck.CoopBuildProblem(thisPc, host, CarriesCoop()) is { } build)
                problems.Insert(0, build);
            else if (RotJoinCheck.SameCoopBuild(thisPc, host) is { } same)
                Log(same);
        }
        var missing = RotSharedList.Missing(LaunchSetup, _installedExtras.Keys).Select(mod => mod.Label).ToList();
        if (missing.Count > 0)
            problems.Add("Install the missing modules first: " + string.Join(", ", missing));
        return problems;
    }

    // A missing Workshop library mod does not keep Connect off, because Connect gets it from the Steam Workshop
    // (GetServerModsFirstAsync); withWorkshopLibraries leaves it out.
    private List<string> CoreProblems(bool withWorkshopLibraries = true) =>
        _gameRoot == null ? new List<string>() : RotJoinCheck.CoreProblems(CoreVersionOrNull, withWorkshopLibraries, CarriesCoop()).ToList();

    // Every Steam Workshop item's module by id. The library mods come from the Workshop, and looking only in one
    // item showed Harmony, ButterLib, UIExtenderEx and MCM as Missing. Read again with the mods on this PC (ScanInstalledExtras).
    private Dictionary<string, string> _workshopXml = new(StringComparer.OrdinalIgnoreCase);

    private void ScanWorkshopModules()
    {
        var found = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var library in Workshops())
        {
            try
            {
                if (!Directory.Exists(library.ContentDirectory))
                    continue;
                foreach (var item in Directory.EnumerateDirectories(library.ContentDirectory))
                {
                    // A module sits at the top of its item, or one folder down.
                    var xml = Path.Combine(item, "SubModule.xml");
                    var candidates = File.Exists(xml)
                        ? new[] { xml }
                        : Directory.EnumerateDirectories(item).Select(folder => Path.Combine(folder, "SubModule.xml")).Where(File.Exists).ToArray();
                    foreach (var candidate in candidates)
                    {
                        var id = ModuleIdentityReader.Read(candidate).Id;
                        if (id.Length > 0)
                            found.TryAdd(id, candidate);
                    }
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
            }
        }

        _workshopXml = found;
    }

    // The game's Modules folder first (by id or folder name, so ROT_Map in its ROT-Map folder is found), then every Workshop item.
    private string? FindModuleXml(string gameRoot, string moduleId)
    {
        var modules = Path.Combine(gameRoot, "Modules");
        if (Directory.Exists(modules))
        {
            foreach (var directory in Directory.EnumerateDirectories(modules))
            {
                var xml = Path.Combine(directory, "SubModule.xml");
                if (!File.Exists(xml))
                    continue;
                var identity = ModuleIdentityReader.Read(xml);
                if (identity.Id.Equals(moduleId, StringComparison.OrdinalIgnoreCase)
                    || Path.GetFileName(directory).Equals(moduleId, StringComparison.OrdinalIgnoreCase))
                    return xml;
            }
        }

        return _workshopXml.TryGetValue(moduleId, out var workshopXml) && File.Exists(workshopXml) ? workshopXml : null;
    }

    private void SetJoinMode(bool steam)
    {
        DirectPanel.Visibility = steam ? Visibility.Collapsed : Visibility.Visible;
        SteamPanel.Visibility = steam ? Visibility.Visible : Visibility.Collapsed;
        _settings.UseSteam = steam;
        PaintMode(DirectButton, !steam);
        PaintMode(SteamButton, steam);
        if (steam)
            ShowSteamHint();
        else if (Coodsz.Ui.UiLanguage.English(JoinStatus) == ClientJoin.SteamNameHint)
            JoinStatus.Text = "";
        if (!_loadingForm)
            UpdateConnectButton();
        UpdateOwnServerNote();
    }

    private void JoinField_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_loadingForm)
            return;
        UpdateConnectButton();
        UpdateOwnServerNote();
    }

    private void UpdateConnectButton()
    {
        var steam = SteamPanel.Visibility == Visibility.Visible;
        var joinReady = steam
            ? SteamHostName.IsValid(SteamBox.Text)
            : ClientJoin.IsDirectAddress(HostBox.Text)
                && int.TryParse(PortBox.Text.Trim(), out var port)
                && port is >= 1 and <= 65535;
        // Missing extra mods and Workshop library mods leave Connect on: Connect offers to get them first.
        var enabled = _gameRoot != null && CoreProblems(withWorkshopLibraries: false).Count == 0 && joinReady && !_joinChecking && !_coopMoving;
        ShowNextStep(enabled);
        if (_connectReady == enabled && ConnectButton.IsEnabled == enabled)
            return;

        _connectReady = enabled;
        ConnectButton.IsEnabled = enabled;
        if (enabled)
        {
            ConnectButton.Opacity = 1;
            ConnectButton.Background = Coodsz.Ui.FrameBrushes.Get("Keep.AccentSoft", "#1F6B3A");
            ConnectButton.BorderBrush = Coodsz.Ui.FrameBrushes.Get("Keep.Success", "#8FCB9B");
            ConnectButton.Foreground = Coodsz.Ui.FrameBrushes.Get("Keep.OnAccent", "#F4FBF6");
        }
        else
        {
            ConnectButton.Opacity = 0.55;
            ConnectButton.ClearValue(Control.BackgroundProperty);
            ConnectButton.ClearValue(Control.BorderBrushProperty);
            ConnectButton.ClearValue(Control.ForegroundProperty);
        }
    }

    private static void PaintMode(Button button, bool selected)
    {
        // The Keep look: the picked way has the gold edge of a selected choice, the other the plain dark button.
        button.Background = (selected ? Coodsz.Ui.FrameBrushes.Get("Keep.BgRaised", "#1B2833") : Coodsz.Ui.FrameBrushes.Get("Keep.BgPanel", "#151D24"));
        button.BorderBrush = (selected ? Coodsz.Ui.FrameBrushes.Get("Keep.GoldInk", "#E2C27A") : Coodsz.Ui.FrameBrushes.Get("Keep.BorderDim", "#3A4C5C"));
    }

    private string CurrentPassword() => _passwordVisible ? PasswordBox.Text : PasswordHidden.Password;

    private void UpdateSteamStatus()
    {
        var path = SteamRegistry.TryGetSteamPath();
        var running = SteamRegistry.GetActiveProcessId() > 0;
        SteamDot.SetResourceReference(System.Windows.Shapes.Shape.FillProperty, running ? "Keep.Success" : "Keep.Amber");
        SteamStatusText.Text = path == null
            ? "Steam is not installed for this Windows user."
            : running ? "Steam is running." : "Steam is installed, and it is not running.";
    }

    private void ShowJoin(string message, bool problem = true)
    {
        JoinStatus.Text = message;
        JoinStatus.Foreground = (problem ? Coodsz.Ui.FrameBrushes.Get("Keep.Amber", "#D2A36A") : Coodsz.Ui.FrameBrushes.Get("Keep.Success", "#8FBF86"));
        Log(message);
    }

    private void ShowSteamHint()
    {
        JoinStatus.Text = ClientJoin.SteamNameHint;
        JoinStatus.SetResourceReference(TextBlock.ForegroundProperty, "Keep.Accent");
    }

    private void RaiseLowTerrain()
    {
        var documents = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Mount and Blade II Bannerlord");
        ClientLaunchGuard.UnlockEngineConfig(documents);
        var path = Path.Combine(documents, "Configs", "engine_config.txt");
        if (!File.Exists(path))
            return;
        var updated = ClientJoin.RaiseLowTerrain(File.ReadAllText(path));
        if (updated == null)
            return;
        File.WriteAllText(path, updated);
        Log("Terrain quality was Low. It was raised to Medium so the loading screen is less likely to close.");
    }

    private void WriteDirectAddress(string address)
    {
        var path = CoopOptionsPath();
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var current = File.Exists(path) ? File.ReadAllText(path) : "{}";
        File.WriteAllText(path, ClientJoin.SetDirectAddress(current, address));
    }

    private void ClearStoredAddress()
    {
        var path = CoopOptionsPath();
        if (!File.Exists(path))
            return;
        var current = File.ReadAllText(path);
        var updated = ClientJoin.RemoveDirectAddress(current);
        if (updated == null)
            return;
        File.WriteAllText(path, updated);
        if (ClientJoin.RemoveNonIpAddress(current) != null)
            Log("Removed a saved address that was not an IP, so the game will not try to look that name up on the loading screen.");
    }

    private static string CoopOptionsPath() =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Mount and Blade II Bannerlord", "Configs", "BannerlordCoop", "coop_options.json");

    private void PageScroll_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (e.OriginalSource is DependencyObject source && IsDescendantOf(LogBox, source))
        {
            var logScroll = FindChildScrollViewer(LogBox);
            if (logScroll == null || logScroll.ScrollableHeight <= 0)
                return;
            ScrollByWheel(logScroll, e.Delta);
            e.Handled = true;
            return;
        }

        if (e.OriginalSource is DependencyObject nested && ParentScrollViewer(nested) is ScrollViewer inner
            && !ReferenceEquals(inner, PageScroll)
            && inner.ScrollableHeight > 0)
            return;

        if (PageScroll.ScrollableHeight <= 0)
            return;
        ScrollByWheel(PageScroll, e.Delta);
        e.Handled = true;
    }

    private static void ScrollByWheel(ScrollViewer viewer, int delta)
    {
        var lines = SystemParameters.WheelScrollLines;
        if (lines == 0)
            return;
        if (lines < 0)
            lines = 3;
        viewer.ScrollToVerticalOffset(viewer.VerticalOffset - delta / 120.0 * lines * 24);
    }

    private static bool IsDescendantOf(DependencyObject parent, DependencyObject? node)
    {
        while (node != null)
        {
            if (ReferenceEquals(node, parent))
                return true;
            node = ParentOf(node);
        }

        return false;
    }

    private static ScrollViewer? ParentScrollViewer(DependencyObject? node)
    {
        while (node != null)
        {
            if (node is ScrollViewer viewer)
                return viewer;
            node = ParentOf(node);
        }

        return null;
    }

    private static DependencyObject? ParentOf(DependencyObject node)
    {
        if (node is Visual || node is System.Windows.Media.Media3D.Visual3D)
            return VisualTreeHelper.GetParent(node);
        return LogicalTreeHelper.GetParent(node);
    }

    private static ScrollViewer? FindChildScrollViewer(DependencyObject parent)
    {
        var count = VisualTreeHelper.GetChildrenCount(parent);
        for (var i = 0; i < count; i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is ScrollViewer viewer)
                return viewer;
            var nested = FindChildScrollViewer(child);
            if (nested != null)
                return nested;
        }

        return null;
    }

    private void Log(string line)
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.Invoke(() => Log(line));
            return;
        }

        Coodsz.Ui.UiLanguage.AppendLog(LogBox, DateTime.Now.ToString("HH:mm:ss") + "  ", line, Environment.NewLine);
        LogBox.ScrollToEnd();
    }

    // A missing or unreadable rot-launcher.json gives the defaults with the one locked Realm of Thrones setup.
    private void LoadSettings() => _settings = RotClientSettings.Load(_settingsPath);

    private void SaveSettings()
    {
        _settings.GamePath = _gameRoot ?? "";
        _settings.Host = HostBox.Text.Trim();
        _settings.Port = PortBox.Text.Trim();
        _settings.Password = CurrentPassword();
        _settings.SteamName = SteamBox.Text.Trim();
        _settings.Save(_settingsPath);
    }
}
