using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using BannerlordCoopLauncher.Core.Launch;
using BannerlordCoopLauncher.Core.Modules;
using BannerlordCoopLauncher.Core.Saves;
using BannerlordCoopLauncher.Core.Server;
using BannerlordCoopLauncher.Core.Steam;
using BannerlordCoopLauncher.Core.Support;
using BannerlordCoopLauncher.Core.Updates;
using Microsoft.Win32;
using RealmOfThrones.Core;
using RealmOfThrones.Ui;
using TaomCoop.Core;

namespace RealmOfThronesHost;

public partial class MainWindow
{
    // The Coop port players join on (RotStack.DirectPort).
    private static readonly int DirectPort = int.Parse(RotStack.DirectPort, System.Globalization.CultureInfo.InvariantCulture);

    // Counts the server starts; the console handlers of a run only pass lines on while it is the newest.
    private int _consoleRunId;

    private bool IsCurrentRun(int runId) => Volatile.Read(ref _consoleRunId) == runId;

    // Stop, Restart or this tool asked the server to stop, so its exit is no crash.
    private volatile bool _stopAsked;

    // The server reached SERVING in this run.
    private volatile bool _servedThisRun;

    // This run makes a new campaign.
    private bool _runCreatesCampaign;

    public const string PickServerButton = "Choose Coop server";

    // The server runs from the folder Prepare server made (RotServerBuild) or, in a build without the RoT build's server,
    // from the folder the host picked (an unpacked Coop server), so the check is that it has its program and that the last
    // Prepare server there finished. Null when it is ready.
    private string? ServerFolderProblem(string? server)
    {
        var carried = CarriesServerCoop();
        if (string.IsNullOrWhiteSpace(server))
            return carried ? NoServerYet : "Click Choose Coop server and pick the folder of the unpacked Coop server.";
        if (!File.Exists(Path.Combine(server, NightlyServer.ExeName)))
            return carried
                ? NightlyServer.ExeName + " is not in " + server + ". Click Prepare server."
                : NightlyServer.ExeName + " is not in " + server + ". Click Choose Coop server and pick the folder of the unpacked Coop server.";
        if (RotServerBuild.IsUnfinished(server))
            return RotServerBuild.Unfinished;
        return null;
    }

    private const string NoServerYet = "There is no server yet. Click Prepare server to make it.";

    // True when this exe carries the RoT build of Coop's server overlay (a single file publish; a Debug build has none).
    private static bool CarriesServerCoop()
    {
        using var zip = Assembly.GetEntryAssembly()?.GetManifestResourceStream(RotServerBuild.ResourceName);
        return zip != null;
    }

    // Prepare server: everything Start server does to the server folder (the checks, the modules, the server map),
    // without starting it, so the long first map step can run while nobody waits for players. With the RoT build of
    // Coop carried it first makes or updates the server itself (MakeRotServerAsync).
    private async void Prepare_Click(object sender, RoutedEventArgs e)
    {
        // Start keeps _busy false while it starts, so _startingServer counts as busy too.
        if (_busy || _startingServer)
        {
            SetControl("Prepare server waits until the tool finishes what it is doing now. Click it again in a moment.", failed: true);
            return;
        }

        // Prepare server copies into the folder the running server loads from.
        if (ServerIsRunning())
        {
            SetControl("Stop the server before Prepare server.", failed: true);
            return;
        }

        var carried = CarriesServerCoop();
        if (!carried)
        {
            NoteLog(RotServerBuild.NotCarried);
            if (ServerFolderProblem(_settings.ServerRoot) is { } problem)
            {
                SetControl(problem, failed: true);
                return;
            }
        }

        _busy = true;
        try
        {
            if (carried && !await MakeRotServerAsync())
                return;
            if (await PrepareRotServerAsync(_settings.ServerRoot) != null)
                SetControl("Server prepared. " + _mapLine + " Pick a Realm of Thrones save or make a new campaign, then click Start server.");
        }
        catch (Exception ex)
        {
            // The last resort: this handler is async void, so anything the steps do not catch would close the tool.
            NoteLog(ex.ToString());
            SetControl(PrepareFailed, failed: true);
        }
        finally
        {
            _busy = false;
            RefreshState();
        }
    }

    private const string PrepareFailed = "Prepare server stopped on an error the tool did not expect. The Logs page has the details. Click Prepare server again.";

    // Makes or updates the server in RotServerBuild.DefaultRoot from the Workshop Coop server, War Sails from the host's
    // game and the carried overlay, then points the tool at it. A server folder the host used before stays where it is.
    // False with the reason shown.
    private async Task<bool> MakeRotServerAsync()
    {
        if (FindGameRoot() is not { } game)
        {
            SetControl(GameMissingLine, failed: true);
            ShowPage("Dashboard");
            return false;
        }

        var libraries = BannerlordFinder.SteamLibraries().ToList();
        var workshop = CoopStaging.FindDedicatedServer(libraries);
        var missing = AntivirusFiles.WorkshopServerProblem(CoopStaging.FindWorkshopModule(libraries), RotServerBuild.PrepareButton, null, libraries);
        if (missing != null || workshop == null)
        {
            SetControl((missing ?? AntivirusText.NoWorkshopCoop(RotServerBuild.PrepareButton)) + " "
                + BannerlordCoopLauncher.Core.Localization.Translator.T("Prepare server uses the Steam Workshop dedicated server as a base and adds the bundled Realm of Thrones build of Coop."), failed: true);
            return false;
        }

        if (RotServerBuild.GameProblem(game) is { } warSails)
        {
            SetControl(warSails, failed: true);
            return false;
        }

        var root = RotServerBuild.DefaultRoot;
        SetControl(RotServerBuild.Making(root));
        // The moving bar shows it still works (RefreshState leaves it up while _makingServer).
        _makingServer = true;
        ShowStartProgress(null);
        try
        {
            var lines = await Task.Run(() =>
            {
                using var zip = Assembly.GetEntryAssembly()!.GetManifestResourceStream(RotServerBuild.ResourceName)!;
                return RotServerBuild.Build(workshop, game, zip, RotStack.CoopBuild.ServerZipSha256, root, line => SetControl(line));
            });
            foreach (var line in lines)
                NoteStart(line);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or InvalidDataException)
        {
            SetControl(ex.Message, failed: true);
            return false;
        }
        finally
        {
            _makingServer = false;
            HideStartProgress();
        }

        var old = _settings.ServerRoot.Trim();
        if (old.Length == 0 || !Path.TrimEndingDirectorySeparator(Path.GetFullPath(old)).Equals(root, StringComparison.OrdinalIgnoreCase))
        {
            _settings.ServerRoot = root;
            SaveSettings();
            ShowServerPath();
            ReloadSaves();
            ShowKingdomProtection();
            if (old.Length > 0)
                NoteStart(RotServerBuild.LeftOld(root, old));
        }

        return true;
    }

    private bool _makingServer;

    // Choose Coop server: the folder of a Coop dedicated server the host unzipped (the Coop nightly server that runs
    // Realm of Thrones). The server runs from that folder; the tool copies no server of its own.
    private void PickCoopServer_Click(object sender, RoutedEventArgs e)
    {
        if (_busy || _startingServer)
        {
            SetControl("Choose Coop server waits until the tool finishes what it is doing now. Click it again in a moment.", failed: true);
            return;
        }

        // A running server keeps the folder it started from.
        if (ServerIsRunning())
        {
            SetControl("Stop the server before you choose another Coop server.", failed: true);
            return;
        }

        var dialog = new OpenFolderDialog { Title = BannerlordCoopLauncher.Core.Localization.Translator.T("Pick the folder with BannerlordCoopServer.exe"), Multiselect = false };
        if (dialog.ShowDialog(this) != true)
            return;
        var server = CoopInstall.ServerFolderIn(dialog.FolderName);
        if (server == null || !File.Exists(Path.Combine(server, NightlyServer.ExeName)))
        {
            // CoopInstall.NotServerFolder names the v0.1.7 download, which cannot run Realm of Thrones.
            SetControl("That folder has no Coop dedicated server. Pick the folder you unpacked the Coop server for Realm of Thrones to, the one with BannerlordCoopServer.exe and the engine folder in it.", failed: true);
            return;
        }

        _settings.ServerRoot = server;
        _settings.CoopServerFolder = server;
        SaveSettings();
        ShowServerPath();
        ReloadSaves();
        ShowKingdomProtection();
        _actions.Note("Choose Coop server", "Coop server folder set");
        if (CoopInstall.UnblockedLine(CoopInstall.ClearServerMarks(server)) is { } unblocked)
            NoteLog(unblocked);
        SetControl("The server runs from " + server + ". Click Start server.");
        RefreshState();
    }

    private async void Start_Click(object sender, RoutedEventArgs e)
    {
        // Restart and Last good save set this before they call here: the tool's own start never asks the older save question.
        var automatic = _startFromTool;
        _startFromTool = false;
        // A Start click while Prepare server (or another action) still works would start the same folder a second time.
        if (_busy || _startingServer)
        {
            SetControl("Wait for the current action to finish.", failed: true);
            return;
        }

        // Start server uses the saved setup, so mod changes not saved yet are saved or dropped first.
        if (!ConfirmHostModChanges())
            return;
        await StartServerAsync(automatic);
    }

    private bool _startingServer;
    private bool _startFromTool;

    // automatic: the tool started this run itself (Restart, Last good save), so no question opens.
    private async Task StartServerAsync(bool automatic = false)
    {
        if (_startingServer)
            return;
        _startingServer = true;
        _automaticRun = automatic;
        try
        {
            await StartServerCoreAsync();
        }
        finally
        {
            _startingServer = false;
            _automaticRun = false;
            // A yes to the older save question holds only for the start again after a setup switch.
            if (!_keepSaveSwitchAgreed)
                _saveSwitchAgreed = null;
            _keepSaveSwitchAgreed = false;
        }
        // A Start that failed or ended opens the Mods page again now, not at the next refresh.
        ApplyModListBlock();
    }

    // The new campaign's name or the picked save, checked; null with the reason shown.
    private (string Save, string? NewCampaign)? StartSave()
    {
        var newCampaign = PendingNewCampaign();
        if (newCampaign != null)
        {
            var problem = NightlyServer.NewSaveProblem(newCampaign);
            if (problem == null && File.Exists(Path.Combine(ServerSavesDirectory(), NightlyServer.SaveStem(newCampaign) + ".sav")))
                problem = "A campaign named " + NightlyServer.SaveStem(newCampaign) + " is already on the server. Pick another name, or pick that save.";
            if (problem != null)
            {
                SetControl(problem, failed: true);
                return null;
            }

            return (Path.Combine(ServerSavesDirectory(), NightlyServer.SaveStem(newCampaign) + ".sav"), NightlyServer.SaveStem(newCampaign));
        }

        var save = SelectedSavePath();
        if (NightlyServer.IsBlank(save))
        {
            SetControl(NightlyServer.NewSaveProblem(Path.GetFileNameWithoutExtension(save))!, failed: true);
            return null;
        }

        if (!SaveSlot.CanHost(save, ServerSavesDirectory()))
        {
            SetControl("Start server only loads a save already in the server Game Saves folder. Pick the one marked Server copy, or click Name server copy first. Or type a name and click New campaign.", failed: true);
            return null;
        }

        // An earlier save picked in the box would quietly replace the campaign the server ran.
        if (!ConfirmSaveSwitch(save))
        {
            SetControl(SaveSwitchWarning.NotStarted(LastRanSaveName()), failed: true);
            return null;
        }

        // A server stopped in the middle of a save leaves this marker. The server puts back the
        // save from before that save when it loads, which is the .sav the header shows now.
        if (SaveSet.MarkerExists(WorldSavedLine.TransactionMarker(ServerSavesDirectory(), Path.GetFileNameWithoutExtension(save))))
            NoteLog("The last save of " + Path.GetFileNameWithoutExtension(save) + " did not finish. The server loads the save from before it.");
        return (save, null);
    }

    /// <summary>
    /// Start server, in this order: the server folder, the save, the save and setup check, the Coop server terms,
    /// then PrepareRotServerAsync (the problems before start, the modules staged and the server map),
    /// server-config.json and mod-config.json (War Sails always on), a new campaign's save seeded from the blank,
    /// then the server started with NightlyServer.StartPlan. The terms come before Prepare on purpose: making the
    /// map can take minutes, and the host answers the question at once instead of after that wait. An accept
    /// stays an accept of those terms even when a problem then stops this start.
    /// A new campaign whose world was never built has its seeded save removed again (RemoveUnbuiltCampaign).
    /// </summary>
    private async Task StartServerCoreAsync()
    {
        if (ServerIsRunning())
        {
            SetControl("A Coop server from this folder is already running. It was left up.");
            RefreshState();
            return;
        }

        var server = _settings.ServerRoot;
        if (ServerFolderProblem(server) is { } folderProblem)
        {
            SetControl(folderProblem, failed: true);
            return;
        }

        _startNotes.Clear();
        if (StartSave() is not { } picked)
            return;
        var (save, newCampaign) = picked;
        var saveName = Path.GetFileNameWithoutExtension(save);
        if (UdpPortBusy(DirectPort))
        {
            SetControl("UDP " + DirectPort + " is already in use. Stop the other dedicated server first. This one was not started.", failed: true);
            return;
        }

        // Low memory does not stop the start; the host reads why the server or game may freeze.
        var memoryWarning = await Task.Run(() => LowMemory.ServerWarning(LowMemory.Read(), LowMemory.GameRunning()));
        if (memoryWarning != null)
        {
            NoteLog(memoryWarning);
            _actions.Note("Low memory", memoryWarning);
        }

        // A world belongs to the setup it was made with.
        if (newCampaign == null && !ConfirmSaveMatchesSetup(save))
        {
            if (_startAgain)
            {
                // The save mods box picked the setup the save was made with: start again with it.
                _startAgain = false;
                SetControl("Switched to the setup this save was made with. Starting the server again.");
                _keepSaveSwitchAgreed = true;
                var automatic = _automaticRun;
                _ = Dispatcher.BeginInvoke(new Action(() => _ = StartServerAsync(automatic)));
                return;
            }

            SetControl("The server was not started. Pick the setup the save was made with on the Mods page.", failed: true);
            return;
        }

        var acceptTerms = AskServerTerms(server);
        if (acceptTerms == null)
        {
            SetControl(RotServerTerms.Refused, failed: true);
            return;
        }

        var plan = await PrepareRotServerAsync(server);
        if (plan == null)
            return;
        if (ServerIsRunning())
        {
            SetControl("A Coop server from this folder is already running. It was left up.");
            RefreshState();
            return;
        }

        try
        {
            var dataDir = _settings.DataRoot;
            Directory.CreateDirectory(dataDir);
            RepairModConfig();
            WriteConfig(null, saveName);
            // Coodsz Server Tools last, once Prepare put it in (RotServerTools); a list naming a missing module stops the server.
            NightlyServer.ApplyConfig(dataDir, ServerToolsModule.Installed(server) ? RotServerTools.ServerModules(plan.Modules) : plan.Modules, saveName);
            NightlyServer.ApplyWarSails(dataDir, warSails: true, NightlyServer.ModConfigTemplate(server));
            ApplyKingdomProtection();
            ShowKingdomProtection();
            NoteStart(RotUiText.WarSailsLine);
            if (newCampaign != null && NightlyServer.SeedSave(dataDir, server, saveName, warSails: true) == SeedResult.NoBlank)
            {
                SetControl("The server has no blank new game to start " + saveName + " from. Choose Coop server again and pick a whole unpacked Coop server.", failed: true);
                return;
            }

            _steamAtStart = SteamBox.IsChecked == true;
            _autosaveHeld = false;
            _spareUntilOnMap = false;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            SetControl(ex.Message, failed: true);
            return;
        }

        RememberServerModuleNotes(server);
        var engine = NightlyServer.StartPlan(server, CoopDataRoot, acceptTerms == true, RotServerTools.StartVariables(_settings.DataRoot));
        var start = new ProcessStartInfo
        {
            FileName = engine.FileName,
            WorkingDirectory = engine.WorkingDirectory,
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            // Console input is UTF-8 without a byte order mark: with one the server loses the first command.
            StandardInputEncoding = engine.InputEncoding,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
            CreateNoWindow = true
        };
        foreach (var argument in engine.Arguments)
            start.ArgumentList.Add(argument);
        foreach (var name in engine.RemoveVariables)
            start.Environment.Remove(name);
        foreach (var name in start.Environment.Keys.Where(RotServerBuild.TesterOnlyVariable).ToList())
            start.Environment.Remove(name);
        foreach (var (key, value) in engine.Environment)
            start.Environment[key] = value;
        var process = new Process { StartInfo = start, EnableRaisingEvents = true };
        // One log per start, so a restart cannot close the next run's file.
        var consoleLog = new ConsoleLogFile();
        // Each run's lines carry its run id. A late line from a stopped run still goes to that run's own
        // log file, but never changes the state of the run that started after it.
        var runId = Interlocked.Increment(ref _consoleRunId);
        var watch = new RotWorldWatch(newWorld: newCampaign != null);
        process.OutputDataReceived += (_, args) =>
        {
            consoleLog.Write(args.Data);
            if (IsCurrentRun(runId))
            {
                CaptureLine(args.Data);
                WatchWorld(watch, args.Data);
            }
        };
        process.ErrorDataReceived += (_, args) =>
        {
            consoleLog.Write(args.Data);
            if (IsCurrentRun(runId))
            {
                CaptureLine(args.Data);
                WatchWorld(watch, args.Data);
            }
        };
        process.Exited += (_, _) =>
        {
            // Let the last output lines arrive before the file is closed.
            try
            {
                if (process.WaitForExit(5000))
                    process.WaitForExit();
            }
            catch (InvalidOperationException)
            {
            }

            int? exitCode;
            try { exitCode = process.ExitCode; }
            catch (InvalidOperationException) { exitCode = null; }
            consoleLog.Write(exitCode is { } loggedCode ? "[RoT host] Server exited with code " + loggedCode + "." : "[RoT host] Server exited.");
            consoleLog.Close();
            Dispatcher.Invoke(() =>
            {
                // Only this tool's own run that is still the current one (a stop already let go of it).
                if (ReferenceEquals(_process, process))
                {
                    _playHold.Stop();
                    NoteHostReport(_logTail.ToString(), exited: true);
                    if (!_stopAsked && exitCode is { } code && NightlyServer.ExitHint(code) is { } hint)
                        SetControl(hint, failed: true);
                    if (newCampaign != null)
                        RemoveUnbuiltCampaign(saveName);
                    OfferServerCrashReport(process);
                }
                RefreshState();
                ShowSaveDetail();
            });
        };
        // A new start follows only its own state events.
        _fatalState = null;
        _playHold.Stop();
        _runCreatesCampaign = newCampaign != null;
        // This run reads only its own logs and its own console lines, never the last run's SERVING.
        _runStartedUtc = DateTime.UtcNow.AddSeconds(-2);
        // Nor the last run's players: a stale list would let a ban kick or a queued kick go by id into this run.
        ForgetPlayers();
        // The Cheats page reads the players and lists of the new run.
        CoopCheats.ServerStarting();
        _logTail.Clear();
        _servedThisRun = false;
        _seenServing = false;
        // A new run: its exit is a crash unless Stop, Restart or this tool ends it.
        _stopAsked = false;
        _servingCheckedPath = "";
        _servingScanOffset = 0;
        consoleLog.Open(Path.Combine(_settings.DataRoot, "logs"), "[RoT host] Start " + start.FileName + " " + engine.ArgumentLine);
        // What the tool did to the server folder before this start, kept with the run's log.
        foreach (var note in _startNotes)
            consoleLog.Write("[RoT host] " + note);
        _startNotes.Clear();
        bool started;
        try
        {
            started = process.Start();
        }
        catch (Exception ex)
        {
            consoleLog.Close();
            SetControl("The server did not start. " + ex.Message, failed: true);
            if (newCampaign != null)
                RemoveUnbuiltCampaign(saveName);
            return;
        }

        if (!started)
        {
            consoleLog.Close();
            SetControl("The server did not start.", failed: true);
            if (newCampaign != null)
                RemoveUnbuiltCampaign(saveName);
            return;
        }

        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        _blockedDllNoted = false;
        lock (_infoReportsNoted)
            _infoReportsNoted.Clear();
        _joinAdvice.Clear();
        _sharedSteamIds.Reset();
        _process = process;
        // The Start question compares against this save until a later Start's server runs.
        _settings.LastHostedSave = saveName;
        // The server already runs, so a settings file that cannot be written only keeps the name until the next save.
        try { SaveSettings(); } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        // The last run's crash bar goes once the server runs again, so its crash line is never sent with the new logs.
        _crashBar?.ServerStarted();
        _speedAtStart = SavedGameSpeed();
        // The first start from this server folder brings the Windows Firewall prompt for the server.
        if (RotServerNotes.FirewallHintShows(_settings.FirewallHintDoneFor, _settings.ServerRoot, _firewall == BannerlordCoopLauncher.Core.Joining.FirewallState.Allowed))
            NoteLog(RotServerNotes.FirewallHint);
        _actions.NotePassword(!string.IsNullOrWhiteSpace(PasswordBox.Text));
        _actions.Note("Start", "Save=" + saveName + ". NewCampaign=" + (newCampaign != null)
            + ". ServerRoot set=" + !string.IsNullOrWhiteSpace(_settings.ServerRoot));
        if (newCampaign != null)
        {
            // The server writes the campaign under this name; later starts load it like any save.
            _settings.SaveFile = save;
            SaveSettings();
        }

        SetControl(newCampaign != null
            ? "Server is starting a new Realm of Thrones campaign named " + newCampaign + ". It is ready when the status at the top says SERVER ONLINE. The first start builds the world and the map cache and can take up to an hour. The status at the top shows how far it is."
            : "Server is starting. It is ready when the status at the top says SERVER ONLINE. The first start of a campaign builds the map cache and can take up to an hour. The status at the top shows how far it is.");
        RefreshState();
    }

    // PLAN F8: a new campaign's save is the seeded blank until the server saves the built world. A run that ended
    // before that (an exit code, a crash, a Stop while it loaded, a start that failed) leaves the blank under the
    // new name, so the name is given back and the next Start server seeds and builds it again.
    private void RemoveUnbuiltCampaign(string saveName)
    {
        try
        {
            if (!NightlyServer.RemoveUnbuiltSeed(_settings.DataRoot, _settings.ServerRoot, saveName))
                return;
            _settings.NewCampaignName = NightlyServer.SaveStem(saveName);
            ReloadSaves();
            SaveSettings();
            ShowSaveDetail();
            NoteLog("The world of " + NightlyServer.SaveStem(saveName) + " was never built, so its empty save was removed. Start server makes the campaign again.");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            NoteLog(ex.Message);
        }
    }

    // The console lines of a run until the world can be saved (RotWorldWatch). Ready: the tool sends "save" once, so
    // a new world is on disk at once. Failed: the line is shown and nothing is saved.
    private void WatchWorld(RotWorldWatch watch, string? line)
    {
        var before = watch.State;
        var state = watch.Feed(line);
        if (state == before)
            return;
        Dispatcher.BeginInvoke(new Action(() =>
        {
            if (state == RotWorldState.Failed)
            {
                SetControl(RotServerNotes.WorldFailed(watch.FailedLine), failed: true);
                _actions.Note("World", "Stopped waiting: " + watch.FailedLine);
                return;
            }

            if (state == RotWorldState.Ready && CommandPipeReady())
            {
                NoteLog(watch.NewWorld ? RotServerNotes.NewWorldReady : RotServerNotes.LoadedWorldReady);
                _ = ShowWorldSave();
            }
        }));
    }

    private void Save_Click(object sender, RoutedEventArgs e) => _ = ShowWorldSave();

    private void Stop_Click(object sender, RoutedEventArgs e)
    {
        // Busy first, so no question is asked for a stop that would be turned down anyway (SaveThenStop checks again).
        if (_busy || _startingServer)
        {
            SetControl("Wait for the current action to finish.", failed: true);
            return;
        }
        if (!AskStopOrRestart("Stop server", "Stop the Realm of Thrones server? It saves the campaign first, then players are disconnected."))
            return;
        _actions.Note("Stop", "Requested stop");
        _ = SaveThenStop(restart: false);
    }

    // One Stop or Restart question at a time: clicks while it is open used to stack more of them (RoT live test).
    // The confirm button says what it does.
    private bool _askingStop;

    private bool AskStopOrRestart(string action, string body)
    {
        if (_askingStop)
            return false;
        _askingStop = true;
        try
        {
            return ConfirmWindow.Ask(this, action, body, null, action);
        }
        finally
        {
            _askingStop = false;
        }
    }

    private void Restart_Click(object sender, RoutedEventArgs e)
    {
        if (_busy || _startingServer)
        {
            SetControl("Wait for the current action to finish.", failed: true);
            return;
        }
        // Restart brings the same world back for the players. A new campaign typed during the run would win at
        // the next start and build a fresh world instead, so Restart lets it go, and the question says so.
        var waiting = PendingNewCampaign();
        var body = waiting == null
            ? "Restart the Realm of Thrones server? It saves the campaign first. Players are disconnected, then the same save loads again."
            : "Restart the Realm of Thrones server? It saves the campaign first. Players are disconnected, then the same save loads again. The new campaign "
              + waiting + " is not made. Type it again after the restart to make it.";
        if (!AskStopOrRestart("Restart server", body))
            return;
        if (waiting != null)
        {
            ClearNewCampaign();
            ShowSaveDetail();
        }
        _actions.Note("Restart", "Requested restart");
        _ = SaveThenStop(restart: true);
    }

    private void SaveSettings_Click(object sender, RoutedEventArgs e) => SaveSettingsPage();

    // The Settings page's one save. The Game speed buttons on the Dashboard use it too.
    // fromSettingsPage false: the Dashboard game speed buttons, which restart the server right after, so the
    // greyed difficulty line (start the server once, save again) belongs only to Save settings itself.
    private bool SaveSettingsPage(bool fromSettingsPage = true)
    {
        try
        {
            var serverRules = ServerRulesFromPage();
            WriteConfig();
            SaveTimePermissions();
            SaveServerRules(serverRules);
            // The save already worked; a file that cannot be read again only leaves a box blank.
            try { RefillEmptySettings(); } catch (Exception) { }
            _actions.NotePassword(!string.IsNullOrWhiteSpace(PasswordBox.Text));
            _actions.Note("Settings", "Saved host settings. Save=" + Path.GetFileNameWithoutExtension(SelectedSavePath() ?? ""));
            if (_difficultySkipped > 0)
                _actions.Note("Settings", "Skipped " + _difficultySkipped + " greyed out difficulty settings (not in mod-config.json yet)");
            SetControl(_difficultySkipped > 0 && fromSettingsPage
                ? CoopDifficulty.SkippedOnSave
                : "Saved settings. Restart the server to use them. Fast forward and the battle size stay as they are until that restart.");
            RefreshGameSpeed();
            return true;
        }
        catch (Exception ex)
        {
            SetControl(ex.Message, failed: true);
            return false;
        }
    }

    private void StopServer()
    {
        _stopAsked = true;
        int? ownId = null;
        try
        {
            ownId = _process?.Id;
            if (_process is { HasExited: false })
                _process.Kill(entireProcessTree: true);
        }
        catch (Exception ex)
        {
            NoteLog(ex.Message);
        }

        foreach (var process in Process.GetProcessesByName("BannerlordCoopServer"))
        {
            try
            {
                string? path = null;
                try { path = process.MainModule?.FileName; }
                catch (Exception) { }
                // Another tool's server can run next to this one: a process whose folder
                // cannot be read, or any process while no server folder is chosen, is stopped only when it is this run's.
                var ours = path != null && !string.IsNullOrWhiteSpace(_settings.ServerRoot)
                    ? DedicatedServerWatch.IsUnder(path, _settings.ServerRoot)
                    : process.Id == ownId;
                if (!ours)
                    continue;
                process.Kill(entireProcessTree: true);
                NoteLog("Stopped server process " + process.Id + ".");
            }
            catch (Exception ex)
            {
                NoteLog(ex.Message);
            }
            finally
            {
                process.Dispose();
            }
        }

        foreach (var process in Process.GetProcessesByName("dotnet"))
        {
            try
            {
                string? path = null;
                try { path = process.MainModule?.FileName; }
                catch (Exception) { }
                if (!DedicatedServerWatch.IsEngine(path, _settings.ServerRoot))
                    continue;
                process.Kill(entireProcessTree: true);
                NoteLog("Stopped server engine " + process.Id + ".");
            }
            catch (Exception ex)
            {
                NoteLog(ex.Message);
            }
            finally
            {
                process.Dispose();
            }
        }

        _process = null;
        ForgetPlayers();
    }

    private async Task ShowWorldSave()
    {
        if (_busy || _startingServer)
        {
            SetControl("Wait for the current action to finish.", failed: true);
            return;
        }
        if (!ServerIsRunning())
        {
            SetControl("The server is stopped, so there is nothing to save.");
            RefreshState();
            return;
        }

        var refusal = SaveRefusal();
        if (refusal != null)
        {
            SetControl(refusal, failed: true);
            return;
        }

        _busy = true;
        ShowSaving();
        try
        {
            var saved = await Task.Run(WaitForWorldSave);
            SetControl(saved ? "Saved " + ConfiguredSaveName() + "." : "The server did not finish saving.", failed: !saved);
        }
        catch (Exception ex)
        {
            SetControl(ex.Message, failed: true);
        }
        finally
        {
            _saving = false;
            _busy = false;
            RefreshState();
            // The chips and the save check read the .sav header, which a save just rewrote.
            ShowSaveDetail();
        }
    }

    private async Task SaveThenStop(bool restart)
    {
        if (_busy || _startingServer)
        {
            SetControl("Wait for the current action to finish.", failed: true);
            return;
        }
        if (!ServerIsRunning())
        {
            SetControl("The server is already stopped.");
            RefreshState();
            return;
        }

        // A campaign that did not load is never saved or sent stop, like a broken world.
        var refusal = SaveRefusal(stopping: true);
        // A server still loading (it never said SERVING in this run) is stopped right away.
        RefreshState();
        var loading = refusal == null && !RotServerNotes.WaitsForSave(_seenServing || _servedThisRun);
        if (loading)
            _actions.Note("Stop", "The server was still loading, so it was stopped without a save");
        _busy = true;
        var saved = false;
        var startAgain = false;
        _stopAsked = true;
        try
        {
            if (refusal == null && !loading)
            {
                if (restart)
                    await WarnPlayersOfRestart();
                ShowSaving();
                SetControl("Saving the world, then the server will stop.");
                saved = await Task.Run(WaitForWorldSave);
                // A broken world is never sent stop: stop writes the save on stop.
                await Task.Run(AskServerToStop);
            }

            var run = _process;
            var unbuilt = _runCreatesCampaign ? _settings.LastHostedSave : null;
            StopServer();
            if (unbuilt != null)
            {
                // The killed server lets go of its save first; a Restart then makes the campaign again.
                try { await Task.Run(() => run?.WaitForExit(5000)); }
                catch (InvalidOperationException) { }
                RemoveUnbuiltCampaign(unbuilt);
            }
            _saving = false;
            RefreshState();
            ShowSaveDetail();
            SetControl(refusal ?? (loading
                ? RotServerNotes.StoppedWhileLoading
                : saved
                    ? "Saved " + ConfiguredSaveName() + ", then stopped."
                    : "Stopped. No new save was written."), failed: refusal != null);
            if (restart)
                startAgain = true;
        }
        catch (Exception ex)
        {
            _saving = false;
            SetControl(ex.Message, failed: true);
            RefreshState();
        }
        finally
        {
            _busy = false;
        }

        // After _busy is cleared, so Start server's own wait check lets the restart through and its Prepare
        // keeps _busy for itself. The restart hosts the save the server ran, not a later pick in the box.
        if (startAgain)
        {
            SelectHostedSave(LastRanSaveName());
            _startFromTool = true;
            Start_Click(this, new RoutedEventArgs());
        }
    }

    private void ShowSaving()
    {
        _saving = true;
        StateText.Text = "SAVING";
        StateDetail.Text = "Writing the campaign.";
        UptimeText.Text = "Saving.";
        StatusDot.Fill = new SolidColorBrush(Color.FromRgb(176, 132, 72));
    }

    private async void RestartLastGood_Click(object sender, RoutedEventArgs e)
    {
        if (_busy || _startingServer)
        {
            SetControl("Wait for the current action to finish.", failed: true);
            return;
        }
        var name = ConfiguredSaveName();
        if (name.Length == 0)
        {
            SetControl("Prepare the server before loading a save.", failed: true);
            return;
        }

        var brokenAt = BrokenWorldTime();
        if (brokenAt == null)
        {
            SetControl("The hero can rejoin this world. Last good save is kept for when they cannot.");
            return;
        }

        await KeepLastGoodCopyAsync();
        // The copy can take seconds on a big save; another action may have started meanwhile.
        if (_busy || _startingServer)
        {
            SetControl("Wait for the current action to finish.", failed: true);
            return;
        }
        var live = Path.Combine(ServerSavesDirectory(), name + ".sav");
        var pick = LastGoodSave.Pick(SaveCopies(name), brokenAt);
        if (string.IsNullOrEmpty(pick) || pick.Equals(live, StringComparison.OrdinalIgnoreCase))
        {
            SetControl("No save from before the break is on disk. Later autosaves replaced those copies. This world was not saved.", failed: true);
            return;
        }

        var created = SaveHeader.ReadCreated(pick);
        var when = created?.ToString("h:mm tt") ?? "an earlier time";
        if (!ConfirmWindow.Ask(this, "Last good save", "Restart on the save from " + when + "? The world running now is not written. Players disconnect, then that earlier save loads."))
            return;

        _busy = true;
        var loaded = false;
        try
        {
            StopServer();
            LastGoodSave.CopyOver(pick, live);
            var jsonSource = Path.Combine(ServerSavesDirectory(), Path.GetFileNameWithoutExtension(pick) + ".json");
            var jsonDestination = Path.Combine(ServerSavesDirectory(), name + ".json");
            if (File.Exists(jsonSource))
                LastGoodSave.CopyOver(jsonSource, jsonDestination);
            _actions.Note("Last good save", "Restored " + name + " from " + Path.GetFileName(pick));
            SetControl("Loaded the save from " + when + ". Starting the server.");
            loaded = true;
        }
        catch (Exception ex)
        {
            SetControl(ex.Message, failed: true);
        }
        finally
        {
            _busy = false;
        }

        // After _busy is cleared, the same as the restart after Save and stop. The restored save is the one the server ran, so
        // it loads with no question.
        if (loaded)
        {
            SelectHostedSave(name);
            _startFromTool = true;
            Start_Click(this, new RoutedEventArgs());
        }
    }

    private int _keepingLastGood;
    private Task _keepLastGoodTask = Task.CompletedTask;

    // From the window refresh: the copy of a whole save runs on a worker, one at a time, so a big
    // Realm of Thrones save never freezes the window while it copies.
    private void KeepLastGoodCopy()
    {
        if (Interlocked.Exchange(ref _keepingLastGood, 1) == 1)
            return;
        _keepLastGoodTask = Task.Run(() =>
        {
            try
            {
                KeepLastGoodCopyNow();
            }
            catch (Exception)
            {
                // Best effort: the next refresh tries again.
            }
            finally
            {
                Volatile.Write(ref _keepingLastGood, 0);
            }
        });
    }

    // Last good save waits for a copy that is still writing the kept file, then keeps the newest one.
    private async Task KeepLastGoodCopyAsync()
    {
        await _keepLastGoodTask;
        KeepLastGoodCopy();
        await _keepLastGoodTask;
    }

    private void KeepLastGoodCopyNow()
    {
        try
        {
            var name = ConfiguredSaveName();
            if (name.Length == 0)
                return;
            var directory = ServerSavesDirectory();
            var destination = Path.Combine(directory, name + LastGoodSave.Suffix + ".sav");
            var pick = LastGoodSave.Pick(SaveCopies(name), BrokenWorldTime());
            if (string.IsNullOrEmpty(pick) || pick.Equals(destination, StringComparison.OrdinalIgnoreCase))
                return;
            var pickedAt = SaveHeader.ReadCreated(pick);
            var keptAt = File.Exists(destination) ? SaveHeader.ReadCreated(destination) : null;
            if (pickedAt != null && keptAt != null && keptAt >= pickedAt)
                return;
            LastGoodSave.CopyOver(pick, destination);
            var jsonSource = Path.Combine(directory, Path.GetFileNameWithoutExtension(pick) + ".json");
            var jsonDestination = Path.Combine(directory, name + LastGoodSave.Suffix + ".json");
            if (File.Exists(jsonSource))
                LastGoodSave.CopyOver(jsonSource, jsonDestination);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private List<SaveCopy> SaveCopies(string name)
    {
        var directory = ServerSavesDirectory();
        var copies = new List<SaveCopy>();
        foreach (var fileName in new[] { name + ".sav", name + ".backup1.sav", name + ".backup2.sav", name + LastGoodSave.Suffix + ".sav" })
        {
            var path = Path.Combine(directory, fileName);
            if (!File.Exists(path))
                continue;
            copies.Add(new SaveCopy(path, SaveHeader.ReadCreated(path)));
        }

        return copies;
    }

    private string? BrokenWorldRefusal() =>
        BrokenWorldGuard.Refusal(RecentWorldLog(out _));

    private bool CampaignLoadFailed() => BrokenWorldGuard.LoadFailed(_fatalState, ReadNewestLog());

    // Every path that saves or backs up asks this: a failed load first, then the broken world.
    private string? SaveRefusal(bool stopping = false) =>
        CampaignLoadFailed() ? (stopping ? BrokenWorldGuard.FailedLoadStopText : BrokenWorldGuard.FailedLoadSaveText) : BrokenWorldRefusal();

    private DateTime? BrokenWorldTime()
    {
        var time = BrokenWorldGuard.BrokenTime(RecentWorldLog(out var written));
        if (time == null || written == null)
            return null;
        var clock = new DateTime(written.Value.Year, written.Value.Month, written.Value.Day, written.Value.Hour, written.Value.Minute, written.Value.Second, DateTimeKind.Unspecified);
        var stamp = new DateTime(clock.Year, clock.Month, clock.Day, time.Value.Hours, time.Value.Minutes, time.Value.Seconds, DateTimeKind.Unspecified);
        if (stamp > clock.AddMinutes(2))
            stamp = stamp.AddDays(-1);
        return stamp;
    }

    private string RecentWorldLog(out DateTime? writtenAt)
    {
        writtenAt = null;
        var logs = Path.Combine(_settings.DataRoot, "logs");
        if (!Directory.Exists(logs))
            return "";
        var text = new StringBuilder();
        foreach (var file in Directory.EnumerateFiles(logs, "*.log", SearchOption.AllDirectories)
                     .Select(path => new FileInfo(path))
                     .Where(file => file.Length > 0 && (file.Name.StartsWith("Coop_server", StringComparison.OrdinalIgnoreCase) || file.Name.StartsWith("coop-server", StringComparison.OrdinalIgnoreCase)))
                     .OrderByDescending(file => file.LastWriteTimeUtc)
                     .Take(4))
        {
            var tail = HostLogFile.ReadTail(file.FullName, 400_000);
            if (!tail.Contains(BrokenWorldGuard.Marker, StringComparison.OrdinalIgnoreCase))
            {
                var older = HostLogFile.ReadTailContaining(file.FullName, BrokenWorldGuard.Marker, 400_000);
                if (older.Contains(BrokenWorldGuard.Marker, StringComparison.OrdinalIgnoreCase))
                    tail = older;
            }
            text.AppendLine(tail);
            if (writtenAt == null || file.LastWriteTime > writtenAt)
                writtenAt = file.LastWriteTime;
        }

        return text.ToString();
    }

    private bool WaitForWorldSave()
    {
        var name = ConfiguredSaveName();
        var directory = Path.Combine(_settings.DataRoot, "Game Saves");
        var sav = Path.Combine(directory, name + ".sav");
        var marker = WorldSavedLine.TransactionMarker(directory, name);
        var beforeSav = File.Exists(sav) ? File.GetLastWriteTimeUtc(sav) : DateTime.MinValue;
        var beforeCount = Volatile.Read(ref _worldSavedCount);
        var owned = _process is { HasExited: false };
        if (owned)
        {
            // In its turn of the command queue; this runs off the window thread, so it may wait for that turn here.
            var process = _process!;
            if (!WriteQueued(() => WriteLine(process, "save")).GetAwaiter().GetResult())
                return false;
        }
        // A server this window did not start gets the save through its console queue, in its turn of the command queue too.
        else if (!WriteQueued(AskQueuedSave).GetAwaiter().GetResult())
        {
            return false;
        }

        // The .json and the .autosave-transaction marker are written when the save starts and
        // the .sav last, so only the server's "world saved" line, or a newer .sav with the marker
        // gone, means the save is on disk. Stopping before that loses the save: the server puts
        // the old one back on its next start.
        var deadline = DateTime.UtcNow.AddSeconds(180);
        while (DateTime.UtcNow < deadline)
        {
            if (owned && Volatile.Read(ref _worldSavedCount) != beforeCount && WorldSavedLine.SameSave(_lastWorldSavedFile, name))
                return true;
            if (SaveFileFinished(sav, marker, beforeSav))
                return true;
            if (owned && _process is not { HasExited: false })
                return false;
            Thread.Sleep(250);
        }

        return false;
    }

    private static bool SaveFileFinished(string sav, string marker, DateTime beforeSav)
    {
        if (!File.Exists(sav) || File.GetLastWriteTimeUtc(sav) <= beforeSav || SaveSet.MarkerExists(marker))
            return false;
        // The .sav stops growing once it is written.
        var size = new FileInfo(sav).Length;
        Thread.Sleep(1000);
        return !SaveSet.MarkerExists(marker) && new FileInfo(sav).Length == size;
    }

    private void WriteConfig(int? autosaveOverride = null, string? saveName = null)
    {
        // An empty box keeps the minutes already in server-config.json; only bad text stops the save.
        var autosave = HostConfigEditor.ParseAutosave(AutosaveBox.Text);
        // While the server runs it keeps its own save, so Restart loads the same save again. The box pick goes in at the next Start.
        var hosted = ServerIsRunning() ? ConfiguredSaveName() : "";
        saveName ??= hosted.Length > 0 ? hosted : CurrentSaveName();
        // Before a save or a new campaign is picked, the password and the rest go in at the first start.
        if (saveName.Length > 0 && !NightlyServer.IsBlank(saveName))
            WriteConfigFile(Path.Combine(_settings.DataRoot, "server-config.json"), saveName, PasswordBox.Text, autosaveOverride ?? autosave, SteamBox.IsChecked == true, LogFileBox.IsChecked != false);
        SaveSettings();
    }

    // The campaign the next start hosts: the new campaign name, the picked save, or the one in server-config.json.
    private string CurrentSaveName()
    {
        if (_settings.NewCampaignName.Trim().Length > 0)
            return _settings.NewCampaignName.Trim();
        var picked = SelectedSavePath();
        return picked.Length > 0 ? Path.GetFileNameWithoutExtension(picked) : ConfiguredSaveName();
    }

    // The host's settings in the data folder's server-config.json: the save, the password, autosave, Steam and the
    // log file. The file is made when the server has not made it yet; every other key and comment stays.
    private static void WriteConfigFile(string path, string saveName, string password, int? autosave, bool steam, bool logFile)
    {
        if (string.IsNullOrWhiteSpace(saveName) || NightlyServer.IsBlank(saveName))
            throw new InvalidOperationException("Pick a Realm of Thrones save or make a new campaign first.");
        if (autosave is < 0 or > 1440)
            throw new InvalidOperationException(HostConfigEditor.AutosaveRange);
        var text = File.Exists(path) ? File.ReadAllText(path) : "{\n}\n";
        var editor = BannerlordCoopLauncher.Core.Configuration.JsoncEditor.Parse(text);
        editor.SetString(NightlyServer.SaveNameKey, NightlyServer.SaveStem(saveName));
        editor.SetString("password", password);
        if (autosave is { } minutes)
            editor.SetInt("autosaveMinutes", minutes);
        editor.SetBool("steam", steam);
        editor.SetBool("logFile", logFile);
        if (editor.Text == text && File.Exists(path))
            return;
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        editor.Save(path);
    }

    // The person hosting reads the Coop server terms and decides; the tool never
    // accepts on its own. Returns null when they were not accepted, true when the
    // server should be told they were, false when the server has no terms to accept.
    private bool? AskServerTerms(string serverFolder)
    {
        var hash = RotServerTerms.TermsHash(serverFolder);
        if (hash == null)
            return false;
        if (!RotServerTerms.NeedsAnswer(hash, _settings.AcceptedEulaSha256))
            return true;
        if (!ConfirmWindow.Ask(this, RotServerTerms.Title, RotServerTerms.Body, RotServerTerms.TermsText(serverFolder), RotServerTerms.AcceptButton))
        {
            _actions.Note("Server terms", "Not accepted");
            return null;
        }

        _settings.AcceptedEulaSha256 = hash;
        SaveSettings();
        _actions.Note("Server terms", "Accepted " + hash[..12]);
        NoteLog("You accepted the Coop server terms. The server saves that in the Data folder's eula.txt.");
        return true;
    }


    private static string LocalAddress()
    {
        foreach (var adapter in NetworkInterface.GetAllNetworkInterfaces())
        {
            if (adapter.OperationalStatus != OperationalStatus.Up)
                continue;
            foreach (var address in adapter.GetIPProperties().UnicastAddresses)
            {
                if (address.Address.AddressFamily == AddressFamily.InterNetwork && !IPAddress.IsLoopback(address.Address))
                    return address.Address.ToString();
            }
        }

        return "127.0.0.1";
    }

    private async Task RunAsync(Action work)
    {
        _busy = true;
        try
        {
            await Task.Run(work);
        }
        catch (Exception ex)
        {
            SetControl(ex.Message, failed: true);
        }
        finally
        {
            _busy = false;
        }
    }

    private void SetControl(string message, bool failed = false)
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.Invoke(() => SetControl(message, failed));
            return;
        }

        _controlNote = message;
        ControlStatus.Text = message;
        ControlStatus.Foreground = (failed ? Coodsz.Ui.FrameBrushes.Get("Keep.Red", "#9E4844") : Coodsz.Ui.FrameBrushes.Get("Keep.Teal", "#7DAAA4"));
        ShowOnServerPage(message, failed);
        NoteLog(message);
    }

    // Prepare server and Choose Coop server sit on the Server page, away from the note under Start server, so a player
    // there saw nothing when one stopped (a missing Workshop Coop said so only on the Dashboard). The note shows there too.
    // It scrolls into view only for a problem or when it first shows, so Prepare's progress lines do not drag the page.
    private void ShowOnServerPage(string message, bool failed)
    {
        var first = ServerPageStatusCard.Visibility != Visibility.Visible;
        ServerPageStatus.Text = message;
        ServerPageStatus.Foreground = ControlStatus.Foreground;
        ServerPageStatusCard.Visibility = Visibility.Visible;
        if ((failed || first) && ServerPage.Visibility == Visibility.Visible)
            ServerPageStatusCard.BringIntoView();
    }

    // The English text of the note under Start server, as SetControl last wrote it.
    private string _controlNote = "";

    private static bool UdpPortBusy(int port) =>
        IPGlobalProperties.GetIPGlobalProperties().GetActiveUdpListeners().Any(end => end.Port == port);

    private static string? FindServerModuleXml(string modules, string moduleId)
    {
        if (!Directory.Exists(modules))
            return null;
        foreach (var directory in Directory.EnumerateDirectories(modules))
        {
            var xml = Path.Combine(directory, "SubModule.xml");
            if (!File.Exists(xml))
                continue;
            var identity = ModuleIdentityReader.Read(xml);
            if (identity.Id.Equals(moduleId, StringComparison.OrdinalIgnoreCase)
                || new DirectoryInfo(directory).Name.Equals(moduleId, StringComparison.OrdinalIgnoreCase))
                return xml;
        }

        return null;
    }

    private void RememberServerModuleNotes(string serverRoot)
    {
        foreach (var note in RotServerNotes.ModNotes(HostSetup))
            NoteLog(note);
        var modules = Path.Combine(serverRoot, "engine", "Modules");
        foreach (var id in RotStack.ServerModules)
        {
            if (ServerModuleNotes.KeepsClientCampaign(id))
                continue;
            var xmlPath = FindServerModuleXml(modules, id);
            if (xmlPath == null)
                continue;
            // Only a note: a SubModule.xml that cannot be read now never stops the start (the click handler is async void).
            string text;
            try { text = File.ReadAllText(xmlPath); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { continue; }
            var note = ServerModuleNotes.SkippedCode(id, text);
            if (note != null)
                NoteLog(note);
        }
    }
}
