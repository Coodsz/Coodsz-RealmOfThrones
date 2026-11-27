using System.IO;
using System.Windows;
using BannerlordCoopLauncher.Core.IO;
using BannerlordCoopLauncher.Core.Launch;
using BannerlordCoopLauncher.Core.Support;
using RealmOfThrones.Core;
using Coodsz.Ui;
using TaomCoop.Core;

namespace RealmOfThronesHost;

public partial class MainWindow
{
    // Fix antivirus in the server controls. It lists this PC's folders and opens Windows Security, nothing more.
    private void FixAntivirus_Click(object sender, RoutedEventArgs e) =>
        AntivirusWindow.Show(this, AntivirusFolders.For(AppContext.BaseDirectory, ToolData.Current?.ProductFolder,
            _settings.ServerRoot, FindGameRoot(), AntivirusFiles.WorkshopCoop(BannerlordFinder.SteamLibraries())));

    // The Realm of Thrones server runs from the Coop server folder the host picked, with no Workshop copy, so
    // there is no server file list to remember: Start server checks the server program itself (ServerFolderProblem).
}
