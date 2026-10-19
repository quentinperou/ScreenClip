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
        _syncingMode = false;
        ResetDrag();
        FillHints();
        Refresh();
    }

    private void Mode_Checked(object sender, RoutedEventArgs e)
    {
        if (_syncingMode)
            return;
        _session.SetMode(sender == WindowButton ? CaptureMode.Window : sender == ScreenButton ? CaptureMode.Screen : CaptureMode.Area);
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => _session.Cancel();

    private void FillHints()
    {
        HintsPanel.Children.Clear();
        switch (_session.Mode)
        {
            case CaptureMode.Area:
                AddHint(null, Strings.Get("HintDrag"));
                AddHint(null, Strings.Get("HintClickWindow"));
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
        Refresh();
    }

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
        // A click: the window under the pointer (Area and Window), or the screen.
        var target = _session.Mode == CaptureMode.Screen ? null : Selection.WindowAt(_session.Windows, _pointerX, _pointerY, _monitor.Bounds);
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
        bool dashed = false;
        if (mode == CaptureMode.Area && _dragging)
            hole = outline = _selection;
        else if (_pointerInside && mode == CaptureMode.Area)
        {
            // Before dragging, the window a click would take is outlined, the screen stays dimmed.
            outline = Selection.WindowAt(_session.Windows, _pointerX, _pointerY, _monitor.Bounds);
            dashed = true;
        }
        else if (_pointerInside && mode == CaptureMode.Window)
            hole = outline = Selection.WindowAt(_session.Windows, _pointerX, _pointerY, _monitor.Bounds) ?? _monitor.Bounds;
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
            Outline.StrokeDashArray = dashed ? new DoubleCollection { 4, 3 } : null;
            Outline.StrokeThickness = mode == CaptureMode.Area ? 1 : 2;
            if (mode == CaptureMode.Area)
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
            double top = r.Y - SizeLabel.DesiredSize.Height - 6;
            Canvas.SetLeft(SizeLabel, Math.Max(4, r.X));
            Canvas.SetTop(SizeLabel, top >= 4 ? top : r.Y + 6);
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
