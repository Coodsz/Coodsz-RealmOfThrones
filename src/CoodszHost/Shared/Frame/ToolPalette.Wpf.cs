using System.Text.RegularExpressions;
using System.Windows.Media;

namespace Coodsz.Ui;

// The brushes of ToolPalette: the same colour unless the tool swapped it. Only solid colours swap; a see-through one
// stays as written.
public static partial class ToolPalette
{
    public static Color Of(Color color)
    {
        if (color.A != 255)
            return color;
        var rgb = Of(color.R << 16 | color.G << 8 | color.B);
        return Color.FromRgb((byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb);
    }

    public static SolidColorBrush Brush(byte r, byte g, byte b) => new(Of(Color.FromRgb(r, g, b)));

    /// <summary>The same swap for the #FFRRGGBB colours of a style written as text (the Donate menu).</summary>
    public static string InXaml(string xaml) => _swaps == null ? xaml : Regex.Replace(xaml, "#FF([0-9A-Fa-f]{6})", match =>
    {
        var rgb = Convert.ToInt32(match.Groups[1].Value, 16);
        var swapped = Of(rgb);
        return swapped == rgb ? match.Value : $"#FF{swapped:X6}";
    });
}
