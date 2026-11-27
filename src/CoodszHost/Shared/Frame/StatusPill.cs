using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;

namespace Coodsz.Ui;

/// <summary>
/// The state pill in the header ("Server online", "Stopped"): a word and a dot in the state's colour. The text is set in
/// English; the window's language walk translates it like any other text block. A screen reader hears each change.
/// </summary>
public class StatusPill : Border
{
    public static readonly DependencyProperty TextProperty = DependencyProperty.Register(
        nameof(Text), typeof(string), typeof(StatusPill), new PropertyMetadata("", (d, _) => ((StatusPill)d).Paint(announce: true)));

    public static readonly DependencyProperty KindProperty = DependencyProperty.Register(
        nameof(Kind), typeof(StatusKind), typeof(StatusPill), new PropertyMetadata(StatusKind.Stopped, (d, _) => ((StatusPill)d).Paint(announce: false)));

    private readonly Ellipse _dot = new() { Width = 8, Height = 8, Margin = new Thickness(0, 0, 8, 0), VerticalAlignment = VerticalAlignment.Center };
    private string? _painted;
    private readonly TextBlock _word = new() { FontSize = 12, FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.NoWrap, TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center };

    public StatusPill()
    {
        CornerRadius = new CornerRadius(15);
        BorderThickness = new Thickness(1);
        Padding = new Thickness(12, 0, 14, 0);
        MinHeight = 30;
        VerticalAlignment = VerticalAlignment.Center;
        AutomationProperties.SetLiveSetting(_word, AutomationLiveSetting.Polite);
        var row = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        row.Children.Add(_dot);
        row.Children.Add(_word);
        Child = row;
        Loaded += (_, _) => Paint(announce: false);
    }

    public string Text
    {
        get => (string)GetValue(TextProperty);
        set => SetValue(TextProperty, value);
    }

    public StatusKind Kind
    {
        get => (StatusKind)GetValue(KindProperty);
        set => SetValue(KindProperty, value);
    }

    /// <summary>Sets both at once, the way RefreshState calls it every second; nothing changes when both are the same.</summary>
    public void Show(StatusKind kind, string text)
    {
        if (Kind != kind)
            Kind = kind;
        if (Text != text)
            Text = text;
    }

    private void Paint(bool announce)
    {
        var (ink, back) = FrameBrushes.Chip(this, FrameRules.Status(Kind));
        _dot.Fill = ink;
        _word.Foreground = ink;
        BorderBrush = ink;
        Background = back;
        // The word on screen is translated, so the English text painted last is what tells a change.
        if (_painted == Text)
            return;
        _painted = Text;
        _word.Text = Text;
        if (announce && IsLoaded)
            (UIElementAutomationPeer.FromElement(_word) ?? UIElementAutomationPeer.CreatePeerForElement(_word))?.RaiseAutomationEvent(AutomationEvents.LiveRegionChanged);
    }
}
