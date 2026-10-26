using SkiaSharp;

namespace ScreenClip.Core.Annotations;

/// <summary>A point of an annotation that can be dragged to reshape it.</summary>
public enum HandleKind
{
    Start,
    End,
    Bend,
    TopLeft,
    TopRight,
    BottomLeft,
    BottomRight,
    /// <summary>An A → B pointer: where it leaves from, the two handles that bend its way, where it arrives.</summary>
    MotionFrom,
    MotionControl1,
    MotionControl2,
    MotionTo,
    /// <summary>A stop on its way (its index among the stops).</summary>
    MotionStop,
}

/// <summary>
/// Sizes, outlines and hit tests of the annotations, in the capture's pixels. The sizes follow the capture as the
/// layout does (<see cref="ScaleOf"/>): an arrow on a 4K capture is as thick, to the eye, as on a small one.
/// </summary>
public static class AnnotationGeometry
{
    /// <summary>The pointers, drawn in a 24 × 24 box (the same drawings as the mock-up), with their tip.</summary>
    private static readonly Dictionary<PointerStyle, (string Path, SKPoint Tip, bool Dark)> Pointers = new()
    {
        [PointerStyle.Windows] = ("M4 2.5v16.2l4.1-3.9 2.7 6.1 2.9-1.3-2.7-5.9h5.8z", new(4, 2.5f), false),
        [PointerStyle.Mac] = ("M4.5 2.5v16.5l4.2-4.1 2.8 6.3 3-1.3-2.7-6.1h6z", new(4.5f, 2.5f), true),
        [PointerStyle.Hand] = ("M9.2 3.8a1.6 1.6 0 0 1 3.2 0v6.4l.6-.1V8.9a1.6 1.6 0 0 1 3.2 0v1.6l.5.1v-.6a1.6 1.6 0 0 1 3.1.4V16c0 3.6-2.3 6.2-5.8 6.2h-1.4c-2 0-3.4-.8-4.6-2.4l-3.6-4.9a1.7 1.7 0 0 1 2.6-2.2l2.2 2.2z", new(10.8f, 2.2f), false),
    };

    /// <summary>How much the strokes, texts and pointers grow with the capture: 1 up to 1600 px, at most 3.</summary>
    public static float ScaleOf(int captureWidth, int captureHeight) =>
        (float)Math.Clamp(Math.Max(captureWidth, captureHeight) / 1600.0, 1, 3);

    public static float StrokeWidth(Annotation annotation, float scale) => annotation.Kind == AnnotationKind.Marker
        ? annotation.Size switch { StrokeSize.Small => 14, StrokeSize.Large => 34, _ => 22 } * scale
        : annotation.Size switch { StrokeSize.Small => 3, StrokeSize.Large => 8, _ => 5 } * scale;

    public static float FontSize(Annotation annotation, float scale) =>
        annotation.Size switch { StrokeSize.Small => 18, StrokeSize.Large => 38, _ => 26 } * scale;

    public static float NumberDiameter(Annotation annotation, float scale) =>
        annotation.Size switch { StrokeSize.Small => 28, StrokeSize.Large => 48, _ => 36 } * scale;

    /// <summary>Corner radius of a rectangle annotation.</summary>
    public static float CornerRadius(float scale) => 4 * scale;

    public static SKRect RectOf(SKPoint a, SKPoint b) =>
        new(Math.Min(a.X, b.X), Math.Min(a.Y, b.Y), Math.Max(a.X, b.X), Math.Max(a.Y, b.Y));

    // ---------------------------------------------------------------- Arrows

    /// <summary>The control point of a curved arrow: off its middle, to one side, by <see cref="Annotation.Bend"/>.</summary>
    public static SKPoint Control(Annotation arrow)
    {
        var (a, b) = (arrow.Start, arrow.End);
        float dx = b.X - a.X, dy = b.Y - a.Y;
        return new SKPoint((a.X + b.X) / 2 - dy * arrow.Bend, (a.Y + b.Y) / 2 + dx * arrow.Bend);
    }

    public static bool IsCurved(Annotation annotation) => annotation.Kind == AnnotationKind.Arrow && annotation.Arrow == ArrowStyle.Curved;

    /// <summary>The middle of a curved arrow (a quadratic curve: a quarter of the way to its control point and back).</summary>
    public static SKPoint CurveMiddle(Annotation arrow)
    {
        var c = Control(arrow);
        return new SKPoint((arrow.Start.X + 2 * c.X + arrow.End.X) / 4, (arrow.Start.Y + 2 * c.Y + arrow.End.Y) / 4);
    }

    /// <summary>Length of the head of an arrow: with its thickness, never more than half the arrow.</summary>
    public static float HeadLength(Annotation arrow, float scale)
    {
        float length = Distance(arrow.Start, arrow.End);
        return Math.Min(StrokeWidth(arrow, scale) * 3.2f + 9 * scale, length * 0.5f);
    }

    // ---------------------------------------------------------------- Texts

    /// <summary>The lines of a text (at least one, for the caret of an empty text).</summary>
    public static string[] Lines(string text) => text.Replace("\r\n", "\n").Split('\n');

    /// <summary>Space between the text and the edge of its label.</summary>
    public static (float X, float Y) TextPadding(float fontSize) => (fontSize * 0.45f, fontSize * 0.22f);

    public static float LineHeight(float fontSize) => fontSize * 1.3f;

    /// <summary>The box of a text annotation (its label), from its top left corner; the bubble's tail is under it.</summary>
    public static SKRect TextBox(Annotation text, float scale)
    {
        float size = FontSize(text, scale);
        using var font = new SKFont(Typefaces.SemiBold, size);
        var lines = Lines(text.Text);
        float width = Math.Max(size * 0.5f, lines.Max(line => font.MeasureText(line)));
        var (padX, padY) = TextPadding(size);
        return SKRect.Create(text.Start.X, text.Start.Y, width + 2 * padX, lines.Length * LineHeight(size) + 2 * padY);
    }

    /// <summary>The tail of a speech bubble, under the left of its box.</summary>
    public static SKPoint[] BubbleTail(SKRect box, float fontSize) =>
    [
        new(box.Left + fontSize * 0.55f, box.Bottom - 1),
        new(box.Left + fontSize * 1.35f, box.Bottom - 1),
        new(box.Left + fontSize * 0.25f, box.Bottom + fontSize * 0.75f),
    ];

    // ---------------------------------------------------------------- Pointers

    /// <summary>The drawing of a pointer, scaled and placed with its tip on the annotation's point.</summary>
    public static SKPath PointerPath(Annotation pointer, float scale, out bool dark) =>
        PointerPath(pointer.Pointer, pointer.Start, PointerHeight(pointer, scale), out dark);

    /// <summary>The drawing of a pointer <paramref name="height"/> pixels high, its tip at <paramref name="at"/>.</summary>
    public static SKPath PointerPath(PointerStyle style, SKPoint at, float height, out bool dark)
    {
        var (data, tip, isDark) = Pointers[Enum.IsDefined(style) ? style : PointerStyle.Windows];
        dark = isDark;
        var path = SKPath.ParseSvgPathData(data);
        float k = height / 24;
        path.Transform(SKMatrix.CreateScaleTranslation(k, k, at.X - tip.X * k, at.Y - tip.Y * k));
        return path;
    }

    public static float PointerHeight(Annotation pointer, float scale) =>
        Math.Clamp(pointer.PointerSize, AnnotationDefaults.MinPointerSize, AnnotationDefaults.MaxPointerSize) * scale;

    public static float HaloRadius(Annotation pointer, float scale) => PointerHeight(pointer, scale) * 0.42f;

    // ---------------------------------------------------------------- Bounds and hits

    /// <summary>What the annotation covers, its stroke included.</summary>
    public static SKRect Bounds(Annotation annotation, float scale)
    {
        float half = StrokeWidth(annotation, scale) / 2;
        switch (annotation.Kind)
        {
            case AnnotationKind.Text:
            {
                var box = TextBox(annotation, scale);
                if (annotation.TextStyle == TextStyle.Bubble)
                    box.Bottom += FontSize(annotation, scale) * 0.75f;
                return box;
            }
            case AnnotationKind.Number:
            {
                float r = NumberDiameter(annotation, scale) / 2;
                return new SKRect(annotation.Start.X - r, annotation.Start.Y - r, annotation.Start.X + r, annotation.Start.Y + r);
            }
            case AnnotationKind.Pointer:
            {
                using var path = PointerPath(annotation, scale, out _);
                var bounds = path.Bounds;
                if (annotation.Halo && annotation.Motion is null)
                {
                    float r = HaloRadius(annotation, scale);
                    bounds.Union(new SKRect(annotation.Start.X - r, annotation.Start.Y - r, annotation.Start.X + r, annotation.Start.Y + r));
                }
                return bounds;
            }
            case AnnotationKind.Rectangle or AnnotationKind.Ellipse or AnnotationKind.Mask:
            {
                var rect = annotation.Rect;
                if (annotation.Kind != AnnotationKind.Mask)
                    rect.Inflate(half, half);
                return rect;
            }
            case AnnotationKind.Arrow:
            {
                var rect = RectOf(annotation.Start, annotation.End);
                if (IsCurved(annotation))
                {
                    var middle = CurveMiddle(annotation);
                    rect.Union(new SKRect(middle.X, middle.Y, middle.X, middle.Y));
                }
                float head = HeadLength(annotation, scale) * 0.7f;
                rect.Inflate(Math.Max(half, head), Math.Max(half, head));
                return rect;
            }
            default:
            {
                var points = annotation.Points;
                if (points.Length == 0)
                    return SKRect.Empty;
                var rect = new SKRect(points.Min(p => p.X), points.Min(p => p.Y), points.Max(p => p.X), points.Max(p => p.Y));
                rect.Inflate(half, half);
                return rect;
            }
        }
    }

    /// <summary>Whether <paramref name="point"/> is on the annotation, within <paramref name="tolerance"/> pixels.</summary>
    public static bool HitTest(Annotation annotation, SKPoint point, float scale, float tolerance)
    {
        float reach = StrokeWidth(annotation, scale) / 2 + tolerance;
        switch (annotation.Kind)
        {
            case AnnotationKind.Rectangle when annotation.Shape == ShapeStyle.Outline:
            {
                var outer = annotation.Rect;
                outer.Inflate(reach, reach);
                var inner = annotation.Rect;
                inner.Inflate(-reach, -reach);
                return outer.Contains(point) && !(inner.Width > 0 && inner.Height > 0 && inner.Contains(point));
            }
            case AnnotationKind.Ellipse:
            {
                var rect = annotation.Rect;
                float rx = Math.Max(rect.Width / 2, 0.5f), ry = Math.Max(rect.Height / 2, 0.5f);
                float nx = (point.X - rect.MidX) / rx, ny = (point.Y - rect.MidY) / ry;
                float distance = MathF.Sqrt(nx * nx + ny * ny);
                return annotation.Shape == ShapeStyle.Outline
                    ? Math.Abs(distance - 1) * Math.Min(rx, ry) <= reach
                    : distance <= 1 + reach / Math.Min(rx, ry);
            }
            case AnnotationKind.Rectangle or AnnotationKind.Mask:
            {
                var rect = annotation.Rect;
                rect.Inflate(tolerance, tolerance);
                return rect.Contains(point);
            }
            case AnnotationKind.Arrow or AnnotationKind.Line:
                return DistanceToPolyline(point, Polyline(annotation)) <= reach;
            case AnnotationKind.Pen or AnnotationKind.Marker:
                return annotation.Points.Length == 1
                    ? Distance(point, annotation.Start) <= reach
                    : DistanceToPolyline(point, annotation.Points) <= reach;
            case AnnotationKind.Number:
                return Distance(point, annotation.Start) <= NumberDiameter(annotation, scale) / 2 + tolerance;
            default:
            {
                var bounds = Bounds(annotation, scale);
                bounds.Inflate(tolerance, tolerance);
                return bounds.Contains(point);
            }
        }
    }

    /// <summary>An arrow or a line as segments (a curved arrow in 24 of them).</summary>
    public static SKPoint[] Polyline(Annotation annotation)
    {
        if (!IsCurved(annotation))
            return [annotation.Start, annotation.End];
        var (a, c, b) = (annotation.Start, Control(annotation), annotation.End);
        const int Steps = 24;
        var points = new SKPoint[Steps + 1];
        for (int i = 0; i <= Steps; i++)
        {
            float t = (float)i / Steps, u = 1 - t;
            points[i] = new SKPoint(u * u * a.X + 2 * u * t * c.X + t * t * b.X, u * u * a.Y + 2 * u * t * c.Y + t * t * b.Y);
        }
        return points;
    }

    public static float Distance(SKPoint a, SKPoint b) => MathF.Sqrt((a.X - b.X) * (a.X - b.X) + (a.Y - b.Y) * (a.Y - b.Y));

    public static float DistanceToPolyline(SKPoint point, IReadOnlyList<SKPoint> points)
    {
        float best = float.MaxValue;
        for (int i = 1; i < points.Count; i++)
            best = Math.Min(best, DistanceToSegment(point, points[i - 1], points[i]));
        return points.Count == 1 ? Distance(point, points[0]) : best;
    }

    public static float DistanceToSegment(SKPoint p, SKPoint a, SKPoint b)
    {
        float dx = b.X - a.X, dy = b.Y - a.Y, lengthSquared = dx * dx + dy * dy;
        if (lengthSquared == 0)
            return Distance(p, a);
        float t = Math.Clamp(((p.X - a.X) * dx + (p.Y - a.Y) * dy) / lengthSquared, 0, 1);
        return Distance(p, new SKPoint(a.X + t * dx, a.Y + t * dy));
    }

    // ---------------------------------------------------------------- Handles

    /// <summary>
    /// The points that reshape the annotation, none for those that are only moved; <c>Index</c> tells which part of a
    /// pointer's way a handle bends, or which stop it is.
    /// </summary>
    public static (HandleKind Kind, SKPoint Point, int Index)[] Handles(Annotation annotation)
    {
        switch (annotation.Kind)
        {
            case AnnotationKind.Arrow or AnnotationKind.Line:
                return IsCurved(annotation)
                    ? [(HandleKind.Start, annotation.Start, 0), (HandleKind.Bend, CurveMiddle(annotation), 0), (HandleKind.End, annotation.End, 0)]
                    : [(HandleKind.Start, annotation.Start, 0), (HandleKind.End, annotation.End, 0)];
            case AnnotationKind.Pointer when annotation.Motion is { } motion:
            {
                var handles = new List<(HandleKind, SKPoint, int)> { (HandleKind.MotionFrom, motion.From, 0) };
                var legs = motion.Legs(annotation.Start);
                for (int i = 0; i < legs.Length && motion.Curved; i++)
                {
                    handles.Add((HandleKind.MotionControl1, legs[i].Control1, i));
                    handles.Add((HandleKind.MotionControl2, legs[i].Control2, i));
                }
                for (int i = 0; i < motion.Stops.Count; i++)
                    handles.Add((HandleKind.MotionStop, motion.Stops[i].At, i));
                handles.Add((HandleKind.MotionTo, annotation.Start, 0));
                return [.. handles];
            }
            case AnnotationKind.Rectangle or AnnotationKind.Ellipse or AnnotationKind.Mask:
                var r = annotation.Rect;
                return
                [
                    (HandleKind.TopLeft, new SKPoint(r.Left, r.Top), 0), (HandleKind.TopRight, new SKPoint(r.Right, r.Top), 0),
                    (HandleKind.BottomLeft, new SKPoint(r.Left, r.Bottom), 0), (HandleKind.BottomRight, new SKPoint(r.Right, r.Bottom), 0),
                ];
            default:
                return [];
        }
    }

    // A handle of part <paramref name="leg"/> of a way (its first, or its second), moved by (dx, dy); the handles of a
    // part after the first belong to the stop it leaves from.
    private static PointerMotion ShiftControl(PointerMotion motion, int leg, bool second, float dx, float dy)
    {
        SKPoint Shift(SKPoint p) => new(p.X + dx, p.Y + dy);
        if (leg == 0)
            return second ? motion with { Control2 = Shift(motion.Control2) } : motion with { Control1 = Shift(motion.Control1) };
        if (leg > motion.Stops.Count)
            return motion;
        var stop = motion.Stops[leg - 1];
        stop = second ? stop with { Control2 = Shift(stop.Control2) } : stop with { Control1 = Shift(stop.Control1) };
        return motion with { Stops = motion.Stops.SetItem(leg - 1, stop) };
    }

    /// <summary>The annotation with <paramref name="handle"/> dragged to <paramref name="point"/>; Shift keeps 45° steps or a square.</summary>
    public static Annotation WithHandle(Annotation annotation, HandleKind handle, SKPoint point, bool constrain, int index = 0)
    {
        // The handles of the parts of a way after its first one belong to the stop it leaves from.
        if (handle is HandleKind.MotionControl1 or HandleKind.MotionControl2 && index > 0 && annotation.Motion is { } way && index <= way.Stops.Count)
        {
            var stop = way.Stops[index - 1];
            stop = handle == HandleKind.MotionControl1 ? stop with { Control1 = point } : stop with { Control2 = point };
            return annotation with { Motion = way with { Stops = way.Stops.SetItem(index - 1, stop) } };
        }
        // A point of a way carries the handles on both sides of it, so that the curve keeps its shape there (asked
        // by the author).
        if (handle == HandleKind.MotionStop && annotation.Motion is { } stopping && index >= 0 && index < stopping.Stops.Count)
        {
            var at = stopping.Stops[index].At;
            float sx = point.X - at.X, sy = point.Y - at.Y;
            var moved = stopping with { Stops = stopping.Stops.SetItem(index, stopping.Stops[index] with { At = point }) };
            moved = ShiftControl(ShiftControl(moved, index, true, sx, sy), index + 1, false, sx, sy);
            return annotation with { Motion = moved };
        }
        switch (handle)
        {
            case HandleKind.Start:
                return annotation with { Points = [Constrain(annotation.End, point, constrain), annotation.End] };
            case HandleKind.End:
                return annotation with { Points = [annotation.Start, Constrain(annotation.Start, point, constrain)] };
            case HandleKind.MotionFrom when annotation.Motion is { } motion:
                return annotation with { Motion = ShiftControl(motion with { From = point }, 0, false, point.X - motion.From.X, point.Y - motion.From.Y) };
            case HandleKind.MotionControl1 when annotation.Motion is { } motion:
                return annotation with { Motion = motion with { Control1 = point } };
            case HandleKind.MotionControl2 when annotation.Motion is { } motion:
                return annotation with { Motion = motion with { Control2 = point } };
            case HandleKind.MotionTo when annotation.Motion is { } ending:
                return annotation with
                {
                    Points = [point],
                    Motion = ShiftControl(ending, ending.Stops.Count, true, point.X - annotation.Start.X, point.Y - annotation.Start.Y),
                };
            case HandleKind.MotionTo:
                return annotation with { Points = [point] };
            case HandleKind.Bend:
            {
                // The middle of the curve is half way to the control point: the bend from where the handle is.
                var (a, b) = (annotation.Start, annotation.End);
                float dx = b.X - a.X, dy = b.Y - a.Y, lengthSquared = dx * dx + dy * dy;
                if (lengthSquared < 1)
                    return annotation;
                float offset = (point.X - (a.X + b.X) / 2) * -dy + (point.Y - (a.Y + b.Y) / 2) * dx;
                return annotation with { Bend = Math.Clamp(2 * offset / lengthSquared, -1, 1) };
            }
            default:
            {
                var r = annotation.Rect;
                var fixedCorner = handle switch
                {
                    HandleKind.TopLeft => new SKPoint(r.Right, r.Bottom),
                    HandleKind.TopRight => new SKPoint(r.Left, r.Bottom),
                    HandleKind.BottomLeft => new SKPoint(r.Right, r.Top),
                    _ => new SKPoint(r.Left, r.Top),
                };
                return annotation with { Points = [fixedCorner, constrain ? Square(fixedCorner, point) : point] };
            }
        }
    }

    /// <summary><paramref name="to"/> moved to the nearest 45° direction from <paramref name="from"/>, when asked.</summary>
    public static SKPoint Constrain(SKPoint from, SKPoint to, bool constrain)
    {
        if (!constrain)
            return to;
        float dx = to.X - from.X, dy = to.Y - from.Y;
        double angle = Math.Round(Math.Atan2(dy, dx) / (Math.PI / 4)) * (Math.PI / 4);
        // The drag projected on that direction: the point stays as near the pointer as it can.
        float along = (float)(dx * Math.Cos(angle) + dy * Math.Sin(angle));
        return new SKPoint(from.X + (float)Math.Cos(angle) * along, from.Y + (float)Math.Sin(angle) * along);
    }

    /// <summary>The corner opposite <paramref name="from"/> of a square, toward <paramref name="to"/>.</summary>
    public static SKPoint Square(SKPoint from, SKPoint to)
    {
        float dx = to.X - from.X, dy = to.Y - from.Y, side = Math.Max(Math.Abs(dx), Math.Abs(dy));
        return new SKPoint(from.X + (dx < 0 ? -side : side), from.Y + (dy < 0 ? -side : side));
    }
}
