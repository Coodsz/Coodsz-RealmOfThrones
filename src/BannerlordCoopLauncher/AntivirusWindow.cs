using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using BannerlordCoopLauncher.Core.Support;

namespace Coodsz.Ui;

/// <summary>
/// Fix antivirus, from every tool's tools row and named in the antivirus lines (AntivirusText). It has the steps
/// for Windows Security, this PC's real folders to exclude with a Copy button each, and one button that
/// opens Windows Security's Virus and threat protection page. It only shows and opens. It never changes
/// a security setting, never adds an exclusion and never runs a command.
/// </summary>
public sealed class AntivirusWindow : Window
{
    private readonly TextBlock _openNote = new() { TextWrapping = TextWrapping.Wrap, FontSize = 13, Margin = new Thickness(0, 6, 0, 0), Visibility = Visibility.Collapsed };

    public AntivirusWindow(IReadOnlyList<AntivirusFolder> folders)
    {
        Title = AntivirusText.WindowTitle;
        // Fits a small screen too. Close stays at the bottom outside the scrolling part.
        Width = Math.Min(640, SystemParameters.WorkArea.Width - 40);
        SizeToContent = SizeToContent.Height;
        MaxHeight = SystemParameters.WorkArea.Height - 40;
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
            Text = AntivirusText.WindowTitle,
            FontFamily = new FontFamily("Palatino Linotype"),
            FontSize = 24,
            Margin = new Thickness(0, 0, 0, 10)
        });
        stack.Children.Add(Line(AntivirusText.WindowIntro, 14, new Thickness(0, 0, 0, 10)));

        var number = 1;
        foreach (var step in new[] { AntivirusText.Step1, AntivirusText.Step2, AntivirusText.Step3, AntivirusText.Step4, AntivirusText.Step5 })
            stack.Children.Add(Step(number++, step));

        var open = new Button
        {
            Content = AntivirusText.OpenSecurity,
            ToolTip = AntivirusText.OpenSecurityTip,
            HorizontalAlignment = HorizontalAlignment.Left,
            Margin = new Thickness(0, 6, 0, 0),
            Padding = new Thickness(14, 6, 14, 6),
            FontSize = 14,
            Cursor = System.Windows.Input.Cursors.Hand,
            Background = BrushOf(31, 78, 121),
            Foreground = BrushOf(213, 222, 230),
            BorderBrush = BrushOf(142, 180, 212)
        };
        System.Windows.Automation.AutomationProperties.SetAutomationId(open, "OpenWindowsSecurity");
        open.Click += (_, _) => OpenSecurity();
        stack.Children.Add(open);
        _openNote.Foreground = BrushOf(226, 194, 122);
        stack.Children.Add(_openNote);

        stack.Children.Add(new TextBlock
        {
            Text = AntivirusText.FoldersTitle,
            FontSize = 16,
            FontWeight = FontWeights.SemiBold,
            Foreground = BrushOf(226, 194, 122),
            Margin = new Thickness(0, 14, 0, 4)
        });
        if (folders.Count == 0)
            stack.Children.Add(Line(AntivirusText.NoFolders, 14, new Thickness(0, 0, 0, 4)));
        foreach (var folder in folders)
        {
            stack.Children.Add(Line(folder.Label, 13, new Thickness(0, 6, 0, 0)));
            stack.Children.Add(JoinGuideWindow.CopyRow(folder.Path));
        }

        stack.Children.Add(Line(AntivirusText.OtherApps, 13, new Thickness(0, 12, 0, 0)));

        var close = new Button
        {
            Content = "Close",
            ToolTip = "Closes this window.",
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(18, 8, 18, 14),
            Padding = new Thickness(18, 6, 18, 6),
            FontSize = 14,
            Cursor = System.Windows.Input.Cursors.Hand,
            Background = BrushOf(27, 40, 51),
            Foreground = BrushOf(213, 222, 230),
            BorderBrush = BrushOf(58, 76, 92),
            IsCancel = true
        };
        close.Click += (_, _) => Close();
        var dock = new DockPanel();
        DockPanel.SetDock(close, Dock.Bottom);
        dock.Children.Add(close);
        dock.Children.Add(new ScrollViewer { Content = stack, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
        Content = dock;
    }

    /// <summary>Opens the window over a tool with this PC's folders (AntivirusFolders.For).</summary>
    public static void Show(Window? owner, IReadOnlyList<AntivirusFolder> folders)
    {
        var window = new AntivirusWindow(folders);
        if (owner != null && owner.IsLoaded)
            window.Owner = owner;
        window.ShowDialog();
    }

    // Only the Windows Security page, through ShellOpen so a PC without it gets a line, not a crash.
    private void OpenSecurity()
    {
        if (ShellOpen.TryOpen(AntivirusText.SecurityPage, out _))
        {
            _openNote.Visibility = Visibility.Collapsed;
            return;
        }

        _openNote.Text = AntivirusText.CouldNotOpen;
        _openNote.Visibility = Visibility.Visible;
    }

    private static TextBlock Line(string text, double size, Thickness margin) =>
        new() { Text = text, TextWrapping = TextWrapping.Wrap, FontSize = size, LineHeight = size + 6, Margin = margin };

    private static UIElement Step(int number, string text)
    {
        var grid = new Grid { Margin = new Thickness(0, 0, 0, 6) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(28) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.Children.Add(new TextBlock
        {
            Text = number.ToString(System.Globalization.CultureInfo.InvariantCulture),
            Tag = UiLanguage.NoTranslate,
            FontWeight = FontWeights.SemiBold,
            FontSize = 15,
            Foreground = BrushOf(226, 194, 122)
        });
        var body = Line(text, 15, new Thickness(0));
        Grid.SetColumn(body, 1);
        grid.Children.Add(body);
        return grid;
    }

    private static SolidColorBrush BrushOf(byte r, byte g, byte b) => Coodsz.Ui.ToolPalette.Brush(r, g, b);
}
