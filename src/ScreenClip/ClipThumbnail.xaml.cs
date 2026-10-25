using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using ScreenClip.Services;
using static ScreenClip.Services.NativeMethods;

namespace ScreenClip;

/// <summary>
/// The thumbnail of a capture just taken, in the bottom left corner of its screen (the notifications of Windows are
/// on the right): copy, save, open, or drag it to an app. It never takes the focus from the app in use, is left out
/// of the next captures, and goes away by itself unless pinned or under the pointer. Newer thumbnails stack below the
/// older ones.
/// </summary>
public partial class ClipThumbnail : Window
{
    private const int MaxThumbnails = 4;
    private static readonly TimeSpan Lifetime = TimeSpan.FromSeconds(6);
    private static readonly List<ClipThumbnail> s_stack = []; // oldest first

    private readonly Clip _clip;
    private MonitorInfo _monitor;
    private readonly DispatcherTimer _timer;
    private bool _pinned;
    private bool _closing;
    private Point? _pressedAt;

    private ClipThumbnail(Clip clip)
    {
        _clip = clip;
        _monitor = clip.Monitor;
        InitializeComponent();

        PictureBrush.ImageSource = clip.Image;
        // At most 280 x 180, keeping the shape of the capture; large enough for the two buttons.
        double scale = Math.Min(1, Math.Min(280.0 / clip.Image.PixelWidth, 180.0 / clip.Image.PixelHeight));
        Card.Width = Math.Max(220, clip.Image.PixelWidth * scale);
        Card.Height = Math.Max(124, clip.Image.PixelHeight * scale);

        _timer = new DispatcherTimer { Interval = Lifetime };
        _timer.Tick += (_, _) => FadeOut();

        SourceInitialized += (_, _) =>
        {
            var hwnd = new WindowInteropHelper(this).Handle;
            // Never activated (the app in use keeps the keyboard), no taskbar button, not in the next captures.
            long style = (long)GetWindowLongPtr(hwnd, GWL_EXSTYLE);
            SetWindowLongPtr(hwnd, GWL_EXSTYLE, new IntPtr(style | WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW));
            // Developer mode (or SCREENCLIP_CAPTURABLE=1) leaves it in the screenshots (README pictures, scripts).
            if (!App.CapturableWindows)
                SetWindowDisplayAffinity(hwnd, WDA_EXCLUDEFROMCAPTURE);
        };
        DpiChanged += (_, _) => Dispatcher.BeginInvoke(Arrange, DispatcherPriority.Loaded);
        MouseEnter += (_, _) =>
        {
            HoverLayer.Visibility = Visibility.Visible;
            _timer.Stop();
        };
        MouseLeave += (_, _) =>
        {
            HoverLayer.Visibility = Visibility.Collapsed;
            RestartTimer();
        };
        // The hover layer lets the pointer through outside its buttons: pressing there starts a drag of the picture.
        Picture.MouseLeftButtonDown += Picture_MouseLeftButtonDown;
        Picture.MouseMove += Picture_MouseMove;
        Closed += (_, _) =>
        {
            _timer.Stop();
            s_stack.Remove(this);
            Arrange();
            MemoryTrim.Soon();
        };
    }

    /// <summary>Shows the thumbnail of <paramref name="clip"/> under the others.</summary>
    internal static void Show(Clip clip)
    {
        App.EnsureTheme();
        var thumbnail = new ClipThumbnail(clip) { Opacity = 0 };
        var monitors = ScreenGrabber.Monitors();
        thumbnail._monitor = App.Settings.ThumbnailScreen switch
        {
            ThumbnailScreen.Primary => monitors.FirstOrDefault(m => m.IsPrimary) ?? clip.Monitor,
            ThumbnailScreen.Pointer => ScreenGrabber.MonitorAtCursor(monitors),
            _ => clip.Monitor,
        };
        s_stack.Add(thumbnail);
        while (s_stack.Count > MaxThumbnails)
            s_stack[0].Close();
        thumbnail.Show();
        Arrange();
        thumbnail.BeginAnimation(OpacityProperty, new DoubleAnimation(1, TimeSpan.FromMilliseconds(150)));
        thumbnail.RestartTimer();
    }

    // Bottom left of the work area of each thumbnail's monitor (Settings › Capture), the newest at the bottom. Positions are set in
    // physical pixels: a window first moved onto a monitor takes that monitor's DPI, then its real height is known.
    private static void Arrange()
    {
        foreach (var group in s_stack.Where(t => t.IsVisible).GroupBy(t => t._monitor))
        {
            var work = group.Key.WorkArea;
            int bottom = work.Bottom;
            foreach (var thumbnail in group.Reverse())
            {
                var hwnd = new WindowInteropHelper(thumbnail).Handle;
                if (hwnd == IntPtr.Zero)
                    continue;
                SetWindowPos(hwnd, IntPtr.Zero, work.X, work.Y, 0, 0, SWP_NOSIZE | SWP_NOZORDER | SWP_NOACTIVATE);
                GetWindowRect(hwnd, out var rect);
                int height = rect.Bottom - rect.Top;
                bottom -= height;
                SetWindowPos(hwnd, IntPtr.Zero, work.X, bottom, 0, 0, SWP_NOSIZE | SWP_NOZORDER | SWP_NOACTIVATE);
            }
        }
    }

    private void RestartTimer()
    {
        _timer.Stop();
        // Developer mode: it stays until closed, for a screenshot.
        if (!_pinned && !_closing && !IsMouseOver && !App.Settings.DeveloperMode)
            _timer.Start();
    }

    private void FadeOut()
    {
        if (_closing)
            return;
        _closing = true;
        _timer.Stop();
        var fade = new DoubleAnimation(0, TimeSpan.FromMilliseconds(200));
        fade.Completed += (_, _) => Close();
        BeginAnimation(OpacityProperty, fade);
    }

    // ---------------------------------------------------------------- Actions

    private async void Copy_Click(object sender, RoutedEventArgs e)
    {
        bool copied = ClipActions.Copy(_clip.Image);
        CopyButton.Content = Strings.Get(copied ? "Copied" : "CopyFailed");
        await Task.Delay(1500);
        CopyButton.Content = Strings.Get("Copy");
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        // Saved already (automatically or by this button): show the file.
        if (_clip.SavedPath is { } saved)
        {
            ClipActions.ShowInExplorer(saved);
            return;
        }
        try
        {
            _clip.SavedPath = ClipActions.Save(_clip.Image, _clip.TakenAt, App.Settings);
            SaveButton.Content = Strings.Get("ShowInExplorer");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            MessageBox.Show(Strings.Format("ErrorSave", ex.Message), "ScreenClip", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    // To the layout editor (button, or a double click on the picture).
    private void Edit_Click(object sender, RoutedEventArgs e)
    {
        App.OpenEditor(clip: _clip);
        FadeOut();
    }

    private void Pin_Click(object sender, RoutedEventArgs e)
    {
        _pinned = !_pinned;
        PinGlyph.Text = _pinned ? "" : ""; // Unpin / Pin
        PinButton.ToolTip = Strings.Get(_pinned ? "ThumbnailUnpin" : "ThumbnailPin");
        RestartTimer();
    }

    private void Close_Click(object sender, RoutedEventArgs e) => FadeOut();

    // ---------------------------------------------------------------- Drag and drop

    private void Picture_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _pressedAt = e.GetPosition(this);
        if (e.ClickCount == 2)
        {
            _pressedAt = null;
            Edit_Click(sender, e);
        }
    }

    // Dragged to an app (mail, chat, document, folder): it gets the file and the picture; the thumbnail goes away.
    private void Picture_MouseMove(object sender, MouseEventArgs e)
    {
        if (_pressedAt is not { } start || e.LeftButton != MouseButtonState.Pressed)
        {
            _pressedAt = null;
            return;
        }
        var delta = e.GetPosition(this) - start;
        if (Math.Abs(delta.X) < SystemParameters.MinimumHorizontalDragDistance
            && Math.Abs(delta.Y) < SystemParameters.MinimumVerticalDragDistance)
            return;

        _pressedAt = null;
        _timer.Stop();
        var data = new DataObject();
        data.SetFileDropList(new System.Collections.Specialized.StringCollection { _clip.FilePath });
        data.SetImage(_clip.Image);
        // Copy only: dropped on a folder of the same drive, a move would take the file out of the capture folder.
        var result = DragDrop.DoDragDrop(this, data, DragDropEffects.Copy);
        if (result != DragDropEffects.None)
            FadeOut();
        else
            RestartTimer();
    }
}
