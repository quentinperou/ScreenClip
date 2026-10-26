using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using ScreenClip.Core;
using ScreenClip.Core.Annotations;
using ScreenClip.Services;
using SkiaSharp;

namespace ScreenClip;

/// <summary>
/// The Edit tab of the editor: the annotation tools, drawn on the capture alone (proposal B of the mock-up). The
/// annotations live in an <see cref="AnnotationLayer"/> in the capture's pixels; the Layout tab lays out the capture
/// flattened with them, so both tabs export the same picture.
/// </summary>
public partial class EditorWindow
{
    /// <summary>The tools of the Edit tab, in the order of their column.</summary>
    private enum EditTool { Select, Crop, Arrow, Rectangle, Ellipse, Line, Pen, Marker, Text, Number, Pointer, Mask }

    private sealed record ToolInfo(EditTool Tool, Key Key, string Icon, AnnotationKind? Kind, bool GapBefore = false);

    // Line icons on an 18 px grid: the drawings of the mock-up.
    private static readonly ToolInfo[] Tools =
    [
        new(EditTool.Select, Key.V, "M4,2.5 L13.5,8.7 L9.2,9.7 L11.8,14.3 L10,15.3 L7.4,10.7 L4,13.6 Z", null),
        new(EditTool.Crop, Key.C, "M4.5,1.5 V13 H16 M1.5,4.5 H13 V16", null),
        new(EditTool.Arrow, Key.A, "M3,15 L15,3 M8,3 H15 V10", AnnotationKind.Arrow, GapBefore: true),
        new(EditTool.Rectangle, Key.R, "M3,4 H15 V14 H3 Z", AnnotationKind.Rectangle),
        new(EditTool.Ellipse, Key.O, "M9,4 A7,5 0 1 1 9,14 A7,5 0 1 1 9,4 Z", AnnotationKind.Ellipse),
        new(EditTool.Line, Key.L, "M3,15 L15,3", AnnotationKind.Line),
        new(EditTool.Pen, Key.P, "M3,15 L4,11 L12.5,2.5 L15.5,5.5 L7,14 Z M11,4 L14,7", AnnotationKind.Pen),
        new(EditTool.Marker, Key.H, "M5,13 L3,15 H7 L8,14 M5,13 L12,6 L15,9 L8,16 Z M3,16.5 H15", AnnotationKind.Marker),
        new(EditTool.Text, Key.T, "M3,4 H15 M9,4 V15 M6.5,15 H11.5", AnnotationKind.Text),
        new(EditTool.Number, Key.N, "M9,2.5 A6.5,6.5 0 1 1 9,15.5 A6.5,6.5 0 1 1 9,2.5 Z M8,6.5 L9.5,5.5 V12.5", AnnotationKind.Number),
        new(EditTool.Pointer, Key.S, "M6.5,5.5 V16 L9.2,13.5 L11.1,17.6 L12.9,16.8 L11,12.8 H14.8 Z M3.6,4.4 L2.2,3 M6.5,2.6 V0.9 M9.4,4.4 L10.8,3", AnnotationKind.Pointer),
        new(EditTool.Mask, Key.B, "M3,3 H7 V7 H3 Z M11,3 H15 V7 H11 Z M7,7 H11 V11 H7 Z M3,11 H7 V15 H3 Z M11,11 H15 V15 H11 Z", AnnotationKind.Mask),
    ];

    private enum DragMode { Draw, Move, Handle, CropNew, CropMove, CropEdges, Motion }

    [Flags]
    private enum Edges { None = 0, Left = 1, Top = 2, Right = 4, Bottom = 8 }

    private sealed class Drag
    {
        public required DragMode Mode { get; init; }
        public required SKPoint Start { get; init; }
        public required AnnotationLayer Base { get; init; }
        public Annotation? Original { get; init; }
        public HandleKind Handle { get; init; }
        public Edges Edges { get; init; }
        public PixelRect Crop { get; init; }
        public List<SKPoint> Stroke { get; } = [];
        public bool Moved { get; set; }
    }

    private readonly AnnotationHistory _history = new();
    private AnnotationLayer? _live;              // the layer while a drag or a text is under way, committed at the end
    private EditTool _tool = EditTool.Select;
    private int? _selectedId;
    private int? _hoveredId;                     // under the pointer with the Select tool: framed, dashed and light
    private Drag? _drag;
    private bool _editTab;
    private SKImage? _flattened;                 // the capture with its annotations, for the Layout tab and the export
    private AnnotationLayer? _flattenedFor;
    private SKImage? _flattenedFrom;             // the picture it was made from (a video changes it at each frame)
    private int _flattenedKey;                   // and what was drawn over it at that moment of a video
    private SKImage? _viewBaseFrom;
    private SKImage? _viewBase;                  // the capture at the size the Edit tab shows it
    private float _viewBaseScale;
    private float _viewScale = 1;
    private SKPoint _viewOrigin;                 // the capture's pixel at the top left of the view
    private float? _zoom;                        // null: the picture fits the window
    private float _viewFit = 1;
    private bool _syncingScroll;
    private bool _panning;
    private Point _panStart;
    private SKPoint _panOrigin;
    private TextBox? _textEditor;
    private int? _editingTextId;
    private AnnotationLayer? _textBase;
    private Annotation? _textOriginal;
    private bool _syncingEdit;
    private bool _captureMouse = true;
    private readonly Dictionary<EditTool, RadioButton> _toolButtons = [];
    private readonly Dictionary<uint, RadioButton> _colorButtons = [];
    private readonly Dictionary<PointerStyle, RadioButton> _pointerButtons = [];

    private AnnotationLayer Layer => _live ?? _history.Current;

    private Annotation? Selected => _selectedId is { } id ? Layer.Find(id) : null;

    private float AnnotationScale => _capture is null ? 1 : AnnotationGeometry.ScaleOf(_capture.Width, _capture.Height);

    private static AnnotationDefaults Defaults
    {
        get => App.Settings.Annotations;
        set => App.Settings.Annotations = value;
    }

    // ---------------------------------------------------------------- Building

    private void BuildEditTab()
    {
        foreach (var info in Tools)
        {
            if (info.GapBefore)
                ToolList.Children.Add(new Border { Height = 1, Margin = new Thickness(6, 4, 6, 4), Background = (Brush)FindResource("DividerStrokeColorDefaultBrush") });
            string name = Strings.Get("Tool" + info.Tool);
            var button = new RadioButton
            {
                Style = (Style)FindResource("ToolButton"),
                GroupName = "Tool",
                Content = ToolPath(info.Icon, 18),
                ToolTip = Strings.Format("ToolToolTip", name, info.Key),
            };
            System.Windows.Automation.AutomationProperties.SetName(button, name);
            button.Checked += (_, _) =>
            {
                if (!_syncingEdit)
                    SetTool(info.Tool);
            };
            _toolButtons[info.Tool] = button;
            ToolList.Children.Add(button);
        }
        UndoButton.ToolTip = Strings.Get("Undo") + " (Ctrl+Z)";
        RedoButton.ToolTip = Strings.Get("Redo") + " (Ctrl+Y)";
        System.Windows.Automation.AutomationProperties.SetName(UndoButton, Strings.Get("Undo"));
        System.Windows.Automation.AutomationProperties.SetName(RedoButton, Strings.Get("Redo"));

        foreach (var (name, color) in Palette.All)
        {
            var swatch = new RadioButton
            {
                Style = (Style)FindResource("ColorSwatch"),
                GroupName = "AnnotationColor",
                Background = new SolidColorBrush(ToColor(color)),
                ToolTip = Strings.Get("Color" + name),
            };
            System.Windows.Automation.AutomationProperties.SetName(swatch, Strings.Get("Color" + name));
            swatch.Checked += (_, _) => ChangeOption(a => a with { Color = color }, "color");
            _colorButtons[color] = swatch;
            ColorList.Children.Add(swatch);
        }

        foreach (var style in Enum.GetValues<PointerStyle>())
        {
            var swatch = new Image { Width = 40, Height = 40, Source = PointerSwatch(style), Margin = new Thickness(0, 4, 0, 0) };
            var label = new TextBlock { Text = Strings.Get("Pointer" + style), FontSize = 12, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 2, 0, 2) };
            var tile = Tile("Pointer", new StackPanel { Children = { swatch, label } }, Strings.Get("Pointer" + style));
            tile.Checked += (_, _) => ChangeOption(a => a with { Pointer = style }, "pointer");
            _pointerButtons[style] = tile;
            PointerList.Children.Add(tile);
        }
        PointerSizeSlider.Minimum = AnnotationDefaults.MinPointerSize;
        PointerSizeSlider.Maximum = AnnotationDefaults.MaxPointerSize;
        BuildRecordedPointerPanel();
        Stage.Focusable = true;
        Stage.FocusVisualStyle = null;
    }

    private static System.Windows.Shapes.Path ToolPath(string data, double size)
    {
        var path = new System.Windows.Shapes.Path
        {
            Data = Geometry.Parse(data),
            Width = size,
            Height = size,
            Stretch = Stretch.None,
            StrokeThickness = 1.4,
            StrokeStartLineCap = PenLineCap.Round,
            StrokeEndLineCap = PenLineCap.Round,
            StrokeLineJoin = PenLineJoin.Round,
        };
        // The color of the button (the accent when chosen).
        path.SetBinding(System.Windows.Shapes.Shape.StrokeProperty, new Binding(nameof(Control.Foreground))
        {
            RelativeSource = new RelativeSource(RelativeSourceMode.FindAncestor, typeof(Control), 1),
        });
        return path;
    }

    // A pointer drawn by the renderer itself, at twice the size of its tile.
    private static ImageSource PointerSwatch(PointerStyle style)
    {
        using var surface = SKSurface.Create(new SKImageInfo(80, 80, SKColorType.Bgra8888, SKAlphaType.Premul));
        surface.Canvas.Clear(SKColors.Transparent);
        var pointer = new Annotation { Kind = AnnotationKind.Pointer, Pointer = style, PointerSize = 64, Halo = false, Points = [new SKPoint(style == PointerStyle.Hand ? 26 : 22, 8)] };
        using var tiny = SKSurface.Create(new SKImageInfo(1, 1));
        using var blank = tiny.Snapshot();
        AnnotationRenderer.Draw(surface.Canvas, blank, [pointer], new PixelRect(0, 0, 80, 80));
        using var image = surface.Snapshot();
        return Imaging.ToWpf(image);
    }

    private static Color ToColor(uint argb) => Color.FromArgb((byte)(argb >> 24), (byte)(argb >> 16), (byte)(argb >> 8), (byte)argb);

    // ---------------------------------------------------------------- Tabs and tools

    private void Tab_Checked(object sender, RoutedEventArgs e)
    {
        if (_initialized && !_syncing)
            SetTab(sender == EditTab);
    }

    private void SetTab(bool edit)
    {
        CommitText();
        CancelDrag();
        _editTab = edit;
        App.Settings.EditorTab = edit ? EditorTab.Edit : EditorTab.Layout;
        _syncing = true;
        EditTab.IsChecked = edit;
        LayoutTab.IsChecked = !edit;
        _syncing = false;
        ToolRail.Visibility = edit ? Visibility.Visible : Visibility.Collapsed;
        EditPanel.Visibility = edit ? Visibility.Visible : Visibility.Collapsed;
        LayoutPanel.Visibility = edit ? Visibility.Collapsed : Visibility.Visible;
        Preview.Cursor = edit ? null : Cursors.Hand;
        Overlay.Children.Clear();
        ShowState();
        ShowEditPanel();
        RequestRender();
    }

    private void SetTool(EditTool tool)
    {
        CommitText();
        CancelDrag();
        if (_tool == EditTool.Crop && tool != EditTool.Crop)
            RequestRender();  // the view goes back to the part kept
        _tool = tool;
        _cursorSelected = false;
        // A drawing tool keeps the annotation chosen only if it is one of its own (its options are then shown).
        if (Selected is { } selected && tool != EditTool.Select && ToolOf(selected.Kind) != tool)
            _selectedId = null;
        if (tool == EditTool.Crop)
            _selectedId = null;
        _syncingEdit = true;
        foreach (var (t, button) in _toolButtons)
            button.IsChecked = t == tool;
        _syncingEdit = false;
        Stage.Cursor = CursorOf(tool);
        ShowEditPanel();
        RequestRender();
    }

    private static EditTool ToolOf(AnnotationKind kind) => Tools.First(t => t.Kind == kind).Tool;

    private static Cursor CursorOf(EditTool tool) => tool switch
    {
        EditTool.Select => Cursors.Arrow,
        EditTool.Text => Cursors.IBeam,
        _ => Cursors.Cross,
    };

    /// <summary>A new picture: nothing drawn on it yet, nothing to undo.</summary>
    private void ResetAnnotations()
    {
        CommitText();
        CancelDrag();
        _history.Reset(AnnotationLayer.Empty);
        _selectedId = null;
        _cursorSelected = false;
        _live = null;
        DropFlattened();
        _viewBase?.Dispose();
        _viewBase = null;
        _zoom = null;
        _viewOrigin = default;
        ShowEditPanel();
    }

    private void DropFlattened()
    {
        _flattened?.Dispose();
        _flattened = null;
        _flattenedFor = null;
        _flattenedFrom = null;
    }

    /// <summary>
    /// The capture with its annotations and crop: what the Layout tab lays out. A video's frame is drawn at the
    /// playhead: the annotations shown then, and the recorded pointer.
    /// </summary>
    private SKImage Flattened()
    {
        var layer = _history.Current;
        var moment = Moment;
        bool pointer = moment?.Track is { IsEmpty: false } && layer.Cursor.Shows;
        if (layer.IsEmpty && !pointer)
            return _capture!;
        int key = moment is null ? 0 : VideoTiming.Fingerprint(layer, moment);
        if (_flattened is null || !ReferenceEquals(_flattenedFor, layer) || !ReferenceEquals(_flattenedFrom, _capture) || _flattenedKey != key)
        {
            DropFlattened();
            _flattened = AnnotationRenderer.Flatten(_capture!, layer, moment);
            _flattenedFor = layer;
            _flattenedFrom = _capture;
            _flattenedKey = key;
        }
        return _flattened;
    }

    // ---------------------------------------------------------------- History

    private void Commit(AnnotationLayer layer, string? mergeKey = null)
    {
        _live = null;
        _history.Commit(layer, mergeKey);
        AnnotationsChanged();
    }

    private void AnnotationsChanged()
    {
        if (_selectedId is { } id && Layer.Find(id) is null)
            _selectedId = null;
        ShowEditPanel();
        ShowTracks();
        RequestRender();
    }

    private void Undo_Click(object sender, RoutedEventArgs e) => Undo();

    private void Redo_Click(object sender, RoutedEventArgs e) => Redo();

    private void Undo()
    {
        CommitText();
        CancelDrag();
        if (_history.Undo())
            AnnotationsChanged();
    }

    private void Redo()
    {
        CommitText();
        CancelDrag();
        if (_history.Redo())
            AnnotationsChanged();
    }

    // ---------------------------------------------------------------- The view of the Edit tab

    /// <summary>
    /// The capture (all of it while cropping, else the part kept) with its annotations: fitted to the window, or zoomed,
    /// and then only the part in the window is drawn.
    /// </summary>
    private void RenderEditView(double width, double height, DpiScale dpi)
    {
        var capture = _capture!;
        var layer = Layer;
        var area = _tool == EditTool.Crop ? new PixelRect(0, 0, capture.Width, capture.Height) : layer.Visible(capture.Width, capture.Height);
        _viewFit = (float)Math.Min(1, Math.Min(width / area.Width, height / area.Height));
        float scale = _zoom ?? _viewFit;
        // All of it when it fits; else a window on it, moved by the scroll bars, the wheel or a drag.
        double shownWidth = Math.Min(area.Width * scale, width), shownHeight = Math.Min(area.Height * scale, height);
        float spanX = (float)(shownWidth / scale), spanY = (float)(shownHeight / scale);
        float x = area.Width * scale <= width ? area.X : Math.Clamp(_viewOrigin.X, area.X, area.Right - spanX);
        float y = area.Height * scale <= height ? area.Y : Math.Clamp(_viewOrigin.Y, area.Y, area.Bottom - spanY);
        _viewOrigin = new SKPoint(x, y);

        // Smaller than its pixels, the capture is scaled once per zoom, not at each stroke drawn.
        if (scale < 1 && (_viewBase is null || _viewBaseScale != scale || !ReferenceEquals(_viewBaseFrom, capture)))
        {
            _viewBase?.Dispose();
            _viewBase = null;
            _viewBaseScale = scale;
            _viewBaseFrom = capture;
            var info = new SKImageInfo(Math.Max(1, (int)Math.Round(capture.Width * scale)), Math.Max(1, (int)Math.Round(capture.Height * scale)), SKColorType.Bgra8888, SKAlphaType.Premul);
            using var scaled = SKSurface.Create(info);
            scaled.Canvas.Clear(SKColors.Transparent);
            scaled.Canvas.DrawImage(capture, SKRect.Create(info.Width, info.Height), new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.Linear));
            _viewBase = scaled.Snapshot();
        }

        var target = new SKImageInfo(Math.Max(1, (int)Math.Round(shownWidth)), Math.Max(1, (int)Math.Round(shownHeight)), SKColorType.Bgra8888, SKAlphaType.Premul);
        using var surface = SKSurface.Create(target);
        var canvas = surface.Canvas;
        canvas.Clear(SKColors.Transparent);
        if (scale < 1 && _viewBase is not null)
            canvas.DrawImage(_viewBase, -x * scale, -y * scale);
        canvas.Scale(scale);
        canvas.Translate(-x, -y);
        // Enlarged, the pixels stay sharp squares, as in image editors.
        if (scale >= 1)
            canvas.DrawImage(capture, 0, 0, new SKSamplingOptions(scale >= 2 ? SKFilterMode.Nearest : SKFilterMode.Linear));
        // The text being typed is shown by its text box, not drawn twice. A video: what shows at the playhead.
        var moment = Moment;
        IEnumerable<Annotation> items = moment is null ? layer.Items : layer.ShownAt(moment.Time);
        AnnotationRenderer.Draw(canvas, capture, _editingTextId is { } typing ? items.Where(a => a.Id != typing) : items, area);
        if (moment is not null)
            AnnotationRenderer.DrawRecordedPointer(canvas, capture, layer, moment);
        using var picture = surface.Snapshot();
        _previewBitmap = Imaging.CopyTo(picture, _previewBitmap);
        Preview.Source = _previewBitmap;
        Preview.Width = picture.Width / dpi.DpiScaleX;
        Preview.Height = picture.Height / dpi.DpiScaleY;
        _viewScale = scale;
        ShowScrollBars(area, scale, spanX, spanY, width, height);
        ZoomText.Text = Strings.Format("EditorPercent", (int)Math.Round(scale * 100));
        ZoomFitButton.IsEnabled = _zoom is not null;
        ZoomOutButton.IsEnabled = scale > MinZoom + 0.001f;
        ZoomInButton.IsEnabled = scale < MaxZoom - 0.001f;
        ShowOverlay(dpi);
        PlaceTextEditor();
    }

    private SKPoint ToCapture(Point point)
    {
        var dpi = VisualTreeHelper.GetDpi(this);
        return new SKPoint((float)(point.X * dpi.DpiScaleX / _viewScale + _viewOrigin.X), (float)(point.Y * dpi.DpiScaleY / _viewScale + _viewOrigin.Y));
    }

    private Point ToView(SKPoint point)
    {
        var dpi = VisualTreeHelper.GetDpi(this);
        return new Point((point.X - _viewOrigin.X) * _viewScale / dpi.DpiScaleX, (point.Y - _viewOrigin.Y) * _viewScale / dpi.DpiScaleY);
    }

    // ---------------------------------------------------------------- Zoom

    private static readonly float[] ZoomSteps = [0.1f, 0.25f, 1 / 3f, 0.5f, 2 / 3f, 0.75f, 1, 1.5f, 2, 3, 4, 6, 8];
    private const float MinZoom = 0.05f, MaxZoom = 8;

    private float CurrentZoom => _zoom ?? _viewFit;

    /// <summary>
    /// The Edit view at <paramref name="scale"/> (null: fitted to the window), the capture's point under
    /// <paramref name="at"/> (the middle of the view by default) staying under it.
    /// </summary>
    private void SetZoom(float? scale, Point? at = null)
    {
        if (_capture is null || !_editTab)
            return;
        var dpi = VisualTreeHelper.GetDpi(this);
        var view = at ?? new Point(Preview.Width / 2, Preview.Height / 2);
        var anchor = ToCapture(view);
        _zoom = scale is { } s ? Math.Clamp(s, MinZoom, MaxZoom) : null;
        float next = _zoom ?? _viewFit;
        _viewOrigin = new SKPoint((float)(anchor.X - view.X * dpi.DpiScaleX / next), (float)(anchor.Y - view.Y * dpi.DpiScaleY / next));
        RequestRender();
    }

    private void ZoomIn() => SetZoom(ZoomSteps.FirstOrDefault(z => z > CurrentZoom + 0.001f, MaxZoom));

    private void ZoomOut() => SetZoom(ZoomSteps.LastOrDefault(z => z < CurrentZoom - 0.001f, MinZoom));

    private void ZoomIn_Click(object sender, RoutedEventArgs e) => ZoomIn();

    private void ZoomOut_Click(object sender, RoutedEventArgs e) => ZoomOut();

    private void ZoomActual_Click(object sender, RoutedEventArgs e) => SetZoom(1);

    private void ZoomFit_Click(object sender, RoutedEventArgs e) => SetZoom(null);

    // Ctrl + wheel zooms around the pointer; the wheel alone scrolls a picture larger than the window (Shift: sideways).
    private void PreviewHost_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (!_editTab || _capture is null)
            return;
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
        {
            SetZoom(CurrentZoom * (float)Math.Pow(1.2, e.Delta / 120.0), e.GetPosition(Stage));
            e.Handled = true;
            return;
        }
        bool sideways = Keyboard.Modifiers.HasFlag(ModifierKeys.Shift);
        if ((sideways ? HScroll : VScroll).Visibility != Visibility.Visible)
            return;
        float step = (float)(e.Delta / 120.0 * 80 * VisualTreeHelper.GetDpi(this).DpiScaleY / _viewScale);
        _viewOrigin = sideways ? new SKPoint(_viewOrigin.X - step, _viewOrigin.Y) : new SKPoint(_viewOrigin.X, _viewOrigin.Y - step);
        e.Handled = true;
        RequestRender();
    }

    private void ShowScrollBars(PixelRect area, float scale, float spanX, float spanY, double width, double height)
    {
        _syncingScroll = true;
        bool scrollX = area.Width * scale > width + 0.5, scrollY = area.Height * scale > height + 0.5;
        HScroll.Visibility = Show(scrollX);
        VScroll.Visibility = Show(scrollY);
        if (scrollX)
        {
            HScroll.Minimum = area.X;
            HScroll.Maximum = area.Right - spanX;
            HScroll.ViewportSize = spanX;
            HScroll.LargeChange = spanX * 0.9;
            HScroll.SmallChange = 40 / scale;
            HScroll.Value = _viewOrigin.X;
        }
        if (scrollY)
        {
            VScroll.Minimum = area.Y;
            VScroll.Maximum = area.Bottom - spanY;
            VScroll.ViewportSize = spanY;
            VScroll.LargeChange = spanY * 0.9;
            VScroll.SmallChange = 40 / scale;
            VScroll.Value = _viewOrigin.Y;
        }
        _syncingScroll = false;
    }

    private void ViewScroll_Scroll(object sender, System.Windows.Controls.Primitives.ScrollEventArgs e)
    {
        if (_syncingScroll)
            return;
        _viewOrigin = sender == HScroll ? new SKPoint((float)HScroll.Value, _viewOrigin.Y) : new SKPoint(_viewOrigin.X, (float)VScroll.Value);
        RequestRender();
    }

    // The middle button, or Space and the left one, move the view of a zoomed picture.
    private bool CanPan => HScroll.Visibility == Visibility.Visible || VScroll.Visibility == Visibility.Visible;

    private void Stage_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Middle && _editTab && CanPan)
        {
            StartPan(e.GetPosition(PreviewHost));
            e.Handled = true;
        }
    }

    private void Stage_MouseUp(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Middle)
            EndPan();
    }

    private void StartPan(Point at)
    {
        CommitText();
        _panning = true;
        _panStart = at;
        _panOrigin = _viewOrigin;
        Stage.CaptureMouse();
        Stage.Cursor = Cursors.ScrollAll;
    }

    private void Pan(Point at)
    {
        var dpi = VisualTreeHelper.GetDpi(this);
        _viewOrigin = new SKPoint(
            (float)(_panOrigin.X - (at.X - _panStart.X) * dpi.DpiScaleX / _viewScale),
            (float)(_panOrigin.Y - (at.Y - _panStart.Y) * dpi.DpiScaleY / _viewScale));
        RequestRender();
    }

    private void EndPan()
    {
        if (!_panning)
            return;
        _panning = false;
        if (Stage.IsMouseCaptured)
            Stage.ReleaseMouseCapture();
        Stage.Cursor = CursorOf(_tool);
    }

    /// <summary>A few screen pixels, in the capture's pixels: how near the pointer must be.</summary>
    private float Tolerance(double dips = 6) => (float)(dips * VisualTreeHelper.GetDpi(this).DpiScaleX / _viewScale);

    // The frame of the annotation chosen and its handles, or the crop frame: drawn by WPF over the picture.
    private void ShowOverlay(DpiScale dpi)
    {
        Overlay.Children.Clear();
        var accent = (Brush)FindResource("AccentFillColorDefaultBrush");
        if (_tool == EditTool.Crop)
        {
            var kept = Layer.Visible(_capture!.Width, _capture.Height);
            var topLeft = ToView(new SKPoint(kept.X, kept.Y));
            var bottomRight = ToView(new SKPoint(kept.Right, kept.Bottom));
            var rect = new Rect(topLeft, bottomRight);
            var shade = new GeometryGroup { FillRule = FillRule.EvenOdd };
            shade.Children.Add(new RectangleGeometry(new Rect(0, 0, Preview.Width, Preview.Height)));
            shade.Children.Add(new RectangleGeometry(rect));
            Overlay.Children.Add(new System.Windows.Shapes.Path { Data = shade, Fill = new SolidColorBrush(Color.FromArgb(0x99, 0, 0, 0)) });
            AddFrame(rect, accent, dashed: false);
            foreach (var point in new[]
            {
                rect.TopLeft, rect.TopRight, rect.BottomLeft, rect.BottomRight,
                new Point(rect.Left + rect.Width / 2, rect.Top), new Point(rect.Left + rect.Width / 2, rect.Bottom),
                new Point(rect.Left, rect.Top + rect.Height / 2), new Point(rect.Right, rect.Top + rect.Height / 2),
            })
                AddHandle(point, accent);
            return;
        }
        // The one under the pointer (asked by the author): a light dashed frame, so that what a click takes is seen.
        if (_hoveredId is { } hovered && hovered != _selectedId && Layer.Find(hovered) is { } over && IsShownNow(over) && _drag is null)
        {
            var overBounds = AnnotationGeometry.Bounds(IsVideo ? VideoTiming.Posed(over, _playhead) : over, AnnotationScale);
            var overFrame = new Rect(ToView(new SKPoint(overBounds.Left, overBounds.Top)), ToView(new SKPoint(overBounds.Right, overBounds.Bottom)));
            overFrame.Inflate(3, 3);
            AddFrame(overFrame, accent, dashed: true);
            Overlay.Children[^1].Opacity = 0.6;
        }
        if (Selected is not { } selected || _editingTextId == selected.Id || !IsShownNow(selected))
            return;
        var posed = IsVideo ? VideoTiming.Posed(selected, _playhead) : selected;
        var bounds = AnnotationGeometry.Bounds(posed, AnnotationScale);
        var frame = new Rect(ToView(new SKPoint(bounds.Left, bounds.Top)), ToView(new SKPoint(bounds.Right, bounds.Bottom)));
        frame.Inflate(3, 3);
        AddFrame(frame, accent, dashed: true);
        if (selected.Motion is { } motion)
        {
            // Its way, dashed; the lines to the square handles that bend it; A and B round.
            var way = new System.Windows.Shapes.Polyline { Stroke = accent, StrokeThickness = 1.5, StrokeDashArray = [5, 3] };
            foreach (var point in VideoTiming.Path(selected))
                way.Points.Add(ToView(point));
            Overlay.Children.Add(way);
            if (motion.Curved)
            {
                foreach (var (end, control) in new[] { (motion.From, motion.Control1), (selected.Start, motion.Control2) })
                    Overlay.Children.Add(new System.Windows.Shapes.Line { X1 = ToView(end).X, Y1 = ToView(end).Y, X2 = ToView(control).X, Y2 = ToView(control).Y, Stroke = accent, StrokeThickness = 1, Opacity = 0.7 });
            }
            foreach (var (kind, point) in AnnotationGeometry.Handles(selected))
            {
                if (kind is HandleKind.MotionFrom or HandleKind.MotionTo)
                    AddEndHandle(ToView(point), accent, kind == HandleKind.MotionFrom ? "A" : "B");
                else
                    AddHandle(ToView(point), accent);
            }
            return;
        }
        foreach (var (_, point) in AnnotationGeometry.Handles(selected))
            AddHandle(ToView(point), accent);
    }

    // A or B of a moving pointer: a round handle with its letter.
    private void AddEndHandle(Point center, Brush brush, string letter)
    {
        var handle = new Border
        {
            Width = 16,
            Height = 16,
            CornerRadius = new CornerRadius(8),
            Background = brush,
            BorderBrush = Brushes.White,
            BorderThickness = new Thickness(1.5),
            Child = new TextBlock { Text = letter, FontSize = 9, FontWeight = FontWeights.Bold, Foreground = Brushes.White, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center },
        };
        Canvas.SetLeft(handle, center.X - 8);
        Canvas.SetTop(handle, center.Y - 8);
        Overlay.Children.Add(handle);
    }

    // The annotation on top under a point, among those that show at the playhead of a video.
    private Annotation? HitAt(AnnotationLayer layer, SKPoint point, float tolerance) => IsVideo
        ? layer.HitTestAt(point, AnnotationScale, tolerance, _playhead)
        : layer.HitTest(point, AnnotationScale, tolerance);

    private void AddFrame(Rect rect, Brush brush, bool dashed)
    {
        var frame = new System.Windows.Shapes.Rectangle { Width = Math.Max(0, rect.Width), Height = Math.Max(0, rect.Height), Stroke = brush, StrokeThickness = 1 };
        if (dashed)
            frame.StrokeDashArray = [4, 3];
        Canvas.SetLeft(frame, rect.Left);
        Canvas.SetTop(frame, rect.Top);
        Overlay.Children.Add(frame);
    }

    private void AddHandle(Point center, Brush brush)
    {
        var handle = new System.Windows.Shapes.Rectangle { Width = 9, Height = 9, Fill = Brushes.White, Stroke = brush, StrokeThickness = 1.5, RadiusX = 2, RadiusY = 2 };
        Canvas.SetLeft(handle, center.X - 4.5);
        Canvas.SetTop(handle, center.Y - 4.5);
        Overlay.Children.Add(handle);
    }

    // ---------------------------------------------------------------- Mouse

    private void Stage_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (_editTab && Keyboard.IsKeyDown(Key.Space) && CanPan)
        {
            StartPan(e.GetPosition(PreviewHost));
            e.Handled = true;
            return;
        }
        if (PointerDown(e.GetPosition(Stage), e.ClickCount, capture: true))
            e.Handled = true;
    }

    private void Stage_MouseMove(object sender, MouseEventArgs e)
    {
        if (_panning)
        {
            if (e.LeftButton == MouseButtonState.Pressed || e.MiddleButton == MouseButtonState.Pressed)
                Pan(e.GetPosition(PreviewHost));
            else
                EndPan();
            return;
        }
        PointerMove(e.GetPosition(Stage), e.LeftButton == MouseButtonState.Pressed, Keyboard.Modifiers.HasFlag(ModifierKeys.Shift));
    }

    /// <summary>
    /// The button pressed at <paramref name="at"/> (in the picture's DIPs): draws, picks or reshapes, after the tool.
    /// True when a text box took the click. <paramref name="capture"/>: the mouse follows the drag (false when scripted).
    /// </summary>
    private bool PointerDown(Point at, int clickCount, bool capture)
    {
        if (!_editTab || _capture is null)
            return false;
        CommitText();
        Stage.Focus();
        _captureMouse = capture;
        var point = ToCapture(at);
        var layer = _history.Current;
        float scale = AnnotationScale, tolerance = Tolerance();

        if (_tool == EditTool.Crop)
        {
            var kept = layer.Visible(_capture.Width, _capture.Height);
            var edges = EdgesAt(kept, point, Tolerance(8));
            var mode = edges != Edges.None ? DragMode.CropEdges : Contains(kept, point) ? DragMode.CropMove : DragMode.CropNew;
            StartDrag(new Drag { Mode = mode, Start = point, Base = layer, Edges = edges, Crop = kept });
            return false;
        }

        // The handles of the annotation chosen come first, whatever the tool.
        if (Selected is { } selected && IsShownNow(selected))
        {
            foreach (var (kind, handle) in AnnotationGeometry.Handles(selected))
            {
                if (AnnotationGeometry.Distance(handle, point) <= Tolerance(7))
                {
                    StartDrag(new Drag { Mode = DragMode.Handle, Start = point, Base = layer, Original = selected, Handle = kind });
                    return false;
                }
            }
        }

        var hit = HitAt(layer, point, tolerance);
        var tool = Tools.First(t => t.Tool == _tool);
        // The Select tool picks; the tools that place one thing pick their own kind (a pointer is moved, a text edited).
        bool picks = _tool == EditTool.Select || (hit is not null && tool.Kind == hit.Kind && _tool is EditTool.Text or EditTool.Number or EditTool.Pointer);
        if (picks)
        {
            if (hit is null)
            {
                Select(null);
                return false;
            }
            Select(hit.Id);
            if (hit.Kind == AnnotationKind.Text && (_tool == EditTool.Text || clickCount == 2))
            {
                BeginText(hit, layer, isNew: false);
                return true;
            }
            StartDrag(new Drag { Mode = DragMode.Move, Start = point, Base = layer, Original = hit });
            return false;
        }

        var kindToDraw = tool.Kind!.Value;
        var defaults = Defaults;
        switch (kindToDraw)
        {
            case AnnotationKind.Text:
            {
                var text = defaults.Create(AnnotationKind.Text, point);
                var withText = layer.Add(text, out var added);
                _live = withText;
                Select(added.Id);
                BeginText(added, layer, isNew: true);
                return true;
            }
            case AnnotationKind.Number or AnnotationKind.Pointer:
            {
                // Put at once, and still movable while the button is down.
                var annotation = defaults.Create(kindToDraw, point);
                if (kindToDraw == AnnotationKind.Number)
                    annotation = annotation with { Number = layer.NextNumber };
                _live = layer.Add(annotation, out var added);
                Select(added.Id);
                // In a video, a pointer dragged from A to B moves there.
                var mode = IsVideo && kindToDraw == AnnotationKind.Pointer ? DragMode.Motion : DragMode.Move;
                StartDrag(new Drag { Mode = mode, Start = point, Base = layer, Original = added, Moved = true }, adding: _live);
                return false;
            }
            default:
            {
                var annotation = AnnotationOptions.IsStroke(kindToDraw)
                    ? defaults.Create(kindToDraw, point)
                    : defaults.Create(kindToDraw, point, point);
                _live = layer.Add(annotation, out var added);
                _selectedId = added.Id;
                var drag = new Drag { Mode = DragMode.Draw, Start = point, Base = layer, Original = added };
                drag.Stroke.Add(point);
                StartDrag(drag, adding: _live);
                return false;
            }
        }
    }

    private void StartDrag(Drag drag, AnnotationLayer? adding = null)
    {
        _drag = drag;
        _live = adding;
        if (_captureMouse)
            Stage.CaptureMouse();
        ShowEditPanel();
        RequestRender();
    }

    private void PointerMove(Point at, bool pressed, bool shift)
    {
        if (!_editTab || _capture is null)
            return;
        var point = ToCapture(at);
        if (_drag is not { } drag)
        {
            ShowHoverCursor(point);
            return;
        }
        if (!pressed)
        {
            // The button went up outside the window, or a scripted drag meets the real mouse.
            if (_captureMouse)
                EndDrag();
            return;
        }
        float dx = point.X - drag.Start.X, dy = point.Y - drag.Start.Y;
        if (Math.Abs(dx) + Math.Abs(dy) > Tolerance(2))
            drag.Moved = true;
        int width = _capture.Width, height = _capture.Height;

        switch (drag.Mode)
        {
            case DragMode.Draw:
            {
                var original = drag.Original!;
                Annotation drawn;
                if (AnnotationOptions.IsStroke(original.Kind))
                {
                    if (original.Kind == AnnotationKind.Marker && shift)
                        drawn = original with { Points = [drag.Start, AnnotationGeometry.Constrain(drag.Start, point, true)] };
                    else
                    {
                        if (AnnotationGeometry.Distance(drag.Stroke[^1], point) >= Tolerance(1.5))
                            drag.Stroke.Add(point);
                        drawn = original with { Points = [.. drag.Stroke] };
                    }
                }
                else
                {
                    var end = original.Kind is AnnotationKind.Rectangle or AnnotationKind.Ellipse or AnnotationKind.Mask
                        ? shift ? AnnotationGeometry.Square(drag.Start, point) : point
                        : AnnotationGeometry.Constrain(drag.Start, point, shift);
                    drawn = original with { Points = [drag.Start, end] };
                }
                // Added again to the layer before it: it gets the same id.
                _live = drag.Base.Add(drawn, out _);
                break;
            }
            case DragMode.Move:
            {
                // A number or a pointer just put is not in the layer before it yet.
                var start = drag.Base.Find(drag.Original!.Id) is null ? drag.Base.Add(drag.Original, out _) : drag.Base;
                _live = start.Replace(drag.Original.MovedBy(dx, dy));
                break;
            }
            case DragMode.Handle:
                _live = drag.Base.Replace(AnnotationGeometry.WithHandle(drag.Original!, drag.Handle, point, shift));
                break;
            case DragMode.Motion:
            {
                // A pointer just put in a video: dragged far enough, it goes from where the drag began to here.
                var original = drag.Original!;
                bool far = AnnotationGeometry.Distance(drag.Start, point) > Tolerance(12);
                _live = drag.Base.Add(far ? original with { Points = [point], Motion = MotionAt(drag.Start, point) } : original, out _);
                break;
            }
            case DragMode.CropNew:
                _live = drag.Base with { Crop = CropOf(Clamp(drag.Start, width, height), Clamp(point, width, height), width, height) };
                break;
            case DragMode.CropMove:
            {
                var c = drag.Crop;
                int x = Math.Clamp(c.X + (int)Math.Round(dx), 0, width - c.Width);
                int y = Math.Clamp(c.Y + (int)Math.Round(dy), 0, height - c.Height);
                _live = drag.Base with { Crop = Kept(new PixelRect(x, y, c.Width, c.Height), width, height) };
                break;
            }
            case DragMode.CropEdges:
            {
                var c = drag.Crop;
                var p = Clamp(point, width, height);
                const int Min = 8;
                int left = drag.Edges.HasFlag(Edges.Left) ? Math.Min((int)Math.Round(p.X), c.Right - Min) : c.X;
                int right = drag.Edges.HasFlag(Edges.Right) ? Math.Max((int)Math.Round(p.X), c.X + Min) : c.Right;
                int top = drag.Edges.HasFlag(Edges.Top) ? Math.Min((int)Math.Round(p.Y), c.Bottom - Min) : c.Y;
                int bottom = drag.Edges.HasFlag(Edges.Bottom) ? Math.Max((int)Math.Round(p.Y), c.Y + Min) : c.Bottom;
                _live = drag.Base with { Crop = Kept(new PixelRect(left, top, right - left, bottom - top), width, height) };
                break;
            }
        }
        ShowEditPanel(listToo: false);
        RequestRender();
    }

    private void Stage_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (_panning)
            EndPan();
        else
            EndDrag();
    }

    private void Stage_LostMouseCapture(object sender, MouseEventArgs e)
    {
        if (_panning)
            EndPan();
        else if (_drag is not null)
            EndDrag();
    }

    private void EndDrag()
    {
        if (_drag is not { } drag)
            return;
        _drag = null;
        if (Stage.IsMouseCaptured)
            Stage.ReleaseMouseCapture();
        var result = _live;
        _live = null;
        if (result is null)
        {
            AnnotationsChanged();
            return;
        }
        if (drag.Mode == DragMode.Draw && !drag.Moved)
        {
            // A click alone draws nothing (a dot for the pen and the highlighter).
            if (!AnnotationOptions.IsStroke(drag.Original!.Kind))
            {
                _selectedId = null;
                AnnotationsChanged();
                return;
            }
        }
        if (drag.Mode is DragMode.Move or DragMode.Handle && !drag.Moved && drag.Base.Find(drag.Original!.Id) is not null)
        {
            AnnotationsChanged();
            return;
        }
        Commit(result);
    }

    private void CancelDrag()
    {
        if (_drag is null)
            return;
        _drag = null;
        _live = null;
        if (Stage.IsMouseCaptured)
            Stage.ReleaseMouseCapture();
        AnnotationsChanged();
    }

    private void ShowHoverCursor(SKPoint point)
    {
        Cursor cursor = CursorOf(_tool);
        if (_tool == EditTool.Crop)
        {
            var kept = Layer.Visible(_capture!.Width, _capture.Height);
            cursor = EdgesAt(kept, point, Tolerance(8)) switch
            {
                Edges.Left or Edges.Right => Cursors.SizeWE,
                Edges.Top or Edges.Bottom => Cursors.SizeNS,
                Edges.Left | Edges.Top or Edges.Right | Edges.Bottom => Cursors.SizeNWSE,
                Edges.Right | Edges.Top or Edges.Left | Edges.Bottom => Cursors.SizeNESW,
                _ => Contains(kept, point) ? Cursors.SizeAll : Cursors.Cross,
            };
        }
        else if (Selected is { } selected && IsShownNow(selected) && AnnotationGeometry.Handles(selected).Any(h => AnnotationGeometry.Distance(h.Point, point) <= Tolerance(7)))
            cursor = Cursors.Hand;
        else if (_tool == EditTool.Select && HitAt(Layer, point, Tolerance()) is not null)
            cursor = Cursors.SizeAll;
        if (Stage.Cursor != cursor)
            Stage.Cursor = cursor;
        SetHovered(_tool == EditTool.Select ? HitAt(Layer, point, Tolerance())?.Id : null);
    }

    private void SetHovered(int? id)
    {
        if (_hoveredId == id)
            return;
        _hoveredId = id;
        if (_editTab && _capture is not null)
            ShowOverlay(VisualTreeHelper.GetDpi(this));
    }

    private void Stage_MouseLeave(object sender, MouseEventArgs e)
    {
        if (_drag is null)
            SetHovered(null);
    }

    private static bool Contains(PixelRect rect, SKPoint point) => point.X >= rect.X && point.X <= rect.Right && point.Y >= rect.Y && point.Y <= rect.Bottom;

    private static Edges EdgesAt(PixelRect rect, SKPoint point, float tolerance)
    {
        bool inX = point.X >= rect.X - tolerance && point.X <= rect.Right + tolerance;
        bool inY = point.Y >= rect.Y - tolerance && point.Y <= rect.Bottom + tolerance;
        var edges = Edges.None;
        if (inY && Math.Abs(point.X - rect.X) <= tolerance)
            edges |= Edges.Left;
        else if (inY && Math.Abs(point.X - rect.Right) <= tolerance)
            edges |= Edges.Right;
        if (inX && Math.Abs(point.Y - rect.Y) <= tolerance)
            edges |= Edges.Top;
        else if (inX && Math.Abs(point.Y - rect.Bottom) <= tolerance)
            edges |= Edges.Bottom;
        return edges;
    }

    private static SKPoint Clamp(SKPoint point, int width, int height) =>
        new(Math.Clamp(point.X, 0, width), Math.Clamp(point.Y, 0, height));

    private static PixelRect? CropOf(SKPoint a, SKPoint b, int width, int height)
    {
        int left = (int)Math.Round(Math.Min(a.X, b.X)), top = (int)Math.Round(Math.Min(a.Y, b.Y));
        int right = (int)Math.Round(Math.Max(a.X, b.X)), bottom = (int)Math.Round(Math.Max(a.Y, b.Y));
        if (right - left < 8 || bottom - top < 8)
            return null;
        return Kept(new PixelRect(left, top, right - left, bottom - top), width, height);
    }

    // The whole capture is no crop at all.
    private static PixelRect? Kept(PixelRect rect, int width, int height) =>
        rect == new PixelRect(0, 0, width, height) ? null : rect;

    private void CropAll_Click(object sender, RoutedEventArgs e)
    {
        if (_history.Current.Crop is not null)
            Commit(_history.Current with { Crop = null });
    }

    // ---------------------------------------------------------------- Selection

    private void Select(int? id)
    {
        _selectedId = id;
        _cursorSelected = false;
        ShowEditPanel();
        ShowTracks();
        RequestRender();
    }

    private void DeleteAnnotation_Click(object sender, RoutedEventArgs e) => DeleteSelected();

    private void DeleteSelected()
    {
        if (Selected is { } selected)
            Commit(_history.Current.Remove(selected.Id));
    }

    private void BringToFront_Click(object sender, RoutedEventArgs e)
    {
        if (Selected is { } selected)
            Commit(_history.Current.BringToFront(selected.Id));
    }

    private void ClearAll_Click(object sender, RoutedEventArgs e)
    {
        CommitText();
        if (!_history.Current.Items.IsEmpty)
            Commit(_history.Current with { Items = [] });
    }

    private void Nudge(float dx, float dy)
    {
        if (Selected is { } selected)
            Commit(_history.Current.Replace(selected.MovedBy(dx, dy)), "nudge" + selected.Id);
    }

    // ---------------------------------------------------------------- Text typed on the capture

    private void BeginText(Annotation text, AnnotationLayer before, bool isNew)
    {
        _textBase = before;
        _textOriginal = isNew ? null : text;
        _editingTextId = text.Id;
        _live ??= before;
        var box = new TextBox
        {
            Style = (Style)FindResource("InlineTextBox"),
            Text = text.Text,
            FontFamily = new FontFamily("Segoe UI Variable Text, Segoe UI"),
            FontWeight = FontWeights.SemiBold,
            MinWidth = 20,
        };
        box.TextChanged += (_, _) =>
        {
            if (_editingTextId is { } id && _live?.Find(id) is { } typed)
                _live = _live.Replace(typed with { Text = box.Text });
        };
        box.PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape)
            {
                CommitText();
                e.Handled = true;
            }
        };
        box.LostKeyboardFocus += (_, _) => CommitText();
        _textEditor = box;
        TextLayer.Children.Clear();
        TextLayer.Children.Add(box);
        PlaceTextEditor();
        RequestRender();
        Dispatcher.BeginInvoke(() =>
        {
            box.Focus();
            box.CaretIndex = box.Text.Length;
        }, System.Windows.Threading.DispatcherPriority.Input);
    }

    // The text box takes the look of the annotation: its font size, its label, at its place on the picture.
    private void PlaceTextEditor()
    {
        if (_textEditor is not { } box || _editingTextId is not { } id || Layer.Find(id) is not { } text)
            return;
        var dpi = VisualTreeHelper.GetDpi(this);
        double toView = _viewScale / dpi.DpiScaleX;
        float size = AnnotationGeometry.FontSize(text, AnnotationScale);
        var (padX, padY) = AnnotationGeometry.TextPadding(size);
        box.FontSize = Math.Max(1, size * toView);
        TextBlock.SetLineHeight(box, AnnotationGeometry.LineHeight(size) * toView);
        TextBlock.SetLineStackingStrategy(box, LineStackingStrategy.BlockLineHeight);
        box.Padding = new Thickness(padX * toView - 1, padY * toView - 1, padX * toView - 1, padY * toView - 1);
        bool label = text.TextStyle != TextStyle.Plain;
        bool light = Palette.IsLight(text.Color);
        box.Background = label ? new SolidColorBrush(ToColor(text.Color)) : new SolidColorBrush(Color.FromArgb(0x33, 0xFF, 0xFF, 0xFF));
        box.Foreground = new SolidColorBrush(label ? (light ? Color.FromRgb(0x1B, 0x1B, 0x1B) : Colors.White) : ToColor(text.Color));
        box.CaretBrush = box.Foreground;
        box.BorderBrush = (Brush)FindResource("AccentFillColorDefaultBrush");
        var at = ToView(text.Start);
        Canvas.SetLeft(box, at.X);
        Canvas.SetTop(box, at.Y);
    }

    private void CommitText()
    {
        if (_textEditor is not { } box || _editingTextId is not { } id)
            return;
        _textEditor = null;
        _editingTextId = null;
        TextLayer.Children.Clear();
        string value = box.Text.TrimEnd();
        var layer = _live ?? _history.Current;
        var before = _textBase ?? _history.Current;
        var original = _textOriginal;
        _live = null;
        _textBase = null;
        _textOriginal = null;
        if (layer.Find(id) is not { } text)
        {
            AnnotationsChanged();
            return;
        }
        if (string.IsNullOrWhiteSpace(value))
        {
            // An empty text is no text: a new one is dropped, an emptied one removed.
            if (original is not null)
                Commit(before.Remove(id));
            else
            {
                _selectedId = null;
                AnnotationsChanged();
            }
            return;
        }
        if (original is not null && original.Text == value)
        {
            AnnotationsChanged();
            return;
        }
        Commit(layer.Replace(text with { Text = value }));
    }

    // ---------------------------------------------------------------- Options

    /// <summary>
    /// An option changed in the panel: it applies to the annotation chosen, and becomes the tool's option for the next
    /// ones. Changes in a row of the same option (a slider dragged) are one step to undo.
    /// </summary>
    private void ChangeOption(Func<Annotation, Annotation> change, string option)
    {
        if (_syncingEdit)
            return;
        var kind = OptionsKind();
        if (kind is null)
            return;
        if (Selected is { } selected && _editingTextId != selected.Id)
        {
            var changed = change(selected);
            Defaults = Defaults.Follow(changed);
            Commit(_history.Current.Replace(changed), option + selected.Id);
        }
        else if (_textEditor is not null && _editingTextId is { } typing && _live?.Find(typing) is { } text)
        {
            // While typing: the text being typed takes it.
            var changed = change(text);
            Defaults = Defaults.Follow(changed);
            _live = _live.Replace(changed);
            PlaceTextEditor();
            ShowEditPanel();
        }
        else
        {
            Defaults = Defaults.Follow(change(Defaults.Create(kind.Value)));
            ShowEditPanel();
        }
    }

    /// <summary>Whose options the panel shows: the annotation chosen, else the tool's.</summary>
    private AnnotationKind? OptionsKind() =>
        Selected?.Kind ?? Tools.First(t => t.Tool == _tool).Kind;

    private void PointerSize_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (!_initialized)
            return;
        int size = (int)PointerSizeSlider.Value;
        PointerSizeValue.Text = Strings.Format("EditorPixels", size);
        ChangeOption(a => a with { PointerSize = size }, "pointerSize");
    }

    private void Halo_Changed(object sender, RoutedEventArgs e)
    {
        if (_initialized)
            ChangeOption(a => a with { Halo = HaloSwitch.IsChecked == true }, "halo");
    }

    /// <summary>The panel of the Edit tab, for the annotation chosen or the tool; and the list of annotations.</summary>
    private void ShowEditPanel(bool listToo = true)
    {
        if (!_initialized || !_editTab)
            return;
        _syncingEdit = true;
        bool cursor = _cursorSelected && HasRecordedPointer;
        RecordedPointerRow.Visibility = Show(cursor);
        ToolKeyCap.Visibility = Show(!cursor);
        if (cursor)
        {
            ToolName.Text = Strings.Get("RecordedPointer");
            ToolIcon.Content = ToolPath(Tools.First(t => t.Tool == EditTool.Pointer).Icon, 18);
            ToolHelp.Text = Strings.Get("RecordedPointerHelp");
            ColorRow.Visibility = SizeRow.Visibility = StyleRow.Visibility = PointerRow.Visibility = Visibility.Collapsed;
            NextNumberText.Visibility = CropRow.Visibility = SelectionRow.Visibility = Visibility.Collapsed;
            ShowRecordedPointerPanel();
            UndoButton.IsEnabled = _history.CanUndo;
            RedoButton.IsEnabled = _history.CanRedo;
            ClearAllButton.IsEnabled = !_history.Current.Items.IsEmpty;
            if (listToo)
                ShowAnnotationList();
            _syncingEdit = false;
            return;
        }
        var selected = Selected;
        var info = selected is not null && _tool == EditTool.Select ? Tools.First(t => t.Kind == selected.Kind) : Tools.First(t => t.Tool == _tool);
        ToolName.Text = selected?.Motion is not null ? Strings.Get("AnnotListMotion") : Strings.Get("Tool" + info.Tool);
        ToolKey.Text = info.Key.ToString();
        ToolIcon.Content = ToolPath(info.Icon, 18);
        ToolHelp.Text = info.Tool == EditTool.Pointer && IsVideo
            ? Strings.Get(selected?.Motion is not null ? "MotionHelp" : "ToolPointerVideoHelp")
            : Strings.Get("Tool" + info.Tool + "Help");

        var kind = OptionsKind();
        var shown = selected ?? (kind is { } k ? Defaults.Create(k) : null);
        if (_editingTextId is { } typing && _live?.Find(typing) is { } typed)
            shown = typed;
        bool hasKind = shown is not null;
        ColorRow.Visibility = Show(hasKind && AnnotationOptions.HasColor(shown!.Kind));
        SizeRow.Visibility = Show(hasKind && AnnotationOptions.HasSize(shown!.Kind));
        StyleRow.Visibility = Show(hasKind && AnnotationOptions.HasStyle(shown!.Kind) && shown.Kind != AnnotationKind.Pointer);
        PointerRow.Visibility = Show(shown?.Kind == AnnotationKind.Pointer);
        NextNumberText.Visibility = Show(_tool == EditTool.Number);
        CropRow.Visibility = Show(_tool == EditTool.Crop);
        SelectionRow.Visibility = Show(selected is not null && _drag is null);

        if (shown is not null)
        {
            foreach (var (color, button) in _colorButtons)
                button.IsChecked = color == shown.Color;
            if (SizeRow.Visibility == Visibility.Visible)
            {
                bool scale = shown.Kind is AnnotationKind.Text or AnnotationKind.Number;
                SizeLabel.Text = Strings.Get(scale ? "AnnotSize" : "AnnotThickness");
                FillSegments(SizeList, "AnnotationSize", Enum.GetValues<StrokeSize>(), shown.Size, size => (scale ? "Scale" : "Size") + size,
                    size => ChangeOption(a => a with { Size = size }, "size"));
            }
            if (StyleRow.Visibility == Visibility.Visible)
            {
                switch (shown.Kind)
                {
                    case AnnotationKind.Arrow:
                        FillSegments(StyleList, "AnnotationStyle", Enum.GetValues<ArrowStyle>(), shown.Arrow, s => "Arrow" + s, s => ChangeOption(a => a with { Arrow = s }, "style"));
                        break;
                    case AnnotationKind.Rectangle or AnnotationKind.Ellipse:
                        FillSegments(StyleList, "AnnotationStyle", Enum.GetValues<ShapeStyle>(), shown.Shape, s => "Shape" + s, s => ChangeOption(a => a with { Shape = s }, "style"));
                        break;
                    case AnnotationKind.Text:
                        FillSegments(StyleList, "AnnotationStyle", Enum.GetValues<TextStyle>(), shown.TextStyle, s => "TextStyle" + s, s => ChangeOption(a => a with { TextStyle = s }, "style"));
                        break;
                    case AnnotationKind.Mask:
                        FillSegments(StyleList, "AnnotationStyle", Enum.GetValues<MaskStyle>(), shown.Mask, s => "Mask" + s, s => ChangeOption(a => a with { Mask = s }, "style"));
                        break;
                }
            }
            if (shown.Kind == AnnotationKind.Pointer)
            {
                foreach (var (style, button) in _pointerButtons)
                    button.IsChecked = style == shown.Pointer;
                PointerSizeSlider.Value = shown.PointerSize;
                PointerSizeValue.Text = Strings.Format("EditorPixels", shown.PointerSize);
                HaloSwitch.IsChecked = shown.Halo;
                // The ring of a still pointer; a moving one shows its click when it arrives instead.
                HaloRow.Visibility = Show(shown.Motion is null);
                MotionRow.Visibility = Show(IsVideo && selected is not null);
                if (IsVideo && selected is not null)
                    ShowMotionOptions(selected);
            }
        }
        NextNumberText.Text = Strings.Format("AnnotNextNumber", Layer.NextNumber);
        if (_capture is not null)
        {
            var kept = Layer.Visible(_capture.Width, _capture.Height);
            CropText.Text = Strings.Format("CropKept", kept.Width, kept.Height);
            CropAllButton.IsEnabled = Layer.Crop is not null;
        }
        UndoButton.IsEnabled = _history.CanUndo;
        RedoButton.IsEnabled = _history.CanRedo;
        ClearAllButton.IsEnabled = !_history.Current.Items.IsEmpty;
        if (listToo)
            ShowAnnotationList();
        _syncingEdit = false;
    }

    private static Visibility Show(bool visible) => visible ? Visibility.Visible : Visibility.Collapsed;

    // The choices of a segmented control, rebuilt for the kind shown (they differ from one tool to another).
    private void FillSegments<T>(UniformGrid list, string group, T[] values, T current, Func<T, string> key, Action<T> pick) where T : struct, Enum
    {
        // Rebuilt when the choices or their names change (a thickness for a line, a size for a text).
        string tag = string.Join("|", values.Select(key));
        if (list.Tag as string != tag)
        {
            list.Children.Clear();
            list.Tag = tag;
            foreach (var value in values)
            {
                var button = new RadioButton { Style = (Style)FindResource("Segment"), GroupName = group, Content = Strings.Get(key(value)), Tag = value };
                if (typeof(T) == typeof(ShapeStyle) && value.Equals(ShapeStyle.Spotlight))
                    button.ToolTip = Strings.Get("ShapeSpotlightToolTip");
                button.Checked += (_, _) =>
                {
                    if (!_syncingEdit)
                        pick(value);
                };
                list.Children.Add(button);
            }
        }
        foreach (RadioButton button in list.Children)
            button.IsChecked = button.Tag is T value && value.Equals(current);
    }

    // The annotations, the one on top first; a click chooses one.
    private void ShowAnnotationList()
    {
        AnnotationList.Children.Clear();
        var items = _history.Current.Items;
        AnnotationListEmpty.Visibility = Show(items.IsEmpty && !HasRecordedPointer);
        if (HasRecordedPointer)
        {
            string name = Strings.Get("RecordedPointer");
            bool hidden = _history.Current.Cursor.Hidden;
            var row = new RadioButton
            {
                Style = (Style)FindResource("ListRow"),
                GroupName = "Annotations",
                IsChecked = _cursorSelected,
                Content = new DockPanel
                {
                    Children =
                    {
                        Eye(hidden, name, () => ChangeCursor(o => o with { Hidden = !o.Hidden }, "hidden")),
                        Faded(ToolPath(Tools.First(t => t.Tool == EditTool.Pointer).Icon, 18), hidden),
                        Faded(new TextBlock { Text = name, Margin = new Thickness(10, 0, 8, 0), VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis }, hidden),
                    },
                },
            };
            System.Windows.Automation.AutomationProperties.SetName(row, name);
            row.Checked += (_, _) =>
            {
                if (!_syncingEdit)
                    SelectCursor();
            };
            AnnotationList.Children.Add(row);
        }
        for (int i = items.Count - 1; i >= 0; i--)
        {
            var annotation = items[i];
            var info = Tools.First(t => t.Kind == annotation.Kind);
            string name = AnnotationName(annotation);
            var swatch = new System.Windows.Shapes.Ellipse { Width = 12, Height = 12, StrokeThickness = 1, VerticalAlignment = VerticalAlignment.Center };
            swatch.SetResourceReference(System.Windows.Shapes.Shape.StrokeProperty, "ControlStrokeColorDefaultBrush");
            swatch.Fill = new SolidColorBrush(ToColor(annotation.Color));
            // The pointer and the masks have no color of their own.
            swatch.Visibility = AnnotationOptions.HasColor(annotation.Kind) ? Visibility.Visible : Visibility.Hidden;
            var row = new RadioButton
            {
                Style = (Style)FindResource("ListRow"),
                GroupName = "Annotations",
                IsChecked = annotation.Id == _selectedId,
                Content = new DockPanel
                {
                    LastChildFill = true,
                    Children =
                    {
                        Eye(annotation.Hidden, name, () => Commit(_history.Current.Replace(annotation with { Hidden = !annotation.Hidden }))),
                        Faded(ToolPath(info.Icon, 18), annotation.Hidden),
                        Docked(Faded(swatch, annotation.Hidden), Dock.Right),
                        Faded(new TextBlock { Text = name, Margin = new Thickness(10, 0, 8, 0), VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis }, annotation.Hidden),
                    },
                },
            };
            System.Windows.Automation.AutomationProperties.SetName(row, name);
            int id = annotation.Id;
            row.Tag = id;
            row.ToolTip = Strings.Get("AnnotListToolTip");
            row.Checked += (_, _) =>
            {
                if (_syncingEdit)
                    return;
                if (_tool != EditTool.Select)
                    SetTool(EditTool.Select);
                Select(id);
            };
            AnnotationList.Children.Add(row);
        }
    }

    // ---------------------------------------------------------------- Reordering the list

    private Point? _listPress;
    private RadioButton? _listDragged;
    private int _listDropSlot = -1;

    // The rows of the annotations (the recorded pointer's row stays on top, it is not one of them), top first.
    private List<RadioButton> AnnotationRows() => [.. AnnotationList.Children.OfType<RadioButton>().Where(r => r.Tag is int)];

    private void AnnotationList_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e) =>
        ListPress(e.GetPosition(AnnotationList), e.OriginalSource as DependencyObject);

    private void AnnotationList_PreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton == MouseButtonState.Pressed && ListDrag(e.GetPosition(AnnotationList)))
            e.Handled = true;
    }

    private void AnnotationList_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (ListDrop())
            e.Handled = true;
    }

    // The mouse code with explicit points, so that the Debug script player can play it (WPF ignores posted buttons).
    private void ListPress(Point at, DependencyObject? source)
    {
        _listPress = at;
        _listDragged = source is null ? null : AnnotationRows().FirstOrDefault(r => r == source || r.IsAncestorOf(source));
    }

    /// <summary>The mouse moved with the button down: a drag once it went far enough. True while dragging.</summary>
    private bool ListDrag(Point at)
    {
        if (_listPress is not { } press || _listDragged is not { } row)
            return false;
        if (!AnnotationList.IsMouseCaptured)
        {
            if (Math.Abs(at.Y - press.Y) < SystemParameters.MinimumVerticalDragDistance)
                return false;
            // A drag, not a click: the list takes the mouse from the row (whose click is then not one).
            AnnotationList.CaptureMouse();
            row.Opacity = 0.5;
            AnnotationList.Cursor = Cursors.SizeNS;
        }
        ShowDropSlot(at.Y);
        return true;
    }

    /// <summary>The button up: the row dragged goes where the line is. True when it was a drag.</summary>
    private bool ListDrop()
    {
        if (!AnnotationList.IsMouseCaptured || _listDragged is not { } row)
        {
            _listPress = null;
            return false;
        }
        int slot = _listDropSlot;
        EndListDrag();
        if (slot >= 0)
            ReorderInList(AnnotationRows().IndexOf(row), slot);
        return true;
    }

    // Only the list's own capture lost ends the drag: the row pressed loses its capture to the list as the drag starts,
    // and that bubbles up here too (it ended every drag at once: the author's “it does not work”).
    private void AnnotationList_LostMouseCapture(object sender, MouseEventArgs e)
    {
        if (_listDragged is not null && ReferenceEquals(e.OriginalSource, AnnotationList))
            EndListDrag();
    }

    private void EndListDrag()
    {
        if (_listDragged is { } row)
            row.Opacity = 1;
        _listPress = null;
        _listDragged = null;
        _listDropSlot = -1;
        AnnotationDrop.Children.Clear();
        AnnotationList.Cursor = null;
        if (AnnotationList.IsMouseCaptured)
            AnnotationList.ReleaseMouseCapture();
    }

    // Where the row would go: between the rows whose middle is above the pointer and the others; a line shows it.
    private void ShowDropSlot(double y)
    {
        var rows = AnnotationRows();
        int slot = 0;
        double lineY = 0;
        foreach (var row in rows)
        {
            double top = row.TranslatePoint(new Point(0, 0), AnnotationList).Y;
            if (y > top + row.ActualHeight / 2)
            {
                slot++;
                lineY = top + row.ActualHeight;
            }
            else
            {
                if (slot == 0)
                    lineY = top;
                break;
            }
        }
        _listDropSlot = slot;
        AnnotationDrop.Children.Clear();
        var line = new System.Windows.Shapes.Rectangle { Width = AnnotationList.ActualWidth, Height = 2, RadiusX = 1, RadiusY = 1, Fill = (Brush)FindResource("AccentFillColorDefaultBrush") };
        Canvas.SetTop(line, lineY - 1);
        AnnotationDrop.Children.Add(line);
    }

    /// <summary>
    /// The annotation of row <paramref name="from"/> of the list put at <paramref name="slot"/> (0: above the first row,
    /// the top of the drawing). The list shows the one on top first: its rows are the layer's items from the end.
    /// </summary>
    private void ReorderInList(int from, int slot)
    {
        var items = _history.Current.Items;
        if (from < 0 || from >= items.Count)
            return;
        int to = slot > from ? slot - 1 : slot;
        if (to == from)
            return;
        var annotation = items[items.Count - 1 - from];
        Commit(_history.Current.MoveTo(annotation.Id, items.Count - 1 - to));
    }

    // The eye of a row (asked by the author): hides the layer or shows it again; the row stays, faded.
    private Button Eye(bool hidden, string name, Action toggle)
    {
        string what = Strings.Format(hidden ? "LayerShow" : "LayerHide", name);
        var eye = new Button
        {
            Width = 28,
            Height = 24,
            Padding = new Thickness(0),
            Margin = new Thickness(4, 0, 0, 0),
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            Focusable = false,
            Content = new TextBlock { Style = (Style)FindResource("IconText"), FontSize = 13, Text = hidden ? "\uED1A" : "\uE7B3", Opacity = hidden ? 0.6 : 1 },
            ToolTip = what,
        };
        System.Windows.Automation.AutomationProperties.SetName(eye, what);
        eye.Click += (_, e) =>
        {
            e.Handled = true;
            CommitText();
            toggle();
        };
        DockPanel.SetDock(eye, Dock.Right);
        return eye;
    }

    private static T Faded<T>(T element, bool hidden) where T : UIElement
    {
        if (hidden)
            element.Opacity = 0.45;
        return element;
    }

    private static UIElement Docked(UIElement element, Dock dock)
    {
        DockPanel.SetDock(element, dock);
        return element;
    }

    private static string Shorten(string text, int length) => text.Length <= length ? text : text[..(length - 1)] + "…";

    // ---------------------------------------------------------------- Keys of the Edit tab

    /// <summary>The keys of the tools and of the annotation chosen; true when the key was used.</summary>
    private bool EditKey(KeyEventArgs e)
    {
        if (!_editTab || _capture is null || Keyboard.FocusedElement is TextBox)
            return false;
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        var modifiers = Keyboard.Modifiers;
        if (modifiers == ModifierKeys.None && Tools.FirstOrDefault(t => t.Key == key) is { } tool)
        {
            SetTool(tool.Tool);
            return true;
        }
        switch (key)
        {
            case Key.Delete or Key.Back when Selected is not null:
                DeleteSelected();
                return true;
            case Key.Escape:
                if (_drag is not null)
                    CancelDrag();
                else if (_selectedId is not null)
                    Select(null);
                else if (_tool != EditTool.Select)
                    SetTool(EditTool.Select);
                else
                    return false;
                return true;
            case Key.Space when modifiers == ModifierKeys.None:
                return true;
            case Key.Enter when _tool == EditTool.Crop:
                SetTool(EditTool.Select);
                return true;
            case Key.Left or Key.Right or Key.Up or Key.Down when Selected is not null && Keyboard.FocusedElement is not Slider:
            {
                float step = modifiers.HasFlag(ModifierKeys.Shift) ? 10 : 1;
                Nudge(key == Key.Left ? -step : key == Key.Right ? step : 0, key == Key.Up ? -step : key == Key.Down ? step : 0);
                return true;
            }
        }
        return false;
    }
}
