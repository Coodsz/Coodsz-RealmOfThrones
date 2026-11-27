using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;

namespace Coodsz.Ui;

/// <summary>The local time (HH:mm) in the header, like the Coop manager's clock. It checks every second while shown, so it never lags behind the minute.</summary>
public class HeaderClock : TextBlock
{
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(1) };

    public HeaderClock()
    {
        FontFamily = new FontFamily("Consolas");
        FontSize = 13;
        VerticalAlignment = VerticalAlignment.Center;
        // The time is never translated or mirrored.
        Tag = UiLanguage.NoTranslate;
        FlowDirection = FlowDirection.LeftToRight;
        _timer.Tick += (_, _) => Tick();
        Loaded += (_, _) =>
        {
            if (ReadLocalValue(ForegroundProperty) == DependencyProperty.UnsetValue)
                Foreground = FrameBrushes.Get(this, "Keep.TextDim", "#8EA0AE");
            Tick();
            _timer.Start();
        };
        Unloaded += (_, _) => _timer.Stop();
    }

    private void Tick()
    {
        var now = DateTime.Now.ToString("HH:mm", CultureInfo.InvariantCulture);
        if (Text != now)
            Text = now;
    }
}
