using System.Windows.Media.Imaging;
using ScreenClip.Core;

namespace ScreenClip.Services;

/// <summary>What the user chose on the frozen screen.</summary>
/// <param name="IsDraggedArea">A rectangle dragged by hand (remembered for “Capture the last area again”).</param>
internal readonly record struct CaptureResult(PixelRect Area, MonitorInfo Monitor, bool IsDraggedArea);

/// <summary>
/// One capture in progress: the screen frozen at the moment of the shortcut, shown by one overlay window per monitor
/// (each at its own DPI, so that every pixel is sharp), until the user chooses or cancels.
/// </summary>
internal sealed class CaptureSession
{
    private readonly List<CaptureOverlay> _overlays = [];
    private bool _finished;

    public CaptureSession(BitmapSource image, PixelRect screen, IReadOnlyList<MonitorInfo> monitors,
        IReadOnlyList<PixelRect> windows, CaptureMode mode)
    {
        Image = image;
        Screen = screen;
        Monitors = monitors;
        Windows = windows;
        Mode = mode;
    }

    /// <summary>The whole virtual screen, frozen.</summary>
    public BitmapSource Image { get; }

    /// <summary>Where <see cref="Image"/> sits in screen coordinates.</summary>
    public PixelRect Screen { get; }

    public IReadOnlyList<MonitorInfo> Monitors { get; }

    /// <summary>The windows of the other apps, front to back, as they were when the screen was frozen.</summary>
    public IReadOnlyList<PixelRect> Windows { get; }

    public CaptureMode Mode { get; private set; }

    /// <summary>The choice, or null when cancelled. Raised once, after the overlays are closed.</summary>
    public event EventHandler<CaptureResult?>? Finished;

    public void Show()
    {
        var atCursor = ScreenGrabber.MonitorAtCursor(Monitors);
        CaptureOverlay? active = null;
        foreach (var monitor in Monitors)
        {
            var part = monitor.Bounds.Intersect(Screen);
            if (part.IsEmpty)
                continue;
            var image = new CroppedBitmap(Image, new System.Windows.Int32Rect(part.X - Screen.X, part.Y - Screen.Y, part.Width, part.Height));
            image.Freeze();
            var overlay = new CaptureOverlay(this, monitor, image, showBars: monitor == atCursor);
            _overlays.Add(overlay);
            overlay.Show();
            if (monitor == atCursor)
                active = overlay;
        }
        (active ?? _overlays.FirstOrDefault())?.Activate();
    }

    public void SetMode(CaptureMode mode)
    {
        Mode = mode;
        foreach (var overlay in _overlays)
            overlay.ModeChanged();
    }

    public void Finish(CaptureResult? result)
    {
        if (_finished)
            return;
        _finished = true;
        foreach (var overlay in _overlays)
            overlay.Close();
        _overlays.Clear();
        Finished?.Invoke(this, result);
    }

    public void Cancel() => Finish(null);
}
