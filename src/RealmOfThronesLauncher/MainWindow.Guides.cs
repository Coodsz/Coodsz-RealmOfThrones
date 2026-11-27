using System.Windows;
using BannerlordCoopLauncher.Core.Keep;

namespace RealmOfThronesLauncher;

public partial class MainWindow
{
    /// <summary>
    /// Guides in the sidebar opens the launcher's own help pages as the sixth page, and Back goes to Play.
    /// </summary>
    private void Guides_Click(object sender, RoutedEventArgs e)
    {
        GuidesPage.Content ??= new Coodsz.Ui.TaomGuidesPage(GuideLibrary.For(GuideSet.RotClient),
            "Short help for joining Realm of Thrones with this launcher. Pick a guide, or search.", CloseGuides);
        ShowPage("Guides");
    }

    private void CloseGuides() => ShowPage("Play");
}
