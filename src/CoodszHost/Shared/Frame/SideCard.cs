using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;

namespace Coodsz.Ui;

/// <summary>
/// The look the side column cards share (redesign D1 and D2): a near solid panel with a thin line and round corners,
/// a header with a stroke icon and a serif title, and the slots a window fills from its own markup (so the buttons and
/// x:Names stay in MainWindow.xaml, rule 0 of build-plan.md).
/// </summary>
internal static class SideCard
{
    public static void Frame(Border card)
    {
        card.CornerRadius = new CornerRadius(10);
        card.BorderThickness = new Thickness(1);
        card.Padding = new Thickness(16, 14, 16, 16);
        card.Margin = new Thickness(0, 0, 0, 12);
        card.SetResourceReference(Border.BackgroundProperty, "Keep.PanelGradient");
        card.SetResourceReference(Border.BorderBrushProperty, "Keep.Line");
    }

    /// <summary>A Keep icon geometry, or the path text when this tool has no such key.</summary>
    public static Geometry Geometry(FrameworkElement scope, string key, string fallback) =>
        scope.TryFindResource(key) as Geometry ?? System.Windows.Media.Geometry.Parse(fallback);

    public static Path Icon(Geometry data, double size, string brushKey)
    {
        var icon = new Path { Data = data, Width = size, Height = size, Stretch = Stretch.Uniform, StrokeThickness = 1.7, StrokeLineJoin = PenLineJoin.Round, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round, VerticalAlignment = VerticalAlignment.Center, SnapsToDevicePixels = true };
        icon.SetResourceReference(Shape.StrokeProperty, brushKey);
        return icon;
    }

    public static TextBlock Title(string text)
    {
        var title = new TextBlock { Text = text, FontFamily = new FontFamily("Palatino Linotype"), FontSize = 20, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
        title.SetResourceReference(TextBlock.ForegroundProperty, "Keep.TextMain");
        return title;
    }

    /// <summary>A slot for markup from the window. A ContentControl, so its content stays in the logical tree.</summary>
    public static ContentControl Slot() => new() { Focusable = false, IsTabStop = false, HorizontalContentAlignment = HorizontalAlignment.Stretch };

    /// <summary>The header row: the icon in a small square, the title, and a slot on the right (Open Mods).</summary>
    public static DockPanel Header(FrameworkElement scope, string iconKey, string iconFallback, TextBlock title, UIElement? right, bool boxed = true)
    {
        var row = new DockPanel { LastChildFill = true, Margin = new Thickness(0, 0, 0, 10) };
        var icon = Icon(Geometry(scope, iconKey, iconFallback), 16, "Keep.GoldInk");
        FrameworkElement mark = icon;
        if (boxed)
        {
            var box = new Border { Width = 30, Height = 30, CornerRadius = new CornerRadius(8), BorderThickness = new Thickness(1), Child = icon, Margin = new Thickness(0, 0, 10, 0), VerticalAlignment = VerticalAlignment.Center };
            box.SetResourceReference(Border.BorderBrushProperty, "Keep.BorderDim");
            mark = box;
        }
        else
        {
            icon.Margin = new Thickness(0, 0, 10, 0);
        }

        DockPanel.SetDock(mark, Dock.Left);
        row.Children.Add(mark);
        if (right != null)
        {
            DockPanel.SetDock(right, Dock.Right);
            row.Children.Add(right);
        }

        row.Children.Add(title);
        return row;
    }
}

/// <summary>Puts a client's Play page blocks where FrameRules.PlaySlots says.</summary>
public static class PlayLayout
{
    public static void Fit(bool stack, UIElement main, UIElement side, UIElement realm)
    {
        var (mainSlot, sideSlot, realmSlot) = FrameRules.PlaySlots(stack);
        Place(main, mainSlot);
        Place(side, sideSlot);
        Place(realm, realmSlot);
    }

    private static void Place(UIElement element, PlaySlot slot)
    {
        Grid.SetRow(element, slot.Row);
        Grid.SetColumn(element, slot.Column);
        Grid.SetRowSpan(element, slot.RowSpan);
        Grid.SetColumnSpan(element, 1);
    }
}
