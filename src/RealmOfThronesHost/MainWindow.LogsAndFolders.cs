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
using BannerlordCoopLauncher.Core.Support;
using BannerlordCoopLauncher.Core.Updates;
using Microsoft.Win32;
using RealmOfThrones.Core;
using TaomCoop.Core;

namespace RealmOfThronesHost;

public partial class MainWindow
{
    private void OpenFolder_Click(object sender, RoutedEventArgs e)
    {
        if (Directory.Exists(_settings.ServerRoot))
            Process.Start(new ProcessStartInfo("explorer.exe", _settings.ServerRoot) { UseShellExecute = true });
    }

    private void ClearConsole_Click(object sender, RoutedEventArgs e) => ColoredConsole.Clear(HistoryBox);

    private void CopyConsole_Click(object sender, RoutedEventArgs e)
    {
        var text = ColoredConsole.Read(HistoryBox);
        if (text.Length > 0)
            Clipboard.SetText(text);
    }

    private void OpenLogs_Click(object sender, RoutedEventArgs e)
    {
        var logs = Path.Combine(_settings.DataRoot, "logs");
        Directory.CreateDirectory(logs);
        _actions.Note("Logs", "Opened log folder. Send the highlighted dated host-action-*.log copy (not the plain host-action.log).");
        LogFolderOpen.Open(logs);
        ShowPage("Logs");
    }

    private void Unblock_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(_settings.ServerRoot) || !Directory.Exists(_settings.ServerRoot))
        {
            SetControl("Prepare the server before unblocking DLLs.", failed: true);
            return;
        }

        try
        {
            var cleared = DownloadMark.Clear(_settings.ServerRoot);
            StopUnblockAlert();
            _actions.Note("Unblock", cleared == 0
                ? "No blocked files in the server folder."
                : "Cleared " + cleared + " blocked file(s) in the server folder.");
            SetControl(cleared == 0
                ? "No files in the server folder were blocked."
                : "Cleared the download block on " + cleared + " files in the server folder.");
        }
        catch (Exception ex)
        {
            SetControl(ex.Message, failed: true);
        }
    }
}
