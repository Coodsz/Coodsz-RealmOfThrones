using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using TaomCoop.Core;

namespace TaomCoopLauncher;

/// <summary>
/// The question before the launcher sets another Coop aside (CoopParking): Set aside and play moves it out of Modules
/// until the launcher closes, Cancel leaves it, and Don't ask again keeps the answer in the launcher's settings.
/// </summary>
public sealed class SetAsideCoopWindow : Window
{
    private bool _setAside;
    private readonly CheckBox _dontAsk;

    private static readonly Brush Ivory = BrushOf(213, 222, 230);
    private static readonly Brush Muted = BrushOf(142, 160, 174);
    private static readonly Brush Gold = BrushOf(226, 194, 122);
    private static readonly Brush Frame = BrushOf(58, 76, 92);

    private SetAsideCoopWindow(string question)
    {
        Title = CoopParking.AskTitle;
        Width = 520;
        MaxWidth = Math.Max(320, SystemParameters.WorkArea.Width - 40);
        MaxHeight = Math.Max(240, SystemParameters.WorkArea.Height - 40);
        SizeToContent = SizeToContent.Height;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Coodsz.Ui.WindowPlacement.KeepOnScreen(this);
        ResizeMode = ResizeMode.NoResize;
        WindowStyle = WindowStyle.None;
        ShowInTaskbar = false;
        Background = BrushOf(21, 29, 36);
        BorderBrush = Gold;
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
        var setAside = new Button
        {
            Content = CoopParking.SetAsideButton,
            ToolTip = CoopParking.SetAsideTip,
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
        setAside.Click += (_, _) => Answer(true);
        buttons.Children.Add(setAside);

        var cancel = new Button
        {
            Content = CoopParking.CancelButton,
            ToolTip = CoopParking.CancelTip,
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
        cancel.Click += (_, _) => Answer(false);
        buttons.Children.Add(cancel);

        _dontAsk = new CheckBox
        {
            Content = CoopParking.DontAskBox,
            ToolTip = CoopParking.DontAskTip,
            Foreground = Muted,
            FontSize = 13.5,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 0, 6)
        };
        buttons.Children.Add(_dontAsk);
        DockPanel.SetDock(buttons, Dock.Bottom);
        layout.Children.Add(buttons);

        layout.Children.Add(new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Content = new TextBlock { Text = question, FontSize = 14, TextWrapping = TextWrapping.Wrap, Foreground = Ivory }
        });

        Content = layout;
        MouseLeftButtonDown += (_, _) =>
        {
            try { DragMove(); }
            catch (InvalidOperationException) { }
        };
    }

    private void Answer(bool setAside)
    {
        _setAside = setAside;
        DialogResult = setAside;
    }

    /// <summary>Shows the question. Closing it any other way counts as Cancel.</summary>
    public static (bool SetAside, bool DontAskAgain) Ask(Window owner, string question)
    {
        var window = new SetAsideCoopWindow(question) { Owner = owner };
        window.ShowDialog();
        return (window._setAside, window._dontAsk.IsChecked == true);
    }

    private static SolidColorBrush BrushOf(byte r, byte g, byte b)
    {
        var brush = new SolidColorBrush(Color.FromRgb(r, g, b));
        brush.Freeze();
        return brush;
    }
}
