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
using BannerlordCoopLauncher.Core.Launch;
using BannerlordCoopLauncher.Core.Modules;
using BannerlordCoopLauncher.Core.Saves;
using BannerlordCoopLauncher.Core.Server;
using BannerlordCoopLauncher.Core.Support;
using BannerlordCoopLauncher.Core.Updates;
using Microsoft.Win32;
using RealmOfThrones.Core;
using TaomCoop.Core;

namespace RealmOfThronesHost;

public partial class MainWindow
{
    private string? _startupProgress;

    private void RefreshState()
    {
        if (_saving)
        {
            ShowSaving();
            ShowTimeButtons(false);
            return;
        }

        var running = TryServerUptime(out var uptime);
        ShowGameSpeedRestart(running);
        KeepRouterPort(running);
        var log = ReadNewestLog();
        if (!running)
        {
            _seenServing = false;
            _servingCheckedPath = "";
            _servingScanOffset = 0;
            _startupProgress = null;
        }
        // The @DS@ serving state is the server's own ready signal; the real SERVING line (not
        // the start banner) is kept for servers that do not send it.
        else if (ServerStateEvent.HasServingLine(log) || ServerStateEvent.Last(log) is { Serving: true })
            _seenServing = true;
        else if (!_seenServing && _cachedLogPath.Length > 0)
            _seenServing = ScanServing(_cachedLogPath);

        // The server's own state events decide: after a fatal one it is never Serving, and the
        // error stays on screen after the server exits, until the next start.
        var state = running ? ServerStateEvent.Last(log) : null;
        var fatal = _fatalState ?? (state is { Fatal: true } ? state : null);
        var serving = running && _seenServing && fatal == null && (state == null || state.Serving);
        ShowTimeButtons(serving);
        ShowFirewallHint(serving);
        // The start note ("Server is starting...") stayed up after Serving, so players pressed Start again.
        // The English note SetControl wrote is checked, so a translated note is swapped too.
        if (serving && (ServerReadyNote.Replaces(_controlNote) || ServerReadyNote.Replaces(Coodsz.Ui.UiLanguage.English(ControlStatus))))
        {
            _controlNote = ServerReadyNote.Ready;
            ControlStatus.Text = ServerReadyNote.Ready;
            ControlStatus.Foreground = Coodsz.Ui.FrameBrushes.Get("Keep.Teal", "#7DAAA4");
            ShowOnServerPage(ServerReadyNote.Ready, failed: false);
        }

        if ((fatal != null || !running || serving) && !_makingMap && !_makingServer)
            HideStartProgress();

        if (fatal != null)
        {
            StateText.Text = "FAILED";
            StateDetail.Text = ServerStateEvent.FatalStatus(fatal);
            UptimeText.Text = running ? "Stopping after an error." : "Stopped after an error.";
            StatusDot.Fill = Coodsz.Ui.FrameBrushes.Get("Keep.Red", "#E8786C");
        }
        else if (!running)
        {
            StateText.Text = "STOPPED";
            StateDetail.Text = "The server is stopped.";
            UptimeText.Text = "Stopped.";
            StatusDot.Fill = new SolidColorBrush(Color.FromRgb(176, 132, 72));
        }
        else if (serving)
        {
            StateText.Text = "SERVER ONLINE";
            StateDetail.Text = UptimeSentence(uptime) + "Friends connect on UDP " + JoinPort + ".";
            UptimeText.Text = "Serving. " + UptimeSentence(uptime).Trim();
            StatusDot.Fill = new SolidColorBrush(Color.FromRgb(90, 138, 96));
        }
        else
        {
            // The log tail moves on between refreshes, so keep the newest map-cache step seen.
            _startupProgress = StartupProgress.Describe(log) ?? _startupProgress;
            ShowStartProgress(StartupProgress.Measure(log));
            StateText.Text = "RUNNING";
            StateDetail.Text = UptimeSentence(uptime) + (_startupProgress ?? "The campaign is still loading.");
            UptimeText.Text = "Running. " + UptimeSentence(uptime).Trim();
            StatusDot.Fill = new SolidColorBrush(Color.FromRgb(176, 132, 72));
        }

        ShowFrame(running, serving, fatal != null);

        var load = _load.Line(running, _settings.ServerRoot, OwnsServerProcess() ? _process?.Id : null);
        ServerLoadText.Text = load;
        ServerLoadText.Visibility = load.Length > 0 ? Visibility.Visible : Visibility.Collapsed;

        LoadPlayerList();
        NoteJoinPause(_onlinePlayers, serving);
        FollowServerLog();
        KeepLastGoodCopy();
        HoldBrokenAutosave();
    }

    // The gold bar from the updater, shown while the server starts. The log tail moves on between
    // refreshes, so the bar never goes back; a number once seen stays until the server stops.
    private double? _startFraction;

    private void ShowStartProgress(StartupStep? step)
    {
        if (step?.Fraction is double fraction && fraction > (_startFraction ?? 0))
            _startFraction = fraction;
        foreach (var bar in new[] { StartProgress, StartTopProgress })
        {
            bar.IsIndeterminate = _startFraction == null;
            bar.Value = _startFraction ?? 0;
        }

        StartProgressText.Text = _startFraction is double value ? (int)(value * 100) + "%" : "";
        StartProgressRow.Visibility = Visibility.Visible;
        StartTopProgress.Visibility = Visibility.Visible;
    }

    private void HideStartProgress()
    {
        _startFraction = null;
        StartProgress.IsIndeterminate = false;
        StartTopProgress.IsIndeterminate = false;
        StartProgressRow.Visibility = Visibility.Collapsed;
        StartTopProgress.Visibility = Visibility.Collapsed;
    }

    private void HoldBrokenAutosave()
    {
        if (_spareUntilOnMap)
        {
            if (BrokenWorldRefusal() == null)
                _spareUntilOnMap = false;
            return;
        }

        if (_autosaveHeld || _busy || _process is not { HasExited: false })
            return;
        if (!BrokenWorldGuard.StopOwnedServer(ownsProcess: true, alreadyHeld: false, spareUntilOnMap: false, RecentWorldLog(out _)))
            return;
        _autosaveHeld = true;
        StopServer();
        SetControl(BrokenWorldGuard.AutosaveStoppedText, failed: true);
    }

    // When this run started, so the logs of an older run are not read as this run's (UTC).
    private DateTime? _runStartedUtc;

    private bool TryServerUptime(out string uptime)
    {
        uptime = "";
        // The server this window started has exited: it is stopped, even while the lock file or
        // a closing process still looks like a server. Then it can never show Serving.
        if (_process is { HasExited: true } && !DedicatedServerWatch.ProcessRunning(_settings.ServerRoot))
            return false;
        if (!DedicatedServerWatch.TryStarted(_settings.ServerRoot, out var found))
            return false;
        // A lock file left by an older run must not give a new start that run's uptime.
        var started = RotServerNotes.UptimeFrom(found, _runStartedUtc);
        if (started != null)
        {
            uptime = FormatUptime(DateTime.Now - started.Value);
            // A server this window did not start (the tool was opened again): its own start time. Restarted by someone else
            // since, it is a new run: its start time and players replace the old ones.
            var foreignStart = started.Value.ToUniversalTime().AddSeconds(-5);
            if (!OwnsServerProcess() && _runStartedUtc is { } known && foreignStart > known.AddSeconds(30))
            {
                _runStartedUtc = foreignStart;
                ForgetPlayers();
            }
            _runStartedUtc ??= foreignStart;
        }

        return true;
    }

    // The .NET Host hint on the Start server card stays until this server folder's server once
    // served; by then Windows has asked. Only that one value is written, not the boxes on screen.
    private void ShowFirewallHint(bool serving)
    {
        if (serving && RotServerNotes.FirewallHintShows(_settings.FirewallHintDoneFor, _settings.ServerRoot, _firewall == BannerlordCoopLauncher.Core.Joining.FirewallState.Allowed))
        {
            _settings.FirewallHintDoneFor = _settings.ServerRoot;
            try
            {
                File.WriteAllText(_settingsPath, JsonSerializer.Serialize(_settings, new JsonSerializerOptions { WriteIndented = true }));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
            }
        }

        FirewallHintText.Visibility = RotServerNotes.FirewallHintShows(_settings.FirewallHintDoneFor, _settings.ServerRoot, _firewall == BannerlordCoopLauncher.Core.Joining.FirewallState.Allowed) ? Visibility.Visible : Visibility.Collapsed;
    }

    private static string UptimeSentence(string uptime) =>
        string.IsNullOrEmpty(uptime) ? "" : "Up for " + uptime + ". ";

    private static string FormatUptime(TimeSpan elapsed)
    {
        if (elapsed < TimeSpan.Zero)
            elapsed = TimeSpan.Zero;
        return (int)elapsed.TotalHours + elapsed.ToString(@"\:mm\:ss");
    }
}
