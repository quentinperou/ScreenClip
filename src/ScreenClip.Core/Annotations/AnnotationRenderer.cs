using ScreenClip.Core.Video;
using SkiaSharp;

namespace ScreenClip.Core.Annotations;

/// <summary>
/// Draws the annotations on a capture. The same drawing makes the editor's view (the canvas scaled to the screen) and
/// the picture that the layout then frames (<see cref="Flatten"/>), so what is seen is what is exported.
/// </summary>
public static class AnnotationRenderer
{
    private static readonly SKColor HaloFill = new(0xFF, 0xC5, 0x3D, 0x61);
    private static readonly SKColor HaloStroke = new(0xF7, 0xA0, 0x15, 0xBF);
    private static readonly SKColor SpotlightShade = new(0, 0, 0, 0x8C);
    private static readonly SKColor SolidMask = new(0x1B, 0x1B, 0x1B);

    /// <summary>
    /// The capture with its annotations, cut to its crop: what the layout lays out. A layer with nothing on it gives a
    /// plain copy. A frame of a video is drawn at its <paramref name="moment"/>: the annotations shown then, and the
    /// pointer recorded with it.
    /// </summary>
    public static SKImage Flatten(SKImage capture, AnnotationLayer layer, VideoMoment? moment = null)
    {
        var area = layer.Visible(capture.Width, capture.Height);
        var info = new SKImageInfo(area.Width, area.Height, SKColorType.Bgra8888, SKAlphaType.Premul);
        using var surface = SKSurface.Create(info) ?? throw new OutOfMemoryException("The picture is too large.");
        var canvas = surface.Canvas;
        canvas.Clear(SKColors.Transparent);
        canvas.Translate(-area.X, -area.Y);
        canvas.DrawImage(capture, 0, 0);
        if (moment is null)
            Draw(canvas, capture, layer.Items, area);
        else
        {
            Draw(canvas, capture, layer.ShownAt(moment.Time), area);
            DrawRecordedPointer(canvas, capture, layer, moment);
        }
        return surface.Snapshot();
    }

    /// <summary>
    /// The pointer recorded with a video, at its moment, as the layer's options say: its real shape or a drawn one, its
    /// size, smoothed or not, and the clicks around then. Over the annotations, as the real one was over the screen.
    /// </summary>
    public static void DrawRecordedPointer(SKCanvas canvas, SKImage capture, AnnotationLayer layer, VideoMoment moment)
    {
        var options = layer.Cursor;
        if (moment.Track is not { } track || track.IsEmpty || !options.Shows)
            return;
        var state = track.At(moment.Time, options.Smooth);
        // The arrow fills about two thirds of its picture: the drawn pointers are that high.
        float size = options.Size / 100f;
        float height = track.HeightOf(state.Visible ? state.Shape : 0) * 0.66f * size;
        float scale = AnnotationGeometry.ScaleOf(capture.Width, capture.Height);
        float press = 1;
        foreach (var (at, phase) in VideoTiming.RecordedClicks(track, options, moment.Time))
        {
            DrawClick(canvas, at, options.Click, phase, ClickRadius(height), scale);
            if (options.Click == ClickEffect.Press)
                press = PressScale(phase);
        }
        if (!state.Visible)
            return;
        if (options.Style == RecordedPointerStyle.AsRecorded)
        {
            var shape = track.Shapes[state.Shape];
            if (track.ImageOf(state.Shape) is not { } image)
                return;
            float k = size * press;
            var rect = SKRect.Create(state.Position.X - shape.HotX * k, state.Position.Y - shape.HotY * k, shape.Width * k, shape.Height * k);
            canvas.DrawImage(image, rect, new SKSamplingOptions(SKCubicResampler.Mitchell));
            return;
        }
        var style = options.Style switch
        {
            RecordedPointerStyle.Mac => PointerStyle.Mac,
            RecordedPointerStyle.Hand => PointerStyle.Hand,
            _ => PointerStyle.Windows,
        };
        DrawPointerShape(canvas, style, state.Position, height * press);
    }

    // A click shows about as wide as the pointer is high.
    private static float ClickRadius(float pointerHeight) => Math.Max(10, pointerHeight * 0.75f);

    // A pointer pressed is a little smaller, and comes back as the button goes up.
    private static float PressScale(ClickPhase phase)
    {
        double back = phase.SinceUp is { } up ? Math.Clamp(up / ClickTiming.PressRelease, 0, 1) : 0;
        double down = Math.Clamp(phase.SinceDown / 0.06, 0, 1);
        return (float)(1 - 0.15 * down * (1 - back));
    }

    /// <summary>The effect of a click at <paramref name="at"/>, under the pointer.</summary>
    private static void DrawClick(SKCanvas canvas, SKPoint at, ClickEffect effect, ClickPhase phase, float radius, float scale)
    {
        switch (effect)
        {
            case ClickEffect.Halo:
            {
                // Grows in quickly, stays while the button is down, then fades.
                double grow = Math.Clamp(phase.SinceDown / 0.08, 0, 1);
                double fade = phase.SinceUp is { } up ? Math.Clamp(1 - up / ClickTiming.HaloFade, 0, 1) : 1;
                float r = radius * (float)(0.6 + 0.4 * grow);
                using var fill = new SKPaint { IsAntialias = true, Color = HaloFill.WithAlpha((byte)(HaloFill.Alpha * fade)) };
                canvas.DrawCircle(at, r, fill);
                using var edge = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 2 * scale, Color = HaloStroke.WithAlpha((byte)(HaloStroke.Alpha * fade)) };
                canvas.DrawCircle(at, r, edge);
                break;
            }
            case ClickEffect.Ripple:
            {
                // A ring that spreads and fades.
                double p = Math.Clamp(phase.SinceDown / ClickTiming.RippleLength, 0, 1);
                float r = radius * (float)(0.35 + 1.25 * p);
                using var ring = new SKPaint
                {
                    IsAntialias = true,
                    Style = SKPaintStyle.Stroke,
                    StrokeWidth = (float)(3.5 * scale * (1 - 0.6 * p)),
                    Color = HaloStroke.WithAlpha((byte)(0xFF * (1 - p))),
                };
                canvas.DrawCircle(at, r, ring);
                break;
            }
            case ClickEffect.Press:
            {
                double fade = phase.SinceUp is { } up ? Math.Clamp(1 - up / ClickTiming.PressRelease, 0, 1) : 1;
                using var dash = SKPathEffect.CreateDash([3 * scale, 3 * scale], 0);
                using var ring = new SKPaint
                {
                    IsAntialias = true,
                    Style = SKPaintStyle.Stroke,
                    StrokeWidth = 2 * scale,
                    Color = HaloStroke.WithAlpha((byte)(HaloStroke.Alpha * fade)),
                    PathEffect = dash,
                };
                canvas.DrawCircle(at, radius * 0.6f, ring);
                break;
            }
        }
    }

    /// <summary>
    /// Draws <paramref name="annotations"/> on <paramref name="canvas"/>, set to the capture's pixels (any scale and
    /// offset). The capture itself is drawn by the caller; a mask hides what is under it: the capture and the
    /// annotations before it in the list. <paramref name="area"/> is the part shown, darkened around the spotlights.
    /// </summary>
    public static void Draw(SKCanvas canvas, SKImage capture, IEnumerable<Annotation> annotations, PixelRect area)
    {
        float scale = AnnotationGeometry.ScaleOf(capture.Width, capture.Height);
        // What is hidden by its eye is not drawn, nor hidden by a mask over it.
        IReadOnlyList<Annotation> list = [.. annotations.Where(a => !a.Hidden)];

        // The spotlights first: everything but them is darkened, and the other annotations stay bright on top.
        using (var spots = new SKPath())
        {
            foreach (var a in list)
            {
                if (a.Shape != ShapeStyle.Spotlight || a.Kind is not (AnnotationKind.Rectangle or AnnotationKind.Ellipse))
                    continue;
                if (a.Kind == AnnotationKind.Ellipse)
                    spots.AddOval(a.Rect);
                else
                    spots.AddRoundRect(a.Rect, AnnotationGeometry.CornerRadius(scale), AnnotationGeometry.CornerRadius(scale));
            }
            if (!spots.IsEmpty)
            {
                // Rectangles and ovals are added clockwise: where they overlap, the clip takes their union.
                canvas.Save();
                canvas.ClipPath(spots, SKClipOperation.Difference, antialias: true);
                using var shade = new SKPaint { Color = SpotlightShade };
                canvas.DrawRect(SKRect.Create(area.X, area.Y, area.Width, area.Height), shade);
                canvas.Restore();
            }
        }

        for (int i = 0; i < list.Count; i++)
        {
            var a = list[i];
            switch (a.Kind)
            {
                case AnnotationKind.Arrow:
                    DrawArrow(canvas, a, scale);
                    break;
                case AnnotationKind.Line:
                    using (var paint = Stroke(a, scale))
                        canvas.DrawLine(a.Start, a.End, paint);
                    break;
                case AnnotationKind.Rectangle or AnnotationKind.Ellipse when a.Shape != ShapeStyle.Spotlight:
                    DrawShape(canvas, a, scale);
                    break;
                case AnnotationKind.Pen or AnnotationKind.Marker:
                    DrawStroke(canvas, a, scale);
                    break;
                case AnnotationKind.Text:
                    DrawText(canvas, a, scale);
                    break;
                case AnnotationKind.Number:
                    DrawNumber(canvas, a, scale);
                    break;
                case AnnotationKind.Pointer:
                    DrawPointer(canvas, a, scale);
                    break;
                case AnnotationKind.Mask:
                    DrawMask(canvas, capture, a, scale, part => Below(capture, list, i, part, area));
                    break;
            }
        }
    }

    private static SKPaint Stroke(Annotation a, float scale) => new()
    {
        IsAntialias = true,
        Style = SKPaintStyle.Stroke,
        Color = new SKColor(a.Color),
        StrokeWidth = AnnotationGeometry.StrokeWidth(a, scale),
        StrokeCap = SKStrokeCap.Round,
        StrokeJoin = SKStrokeJoin.Round,
    };

    private static SKPaint Fill(uint color) => new() { IsAntialias = true, Style = SKPaintStyle.Fill, Color = new SKColor(color) };

    private static void DrawShape(SKCanvas canvas, Annotation a, float scale)
    {
        using var paint = a.Shape == ShapeStyle.Filled ? Fill(a.Color) : Stroke(a, scale);
        var rect = a.Rect;
        if (a.Kind == AnnotationKind.Ellipse)
            canvas.DrawOval(rect, paint);
        else
            canvas.DrawRoundRect(rect, AnnotationGeometry.CornerRadius(scale), AnnotationGeometry.CornerRadius(scale), paint);
    }

    // The shaft stops under the head, whose point is the end: a round cap never shows past it.
    private static void DrawArrow(SKCanvas canvas, Annotation a, float scale)
    {
        float length = AnnotationGeometry.Distance(a.Start, a.End);
        if (length < 1)
            return;
        float head = AnnotationGeometry.HeadLength(a, scale);
        bool curved = AnnotationGeometry.IsCurved(a);
        var control = curved ? AnnotationGeometry.Control(a) : default;
        // Directions into each end: along the line, or along the curve's tangent there.
        var intoEnd = Unit(curved ? Minus(a.End, control) : Minus(a.End, a.Start));
        var intoStart = Unit(curved ? Minus(a.Start, control) : Minus(a.Start, a.End));
        bool both = a.Arrow == ArrowStyle.Double;

        var start = both ? Plus(a.Start, intoStart, -head * 0.8f) : a.Start;
        var end = Plus(a.End, intoEnd, -head * 0.8f);
        using var paint = Stroke(a, scale);
        using (var shaft = new SKPath())
        {
            shaft.MoveTo(start);
            if (curved)
                shaft.QuadTo(control, end);
            else
                shaft.LineTo(end);
            canvas.DrawPath(shaft, paint);
        }
        DrawHead(canvas, a.End, intoEnd, head, a.Color, paint.StrokeWidth);
        if (both)
            DrawHead(canvas, a.Start, intoStart, head, a.Color, paint.StrokeWidth);
    }

    private static void DrawHead(SKCanvas canvas, SKPoint tip, SKPoint direction, float length, uint color, float width)
    {
        var normal = new SKPoint(-direction.Y, direction.X);
        var baseCenter = Plus(tip, direction, -length);
        using var path = new SKPath();
        path.MoveTo(tip);
        path.LineTo(Plus(baseCenter, normal, length * 0.6f));
        path.LineTo(Plus(baseCenter, normal, -length * 0.6f));
        path.Close();
        // Filled, and outlined thinly with round joins: softer corners, as in the mock-up.
        using var fill = Fill(color);
        canvas.DrawPath(path, fill);
        using var edge = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Stroke, Color = new SKColor(color), StrokeWidth = width * 0.5f, StrokeJoin = SKStrokeJoin.Round };
        canvas.DrawPath(path, edge);
    }

    // A freehand stroke, smoothed: through the middles of its points.
    private static void DrawStroke(SKCanvas canvas, Annotation a, float scale)
    {
        var points = a.Points;
        if (points.Length == 0)
            return;
        using var paint = Stroke(a, scale);
        if (a.Kind == AnnotationKind.Marker)
        {
            // A highlighter: it darkens what is under it (black text stays black), as a real one does.
            paint.BlendMode = SKBlendMode.Multiply;
            paint.Color = paint.Color.WithAlpha(0xC0);
        }
        if (points.Length == 1)
        {
            paint.Style = SKPaintStyle.Fill;
            canvas.DrawCircle(points[0], paint.StrokeWidth / 2, paint);
            return;
        }
        using var path = new SKPath();
        path.MoveTo(points[0]);
        for (int i = 1; i < points.Length - 1; i++)
            path.QuadTo(points[i], new SKPoint((points[i].X + points[i + 1].X) / 2, (points[i].Y + points[i + 1].Y) / 2));
        path.LineTo(points[^1]);
        canvas.DrawPath(path, paint);
    }

    private static void DrawText(SKCanvas canvas, Annotation a, float scale)
    {
        float size = AnnotationGeometry.FontSize(a, scale);
        var box = AnnotationGeometry.TextBox(a, scale);
        var (padX, padY) = AnnotationGeometry.TextPadding(size);
        bool light = Palette.IsLight(a.Color);
        uint ink = a.Color;
        if (a.TextStyle != TextStyle.Plain)
        {
            using var label = new SKPath();
            label.AddRoundRect(box, size * 0.3f, size * 0.3f);
            if (a.TextStyle == TextStyle.Bubble)
            {
                using var tail = new SKPath();
                tail.AddPoly(AnnotationGeometry.BubbleTail(box, size));
                using var joined = label.Op(tail, SKPathOp.Union);
                if (joined is not null)
                {
                    label.Reset();
                    label.AddPath(joined);
                }
            }
            using var fill = Fill(a.Color);
            canvas.DrawPath(label, fill);
            ink = light ? 0xFF1B1B1B : 0xFFFFFFFF;
        }

        using var font = new SKFont(Typefaces.SemiBold, size) { Subpixel = true };
        using var paint = Fill(ink);
        float lineHeight = AnnotationGeometry.LineHeight(size);
        float baseline = box.Top + padY + (lineHeight - (font.Metrics.Descent - font.Metrics.Ascent)) / 2 - font.Metrics.Ascent;
        var lines = AnnotationGeometry.Lines(a.Text);
        for (int i = 0; i < lines.Length; i++)
        {
            float y = baseline + i * lineHeight;
            if (a.TextStyle == TextStyle.Plain)
            {
                // A text alone is outlined, so that it reads on a busy capture.
                using var outline = new SKPaint
                {
                    IsAntialias = true,
                    Style = SKPaintStyle.Stroke,
                    StrokeWidth = size * 0.16f,
                    StrokeJoin = SKStrokeJoin.Round,
                    Color = light ? new SKColor(0x1B, 0x1B, 0x1B, 0xCC) : new SKColor(0xFF, 0xFF, 0xFF, 0xE6),
                };
                canvas.DrawText(lines[i], box.Left + padX, y, SKTextAlign.Left, font, outline);
            }
            canvas.DrawText(lines[i], box.Left + padX, y, SKTextAlign.Left, font, paint);
        }
    }

    private static void DrawNumber(SKCanvas canvas, Annotation a, float scale)
    {
        float radius = AnnotationGeometry.NumberDiameter(a, scale) / 2;
        bool light = Palette.IsLight(a.Color);
        // A ring of the other tone around it: it stands out on any capture.
        using (var ring = Fill(light ? 0xFF1B1B1Bu : 0xFFFFFFFFu))
            canvas.DrawCircle(a.Start, radius + 2 * scale, ring);
        using (var disc = Fill(a.Color))
            canvas.DrawCircle(a.Start, radius, disc);
        string text = a.Number.ToString(System.Globalization.CultureInfo.InvariantCulture);
        using var font = new SKFont(Typefaces.Bold, radius * (text.Length > 1 ? 0.95f : 1.1f));
        using var paint = Fill(light ? 0xFF1B1B1Bu : 0xFFFFFFFFu);
        // Centered on the digits' own height (cap height), not on the line's.
        float capHeight = font.Metrics.CapHeight > 0 ? font.Metrics.CapHeight : font.Size * 0.7f;
        canvas.DrawText(text, a.Start.X, a.Start.Y + capHeight / 2, SKTextAlign.Center, font, paint);
    }

    private static void DrawPointer(SKCanvas canvas, Annotation a, float scale)
    {
        float height = AnnotationGeometry.PointerHeight(a, scale);
        if (a.Motion is { } motion)
        {
            // A pointer that moves shows its click when it arrives (posed at a moment of the video).
            if (a.Click is { } phase)
            {
                DrawClick(canvas, a.Start, motion.Click, phase, ClickRadius(height), scale);
                if (motion.Click == ClickEffect.Press)
                    height *= PressScale(phase);
            }
        }
        else if (a.Halo)
        {
            float radius = AnnotationGeometry.HaloRadius(a, scale);
            using var fill = new SKPaint { IsAntialias = true, Color = HaloFill };
            canvas.DrawCircle(a.Start, radius, fill);
            using var edge = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 2 * scale, Color = HaloStroke };
            canvas.DrawCircle(a.Start, radius, edge);
        }
        DrawPointerShape(canvas, a.Pointer, a.Start, height);
    }

    /// <summary>A drawn pointer (white with a dark outline, or the reverse), with a soft shadow.</summary>
    private static void DrawPointerShape(SKCanvas canvas, PointerStyle style, SKPoint at, float height)
    {
        using var path = AnnotationGeometry.PointerPath(style, at, height, out bool dark);
        float k = height / 24;
        using (var shadow = new SKPaint
        {
            IsAntialias = true,
            Color = new SKColor(0, 0, 0, 0x59),
            ImageFilter = SKImageFilter.CreateBlur(1.2f * k, 1.2f * k),
        })
        {
            canvas.Save();
            canvas.Translate(0, 1 * k);
            canvas.DrawPath(path, shadow);
            canvas.Restore();
        }
        using (var body = Fill(dark ? 0xFF111111u : 0xFFFFFFFFu))
            canvas.DrawPath(path, body);
        using var outline = new SKPaint
        {
            IsAntialias = true,
            Style = SKPaintStyle.Stroke,
            StrokeWidth = 1.5f * k,
            StrokeJoin = SKStrokeJoin.Round,
            Color = dark ? SKColors.White : new SKColor(0x11, 0x11, 0x11),
        };
        canvas.DrawPath(path, outline);
    }

    /// <summary>
    /// What is under the annotation <paramref name="index"/> of <paramref name="list"/> on the part <paramref name="part"/>
    /// of the capture: the capture with the annotations before it (the author: a blur that left a line drawn under it
    /// sharp looked wrong).
    /// </summary>
    private static SKImage Below(SKImage capture, IReadOnlyList<Annotation> list, int index, SKRectI part, PixelRect area)
    {
        using var surface = SKSurface.Create(new SKImageInfo(part.Width, part.Height, SKColorType.Bgra8888, SKAlphaType.Premul))
            ?? throw new OutOfMemoryException("The part is too large.");
        var canvas = surface.Canvas;
        canvas.Clear(SKColors.Transparent);
        canvas.Translate(-part.Left, -part.Top);
        canvas.DrawImage(capture, 0, 0);
        if (index > 0)
            Draw(canvas, capture, list.Take(index).ToList(), area);
        return surface.Snapshot();
    }

    private static void DrawMask(SKCanvas canvas, SKImage capture, Annotation a, float scale, Func<SKRectI, SKImage> below)
    {
        var rect = a.Rect;
        rect.Intersect(SKRect.Create(0, 0, capture.Width, capture.Height));
        if (rect.Width < 1 || rect.Height < 1)
            return;
        switch (a.Mask)
        {
            case MaskStyle.Solid:
                using (var fill = new SKPaint { IsAntialias = true, Color = SolidMask })
                    canvas.DrawRect(rect, fill);
                break;
            case MaskStyle.Blur:
            {
                // Blurred from what is under it; strong enough that a line of text cannot be read.
                float sigma = Math.Max(8 * scale, Math.Min(rect.Width, rect.Height) / 10);
                var source = SKRect.Inflate(rect, sigma * 3, sigma * 3);
                source.Intersect(SKRect.Create(0, 0, capture.Width, capture.Height));
                var part = SKRectI.Ceiling(source, outwards: true);
                using var under = below(part);
                canvas.Save();
                canvas.ClipRect(rect, antialias: true);
                using var paint = new SKPaint { ImageFilter = SKImageFilter.CreateBlur(sigma, sigma, SKShaderTileMode.Clamp) };
                canvas.DrawImage(under, part.Left, part.Top, new SKSamplingOptions(SKFilterMode.Linear), paint);
                canvas.Restore();
                break;
            }
            default:
            {
                int cell = Math.Max(6, (int)Math.Round(12 * scale));
                var part = SKRectI.Round(rect);
                part.Intersect(SKRectI.Create(0, 0, capture.Width, capture.Height));
                if (part.Width <= 0 || part.Height <= 0)
                    break;
                using var under = below(part);
                using var cells = Pixelate(under, SKRect.Create(0, 0, part.Width, part.Height), cell, out var cut);
                var area = SKRectI.Create(part.Left + cut.Left, part.Top + cut.Top, cut.Width, cut.Height);
                if (cells is null)
                    break;
                // One pixel per block, enlarged without smoothing; the last blocks may be cut by the mask's edge.
                canvas.Save();
                canvas.ClipRect(rect, antialias: true);
                canvas.DrawImage(cells, SKRect.Create(area.Left, area.Top, cells.Width * cell, cells.Height * cell), new SKSamplingOptions(SKFilterMode.Nearest), null);
                canvas.Restore();
                break;
            }
        }
    }

    /// <summary>
    /// The part <paramref name="rect"/> of the capture (rounded out to <paramref name="area"/>) in blocks of
    /// <paramref name="cell"/> pixels, one pixel per block: the average of the block's pixels.
    /// </summary>
    public static SKImage? Pixelate(SKImage capture, SKRect rect, int cell, out SKRectI area)
    {
        area = SKRectI.Round(rect);
        area.Intersect(SKRectI.Create(0, 0, capture.Width, capture.Height));
        if (area.Width <= 0 || area.Height <= 0)
            return null;
        using var bitmap = new SKBitmap(new SKImageInfo(area.Width, area.Height, SKColorType.Bgra8888, SKAlphaType.Premul));
        if (!capture.ReadPixels(bitmap.Info, bitmap.GetPixels(), bitmap.RowBytes, area.Left, area.Top))
            return null;
        var pixels = bitmap.GetPixelSpan();
        int width = area.Width, height = area.Height, stride = bitmap.RowBytes;
        int columns = (width + cell - 1) / cell, rows = (height + cell - 1) / cell;
        var blocks = new byte[columns * rows * 4];
        for (int row = 0; row < rows; row++)
        {
            int y0 = row * cell, y1 = Math.Min(height, y0 + cell);
            for (int column = 0; column < columns; column++)
            {
                int x0 = column * cell, x1 = Math.Min(width, x0 + cell);
                long b = 0, g = 0, r = 0, alpha = 0;
                for (int y = y0; y < y1; y++)
                {
                    int i = y * stride + x0 * 4;
                    for (int x = x0; x < x1; x++, i += 4)
                    {
                        b += pixels[i];
                        g += pixels[i + 1];
                        r += pixels[i + 2];
                        alpha += pixels[i + 3];
                    }
                }
                int count = (y1 - y0) * (x1 - x0), o = (row * columns + column) * 4;
                blocks[o] = (byte)(b / count);
                blocks[o + 1] = (byte)(g / count);
                blocks[o + 2] = (byte)(r / count);
                blocks[o + 3] = (byte)(alpha / count);
            }
        }
        return SKImage.FromPixelCopy(new SKImageInfo(columns, rows, SKColorType.Bgra8888, SKAlphaType.Premul), blocks, columns * 4);
    }

    private static SKPoint Minus(SKPoint a, SKPoint b) => new(a.X - b.X, a.Y - b.Y);

    private static SKPoint Plus(SKPoint a, SKPoint direction, float length) => new(a.X + direction.X * length, a.Y + direction.Y * length);

    private static SKPoint Unit(SKPoint v)
    {
        float length = MathF.Sqrt(v.X * v.X + v.Y * v.Y);
        return length < 1e-3f ? new SKPoint(1, 0) : new SKPoint(v.X / length, v.Y / length);
    }
}
