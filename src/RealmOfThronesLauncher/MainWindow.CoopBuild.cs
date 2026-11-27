using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Threading;
using BannerlordCoopLauncher.Core.IO;
using BannerlordCoopLauncher.Core.Launch;
using BannerlordCoopLauncher.Core.Localization;
using RealmOfThrones.Core;
using TaomCoop.Core;

namespace RealmOfThronesLauncher;

// The RoT build of Coop this launcher carries (RotCoopInstall) is unpacked once, goes into Modules\Coop at Connect and
// Find the crashing mod, and comes back out when the launcher closes, or at the next start after a crash, so the other
// Coodsz launchers never find it there. Another Coop in Modules is set aside after one question (CoopParking's window).
public partial class MainWindow
{
    private readonly string _coopMarker = ToolData.File(RotCoopInstall.MarkerFileName);
    // Started with the window: the unpacked Coop folder, or the line why it is not there. Null Folder and Problem when this
    // launcher carries no zip.
    private Task<(string? Folder, string? Problem)>? _carriedCoop;
    // The carried Coop's SubModule.xml once it is unpacked; the core checks read it the way they read the carried compat.
    private string? _carriedCoopXml;
    // Only a yes is kept for this run, so Cancel asks again at the next Connect.
    private bool? _setAsideThisRun;
    // Connect stays off while the Coop moves in off the UI thread.
    private bool _coopMoving;
    // The window waits, minimized with its title saying so, for Bannerlord to close before it takes the Coop out and exits.
    private bool _closingAfterGame;
    private DispatcherTimer? _closeWait;
    // The exe files in Modules\Coop\bin, read once while the window waits (CoopProgramRunning).
    private string[]? _coopExes;
    // Windows logs off: the window closes at once and the marker stays for the next start.
    private bool _sessionEnding;
    // When this launcher last started the game, and that process (StartWithPrefabsFix). A game started a moment ago may
    // not show its process yet, so the close counts it as running for a few seconds unless that process already exited.
    private DateTime _gameStartedAt = DateTime.MinValue;
    private Process? _startedGame;

    private static bool CarriesCoop()
    {
        using var zip = Assembly.GetEntryAssembly()?.GetManifestResourceStream(RotCoopInstall.ResourceName);
        return zip != null;
    }

    private static (string? Folder, string? Problem) UnpackCarriedCoop()
    {
        try
        {
            using var zip = Assembly.GetEntryAssembly()?.GetManifestResourceStream(RotCoopInstall.ResourceName);
            if (zip == null)
                return (null, null);
            var folder = RotCoopInstall.BuildFolder(ToolData.Current?.ProductFolder ?? ToolData.Folder);
            return (RotCoopInstall.Unpack(zip, RotStack.CoopBuild.ClientZipSha256, folder), null);
        }
        catch (Exception ex)
        {
            // Any failure is a line under Connect, never a crash of the launcher.
            return (null, RotCoopInstall.CouldNotUnpack(ex.Message));
        }
    }

    // At startup, next to the window: the unpack runs off the UI thread, then the rows and Connect read the carried copy.
    private async Task PrepareCarriedCoopAsync()
    {
        try
        {
            _carriedCoop = Task.Run(UnpackCarriedCoop);
            var (folder, _) = await _carriedCoop;
            if (folder == null)
                return;
            _carriedCoopXml = Path.Combine(folder, "SubModule.xml");
            RefreshModules();
            UpdateConnectButton();
        }
        catch (Exception ex)
        {
            Log(RotCoopInstall.CouldNotUnpack(ex.Message));
        }
    }

    // True when Modules\Coop is the RoT build. Without a carried zip (a Debug build) the kept Coop check of before runs.
    // False with the line under Connect: the game runs, the player kept the other Coop, or a move failed (undone).
    private async Task<bool> PutInCoopAsync()
    {
        if (!CarriesCoop())
        {
            Log(RotCoopInstall.NotCarried);
            return RestoreKeptCoop();
        }

        _busy = _coopMoving = true;
        UpdateConnectButton();
        try
        {
            var carried = await (_carriedCoop ??= Task.Run(UnpackCarriedCoop));
            // A failed unpack (a locked file, a full disk) is tried again once here.
            if (carried.Folder == null)
                carried = await (_carriedCoop = Task.Run(UnpackCarriedCoop));
            if (carried.Folder == null)
            {
                ShowJoin(carried.Problem ?? RotCoopInstall.CouldNotUnpack(""));
                return false;
            }

            _carriedCoopXml = Path.Combine(carried.Folder, "SubModule.xml");
            var gameRoot = _gameRoot!;
            var gameRunning = ClientLaunchGuard.IsGameRunning();
            // The moves and the copy run off the UI thread; the set aside question is asked on it before anything moves.
            var result = await Task.Run(() => RotCoopInstall.PutIn(gameRoot, carried.Folder, _coopMarker, gameRunning,
                others => Dispatcher.Invoke(() => AskSetAside(others))));
            foreach (var line in result.Lines)
                LogCoop(line);
            if (result.Problem == null)
                return true;
            _actions.Note("Coop", result.Problem);
            ShowJoin(result.Problem);
            return false;
        }
        catch (Exception ex)
        {
            // Connect_Click is async void, so nothing may escape here.
            _actions.Note("Coop", RotCoopInstall.CouldNotPutIn(ex.Message));
            ShowJoin(RotCoopInstall.CouldNotPutIn(ex.Message));
            return false;
        }
        finally
        {
            _busy = _coopMoving = false;
            UpdateConnectButton();
        }
    }

    private bool AskSetAside(IReadOnlyList<RotCoopFolder> others)
    {
        var decision = CoopParking.Decide(_settings.SetAsideCoop, _setAsideThisRun,
            () => TaomCoopLauncher.SetAsideCoopWindow.Ask(this, RotCoopInstall.Question(others)));
        _setAsideThisRun = decision.SetAside ? true : null;
        if (decision.Save != null)
        {
            _settings.SetAsideCoop = decision.Save;
            SaveSettings();
        }

        return decision.SetAside;
    }

    // At startup: what a launcher that crashed or was killed left (our Coop in Modules, another one parked) is undone
    // first. While Bannerlord runs it stays, and closing this launcher takes it out then.
    private void TakeOutAfterCrash()
    {
        if (!RotCoopInstall.HasWork(_coopMarker))
            return;
        _actions.Note("Start", "The last run left Coop moved, taking it out.");
        TakeOutCoop();
    }

    // A Coop that stays parked or a marker that could not be read goes under Connect, so the player sees where their Coop is.
    private void TakeOutCoop()
    {
        try
        {
            var result = RotCoopInstall.TakeOut(_coopMarker, GameWaitReason() != null);
            foreach (var line in result.Lines)
                LogCoop(line);
            if (result.Notice != null)
            {
                _actions.Note("Coop", result.Notice);
                ShowJoin(result.Notice);
            }
            if (result.Lines.Count == 0 && result.Notice == null)
                _actions.Note("Coop", "Nothing to take out.");
        }
        catch (Exception ex)
        {
            LogCoop(RotCoopInstall.CouldNotTakeOut(ex.Message));
        }
    }

    // The Coop lines also go to client-action.log, which Send my logs packs, so a report shows where the Coop went.
    private void LogCoop(string line)
    {
        Log(line);
        _actions.Note("Coop", line);
    }

    // Why the close waits (RotCoopInstall.WaitReason): Bannerlord, its own launcher, or a game this launcher started a
    // moment ago that has not exited yet. Null when Coop can come out now.
    private string? GameWaitReason()
    {
        bool? exited = null;
        try
        {
            exited = _startedGame?.HasExited;
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            // A process that can't be looked at keeps the grace.
        }

        var launchers = Process.GetProcessesByName("TaleWorlds.MountAndBlade.Launcher");
        foreach (var launcher in launchers)
            launcher.Dispose();
        return RotCoopInstall.WaitReason(ClientLaunchGuard.IsGameRunning(), CoopProgramRunning(), exited, DateTime.UtcNow - _gameStartedAt,
            launchers.Length > 0);
    }

    // The name of a running program from Modules\Coop\bin (Coop.CrashReporter after a crash or a kill), which holds files
    // there so the move out fails. Only a process whose file is that exe counts; one that can't be read is skipped. The
    // exe list is read once per wait.
    private string? CoopProgramRunning()
    {
        if (_closeWait == null || _coopExes == null)
        {
            _coopExes = [];
            try
            {
                var coopBin = Path.Combine(_gameRoot ?? "", "Modules", RotCoopInstall.FolderName, "bin");
                if (_gameRoot != null && Directory.Exists(coopBin))
                    _coopExes = Directory.GetFiles(coopBin, "*.exe", SearchOption.AllDirectories);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // The take out finds out itself and keeps the marker for the next start.
            }
        }

        string? found = null;
        foreach (var exe in _coopExes)
        {
            var name = Path.GetFileNameWithoutExtension(exe);
            foreach (var process in Process.GetProcessesByName(name))
            {
                using (process)
                {
                    try
                    {
                        if (found == null && string.Equals(Path.GetFullPath(process.MainModule?.FileName ?? ""), Path.GetFullPath(exe), StringComparison.OrdinalIgnoreCase))
                            found = name;
                    }
                    catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException or ArgumentException)
                    {
                        // Started as administrator or already gone: skipped.
                    }
                }
            }
        }

        return found;
    }

    // True when the window can close now. With Bannerlord still running the window waits instead, since the game has our
    // Coop open; once it closes the Coop comes out, the other one goes back and the launcher exits. It waits minimized in
    // the taskbar with its title saying so. Closing it again takes the Coop out when the game is gone by then, and
    // otherwise leaves it for the next start.
    private bool TakeOutOnClose()
    {
        if (_sessionEnding || !RotCoopInstall.HasWork(_coopMarker))
        {
            StopCloseWait();
            return true;
        }
        var reason = GameWaitReason();
        switch (RotCoopInstall.CloseStep(_closingAfterGame, reason))
        {
            case RotCloseStep.TakeOut:
                _actions.Note("Close", _closingAfterGame ? "Closed again while waiting and the game is gone, taking Coop out." : "Closing now, taking Coop out.");
                StopCloseWait();
                TakeOutCoop();
                return true;
            case RotCloseStep.LeaveForNextStart:
                _actions.Note("Close", "Closed again while waiting (" + reason + "), Coop stays in for the next start.");
                StopCloseWait();
                return true;
        }

        _actions.Note("Close", "Waiting for the game to close (" + reason + ").");
        _closingAfterGame = true;
        Title = Translator.T(RotCoopInstall.WaitingTitle);
        ShowJoin(RotCoopInstall.WaitingToTakeOut, problem: false);
        WindowState = WindowState.Minimized;
        _closeWait = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
        _closeWait.Tick += (_, _) =>
        {
            if (GameWaitReason() != null)
                return;
            StopCloseWait();
            _actions.Note("Close", "The game closed, taking Coop out.");
            TakeOutCoop();
            Application.Current.Shutdown();
        };
        _closeWait.Start();
        return false;
    }

    // The timer is dropped once it took the Coop out or a second close came, so _closeWait is set only while the window waits.
    private void StopCloseWait()
    {
        _closeWait?.Stop();
        _closeWait = null;
    }

    // Windows logs off or shuts down: the wait ends and nothing moves, so the game can close in peace. The marker stays,
    // and the next start takes the Coop out.
    private void App_SessionEnding(object? sender, SessionEndingCancelEventArgs e)
    {
        _sessionEnding = true;
        StopCloseWait();
    }
}
