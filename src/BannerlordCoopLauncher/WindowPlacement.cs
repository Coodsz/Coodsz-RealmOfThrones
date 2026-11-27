using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using BannerlordCoopLauncher.Core.Support;

namespace Coodsz.Ui;

/// <summary>
/// Keeps a popup window fully inside the work area of the screen it opens on. WPF's CenterOwner
/// only places the window once, so a window that grows after it opens (Send my logs fills in when
/// the zip is ready) slid below the taskbar and Send could not be reached. Until the player drags
/// the window it is centered on its owner again each time it changes size, and it is always
/// moved back inside the work area.
/// </summary>
public static class WindowPlacement
{
    public static void KeepOnScreen(Window window)
    {
        var placed = false;
        var userMoved = false;
        double lastLeft = double.NaN, lastTop = double.NaN;

        void Place(bool center)
        {
            // Until the player drags it, the popup belongs on its owner's screen.
            var work = WorkArea(window, preferOwner: !userMoved);
            if (!work.IsUsable)
                return;
            var width = window.ActualWidth;
            var height = window.ActualHeight;
            if (!(width > 0) || !(height > 0))
                return;
            var (left, top) = center
                ? WindowFit.Place(width, height, OwnerRect(window), work)
                : WindowFit.Clamp(window.Left, window.Top, width, height, work);
            lastLeft = left;
            lastTop = top;
            if (Math.Abs(left - window.Left) < 0.5 && Math.Abs(top - window.Top) < 0.5)
                return;
            window.Left = left;
            window.Top = top;
        }

        window.SourceInitialized += (_, _) =>
        {
            var work = WorkArea(window, preferOwner: true);
            if (!work.IsUsable)
                return;
            // A window taller or wider than the screen gets smaller first, so the clamp can show all of it.
            window.MaxWidth = Math.Min(window.MaxWidth, work.Width);
            window.MaxHeight = Math.Min(window.MaxHeight, work.Height);
            window.MinWidth = Math.Min(window.MinWidth, work.Width);
            window.MinHeight = Math.Min(window.MinHeight, work.Height);
            var sizeToContent = window.SizeToContent;
            if (sizeToContent is SizeToContent.Manual or SizeToContent.Height && window.Width > work.Width)
                window.Width = work.Width;
            if (sizeToContent is SizeToContent.Manual or SizeToContent.Width && window.Height > work.Height)
                window.Height = work.Height;
        };
        window.Loaded += (_, _) =>
        {
            Place(center: true);
            placed = true;
        };
        window.SizeChanged += (_, _) =>
        {
            if (!placed || window.WindowState != WindowState.Normal)
                return;
            // A resize edge dragged by the player counts as moving it: only keep it on screen then.
            if (Mouse.LeftButton == MouseButtonState.Pressed)
                userMoved = true;
            // Only a window that grows with its content is centered again; a fixed size window is just kept inside.
            Place(center: !userMoved && window.SizeToContent != SizeToContent.Manual);
        };
        window.LocationChanged += (_, _) =>
        {
            // Compared with where this helper put it, so it works however late WPF raises the event.
            if (placed && (double.IsNaN(lastLeft) || Math.Abs(window.Left - lastLeft) > 1 || Math.Abs(window.Top - lastTop) > 1))
                userMoved = true;
        };
    }

    private static ScreenRect? OwnerRect(Window window)
    {
        var owner = window.Owner;
        if (owner == null || !owner.IsVisible || owner.WindowState == WindowState.Minimized)
            return null;
        var handle = new WindowInteropHelper(owner).Handle;
        if (handle == IntPtr.Zero || !GetWindowRect(handle, out var rect))
            return null;
        // In the popup's own units, the same ones as its Left and Top.
        return ToDips(window, owner, rect);
    }

    private static ScreenRect WorkArea(Window window, bool preferOwner)
    {
        var handle = IntPtr.Zero;
        if (preferOwner && window.Owner is { IsVisible: true } owner && owner.WindowState != WindowState.Minimized)
            handle = new WindowInteropHelper(owner).Handle;
        if (handle == IntPtr.Zero)
            handle = new WindowInteropHelper(window).Handle;
        if (handle == IntPtr.Zero && window.Owner != null)
            handle = new WindowInteropHelper(window.Owner).Handle;
        if (handle != IntPtr.Zero)
        {
            var monitor = MonitorFromWindow(handle, MonitorDefaultToNearest);
            var info = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };
            if (monitor != IntPtr.Zero && GetMonitorInfo(monitor, ref info))
                return ToDips(window, window.Owner, info.Work);
        }
        var area = SystemParameters.WorkArea;
        return new ScreenRect(area.Left, area.Top, area.Width, area.Height);
    }

    private static ScreenRect ToDips(Visual visual, Visual? fallback, NativeRect rect)
    {
        var source = PresentationSource.FromVisual(visual) ?? (fallback == null ? null : PresentationSource.FromVisual(fallback));
        var fromDevice = source?.CompositionTarget?.TransformFromDevice ?? Matrix.Identity;
        var topLeft = fromDevice.Transform(new Point(rect.Left, rect.Top));
        var bottomRight = fromDevice.Transform(new Point(rect.Right, rect.Bottom));
        return new ScreenRect(topLeft.X, topLeft.Y, bottomRight.X - topLeft.X, bottomRight.Y - topLeft.Y);
    }

    private const uint MonitorDefaultToNearest = 2;

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MonitorInfo
    {
        public int Size;
        public NativeRect Monitor;
        public NativeRect Work;
        public uint Flags;
    }

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint flags);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInfo info);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetWindowRect(IntPtr hwnd, out NativeRect rect);
}
