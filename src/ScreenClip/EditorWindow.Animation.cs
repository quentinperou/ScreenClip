using System.Globalization;
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
/// A still picture animated (asked by the author): “Animate” gives it a length and the timeline of a video, so that a
/// pointer moves on it and annotations come and go, then it is exported as a GIF or an MP4. The steps of a moving
/// pointer's way (A → B → C…): a click with the Pointer tool takes it on, a right click on it adds, removes or
/// smooths. And the handle above the timeline that gives the tracks more room.
/// </summary>
public partial class EditorWindow
{
    private static readonly TimeSpan DefaultAnimation = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan MinAnimation = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan MaxAnimation = TimeSpan.FromSeconds(60);
    private const double MinPreviewHeight = 180;

    private bool _animated;                      // the timeline of an animation shows, in step with the layer
    private TimeSpan _span;                      // what the timeline's width shows: more than an animation, so that its end is pulled further
    private double _tracksExtra;                 // more room for the tracks, from the handle (back to none in each new editor)

    /// <summary>A still picture animated (“Animate”: a length in the layer).</summary>
    private bool IsAnimation => _videoClip is null && _capture is not null && Layer.Animation is not null;

    /// <summary>Something in time: a video, or a picture animated (the timeline, the tracks, the moving pointers).</summary>
    private bool IsTimed => IsVideo || IsAnimation;

    /// <summary>The time the timeline's width shows.</summary>
    private TimeSpan Span => IsVideo || _span <= _duration ? _duration : _span;

    private static TimeSpan SpanOf(TimeSpan length) => length + TimeSpan.FromSeconds(Math.Max(2, Math.Ceiling(length.TotalSeconds * 0.25)));

    // ---------------------------------------------------------------- Animate

    private void Animate_Click(object sender, RoutedEventArgs e) => ToggleAnimation();

    private void ToggleAnimation()
    {
        if (_capture is null || IsVideo || _export is not null)
        {
            AnimateButton.IsChecked = IsAnimation;
            return;
        }
        CommitText();
        CancelDrag();
        StopPlayback();
        var layer = _history.Current;
        if (layer.Animation is null)
        {
            // Long enough for the ways set before (a pointer that already moves).
            Commit(Lengthened(layer with { Animation = DefaultAnimation }));
            if (!_editTab)
                SetTab(true);
        }
        else
            Commit(layer with { Animation = null });
    }

    /// <summary>The layer's animation shown (turned on or off, its length changed or undone): the timeline and its length.</summary>
    private void SyncAnimation()
    {
        if (IsVideo)
        {
            _animated = false;
            return;
        }
        if (Layer.Animation is { } length && _capture is not null)
        {
            bool starting = !_animated;
            _animated = true;
            if (starting || length != _duration)
            {
                _duration = length;
                _trim = TrimRange.All(length);
                if (_grip != TimelineGrip.End)
                    _span = SpanOf(length);
            }
            if (starting)
            {
                _playhead = TimeSpan.Zero;
                ShowStillStrip();
            }
            if (_playhead > _duration)
                _playhead = _duration;
        }
        else if (_animated)
        {
            StopPlayback();
            _animated = false;
            _duration = _playhead = TimeSpan.Zero;
            ThumbStrip.Source = null;
        }
        ShowAnimationLength();
        ShowState();
    }

    /// <summary>
    /// An animation long enough for every way in it (a pointer taken further makes it longer, up to a minute); a
    /// video or a still picture as it is.
    /// </summary>
    private AnnotationLayer Lengthened(AnnotationLayer layer)
    {
        if (IsVideo || layer.Animation is not { } length)
            return layer;
        var needed = layer.Items.Select(VideoTiming.EndOf).DefaultIfEmpty(TimeSpan.Zero).Max();
        if (needed <= length)
            return layer;
        var longer = TimeSpan.FromSeconds(Math.Ceiling(needed.TotalSeconds * 10) / 10);
        return layer with { Animation = longer > MaxAnimation ? MaxAnimation : longer };
    }

    // The picture along the timeline, as a video's frames.
    private void ShowStillStrip()
    {
        if (_capture is null)
            return;
        const int Height = 72;
        int width = Math.Max(1, (int)Math.Round(Height * (double)_capture.Width / _capture.Height));
        int count = Math.Clamp((int)(Timeline.ActualWidth / 72), 6, 24);
        using var strip = SKSurface.Create(new SKImageInfo(width * count, Height, SKColorType.Bgra8888, SKAlphaType.Premul));
        strip.Canvas.Clear(SKColors.Black);
        for (int i = 0; i < count; i++)
            strip.Canvas.DrawImage(_capture, SKRect.Create(i * width, 0, width, Height), new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.Linear));
        using var picture = strip.Snapshot();
        ThumbStrip.Source = Imaging.ToWpf(picture);
    }

    // ---------------------------------------------------------------- Its length

    private void ShowAnimationLength()
    {
        AnimationLengthPanel.Visibility = Show(_animated);
        if (!_animated || AnimationLengthBox.IsKeyboardFocusWithin)
            return;
        AnimationLengthBox.Text = _duration.TotalSeconds.ToString("0.#", CultureInfo.CurrentCulture);
    }

    private void AnimationLength_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            ApplyAnimationLength();
            Stage.Focus();
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            Stage.Focus();
            ShowAnimationLength();
            e.Handled = true;
        }
    }

    private void AnimationLength_LostFocus(object sender, RoutedEventArgs e) => ApplyAnimationLength();

    private void ApplyAnimationLength()
    {
        if (!_animated)
            return;
        string text = AnimationLengthBox.Text.Trim().Replace(',', '.');
        if (double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out double seconds) && double.IsFinite(seconds))
            SetAnimationLength(TimeSpan.FromSeconds(seconds));
        else
            ShowAnimationLength();
    }

    /// <summary>The animation made that long (1 s to a minute, to the tenth); one step to undo.</summary>
    private void SetAnimationLength(TimeSpan length)
    {
        length = length < MinAnimation ? MinAnimation : length > MaxAnimation ? MaxAnimation : length;
        length = TimeSpan.FromSeconds(Math.Round(length.TotalSeconds, 1));
        _span = SpanOf(length);
        if (_history.Current.Animation == length)
        {
            ShowAnimationLength();
            ShowTimeline();
            ShowTracks();
            return;
        }
        Commit(_history.Current with { Animation = length });
    }

    // ---------------------------------------------------------------- The way of a moving pointer

    /// <summary>
    /// The layer with the pointer's way taken on to <paramref name="point"/> (A → B → C…); null when a video has no time
    /// left for it (said). An animation grows to hold it.
    /// </summary>
    private AnnotationLayer? WithWayTo(AnnotationLayer layer, Annotation pointer, SKPoint point) =>
        Fitted(layer, VideoTiming.WithNextStop(pointer, point));

    // The pointer changed in the layer, if it fits: a video keeps its length (null and said when it does not fit), an
    // animation grows.
    private AnnotationLayer? Fitted(AnnotationLayer layer, Annotation changed)
    {
        if (IsVideo && changed.Motion is { } motion && motion.Arrive > _duration)
        {
            ShowStatus(Strings.Get("MotionNoRoom"));
            return null;
        }
        if (IsVideo && changed.Time is { } time && time.End > _duration)
            changed = changed with { Time = time.Start <= TimeSpan.Zero ? null : time with { End = _duration } };
        return Lengthened(layer.Replace(changed));
    }

    /// <summary>A still pointer made to move (A → B), in an animation turned on for it when the picture was still.</summary>
    private void MakeMove(Annotation pointer)
    {
        if (_capture is null || pointer.Motion is not null)
            return;
        var layer = _history.Current;
        var duration = _duration;
        if (!IsTimed)
        {
            layer = layer with { Animation = DefaultAnimation };
            duration = DefaultAnimation;
        }
        // From up and to the left of where it is (or the other way near that corner); there at the playhead, on its
        // way the second before.
        float dx = _capture.Width * 0.2f, dy = _capture.Height * 0.16f;
        var b = pointer.Start;
        var a = new SKPoint(b.X - dx < 0 ? b.X + dx : b.X - dx, b.Y - dy < 0 ? b.Y + dy : b.Y - dy);
        var shown = VideoTiming.TimeOf(pointer, duration);
        var playhead = IsTimed ? _playhead : TimeSpan.Zero;
        var arrive = playhead < shown.Start + TimeSpan.FromSeconds(1) ? shown.Start + TimeSpan.FromSeconds(1) : playhead;
        if (arrive > shown.End)
            arrive = shown.End;
        var leave = arrive - TimeSpan.FromSeconds(1) < shown.Start ? shown.Start : arrive - TimeSpan.FromSeconds(1);
        var motion = VideoTiming.NewMotion(a, b, leave, Defaults) with { Leave = leave, Arrive = arrive };
        var changed = pointer with { Motion = motion };
        Defaults = Defaults.Follow(changed);
        bool starting = !IsTimed;
        Commit(Lengthened(layer.Replace(changed)));
        if (starting && !_editTab)
            SetTab(true);
    }

    /// <summary>
    /// What a right click on the picture offers (asked by the author): for a moving pointer, a step added on its way or
    /// its way taken on to there, a step removed, its way smoothed, curved or straight; for a still one, to move it;
    /// and to delete what is under the click.
    /// </summary>
    private List<(string Key, string Glyph, Action Act)> PictureMenu(SKPoint point)
    {
        var items = new List<(string, string, Action)>();
        if (!_editTab || _capture is null || _drag is not null || _export is not null)
            return items;
        var layer = _history.Current;
        float tolerance = Tolerance(7);
        Annotation? target = null;
        int stop = -1;
        bool atEnd = false;
        (int Leg, float U, float Distance)? onWay = null;
        // The moving pointer chosen: its stops, its end and its way are under the click too.
        if (Selected is { Kind: AnnotationKind.Pointer, Motion: { } motion } chosen && IsShownNow(chosen))
        {
            for (int i = 0; i < motion.Stops.Count && stop < 0; i++)
            {
                if (AnnotationGeometry.Distance(motion.Stops[i].At, point) <= tolerance)
                    stop = i;
            }
            atEnd = stop < 0 && AnnotationGeometry.Distance(chosen.Start, point) <= tolerance;
            var near = VideoTiming.NearestOnPath(chosen, point);
            if (stop < 0 && !atEnd && near.Distance <= tolerance && near.U is > 0.02f and < 0.98f)
                onWay = near;
            if (stop >= 0 || atEnd || onWay is not null)
                target = chosen;
        }
        var hit = target ?? HitAt(layer, point, tolerance);
        bool elsewhere = hit is null;
        target = hit ?? (IsTimed && Selected is { Kind: AnnotationKind.Pointer, Motion: not null } moving && IsShownNow(moving) ? moving : null);
        if (target is null)
            return items;
        var pointer = target;
        if (pointer.Kind == AnnotationKind.Pointer)
        {
            if (pointer.Motion is not { } way)
                items.Add(("MenuMovePointer", "", () => MakeMove(pointer)));
            else
            {
                // A click there, or none (asked by the author: chosen step by step).
                if ((stop >= 0 || atEnd) && way.Click != ClickEffect.None)
                {
                    int at = stop >= 0 ? stop : way.Stops.Count;
                    bool clicks = way.ClicksAt(1 + 2 * at);
                    // A box ticked or empty: the menu's own check mark did not show (the author).
                    items.Add(("MenuClickHere", clicks ? "" : "", () => Commit(layer.Replace(VideoTiming.WithClickAt(pointer, at, !clicks)))));
                }
                if (stop >= 0)
                    items.Add(("MenuRemoveStop", "", () => Commit(layer.Replace(VideoTiming.WithoutStop(pointer, stop)))));
                else if (onWay is { } at)
                    items.Add(("MenuAddStop", "", () => CommitIfFits(Fitted(layer, VideoTiming.WithStopOn(pointer, at.Leg, at.U)))));
                else if (elsewhere)
                    items.Add(("MenuContinueWay", "", () => CommitIfFits(WithWayTo(layer, pointer, point))));
                if (way.Stops.Count > 0 && stop < 0)
                    items.Add(("MenuRemoveLastStop", "", () => Commit(layer.Replace(VideoTiming.WithoutLastStop(pointer)))));
                items.Add(("MenuSmoothWay", "", () => Commit(layer.Replace(VideoTiming.Smoothed(pointer)))));
                items.Add((way.Curved ? "MenuWayStraight" : "MenuWayCurved", way.Curved ? "" : "",
                    () => Commit(layer.Replace(pointer with { Motion = way with { Curved = !way.Curved } }))));
            }
        }
        if (!elsewhere)
            items.Add(("MenuDelete", "", () => Commit(layer.Remove(pointer.Id))));
        if (pointer.Id != _selectedId)
            Select(pointer.Id);
        return items;
    }

    private void CommitIfFits(AnnotationLayer? layer)
    {
        if (layer is not null)
            Commit(layer);
    }

    private void Stage_MouseRightButtonUp(object sender, MouseButtonEventArgs e)
    {
        var items = PictureMenu(ToCapture(e.GetPosition(Stage)));
        if (items.Count == 0)
            return;
        var menu = new ContextMenu { PlacementTarget = Stage, Placement = PlacementMode.MousePoint };
        foreach (var (key, glyph, act) in items)
        {
            if (key == "MenuDelete" && menu.Items.Count > 0)
                menu.Items.Add(new Separator());
            var item = new MenuItem
            {
                Header = Strings.Get(key),
                Icon = new TextBlock { Text = glyph, FontFamily = new FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets"), FontSize = 14 },
            };
            item.Click += (_, _) => act();
            menu.Items.Add(item);
        }
        menu.IsOpen = true;
        e.Handled = true;
    }

    // ---------------------------------------------------------------- More room for the tracks

    private void TracksHandle_DragDelta(object sender, DragDeltaEventArgs e)
    {
        // Up gives the tracks room, down takes it back; the picture keeps enough to be seen.
        double room = Math.Max(0, PreviewHost.ActualHeight - MinPreviewHeight);
        _tracksExtra = Math.Clamp(_tracksExtra - e.VerticalChange, 0, _tracksExtra + room);
        ShowTracks();
    }

    private void TracksHandle_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        _tracksExtra = 0;
        ShowTracks();
    }

    // A smaller window: the room given to the tracks shrinks first, so that the picture stays seen.
    private void KeepPreviewSeen()
    {
        if (_tracksExtra <= 0 || PreviewHost.ActualHeight >= MinPreviewHeight)
            return;
        _tracksExtra = Math.Max(0, _tracksExtra - (MinPreviewHeight - PreviewHost.ActualHeight));
        ShowTracks();
    }
}
