using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;

namespace Coodsz.Ui;

/// <summary>
/// A small rounded word with a dot ("Always on", "Broken", "Password Open"): colour and word together, never colour only.
/// One line: a long word ends in "…" and the hover tip shows all of it.
/// </summary>
public class Chip : Border
{
    public static readonly DependencyProperty TextProperty = DependencyProperty.Register(
        nameof(Text), typeof(string), typeof(Chip), new PropertyMetadata("", (d, _) => ((Chip)d).Paint()));

    public static readonly DependencyProperty KindProperty = DependencyProperty.Register(
        nameof(Kind), typeof(ChipKind), typeof(Chip), new PropertyMetadata(ChipKind.Mute, (d, _) => ((Chip)d).Paint()));

    public static readonly DependencyProperty TipProperty = DependencyProperty.Register(
        nameof(Tip), typeof(string), typeof(Chip), new PropertyMetadata("", (d, _) => ((Chip)d).Paint()));

    private readonly Ellipse _dot = new() { Width = 6, Height = 6, Margin = new Thickness(0, 0, 6, 0), VerticalAlignment = VerticalAlignment.Center };
    private readonly TextBlock _word = new() { FontSize = 11, FontWeight = FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis, TextWrapping = TextWrapping.NoWrap, VerticalAlignment = VerticalAlignment.Center };

    public Chip()
    {
        CornerRadius = new CornerRadius(9);
        BorderThickness = new Thickness(1);
        Padding = new Thickness(8, 2, 9, 2);
        MinHeight = 20;
        HorizontalAlignment = HorizontalAlignment.Left;
        VerticalAlignment = VerticalAlignment.Center;
        // A DockPanel, not a StackPanel: the word gets the width left after the dot, so a MaxWidth trims it.
        var row = new DockPanel { LastChildFill = true };
        row.Children.Add(_dot);
        row.Children.Add(_word);
        Child = row;
        Loaded += (_, _) => Paint();
    }

    public string Text
    {
        get => (string)GetValue(TextProperty);
        set => SetValue(TextProperty, value);
    }

    public ChipKind Kind
    {
        get => (ChipKind)GetValue(KindProperty);
        set => SetValue(KindProperty, value);
    }

    /// <summary>The hover line when the word needs more ("This mod is not on this PC..."); empty shows the word.</summary>
    public string Tip
    {
        get => (string)GetValue(TipProperty);
        set => SetValue(TipProperty, value);
    }

    // The English hover this chip set itself, so a ToolTip written in XAML is never painted over.
    private string? _paintedTip;

    private void Paint()
    {
        var (ink, back) = FrameBrushes.Chip(this, Kind);
        _dot.Fill = ink;
        _word.Foreground = ink;
        _word.Text = Text;
        BorderBrush = ink;
        Background = back;
        // UiLanguage swaps a shown hover for its translation, so the English behind it is what gets compared.
        if (ToolTip == null || (_paintedTip != null && UiLanguage.English(this, ToolTipProperty) == _paintedTip))
        {
            _paintedTip = !string.IsNullOrEmpty(Tip) ? Tip : string.IsNullOrEmpty(Text) ? null : Text;
            ToolTip = _paintedTip;
        }
    }
}
