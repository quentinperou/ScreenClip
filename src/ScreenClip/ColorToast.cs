using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using ScreenClip.Services;
using static ScreenClip.Services.NativeMethods;

namespace ScreenClip;

/// <summary>
/// A small bubble by the pointer that says what the color picker copied (“#3A7BD5 copied”, with its swatch). Never
/// activated, not in the next captures, gone after a moment; it outlives the frozen screen it came from.
/// </summary>
internal sealed class ColorToast : Window
{
    private static readonly TimeSpan Lifetime = TimeSpan.FromMilliseconds(1400);
    private static ColorToast? _current;

    private readonly int _x, _y;

    private ColorToast(string text, Color color, int x, int y)
    {
        _x = x;
        _y = y;
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        ShowActivated = false;
        Topmost = true;
        SizeToContent = SizeToContent.WidthAndHeight;
        // Off screen until it is placed by the pointer, once its size is known.
        Left = Top = -10_000;
        Content = new Border
        {
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(9, 6, 12, 6),
            Background = new SolidColorBrush(Color.FromArgb(0xE6, 0x20, 0x20, 0x24)),
            Child = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Children =
                {
                    new Border
                    {
                        Width = 16,
                        Height = 16,
                        CornerRadius = new CornerRadius(4),
                        Background = new SolidColorBrush(color),
                        BorderBrush = new SolidColorBrush(Color.FromArgb(0x90, 0xFF, 0xFF, 0xFF)),
                        BorderThickness = new Thickness(1),
                        VerticalAlignment = VerticalAlignment.Center,
                    },
                    new TextBlock
                    {
                        Text = text,
                        Foreground = new SolidColorBrush(Color.FromRgb(0xF2, 0xF2, 0xF2)),
                        FontSize = 13,
                        Margin = new Thickness(8, 0, 0, 0),
                        VerticalAlignment = VerticalAlignment.Center,
                    },
                },
            },
        };
        SourceInitialized += (_, _) =>
        {
            var hwnd = new WindowInteropHelper(this).Handle;
            long style = (long)GetWindowLongPtr(hwnd, GWL_EXSTYLE);
            SetWindowLongPtr(hwnd, GWL_EXSTYLE, new IntPtr(style | WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW));
            if (!App.CapturableWindows)
                SetWindowDisplayAffinity(hwnd, WDA_EXCLUDEFROMCAPTURE);
        };
        Loaded += (_, _) => Place();
        var timer = new DispatcherTimer { Interval = Lifetime };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            Close();
        };
        timer.Start();
    }

    /// <summary>Shows <paramref name="text"/> by the screen point (<paramref name="x"/>, <paramref name="y"/>), in place of the one before.</summary>
    public static void Pop(string text, Color color, int x, int y)
    {
        _current?.Close();
        var toast = new ColorToast(text, color, x, y);
        toast.Closed += (_, _) =>
        {
            if (_current == toast)
                _current = null;
        };
        _current = toast;
        toast.Show();
    }

    // Below right of the pointer, or on the other side near the edges of its screen; over the frozen screen too.
    private void Place()
    {
        var dpi = VisualTreeHelper.GetDpi(this);
        int width = (int)Math.Ceiling(ActualWidth * dpi.DpiScaleX), height = (int)Math.Ceiling(ActualHeight * dpi.DpiScaleY);
        var monitor = ScreenGrabber.Monitors().FirstOrDefault(m => m.Bounds.Contains(_x, _y))?.Bounds ?? ScreenGrabber.VirtualScreen;
        int gap = (int)Math.Round(18 * dpi.DpiScaleX);
        int left = _x + gap + width > monitor.Right ? _x - gap - width : _x + gap;
        int top = _y + gap + height > monitor.Bottom ? _y - gap - height : _y + gap;
        SetWindowPos(new WindowInteropHelper(this).Handle, HWND_TOPMOST, left, top, 0, 0, SWP_NOSIZE | SWP_NOACTIVATE);
    }
}
