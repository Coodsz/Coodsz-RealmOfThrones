using System.IO;
using System.Windows;
using System.Windows.Media;
using BannerlordCoopLauncher.Core.Configuration;
using RealmOfThrones.Core;
using TaomCoop.Core;

namespace RealmOfThronesHost;

// Game speed on the Dashboard. The buttons tick the Settings page's own Game speed boxes and
// run its Save settings, so 2x speed has one way into mod-config.json (fastForwardEnabled,
// speedLimitWhilePlayersInBattle).
public partial class MainWindow
{
    // What mod-config.json says, and what the running server started with.
    private GameSpeedState _savedSpeed = new(FastForward: true, BattleSpeedLimit: true);
    private GameSpeedState? _speedAtStart;
    private string _speedError = "";
    private bool _speedRunning;

    private GameSpeedState SavedGameSpeed()
    {
        var path = ModConfigPath;
        try
        {
            var json = File.Exists(path) ? File.ReadAllText(path) : "";
            return new GameSpeedState(CoopModConfig.ReadBool(json, "fastForwardEnabled"), CoopModConfig.ReadBool(json, "speedLimitWhilePlayersInBattle"));
        }
        catch (Exception ex) when (ex is InvalidOperationException or IOException or UnauthorizedAccessException)
        {
            return GameSpeed.From(ModSettingsCatalog.Defaults());
        }
    }

    private void RefreshGameSpeed()
    {
        _savedSpeed = SavedGameSpeed();
        _speedError = "";
        GameSpeedLine.Text = GameSpeed.SpeedLine(_savedSpeed);
        // Off is Keep.Warning, amber in the Realm of Thrones colours; GoldInk is green there like Success.
        GameSpeedLine.Foreground = (_savedSpeed.TwoTimesOn ? Coodsz.Ui.FrameBrushes.Get("Keep.Success", "#8FBF86") : Coodsz.Ui.FrameBrushes.Get("Keep.Warning", "#E2C27A"));
        GameSpeedBattleLine.Text = GameSpeed.BattleLine(_savedSpeed);
        GameSpeedButton.Content = GameSpeed.SpeedButton(_savedSpeed);
        GameSpeedBattleButton.Content = GameSpeed.BattleButton(_savedSpeed);
        ShowGameSpeedRestart(_speedRunning);
    }

    // Runs with every state refresh, so Restart server shows only while a server runs.
    private void ShowGameSpeedRestart(bool running)
    {
        _speedRunning = running;
        GameSpeedRestartLine.Text = _speedError.Length > 0 ? _speedError : GameSpeed.RestartLine(running, _speedAtStart, _savedSpeed);
        GameSpeedRestartButton.Visibility = running ? Visibility.Visible : Visibility.Collapsed;
    }

    private void GameSpeed_Click(object sender, RoutedEventArgs e) =>
        SaveGameSpeed(GameSpeed.WithTwoTimes(_savedSpeed, !_savedSpeed.TwoTimesOn));

    private void GameSpeedBattle_Click(object sender, RoutedEventArgs e) =>
        SaveGameSpeed(GameSpeed.WithBattleLimit(_savedSpeed, !_savedSpeed.BattleSpeedLimit));

    private void SaveGameSpeed(GameSpeedState wanted)
    {
        var before = _savedSpeed;
        var shown = (FastForwardBox.IsChecked, BattleSpeedLimitBox.IsChecked);
        FastForwardBox.IsChecked = wanted.FastForward;
        BattleSpeedLimitBox.IsChecked = wanted.BattleSpeedLimit;
        if (!SaveSettingsPage(fromSettingsPage: false))
        {
            // Put the Settings page back as it was; the reason is in the status line, shown here too.
            FastForwardBox.IsChecked = shown.Item1;
            BattleSpeedLimitBox.IsChecked = shown.Item2;
            _speedError = Coodsz.Ui.UiLanguage.English(ControlStatus);
            ShowGameSpeedRestart(_speedRunning);
            return;
        }

        // A server that was already running when this tool opened started with what was saved before.
        if (_speedRunning)
            _speedAtStart ??= before;
        RefreshGameSpeed();
    }
}
