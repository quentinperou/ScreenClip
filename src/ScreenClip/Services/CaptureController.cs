using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using ScreenClip.Core;

namespace ScreenClip.Services;

/// <summary>A capture just taken: its pixels, when, where on screen, and its file once saved.</summary>
internal sealed class Clip(BitmapSource image, DateTime takenAt, MonitorInfo monitor)
{
    public BitmapSource Image { get; } = image;
    public DateTime TakenAt { get; } = takenAt;

    /// <summary>The monitor it was taken on: its thumbnail shows there.</summary>
    public MonitorInfo Monitor { get; } = monitor;

    /// <summary>The file in the capture folder, once saved.</summary>
    public string? SavedPath { get; set; }

    /// <summary>A copy in the temp folder, made when the capture is dragged to an app or opened.</summary>
    public string? TempPath { get; set; }

    /// <summary>The saved file, else a temp copy made now: drag and drop and "Open" need a real file.</summary>
    public string FilePath => SavedPath ?? (TempPath ??= ClipActions.SaveTemp(Image, TakenAt, App.Settings));
}

/// <summary>
/// Runs a capture from a shortcut or the menu: freezes the screen, lets the user choose on it (area, window, screen),
/// then copies, saves and shows the thumbnail as the settings say.
/// </summary>
internal sealed class CaptureController
{
    private CaptureSession? _session;

    // Set while the editor waits for a capture: it gets the capture instead of the thumbnail, copy and save.
    private Action<Clip?>? _forEditor;

    public bool HasLastArea => PixelRect.TryParse(App.Settings.LastArea, out _);

    /// <summary>
    /// A capture for the editor (its “New capture” button): the editor has just hidden itself, so the capture waits a
    /// moment for it to be gone from the screen. <paramref name="done"/> gets the capture, or null when cancelled.
    /// </summary>
    public void RunForEditor(ShortcutAction action, Action<Clip?> done)
    {
        _forEditor = done;
        RunAfterMenu(action);
    }

    public void Run(ShortcutAction action)
    {
        switch (action)
        {
            case ShortcutAction.Area:
                Start(CaptureMode.Area);
                break;
            case ShortcutAction.Window:
                Start(CaptureMode.Window);
                break;
            case ShortcutAction.Screen:
                Start(CaptureMode.Screen);
                break;
            case ShortcutAction.RepeatArea:
                RepeatLastArea();
                break;
        }
    }

    /// <summary>From the tray menu: the menu fades out first, so that it is not in the picture.</summary>
    public void RunAfterMenu(ShortcutAction action)
    {
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            Run(action);
        };
        timer.Start();
    }

    private void Start(CaptureMode mode)
    {
        // A shortcut pressed again while choosing switches the mode.
        if (_session is not null)
        {
            _session.SetMode(mode);
            return;
        }

        App.EnsureTheme();
        var screen = ScreenGrabber.VirtualScreen;
        var image = ScreenGrabber.Grab(screen);
        _session = new CaptureSession(image, screen, ScreenGrabber.Monitors(), ScreenGrabber.VisibleWindows(), mode);
        _session.Finished += Session_Finished;
        _session.Show();
    }

    private void Session_Finished(object? sender, CaptureResult? result)
    {
        var session = _session!;
        _session = null;
        if (result is { } chosen)
        {
            if (chosen.IsDraggedArea)
            {
                App.Settings.LastArea = chosen.Area.ToString();
                App.Settings.Save();
            }
            Deliver(Crop(session.Image, session.Screen, chosen.Area), chosen.Monitor);
        }
        else
            TakeEditorRequest()?.Invoke(null);
        MemoryTrim.Soon();
    }

    private Action<Clip?>? TakeEditorRequest()
    {
        var request = _forEditor;
        _forEditor = null;
        return request;
    }

    private void RepeatLastArea()
    {
        var area = PixelRect.TryParse(App.Settings.LastArea, out var last) ? last.Intersect(ScreenGrabber.VirtualScreen) : default;
        if (area.IsEmpty)
        {
            Start(CaptureMode.Area); // nothing to repeat (or that screen is gone): choose one
            return;
        }
        App.EnsureTheme();
        Deliver(ScreenGrabber.Grab(area), MonitorOf(area, ScreenGrabber.Monitors()));
        MemoryTrim.Soon();
    }

    private void Deliver(BitmapSource image, MonitorInfo monitor)
    {
        var settings = App.Settings;
        var clip = new Clip(image, DateTime.Now, monitor);
        // Copy and save follow the settings for every capture, the ones made from the editor too: only the thumbnail
        // gives way to the editor.
        if (settings.CopyToClipboard)
            ClipActions.Copy(image);
        if (settings.SaveAutomatically)
        {
            try
            {
                clip.SavedPath = ClipActions.Save(image, clip.TakenAt, settings);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                MessageBox.Show(Strings.Format("ErrorSave", ex.Message), "ScreenClip", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }
        if (TakeEditorRequest() is { } editor)
            editor(clip);
        else if (settings.ShowThumbnail)
            ClipThumbnail.Show(clip);
    }

    /// <summary>The monitor that holds the center of <paramref name="area"/>.</summary>
    public static MonitorInfo MonitorOf(PixelRect area, IReadOnlyList<MonitorInfo> monitors)
    {
        int x = area.X + area.Width / 2, y = area.Y + area.Height / 2;
        return monitors.FirstOrDefault(m => m.Bounds.Contains(x, y)) ?? ScreenGrabber.MonitorAtCursor(monitors);
    }

    // A copy of the chosen pixels, so that the frozen screen (tens of MB with several monitors) can be freed.
    private static BitmapSource Crop(BitmapSource screenImage, PixelRect screen, PixelRect area)
    {
        var cropped = new CroppedBitmap(screenImage, new Int32Rect(area.X - screen.X, area.Y - screen.Y, area.Width, area.Height));
        int stride = area.Width * 4;
        var pixels = new byte[stride * area.Height];
        cropped.CopyPixels(pixels, stride, 0);
        var copy = BitmapSource.Create(area.Width, area.Height, 96, 96, PixelFormats.Bgr32, null, pixels, stride);
        copy.Freeze();
        return copy;
    }
}
