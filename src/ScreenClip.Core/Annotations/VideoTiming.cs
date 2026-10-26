using System.Collections.Immutable;
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

    /// <summary>How long a pointer waits at a stop of its way when one is added (its click shows meanwhile).</summary>
    public static readonly TimeSpan StopPause = TimeSpan.FromMilliseconds(500);

    /// <summary>Where a pointer is at <paramref name="time"/>: at its start before it leaves, at its point once arrived.</summary>
    public static SKPoint PositionAt(Annotation pointer, TimeSpan time)
    {
        if (pointer.Motion is not { } motion)
            return pointer.Start;
        foreach (var leg in motion.Legs(pointer.Start))
        {
            if (time < leg.Leave)
                return leg.From;
            if (time >= leg.Arrive)
                continue;
            double span = (leg.Arrive - leg.Leave).TotalSeconds;
            double u = span <= 0 ? 1 : Math.Clamp((time - leg.Leave).TotalSeconds / span, 0, 1);
            if (motion.Eased)
                u = u < 0.5 ? 4 * u * u * u : 1 - Math.Pow(-2 * u + 2, 3) / 2;
            return PointOnLeg(leg, motion.Curved, (float)u);
        }
        return pointer.Start;
    }

    /// <summary>The point at <paramref name="u"/> (0 to 1) of the way from the motion's start to <paramref name="to"/> (its first part).</summary>
    public static SKPoint PointOnPath(PointerMotion motion, SKPoint to, float u) =>
        PointOnLeg(motion.Legs(to)[0], motion.Curved, u);

    /// <summary>The point at <paramref name="u"/> (0 to 1) of a part of the way.</summary>
    public static SKPoint PointOnLeg(PointerLeg leg, bool curved, float u)
    {
        var (a, to) = (leg.From, leg.To);
        if (!curved)
            return new SKPoint(a.X + (to.X - a.X) * u, a.Y + (to.Y - a.Y) * u);
        float m = 1 - u;
        var (c1, c2) = (leg.Control1, leg.Control2);
        return new SKPoint(
            m * m * m * a.X + 3 * m * m * u * c1.X + 3 * m * u * u * c2.X + u * u * u * to.X,
            m * m * m * a.Y + 3 * m * m * u * c1.Y + 3 * m * u * u * c2.Y + u * u * u * to.Y);
    }

    /// <summary>The way a pointer goes, as segments (the editor draws it while the pointer is chosen).</summary>
    public static SKPoint[] Path(Annotation pointer, int steps = 40)
    {
        if (pointer.Motion is not { } motion)
            return [pointer.Start];
        var points = new List<SKPoint> { motion.From };
        foreach (var leg in motion.Legs(pointer.Start))
        {
            if (!motion.Curved)
            {
                points.Add(leg.To);
                continue;
            }
            for (int i = 1; i <= steps; i++)
                points.Add(PointOnLeg(leg, true, (float)i / steps));
        }
        return [.. points];
    }

    /// <summary>
    /// A new A → B motion: the handles a third of the way, off to the same side (a gentle arc, as a hand draws), and a
    /// time to go that grows with the distance.
    /// </summary>
    public static PointerMotion NewMotion(SKPoint from, SKPoint to, TimeSpan leave, AnnotationDefaults defaults)
    {
        var (control1, control2) = ArcHandles(from, to);
        return new PointerMotion
        {
            From = from,
            Control1 = control1,
            Control2 = control2,
            Curved = defaults.MotionCurved,
            Eased = defaults.MotionEased,
            Click = defaults.MotionClick,
            Leave = leave,
            Arrive = leave + TravelTime(from, to),
        };
    }

    // The handles a third of the way, off to the same side: a gentle arc.
    private static (SKPoint, SKPoint) ArcHandles(SKPoint from, SKPoint to)
    {
        float dx = to.X - from.X, dy = to.Y - from.Y;
        var side = new SKPoint(-dy * 0.18f, dx * 0.18f);
        return (new SKPoint(from.X + dx / 3 + side.X, from.Y + dy / 3 + side.Y), new SKPoint(from.X + 2 * dx / 3 + side.X, from.Y + 2 * dy / 3 + side.Y));
    }

    // A time to go that grows with the distance.
    private static TimeSpan TravelTime(SKPoint from, SKPoint to)
    {
        float dx = to.X - from.X, dy = to.Y - from.Y;
        return TimeSpan.FromSeconds(Math.Clamp(MathF.Sqrt(dx * dx + dy * dy) / 700.0, 0.6, 2.0));
    }

    /// <summary>
    /// The way goes on to <paramref name="next"/> (A → B → C…): where the pointer arrived becomes a stop, where it clicks
    /// and waits a moment, then it goes there, as long as the distance says. Its bar grows to hold it.
    /// </summary>
    public static Annotation WithNextStop(Annotation pointer, SKPoint next)
    {
        if (pointer.Motion is not { } motion)
            return pointer;
        var (control1, control2) = ArcHandles(pointer.Start, next);
        var leave = motion.Arrive + StopPause;
        // Where it arrived keeps its click (or its none); the new end clicks.
        var stop = new PointerStop { At = pointer.Start, Arrive = motion.Arrive, Leave = leave, Control1 = control1, Control2 = control2, Clicks = motion.ClicksAtEnd };
        var arrive = leave + TravelTime(pointer.Start, next);
        var changed = pointer with { Points = [next], Motion = motion with { Stops = motion.Stops.Add(stop), Arrive = arrive, ClicksAtEnd = true } };
        if (pointer.Time is { } time && time.End < arrive + StopPause)
            changed = changed with { Time = time with { End = arrive + StopPause } };
        return changed;
    }

    /// <summary>The last stop taken away: the pointer ends its way there again.</summary>
    public static Annotation WithoutLastStop(Annotation pointer)
    {
        if (pointer.Motion is not { Stops.Count: > 0 } motion)
            return pointer;
        var last = motion.Stops[^1];
        return pointer with { Points = [last.At], Motion = motion with { Stops = motion.Stops.RemoveAt(motion.Stops.Count - 1), Arrive = last.Arrive, ClicksAtEnd = last.Clicks } };
    }

    /// <summary>The point of a pointer's way nearest <paramref name="point"/>: which part, where on it (0 to 1), how far.</summary>
    public static (int Leg, float U, float Distance) NearestOnPath(Annotation pointer, SKPoint point, int steps = 60)
    {
        if (pointer.Motion is not { } motion)
            return (0, 0, AnnotationGeometry.Distance(pointer.Start, point));
        var best = (Leg: 0, U: 0f, Distance: float.MaxValue);
        var legs = motion.Legs(pointer.Start);
        for (int leg = 0; leg < legs.Length; leg++)
        {
            for (int i = 0; i <= steps; i++)
            {
                float u = (float)i / steps;
                float distance = AnnotationGeometry.Distance(PointOnLeg(legs[leg], motion.Curved, u), point);
                if (distance < best.Distance)
                    best = (leg, u, distance);
            }
        }
        return best;
    }

    /// <summary>
    /// A stop put on the way at <paramref name="u"/> of its part <paramref name="leg"/>, the way's shape kept (the curve
    /// is cut in two there): it clicks and waits a moment, and what comes after is later by that much.
    /// </summary>
    public static Annotation WithStopOn(Annotation pointer, int leg, float u)
    {
        if (pointer.Motion is not { } motion || leg < 0 || leg > motion.Stops.Count)
            return pointer;
        var part = motion.Legs(pointer.Start)[leg];
        u = Math.Clamp(u, 0.05f, 0.95f);
        // De Casteljau: the two halves of the curve, each with its own handles.
        var p01 = Lerp(part.From, part.Control1, u);
        var p12 = Lerp(part.Control1, part.Control2, u);
        var p23 = Lerp(part.Control2, part.To, u);
        var p012 = Lerp(p01, p12, u);
        var p123 = Lerp(p12, p23, u);
        var at = motion.Curved ? Lerp(p012, p123, u) : Lerp(part.From, part.To, u);
        var time = part.Leave + (part.Arrive - part.Leave) * u;
        var stop = new PointerStop { At = at, Arrive = time, Leave = time + StopPause, Control1 = p123, Control2 = p23 };
        var stops = motion.Stops.Select((s, i) => i < leg ? s : s with { Arrive = s.Arrive + StopPause, Leave = s.Leave + StopPause }).ToImmutableList();
        var changed = motion with { Arrive = motion.Arrive + StopPause };
        // The first half's handles belong to where the part leaves from.
        if (leg == 0)
            changed = changed with { Control1 = p01, Control2 = p012 };
        else
            stops = stops.SetItem(leg - 1, stops[leg - 1] with { Control1 = p01, Control2 = p012 });
        var result = pointer with { Motion = changed with { Stops = stops.Insert(leg, stop) } };
        if (pointer.Time is { } shown && shown.End < result.Motion.Arrive + StopPause)
            result = result with { Time = shown with { End = result.Motion.Arrive + StopPause } };
        return result;
    }

    /// <summary>A click, or none, when the pointer arrives at stop <paramref name="stop"/> (its end: the number of stops).</summary>
    public static Annotation WithClickAt(Annotation pointer, int stop, bool clicks)
    {
        if (pointer.Motion is not { } motion || stop < 0 || stop > motion.Stops.Count)
            return pointer;
        return pointer with
        {
            Motion = stop == motion.Stops.Count
                ? motion with { ClicksAtEnd = clicks }
                : motion with { Stops = motion.Stops.SetItem(stop, motion.Stops[stop] with { Clicks = clicks }) },
        };
    }

    /// <summary>The stop at <paramref name="index"/> taken away: the parts on both sides become one, its wait gone.</summary>
    public static Annotation WithoutStop(Annotation pointer, int index)
    {
        if (pointer.Motion is not { } motion || index < 0 || index >= motion.Stops.Count)
            return pointer;
        var legs = motion.Legs(pointer.Start);
        var removed = motion.Stops[index];
        var wait = removed.Leave - removed.Arrive;
        // What comes after it is sooner by its wait.
        var stops = motion.Stops.Select((s, i) => i <= index ? s : s with { Arrive = s.Arrive - wait, Leave = s.Leave - wait }).ToImmutableList();
        var changed = motion with { Arrive = motion.Arrive - wait };
        // The part made of both keeps the first one's start and the second one's end.
        if (index == 0)
            changed = changed with { Control2 = legs[1].Control2 };
        else
            stops = stops.SetItem(index - 1, stops[index - 1] with { Control2 = legs[index + 1].Control2 });
        return pointer with { Motion = changed with { Stops = stops.RemoveAt(index) } };
    }

    /// <summary>
    /// The way smoothed through its stops (asked by the author): the handles of each part set so that it flows on from
    /// one to the next (Catmull-Rom); a way with no stop gets a gentle arc.
    /// </summary>
    public static Annotation Smoothed(Annotation pointer)
    {
        if (pointer.Motion is not { } motion)
            return pointer;
        SKPoint[] points = [motion.From, .. motion.Stops.Select(s => s.At), pointer.Start];
        if (points.Length == 2)
        {
            var (arc1, arc2) = ArcHandles(points[0], points[1]);
            return pointer with { Motion = motion with { Curved = true, Control1 = arc1, Control2 = arc2 } };
        }
        var tangents = new SKPoint[points.Length];
        for (int i = 0; i < points.Length; i++)
        {
            var before = points[Math.Max(0, i - 1)];
            var after = points[Math.Min(points.Length - 1, i + 1)];
            float share = i == 0 || i == points.Length - 1 ? 1 : 0.5f;
            tangents[i] = new SKPoint((after.X - before.X) * share, (after.Y - before.Y) * share);
        }
        (SKPoint, SKPoint) Handles(int leg) => (
            new SKPoint(points[leg].X + tangents[leg].X / 3, points[leg].Y + tangents[leg].Y / 3),
            new SKPoint(points[leg + 1].X - tangents[leg + 1].X / 3, points[leg + 1].Y - tangents[leg + 1].Y / 3));
        var (first1, first2) = Handles(0);
        var stops = motion.Stops.Select((s, i) =>
        {
            var (c1, c2) = Handles(i + 1);
            return s with { Control1 = c1, Control2 = c2 };
        }).ToImmutableList();
        return pointer with { Motion = motion with { Curved = true, Control1 = first1, Control2 = first2, Stops = stops } };
    }

    private static SKPoint Lerp(SKPoint a, SKPoint b, float u) => new(a.X + (b.X - a.X) * u, a.Y + (b.Y - a.Y) * u);

    /// <summary>When the pointer's way ends, its bar's end included: the length a video or an animation needs for it.</summary>
    public static TimeSpan EndOf(Annotation annotation) =>
        annotation.Time is { } time ? time.End : annotation.Motion is { } motion ? motion.Arrive + StopPause : TimeSpan.Zero;

    // ---------------------------------------------------------------- A moment

    /// <summary>The annotation as it is at <paramref name="time"/>: a moving pointer where it is then, with its click.</summary>
    public static Annotation Posed(Annotation annotation, TimeSpan time)
    {
        if (annotation.Motion is not { } motion)
            return annotation;
        // The click of its last arrival that still shows (at a stop, or at its end).
        ClickPhase? click = null;
        var keys = motion.Keys();
        for (int i = keys.Length - 1; i > 0 && click is null; i -= 2)
        {
            if (motion.ClicksAt(i))
                click = ClickTiming.PhaseAt(keys[i], keys[i] + ClickTiming.Press, time, motion.Click);
        }
        return annotation with { Points = [PositionAt(annotation, time)], Click = click };
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
            Motion = annotation.Motion is { } m ? m.WithKeys([.. m.Keys().Select(k => k + delta)]) : null,
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
    public static Annotation WithLeave(Annotation pointer, TimeSpan leave, TimeSpan duration) => WithKey(pointer, 0, leave, duration);

    /// <summary>The moment an A → B pointer arrives at its last point, inside its bar and after it leaves its last stop.</summary>
    public static Annotation WithArrive(Annotation pointer, TimeSpan arrive, TimeSpan duration) =>
        pointer.Motion is { } m ? WithKey(pointer, m.Keys().Length - 1, arrive, duration) : pointer;

    /// <summary>
    /// One of the times of a moving pointer (<see cref="PointerMotion.Keys"/>: it leaves, arrives at a stop, leaves it…,
    /// arrives) set to <paramref name="time"/>, between the ones before and after it and inside its bar.
    /// </summary>
    public static Annotation WithKey(Annotation pointer, int index, TimeSpan time, TimeSpan duration)
    {
        if (pointer.Motion is not { } m)
            return pointer;
        var keys = m.Keys();
        if (index < 0 || index >= keys.Length)
            return pointer;
        var shown = TimeOf(pointer, duration);
        // A way takes a moment; a stop may be left at once.
        var low = index == 0 ? shown.Start : keys[index - 1] + Gap(index);
        var high = index == keys.Length - 1 ? shown.End : keys[index + 1] - Gap(index + 1);
        keys[index] = Clamp(time, low, high);
        return pointer with { Motion = m.WithKeys(keys) };
    }

    // The least time before key <paramref name="index"/>: a way takes a moment, a stop may be left at once.
    private static TimeSpan Gap(int index) => PointerMotion.IsArrival(index) ? ShowTime.MinLength : TimeSpan.Zero;

    // The times of a motion moved inside a bar made shorter, in their order.
    private static Annotation KeysInside(Annotation annotation, ShowTime shown)
    {
        if (annotation.Motion is not { } m)
            return annotation;
        var keys = m.Keys();
        // What the times after each one need at least, to fit before the bar's end.
        var after = new TimeSpan[keys.Length];
        for (int i = keys.Length - 2; i >= 0; i--)
            after[i] = after[i + 1] + Gap(i + 1);
        for (int i = 0; i < keys.Length; i++)
        {
            var low = i == 0 ? shown.Start : keys[i - 1] + Gap(i);
            keys[i] = Clamp(keys[i], low, shown.End - after[i]);
        }
        return annotation with { Motion = m.WithKeys(keys) };
    }

    // The whole video is no time at all: such an annotation follows the video when it is cut.
    private static ShowTime? Kept(ShowTime time, TimeSpan duration) =>
        time.Start <= TimeSpan.Zero && time.End >= duration ? null : time;

    private static TimeSpan Clamp(TimeSpan value, TimeSpan low, TimeSpan high) =>
        high < low ? low : value < low ? low : value > high ? high : value;
}
