using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace Coodsz.Ui;

/// <summary>
/// The sidebar's page buttons (style Keep.Nav): the icon of each button, the selected one, and the compact
/// look (icons only) that a narrow window gets. The buttons stay in each MainWindow.xaml, so their names keep working.
/// </summary>
public static class NavRail
{
    public const double WideWidth = 220;
    public const double CompactWidth = 64;

    public static readonly DependencyProperty IconProperty = DependencyProperty.RegisterAttached(
        "Icon", typeof(Geometry), typeof(NavRail), new FrameworkPropertyMetadata(null));

    public static Geometry? GetIcon(DependencyObject element) => (Geometry?)element.GetValue(IconProperty);
    public static void SetIcon(DependencyObject element, Geometry? value) => element.SetValue(IconProperty, value);

    /// <summary>Set on the window; every page button under it shows only its icon (its hover tip still names the page).</summary>
    public static readonly DependencyProperty IsCompactProperty = DependencyProperty.RegisterAttached(
        "IsCompact", typeof(bool), typeof(NavRail), new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.Inherits));

    public static bool GetIsCompact(DependencyObject element) => (bool)element.GetValue(IsCompactProperty);
    public static void SetIsCompact(DependencyObject element, bool value) => element.SetValue(IsCompactProperty, value);

    public static readonly DependencyProperty SelectedProperty = DependencyProperty.RegisterAttached(
        "Selected", typeof(bool), typeof(NavRail), new FrameworkPropertyMetadata(false));

    public static bool GetSelected(DependencyObject element) => (bool)element.GetValue(SelectedProperty);
    public static void SetSelected(DependencyObject element, bool value) => element.SetValue(SelectedProperty, value);

    /// <summary>Marks the button whose Tag is the page as selected and clears the others.</summary>
    public static void Select(IEnumerable<Button> buttons, string page)
    {
        foreach (var button in buttons)
            SetSelected(button, (button.Tag as string) == page);
    }

    /// <summary>
    /// Keeps the sidebar column 220 px wide, or 64 px with icons only while the window is narrower than the breakpoint.
    /// <paramref name="changed"/> runs when the look flips (and once at the start), for parts the style can't reach.
    /// </summary>
    public static void Fit(Window window, ColumnDefinition column, double breakpoint = 1000, Action<bool>? changed = null)
    {
        bool? last = null;
        void Apply()
        {
            var width = window.ActualWidth > 0 ? window.ActualWidth : window.Width;
            var compact = width > 0 && width < breakpoint;
            if (last == compact)
                return;
            last = compact;
            SetIsCompact(window, compact);
            column.Width = new GridLength(compact ? CompactWidth : WideWidth);
            changed?.Invoke(compact);
        }

        window.SizeChanged += (_, _) => Apply();
        Apply();
    }
}
