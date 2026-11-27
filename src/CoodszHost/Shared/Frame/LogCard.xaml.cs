using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;

namespace Coodsz.Ui;

/// <summary>
/// The newest log lines in a dark card (the Dashboard keeps 8): time, an optional level word, the line. Copy copies them,
/// Open full log raises OpenFullLog for the page that shows every line.
/// </summary>
public partial class LogCard
{
    private readonly List<string> _plain = new();

    public static readonly DependencyProperty MaxLinesProperty = DependencyProperty.Register(
        nameof(MaxLines), typeof(int), typeof(LogCard), new PropertyMetadata(8));

    public static readonly DependencyProperty TitleProperty = DependencyProperty.Register(
        nameof(Title), typeof(string), typeof(LogCard), new PropertyMetadata("Recent server output", (d, e) => ((LogCard)d).Heading.Text = (string)e.NewValue));

    public event Action? OpenFullLog;

    public LogCard()
    {
        InitializeComponent();
        // The tool's Keep colours, today's dark blue when a tool has none.
        Frame.Background = FrameBrushes.Get(this, "Keep.SidebarBack", "#0A1015");
        Frame.BorderBrush = FrameBrushes.Get(this, "Keep.Line", "#2A3844");
        Heading.Foreground = FrameBrushes.Get(this, "Keep.TextDim", "#8EA0AE");
    }

    public int MaxLines { get => (int)GetValue(MaxLinesProperty); set => SetValue(MaxLinesProperty, value); }
    public string Title { get => (string)GetValue(TitleProperty); set => SetValue(TitleProperty, value); }

    /// <summary>Adds a line at the bottom and drops the oldest past MaxLines. Tone colours the level word (Warn amber, Bad red).</summary>
    public void Push(DateTime time, string kind, string text, ChipKind tone = ChipKind.Mute)
    {
        var stamp = time.ToString("HH:mm:ss", CultureInfo.InvariantCulture);
        // Server lines are shown as the server wrote them, not translated.
        var line = new TextBlock { TextWrapping = TextWrapping.Wrap, Tag = UiLanguage.NoTranslate, Foreground = FrameBrushes.Get(this, "Keep.NavText", "#B8C6D1") };
        line.Inlines.Add(new Run(stamp + " ") { Foreground = FrameBrushes.Get(this, "Keep.TextDim", "#8EA0AE") });
        if (!string.IsNullOrEmpty(kind))
            line.Inlines.Add(new Run(kind + " ") { Foreground = FrameBrushes.Chip(this, tone).Ink });
        line.Inlines.Add(new Run(text));
        Lines.Items.Add(line);
        _plain.Add(stamp + " " + (string.IsNullOrEmpty(kind) ? "" : kind + " ") + text);
        while (Lines.Items.Count > Math.Max(1, MaxLines))
        {
            Lines.Items.RemoveAt(0);
            _plain.RemoveAt(0);
        }
    }

    public void Clear()
    {
        Lines.Items.Clear();
        _plain.Clear();
    }

    private void Copy_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            Clipboard.SetText(string.Join(Environment.NewLine, _plain));
        }
        catch
        {
            // The clipboard is busy in another program; the lines stay on screen.
        }
    }

    private void Open_Click(object sender, RoutedEventArgs e) => OpenFullLog?.Invoke();
}
