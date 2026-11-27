using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using BannerlordCoopLauncher.Core.Localization;
using BannerlordCoopLauncher.Core.Updates;

namespace Coodsz.Ui;

/// <summary>Where a NEW badge sits on its button.</summary>
public enum BadgePlace
{
    /// <summary>Inside the right end, centred: the side menu buttons.</summary>
    Right,

    /// <summary>On the top right corner, a little outside: any other button.</summary>
    Corner,

    /// <summary>
    /// Just after the end, centred: a heading, a line of text or an inline link. The target's
    /// right margin grows by the badge's width while it shows, so the next item in the row moves
    /// over and the badge never covers text.
    /// </summary>
    After,
}

/// <summary>
/// The NEW badges on the things this update added, in the gold of the Join my server badge
/// with a slow, soft pulse. A badge sits on top of its button (an adorner), so the button's
/// own content and code stay as they are. Clicking the button, or the badge, marks the feature
/// tried and every badge for it goes away for good. Which features a tool badges is in
/// <see cref="NewFeatures"/>; <see cref="NewFeatureStore"/> remembers what was tried.
/// </summary>
public static class NewBadges
{
    private static readonly Color Gold = Color.FromRgb(0xE2, 0xC2, 0x7A);
    private static readonly List<(string Id, BadgeAdorner Adorner)> Showing = new();
    private static NewFeatureStore? _store;

    /// <summary>Called from <see cref="UiLanguage.WhatsNewOnce"/> with the tool's name and version.</summary>
    public static void Start(string tool, string version, string? folder = null) =>
        _store = NewFeatureStore.Load(tool, version, NewFeatures.For(tool), folder);

    /// <summary>True while the feature's badge shows.</summary>
    public static bool Shows(string id) => _store?.Shows(id) == true;

    /// <summary>
    /// Puts the NEW badge on <paramref name="target"/> while <paramref name="id"/> is new in
    /// this tool. A button marks it seen when clicked; for anything else call <see cref="Seen"/>.
    /// </summary>
    public static void Attach(FrameworkElement target, string id, BadgePlace place = BadgePlace.Corner)
    {
        if (!Shows(id))
            return;
        if (target is ButtonBase button)
            button.Click += (_, _) => Seen(id);

        var added = false;
        void Add()
        {
            if (added || !Shows(id) || AdornerLayer.GetAdornerLayer(target) is not { } layer)
                return;
            added = true;
            var badge = MakeBadge(target, id);
            var adorner = new BadgeAdorner(target, badge, place);
            if (place == BadgePlace.After)
            {
                // Make room in the row for the badge (8 before it, 6 after it).
                badge.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
                var margin = target.Margin;
                adorner.RoomMade = (target, margin);
                target.Margin = new Thickness(margin.Left, margin.Top, margin.Right + badge.DesiredSize.Width + 14, margin.Bottom);
            }
            layer.Add(adorner);
            Showing.Add((id, adorner));
            void Follow() => adorner.Visibility = target.IsVisible ? Visibility.Visible : Visibility.Collapsed;
            target.IsVisibleChanged += (_, _) => Follow();
            Follow();
        }

        if (target.IsLoaded)
            Add();
        else
            target.Loaded += (_, _) => Add();
    }

    /// <summary>The player tried the feature: its badges go away and never come back.</summary>
    public static void Seen(string id)
    {
        if (_store == null || _store.IsSeen(id))
            return;
        _store.MarkSeen(id);
        foreach (var shown in Showing.Where(s => s.Id == id).ToList())
        {
            Showing.Remove(shown);
            shown.Adorner.Stop();
            AdornerLayer.GetAdornerLayer(shown.Adorner.AdornedElement)?.Remove(shown.Adorner);
        }
    }

    private static Border MakeBadge(FrameworkElement target, string id)
    {
        var text = new TextBlock
        {
            Text = Translator.T(NewFeatures.Badge),
            Tag = UiLanguage.NoTranslate,
            FontFamily = new FontFamily("Segoe UI"),
            FontSize = 10,
            FontWeight = FontWeights.Bold,
            FontStyle = FontStyles.Normal,
            Foreground = new SolidColorBrush(Color.FromRgb(0x10, 0x26, 0x1A)),
        };
        var badge = new Border
        {
            Background = new SolidColorBrush(Gold),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(5, 1, 5, 1),
            Child = text,
            Tag = UiLanguage.NoTranslate,
            ToolTip = Translator.T(NewFeatures.BadgeTip),
            Cursor = System.Windows.Input.Cursors.Hand,
            SnapsToDevicePixels = true,
            Effect = new DropShadowEffect { Color = Gold, ShadowDepth = 0, BlurRadius = 9, Opacity = 0.3 },
        };
        // Clicking the badge is clicking its button.
        badge.MouseLeftButtonUp += (_, e) =>
        {
            e.Handled = true;
            if (target is ButtonBase { IsEnabled: true } button
                && UIElementAutomationPeer.CreatePeerForElement(button)?.GetPattern(PatternInterface.Invoke) is IInvokeProvider invoke)
                invoke.Invoke();
            else
                Seen(id);
        };
        return badge;
    }

    private sealed class BadgeAdorner : Adorner
    {
        private readonly Border _badge;
        private readonly BadgePlace _place;
        private readonly Action _relabel;

        public BadgeAdorner(UIElement target, Border badge, BadgePlace place) : base(target)
        {
            _badge = badge;
            _place = place;
            AddVisualChild(badge);
            Pulse(badge);
            _relabel = () => badge.Dispatcher.BeginInvoke(() =>
            {
                ((TextBlock)badge.Child).Text = Translator.T(NewFeatures.Badge);
                badge.ToolTip = Translator.T(NewFeatures.BadgeTip);
                if (RoomMade is var (room, margin))
                {
                    badge.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
                    room.Margin = new Thickness(margin.Left, margin.Top, margin.Right + badge.DesiredSize.Width + 14, margin.Bottom);
                }
            });
            Translator.Changed += _relabel;
        }

        /// <summary>The target and its margin from before the badge made room after it.</summary>
        public (FrameworkElement Target, Thickness Margin)? RoomMade { get; set; }

        public void Stop()
        {
            Translator.Changed -= _relabel;
            if (RoomMade is var (target, margin))
                target.Margin = margin;
            _badge.BeginAnimation(OpacityProperty, null);
            _badge.Effect?.BeginAnimation(DropShadowEffect.OpacityProperty, null);
        }

        // A gentle breath: the glow swells and the badge dims a little, about once every 2.4 seconds.
        private static void Pulse(Border badge)
        {
            var ease = new SineEase { EasingMode = EasingMode.EaseInOut };
            var time = TimeSpan.FromSeconds(1.2);
            badge.BeginAnimation(OpacityProperty, new DoubleAnimation(1, 0.72, time) { AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever, EasingFunction = ease });
            badge.Effect.BeginAnimation(DropShadowEffect.OpacityProperty, new DoubleAnimation(0.25, 0.85, time) { AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever, EasingFunction = ease });
        }

        protected override int VisualChildrenCount => 1;

        protected override Visual GetVisualChild(int index) => _badge;

        protected override Size MeasureOverride(Size constraint)
        {
            _badge.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            return AdornedElement.RenderSize;
        }

        protected override Size ArrangeOverride(Size finalSize)
        {
            var size = AdornedElement.RenderSize;
            var badge = _badge.DesiredSize;
            var spot = _place switch
            {
                BadgePlace.Right => new Point(size.Width - badge.Width - 12, (size.Height - badge.Height) / 2),
                BadgePlace.After => new Point(size.Width + 8, (size.Height - badge.Height) / 2),
                _ => new Point(size.Width - badge.Width + 6, -badge.Height / 2 + 1),
            };
            _badge.Arrange(new Rect(spot, badge));
            return finalSize;
        }
    }
}
