using System.IO;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Media;
using BannerlordCoopLauncher.Core.Joining;
using BannerlordCoopLauncher.Core.Localization;
using BannerlordCoopLauncher.Core.Steam;
using RealmOfThrones.Core;
using TaomCoop.Core;

namespace RealmOfThronesHost;

// Easy joining, the same as in Coodsz Host: the addresses friends can use, Copy invite, automatic
// router port opening while the server runs, the Windows Firewall rule, and the join guide. The
// logic is in Core\Joining.
public partial class MainWindow
{
    private readonly RouterPortKeeper _routerPort = new();
    private IReadOnlyList<AdapterAddress> _adapters = Array.Empty<AdapterAddress>();
    private FirewallState _firewall = FirewallState.Unknown;
    private string? _steamName;
    private bool _routerTried;
    private bool _routerBusy;
    private DateTime? _notRunningSince;
    private bool _joiningReady;
    private bool _lastRunning;
    private string _joiningShown = "";

    private static Brush JoinGood => Coodsz.Ui.FrameBrushes.Get("Keep.Success", "#8FCB9B");
    private static Brush JoinMuted => Coodsz.Ui.FrameBrushes.Get("Keep.TextDim", "#8EA0AE");
    private static Brush JoinWarn => Coodsz.Ui.FrameBrushes.Get("Keep.Warning", "#E0B15A");

    private const string ClientToolName = "Coodsz Realm of Thrones Client Launcher";
    private const string ServerToolName = "Coodsz Realm of Thrones Server Tool";

    // The listening program is the server engine (engine\dotnet\dotnet.exe in the server folder),
    // which BannerlordCoopServer.exe starts.
    private string? ServerProgram
    {
        get
        {
            if (string.IsNullOrWhiteSpace(_settings.ServerRoot))
                return null;
            var engine = Path.Combine(_settings.ServerRoot, "engine", "dotnet", "dotnet.exe");
            return File.Exists(engine) ? engine : null;
        }
    }

    // The Coop port players join on: "port" in the Data folder's server-config.json, 4200 by default.
    private int JoinPort
    {
        get
        {
            try
            {
                var path = Path.Combine(_settings.DataRoot ?? "", "server-config.json");
                if (File.Exists(path))
                {
                    var match = Regex.Match(File.ReadAllText(path), "\"port\"\\s*:\\s*(\\d{1,5})");
                    if (match.Success && int.TryParse(match.Groups[1].Value, out var port) && port is >= 1 and <= 65535)
                        return port;
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
            }

            return int.TryParse(RotStack.DirectPort, out var fallback) ? fallback : LocalServer.DefaultPort;
        }
    }

    private void StartJoining()
    {
        RouterPortBox.IsChecked = _settings.OpenRouterPort;
        _routerPort.Changed += result => Dispatcher.BeginInvoke(() =>
        {
            SteamLobbyForSharedAddress(result);
            ShowJoining();
        });
        Closed += (_, _) =>
        {
            if (_routerPort.IsOpen)
                _actions.Note("Router port", "Closing on exit");
            _routerPort.CloseNow(TimeSpan.FromSeconds(4));
            _routerPort.Dispose();
        };
        _joiningReady = true;
        _ = DetectJoiningAsync();
    }

    private async Task DetectJoiningAsync()
    {
        var port = JoinPort;
        var program = ServerProgram;
        var (adapters, firewall, steamName) = await Task.Run(() =>
        {
            IReadOnlyList<AdapterAddress> found;
            try
            {
                found = NetworkAdapters.Read();
            }
            catch (Exception)
            {
                found = Array.Empty<AdapterAddress>();
            }

            return (found, FirewallRules.Check(FirewallRules.Read(), port, program), SteamRegistry.TryGetPersonaName());
        });
        _adapters = adapters;
        _firewall = firewall;
        _steamName = steamName;
        ShowJoining();
    }

    private JoinFacts JoinFactsNow() => JoinFactsNow(ServerIsRunning());

    private JoinFacts JoinFactsNow(bool running)
    {
        _lastRunning = running;
        return new JoinFacts
        {
            IsHost = true,
            Port = JoinPort,
            SteamLobbyOn = SteamBox.IsChecked == true,
            SteamName = _steamName,
            PasswordNeeded = !string.IsNullOrWhiteSpace(PasswordBox.Text),
            Adapters = _adapters,
            // Only a mapping that is open now, or the answer to the last try when it failed.
            Router = _routerPort.IsOpen || _routerPort.Last?.IsOpen == false ? _routerPort.Last : null,
            PortOpensOnStart = _settings.OpenRouterPort && !running && _routerPort.Last?.IsOpen != false,
            Firewall = _firewall,
            ClientTool = ClientToolName,
            ServerTool = ServerToolName,
            SteamOption = "Advertise through Steam",
            Taom = false,
            Setup = HostSetup.Name
        };
    }

    private void ShowJoining() => ShowJoining(ServerIsRunning(), force: true);

    /// <summary>Fills the card. From the state clock it only redraws when something changed.</summary>
    private void ShowJoining(bool running, bool force)
    {
        if (!_joiningReady)
            return;
        var facts = JoinFactsNow(running);
        var addresses = JoinPlan.Addresses(facts);
        var shown = string.Join("|", addresses) + "|" + RouterStatusText() + "|" + _firewall + "|" + facts.SteamLobbyOn + "|" + facts.Setup;
        if (!force && shown == _joiningShown)
            return;
        _joiningShown = shown;
        JoinAddressList.ItemsSource = addresses;
        var best = JoinGuide.Choices(facts).FirstOrDefault(choice => choice.WorksNow && choice.Topic != GuideTopic.Host);
        JoinBestText.Text = best == null
            ? "No easy way found yet. Turn on Advertise through Steam, use Radmin VPN, or open the port. Click How will friends join?"
            : "Easiest way right now is " + best.Title + ". " + best.Status;
        JoinBestText.Foreground = best == null ? JoinWarn : JoinGood;

        RouterPortStatus.Text = RouterStatusText();
        RouterPortStatus.Foreground = _routerPort.IsOpen ? JoinGood : _routerPort.Last is { IsOpen: false } ? JoinWarn : JoinMuted;
        FirewallStatus.Text = FirewallRules.Describe(_firewall, facts.Port);
        FirewallStatus.Foreground = _firewall switch
        {
            FirewallState.Allowed => JoinGood,
            FirewallState.Blocked => Coodsz.Ui.FrameBrushes.Get("Keep.Red", "#E8786C"),
            _ => JoinMuted
        };
    }

    private string RouterStatusText()
    {
        if (!_settings.OpenRouterPort)
            return "Off. Friends use Steam lobby, a VPN, or a port you forward by hand.";
        if (_routerBusy)
            return "Opening the port on your router...";
        if (_routerPort.IsOpen && _routerPort.Last != null)
            return _routerPort.Last.Status;
        if (_routerPort.Last is { IsOpen: false } failed)
            return failed.Status;
        if (_routerPort.Last != null && !_lastRunning)
            return "The port was closed on your router because the server stopped.";
        return "The port opens on your router when you start the server.";
    }

    /// <summary>Called from RefreshState: opens the port while the server runs, closes it after it stops.</summary>
    private void KeepRouterPort(bool running)
    {
        if (!_joiningReady)
            return;
        ShowJoining(running, force: false);
        if (_routerBusy)
            return;
        if (running)
        {
            _notRunningSince = null;
            if (_settings.OpenRouterPort && !_routerTried)
                _ = OpenRouterPortAsync();
            return;
        }

        // A restart stops the server for a moment, so wait before closing.
        _notRunningSince ??= DateTime.UtcNow;
        if (DateTime.UtcNow - _notRunningSince.Value < TimeSpan.FromSeconds(10))
            return;
        _routerTried = false;
        if (_routerPort.IsOpen)
            _ = CloseRouterPortAsync("Closed the port on your router because the server stopped.");
    }

    private async Task OpenRouterPortAsync()
    {
        _routerTried = true;
        _routerBusy = true;
        ShowJoining();
        try
        {
            var result = await _routerPort.OpenAsync(JoinPort, RouterPortMapper.DefaultDescription);
            _actions.Note("Router port", result.LogLine);
            NoteLog(result.Status);
        }
        catch (Exception ex)
        {
            _actions.Note("Router port", "Failed: " + ex.Message);
        }
        finally
        {
            _routerBusy = false;
            ShowJoining();
        }
    }

    private async Task CloseRouterPortAsync(string message)
    {
        _routerBusy = true;
        try
        {
            await _routerPort.CloseAsync();
            _actions.Note("Router port", "Closed " + JoinPort + " UDP");
            NoteLog(message);
        }
        finally
        {
            _routerBusy = false;
            ShowJoining();
        }
    }

    private async void RouterPort_Changed(object sender, RoutedEventArgs e)
    {
        if (!_joiningReady)
            return;
        var on = RouterPortBox.IsChecked == true;
        if (_settings.OpenRouterPort != on)
        {
            _settings.OpenRouterPort = on;
            SaveSettings();
        }

        if (!on && _routerPort.IsOpen)
            await CloseRouterPortAsync("Closed the port on your router.");
        else if (on && ServerIsRunning() && !_routerBusy)
            await OpenRouterPortAsync();
        ShowJoining();
    }

    private async void CheckJoining_Click(object sender, RoutedEventArgs e)
    {
        await DetectJoiningAsync();
        if (_settings.OpenRouterPort && ServerIsRunning() && !_routerPort.IsOpen && !_routerBusy)
            await OpenRouterPortAsync();
    }

    private void CopyAddress_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not JoinAddress address)
            return;
        var value = address.Value.Length > 0 ? address.Value : address.Label;
        CopyText(value, address.Kind == JoinAddressKind.SteamLobby ? "Copied." : "Copied " + value + ". The UDP port is " + JoinPort + ".");
    }

    // Advertise through Steam as the running server started with; null when this tool did not start it.
    private bool? _steamAtStart;

    private bool SteamLobbyLive => ServerIsRunning() && _steamAtStart is { } started ? started : SteamBox.IsChecked == true;

    // A shared address (CGNAT) means only Steam lobby reaches the server, so it is the smart default
    // for the next start. No new setting: Advertise through Steam is ticked and saved.
    // Only once, so a host who turns it off again keeps it off.
    private void SteamLobbyForSharedAddress(PortOpenResult? result)
    {
        if (result?.Outcome != PortOpenOutcome.SharedAddress || SteamBox.IsChecked == true || _settings.SteamLobbyAutoOn)
            return;
        if (TurnOnSteamLobby(auto: true))
            NoteLog(JoinPlan.SteamLobbyTurnedOn);
    }

    // Saves only the Steam lobby value (and the once flag), not other unsaved edits on the Settings page.
    // Runs from the router callback and Copy invite, so any failure only gets a log line.
    private bool TurnOnSteamLobby(bool auto = false)
    {
        try
        {
            _settings.SteamRelay = true;
            if (auto)
                _settings.SteamLobbyAutoOn = true;
            File.WriteAllText(_settingsPath, System.Text.Json.JsonSerializer.Serialize(_settings, new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
            SteamBox.IsChecked = true;
            _actions.Note("Steam lobby", "Turned on for a shared address");
            return true;
        }
        catch (Exception ex)
        {
            NoteLog("Could not turn on Steam lobby: " + ex.Message);
            return false;
        }
    }

    private async void CopyInvite_Click(object sender, RoutedEventArgs e)
    {
        await DetectJoiningAsync();
        var facts = JoinFactsNow() with { SteamLobbyOn = SteamLobbyLive };
        // Never a Steam lobby invite while it is off: turn it on first (a running server restarts for it).
        if (JoinPlan.NeedsSteamLobby(facts))
        {
            if (!ServerIsRunning())
            {
                if (TurnOnSteamLobby())
                    facts = facts with { SteamLobbyOn = true };
            }
            // SaveThenStop does nothing while another server action runs, so the prompt only comes when
            // the restart can really happen; otherwise the invite stays without Steam lobby.
            else if (!_busy
                     && ConfirmWindow.Ask(this, JoinPlan.SteamLobbyOffTitle, JoinPlan.SteamLobbyOffBody, okText: JoinPlan.SteamLobbyOffButton)
                     && TurnOnSteamLobby()
                     && !_busy && ServerIsRunning())
            {
                CopyText(JoinPlan.Invite(facts with { SteamLobbyOn = true }, Translator.T), JoinPlan.InviteCopiedRestart);
                _actions.Note("Restart", "Requested restart for Steam lobby");
                await SaveThenStop(restart: true);
                return;
            }
        }

        CopyText(JoinPlan.Invite(facts, Translator.T), JoinPlan.InviteCopied(facts));
    }

    private void CopyText(string text, string done)
    {
        try
        {
            Clipboard.SetText(text);
            SetControl(done);
        }
        catch (System.Runtime.InteropServices.COMException)
        {
            SetControl("Could not copy. Another program is using the clipboard. Try again.", failed: true);
        }
    }

    private async void HowJoin_Click(object sender, RoutedEventArgs e)
    {
        await DetectJoiningAsync();
        Coodsz.Ui.JoinGuideWindow.Show(this, JoinFactsNow());
    }

    private async void Firewall_Click(object sender, RoutedEventArgs e)
    {
        var port = JoinPort;
        var program = ServerProgram;
        _firewall = await Task.Run(() => FirewallRules.Check(FirewallRules.Read(), port, program));
        if (_firewall == FirewallState.Allowed)
        {
            ShowJoining();
            SetControl(FirewallRules.Describe(_firewall, port));
            return;
        }

        var body = "Windows asks for permission, then adds an inbound rule that lets friends reach the server on UDP " + port + "."
            + (program != null ? " A second rule allows the server program." : "")
            + " Nothing else changes.";
        if (!ConfirmWindow.Ask(this, "Allow through firewall", body, null, "Continue"))
            return;
        FirewallButton.IsEnabled = false;
        try
        {
            var outcome = await Task.Run(() => FirewallRules.Add(port, program));
            _actions.Note("Firewall", outcome + " UDP " + port + (program != null ? " and " + program : ""));
            _firewall = await Task.Run(() => FirewallRules.Check(FirewallRules.Read(), port, program));
            var message = FirewallRules.Describe(outcome) + (outcome == FirewallAddOutcome.Added ? " " + FirewallRules.Describe(_firewall, port) : "");
            SetControl(message, failed: outcome == FirewallAddOutcome.Failed);
            NoteLog(message);
        }
        finally
        {
            FirewallButton.IsEnabled = true;
            ShowJoining();
        }
    }
}
