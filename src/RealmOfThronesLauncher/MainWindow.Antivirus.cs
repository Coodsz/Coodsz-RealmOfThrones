using System.IO;
using System.Windows;
using BannerlordCoopLauncher.Core.IO;
using BannerlordCoopLauncher.Core.Support;
using Coodsz.Ui;
using TaomCoop.Core;

namespace RealmOfThronesLauncher;

public partial class MainWindow
{
    // Fix antivirus on the Logs and help page. It lists this PC's folders and opens Windows Security, nothing more.
    private void FixAntivirus_Click(object sender, RoutedEventArgs e) =>
        AntivirusWindow.Show(this, AntivirusFolders.For(AppContext.BaseDirectory, ToolData.Current?.ProductFolder,
            null, _gameRoot, AntivirusFiles.WorkshopCoop(BannerlordFinder.SteamLibraries())));
}
