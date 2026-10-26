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
        // SCREENCLIP_RECORD_SIZE=1800x1000 and SCREENCLIP_RECORD_PICTURE=<file>: a large window whose whole picture
        // moves at each frame, as a video played on the screen (the heaviest to decode).
        var size = Environment.GetEnvironmentVariable("SCREENCLIP_RECORD_SIZE")?.Split('x');
        double width = size is { Length: 2 } && double.TryParse(size[0], out double w0) ? w0 : 480;
        double height = size is { Length: 2 } && double.TryParse(size[1], out double h0) ? h0 : 300;
        ImageBrush? picture = Environment.GetEnvironmentVariable("SCREENCLIP_RECORD_PICTURE") is { Length: > 0 } file && File.Exists(file)
            ? new ImageBrush(new System.Windows.Media.Imaging.BitmapImage(new Uri(file))) { TileMode = TileMode.FlipXY, ViewportUnits = BrushMappingMode.Absolute, Stretch = Stretch.None }
            : null;
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
            Top = 40,
            Width = width,
            Height = height,
            Content = root,
        };
        var clock = System.Diagnostics.Stopwatch.StartNew();
        var animation = new DispatcherTimer(DispatcherPriority.Render) { Interval = TimeSpan.FromMilliseconds(16) };
        // SCREENCLIP_RECORD_STILL=1: the window stops moving after a second (only the pointer moves then).
        bool still = Environment.GetEnvironmentVariable("SCREENCLIP_RECORD_STILL") == "1";
        animation.Tick += (_, _) =>
        {
            double t = clock.Elapsed.TotalSeconds;
            if (still && t > 1)
                return;
            text.Text = $"{t:0.0} s";
            if (picture is not null)
            {
                picture.Viewport = new Rect(-t * 37 % 940, -t * 23 % 680, 940, 680);
                root.Background = picture;
            }
            else
                root.Background = new SolidColorBrush(Color.FromRgb((byte)(120 + 100 * Math.Sin(t * 1.3)), 60, (byte)(150 + 90 * Math.Cos(t * 0.9))));
            ball.Margin = new Thickness(width / 2 - 25 + (width / 2 - 60) * Math.Sin(t * 2), height / 2 - 25 + (height / 2 - 60) * Math.Cos(t * 3), 0, 0);
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
            // The pointer recorded apart follows a made-up way over the window, with a click every 1.2 s.
            float w = area.Width, h = area.Height;
            PointerRecorder.Fake = t =>
            {
                double s = t.TotalSeconds;
                return ((float)(w * (0.5 + 0.32 * Math.Cos(s * 1.7))), (float)(h * (0.5 + 0.3 * Math.Sin(s * 2.3))), s % 1.2 is > 0.6 and < 0.75);
            };
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
                    PointerRecorder.Fake = null;
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
