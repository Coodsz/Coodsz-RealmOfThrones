namespace BannerlordCoopLauncher.Core.Support;

public readonly record struct WindowSize(double Width, double Height, double MinWidth, double MinHeight);

public readonly record struct ScreenRect(double Left, double Top, double Width, double Height)
{
    public double Right => Left + Width;
    public double Bottom => Top + Height;
    public bool IsUsable => Width > 0 && Height > 0 && double.IsFinite(Left) && double.IsFinite(Top) && double.IsFinite(Width) && double.IsFinite(Height);
}

/// <summary>
/// Fits a tool window into the screen's work area. A window bigger than the screen (1400 wide on a
/// 1366 px screen at 125% scaling, about 1093 logical px) opened with its right side off screen,
/// and the Install update button there could not be reached.
/// </summary>
public static class WindowFit
{
    public static WindowSize Fit(WindowSize wanted, double workWidth, double workHeight)
    {
        if (!(workWidth > 0) || !(workHeight > 0) || double.IsInfinity(workWidth) || double.IsInfinity(workHeight))
            return wanted;
        var minWidth = Math.Min(wanted.MinWidth, workWidth);
        var minHeight = Math.Min(wanted.MinHeight, workHeight);
        var width = Math.Max(minWidth, Math.Min(wanted.Width, workWidth));
        var height = Math.Max(minHeight, Math.Min(wanted.Height, workHeight));
        return new WindowSize(width, height, minWidth, minHeight);
    }

    /// <summary>
    /// Where a popup goes: centered on its owner (or on the work area when there is no owner),
    /// then moved so the whole window is inside the work area. The Send my logs window opened
    /// at 900,616 and 1001 px tall on a 1440 px screen, so Send fell below the taskbar.
    /// </summary>
    public static (double Left, double Top) Place(double width, double height, ScreenRect? owner, ScreenRect work)
    {
        var around = owner is { IsUsable: true } o ? o : work;
        return Clamp(around.Left + (around.Width - width) / 2, around.Top + (around.Height - height) / 2, width, height, work);
    }

    /// <summary>Moves a window so it is fully inside the work area. A window bigger than the work area keeps its top left corner on screen.</summary>
    public static (double Left, double Top) Clamp(double left, double top, double width, double height, ScreenRect work)
    {
        if (!work.IsUsable || !double.IsFinite(width) || !double.IsFinite(height))
            return (left, top);
        if (!double.IsFinite(left)) left = work.Left;
        if (!double.IsFinite(top)) top = work.Top;
        left = Math.Max(work.Left, Math.Min(left, work.Right - width));
        top = Math.Max(work.Top, Math.Min(top, work.Bottom - height));
        return (left, top);
    }
}
