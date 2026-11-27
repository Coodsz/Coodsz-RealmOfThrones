using System.Windows;
using System.Windows.Automation;

namespace Coodsz.Ui;

/// <summary>A notice for a BannerStack: Kind sets the colours, ActionText shows the one button, CanDismiss the Dismiss button.</summary>
public partial class Banner
{
    public static readonly DependencyProperty KindProperty = DependencyProperty.Register(
        nameof(Kind), typeof(BannerKind), typeof(Banner), new PropertyMetadata(BannerKind.Info, (d, _) => ((Banner)d).Paint()));

    public static readonly DependencyProperty TitleProperty = DependencyProperty.Register(
        nameof(Title), typeof(string), typeof(Banner), new PropertyMetadata("", (d, _) => ((Banner)d).Paint()));

    public static readonly DependencyProperty TextProperty = DependencyProperty.Register(
        nameof(Text), typeof(string), typeof(Banner), new PropertyMetadata("", (d, _) => ((Banner)d).Paint()));

    public static readonly DependencyProperty ActionTextProperty = DependencyProperty.Register(
        nameof(ActionText), typeof(string), typeof(Banner), new PropertyMetadata("", (d, _) => ((Banner)d).Paint()));

    public static readonly DependencyProperty ActionTipProperty = DependencyProperty.Register(
        nameof(ActionTip), typeof(string), typeof(Banner), new PropertyMetadata("", (d, _) => ((Banner)d).Paint()));

    public static readonly DependencyProperty CanDismissProperty = DependencyProperty.Register(
        nameof(CanDismiss), typeof(bool), typeof(Banner), new PropertyMetadata(false, (d, _) => ((Banner)d).Paint()));

    /// <summary>The one button was clicked.</summary>
    public event Action? Action;

    /// <summary>Dismiss was clicked; the banner has hidden itself.</summary>
    public event Action? Dismissed;

    public Banner()
    {
        InitializeComponent();
        AutomationProperties.SetLiveSetting(TitleText, AutomationLiveSetting.Polite);
        Loaded += (_, _) => Paint();
    }

    public BannerKind Kind { get => (BannerKind)GetValue(KindProperty); set => SetValue(KindProperty, value); }
    public string Title { get => (string)GetValue(TitleProperty); set => SetValue(TitleProperty, value); }
    public string Text { get => (string)GetValue(TextProperty); set => SetValue(TextProperty, value); }
    public string ActionText { get => (string)GetValue(ActionTextProperty); set => SetValue(ActionTextProperty, value); }
    public string ActionTip { get => (string)GetValue(ActionTipProperty); set => SetValue(ActionTipProperty, value); }
    public bool CanDismiss { get => (bool)GetValue(CanDismissProperty); set => SetValue(CanDismissProperty, value); }

    private void Paint()
    {
        var (ink, back) = FrameBrushes.Chip(this, FrameRules.Banner(Kind));
        Frame.Background = back;
        Frame.BorderBrush = ink;
        TitleText.Foreground = ink;
        TitleText.Text = Title;
        TitleText.Visibility = string.IsNullOrEmpty(Title) ? Visibility.Collapsed : Visibility.Visible;
        BodyText.Foreground = FrameBrushes.Get(this, "Keep.TextMain", "#D5DEE6");
        BodyText.Text = Text;
        BodyText.Visibility = string.IsNullOrEmpty(Text) ? Visibility.Collapsed : Visibility.Visible;
        ActionButton.Content = ActionText;
        ActionButton.ToolTip = string.IsNullOrEmpty(ActionTip) ? ActionText : ActionTip;
        ActionButton.Visibility = string.IsNullOrEmpty(ActionText) ? Visibility.Collapsed : Visibility.Visible;
        DismissButton.Visibility = CanDismiss ? Visibility.Visible : Visibility.Collapsed;
    }

    private void Action_Click(object sender, RoutedEventArgs e) => Action?.Invoke();

    private void Dismiss_Click(object sender, RoutedEventArgs e)
    {
        Visibility = Visibility.Collapsed;
        Dismissed?.Invoke();
    }
}
