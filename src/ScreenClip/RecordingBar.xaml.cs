using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using ScreenClip.Core;
using ScreenClip.Services;
using static ScreenClip.Services.NativeMethods;

namespace ScreenClip;

/// <summary>
/// The small bar of a recording, under the area: its time, pause, stop, start again, discard. It never takes the focus
/// from the app being filmed, and is never in the video.
/// </summary>
public partial class RecordingBar : Window
{
    private readonly PixelRect _area;
    private readonly MonitorInfo _monitor;
    private bool _moved;

    internal RecordingBar(PixelRect area, MonitorInfo monitor)
    {
        _area = area;
        _monitor = monitor;
        InitializeComponent();
        string stop = ShortcutText.Format(App.Settings.GetShortcut(ShortcutAction.Video));
        StopButton.ToolTip = App.Settings.ShortcutsEnabled && stop.Length > 0 ? Strings.Format("RecordingStopKey", stop) : Strings.Get("RecordingStop");
        RestartButton.ToolTip = Strings.Get("RecordingRestart");
        StartNowButton.ToolTip = Strings.Get("RecordingStartNow");
        System.Windows.Automation.AutomationProperties.SetName(StartNowButton, Strings.Get("RecordingStartNow"));
        DiscardButton.ToolTip = Strings.Get("RecordingDiscard");
        System.Windows.Automation.AutomationProperties.SetName(StopButton, Strings.Get("RecordingStop"));
        System.Windows.Automation.AutomationProperties.SetName(RestartButton, Strings.Get("RecordingRestart"));
        System.Windows.Automation.AutomationProperties.SetName(DiscardButton, Strings.Get("RecordingDiscard"));
        ShowPaused(false);
        SourceInitialized += (_, _) =>
        {
            var hwnd = new WindowInteropHelper(this).Handle;
            long style = (long)GetWindowLongPtr(hwnd, GWL_EXSTYLE);
            SetWindowLongPtr(hwnd, GWL_EXSTYLE, new IntPtr(style | WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW));
            if (!App.CapturableWindows)
                SetWindowDisplayAffinity(hwnd, WDA_EXCLUDEFROMCAPTURE);
        };
        Loaded += (_, _) => Place();
        // Its width follows the text (an hour of recording is wider): centered again, unless the user moved it.
        SizeChanged += (_, _) =>
        {
            if (IsLoaded && !_moved)
                Place();
        };
    }

    public event EventHandler? StartNowClicked;
    public event EventHandler? PauseClicked;
    public event EventHandler? StopClicked;
    public event EventHandler? RestartClicked;
    public event EventHandler? DiscardClicked;

    /// <summary>The time recorded, or the countdown before it starts.</summary>
    public void ShowText(string text) => TimeText.Text = text;

    public void ShowPaused(bool paused)
    {
        PauseGlyph.Text = paused ? "" : "";
        PauseButton.ToolTip = Strings.Get(paused ? "RecordingResume" : "RecordingPause");
        System.Windows.Automation.AutomationProperties.SetName(PauseButton, Strings.Get(paused ? "RecordingResume" : "RecordingPause"));
        Dot.Opacity = paused ? 0.4 : 1;
    }

    /// <summary>While the video is written: nothing more to press.</summary>
    public void ShowSaving()
    {
        TimeText.Text = Strings.Get("RecordingSaving");
        PauseButton.IsEnabled = StopButton.IsEnabled = RestartButton.IsEnabled = DiscardButton.IsEnabled = StartNowButton.IsEnabled = false;
    }

    /// <summary>Recording: pause; before (the countdown): start now in its place.</summary>
    public void EnableControls(bool recording)
    {
        PauseButton.IsEnabled = recording;
        PauseButton.Visibility = recording ? Visibility.Visible : Visibility.Collapsed;
        StartNowButton.Visibility = recording ? Visibility.Collapsed : Visibility.Visible;
    }

    // Centered under the area, inside its monitor's work area; above it when there is no room below; at the bottom of
    // the area itself for a full screen (the bar is not in the video anyway).
    private void Place()
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        var dpi = System.Windows.Media.VisualTreeHelper.GetDpi(this);
        int width = (int)Math.Ceiling(ActualWidth * dpi.DpiScaleX), height = (int)Math.Ceiling(ActualHeight * dpi.DpiScaleY);
        var work = _monitor.WorkArea;
        int x = Math.Clamp(_area.X + (_area.Width - width) / 2, work.X, Math.Max(work.X, work.Right - width));
        int y = _area.Bottom + 4;
        if (y + height > work.Bottom)
            y = _area.Y - height - 4;
        if (y < work.Y)
            y = Math.Min(_area.Bottom, work.Bottom) - height - 12;
        SetWindowPos(hwnd, HWND_TOPMOST, x, y, 0, 0, SWP_NOSIZE | SWP_NOACTIVATE);
    }

    private void Pill_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        // The bar can be moved out of the way.
        if (e.OriginalSource is not System.Windows.Controls.Button)
        {
            _moved = true;
            DragMove();
        }
    }

    private void StartNow_Click(object sender, RoutedEventArgs e) => StartNowClicked?.Invoke(this, EventArgs.Empty);

    private void Pause_Click(object sender, RoutedEventArgs e) => PauseClicked?.Invoke(this, EventArgs.Empty);

    private void Stop_Click(object sender, RoutedEventArgs e) => StopClicked?.Invoke(this, EventArgs.Empty);

    private void Restart_Click(object sender, RoutedEventArgs e) => RestartClicked?.Invoke(this, EventArgs.Empty);

    private void Discard_Click(object sender, RoutedEventArgs e) => DiscardClicked?.Invoke(this, EventArgs.Empty);
}
