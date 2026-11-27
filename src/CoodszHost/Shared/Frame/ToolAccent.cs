using System.Windows;
using System.Windows.Media;

namespace Coodsz.Ui;

/// <summary>
/// Sets the tool's own colour (Keep.ToolAccent, Keep.ToolAccentInk) from the table in FrameRules. Call it in
/// App.OnStartup before the first window opens; the frame reads both keys as DynamicResource.
/// </summary>
public static class ToolAccent
{
    public const string EdgeKey = "Keep.ToolEdge";

    /// <summary>True when the tool's colour file has its own Keep.ToolEdge (Realm of Thrones), so the Join my server
    /// strip uses it too; otherwise the selected page edge is the tool colour and the strip stays gold.</summary>
    public static bool OwnEdge { get; private set; }

    public static void Apply(ResourceDictionary resources, string tool)
    {
        var accent = FrameRules.Accent(tool);
        resources["Keep.ToolAccent"] = FrameBrushes.Frozen(accent.Deep);
        resources["Keep.ToolAccentInk"] = FrameBrushes.Frozen(accent.Ink);
        // The indexer looks in the merged colour file too.
        OwnEdge = resources[EdgeKey] is Brush;
        if (!OwnEdge)
            resources[EdgeKey] = FrameBrushes.Frozen(accent.Ink);
    }
}

/// <summary>Keep brushes for the frame controls, with a fixed colour when a tool has not loaded the theme.</summary>
public static class FrameBrushes
{
    public static SolidColorBrush Frozen(string hex)
    {
        var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
        brush.Freeze();
        return brush;
    }

    public static Brush Get(FrameworkElement scope, string key, string fallback) =>
        scope.TryFindResource(key) as Brush ?? Application.Current?.TryFindResource(key) as Brush ?? Frozen(fallback);

    /// <summary>A Keep brush of the app, for code with no element at hand.</summary>
    public static Brush Get(string key, string fallback) =>
        Application.Current?.TryFindResource(key) as Brush ?? Frozen(fallback);

    /// <summary>The word and dot colour, and the fill, of a chip kind.</summary>
    public static (Brush Ink, Brush Back) Chip(FrameworkElement scope, ChipKind kind)
    {
        var (inkKey, inkFallback, backKey, backFallback) = FrameRules.Chip(kind);
        return (Get(scope, inkKey, inkFallback), Get(scope, backKey, backFallback));
    }
}
