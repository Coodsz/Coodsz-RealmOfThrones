namespace Coodsz.Ui;

/// <summary>
/// The colours the small windows built in code paint with (BrushOf in CrashReportWindow and the others). Every colour
/// stays as written unless a tool swaps it at start: only the Coop client launcher does, for the look of the Coop
/// Dedicated Server Manager (ManagerLook.Palette), so the other tools paint exactly the colours they always did.
/// This part has no WPF in it, so the tests can check it; the brushes are in ToolPalette.Wpf.cs.
/// </summary>
public static partial class ToolPalette
{
    private static IReadOnlyDictionary<int, int>? _swaps;

    /// <summary>Call first thing in the tool's App constructor, before any window or brush is made.</summary>
    public static void Use(IReadOnlyDictionary<int, int> swaps) => _swaps = swaps;

    /// <summary>A colour as 0xRRGGBB, the swapped one when the tool turned a table on.</summary>
    public static int Of(int rgb) => _swaps != null && _swaps.TryGetValue(rgb, out var swapped) ? swapped : rgb;

    // The tests turn the table off again, so one test's swap never reaches the next.
    internal static void Reset() => _swaps = null;
}
