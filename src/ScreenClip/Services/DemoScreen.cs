using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using ScreenClip.Core;

namespace ScreenClip.Services;

/// <summary>
/// For the pictures of the README: <c>SCREENCLIP_DEMO_SCREEN=&lt;picture&gt;</c> makes a capture freeze that picture on
/// every monitor instead of the real screen, with one made-up window per monitor, so that the real capture overlay can
/// be shown and photographed on a made-up desktop, whatever is on the author's screens.
/// </summary>
internal static class DemoScreen
{
    private static readonly string? PicturePath = Environment.GetEnvironmentVariable("SCREENCLIP_DEMO_SCREEN") is { Length: > 0 } path
        && File.Exists(path) ? path : null;

    public static bool IsOn => PicturePath is not null;

    /// <summary>The picture stretched over each monitor, as one frozen screen.</summary>
    public static BitmapSource Grab(PixelRect screen, IReadOnlyList<MonitorInfo> monitors)
    {
        var picture = Imaging.Load(PicturePath!);
        var visual = new DrawingVisual();
        using (var context = visual.RenderOpen())
        {
            foreach (var monitor in monitors)
            {
                var b = monitor.Bounds;
                context.DrawImage(picture, new Rect(b.X - screen.X, b.Y - screen.Y, b.Width, b.Height));
            }
        }
        var target = new RenderTargetBitmap(screen.Width, screen.Height, 96, 96, PixelFormats.Pbgra32);
        target.Render(visual);
        var frozen = new FormatConvertedBitmap(target, PixelFormats.Bgr32, null, 0);
        frozen.Freeze();
        return frozen;
    }

    /// <summary>
    /// A window over each monitor, with no handle: the Area mode finds its blocks on the picture (there is no app to
    /// ask for elements).
    /// </summary>
    public static IReadOnlyList<VisibleWindow> Windows(IReadOnlyList<MonitorInfo> monitors) =>
        [.. monitors.Select(m => new VisibleWindow(IntPtr.Zero, m.Bounds))];
}
