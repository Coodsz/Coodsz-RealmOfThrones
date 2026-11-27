using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using BannerlordCoopLauncher.Core.Joining;

namespace Coodsz.Ui;

/// <summary>
/// The one prompt Connect shows when the server needs mods this PC does not have turned on
/// (<see cref="ServerModPrompt"/>): the count, the mods as chips with +N more like the save mods card,
/// the ones the launcher can not get by name with Get it, and one main button. Ask returns true when
/// the main button was clicked; Cancel and Escape return false.
/// </summary>
public sealed class ServerModsWindow : Window
{
    private readonly ServerModPrompt _prompt;
    private readonly WrapPanel _chips = new() { Margin = new Thickness(0, 0, 0, 4) };
    private bool _expanded;
    private bool _chosen;

    private static readonly Brush Ivory = BrushOf(213, 222, 230);
    private static readonly Brush Muted = BrushOf(142, 160, 174);
    private static readonly Brush Gold = BrushOf(226, 194, 122);
    private static readonly Brush Warning = BrushOf(224, 177, 90);
    private static readonly Brush Frame = BrushOf(58, 76, 92);

    private ServerModsWindow(ServerModPrompt prompt)
    {
        _prompt = prompt;
        Title = prompt.Title;
        Width = 600;
        SizeToContent = SizeToContent.Height;
        MaxHeight = Math.Max(420, SystemParameters.WorkArea.Height - 40);
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

        var stack = new StackPanel { Margin = new Thickness(20, 16, 20, 16) };
        stack.Children.Add(new TextBlock
        {
            Text = prompt.Title,
            FontFamily = new FontFamily("Palatino Linotype"),
            FontSize = 21,
            TextWrapping = TextWrapping.Wrap,
            Foreground = Ivory,
            Margin = new Thickness(0, 0, 0, 4)
        });
        stack.Children.Add(new TextBlock { Text = prompt.Why, FontSize = 13.5, TextWrapping = TextWrapping.Wrap, Foreground = Muted, Margin = new Thickness(0, 0, 0, 12) });
        stack.Children.Add(_chips);
        FillChips();

        if (prompt.ByHand.Count > 0)
        {
            stack.Children.Add(new TextBlock { Text = prompt.ByHandNote, FontSize = 13, TextWrapping = TextWrapping.Wrap, Foreground = Warning, Margin = new Thickness(0, 10, 0, 4) });
            foreach (var need in prompt.ByHand)
                stack.Children.Add(ByHandRow(need));
        }

        var buttons = new WrapPanel { Margin = new Thickness(0, 14, 0, 0) };
        if (prompt.MainButton.Length > 0)
        {
            var main = new Button
            {
                Content = prompt.MainButton,
                ToolTip = prompt.MainTip,
                Padding = new Thickness(18, 8, 18, 8),
                Margin = new Thickness(0, 0, 18, 0),
                FontSize = 14,
                FontWeight = FontWeights.SemiBold,
                Cursor = Cursors.Hand,
                Background = BrushOf(31, 107, 58),
                Foreground = BrushOf(244, 251, 246),
                BorderBrush = BrushOf(143, 203, 155),
                IsDefault = true
            };
            main.Click += (_, _) =>
            {
                _chosen = true;
                DialogResult = true;
            };
            buttons.Children.Add(main);
        }

        var cancel = Link(ServerModPrompt.CancelButton, ServerModPrompt.CancelTip, Muted);
        cancel.Click += (_, _) => DialogResult = false;
        buttons.Children.Add(cancel);
        stack.Children.Add(buttons);

        Content = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = stack };
        MouseLeftButtonDown += (_, args) =>
        {
            if (args.OriginalSource is DependencyObject source && FindButton(source) != null)
                return;
            try { DragMove(); }
            catch (InvalidOperationException) { }
        };
        PreviewKeyDown += (_, e) =>
        {
            if (e.Key != Key.Escape)
                return;
            DialogResult = false;
            e.Handled = true;
        };
    }

    /// <summary>Shows the prompt. True when the player clicked the main button.</summary>
    public static bool Ask(Window owner, ServerModPrompt prompt)
    {
        var window = new ServerModsWindow(prompt) { Owner = owner };
        window.ShowDialog();
        return window._chosen;
    }

    private void FillChips()
    {
        _chips.Children.Clear();
        foreach (var need in _prompt.Shown(_expanded))
            _chips.Children.Add(Chip(need));
        if (!_prompt.HasMore)
            return;
        var more = Link(_prompt.MoreButton(_expanded), _prompt.MoreTip(_expanded), Gold);
        more.Margin = new Thickness(4, 4, 0, 4);
        more.VerticalAlignment = VerticalAlignment.Center;
        more.Click += (_, _) =>
        {
            _expanded = !_expanded;
            FillChips();
        };
        _chips.Children.Add(more);
    }

    private static Border Chip(ServerModNeed need) => new()
    {
        Background = BrushOf(27, 40, 51),
        BorderBrush = need.Automatic ? Frame : Warning,
        BorderThickness = new Thickness(1),
        CornerRadius = new CornerRadius(12),
        Padding = new Thickness(10, 3, 10, 4),
        Margin = new Thickness(0, 0, 6, 6),
        ToolTip = need.Tip,
        Child = new TextBlock { Text = need.Name, FontSize = 13, Foreground = Ivory }
    };

    private static DockPanel ByHandRow(ServerModNeed need)
    {
        var row = new DockPanel { Margin = new Thickness(0, 2, 0, 2), LastChildFill = true };
        if (!string.IsNullOrWhiteSpace(need.Link))
        {
            var link = need.Link!;
            var get = new Button
            {
                Content = ServerModPrompt.GetItButton,
                ToolTip = link,
                Padding = new Thickness(12, 3, 12, 4),
                Margin = new Thickness(10, 0, 0, 0),
                FontSize = 13,
                Cursor = Cursors.Hand,
                Background = BrushOf(27, 40, 51),
                Foreground = Ivory,
                BorderBrush = Frame
            };
            get.Click += (_, _) =>
            {
                try { Process.Start(new ProcessStartInfo(link) { UseShellExecute = true }); }
                catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception) { }
            };
            DockPanel.SetDock(get, Dock.Right);
            row.Children.Add(get);
        }

        row.Children.Add(new TextBlock { Text = need.Name, FontSize = 14, Foreground = Ivory, VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap, ToolTip = need.Tip });
        return row;
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

    private static Button? FindButton(DependencyObject? node)
    {
        while (node != null)
        {
            if (node is Button button)
                return button;
            node = node is Visual or System.Windows.Media.Media3D.Visual3D ? VisualTreeHelper.GetParent(node) : LogicalTreeHelper.GetParent(node);
        }

        return null;
    }

    private static SolidColorBrush BrushOf(byte r, byte g, byte b)
    {
        var brush = Coodsz.Ui.ToolPalette.Brush(r, g, b);
        brush.Freeze();
        return brush;
    }
}
