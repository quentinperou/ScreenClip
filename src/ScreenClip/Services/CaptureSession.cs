using System.Windows.Media.Imaging;
using ScreenClip.Core;

namespace ScreenClip.Services;

/// <summary>What the user chose on the frozen screen.</summary>
/// <param name="IsDraggedArea">A rectangle dragged by hand (remembered for “Capture the last area again”).</param>
/// <param name="IsAllScreens">Every screen at once: what lies between them is made transparent.</param>
internal readonly record struct CaptureResult(PixelRect Area, MonitorInfo Monitor, bool IsDraggedArea, bool IsAllScreens = false);

/// <summary>
/// One capture in progress: the screen frozen at the moment of the shortcut, shown by one overlay window per monitor
/// (each at its own DPI, so that every pixel is sharp), until the user chooses or cancels.
/// </summary>
internal sealed class CaptureSession
{
    private readonly List<CaptureOverlay> _overlays = [];
    private bool _finished;
    private ElementFinder? _finder;

    public CaptureSession(BitmapSource image, PixelRect screen, IReadOnlyList<MonitorInfo> monitors,
        IReadOnlyList<VisibleWindow> windows, CaptureMode mode)
    {
        Image = image;
        Screen = screen;
        Monitors = monitors;
        Windows = windows;
        WindowBounds = windows.Select(w => w.Bounds).ToList();
        Mode = mode;
    }

    /// <summary>The whole virtual screen, frozen.</summary>
    public BitmapSource Image { get; }

    /// <summary>Where <see cref="Image"/> sits in screen coordinates.</summary>
    public PixelRect Screen { get; }

    public IReadOnlyList<MonitorInfo> Monitors { get; }

    /// <summary>The windows of the other apps, front to back, as they were when the screen was frozen.</summary>
    public IReadOnlyList<VisibleWindow> Windows { get; }

    public IReadOnlyList<PixelRect> WindowBounds { get; }

    /// <summary>All the screens together (the rectangle around them).</summary>
    public PixelRect AllScreens => PixelRect.Union(Monitors.Select(m => m.Bounds)).Intersect(Screen);

    /// <summary>
    /// Every screen at once, as frozen, right away: there is nothing to choose (asked by the author: no click after
    /// the button). The thumbnail goes to the screen of the pointer.
    /// </summary>
    public void TakeAllScreens() =>
        Finish(new CaptureResult(AllScreens, ScreenGrabber.MonitorAtCursor(Monitors), IsDraggedArea: false, IsAllScreens: true));

    /// <summary>Whether there is more than one screen (the “All screens” mode is offered then).</summary>
    public bool HasSeveralScreens => Monitors.Count > 1;

    /// <summary>The front window under the point, if any (the desktop has none).</summary>
    public VisibleWindow? WindowUnder(int x, int y) => Windows.FirstOrDefault(w => w.Bounds.Contains(x, y));

    /// <summary>
    /// What was found under a point: the window, the screen point, the UI elements the app exposes (outermost first)
    /// and the blocks seen on the frozen screen.
    /// </summary>
    public event Action<VisibleWindow, int, int, IReadOnlyList<PixelRect>, IReadOnlyList<PixelRect>>? ElementsFound;

    /// <summary>
    /// Looks for the UI elements and the blocks under the point in <paramref name="window"/>, in the background; the
    /// answer comes back through <see cref="ElementsFound"/>, on the UI thread. Only the latest request counts.
    /// </summary>
    public void FindElements(VisibleWindow window, int x, int y)
    {
        // The blocks are read on the part of the window on the monitor of the point (a block cannot span two).
        var monitor = Monitors.FirstOrDefault(m => m.Bounds.Contains(x, y));
        if (monitor is null)
            return;
        _finder ??= new ElementFinder(Image, Screen,
            (request, elements, boxes) => ElementsFound?.Invoke(request.Window, request.X, request.Y, elements, boxes));
        _finder.Ask(new ElementFinder.Request(window, window.Bounds.Intersect(monitor.Bounds), x, y));
    }

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

        // Area: a first look under the pointer at once, so that the apps that build their tree on demand (Chrome, Edge,
        // Electron apps) start building it, and the blocks of that window are read, by the time the pointer moves.
        if (Mode == CaptureMode.Area && NativeMethods.GetCursorPos(out var pointer) && WindowUnder(pointer.X, pointer.Y) is { } window)
            FindElements(window, pointer.X, pointer.Y);
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
        _finder?.Dispose();
        foreach (var overlay in _overlays)
            overlay.Close();
        _overlays.Clear();
        Finished?.Invoke(this, result);
    }

    public void Cancel() => Finish(null);
}
