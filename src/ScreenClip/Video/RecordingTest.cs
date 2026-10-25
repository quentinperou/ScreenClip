#if DEBUG
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using ScreenClip.Core;
using ScreenClip.Services;

namespace ScreenClip.Video;

/// <summary>
/// Debug builds only (<c>SCREENCLIP_RECORD_TEST=&lt;seconds&gt;</c>): records a window of its own, moving, for that long,
/// through the real recording (countdown, bar, recorder, editor), so that the video can be checked without filming
/// the screen of the person at the PC.
/// </summary>
internal static class RecordingTest
{
    public static void Run(double seconds)
    {
        App.EnsureTheme();
        var text = new TextBlock { FontSize = 48, FontWeight = FontWeights.SemiBold, Foreground = Brushes.White, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        var ball = new System.Windows.Shapes.Ellipse { Width = 50, Height = 50, Fill = Brushes.White, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top };
        var root = new Grid { Children = { ball, text } };
        var window = new Window
        {
            Title = "ScreenClip recording test",
            WindowStyle = WindowStyle.None,
            ResizeMode = ResizeMode.NoResize,
            Topmost = true,
            ShowInTaskbar = false,
            ShowActivated = false,
            WindowStartupLocation = WindowStartupLocation.Manual,
            Left = 60,
            Top = 60,
            Width = 480,
            Height = 300,
            Content = root,
        };
        var clock = System.Diagnostics.Stopwatch.StartNew();
        var animation = new DispatcherTimer(DispatcherPriority.Render) { Interval = TimeSpan.FromMilliseconds(16) };
        animation.Tick += (_, _) =>
        {
            double t = clock.Elapsed.TotalSeconds;
            text.Text = $"{t:0.0} s";
            root.Background = new SolidColorBrush(Color.FromRgb((byte)(120 + 100 * Math.Sin(t * 1.3)), 60, (byte)(150 + 90 * Math.Cos(t * 0.9))));
            ball.Margin = new Thickness(215 + 180 * Math.Sin(t * 2), 125 + 90 * Math.Cos(t * 3), 0, 0);
        };
        animation.Start();
        window.Show();

        // The area: the window, in screen pixels, once it is shown.
        var start = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(400) };
        start.Tick += (_, _) =>
        {
            start.Stop();
            NativeMethods.GetWindowRect(new WindowInteropHelper(window).Handle, out var r);
            var area = new PixelRect(r.Left, r.Top, r.Right - r.Left, r.Bottom - r.Top);
            var monitors = ScreenGrabber.Monitors();
            RecordingSession.Begin(area, CaptureController.MonitorOf(area, monitors));
            var stop = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3.2 + seconds) };
            stop.Tick += (_, _) =>
            {
                stop.Stop();
                RecordingSession.Current?.Stop();
                var close = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
                close.Tick += (_, _) =>
                {
                    close.Stop();
                    animation.Stop();
                    window.Close();
                };
                close.Start();
            };
            stop.Start();
        };
        start.Start();
    }
}
#endif
