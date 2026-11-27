using System.Windows;
using System.Windows.Controls;
using System.Windows.Markup;
using System.Windows.Media;
using BannerlordCoopLauncher.Core.Modules;

namespace Coodsz.Ui;

/// <summary>
/// The Made for card (redesign D1): a row for the game, Coop and the overhaul, each with its icon, its name, the version
/// in mono and a mark (a green tick when it matches the tool's pin, amber when it is not found, red when it is another
/// version), then the tool's own short notes in amber. Detail holds the window's extra lines (the Active Coop line).
/// </summary>
[ContentProperty(nameof(Detail))]
public class MadeForCard : Border
{
    public static readonly DependencyProperty TitleProperty = DependencyProperty.Register(
        nameof(Title), typeof(string), typeof(MadeForCard), new PropertyMetadata("Made for", (d, e) => ((MadeForCard)d)._title.Text = (string)e.NewValue));

    public static readonly DependencyProperty SubtitleProperty = DependencyProperty.Register(
        nameof(Subtitle), typeof(string), typeof(MadeForCard), new PropertyMetadata("", (d, e) => ((MadeForCard)d).PaintSubtitle()));

    private readonly TextBlock _title = SideCard.Title("Made for");
    private readonly TextBlock _subtitle = new() { Tag = UiLanguage.NoTranslate, FontFamily = new FontFamily("Consolas"), FontSize = 12, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(10, 0, 0, 0) };
    private readonly StackPanel _rows = new();
    private readonly StackPanel _notes = new();
    private readonly ContentControl _detail = SideCard.Slot();
    private string _shownKey = "";

    public MadeForCard()
    {
        SideCard.Frame(this);
        _subtitle.SetResourceReference(TextBlock.ForegroundProperty, "Keep.NavText");
        var body = new StackPanel();
        body.Children.Add(SideCard.Header(this, "Keep.IconShield", "M 8 1 L 14 3.5 C 14 9 11.5 12.5 8 15 C 4.5 12.5 2 9 2 3.5 Z", _title, _subtitle));
        body.Children.Add(_rows);
        body.Children.Add(_detail);
        body.Children.Add(_notes);
        Child = body;
        PaintSubtitle();
    }

    public string Title
    {
        get => (string)GetValue(TitleProperty);
        set => SetValue(TitleProperty, value);
    }

    /// <summary>The kit or pack name in mono on the right of the title ("TAOM-Coop 0.3.25"); empty hides it.</summary>
    public string Subtitle
    {
        get => (string)GetValue(SubtitleProperty);
        set => SetValue(SubtitleProperty, value);
    }

    public object? Detail
    {
        get => _detail.Content;
        set => _detail.Content = value;
    }

    public IReadOnlyList<VersionPin> Pins { get; private set; } = Array.Empty<VersionPin>();

    /// <summary>Draws the rows and the notes again; a call with the same rows and notes changes nothing.</summary>
    public void Show(IReadOnlyList<VersionPin> pins, IReadOnlyList<string>? notes = null)
    {
        notes ??= Array.Empty<string>();
        var key = string.Join("|", pins.Select(pin => pin.Name + "=" + pin.Shown + ":" + pin.State)) + "#" + string.Join("|", notes);
        if (key == _shownKey)
            return;
        _shownKey = key;
        Pins = pins;
        _rows.Children.Clear();
        foreach (var pin in pins)
            _rows.Children.Add(Row(pin));
        _notes.Children.Clear();
        foreach (var note in notes.Where(note => !string.IsNullOrWhiteSpace(note)))
            _notes.Children.Add(Note(note));
    }

    private void PaintSubtitle()
    {
        _subtitle.Text = Subtitle ?? "";
        _subtitle.Visibility = string.IsNullOrEmpty(Subtitle) ? Visibility.Collapsed : Visibility.Visible;
    }

    private Border Row(VersionPin pin)
    {
        var (iconKey, fallback) = pin.Icon switch
        {
            PinIcon.Game => ("Keep.IconShield", "M 8 1 L 14 3.5 C 14 9 11.5 12.5 8 15 C 4.5 12.5 2 9 2 3.5 Z"),
            PinIcon.Coop => ("Keep.IconPlayers", "M 9 11 a 3 3 0 1 0 0 -6 a 3 3 0 0 0 0 6 Z M 3 20 c 0 -3 3 -5 6 -5 s 6 2 6 5 M 17 6 a 3 3 0 0 1 0 6 M 21 20 c 0 -2 -1.5 -3.5 -3.5 -4.2"),
            _ => ("Keep.IconMods", "M 12 3 l 8 4 l -8 4 l -8 -4 Z M 4 12 l 8 4 l 8 -4 M 4 16 l 8 4 l 8 -4"),
        };
        var row = new DockPanel { LastChildFill = true };
        var icon = SideCard.Icon(SideCard.Geometry(this, iconKey, fallback), 16, "Keep.NavText");
        icon.Margin = new Thickness(0, 0, 10, 0);
        DockPanel.SetDock(icon, Dock.Left);
        row.Children.Add(icon);

        var mark = Mark(pin);
        DockPanel.SetDock(mark, Dock.Right);
        row.Children.Add(mark);
        var version = new TextBlock { Text = pin.Shown, Tag = UiLanguage.NoTranslate, FontFamily = new FontFamily("Consolas"), FontSize = 13, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(10, 0, 10, 0) };
        version.SetResourceReference(TextBlock.ForegroundProperty, pin.State == PinState.Match ? "Keep.NavText" : "Keep.TextMain");
        DockPanel.SetDock(version, Dock.Right);
        row.Children.Add(version);
        // The names are mod names (Bannerlord, TAOM, Coop), the same in every language.
        var name = new TextBlock { Text = pin.Name, Tag = UiLanguage.NoTranslate, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
        name.SetResourceReference(TextBlock.ForegroundProperty, "Keep.TextMain");
        row.Children.Add(name);

        var line = new Border { Child = row, MinHeight = 40, Padding = new Thickness(12, 0, 12, 0), CornerRadius = new CornerRadius(8), BorderThickness = new Thickness(1), Margin = new Thickness(0, 0, 0, 8) };
        line.SetResourceReference(Border.BackgroundProperty, "Keep.BgDeep");
        line.SetResourceReference(Border.BorderBrushProperty, "Keep.Line");
        return line;
    }

    private Border Mark(VersionPin pin)
    {
        var kind = pin.State switch
        {
            PinState.Match => ChipKind.Ok,
            PinState.Missing => ChipKind.Warn,
            _ => ChipKind.Bad,
        };
        var (ink, back) = FrameBrushes.Chip(this, kind);
        // A tick, an exclamation mark or a cross: the mark never says it with colour only.
        var data = pin.State switch
        {
            PinState.Match => "M 5 12.5 L 9.5 17 L 19 7.5",
            PinState.Missing => "M 12 6 L 12 13 M 12 17 L 12 17.5",
            _ => "M 7 7 L 17 17 M 17 7 L 7 17",
        };
        var glyph = new System.Windows.Shapes.Path { Data = System.Windows.Media.Geometry.Parse(data), Stroke = ink, StrokeThickness = 2.6, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round, StrokeLineJoin = PenLineJoin.Round, Width = 11, Height = 11, Stretch = Stretch.Uniform };
        return new Border { Width = 22, Height = 22, CornerRadius = new CornerRadius(11), BorderThickness = new Thickness(1), BorderBrush = ink, Background = back, Child = glyph, VerticalAlignment = VerticalAlignment.Center, ToolTip = VersionPins.Tip(pin) };
    }

    private Border Note(string text)
    {
        var row = new DockPanel { LastChildFill = true };
        var icon = SideCard.Icon(System.Windows.Media.Geometry.Parse("M 12 3 L 22 21 L 2 21 Z M 12 10 L 12 15 M 12 18 L 12 18.01"), 15, "Keep.WarningInk");
        icon.Margin = new Thickness(0, 1, 10, 0);
        icon.VerticalAlignment = VerticalAlignment.Top;
        DockPanel.SetDock(icon, Dock.Left);
        row.Children.Add(icon);
        var words = new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap, FontSize = 13 };
        words.SetResourceReference(TextBlock.ForegroundProperty, "Keep.WarningInk");
        row.Children.Add(words);
        var note = new Border { Child = row, Padding = new Thickness(12, 9, 12, 9), CornerRadius = new CornerRadius(8), BorderThickness = new Thickness(1), Margin = new Thickness(0, 4, 0, 0) };
        note.SetResourceReference(Border.BackgroundProperty, "Keep.WarningSoft");
        note.SetResourceReference(Border.BorderBrushProperty, "Keep.WarningLine");
        return note;
    }
}
