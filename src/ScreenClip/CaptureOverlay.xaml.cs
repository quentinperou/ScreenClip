using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using ScreenClip.Core;
using CaptureMode = ScreenClip.Core.CaptureMode;
using ScreenClip.Services;
using static ScreenClip.Services.NativeMethods;

namespace ScreenClip;

/// <summary>
/// The frozen screen of one monitor, on which the user drags an area or clicks a window or the screen. It covers
/// its monitor exactly, in physical pixels; positions are turned into screen pixels with the ratio between the
/// monitor size and the window size (the DPI of the monitor).
/// </summary>
public partial class CaptureOverlay : Window
{
    private const int LensPixels = 15; // pixels shown across the magnifier

    private readonly CaptureSession _session;
    private readonly MonitorInfo _monitor;
    private readonly BitmapSource _image;
    private readonly bool _showBars;
    private readonly RectangleGeometry _hole = new(Rect.Empty);
    private readonly byte[] _pixel = new byte[4];
    private double _scaleX = 1, _scaleY = 1; // screen pixels per WPF unit

    // Dragging an area: where the button went down, and the rectangle so far.
    private bool _pressed;
    private bool _dragging;
    private int _anchorX, _anchorY;
    private PixelRect _selection;

    // Space held while dragging: the area moves instead of growing.
    private bool _moving;
    private int _pointerX, _pointerY;
    private bool _pointerInside;
    private bool _syncingMode;

    // Area mode, before dragging: the UI elements under the pointer, from the smallest to the window, and the one a
    // click takes (the mouse wheel moves along them). Looked up in the background (CaptureSession.FindElements).
    private VisibleWindow? _hoveredWindow;
    private IReadOnlyList<PixelRect> _elements = [];
    private int _elementLevel;
    private int _lookupX = int.MinValue, _lookupY = int.MinValue;

    internal CaptureOverlay(CaptureSession session, MonitorInfo monitor, BitmapSource image, bool showBars)
    {
        _session = session;
        _monitor = monitor;
        _image = image;
        _showBars = showBars;
        InitializeComponent();

        Frozen.Source = image;
        LensBrush.ImageSource = image;
        Shade.Data = new GeometryGroup
        {
            FillRule = FillRule.EvenOdd,
            Children = { new RectangleGeometry(new Rect(0, 0, 100_000, 100_000)), _hole },
        };
        Toolbar.Visibility = showBars ? Visibility.Visible : Visibility.Collapsed;
        Hints.Visibility = showBars ? Visibility.Visible : Visibility.Collapsed;

        SourceInitialized += (_, _) => Place();
        SizeChanged += (_, _) =>
        {
            if (ActualWidth > 0 && ActualHeight > 0)
            {
                _scaleX = _monitor.Bounds.Width / ActualWidth;
                _scaleY = _monitor.Bounds.Height / ActualHeight;
            }
            Refresh();
        };
        Root.MouseMove += Root_MouseMove;
        Root.MouseLeftButtonDown += Root_MouseLeftButtonDown;
        Root.MouseLeftButtonUp += Root_MouseLeftButtonUp;
        Root.MouseRightButtonUp += Root_MouseRightButtonUp;
        Root.MouseWheel += Root_MouseWheel;
        _session.ElementsFound += Session_ElementsFound;
        Closed += (_, _) => _session.ElementsFound -= Session_ElementsFound;
        Root.MouseLeave += (_, _) =>
        {
            _pointerInside = false;
            Refresh();
        };
        PreviewKeyDown += Overlay_KeyDown;
        PreviewKeyUp += Overlay_KeyUp;
        Loaded += (_, _) => Refresh();
        ModeChanged();
    }

    // ---------------------------------------------------------------- Placement

    // Exactly over the monitor, in physical pixels. When the window lands on a monitor of another DPI, Windows suggests
    // a scaled size (WM_DPICHANGED): the monitor's own rectangle is put in its place before WPF applies it.
    private void Place()
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        HwndSource.FromHwnd(hwnd)?.AddHook(WndProc);
        // A tool window: Chrome and Edge do not count those when they check whether something covers them. Covered by
        // an ordinary window this size, a browser takes its page for hidden and stops preparing what the element
        // detection reads. Also kept out of Alt+Tab.
        long style = (long)GetWindowLongPtr(hwnd, GWL_EXSTYLE);
        SetWindowLongPtr(hwnd, GWL_EXSTYLE, new IntPtr(style | WS_EX_TOOLWINDOW));
        var b = _monitor.Bounds;
        SetWindowPos(hwnd, HWND_TOPMOST, b.X, b.Y, b.Width, b.Height, 0);
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WM_DPICHANGED)
        {
            var b = _monitor.Bounds;
            Marshal.StructureToPtr(new RECT { Left = b.X, Top = b.Y, Right = b.Right, Bottom = b.Bottom }, lParam, false);
        }
        return IntPtr.Zero;
    }

    // ---------------------------------------------------------------- Mode

    /// <summary>The mode changed (bar, shortcut): the bar and the drawing follow.</summary>
    internal void ModeChanged()
    {
        _syncingMode = true;
        AreaButton.IsChecked = _session.Mode == CaptureMode.Area;
        WindowButton.IsChecked = _session.Mode == CaptureMode.Window;
        ScreenButton.IsChecked = _session.Mode == CaptureMode.Screen;
        // A video films one screen: all of them at once is for pictures.
        AllScreensButton.Visibility = _session.HasSeveralScreens && !_session.IsVideo ? Visibility.Visible : Visibility.Collapsed;
        VideoButton.IsChecked = _session.IsVideo;
        AudioOptions.Visibility = _session.IsVideo ? Visibility.Visible : Visibility.Collapsed;
        ShowAudio();
        _syncingMode = false;
        ResetDrag();
        FillHints();
        Refresh();
    }

    private void Mode_Checked(object sender, RoutedEventArgs e)
    {
        if (_syncingMode)
            return;
        // All screens: nothing to choose, taken at once.
        if (sender == AllScreensButton)
        {
            _session.TakeAllScreens();
            return;
        }
        _session.SetMode(sender == WindowButton ? CaptureMode.Window : sender == ScreenButton ? CaptureMode.Screen : CaptureMode.Area);
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => _session.Cancel();

    private void Video_Click(object sender, RoutedEventArgs e) => _session.SetVideo(VideoButton.IsChecked == true);

    // The sound of the video, kept for the next one.
    private void Audio_Click(object sender, RoutedEventArgs e)
    {
        App.Settings.RecordSystemSound = SystemSoundButton.IsChecked == true;
        App.Settings.RecordMicrophone = MicrophoneButton.IsChecked == true;
        App.Settings.Save();
        ShowAudio();
    }

    private void ShowAudio()
    {
        bool sound = App.Settings.RecordSystemSound, microphone = App.Settings.RecordMicrophone;
        SystemSoundButton.IsChecked = sound;
        MicrophoneButton.IsChecked = microphone;
        SystemSoundGlyph.Text = sound ? "\uE767" : "\uE74F";
        MicrophoneGlyph.Text = microphone ? "\uE720" : "\uEC54";
        string soundText = Strings.Format(sound ? "AudioOn" : "AudioOff", Strings.Get("SystemSound"));
        string microphoneText = Strings.Format(microphone ? "AudioOn" : "AudioOff", Strings.Get("Microphone"));
        SystemSoundButton.ToolTip = soundText;
        MicrophoneButton.ToolTip = microphoneText;
        System.Windows.Automation.AutomationProperties.SetName(SystemSoundButton, Strings.Get("SystemSound"));
        System.Windows.Automation.AutomationProperties.SetName(MicrophoneButton, Strings.Get("Microphone"));
    }

    private void FillHints()
    {
        HintsPanel.Children.Clear();
        switch (_session.Mode)
        {
            case CaptureMode.Area:
                AddHint(null, Strings.Get("HintDrag"));
                AddHint(null, Strings.Get("HintClickElement"));
                AddHint(null, Strings.Get("HintWheel"));
                AddHint(Strings.Find("KeyShift") ?? "Shift", Strings.Get("HintSquare"));
                AddHint(Strings.Find("KeySpace") ?? "Space", Strings.Get("HintMove"));
                break;
            case CaptureMode.Window:
                AddHint(null, Strings.Get("HintClickAWindow"));
                break;
            case CaptureMode.Screen:
                AddHint(null, Strings.Get("HintClickScreen"));
                break;
        }
        if (_session.IsVideo)
            AddHint(null, Strings.Get("HintVideoStart"));
        AddHint(Strings.Find("KeyEscape") ?? "Esc", Strings.Get("HintCancel"));
    }

    private void AddHint(string? key, string text)
    {
        var hint = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(HintsPanel.Children.Count == 0 ? 0 : 16, 0, 0, 0) };
        if (key is not null)
        {
            hint.Children.Add(new Border
            {
                BorderBrush = new SolidColorBrush(Color.FromRgb(0x6A, 0x6A, 0x70)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(4),
                Padding = new Thickness(6, 0, 6, 1),
                Margin = new Thickness(0, 0, 6, 0),
                Child = new TextBlock { Text = key, FontSize = 11 },
            });
        }
        hint.Children.Add(new TextBlock { Text = text, VerticalAlignment = VerticalAlignment.Center });
        HintsPanel.Children.Add(hint);
    }

    // ---------------------------------------------------------------- Mouse and keys

    private void Root_MouseMove(object sender, MouseEventArgs e)
    {
        int previousX = _pointerX, previousY = _pointerY;
        (_pointerX, _pointerY) = ToPixel(e.GetPosition(Root));
        _pointerInside = !IsOverBars(e);

        if (_pressed && _session.Mode == CaptureMode.Area)
        {
            if (!_dragging && Selection.IsDrag(_anchorX, _anchorY, _pointerX, _pointerY))
                _dragging = true;
            if (_dragging)
            {
                if (_moving)
                {
                    // The rectangle keeps its size; its anchor moves with it, so that the drag goes on from there
                    // once Space is released.
                    var moved = Selection.Move(_selection, _pointerX - previousX, _pointerY - previousY, _monitor.Bounds);
                    _anchorX += moved.X - _selection.X;
                    _anchorY += moved.Y - _selection.Y;
                    _selection = moved;
                }
                else
                    UpdateSelection();
            }
        }
        if (_session.Mode == CaptureMode.Area && !_dragging && _pointerInside)
            UpdateHover();
        Refresh();
    }

    // ---------------------------------------------------------------- Elements under the pointer

    // A new lookup when the pointer leaves the smallest element found, or has moved a few pixels since the last one: a
    // smaller element may lie under it now (the first look may have been on the background of a window, which gave the
    // whole window). Only the latest point is looked at, so a quick move costs nothing more.
    private void UpdateHover()
    {
        var window = _session.WindowUnder(_pointerX, _pointerY);
        if (window != _hoveredWindow)
        {
            _hoveredWindow = window;
            _elements = [];
            _elementLevel = 0;
        }
        if (window is null)
            return; // the desktop: a click takes the screen
        bool outside = _elements.Count == 0 || !_elements[0].Contains(_pointerX, _pointerY);
        bool moved = Math.Abs(_pointerX - _lookupX) + Math.Abs(_pointerY - _lookupY) > 8;
        if (outside || moved)
        {
            (_lookupX, _lookupY) = (_pointerX, _pointerY);
            _session.FindElements(window, _pointerX, _pointerY);
        }
    }

    private void Session_ElementsFound(VisibleWindow window, int x, int y, IReadOnlyList<PixelRect> elements, IReadOnlyList<PixelRect> boxes)
    {
        if (window != _hoveredWindow || !_monitor.Bounds.Contains(x, y))
            return;
        var chain = ElementChain.Build(elements, boxes, window.Bounds, _monitor.Bounds);
        if (chain.Count == 0)
            return;
        // The same smallest element: keep the level chosen with the wheel; another one: start from the smallest.
        if (_elements.Count == 0 || _elements[0] != chain[0])
            _elementLevel = 0;
        _elements = chain;
        _elementLevel = ElementChain.Clamp(_elements, _elementLevel);
        Refresh();
    }

    // Wheel up: the larger element around; down: back to the smaller one.
    private void Root_MouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (_session.Mode != CaptureMode.Area || _dragging || _elements.Count < 2)
            return;
        _elementLevel = ElementChain.Clamp(_elements, _elementLevel + (e.Delta > 0 ? 1 : -1));
        e.Handled = true;
        Refresh();
    }

    /// <summary>What a click takes in Area mode: the element shown, else the window under the pointer, else nothing (the screen).</summary>
    private PixelRect? HoverTarget() =>
        _elements.Count > 0 && _hoveredWindow is not null
            ? _elements[ElementChain.Clamp(_elements, _elementLevel)]
            : Selection.WindowAt(_session.WindowBounds, _pointerX, _pointerY, _monitor.Bounds);

    private void UpdateSelection() =>
        _selection = Selection.FromDrag(_anchorX, _anchorY, _pointerX, _pointerY, Keyboard.Modifiers.HasFlag(ModifierKeys.Shift), _monitor.Bounds);

    private void Root_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (IsOverBars(e))
            return;
        (_pointerX, _pointerY) = ToPixel(e.GetPosition(Root));
        _pressed = true;
        _anchorX = _pointerX;
        _anchorY = _pointerY;
        Root.CaptureMouse();
        e.Handled = true;
    }

    private void Root_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (!_pressed)
            return;
        _pressed = false;
        Root.ReleaseMouseCapture();
        (_pointerX, _pointerY) = ToPixel(e.GetPosition(Root));

        if (_session.Mode == CaptureMode.Area && _dragging)
        {
            _session.Finish(new CaptureResult(_selection, _monitor, IsDraggedArea: true));
            return;
        }
        // A click: the element under the pointer (Area), the window (Window), or the screen.
        var target = _session.Mode switch
        {
            CaptureMode.Area => HoverTarget(),
            CaptureMode.Window => Selection.WindowAt(_session.WindowBounds, _pointerX, _pointerY, _monitor.Bounds),
            _ => null,
        };
        _session.Finish(new CaptureResult(target ?? _monitor.Bounds, _monitor, IsDraggedArea: false));
    }

    private void Root_MouseRightButtonUp(object sender, MouseButtonEventArgs e)
    {
        // Right click: forget the area being dragged, or leave.
        if (_dragging)
        {
            ResetDrag();
            Refresh();
        }
        else
            _session.Cancel();
    }

    private void Overlay_KeyDown(object sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Escape:
                if (_dragging)
                {
                    ResetDrag();
                    Refresh();
                }
                else
                    _session.Cancel();
                e.Handled = true;
                break;
            case Key.Space when _dragging && !_moving:
                _moving = true;
                e.Handled = true;
                break;
            case Key.LeftShift or Key.RightShift when _dragging && !_moving:
                UpdateSelection();
                Refresh();
                break;
        }
    }

    private void Overlay_KeyUp(object sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Space:
                _moving = false;
                e.Handled = true;
                break;
            case Key.LeftShift or Key.RightShift when _dragging && !_moving:
                UpdateSelection();
                Refresh();
                break;
        }
    }

    private void ResetDrag()
    {
        if (_pressed)
            Root.ReleaseMouseCapture();
        _pressed = _dragging = _moving = false;
        _selection = default;
    }

    // Where the capture bar is drawn, when it is.
    private Rect? BarBounds() =>
        Toolbar.Visibility == Visibility.Visible && Toolbar.ActualWidth > 0
            ? new Rect(Toolbar.TranslatePoint(new Point(0, 0), Root), new Size(Toolbar.ActualWidth, Toolbar.ActualHeight))
            : null;

    // The capture bar takes its own clicks; no crosshair over it.
    private bool IsOverBars(MouseEventArgs e) => Toolbar.IsVisible && Toolbar.IsMouseOver;

    // ---------------------------------------------------------------- Drawing

    private (int X, int Y) ToPixel(Point point) => (
        Math.Clamp(_monitor.Bounds.X + (int)Math.Floor(point.X * _scaleX), _monitor.Bounds.X, _monitor.Bounds.Right - 1),
        Math.Clamp(_monitor.Bounds.Y + (int)Math.Floor(point.Y * _scaleY), _monitor.Bounds.Y, _monitor.Bounds.Bottom - 1));

    private Rect ToLocal(PixelRect r) => new(
        (r.X - _monitor.Bounds.X) / _scaleX, (r.Y - _monitor.Bounds.Y) / _scaleY, r.Width / _scaleX, r.Height / _scaleY);

    /// <summary>Draws the hole, the outline, the size, the crosshair and the magnifier for the pointer.</summary>
    private void Refresh()
    {
        if (!IsLoaded)
            return;

        var mode = _session.Mode;
        PixelRect? hole = null, outline = null;
        if (mode == CaptureMode.Area && _dragging)
            hole = outline = _selection;
        else if (_pointerInside && mode == CaptureMode.Area)
        {
            // Before dragging, what a click would take: the element under the pointer (or its window).
            hole = outline = HoverTarget();
        }
        else if (_pointerInside && mode == CaptureMode.Window)
            hole = outline = Selection.WindowAt(_session.WindowBounds, _pointerX, _pointerY, _monitor.Bounds) ?? _monitor.Bounds;
        else if (_pointerInside && mode == CaptureMode.Screen)
            hole = outline = _monitor.Bounds;

        _hole.Rect = hole is { } h ? ToLocal(h) : Rect.Empty;

        if (outline is { } o)
        {
            var r = ToLocal(o);
            Outline.Visibility = Visibility.Visible;
            Outline.Width = r.Width;
            Outline.Height = r.Height;
            Canvas.SetLeft(Outline, r.X);
            Canvas.SetTop(Outline, r.Y);
            // White and thin around an area being dragged; the accent color around what a click takes.
            bool dragged = mode == CaptureMode.Area && _dragging;
            Outline.StrokeThickness = dragged ? 1 : 2;
            if (dragged)
                Outline.Stroke = Brushes.White;
            else
                Outline.SetResourceReference(Shape.StrokeProperty, "AccentFillColorDefaultBrush");
        }
        else
            Outline.Visibility = Visibility.Collapsed;

        // Size of what would be captured.
        if (hole is { } size)
        {
            var r = ToLocal(size);
            SizeText.Text = Strings.Format("SizePixels", size.Width, size.Height);
            SizeLabel.Visibility = Visibility.Visible;
            SizeLabel.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            var label = SizeLabel.DesiredSize;
            double labelLeft = Math.Max(4, r.X), labelTop = r.Y - label.Height - 6;
            if (labelTop < 4)
                labelTop = r.Y + 6;
            // Not hidden under the capture bar (an element at the top of the screen): below the bar then.
            if (BarBounds() is { } bar && bar.IntersectsWith(new Rect(labelLeft, labelTop, label.Width, label.Height)))
                labelTop = Math.Max(r.Bottom, bar.Bottom) + 6;
            Canvas.SetLeft(SizeLabel, labelLeft);
            Canvas.SetTop(SizeLabel, labelTop);
        }
        else
            SizeLabel.Visibility = Visibility.Collapsed;

        // Crosshair and magnifier, to place the corners of an area to the pixel.
        bool precise = mode == CaptureMode.Area && _pointerInside && !_moving;
        var pointer = new Point((_pointerX - _monitor.Bounds.X + 0.5) / _scaleX, (_pointerY - _monitor.Bounds.Y + 0.5) / _scaleY);
        CrossH.Visibility = CrossV.Visibility = Magnifier.Visibility = precise ? Visibility.Visible : Visibility.Collapsed;
        if (!precise)
            return;

        CrossH.Width = ActualWidth;
        Canvas.SetTop(CrossH, Math.Floor(pointer.Y));
        CrossV.Height = ActualHeight;
        Canvas.SetLeft(CrossV, Math.Floor(pointer.X));

        int localX = _pointerX - _monitor.Bounds.X, localY = _pointerY - _monitor.Bounds.Y;
        LensBrush.Viewbox = new Rect(localX - LensPixels / 2, localY - LensPixels / 2, LensPixels, LensPixels);
        _image.CopyPixels(new Int32Rect(localX, localY, 1, 1), _pixel, 4, 0);
        LensText.Text = $"{localX} · {localY} · #{_pixel[2]:X2}{_pixel[1]:X2}{_pixel[0]:X2}";

        // Below right of the pointer, or on the other side near the edges.
        const double offset = 24, width = 132, height = 160;
        double left = pointer.X + offset + width > ActualWidth ? pointer.X - offset - width : pointer.X + offset;
        double topLens = pointer.Y + offset + height > ActualHeight ? pointer.Y - offset - height : pointer.Y + offset;
        Canvas.SetLeft(Magnifier, left + (width - 120) / 2);
        Canvas.SetTop(Magnifier, topLens);
    }
}
