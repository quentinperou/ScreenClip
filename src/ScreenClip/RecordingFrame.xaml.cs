using System.Windows;
using System.Windows.Interop;
using ScreenClip.Core;
using static ScreenClip.Services.NativeMethods;

namespace ScreenClip;

/// <summary>
/// A dashed red line around the area being recorded, and the countdown before it starts. Every click goes through it,
/// and it is never in the video.
/// </summary>
public partial class RecordingFrame : Window
{
    private const int Inset = 3; // pixels between the area and the line

    private readonly PixelRect _area;

    internal RecordingFrame(PixelRect area)
    {
        _area = area;
        InitializeComponent();
        SourceInitialized += (_, _) =>
        {
            var hwnd = new WindowInteropHelper(this).Handle;
            long style = (long)GetWindowLongPtr(hwnd, GWL_EXSTYLE);
            SetWindowLongPtr(hwnd, GWL_EXSTYLE, new IntPtr(style | WS_EX_TRANSPARENT | WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW));
            if (!App.CapturableWindows)
                SetWindowDisplayAffinity(hwnd, WDA_EXCLUDEFROMCAPTURE);
            SetWindowPos(hwnd, HWND_TOPMOST, _area.X - Inset, _area.Y - Inset, _area.Width + 2 * Inset, _area.Height + 2 * Inset, SWP_NOACTIVATE);
        };
    }

    /// <summary>The countdown, or none.</summary>
    /// <summary>Whether the countdown's number shows.</summary>
    public bool ShowsCount => Countdown.Visibility == Visibility.Visible;

    public void ShowCount(int? seconds)
    {
        Countdown.Visibility = seconds is null ? Visibility.Collapsed : Visibility.Visible;
        CountText.Text = seconds?.ToString(System.Globalization.CultureInfo.CurrentCulture) ?? "";
    }
}
