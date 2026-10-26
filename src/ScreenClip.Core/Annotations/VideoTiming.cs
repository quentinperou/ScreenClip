using ScreenClip.Core.Video;
using SkiaSharp;

namespace ScreenClip.Core.Annotations;

/// <summary>A moment of a video, for the drawing: its time, and the pointer recorded with it (if any).</summary>
public sealed record VideoMoment(TimeSpan Time, PointerTrack? Track = null);

/// <summary>How long a click shows, after its effect.</summary>
public static class ClickTiming
{
    /// <summary>How long the button of an A → B pointer stays down when it arrives.</summary>
    public static readonly TimeSpan Press = TimeSpan.FromMilliseconds(150);

    /// <summary>Seconds: a halo fading once the button is up, a ring spreading, a pointer pressed coming back.</summary>
    public const double HaloFade = 0.45, RippleLength = 0.6, PressRelease = 0.12;

    public static bool IsActive(ClickEffect effect, ClickPhase phase) => effect switch
    {
        ClickEffect.Halo => phase.SinceUp is not { } up || up < HaloFade,
        ClickEffect.Ripple => phase.SinceDown < RippleLength,
        ClickEffect.Press => phase.SinceUp is not { } up || up < PressRelease,
        _ => false,
    };

    /// <summary>Where a click (button down then up) is in its effect at <paramref name="time"/>; null when it shows nothing.</summary>
    public static ClickPhase? PhaseAt(TimeSpan down, TimeSpan up, TimeSpan time, ClickEffect effect)
    {
        if (effect == ClickEffect.None || time < down)
            return null;
        var phase = new ClickPhase((time - down).TotalSeconds, time >= up ? (time - up).TotalSeconds : null);
        return IsActive(effect, phase) ? phase : null;
    }
}

/// <summary>
/// The annotations of a video in time: which show at a moment, where an A → B pointer is then, and the changes made
/// on the timeline (a bar moved or stretched, the times of a motion).
/// </summary>
public static class VideoTiming
{
    // ---------------------------------------------------------------- A pointer that moves

    /// <summary>Where a pointer is at <paramref name="time"/>: at its start before it leaves, at its point once arrived.</summary>
    public static SKPoint PositionAt(Annotation pointer, TimeSpan time)
    {
        if (pointer.Motion is not { } motion)
            return pointer.Start;
        double span = (motion.Arrive - motion.Leave).TotalSeconds;
        double u = span <= 0 ? (time >= motion.Arrive ? 1 : 0) : Math.Clamp((time - motion.Leave).TotalSeconds / span, 0, 1);
        if (motion.Eased)
            u = u < 0.5 ? 4 * u * u * u : 1 - Math.Pow(-2 * u + 2, 3) / 2;
        return PointOnPath(motion, pointer.Start, (float)u);
    }

    /// <summary>The point at <paramref name="u"/> (0 to 1) of the way from the motion's start to <paramref name="to"/>.</summary>
    public static SKPoint PointOnPath(PointerMotion motion, SKPoint to, float u)
    {
        var a = motion.From;
        if (!motion.Curved)
            return new SKPoint(a.X + (to.X - a.X) * u, a.Y + (to.Y - a.Y) * u);
        float m = 1 - u;
        var (c1, c2) = (motion.Control1, motion.Control2);
        return new SKPoint(
            m * m * m * a.X + 3 * m * m * u * c1.X + 3 * m * u * u * c2.X + u * u * u * to.X,
            m * m * m * a.Y + 3 * m * m * u * c1.Y + 3 * m * u * u * c2.Y + u * u * u * to.Y);
    }

    /// <summary>The way a pointer goes, as segments (the editor draws it while the pointer is chosen).</summary>
    public static SKPoint[] Path(Annotation pointer, int steps = 40)
    {
        if (pointer.Motion is not { } motion)
            return [pointer.Start];
        if (!motion.Curved)
            return [motion.From, pointer.Start];
        var points = new SKPoint[steps + 1];
        for (int i = 0; i <= steps; i++)
            points[i] = PointOnPath(motion, pointer.Start, (float)i / steps);
        return points;
    }

    /// <summary>
    /// A new A → B motion: the handles a third of the way, off to the same side (a gentle arc, as a hand draws), and a
    /// time to go that grows with the distance.
    /// </summary>
    public static PointerMotion NewMotion(SKPoint from, SKPoint to, TimeSpan leave, AnnotationDefaults defaults)
    {
        float dx = to.X - from.X, dy = to.Y - from.Y;
        float length = MathF.Sqrt(dx * dx + dy * dy);
        var side = new SKPoint(-dy * 0.18f, dx * 0.18f);
        return new PointerMotion
        {
            From = from,
            Control1 = new SKPoint(from.X + dx / 3 + side.X, from.Y + dy / 3 + side.Y),
            Control2 = new SKPoint(from.X + 2 * dx / 3 + side.X, from.Y + 2 * dy / 3 + side.Y),
            Curved = defaults.MotionCurved,
            Eased = defaults.MotionEased,
            Click = defaults.MotionClick,
            Leave = leave,
            Arrive = leave + TimeSpan.FromSeconds(Math.Clamp(length / 700.0, 0.6, 2.0)),
        };
    }

    // ---------------------------------------------------------------- A moment

    /// <summary>The annotation as it is at <paramref name="time"/>: a moving pointer where it is then, with its click.</summary>
    public static Annotation Posed(Annotation annotation, TimeSpan time)
    {
        if (annotation.Motion is not { } motion)
            return annotation;
        return annotation with
        {
            Points = [PositionAt(annotation, time)],
            Click = ClickTiming.PhaseAt(motion.Arrive, motion.Arrive + ClickTiming.Press, time, motion.Click),
        };
    }

    /// <summary>The annotations shown at <paramref name="time"/>, in drawing order, posed.</summary>
    public static IReadOnlyList<Annotation> ShownAt(this AnnotationLayer layer, TimeSpan time) =>
        [.. layer.Items.Where(a => a.IsShownAt(time)).Select(a => Posed(a, time))];

    /// <summary>The annotation on top under <paramref name="point"/> at <paramref name="time"/> (as it is in the layer).</summary>
    public static Annotation? HitTestAt(this AnnotationLayer layer, SKPoint point, float scale, float tolerance, TimeSpan time)
    {
        var shown = layer.ShownAt(time);
        for (int i = shown.Count - 1; i >= 0; i--)
        {
            if (AnnotationGeometry.HitTest(shown[i], point, scale, tolerance))
                return layer.Find(shown[i].Id);
        }
        return null;
    }

    /// <summary>The clicks of the recorded pointer that show at <paramref name="time"/>: where, and how far in their effect.</summary>
    public static List<(SKPoint At, ClickPhase Phase)> RecordedClicks(PointerTrack track, RecordedPointerOptions options, TimeSpan time)
    {
        var shown = new List<(SKPoint, ClickPhase)>();
        foreach (var click in track.Clicks)
        {
            if (click.Down > time)
                break;
            if (ClickTiming.PhaseAt(click.Down, click.Up, time, options.Click) is { } phase)
                shown.Add((track.At(click.Down, options.Smooth).Position, phase));
        }
        return shown;
    }

    /// <summary>
    /// What changes from one moment to another in the drawing (annotations shown, moving pointers, the recorded pointer
    /// and its clicks): two moments with the same value draw the same overlay, so an export draws a frame only when the
    /// video or this changes.
    /// </summary>
    public static int Fingerprint(AnnotationLayer layer, VideoMoment moment)
    {
        var hash = new HashCode();
        var time = moment.Time;
        foreach (var annotation in layer.Items)
        {
            if (!annotation.IsShownAt(time))
                continue;
            hash.Add(annotation.Id);
            if (annotation.Motion is not null)
            {
                var posed = Posed(annotation, time);
                hash.Add(Quarter(posed.Start.X));
                hash.Add(Quarter(posed.Start.Y));
                hash.Add(posed.Click is { } click ? (int)(click.SinceDown * 60) : -1);
            }
        }
        if (moment.Track is { } track && layer.Cursor.Shows)
        {
            var state = track.At(time, layer.Cursor.Smooth);
            hash.Add(Quarter(state.Position.X));
            hash.Add(Quarter(state.Position.Y));
            hash.Add(state.Shape);
            hash.Add(state.Pressed);
            foreach (var (_, phase) in RecordedClicks(track, layer.Cursor, time))
                hash.Add((int)(phase.SinceDown * 60));
        }
        return hash.ToHashCode();
    }

    private static int Quarter(float value) => (int)MathF.Round(value * 4);

    // ---------------------------------------------------------------- The timeline

    /// <summary>When the annotation shows in a video of <paramref name="duration"/> (all of it when not set).</summary>
    public static ShowTime TimeOf(Annotation annotation, TimeSpan duration) => annotation.Time ?? new ShowTime(TimeSpan.Zero, duration);

    /// <summary>Its bar moved by <paramref name="delta"/>, kept in the video; a motion goes with it.</summary>
    public static Annotation TimeMovedBy(Annotation annotation, TimeSpan delta, TimeSpan duration)
    {
        var shown = TimeOf(annotation, duration);
        if (delta < -shown.Start)
            delta = -shown.Start;
        if (delta > duration - shown.End)
            delta = duration - shown.End;
        return annotation with
        {
            Time = Kept(new ShowTime(shown.Start + delta, shown.End + delta), duration),
            Motion = annotation.Motion is { } m ? m with { Leave = m.Leave + delta, Arrive = m.Arrive + delta } : null,
        };
    }

    /// <summary>Its bar starting at <paramref name="start"/> (its end kept).</summary>
    public static Annotation WithShowStart(Annotation annotation, TimeSpan start, TimeSpan duration)
    {
        var shown = TimeOf(annotation, duration);
        var time = shown with { Start = Clamp(start, TimeSpan.Zero, shown.End - ShowTime.MinLength) };
        return KeysInside(annotation with { Time = Kept(time, duration) }, time);
    }

    /// <summary>Its bar ending at <paramref name="end"/> (its start kept).</summary>
    public static Annotation WithShowEnd(Annotation annotation, TimeSpan end, TimeSpan duration)
    {
        var shown = TimeOf(annotation, duration);
        var time = shown with { End = Clamp(end, shown.Start + ShowTime.MinLength, duration) };
        return KeysInside(annotation with { Time = Kept(time, duration) }, time);
    }

    /// <summary>The moment an A → B pointer leaves A, inside its bar and before it arrives.</summary>
    public static Annotation WithLeave(Annotation pointer, TimeSpan leave, TimeSpan duration)
    {
        if (pointer.Motion is not { } m)
            return pointer;
        var shown = TimeOf(pointer, duration);
        return pointer with { Motion = m with { Leave = Clamp(leave, shown.Start, m.Arrive - ShowTime.MinLength) } };
    }

    /// <summary>The moment an A → B pointer arrives at B, inside its bar and after it leaves.</summary>
    public static Annotation WithArrive(Annotation pointer, TimeSpan arrive, TimeSpan duration)
    {
        if (pointer.Motion is not { } m)
            return pointer;
        var shown = TimeOf(pointer, duration);
        return pointer with { Motion = m with { Arrive = Clamp(arrive, m.Leave + ShowTime.MinLength, shown.End) } };
    }

    // The times of a motion moved inside a bar made shorter.
    private static Annotation KeysInside(Annotation annotation, ShowTime shown)
    {
        if (annotation.Motion is not { } m)
            return annotation;
        var leave = Clamp(m.Leave, shown.Start, shown.End - ShowTime.MinLength);
        var arrive = Clamp(m.Arrive, leave + ShowTime.MinLength, shown.End);
        return annotation with { Motion = m with { Leave = leave, Arrive = arrive } };
    }

    // The whole video is no time at all: such an annotation follows the video when it is cut.
    private static ShowTime? Kept(ShowTime time, TimeSpan duration) =>
        time.Start <= TimeSpan.Zero && time.End >= duration ? null : time;

    private static TimeSpan Clamp(TimeSpan value, TimeSpan low, TimeSpan high) =>
        high < low ? low : value < low ? low : value > high ? high : value;
}
