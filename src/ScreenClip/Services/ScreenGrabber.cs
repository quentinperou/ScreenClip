using System.Runtime.InteropServices;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using ScreenClip.Core;
using static ScreenClip.Services.NativeMethods;

namespace ScreenClip.Services;

/// <summary>A monitor, in physical pixels of the virtual screen (the app is per-monitor DPI aware).</summary>
internal sealed record MonitorInfo(PixelRect Bounds, PixelRect WorkArea, bool IsPrimary);

/// <summary>Reads the screen: monitors, visible windows, and the pixels themselves (GDI, the fastest for a still).</summary>
internal static class ScreenGrabber
{
    /// <summary>The rectangle that holds every monitor.</summary>
    public static PixelRect VirtualScreen => new(
        GetSystemMetrics(SM_XVIRTUALSCREEN), GetSystemMetrics(SM_YVIRTUALSCREEN),
        GetSystemMetrics(SM_CXVIRTUALSCREEN), GetSystemMetrics(SM_CYVIRTUALSCREEN));

    public static IReadOnlyList<MonitorInfo> Monitors()
    {
        var monitors = new List<MonitorInfo>();
        EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, (IntPtr monitor, IntPtr _, ref RECT _, IntPtr _) =>
        {
            var info = new MONITORINFO { cbSize = Marshal.SizeOf<MONITORINFO>() };
            if (GetMonitorInfo(monitor, ref info))
                monitors.Add(new MonitorInfo(ToPixelRect(info.rcMonitor), ToPixelRect(info.rcWork), (info.dwFlags & MONITORINFOF_PRIMARY) != 0));
            return true;
        }, IntPtr.Zero);
        return monitors;
    }

    /// <summary>The monitor under the pointer (the primary one if none).</summary>
    public static MonitorInfo MonitorAtCursor(IReadOnlyList<MonitorInfo> monitors)
    {
        GetCursorPos(out var point);
        return monitors.FirstOrDefault(m => m.Bounds.Contains(point.X, point.Y))
            ?? monitors.FirstOrDefault(m => m.IsPrimary)
            ?? monitors[0];
    }

    /// <summary>
    /// The pixels of <paramref name="area"/> as they are on screen now (without the pointer), as a frozen 96 dpi
    /// bitmap: one bitmap pixel per screen pixel.
    /// </summary>
    public static BitmapSource Grab(PixelRect area)
    {
        IntPtr screen = GetDC(IntPtr.Zero);
        IntPtr memory = CreateCompatibleDC(screen);
        var header = new BITMAPINFOHEADER
        {
            biSize = Marshal.SizeOf<BITMAPINFOHEADER>(),
            biWidth = area.Width,
            biHeight = -area.Height, // top-down rows, like WPF bitmaps
            biPlanes = 1,
            biBitCount = 32,
        };
        IntPtr bitmap = CreateDIBSection(screen, ref header, 0, out IntPtr bits, IntPtr.Zero, 0);
        try
        {
            if (bitmap == IntPtr.Zero)
                throw new OutOfMemoryException();
            IntPtr previous = SelectObject(memory, bitmap);
            // CAPTUREBLT: layered windows (menus, tooltips, translucent windows) are part of the picture too.
            BitBlt(memory, 0, 0, area.Width, area.Height, screen, area.X, area.Y, SRCCOPY | CAPTUREBLT);
            SelectObject(memory, previous);

            int stride = area.Width * 4;
            var source = BitmapSource.Create(area.Width, area.Height, 96, 96, PixelFormats.Bgr32, null, bits, stride * area.Height, stride);
            source.Freeze();
            return source;
        }
        finally
        {
            if (bitmap != IntPtr.Zero)
                DeleteObject(bitmap);
            DeleteDC(memory);
            ReleaseDC(IntPtr.Zero, screen);
        }
    }

    /// <summary>
    /// The visible top-level windows of the other apps, from front to back, by their visible bounds (without the
    /// invisible resize borders of Windows 10/11). The desktop and the windows hidden by Windows are left out.
    /// </summary>
    public static IReadOnlyList<PixelRect> VisibleWindows()
    {
        int ownProcess = Environment.ProcessId;
        var windows = new List<PixelRect>();
        var className = new char[64];
        EnumWindows((hwnd, _) =>
        {
            if (!IsWindowVisible(hwnd) || IsIconic(hwnd))
                return true;
            GetWindowThreadProcessId(hwnd, out int process);
            if (process == ownProcess)
                return true;
            // Windows of other virtual desktops and suspended Store apps are "cloaked": visible for Windows, not on screen.
            if (DwmGetWindowAttribute(hwnd, DWMWA_CLOAKED, out int cloaked, sizeof(int)) == 0 && cloaked != 0)
                return true;
            // Click-through overlays (game bars, screen recorders) are not windows the user sees as such.
            if (((long)GetWindowLongPtr(hwnd, GWL_EXSTYLE) & WS_EX_TRANSPARENT) != 0)
                return true;
            string name = new(className, 0, GetClassName(hwnd, className, className.Length));
            if (name is "Progman" or "WorkerW")
                return true; // the desktop: a click there takes the screen

            if (DwmGetWindowAttribute(hwnd, DWMWA_EXTENDED_FRAME_BOUNDS, out RECT bounds, Marshal.SizeOf<RECT>()) != 0)
                GetWindowRect(hwnd, out bounds);
            var rect = ToPixelRect(bounds);
            if (rect.Width >= 8 && rect.Height >= 8)
                windows.Add(rect);
            return true;
        }, IntPtr.Zero);
        return windows;
    }

    private static PixelRect ToPixelRect(RECT r) => new(r.Left, r.Top, r.Right - r.Left, r.Bottom - r.Top);
}
