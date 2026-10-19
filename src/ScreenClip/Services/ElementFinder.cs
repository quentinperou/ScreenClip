using System.Diagnostics;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using ScreenClip.Core;

namespace ScreenClip.Services;

/// <summary>
/// Finds what lies under a point of a window, in two ways, on a thread of its own, for the latest point asked only. It
/// lives as long as a capture.
/// <list type="bullet">
/// <item>The UI elements the app exposes through UI Automation, the tree screen readers use (a video player, a card of a
/// page, a button…): it walks down from the window to the deepest element holding the point, one call to the app per
/// level, 100 to 200 ms for a web page. Browsers, Office and most apps expose one, games do not.</item>
/// <item>The blocks drawn on the frozen screen (<see cref="BoxDetector"/>): panels, cards, pictures, whatever the app
/// exposes. Chrome shows the elements of its pages only once it believes a screen reader is there, which ScreenClip
/// cannot make sure of.</item>
/// </list>
/// <para>
/// Chrome, Edge and the Electron apps also build a page's tree a little after a client asks (measured: depth 9, then 41
/// a second later), and do not always link it to the tree of their window: so the window that holds the page
/// (Chrome_RenderWidgetHostHWND) is walked as well, and for a while after the first look into a window the last point
/// is looked at again until a new one comes.
/// </para>
/// </summary>
internal sealed class ElementFinder : IDisposable
{
    private const int MaxDepth = 60;
    private const int Candidates = 3;                            // per level, when a branch ends early
    private const int ChildWindows = 2;                          // the smallest child windows under the point walked too
    private static readonly TimeSpan Budget = TimeSpan.FromMilliseconds(600);
    private static readonly TimeSpan WarmUp = TimeSpan.FromSeconds(3);      // looks again for that long after the first one
    private static readonly TimeSpan Again = TimeSpan.FromMilliseconds(250); // between two looks at the same point

    private readonly BitmapSource _screenImage;
    private readonly PixelRect _screen;
    private readonly Action<Request, IReadOnlyList<PixelRect>, IReadOnlyList<PixelRect>> _found;
    private readonly Dispatcher _dispatcher = Dispatcher.CurrentDispatcher;
    private readonly AutoResetEvent _wake = new(false);
    private readonly object _lock = new();
    private Request? _pending;
    private volatile bool _stopped;

    /// <summary>A point to look under: the window there, and the part of it to read on the frozen screen.</summary>
    internal sealed record Request(VisibleWindow Window, PixelRect Region, int X, int Y);

    /// <param name="screenImage">The frozen screen (frozen, so that this thread can read it).</param>
    /// <param name="found">The elements (outermost first) and the blocks seen, on the UI thread.</param>
    public ElementFinder(BitmapSource screenImage, PixelRect screen, Action<Request, IReadOnlyList<PixelRect>, IReadOnlyList<PixelRect>> found)
    {
        _screenImage = screenImage;
        _screen = screen;
        _found = found;
        // UI Automation clients work best away from the UI thread (calls into other apps can take a while).
        new Thread(Run) { IsBackground = true, Name = "ScreenClip element finder" }.Start();
    }

    public void Ask(Request request)
    {
        if (_stopped)
            return; // the capture is over (a last pointer move): the thread and its event are gone
        lock (_lock)
            _pending = request;
        _wake.Set();
    }

    private void Run()
    {
        var cache = new CacheRequest { TreeFilter = Automation.RawViewCondition };
        cache.Add(AutomationElement.BoundingRectangleProperty);
        var clock = Stopwatch.StartNew();
        var firstLook = new Dictionary<IntPtr, TimeSpan>();
        var detectors = new Dictionary<PixelRect, BoxDetector?>();
        Request? again = null;
        while (!_stopped)
        {
            // A new point wakes the thread at once; without one, the last point is looked at again while its window
            // may still be building its tree.
            if (again is null)
                _wake.WaitOne();
            else
                _wake.WaitOne(Again);
            Request? request;
            lock (_lock)
            {
                request = _pending ?? again;
                _pending = null;
            }
            if (_stopped || request is null)
                continue;
            var started = clock.Elapsed;
            if (!firstLook.TryGetValue(request.Window.Handle, out var first))
                firstLook[request.Window.Handle] = first = clock.Elapsed;
            again = clock.Elapsed - first < WarmUp ? request : null;

            IReadOnlyList<PixelRect> elements;
            try
            {
                using (cache.Activate())
                    elements = FindElements(request.Window.Handle, request.X, request.Y);
            }
            catch (Exception ex) when (ex is ElementNotAvailableException or InvalidOperationException or System.Runtime.InteropServices.COMException or ArgumentException)
            {
                elements = []; // the window closed or does not answer: the window itself will do
            }

            if (!detectors.TryGetValue(request.Region, out var detector))
                detectors[request.Region] = detector = Detector(request.Region);
            var boxes = detector is null
                ? []
                : detector.BoxesAt(request.X - request.Region.X, request.Y - request.Region.Y)
                    .Select(b => new PixelRect(b.X + request.Region.X, b.Y + request.Region.Y, b.Width, b.Height)).ToList();

            if (TracePath is { } trace)
                Trace(trace, request, elements, boxes, clock.Elapsed - started);
            if (!_stopped)
                _dispatcher.BeginInvoke(() => _found(request, elements, boxes));
        }
        _wake.Dispose();
    }

    // SCREENCLIP_TRACE=1: every look is written to %TEMP%\ScreenClip-trace.txt (to see what an app exposes).
    private static readonly string? TracePath =
        Environment.GetEnvironmentVariable("SCREENCLIP_TRACE") == "1" ? Path.Combine(Path.GetTempPath(), "ScreenClip-trace.txt") : null;

    private static void Trace(string path, Request request, IReadOnlyList<PixelRect> elements, IReadOnlyList<PixelRect> boxes, TimeSpan took)
    {
        try
        {
            static string Smallest(IReadOnlyList<PixelRect> rects) =>
                rects.Count == 0 ? "-" : rects.MinBy(r => (long)r.Width * r.Height).ToString();
            File.AppendAllText(path, $"{DateTime.Now:HH:mm:ss.fff} window {request.Window.Handle} at {request.X},{request.Y}: "
                + $"{elements.Count} elements (smallest {Smallest(elements)}), {boxes.Count} blocks (smallest {Smallest(boxes)}), "
                + $"{took.TotalMilliseconds:0} ms{Environment.NewLine}");
        }
        catch (IOException)
        {
        }
    }

    // The blocks drawn on that part of the frozen screen (built once per window and monitor in a capture: ~15 ms for a
    // full HD window).
    private BoxDetector? Detector(PixelRect region)
    {
        var part = region.Intersect(_screen);
        if (part.Width < BoxDetector.MinimumSide || part.Height < BoxDetector.MinimumSide)
            return null;
        int stride = part.Width * 4;
        var pixels = new byte[stride * part.Height];
        _screenImage.CopyPixels(new Int32Rect(part.X - _screen.X, part.Y - _screen.Y, part.Width, part.Height), pixels, stride, 0);
        return new BoxDetector(pixels, part.Width, part.Height, stride);
    }

    // The elements under the point, from the window and from the smallest child windows there (a browser's page lives
    // in a window of its own, whose tree is not always linked to the window's): one list, outermost first per walk.
    private static IReadOnlyList<PixelRect> FindElements(IntPtr window, int x, int y)
    {
        if (window == IntPtr.Zero)
            return []; // a made-up window (DemoScreen): no app to ask
        var clock = Stopwatch.StartNew();
        var elements = Descend(AutomationElement.FromHandle(window), x, y, 0, clock);
        foreach (var child in ChildWindowsAt(window, x, y).Take(ChildWindows))
        {
            if (clock.Elapsed > Budget)
                break;
            elements.AddRange(Descend(AutomationElement.FromHandle(child), x, y, 0, clock));
        }
        return elements;
    }

    // The visible child windows of a window that hold the point, the smallest first.
    private static List<IntPtr> ChildWindowsAt(IntPtr window, int x, int y)
    {
        var found = new List<(IntPtr Handle, long Area)>();
        NativeMethods.EnumChildWindows(window, (child, _) =>
        {
            if (NativeMethods.IsWindowVisible(child) && NativeMethods.GetWindowRect(child, out var r)
                && x >= r.Left && x < r.Right && y >= r.Top && y < r.Bottom)
                found.Add((child, (long)(r.Right - r.Left) * (r.Bottom - r.Top)));
            return true;
        }, IntPtr.Zero);
        return [.. found.OrderBy(c => c.Area).Select(c => c.Handle)];
    }

    // Several children can hold the point (layers of the same size, one of them empty): the smallest first, then the
    // next ones if that branch ends at once.
    private static List<PixelRect> Descend(AutomationElement element, int x, int y, int depth, Stopwatch clock)
    {
        if (depth >= MaxDepth || clock.Elapsed > Budget)
            return [];
        var children = element.FindAll(TreeScope.Children, System.Windows.Automation.Condition.TrueCondition);
        var candidates = new List<(AutomationElement Element, PixelRect Bounds)>();
        foreach (AutomationElement child in children)
        {
            var bounds = (Rect)child.GetCachedPropertyValue(AutomationElement.BoundingRectangleProperty);
            if (bounds.IsEmpty || double.IsInfinity(bounds.Width) || bounds.Width < 1 || bounds.Height < 1)
                continue;
            var rect = new PixelRect((int)Math.Round(bounds.X), (int)Math.Round(bounds.Y), (int)Math.Round(bounds.Width), (int)Math.Round(bounds.Height));
            if (rect.Contains(x, y))
                candidates.Add((child, rect));
        }

        var best = new List<PixelRect>();
        foreach (var (child, rect) in candidates.OrderBy(c => (long)c.Bounds.Width * c.Bounds.Height).Take(Candidates))
        {
            var path = Descend(child, x, y, depth + 1, clock);
            path.Insert(0, rect);
            if (path.Count > best.Count)
                best = path;
            if (best.Count > 1)
                break; // this branch goes deeper: the others are not needed
        }
        return best;
    }

    public void Dispose()
    {
        _stopped = true;
        _wake.Set();
    }
}
