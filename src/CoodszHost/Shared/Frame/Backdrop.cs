using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;

namespace Coodsz.Ui;

/// <summary>
/// The tool's picture behind the pages (Keep.BackdropArt). A Keep.BackdropBlur above 0 blurs it once at its own size,
/// so resizing never redraws the blur; 0 (the default) shows it sharp. A tool that never calls Apply has no
/// Keep.BackdropArt and shows the plain background in the same layout.
/// </summary>
public static class Backdrop
{
    public const string ArtKey = "Keep.BackdropArt";

    public static void Apply(ResourceDictionary resources, Uri picture)
    {
        try
        {
            var source = new BitmapImage(picture);
            var radius = resources.Contains("Keep.BackdropBlur") ? (double)resources["Keep.BackdropBlur"] : FindBlur();
            source.Freeze();
            resources[ArtKey] = radius > 0 ? Blurred(source, radius) : source;
        }
        catch (Exception ex) when (ex is IOException or NotSupportedException or InvalidOperationException or ArgumentException)
        {
            // No picture: the plain background, the pages work the same.
        }
    }

    // The blur from KeepTheme.xaml (merged into the app), none when the theme is not loaded yet.
    private static double FindBlur() =>
        Application.Current?.TryFindResource("Keep.BackdropBlur") is double blur ? blur : 0;

    private static BitmapSource Blurred(BitmapSource source, double radius)
    {
        var width = source.PixelWidth;
        var height = source.PixelHeight;
        // Drawn a radius bigger on each side and cut back, so the blur does not darken the edges.
        var image = new Image
        {
            Source = source,
            Stretch = Stretch.Fill,
            Width = width + 2 * radius,
            Height = height + 2 * radius,
            Effect = new BlurEffect { Radius = radius, KernelType = KernelType.Gaussian },
        };
        var host = new Canvas { Width = width, Height = height, ClipToBounds = true };
        Canvas.SetLeft(image, -radius);
        Canvas.SetTop(image, -radius);
        host.Children.Add(image);
        host.Measure(new Size(width, height));
        host.Arrange(new Rect(0, 0, width, height));
        var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(host);
        bitmap.Freeze();
        return bitmap;
    }
}
