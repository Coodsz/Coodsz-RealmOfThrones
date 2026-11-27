using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using BannerlordCoopLauncher.Core.Keep;
using BannerlordCoopLauncher.Core.Localization;
using TaomCoop.Core;

namespace RealmOfThronesHost;

// The Console page and the Cheats page, laid out like Coodsz Host's: the live server output with
// what you send, the command box with the recent commands, and every Coop command from
// bannerlordcoop.com/cheats with one box per parameter (CoopCommandBrowser, shared with Coodsz
// Host). The Cheats page (CoopCheatsPage) sends Coop's own coop.debug commands only; the server runs
// Coodsz Server Tools for the host rules (RotServerTools), not for cheats.
public partial class MainWindow
{
    private const int MaxRecentCommands = 8;

    private void StartCheats()
    {
        CoopCheats.Configure(
            () => CommandPipeReady() ? "" : CheatBlockReason(),
            RunCommandAndRead,
            Remember,
            () => _settings.FavouriteCheats,
            ids =>
            {
                _settings.FavouriteCheats = ids.ToList();
                WriteSettingsFile();
            });
    }

    private void StartConsole()
    {
        CommandBrowser.Send = command =>
        {
            SendConsoleCommand(command);
            return Task.CompletedTask;
        };
        CommandBrowser.PutInBox = command =>
        {
            CommandBox.Text = command;
            CommandBox.Focus();
            CommandBox.CaretIndex = CommandBox.Text.Length;
        };
        CommandBrowser.GuidePlayer = () => (CurrentPlayerNames().FirstOrDefault(), null);
        ShowRecentCommands();
    }

    /// <summary>
    /// Sends a command from the box or the list. A CLIENT command, or a Coop command with too few
    /// values, is not sent and its usage line is shown instead. The reply shows in the console box.
    /// </summary>
    private async void SendConsoleCommand(string command)
    {
        command = command.Trim();
        if (command.Length == 0)
            return;
        var check = CoopCommandCatalog.CheckLine(command);
        var tokens = CoopArg.Split(command);
        var known = CoopCommandCatalog.Find(tokens.FirstOrDefault());
        // Too many values only warns: some commands join the words of a name with spaces themselves.
        if (check != null && known != null && (known.IsClientOnly || tokens.Count - 1 < known.Parameters.Count(p => !p.Optional)))
        {
            Remember("> " + command);
            Remember(Translator.T(check));
            return;
        }

        try
        {
            if (CommandPipeReady())
                AddRecentCommand(command);
            // The usage warning shows at once, not after the commands queued before this one.
            if (check != null)
                Remember(Translator.T(check));
            // A refused command, save and a server that is not running never reach the pipe; SendCommand says why.
            if (ServerCommandPolicy.Refusal(command) != null || command.Equals("save", StringComparison.OrdinalIgnoreCase) || !CommandPipeReady())
            {
                SendCommand(command);
                return;
            }

            // A time command typed here sets or ends Play hold like the buttons and wins over a join pause. The host's
            // intent is recorded now, in queue order; the command then waits its turn and keeps it until its reply is read
            // (ReadCommandReply shows the reply).
            if (OwnsServerProcess())
                _playHold.HostSent(command);
            _joinPause.HostSent(command);
            var reply = await RunCommandAndRead(command);
            if (reply.Count == 0 && OwnsServerProcess())
                Remember("Sent. The server printed nothing after that command.");
        }
        catch (Exception ex)
        {
            // An async void handler that throws would close the tool.
            Remember(ex.Message);
        }
    }

    private void AddRecentCommand(string command)
    {
        var recent = _settings.RecentCommands;
        recent.RemoveAll(c => string.Equals(c, command, StringComparison.Ordinal));
        recent.Insert(0, command);
        if (recent.Count > MaxRecentCommands)
            recent.RemoveRange(MaxRecentCommands, recent.Count - MaxRecentCommands);
        WriteSettingsFile();
        ShowRecentCommands();
    }

    /// <summary>The recent command chips under the box: a click puts the command back in the box.</summary>
    private void ShowRecentCommands()
    {
        RecentCommands.Children.Clear();
        foreach (var command in _settings.RecentCommands.Take(MaxRecentCommands))
        {
            var text = command;
            var chip = new Button
            {
                Content = text.Length > 60 ? text[..57] + "..." : text,
                Style = (Style)FindResource("Keep.Small"),
                FontFamily = new System.Windows.Media.FontFamily("Consolas"),
                Margin = new Thickness(0, 0, 6, 6),
                ToolTip = text,
            };
            chip.Click += (_, _) =>
            {
                CommandBox.Text = text;
                CommandBox.Focus();
                CommandBox.CaretIndex = text.Length;
            };
            RecentCommands.Children.Add(chip);
        }
    }

    /// <summary>Live server lines, shown in the console box too while Show server output is ticked.</summary>
    private void MirrorServerLines(string lines)
    {
        if (ConsoleServerLines.IsChecked == true && !string.IsNullOrWhiteSpace(lines))
            ColoredConsole.Append(HistoryBox, lines, ConsolePage.Visibility == Visibility.Visible && AutoScroll.IsChecked == true);
    }

    /// <summary>Writes the settings as they are now (the favourites and the recent commands change without Save).</summary>
    private void WriteSettingsFile()
    {
        try
        {
            File.WriteAllText(_settingsPath, JsonSerializer.Serialize(_settings, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }
}
