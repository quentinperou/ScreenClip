using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using ScreenClip.Core.Annotations;
using ScreenClip.Core.Video;
using ScreenClip.Services;
using SkiaSharp;

namespace ScreenClip;

/// <summary>
/// The tracks of a video under its timeline (the mock-up's VideoEdit board): the recorded pointer with its clicks, then
/// a bar per annotation, when it shows. A bar is dragged in time or stretched by its ends; the diamonds of an A → B
/// pointer are when it leaves and arrives. And the options of the recorded pointer, in the Edit panel.
/// </summary>
public partial class EditorWindow
{
    private enum TrackGrip { Move, Start, End, Leave, Arrive }

    private enum PointerMovement { Real, Smooth }

    private enum MotionPathKind { Curve, Straight }

    private enum MotionPace { Eased, Steady }

    private sealed record TrackDrag(TrackGrip Grip, Annotation Original, AnnotationLayer Base, double StartX);

    private const double TrackHeight = 24, TrackGap = 3, VisibleTracks = 4;

    private TrackDrag? _trackDrag;
    private bool _cursorSelected;                 // the recorded pointer is the one chosen
    private System.Windows.Shapes.Rectangle? _trackPlayhead;
    private readonly Dictionary<RecordedPointerStyle, RadioButton> _recordedStyleButtons = [];
    private Image? _asRecordedSwatch;

    private PointerTrack? Track => _videoClip?.Pointer;

    private bool HasRecordedPointer => Track is { IsEmpty: false };

    /// <summary>The moment of the video shown (null for a picture): what the annotations and the pointer are drawn at.</summary>
    private VideoMoment? Moment => IsVideo ? new VideoMoment(_playhead, Track) : null;

    /// <summary>Something drawn over the frames changes with the time: the recorded pointer, a timed or moving annotation.</summary>
    private bool HasTimedOverlay =>
        (HasRecordedPointer && _history.Current.Cursor.Shows)
        || Layer.Items.Any(a => a.Time is not null || a.Motion is not null);

    /// <summary>Whether the annotation chosen shows at the playhead (always, in a picture).</summary>
    private bool IsShownNow(Annotation annotation) => !annotation.Hidden && (!IsVideo || annotation.IsShownAt(_playhead));

    // ---------------------------------------------------------------- Building

    private void BuildRecordedPointerPanel()
    {
        // As high as the drawn pointers show in their tiles.
        _asRecordedSwatch = new Image { Width = 40, Height = 28, Margin = new Thickness(0, 10, 0, 6), Stretch = Stretch.Uniform };
        RenderOptions.SetBitmapScalingMode(_asRecordedSwatch, BitmapScalingMode.HighQuality);
        // Hidden is the eye of its row in the list, not a style.
        foreach (var style in Enum.GetValues<RecordedPointerStyle>().Where(s => s != RecordedPointerStyle.Hidden))
        {
            UIElement picture = style switch
            {
                RecordedPointerStyle.AsRecorded => _asRecordedSwatch,
                _ => new Image { Width = 40, Height = 40, Margin = new Thickness(0, 4, 0, 0), Source = PointerSwatch(Enum.Parse<PointerStyle>(style.ToString())) },
            };
            string name = Strings.Get("RecordedStyle" + style);
            var label = new TextBlock { Text = name, FontSize = 12, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 2, 0, 2) };
            var tile = Tile("RecordedPointer", new StackPanel { Children = { picture, label } }, name);
            tile.Checked += (_, _) => ChangeCursor(o => o with { Style = style }, "style");
            _recordedStyleButtons[style] = tile;
            RecordedStyleList.Children.Add(tile);
        }
        RecordedSizeSlider.Minimum = RecordedPointerOptions.MinSize;
        RecordedSizeSlider.Maximum = RecordedPointerOptions.MaxSize;
    }

    // The first shape the pointer had (the arrow, usually), as the “as it was” swatch.
    private void ShowAsRecordedSwatch()
    {
        if (_asRecordedSwatch is null)
            return;
        _asRecordedSwatch.Source = null;
        if (Track is not { } track || track.Shapes.Count == 0)
            return;
        int index = Math.Max(0, track.Samples.FirstOrDefault(s => s.Shape >= 0).Shape);
        if (track.ImageOf(index) is not { } image)
            return;
        // Only what the pointer draws: its picture is larger, the arrow in its top left corner.
        var shape = track.Shapes[index];
        int left = shape.Width, top = shape.Height, right = -1, bottom = -1;
        for (int y = 0; y < shape.Height; y++)
        {
            for (int x = 0; x < shape.Width; x++)
            {
                if (shape.Pixels[(y * shape.Width + x) * 4 + 3] == 0)
                    continue;
                left = Math.Min(left, x);
                top = Math.Min(top, y);
                right = Math.Max(right, x);
                bottom = Math.Max(bottom, y);
            }
        }
        using var drawn = right < 0 ? null : image.Subset(SKRectI.Create(left, top, right - left + 1, bottom - top + 1));
        _asRecordedSwatch.Source = Imaging.ToWpf(drawn ?? image);
    }

    // ---------------------------------------------------------------- The recorded pointer chosen

    private void SelectCursor()
    {
        if (!HasRecordedPointer)
            return;
        CommitText();
        CancelDrag();
        if (!_editTab)
            SetTab(true);
        if (_tool != EditTool.Select)
            SetTool(EditTool.Select);
        _selectedId = null;
        _cursorSelected = true;
        ShowEditPanel();
        ShowTracks();
        RequestRender();
    }

    /// <summary>An option of the recorded pointer changed: undone as the others (the next video starts from the defaults).</summary>
    private void ChangeCursor(Func<RecordedPointerOptions, RecordedPointerOptions> change, string option)
    {
        if (_syncingEdit || !IsVideo)
            return;
        var options = change(_history.Current.Cursor).Normalized();
        Commit(_history.Current with { Cursor = options }, "cursor" + option);
    }

    private void RecordedSize_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (!_initialized)
            return;
        int size = (int)RecordedSizeSlider.Value;
        RecordedSizeValue.Text = Strings.Format("EditorPercent", size);
        ChangeCursor(o => o with { Size = size }, "size");
    }

    private void ShowRecordedPointerPanel()
    {
        var options = _history.Current.Cursor;
        foreach (var (style, button) in _recordedStyleButtons)
            button.IsChecked = style == options.Style;
        RecordedSizeSlider.Value = options.Size;
        RecordedSizeValue.Text = Strings.Format("EditorPercent", options.Size);
        FillSegments(RecordedMovementList, "RecordedMovement", Enum.GetValues<PointerMovement>(), options.Smooth ? PointerMovement.Smooth : PointerMovement.Real,
            m => "Movement" + m, m => ChangeCursor(o => o with { Smooth = m == PointerMovement.Smooth }, "smooth"));
        FillSegments(RecordedClickList, "RecordedClick", Enum.GetValues<ClickEffect>(), options.Click, c => "Click" + c,
            c => ChangeCursor(o => o with { Click = c }, "click"));
    }

    // ---------------------------------------------------------------- An A → B pointer

    /// <summary>A new A → B motion at the playhead, kept inside the video.</summary>
    private PointerMotion MotionAt(SKPoint from, SKPoint to)
    {
        var motion = VideoTiming.NewMotion(from, to, _playhead, Defaults);
        var span = motion.Arrive - motion.Leave;
        if (motion.Arrive > _duration)
            motion = motion with { Arrive = _duration, Leave = _duration - span < TimeSpan.Zero ? TimeSpan.Zero : _duration - span };
        return motion;
    }

    private void Motion_Changed(object sender, RoutedEventArgs e)
    {
        if (!_initialized || _syncingEdit || Selected is not { Kind: AnnotationKind.Pointer } pointer || _capture is null)
            return;
        bool on = MotionSwitch.IsChecked == true;
        if (on == (pointer.Motion is not null))
            return;
        if (!on)
        {
            Commit(_history.Current.Replace(pointer with { Motion = null }));
            return;
        }
        // From up and to the left of where it is (or the other way near that corner); there at the playhead, on its
        // way the second before.
        float dx = _capture.Width * 0.2f, dy = _capture.Height * 0.16f;
        var b = pointer.Start;
        var a = new SKPoint(b.X - dx < 0 ? b.X + dx : b.X - dx, b.Y - dy < 0 ? b.Y + dy : b.Y - dy);
        var shown = VideoTiming.TimeOf(pointer, _duration);
        var arrive = _playhead < shown.Start + TimeSpan.FromSeconds(1) ? shown.Start + TimeSpan.FromSeconds(1) : _playhead;
        if (arrive > shown.End)
            arrive = shown.End;
        var leave = arrive - TimeSpan.FromSeconds(1) < shown.Start ? shown.Start : arrive - TimeSpan.FromSeconds(1);
        var motion = VideoTiming.NewMotion(a, b, leave, Defaults) with { Leave = leave, Arrive = arrive };
        var changed = pointer with { Motion = motion };
        Defaults = Defaults.Follow(changed);
        Commit(_history.Current.Replace(changed));
    }

    private void ShowMotionOptions(Annotation pointer)
    {
        MotionSwitch.IsChecked = pointer.Motion is not null;
        MotionOptions.Visibility = Show(pointer.Motion is not null);
        if (pointer.Motion is not { } motion)
            return;
        FillSegments(MotionPathList, "MotionPath", Enum.GetValues<MotionPathKind>(), motion.Curved ? MotionPathKind.Curve : MotionPathKind.Straight,
            k => "MotionPath" + k, k => ChangeOption(a => a.Motion is null ? a : a with { Motion = a.Motion with { Curved = k == MotionPathKind.Curve } }, "motionPath"));
        FillSegments(MotionPaceList, "MotionPace", Enum.GetValues<MotionPace>(), motion.Eased ? MotionPace.Eased : MotionPace.Steady,
            p => "MotionPace" + p, p => ChangeOption(a => a.Motion is null ? a : a with { Motion = a.Motion with { Eased = p == MotionPace.Eased } }, "motionPace"));
        FillSegments(MotionClickList, "MotionClick", Enum.GetValues<ClickEffect>(), motion.Click, c => "Click" + c,
            c => ChangeOption(a => a.Motion is null ? a : a with { Motion = a.Motion with { Click = c } }, "motionClick"));
    }

    // ---------------------------------------------------------------- Tracks

    /// <summary>The rows: the recorded pointer first (it is drawn over all), then the annotations, the one on top first.</summary>
    private List<Annotation?> TrackRows()
    {
        var rows = new List<Annotation?>();
        if (HasRecordedPointer)
            rows.Add(null);
        var items = Layer.Items;
        for (int i = items.Count - 1; i >= 0; i--)
            rows.Add(items[i]);
        return rows;
    }

    /// <summary>The tracks again: after a change of the annotations, of their times, of the part kept or of the size.</summary>
    private void ShowTracks()
    {
        TrackCanvas.Children.Clear();
        _trackPlayhead = null;
        var rows = IsVideo ? TrackRows() : [];
        TrackScroll.Visibility = Show(rows.Count > 0);
        double width = Timeline.ActualWidth;
        if (rows.Count == 0 || width <= 0)
            return;
        TrackScroll.MaxHeight = VisibleTracks * (TrackHeight + TrackGap);
        TrackCanvas.Width = width;
        TrackCanvas.Height = rows.Count * (TrackHeight + TrackGap) - TrackGap;
        var accent = (Brush)FindResource("AccentFillColorDefaultBrush");
        var text = (Brush)FindResource("TextFillColorPrimaryBrush");
        var orange = new SolidColorBrush(Color.FromRgb(0xF7, 0xA0, 0x15));
        for (int row = 0; row < rows.Count; row++)
        {
            double y = row * (TrackHeight + TrackGap);
            if (rows[row] is not { } annotation)
            {
                // The recorded pointer: all along, a tick at each click.
                var bar = Bar(0, width, y, Color.FromRgb(0x8A, 0x8F, 0x98), _cursorSelected, accent, text, Strings.Get("RecordedPointer"), Tools.First(t => t.Tool == EditTool.Pointer).Icon);
                if (!_history.Current.Cursor.Shows)
                    bar.Opacity = 0.45;
                TrackCanvas.Children.Add(bar);
                foreach (var click in Track!.Clicks)
                    Place(new System.Windows.Shapes.Rectangle { Width = 2, Height = 10, Fill = orange, RadiusX = 1, RadiusY = 1 }, XOf(click.Down) - 1, y + TrackHeight - 12);
                continue;
            }
            var time = VideoTiming.TimeOf(annotation, _duration);
            double x1 = XOf(time.Start), x2 = Math.Max(x1 + 4, XOf(time.End));
            var color = AnnotationOptions.HasColor(annotation.Kind) ? ToColor(annotation.Color)
                : annotation.Kind == AnnotationKind.Pointer ? Color.FromRgb(0x00, 0x90, 0xFF) : Color.FromRgb(0x8A, 0x8F, 0x98);
            var info = Tools.First(t => t.Kind == annotation.Kind);
            var annotationBar = Bar(x1, x2 - x1, y, color, annotation.Id == _selectedId, accent, text, AnnotationName(annotation), info.Icon);
            if (annotation.Hidden)
                annotationBar.Opacity = 0.45;
            TrackCanvas.Children.Add(annotationBar);
            if (annotation.Motion is { } motion)
            {
                Place(Diamond(Color.FromRgb(0x00, 0x5F, 0xB8)), XOf(motion.Leave) - 5, y + TrackHeight / 2 - 5);
                Place(Diamond(motion.Click == ClickEffect.None ? Color.FromRgb(0x00, 0x5F, 0xB8) : Color.FromRgb(0xF7, 0xA0, 0x15)), XOf(motion.Arrive) - 5, y + TrackHeight / 2 - 5);
            }
        }
        // What is cut, darkened as on the frames.
        var cut = new SolidColorBrush(Color.FromArgb(0x70, 0x10, 0x10, 0x14));
        Place(new System.Windows.Shapes.Rectangle { Width = Math.Max(0, XOf(_trim.Start)), Height = TrackCanvas.Height, Fill = cut, IsHitTestVisible = false }, 0, 0);
        Place(new System.Windows.Shapes.Rectangle { Width = Math.Max(0, width - XOf(_trim.End)), Height = TrackCanvas.Height, Fill = cut, IsHitTestVisible = false }, XOf(_trim.End), 0);
        _trackPlayhead = new System.Windows.Shapes.Rectangle { Width = 2, Height = TrackCanvas.Height, Fill = new SolidColorBrush(Color.FromRgb(0xE5, 0x48, 0x4D)), IsHitTestVisible = false };
        Place(_trackPlayhead, XOf(_playhead) - 1, 0);
    }

    // A bar of a track: tinted with the annotation's color, its icon and name inside.
    private Border Bar(double x, double width, double y, Color color, bool chosen, Brush accent, Brush text, string name, string icon)
    {
        var path = ToolPath(icon, 18);
        path.Width = path.Height = 18;
        path.RenderTransform = new ScaleTransform(0.75, 0.75, 0, 9);
        path.Stroke = text;
        var bar = new Border
        {
            Width = Math.Max(4, width),
            Height = TrackHeight,
            CornerRadius = new CornerRadius(5),
            Background = new SolidColorBrush(Color.FromArgb(0x38, color.R, color.G, color.B)),
            BorderBrush = chosen ? accent : new SolidColorBrush(Color.FromArgb(0xB0, color.R, color.G, color.B)),
            BorderThickness = new Thickness(chosen ? 2 : 1),
            ClipToBounds = true,
            ToolTip = name,
            Child = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Margin = new Thickness(6, 0, 4, 0),
                VerticalAlignment = VerticalAlignment.Center,
                Children = { path, new TextBlock { Text = name, FontSize = 11, Foreground = text, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(2, 0, 0, 0) } },
            },
        };
        Canvas.SetLeft(bar, x);
        Canvas.SetTop(bar, y);
        return bar;
    }

    private static System.Windows.Shapes.Rectangle Diamond(Color color) => new()
    {
        Width = 10,
        Height = 10,
        Fill = new SolidColorBrush(color),
        Stroke = Brushes.White,
        StrokeThickness = 1.5,
        RenderTransform = new RotateTransform(45, 5, 5),
        IsHitTestVisible = false,
    };

    private void Place(UIElement element, double x, double y)
    {
        Canvas.SetLeft(element, x);
        Canvas.SetTop(element, y);
        TrackCanvas.Children.Add(element);
    }

    /// <summary>The playhead moved: its line on the tracks follows, nothing else is drawn again.</summary>
    private void MoveTrackPlayhead()
    {
        if (_trackPlayhead is not null)
            Canvas.SetLeft(_trackPlayhead, XOf(_playhead) - 1);
    }

    // ---------------------------------------------------------------- Dragging on the tracks

    private void TrackCanvas_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (TrackDown(e.GetPosition(TrackCanvas)))
        {
            TrackCanvas.CaptureMouse();
            e.Handled = true;
        }
    }

    private void TrackCanvas_MouseMove(object sender, MouseEventArgs e) =>
        TrackMove(e.GetPosition(TrackCanvas), e.LeftButton == MouseButtonState.Pressed);

    private void TrackCanvas_MouseLeftButtonUp(object sender, MouseButtonEventArgs e) => TrackUp();

    private void TrackCanvas_LostMouseCapture(object sender, MouseEventArgs e)
    {
        if (_trackDrag is not null)
            TrackUp();
    }

    private Annotation? TrackRowAt(Point at, out bool cursorRow)
    {
        cursorRow = false;
        var rows = TrackRows();
        int index = (int)Math.Floor(at.Y / (TrackHeight + TrackGap));
        if (index < 0 || index >= rows.Count)
            return null;
        cursorRow = rows[index] is null;
        return rows[index];
    }

    private TrackGrip? GripOf(Annotation annotation, double x)
    {
        const double Reach = 6;
        if (annotation.Motion is { } motion)
        {
            if (Math.Abs(x - XOf(motion.Arrive)) <= Reach)
                return TrackGrip.Arrive;
            if (Math.Abs(x - XOf(motion.Leave)) <= Reach)
                return TrackGrip.Leave;
        }
        var time = VideoTiming.TimeOf(annotation, _duration);
        double x1 = XOf(time.Start), x2 = XOf(time.End);
        if (Math.Abs(x - x1) <= Reach && Math.Abs(x - x1) <= Math.Abs(x - x2))
            return TrackGrip.Start;
        if (Math.Abs(x - x2) <= Reach)
            return TrackGrip.End;
        return x > x1 && x < x2 ? TrackGrip.Move : null;
    }

    /// <summary>A press on the tracks: the row's annotation is chosen, and its bar or a diamond is grabbed. True when dragging.</summary>
    private bool TrackDown(Point at)
    {
        if (!IsVideo || _export is not null)
            return false;
        StopPlayback();
        CommitText();
        CancelDrag();
        var annotation = TrackRowAt(at, out bool cursorRow);
        if (cursorRow)
        {
            SelectCursor();
            return false;
        }
        if (annotation is null)
            return false;
        if (!_editTab)
            SetTab(true);
        if (_tool != EditTool.Select)
            SetTool(EditTool.Select);
        Select(annotation.Id);
        if (GripOf(annotation, at.X) is not { } grip)
            return false;
        // The playhead stays where it is (the author: moving it while a bar is shortened was in the way).
        _trackDrag = new TrackDrag(grip, annotation, _history.Current, at.X);
        return true;
    }

    private void TrackMove(Point at, bool pressed)
    {
        if (!IsVideo)
            return;
        if (_trackDrag is not { } drag)
        {
            var annotation = TrackRowAt(at, out _);
            TrackCanvas.Cursor = annotation is null ? Cursors.Arrow : GripOf(annotation, at.X) switch
            {
                TrackGrip.Move => Cursors.SizeAll,
                null => Cursors.Arrow,
                _ => Cursors.SizeWE,
            };
            return;
        }
        if (!pressed)
        {
            TrackUp();
            return;
        }
        double width = Math.Max(1, Timeline.ActualWidth);
        var time = TimeAt(at.X);
        var original = drag.Original;
        var changed = drag.Grip switch
        {
            TrackGrip.Move => VideoTiming.TimeMovedBy(original, _duration * ((at.X - drag.StartX) / width), _duration),
            TrackGrip.Start => VideoTiming.WithShowStart(original, time, _duration),
            TrackGrip.End => VideoTiming.WithShowEnd(original, time, _duration),
            TrackGrip.Leave => VideoTiming.WithLeave(original, time, _duration),
            _ => VideoTiming.WithArrive(original, time, _duration),
        };
        _live = drag.Base.Replace(changed);
        // The playhead follows the moment being set, so that the picture shows it (asked by the author once moving
        // it became fluid): the start or the last frame of the bar, the leaving or the arrival; a bar moved whole
        // keeps it unless it leaves the bar.
        var shown = VideoTiming.TimeOf(changed, _duration);
        var follow = drag.Grip switch
        {
            TrackGrip.Start => shown.Start,
            TrackGrip.End => shown.End - TimeSpan.FromMilliseconds(1),
            TrackGrip.Leave => changed.Motion!.Leave,
            TrackGrip.Arrive => changed.Motion!.Arrive,
            _ => shown.Contains(_playhead) ? _playhead : shown.Start,
        };
        RequestFrame(_trim.Clamp(follow));
        ShowTracks();
        RequestRender();
    }

    private void TrackUp()
    {
        if (_trackDrag is null)
            return;
        _trackDrag = null;
        if (TrackCanvas.IsMouseCaptured)
            TrackCanvas.ReleaseMouseCapture();
        if (_live is { } changed)
            Commit(changed);
        else
            AnnotationsChanged();
    }

    // ---------------------------------------------------------------- Names

    /// <summary>The name of an annotation in the list and on its track.</summary>
    private static string AnnotationName(Annotation annotation) => annotation.Kind switch
    {
        AnnotationKind.Text => Strings.Format("AnnotListText", Shorten(annotation.Text.Replace('\n', ' '), 24)),
        AnnotationKind.Number => Strings.Format("AnnotListNumber", annotation.Number),
        AnnotationKind.Pointer when annotation.Motion is not null => Strings.Get("AnnotListMotion"),
        _ => Strings.Get("Tool" + Tools.First(t => t.Kind == annotation.Kind).Tool),
    };
}
