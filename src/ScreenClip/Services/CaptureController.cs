using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using ScreenClip.Core;
using ScreenClip.Video;

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
            case ShortcutAction.AllScreens:
                TakeAllScreens();
                break;
            case ShortcutAction.RepeatArea:
                RepeatLastArea();
                break;
            case ShortcutAction.Video:
                RunVideo();
                break;
        }
    }

    public bool IsRecording => RecordingSession.Current is not null;

    // The video shortcut starts a recording, switches the bar to Video while choosing, or stops the recording.
    private void RunVideo()
    {
        if (RecordingSession.Current is { } recording)
            recording.Stop();
        else if (_session is not null)
            _session.SetVideo(true);
        else
            Start(CaptureMode.Area, video: true);
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

    private void Start(CaptureMode mode, bool video = false)
    {
        // A shortcut pressed again while choosing switches the mode.
        if (_session is not null)
        {
            _session.SetMode(mode);
            return;
        }
        // No picture while a video is recorded: the shortcut of an area, a window or a screen stops it instead.
        if (RecordingSession.Current is { } recording)
        {
            recording.Stop();
            return;
        }

        App.EnsureTheme();
        var screen = ScreenGrabber.VirtualScreen;
        var monitors = ScreenGrabber.Monitors();
        var image = DemoScreen.IsOn ? DemoScreen.Grab(screen, monitors) : ScreenGrabber.Grab(screen);
        var windows = DemoScreen.IsOn ? DemoScreen.Windows(monitors) : ScreenGrabber.VisibleWindows();
        _session = new CaptureSession(image, screen, monitors, windows, mode, video);
        DevLog.Write($"capture: {mode}{(video ? ", video" : "")}, screen {screen}");
        _session.Finished += Session_Finished;
        _session.Show();
    }

    private void Session_Finished(object? sender, CaptureResult? result)
    {
        var session = _session!;
        _session = null;
        DevLog.Write(result is { } choice ? $"capture: chosen {choice.Area} ({session.Mode}{(session.IsVideo ? ", video" : "")})" : "capture: cancelled");
        if (session.IsVideo)
        {
            if (result is { } area)
            {
                // An editor that asked stays hidden while it records: the video opens it.
                _forEditor = null;
                RecordingSession.Begin(area.Area, area.Monitor);
            }
            else
                TakeEditorRequest()?.Invoke(null);
            MemoryTrim.Soon();
            return;
        }
        if (result is { } chosen)
        {
            if (chosen.IsDraggedArea)
            {
                App.Settings.LastArea = chosen.Area.ToString();
                App.Settings.Save();
            }
            var image = Crop(session.Image, session.Screen, chosen.Area);
            if (chosen.IsAllScreens)
                image = ClearBetweenScreens(image, chosen.Area, session.Monitors);
            Deliver(image, chosen.Monitor);
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

    // Every screen at once, without the overlay: nothing to choose. Pressed while choosing, from what is frozen.
    private void TakeAllScreens()
    {
        if (_session is not null)
        {
            _session.TakeAllScreens();
            return;
        }
        App.EnsureTheme();
        var screen = ScreenGrabber.VirtualScreen;
        var monitors = ScreenGrabber.Monitors();
        var area = PixelRect.Union(monitors.Select(m => m.Bounds)).Intersect(screen);
        var image = DemoScreen.IsOn ? Crop(DemoScreen.Grab(screen, monitors), screen, area) : ScreenGrabber.Grab(area);
        Deliver(ClearBetweenScreens(image, area, monitors), ScreenGrabber.MonitorAtCursor(monitors));
        MemoryTrim.Soon();
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
        DevLog.Write($"capture: {image.PixelWidth} x {image.PixelHeight}, copied {settings.CopyToClipboard}, saved {settings.SaveAutomatically && !DemoScreen.IsOn}");
        // Copy and save follow the settings for every capture, the ones made from the editor too; then the editor, or
        // the thumbnail.
        if (settings.CopyToClipboard)
            ClipActions.Copy(image);
        // A made-up desktop (README pictures, tests) never lands in the user's captures folder.
        if (settings.SaveAutomatically && !DemoScreen.IsOn)
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
        else if (settings.OpenEditorAfterCapture)
            App.OpenEditor(clip: clip);
        else if (settings.ShowThumbnail)
            ClipThumbnail.Show(clip);
    }

    /// <summary>
    /// A video just recorded: saved in the videos folder and put on the clipboard (as a file) as the settings say,
    /// then opened in the editor.
    /// </summary>
    public void DeliverVideo(string path, DateTime takenAt, MonitorInfo monitor)
    {
        var settings = App.Settings;
        string? saved = null;
        if (settings.SaveAutomatically)
        {
            try
            {
                string folder = settings.EffectiveVideoFolder;
                Directory.CreateDirectory(folder);
                saved = FileNaming.UniquePath(folder, Path.GetFileNameWithoutExtension(path), ".mp4");
                File.Copy(path, saved);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                saved = null;
                MessageBox.Show(Strings.Format("ErrorSave", ex.Message), "ScreenClip", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }
        if (settings.CopyToClipboard)
            ClipActions.CopyFile(saved ?? path);
        DevLog.Write($"video: {new FileInfo(path).Length / 1024} KB, saved {saved is not null}, copied {settings.CopyToClipboard}");
        App.OpenEditor(video: new VideoClip(path, takenAt, saved));
    }

    // Screens of different sizes or placed apart leave gaps in the rectangle around them: transparent rather than
    // black, so that a layout shows its background there (the PNG keeps it; a bitmap for other apps shows black).
    private static BitmapSource ClearBetweenScreens(BitmapSource image, PixelRect area, IReadOnlyList<MonitorInfo> monitors)
    {
        int width = image.PixelWidth, height = image.PixelHeight, stride = width * 4;
        var pixels = new byte[stride * height];
        (image.Format.BitsPerPixel == 32 ? image : new FormatConvertedBitmap(image, PixelFormats.Bgr32, null, 0)).CopyPixels(pixels, stride, 0);
        Selection.ClearBetweenScreens(pixels, area with { Width = width, Height = height }, [.. monitors.Select(m => m.Bounds)]);
        var cleared = BitmapSource.Create(width, height, 96, 96, PixelFormats.Bgra32, null, pixels, stride);
        cleared.Freeze();
        return cleared;
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
