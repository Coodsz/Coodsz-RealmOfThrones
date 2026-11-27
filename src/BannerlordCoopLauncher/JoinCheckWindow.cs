using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using BannerlordCoopLauncher.Core.Joining;

namespace Coodsz.Ui;

/// <summary>
/// The prompt Connect shows in Direct mode when the server did not answer (<see cref="JoinPreflight"/>).
/// Use Steam lobby is the main button (it only switches mode, so Enter is safe), Cancel starts nothing,
/// and Connect anyway stays as a quiet link, since the check can be wrong. The body scrolls, so long
/// German or Russian text at 150% on a small screen is never cut off.
/// </summary>
public sealed class JoinCheckWindow : Window
{
    public const string SteamButton = "Use Steam lobby";
    public const string AnywayButton = "Connect anyway";
    public const string CancelButton = "Cancel";
    public const string SteamTip = "Switches to Steam lobby. Type the host's Steam name, then click Connect.";
    public const string AnywayTip = "Starts the game with this address anyway. The check can be wrong.";
    public const string CancelTip = "Closes this. Nothing is changed and the game does not start.";

    private JoinCheckChoice _choice = JoinCheckChoice.Cancel;

    private static readonly Brush Ivory = BrushOf(213, 222, 230);
    private static readonly Brush Muted = BrushOf(142, 160, 174);
    private static readonly Brush Warning = BrushOf(224, 177, 90);
    private static readonly Brush Frame = BrushOf(58, 76, 92);

    private JoinCheckWindow(JoinCheckResult result)
    {
        Title = result.Title;
        Width = 560;
        MaxWidth = Math.Max(320, SystemParameters.WorkArea.Width - 40);
        MaxHeight = Math.Max(240, SystemParameters.WorkArea.Height - 40);
        SizeToContent = SizeToContent.Height;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        WindowPlacement.KeepOnScreen(this);
        ResizeMode = ResizeMode.NoResize;
        WindowStyle = WindowStyle.None;
        ShowInTaskbar = false;
        Background = BrushOf(21, 29, 36);
        BorderBrush = Warning;
        BorderThickness = new Thickness(3, 1, 1, 1);
        Foreground = Ivory;
        FontFamily = new FontFamily("Segoe UI");

        var layout = new DockPanel { Margin = new Thickness(20, 16, 20, 16), LastChildFill = true };
        var title = new TextBlock
        {
            Text = Title,
            FontFamily = new FontFamily("Palatino Linotype"),
            FontSize = 21,
            TextWrapping = TextWrapping.Wrap,
            Foreground = Ivory,
            Margin = new Thickness(0, 0, 0, 6)
        };
        DockPanel.SetDock(title, Dock.Top);
        layout.Children.Add(title);

        var buttons = new WrapPanel { Margin = new Thickness(0, 16, 0, 0) };
        var steam = new Button
        {
            Content = SteamButton,
            ToolTip = SteamTip,
            Padding = new Thickness(18, 8, 18, 8),
            Margin = new Thickness(0, 0, 12, 6),
            FontSize = 14,
            FontWeight = FontWeights.SemiBold,
            Cursor = Cursors.Hand,
            Background = BrushOf(31, 107, 58),
            Foreground = BrushOf(244, 251, 246),
            BorderBrush = BrushOf(143, 203, 155),
            IsDefault = true
        };
        steam.Click += (_, _) => Close(JoinCheckChoice.SteamLobby);
        buttons.Children.Add(steam);

        var cancel = new Button
        {
            Content = CancelButton,
            ToolTip = CancelTip,
            Padding = new Thickness(18, 8, 18, 8),
            Margin = new Thickness(0, 0, 18, 6),
            FontSize = 14,
            Cursor = Cursors.Hand,
            Background = BrushOf(27, 40, 51),
            Foreground = Ivory,
            BorderBrush = Frame,
            BorderThickness = new Thickness(1),
            IsCancel = true
        };
        cancel.Click += (_, _) => Close(JoinCheckChoice.Cancel);
        buttons.Children.Add(cancel);

        var anyway = Link(AnywayButton, AnywayTip, Muted);
        anyway.Margin = new Thickness(0, 0, 0, 6);
        anyway.Click += (_, _) => Close(JoinCheckChoice.Connect);
        buttons.Children.Add(anyway);
        DockPanel.SetDock(buttons, Dock.Bottom);
        layout.Children.Add(buttons);

        layout.Children.Add(new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Content = new TextBlock { Text = result.Message, FontSize = 14, TextWrapping = TextWrapping.Wrap, Foreground = Ivory }
        });

        Content = layout;
        MouseLeftButtonDown += (_, _) =>
        {
            try { DragMove(); }
            catch (InvalidOperationException) { }
        };
    }

    private void Close(JoinCheckChoice choice)
    {
        _choice = choice;
        DialogResult = choice != JoinCheckChoice.Cancel;
    }

    /// <summary>Shows the prompt and returns what the player picked; Connect when the server answered.</summary>
    public static JoinCheckChoice Ask(Window owner, JoinCheckResult result)
    {
        if (result.Outcome == JoinCheckOutcome.Connect)
            return JoinCheckChoice.Connect;
        var window = new JoinCheckWindow(result) { Owner = owner };
        window.ShowDialog();
        return window._choice;
    }

    private static Button Link(string text, string tip, Brush foreground)
    {
        var button = new Button
        {
            Content = text,
            ToolTip = tip,
            Foreground = foreground,
            FontSize = 13.5,
            Cursor = Cursors.Hand,
            VerticalAlignment = VerticalAlignment.Center
        };
        var presenter = new FrameworkElementFactory(typeof(ContentPresenter));
        presenter.SetValue(TextElement.ForegroundProperty, foreground);
        button.Template = new ControlTemplate(typeof(Button)) { VisualTree = presenter };
        return button;
    }

    private static SolidColorBrush BrushOf(byte r, byte g, byte b)
    {
        var brush = Coodsz.Ui.ToolPalette.Brush(r, g, b);
        brush.Freeze();
        return brush;
    }
}
