using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using BannerlordCoopLauncher.Core.Joining;

namespace Coodsz.Ui;

/// <summary>
/// The small join guide behind the ? buttons: pick one way to join and see only its steps,
/// with the addresses this tool found and a Copy button, plus a closed Something went wrong
/// list. The text comes from <see cref="JoinGuide"/>, so every Coodsz tool shows the same guide.
/// </summary>
public sealed class JoinGuideWindow : Window
{
    private readonly JoinFacts _facts;
    private readonly WrapPanel _picker = new() { Margin = new Thickness(0, 0, 0, 8) };
    private readonly TextBlock _status = new() { TextWrapping = TextWrapping.Wrap, FontSize = 13, Margin = new Thickness(0, 0, 0, 8) };
    private readonly StackPanel _steps = new();

    public JoinGuideWindow(JoinFacts facts, GuideTopic? start = null)
    {
        _facts = facts;
        Title = JoinGuide.Title;
        Width = 640;
        SizeToContent = SizeToContent.Height;
        MaxHeight = Math.Max(480, SystemParameters.WorkArea.Height - 40);
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        WindowPlacement.KeepOnScreen(this);
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        Background = BrushOf(14, 20, 24);
        Foreground = BrushOf(213, 222, 230);
        FontFamily = new FontFamily("Segoe UI");

        var stack = new StackPanel { Margin = new Thickness(18) };
        stack.Children.Add(new TextBlock
        {
            Text = JoinGuide.Title,
            FontFamily = new FontFamily("Palatino Linotype"),
            FontSize = 24,
            Margin = new Thickness(0, 0, 0, 10)
        });
        stack.Children.Add(_picker);
        stack.Children.Add(_status);
        stack.Children.Add(_steps);
        stack.Children.Add(Fixes());

        var close = new Button
        {
            Content = "Close",
            ToolTip = "Closes the join guide.",
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 12, 0, 0),
            Padding = new Thickness(18, 6, 18, 6),
            FontSize = 14,
            Cursor = System.Windows.Input.Cursors.Hand,
            Background = BrushOf(27, 40, 51),
            Foreground = BrushOf(213, 222, 230),
            BorderBrush = BrushOf(58, 76, 92),
            IsCancel = true
        };
        close.Click += (_, _) => Close();
        stack.Children.Add(close);
        Content = new ScrollViewer { Content = stack, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };

        Pick(start ?? JoinGuide.Suggested(facts));
    }

    /// <summary>Opens the guide over a window. Start picks the topic it opens on.</summary>
    public static void Show(Window? owner, JoinFacts facts, GuideTopic? start = null)
    {
        var window = new JoinGuideWindow(facts, start);
        if (owner != null && owner.IsLoaded)
            window.Owner = owner;
        window.ShowDialog();
    }

    private void Pick(GuideTopic topic)
    {
        _picker.Children.Clear();
        GuideChoice? picked = null;
        foreach (var choice in JoinGuide.Choices(_facts))
        {
            var selected = choice.Topic == topic;
            if (selected)
                picked = choice;
            var label = new StackPanel { Orientation = Orientation.Horizontal };
            if (choice.WorksNow)
                label.Children.Add(new System.Windows.Shapes.Ellipse
                {
                    Width = 8,
                    Height = 8,
                    Fill = BrushOf(143, 203, 155),
                    Margin = new Thickness(0, 0, 6, 0),
                    VerticalAlignment = VerticalAlignment.Center
                });
            label.Children.Add(new TextBlock { Text = choice.Title });
            var button = new Button
            {
                Content = label,
                ToolTip = "Shows the steps for this way to join. A green dot means it should work right now.",
                Margin = new Thickness(0, 0, 6, 6),
                Padding = new Thickness(12, 6, 12, 6),
                FontSize = 14,
                Cursor = System.Windows.Input.Cursors.Hand,
                Background = selected ? BrushOf(31, 78, 121) : BrushOf(27, 40, 51),
                Foreground = BrushOf(213, 222, 230),
                BorderBrush = selected ? BrushOf(142, 180, 212) : BrushOf(58, 76, 92),
                BorderThickness = new Thickness(selected ? 2 : 1)
            };
            var target = choice.Topic;
            button.Click += (_, _) => Pick(target);
            _picker.Children.Add(button);
        }

        _status.Text = picked?.Status ?? "";
        _status.Foreground = picked?.WorksNow == true ? BrushOf(143, 203, 155) : BrushOf(142, 160, 174);
        _status.Visibility = string.IsNullOrEmpty(_status.Text) ? Visibility.Collapsed : Visibility.Visible;

        _steps.Children.Clear();
        var number = 1;
        foreach (var step in JoinGuide.Steps(topic, _facts))
            _steps.Children.Add(Step(number++, step));
    }

    private static UIElement Step(int number, GuideStep step)
    {
        var grid = new Grid { Margin = new Thickness(0, 0, 0, 8) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(28) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var badge = new TextBlock
        {
            Text = number.ToString(CultureInfo.InvariantCulture),
            Tag = UiLanguage.NoTranslate,
            FontWeight = FontWeights.SemiBold,
            FontSize = 15,
            Foreground = BrushOf(226, 194, 122)
        };
        grid.Children.Add(badge);

        var body = new StackPanel();
        body.Children.Add(new TextBlock { Text = step.Text, TextWrapping = TextWrapping.Wrap, FontSize = 15, LineHeight = 22 });
        if (!string.IsNullOrWhiteSpace(step.Copy))
            body.Children.Add(CopyRow(step.Copy));
        Grid.SetColumn(body, 1);
        grid.Children.Add(body);
        return grid;
    }

    /// <summary>An address in a read only box with a Copy button.</summary>
    public static UIElement CopyRow(string value)
    {
        var row = new DockPanel { Margin = new Thickness(0, 4, 0, 0), LastChildFill = false };
        var box = new TextBox
        {
            Text = value,
            IsReadOnly = true,
            MinWidth = 180,
            Padding = new Thickness(8, 3, 8, 3),
            FontFamily = new FontFamily("Consolas"),
            FontSize = 14,
            Background = BrushOf(16, 22, 27),
            Foreground = BrushOf(213, 222, 230),
            BorderBrush = BrushOf(58, 76, 92),
            FlowDirection = FlowDirection.LeftToRight
        };
        var note = new TextBlock { FontSize = 12, Foreground = BrushOf(143, 203, 155), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 0, 0) };
        var copy = new Button
        {
            Content = "Copy",
            ToolTip = "Copies this to the clipboard.",
            Margin = new Thickness(6, 0, 0, 0),
            Padding = new Thickness(12, 3, 12, 3),
            FontSize = 13,
            Cursor = System.Windows.Input.Cursors.Hand,
            Background = BrushOf(27, 40, 51),
            Foreground = BrushOf(142, 180, 212),
            BorderBrush = BrushOf(142, 180, 212)
        };
        copy.Click += (_, _) =>
        {
            try
            {
                Clipboard.SetText(value);
                note.Text = "Copied";
            }
            catch (System.Runtime.InteropServices.COMException)
            {
                note.Text = "Could not copy. Select the text and press Ctrl C.";
            }
        };
        row.Children.Add(box);
        row.Children.Add(copy);
        row.Children.Add(note);
        return row;
    }

    private UIElement Fixes()
    {
        var list = new StackPanel { Margin = new Thickness(4, 6, 0, 0) };
        foreach (var fix in JoinGuide.Fixes(_facts))
        {
            var line = new DockPanel { Margin = new Thickness(0, 0, 0, 6) };
            var dot = new TextBlock { Text = "•", Tag = UiLanguage.NoTranslate, Margin = new Thickness(0, 0, 8, 0), Foreground = BrushOf(226, 194, 122) };
            DockPanel.SetDock(dot, Dock.Left);
            line.Children.Add(dot);
            line.Children.Add(new TextBlock { Text = fix, TextWrapping = TextWrapping.Wrap, FontSize = 14 });
            list.Children.Add(line);
        }

        return new Expander
        {
            Header = JoinGuide.SomethingWentWrong,
            IsExpanded = false,
            Margin = new Thickness(0, 6, 0, 0),
            FontSize = 15,
            Foreground = BrushOf(226, 194, 122),
            Content = new ContentControl { Content = list, Foreground = BrushOf(213, 222, 230) }
        };
    }

    /// <summary>A small round ? button that opens the guide, for next to a join area.</summary>
    public static Button HelpButton(Func<JoinFacts> facts, GuideTopic? start = null)
    {
        var button = new Button
        {
            Content = "?",
            ToolTip = "How to join",
            Width = 30,
            Height = 30,
            Padding = new Thickness(0),
            FontSize = 15,
            FontWeight = FontWeights.SemiBold,
            Cursor = System.Windows.Input.Cursors.Hand,
            Background = BrushOf(26, 42, 58),
            Foreground = BrushOf(142, 180, 212),
            BorderBrush = BrushOf(142, 180, 212),
            BorderThickness = new Thickness(1),
            VerticalAlignment = VerticalAlignment.Center
        };
        button.Click += (_, _) => Show(Window.GetWindow(button), facts(), start);
        return button;
    }

    private static SolidColorBrush BrushOf(byte r, byte g, byte b) => Coodsz.Ui.ToolPalette.Brush(r, g, b);
}
