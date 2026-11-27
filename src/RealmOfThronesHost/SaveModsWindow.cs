using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using BannerlordCoopLauncher.Core.Modules;
using Coodsz.Ui;

namespace RealmOfThronesHost;

/// <summary>
/// The box Start server shows when the save needs mods this setup leaves out: Coodsz Host's save mods
/// card (CoodszHost\Shared\SaveModsCard.xaml) in this tool's colours. It returns the button pressed
/// (fix, othersave), or an empty string for Dismiss and Escape. Start anyway stays hidden, as in Coodsz Host.
/// A save that needs a mod the server leaves out cannot start, so Start anyway stays hidden.
/// </summary>
public sealed class SaveModsWindow : Window
{
    private string _choice = "";

    private SaveModsWindow(SaveModCard card)
    {
        Title = card.Title;
        SizeToContent = SizeToContent.Height;
        Width = 620;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        WindowPlacement.KeepOnScreen(this);
        ResizeMode = ResizeMode.NoResize;
        WindowStyle = WindowStyle.None;
        ShowInTaskbar = false;
        Background = (Brush)Application.Current.FindResource("Keep.BgDeep");
        BorderBrush = (Brush)Application.Current.FindResource("Keep.Frame");
        BorderThickness = new Thickness(1);
        FontFamily = new FontFamily("Segoe UI");
        var view = new SaveModsCard { ShowStartAnyway = false, Margin = new Thickness(12) };
        view.Show(card);
        view.Command += command =>
        {
            _choice = command == "dismiss" ? "" : command;
            DialogResult = _choice.Length > 0;
        };
        Content = view;
        PreviewKeyDown += (_, e) =>
        {
            if (e.Key != Key.Escape)
                return;
            DialogResult = false;
            e.Handled = true;
        };
    }

    public static string Ask(Window owner, SaveModCard card)
    {
        var window = new SaveModsWindow(card) { Owner = owner };
        window.ShowDialog();
        return window._choice;
    }
}
