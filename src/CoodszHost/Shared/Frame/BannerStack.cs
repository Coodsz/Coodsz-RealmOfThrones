using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using BannerlordCoopLauncher.Core.Localization;

namespace Coodsz.Ui;

/// <summary>
/// The notices under the header, one above the other. Only the most urgent visible ones show (MaxShown: one in every tool window, two by default; BannerStack.Priority,
/// lower first, then file order); the rest wait behind an "N more notices" chip that opens them in place.
/// It never changes a child's Visibility, so code that shows or hides a banner keeps working as before.
/// </summary>
public class BannerStack : Panel
{
    public static readonly DependencyProperty PriorityProperty = DependencyProperty.RegisterAttached(
        "Priority", typeof(int), typeof(BannerStack), new FrameworkPropertyMetadata(100, FrameworkPropertyMetadataOptions.AffectsParentMeasure));

    public static int GetPriority(DependencyObject element) => (int)element.GetValue(PriorityProperty);
    public static void SetPriority(DependencyObject element, int value) => element.SetValue(PriorityProperty, value);

    public static readonly DependencyProperty MaxShownProperty = DependencyProperty.Register(
        nameof(MaxShown), typeof(int), typeof(BannerStack), new FrameworkPropertyMetadata(2, FrameworkPropertyMetadataOptions.AffectsMeasure));

    public int MaxShown
    {
        get => (int)GetValue(MaxShownProperty);
        set => SetValue(MaxShownProperty, value);
    }

    private readonly Button _more;
    private readonly TextBlock _moreText = new() { Tag = UiLanguage.NoTranslate, FontSize = 11, FontWeight = FontWeights.SemiBold };
    private bool _expanded;
    private IReadOnlyList<int> _shown = [];
    private int _hidden;

    public BannerStack()
    {
        ClipToBounds = true;
        _more = new Button
        {
            Content = _moreText,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 6, 0, 0),
            Padding = new Thickness(10, 2, 10, 2),
            MinHeight = 24,
            Cursor = System.Windows.Input.Cursors.Hand,
            BorderThickness = new Thickness(1),
            Tag = UiLanguage.NoTranslate,
        };
        _more.Click += (_, _) =>
        {
            _expanded = !_expanded;
            InvalidateMeasure();
        };
        Loaded += (_, _) =>
        {
            _more.Foreground = FrameBrushes.Get(this, "Keep.NavText", "#B8C6D1");
            _more.Background = FrameBrushes.Get(this, "Keep.BgRaised", "#1B2833");
            _more.BorderBrush = FrameBrushes.Get(this, "Keep.BorderDim", "#3A4C5C");
        };
        // Translator.Changed is static, so the stack only listens while it is on screen (as MoveButtons does).
        Loaded += (_, _) =>
        {
            Translator.Changed -= LanguageChanged;
            Translator.Changed += LanguageChanged;
        };
        Unloaded += (_, _) => Translator.Changed -= LanguageChanged;
        AddVisualChild(_more);
        AddLogicalChild(_more);
    }

    private void LanguageChanged() => Dispatcher.BeginInvoke(InvalidateMeasure);

    /// <summary>The words on the chip, in the picked language.</summary>
    public static string MoreText(int hidden, bool expanded) =>
        expanded ? Translator.T("Show fewer notices")
        : hidden == 1 ? Translator.T("1 more notice")
        : Translator.T($"{hidden} more notices");

    protected override int VisualChildrenCount => base.VisualChildrenCount + 1;

    protected override Visual GetVisualChild(int index) =>
        index < base.VisualChildrenCount ? base.GetVisualChild(index) : _more;

    protected override Size MeasureOverride(Size available)
    {
        var children = InternalChildren.Cast<UIElement>().ToList();
        var (shown, hidden) = FrameRules.Banners(children.Select(child => (child.Visibility != Visibility.Collapsed, GetPriority(child))).ToList(), MaxShown, _expanded);
        // Writing _shown, _hidden, _expanded and the chip here is safe: they are this panel's own fields and its own
        // chip, never a layout input of a child, so no child's measure is invalidated and no layout loop starts.
        _shown = shown;
        _hidden = hidden;
        var width = 0.0;
        var height = 0.0;
        var room = new Size(available.Width, double.PositiveInfinity);
        foreach (var child in children)
            child.Measure(room);
        foreach (var index in shown)
        {
            width = Math.Max(width, children[index].DesiredSize.Width);
            height += children[index].DesiredSize.Height;
        }
        var showMore = hidden > 0 || (_expanded && shown.Count > MaxShown);
        _more.Visibility = showMore ? Visibility.Visible : Visibility.Collapsed;
        if (showMore)
        {
            _moreText.Text = MoreText(hidden, _expanded);
            _more.ToolTip = Translator.T(_expanded ? (MaxShown == 1 ? "Shows only the most urgent notice." : "Shows only the two most urgent notices.") : "Shows the other notices here.");
            _more.Measure(room);
            height += _more.DesiredSize.Height;
            width = Math.Max(width, _more.DesiredSize.Width);
        }
        else
        {
            _expanded = false;
        }
        return new Size(width, height);
    }

    protected override Size ArrangeOverride(Size final)
    {
        var children = InternalChildren.Cast<UIElement>().ToList();
        var y = 0.0;
        foreach (var index in _shown)
        {
            var child = children[index];
            child.Arrange(new Rect(0, y, final.Width, child.DesiredSize.Height));
            y += child.DesiredSize.Height;
        }
        // A visible banner past the first two waits off the panel (the panel clips), so it keeps its own Visibility.
        // Tab and UI Automation skip it while it waits; it gets both back once it shows.
        for (var index = 0; index < children.Count; index++)
        {
            var parked = !_shown.Contains(index);
            Park(children[index], parked);
            if (parked)
                children[index].Arrange(new Rect(-20000, -20000, final.Width, children[index].DesiredSize.Height));
        }
        if (_more.Visibility == Visibility.Visible)
            _more.Arrange(new Rect(0, y, final.Width, _more.DesiredSize.Height));
        return final;
    }

    // A parked banner: no Tab stop inside it and off screen for screen readers, without touching its Visibility.
    private static void Park(UIElement child, bool parked)
    {
        if (parked)
        {
            KeyboardNavigation.SetTabNavigation(child, KeyboardNavigationMode.None);
            KeyboardNavigation.SetIsTabStop(child, false);
            AutomationProperties.SetIsOffscreenBehavior(child, IsOffscreenBehavior.Offscreen);
        }
        else
        {
            child.ClearValue(KeyboardNavigation.TabNavigationProperty);
            child.ClearValue(KeyboardNavigation.IsTabStopProperty);
            child.ClearValue(AutomationProperties.IsOffscreenBehaviorProperty);
        }
    }
}
