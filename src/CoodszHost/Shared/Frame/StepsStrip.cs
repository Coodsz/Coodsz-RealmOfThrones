using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace Coodsz.Ui;

/// <summary>One step of a first run strip: its words, whether it is done, and an optional button that does it.</summary>
public sealed record FrameStep(string Text, bool Done, string? LinkText = null, string? LinkTip = null, Action? Link = null);

/// <summary>
/// Numbered first run steps that tick when done ("1 Set server folder, 2 Pick or make a save, 3 Start server").
/// Each step may carry one button. Call Show again whenever a step changes.
/// </summary>
public class StepsStrip : WrapPanel
{
    public StepsStrip()
    {
        Orientation = Orientation.Horizontal;
    }

    public void Show(IReadOnlyList<FrameStep> steps)
    {
        Children.Clear();
        var done = FrameBrushes.Get(this, "Keep.Success", "#8FCB9B");
        var todo = FrameBrushes.Get(this, "Keep.GoldInk", "#E2C27A");
        var text = FrameBrushes.Get(this, "Keep.TextMain", "#D5DEE6");
        for (var i = 0; i < steps.Count; i++)
        {
            var step = steps[i];
            var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 24, 8), VerticalAlignment = VerticalAlignment.Center };
            var mark = new Border
            {
                Width = 24,
                Height = 24,
                CornerRadius = new CornerRadius(12),
                BorderThickness = new Thickness(1),
                BorderBrush = step.Done ? done : todo,
                Background = step.Done ? done : Brushes.Transparent,
                Margin = new Thickness(0, 0, 8, 0),
                Child = new TextBlock
                {
                    Text = step.Done ? "✓" : (i + 1).ToString(),
                    Tag = UiLanguage.NoTranslate,
                    FontWeight = FontWeights.SemiBold,
                    Foreground = step.Done ? FrameBrushes.Get(this, "Keep.BgDeep", "#0E1418") : todo,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                },
            };
            row.Children.Add(mark);
            row.Children.Add(new TextBlock { Text = step.Text, Foreground = text, Opacity = step.Done ? 0.7 : 1, VerticalAlignment = VerticalAlignment.Center });
            if (!step.Done && step.Link != null && !string.IsNullOrEmpty(step.LinkText))
            {
                var button = new Button { Content = step.LinkText, ToolTip = step.LinkTip ?? step.LinkText, Margin = new Thickness(10, 0, 0, 0) };
                button.SetResourceReference(StyleProperty, "Keep.BtnLink");
                var link = step.Link;
                button.Click += (_, _) => link();
                row.Children.Add(button);
            }
            Children.Add(row);
        }
    }
}
