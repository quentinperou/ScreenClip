using System.Runtime.CompilerServices;
using SkiaSharp;

namespace ScreenClip.Core.Composition;

/// <summary>
/// Draws a capture laid out in a <see cref="ClipStyle"/>: background, shadow, window bar, rounded corners, 3D turn.
/// The same drawing makes the preview (smaller <c>scale</c>) and the export (scale 1, the capture's own pixels).
/// </summary>
public static class Composer
{
    private static readonly SKColor[] TrafficLights = [new(0xFF, 0x5F, 0x57), new(0xFE, 0xBC, 0x2E), new(0x28, 0xC8, 0x40)];
    private static readonly Lazy<SKTypeface> Regular = new(() => LoadTypeface(SKFontStyle.Normal));
    private static readonly Lazy<SKTypeface> SemiBold = new(() => LoadTypeface(new SKFontStyle(SKFontStyleWeight.SemiBold, SKFontStyleWidth.Normal, SKFontStyleSlant.Upright)));

    /// <summary>
    /// The laid-out picture, <paramref name="scale"/> times its real size. <paramref name="backgroundImage"/> is the
    /// user's picture for <see cref="Backgrounds.ImageId"/>.
    /// </summary>
    public static SKImage Render(SKImage capture, ClipStyle style, float scale = 1, SKImage? backgroundImage = null)
    {
        var layout = ClipLayout.Measure(capture.Width, capture.Height, style);
        var info = new SKImageInfo(Math.Max(1, (int)Math.Round(layout.Width * scale)), Math.Max(1, (int)Math.Round(layout.Height * scale)),
            SKColorType.Bgra8888, SKAlphaType.Premul);
        using var surface = SKSurface.Create(info) ?? throw new OutOfMemoryException("The picture is too large.");
        var canvas = surface.Canvas;
        canvas.Clear(SKColors.Transparent);
        canvas.Scale(scale);

        DrawBackground(canvas, layout, style, capture, backgroundImage);
        DrawWindow(canvas, layout, style, capture, pixelExact: scale == 1 && !style.IsTurned);
        return surface.Snapshot();
    }

    /// <summary>The picture as a file: PNG (lossless), JPEG (on white: it has no transparency) or WebP.</summary>
    public static byte[] Encode(SKImage image, ExportFormat format)
    {
        if (format == ExportFormat.Jpeg)
        {
            using var flat = OnWhite(image);
            using var jpeg = flat.Encode(SKEncodedImageFormat.Jpeg, 92);
            return jpeg.ToArray();
        }
        using var data = image.Encode(format == ExportFormat.Webp ? SKEncodedImageFormat.Webp : SKEncodedImageFormat.Png, format == ExportFormat.Webp ? 92 : 100);
        return data.ToArray();
    }

    /// <summary>The picture over white: for the formats and apps that have no transparency.</summary>
    public static SKImage OnWhite(SKImage image)
    {
        using var surface = SKSurface.Create(new SKImageInfo(image.Width, image.Height, SKColorType.Bgra8888, SKAlphaType.Premul));
        surface.Canvas.Clear(SKColors.White);
        surface.Canvas.DrawImage(image, 0, 0);
        return surface.Snapshot();
    }

    public static string Extension(ExportFormat format) => format switch
    {
        ExportFormat.Jpeg => ".jpg",
        ExportFormat.Webp => ".webp",
        _ => ".png",
    };

    // ---------------------------------------------------------------- Background

    private static void DrawBackground(SKCanvas canvas, ClipLayout layout, ClipStyle style, SKImage capture, SKImage? backgroundImage)
    {
        var all = SKRect.Create(layout.Width, layout.Height);
        var background = Backgrounds.Find(style.Background) ?? Backgrounds.All[0];
        switch (background.Kind)
        {
            case BackgroundKind.Gradient:
            case BackgroundKind.Solid:
                using (var shader = SKShader.CreateLinearGradient(new SKPoint(0, 0), new SKPoint(all.Right, all.Bottom),
                    [ToColor(background.From), ToColor(background.To)], SKShaderTileMode.Clamp))
                using (var paint = new SKPaint { Shader = shader })
                    canvas.DrawRect(all, paint);
                break;
            case BackgroundKind.Blur:
                DrawBlurred(canvas, all, capture);
                break;
            case BackgroundKind.Image when backgroundImage is not null:
                DrawCover(canvas, all, backgroundImage);
                break;
            case BackgroundKind.Image:
            case BackgroundKind.Transparent:
                break;
        }
    }

    /// <summary>The “blurred capture” background of <paramref name="capture"/>, as a small square swatch.</summary>
    public static SKImage BlurredSwatch(SKImage capture, int size)
    {
        using var surface = SKSurface.Create(new SKImageInfo(size, size, SKColorType.Bgra8888, SKAlphaType.Premul));
        DrawBlurred(surface.Canvas, SKRect.Create(size, size), capture, small: 9); // the shapes of the capture still show
        return surface.Snapshot();
    }

    // The capture shrunk to a few pixels then enlarged smoothly: a strong blur, cheap at any size.
    private static void DrawBlurred(SKCanvas canvas, SKRect all, SKImage capture, int small = 24)
    {
        float ratio = all.Width / all.Height;
        var info = new SKImageInfo(ratio >= 1 ? small : Math.Max(2, (int)(small * ratio)), ratio >= 1 ? Math.Max(2, (int)(small / ratio)) : small);
        using var surface = SKSurface.Create(info);
        DrawCover(surface.Canvas, SKRect.Create(info.Width, info.Height), capture);
        if (capture.AlphaType != SKAlphaType.Opaque)
        {
            // What a see-through picture leaves empty (the gaps of an “All screens” capture) takes its average
            // color: the background stays a background, not a hole (seen by the author).
            using var shown = surface.Snapshot();
            surface.Canvas.Clear(AverageColor(shown));
            surface.Canvas.DrawImage(shown, 0, 0);
        }
        using var tiny = surface.Snapshot();
        // Clamp: the blur carries the edge pixels on; the default would fade the edges of the picture to transparent.
        using var paint = new SKPaint { ImageFilter = SKImageFilter.CreateBlur(all.Width / 60f, all.Width / 60f, SKShaderTileMode.Clamp) };
        canvas.DrawImage(tiny, all, new SKSamplingOptions(SKCubicResampler.Mitchell), paint);
    }

    // The mean color of what a small picture shows (transparent pixels aside); grey when it shows nothing.
    private static SKColor AverageColor(SKImage image)
    {
        using var bitmap = new SKBitmap(new SKImageInfo(image.Width, image.Height, SKColorType.Bgra8888, SKAlphaType.Unpremul));
        if (!image.ReadPixels(bitmap.Info, bitmap.GetPixels(), bitmap.RowBytes))
            return SKColors.Gray;
        double r = 0, g = 0, b = 0, weight = 0;
        foreach (var color in bitmap.Pixels)
        {
            double a = color.Alpha / 255.0;
            r += color.Red * a;
            g += color.Green * a;
            b += color.Blue * a;
            weight += a;
        }
        return weight < 0.01 ? SKColors.Gray : new SKColor((byte)(r / weight), (byte)(g / weight), (byte)(b / weight));
    }

    // Fills the rectangle with the picture, cropped to its shape.
    private static void DrawCover(SKCanvas canvas, SKRect target, SKImage image)
    {
        float scale = Math.Max(target.Width / image.Width, target.Height / image.Height);
        float w = target.Width / scale, h = target.Height / scale;
        var source = SKRect.Create((image.Width - w) / 2, (image.Height - h) / 2, w, h);
        canvas.DrawImage(image, source, target, new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.Linear));
    }

    // ---------------------------------------------------------------- Window

    private static void DrawWindow(SKCanvas canvas, ClipLayout layout, ClipStyle style, SKImage capture, bool pixelExact)
    {
        var window = SKRect.Create(layout.WindowX, layout.WindowY, layout.WindowWidth, layout.WindowHeight);
        // Never more than half the window: a round end, not a shape folded on itself.
        float radius = (float)Math.Min(layout.Radius, Math.Min(window.Width, window.Height) / 2);
        float k = (float)layout.Scale;
        var shape = new SKRoundRect(window, radius, radius);
        var tilt = style.IsTurned
            ? Perspective.RectToQuad(window, Perspective.TiltedCorners(window, style.TurnDegrees, style.TiltDegrees))
            : SKMatrix.Identity;
        var content = SKRect.Create(layout.CaptureX, layout.CaptureY, layout.Shown.Width, layout.Shown.Height);
        var shown = SKRect.Create(layout.Shown.X, layout.Shown.Y, layout.Shown.Width, layout.Shown.Height);
        var sampling = pixelExact ? new SKSamplingOptions(SKFilterMode.Nearest) : new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.Linear);
        float strength = Math.Clamp(style.Shadow, 0, 100) / 100f;

        if (ClipLayout.IsDevice(style.Frame))
        {
            canvas.Save();
            canvas.Concat(in tilt);
            using (var silhouette = style.Frame == FrameKind.Laptop ? LaptopSilhouette(content, layout.Bezel) : PhoneSilhouette(content, layout.Bezel))
            {
                if (strength > 0)
                {
                    DrawShadow(canvas, silhouette, (byte)(40 + 100 * strength), (6 + 34 * strength) * k, (2 + 14 * strength) * k);
                    DrawShadow(canvas, silhouette, (byte)(30 + 30 * strength), 1.5f * k, 1 * k);
                }
            }
            if (style.Frame == FrameKind.Laptop)
                DrawLaptop(canvas, content, layout.Bezel, style.DarkFrame, capture, shown, sampling);
            else
                DrawPhone(canvas, content, layout.Bezel, style.DarkFrame, capture, shown, sampling);
            canvas.Restore();
            return;
        }

        // A picture with transparent parts (the gaps of an “All screens” capture, an icon) and no window around it:
        // its shadow follows what it shows, not the rectangle around it (asked by the author: “the shadow is odd”),
        // and no outline traces that rectangle.
        bool seeThrough = capture.AlphaType != SKAlphaType.Opaque;
        if (seeThrough && style.Frame == FrameKind.None)
        {
            canvas.Save();
            canvas.Concat(in tilt);
            using var recorder = new SKPictureRecorder();
            var recording = recorder.BeginRecording(window);
            // Rounded corners on every visible corner (each screen of an “All screens” capture), not on the corners of
            // the rectangle around, which may be empty (asked by the author: “some corners rounded, others not”).
            if (Visible(capture) is { } visible && radius > 0)
            {
                using var placed = new SKPath(visible.Path);
                placed.Transform(SKMatrix.CreateTranslation(content.Left, content.Top));
                using var rounded = RoundCorners(placed, Math.Min((float)layout.Radius, visible.SmallestSide / 2));
                recording.ClipPath(rounded, SKClipOperation.Intersect, antialias: true);
            }
            else
                recording.ClipRoundRect(shape, SKClipOperation.Intersect, antialias: true);
            recording.DrawImage(capture, content, sampling);
            using var picture = recorder.EndRecording();
            if (strength > 0)
            {
                DrawShadowOf(canvas, picture, (byte)(40 + 100 * strength), (6 + 34 * strength) * k, (2 + 14 * strength) * k);
                DrawShadowOf(canvas, picture, (byte)(30 + 30 * strength), 1.5f * k, 1 * k);
            }
            canvas.DrawPicture(picture);
            canvas.Restore();
            return;
        }

        // Shadow: a soft one that grows with the setting, and a thin one that keeps the edge visible.
        if (strength > 0)
        {
            canvas.Save();
            canvas.Concat(in tilt);
            using var silhouette = new SKPath();
            silhouette.AddRoundRect(shape);
            DrawShadow(canvas, silhouette, (byte)(40 + 100 * strength), (6 + 34 * strength) * k, (2 + 14 * strength) * k);
            DrawShadow(canvas, silhouette, (byte)(30 + 30 * strength), 1.5f * k, 1 * k);
            canvas.Restore();
        }

        canvas.Save();
        canvas.Concat(in tilt);
        canvas.ClipRoundRect(shape, SKClipOperation.Intersect, antialias: true);
        if (layout.BarHeight > 0)
            DrawBar(canvas, SKRect.Create(window.Left, window.Top, window.Width, layout.BarHeight), style, k);
        if (seeThrough)
        {
            // In a window, what the picture leaves empty is the window's own page.
            using var page = new SKPaint { Color = style.DarkFrame ? new SKColor(0x20, 0x20, 0x22) : SKColors.White };
            canvas.DrawRect(content, page);
        }
        canvas.DrawImage(capture, content, sampling);
        canvas.Restore();

        // Nothing around a bare picture without a shadow (the “Plain” preset, asked by the author).
        if (style.Frame == FrameKind.None && strength == 0)
            return;

        // A fine outline, as Windows and macOS draw around their windows.
        canvas.Save();
        canvas.Concat(in tilt);
        using (var outline = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = k, Color = new SKColor(0, 0, 0, 0x1F) })
        {
            var inset = new SKRoundRect(SKRect.Inflate(window, -k / 2, -k / 2), Math.Max(0, radius - k / 2), Math.Max(0, radius - k / 2));
            canvas.DrawRoundRect(inset, outline);
        }
        canvas.Restore();
    }

    // ---------------------------------------------------------------- Shape of a see-through picture

    private const int MaxShapeRectangles = 64;

    /// <summary>What a see-through picture shows, as rectangles joined in one path, and the shortest side of them.</summary>
    private sealed record VisibleShape(SKPath Path, float SmallestSide);

    private sealed record ShapeEntry(VisibleShape? Shape);

    // Traced once per picture (the preview draws it again on every change).
    private static readonly ConditionalWeakTable<SKImage, ShapeEntry> Shapes = new();

    private static VisibleShape? Visible(SKImage capture) => Shapes.GetValue(capture, image => new ShapeEntry(Trace(image))).Shape;

    // The opaque part of the picture as rectangles: rows with the same opaque runs make one band. A few rectangles for
    // screens; a shape that is not (an icon with soft edges) gives null, and the rectangle around is used.
    private static VisibleShape? Trace(SKImage image)
    {
        using var pixmap = image.PeekPixels();
        if (pixmap is null || pixmap.ColorType is not (SKColorType.Bgra8888 or SKColorType.Rgba8888))
            return null;
        var pixels = pixmap.GetPixelSpan();
        int width = pixmap.Width, height = pixmap.Height, stride = pixmap.RowBytes;
        var rects = new List<SKRect>();
        var band = new List<(int Start, int End)>();
        var row = new List<(int Start, int End)>();
        int bandTop = 0;
        for (int y = 0; y <= height; y++)
        {
            row.Clear();
            if (y < height)
            {
                int start = -1;
                for (int x = 0; x <= width; x++)
                {
                    bool opaque = x < width && pixels[y * stride + x * 4 + 3] >= 128;
                    if (opaque && start < 0)
                        start = x;
                    else if (!opaque && start >= 0)
                    {
                        row.Add((start, x));
                        start = -1;
                    }
                }
            }
            if (y > 0 && row.SequenceEqual(band))
                continue;
            foreach (var (start, end) in band)
                rects.Add(new SKRect(start, bandTop, end, y));
            if (rects.Count > MaxShapeRectangles)
                return null;
            band = [.. row];
            bandTop = y;
        }
        if (rects.Count == 0)
            return null;

        var path = new SKPath();
        foreach (var rect in rects)
        {
            using var piece = new SKPath();
            piece.AddRect(rect);
            var joined = path.Op(piece, SKPathOp.Union);
            if (joined is null)
                return null;
            path.Dispose();
            path = joined;
        }
        return new VisibleShape(path, rects.Min(r => Math.Min(r.Width, r.Height)));
    }

    private static SKPath RoundCorners(SKPath path, float radius)
    {
        using var effect = SKPathEffect.CreateCorner(radius);
        using var paint = new SKPaint { Style = SKPaintStyle.Fill, PathEffect = effect };
        var rounded = new SKPath();
        paint.GetFillPath(path, rounded);
        return rounded;
    }

    // The shadow of what a picture shows (its alpha), drawn under it.
    private static void DrawShadowOf(SKCanvas canvas, SKPicture picture, byte alpha, float blur, float offset)
    {
        float sigma = Math.Max(0.5f, blur / 2);
        using var paint = new SKPaint { ImageFilter = SKImageFilter.CreateDropShadowOnly(0, offset, sigma, sigma, new SKColor(10, 12, 30, alpha)) };
        canvas.DrawPicture(picture, paint);
    }

    private static void DrawShadow(SKCanvas canvas, SKPath shape, byte alpha, float blur, float offset)
    {
        using var paint = new SKPaint
        {
            IsAntialias = true,
            Color = new SKColor(10, 12, 30, alpha),
            MaskFilter = SKMaskFilter.CreateBlur(SKBlurStyle.Normal, Math.Max(0.5f, blur / 2)),
        };
        canvas.Save();
        // Only around the window: through the transparent parts of a picture (the gaps between screens, an icon)
        // the background shows, not a shadow.
        canvas.ClipPath(shape, SKClipOperation.Difference, antialias: true);
        canvas.Translate(0, offset);
        canvas.DrawPath(shape, paint);
        canvas.Restore();
    }

    // ---------------------------------------------------------------- Devices

    // A laptop, no brand: a black screen frame (camera at the top) on a metal base with a thumb notch. Its parts follow
    // the bezel, itself a share of the capture (DeviceShape).
    private static (SKRoundRect Lid, SKRect Base) LaptopParts(SKRect screen, int bezel)
    {
        float b = bezel;
        var lid = SKRect.Create(screen.Left - b, screen.Top - (float)Math.Round(b * DeviceShape.LaptopTopBezel), screen.Width + 2 * b, 0);
        lid.Bottom = screen.Bottom + b;
        var lidShape = new SKRoundRect();
        float top = b * 0.9f, bottom = b * 0.25f;
        lidShape.SetRectRadii(lid, [new SKPoint(top, top), new SKPoint(top, top), new SKPoint(bottom, bottom), new SKPoint(bottom, bottom)]);
        float baseWidth = (float)Math.Round(lid.Width * DeviceShape.LaptopBaseWidth);
        var baseRect = SKRect.Create(lid.MidX - baseWidth / 2, lid.Bottom, baseWidth, (float)Math.Round(b * DeviceShape.LaptopBaseHeight));
        return (lidShape, baseRect);
    }

    private static SKPath LaptopSilhouette(SKRect screen, int bezel)
    {
        var (lid, baseRect) = LaptopParts(screen, bezel);
        var path = new SKPath();
        path.AddRoundRect(lid);
        path.AddRoundRect(BaseShape(baseRect));
        return path;
    }

    private static SKRoundRect BaseShape(SKRect baseRect)
    {
        var shape = new SKRoundRect();
        float top = baseRect.Height * 0.12f, bottom = baseRect.Height * 0.75f;
        shape.SetRectRadii(baseRect, [new SKPoint(top, top), new SKPoint(top, top), new SKPoint(bottom * 2.5f, bottom), new SKPoint(bottom * 2.5f, bottom)]);
        return shape;
    }

    private static void DrawLaptop(SKCanvas canvas, SKRect screen, int bezel, bool dark, SKImage capture, SKRect shown, SKSamplingOptions sampling)
    {
        var (lid, baseRect) = LaptopParts(screen, bezel);
        float b = bezel;

        // The lid: black glass with a thin metal rim.
        using (var rim = new SKPaint { IsAntialias = true, Color = dark ? new SKColor(0x4A, 0x4C, 0x52) : new SKColor(0xC9, 0xCC, 0xD2) })
            canvas.DrawRoundRect(lid, rim);
        var glass = new SKRoundRect(lid);
        glass.Deflate(Math.Max(1, b * 0.08f), Math.Max(1, b * 0.08f));
        using (var black = new SKPaint { IsAntialias = true, Color = new SKColor(0x11, 0x11, 0x13) })
            canvas.DrawRoundRect(glass, black);
        using (var camera = new SKPaint { IsAntialias = true, Color = new SKColor(0x2C, 0x2C, 0x31) })
            canvas.DrawCircle(screen.MidX, (lid.Rect.Top + screen.Top) / 2, Math.Max(1.5f, b * 0.12f), camera);

        canvas.DrawImage(capture, shown, screen, sampling);

        // The base: metal, lighter on top, with the notch where the lid opens.
        var baseShape = BaseShape(baseRect);
        var light = dark ? new SKColor(0x6A, 0x6D, 0x74) : new SKColor(0xE9, 0xEB, 0xEF);
        var shade = dark ? new SKColor(0x2E, 0x30, 0x35) : new SKColor(0xA8, 0xAD, 0xB5);
        using (var shader = SKShader.CreateLinearGradient(new SKPoint(0, baseRect.Top), new SKPoint(0, baseRect.Bottom), [light, shade], SKShaderTileMode.Clamp))
        using (var metal = new SKPaint { IsAntialias = true, Shader = shader })
            canvas.DrawRoundRect(baseShape, metal);
        float notchWidth = baseRect.Width * 0.16f, notchHeight = baseRect.Height * 0.3f;
        var notch = new SKRoundRect();
        notch.SetRectRadii(SKRect.Create(baseRect.MidX - notchWidth / 2, baseRect.Top, notchWidth, notchHeight),
            [new SKPoint(0, 0), new SKPoint(0, 0), new SKPoint(notchHeight, notchHeight), new SKPoint(notchHeight, notchHeight)]);
        using (var notchPaint = new SKPaint { IsAntialias = true, Color = dark ? new SKColor(0x3A, 0x3C, 0x41) : new SKColor(0xC4, 0xC8, 0xCE) })
            canvas.DrawRoundRect(notch, notchPaint);
        // The hinge: a dark line where the lid meets the base.
        using (var hinge = new SKPaint { Color = new SKColor(0, 0, 0, 0x55) })
            canvas.DrawRect(SKRect.Create(lid.Rect.Left, baseRect.Top, lid.Rect.Width, Math.Max(1, b * 0.06f)), hinge);
    }

    // A phone, no brand: metal edge, black border, rounded screen with a camera hole, side buttons.
    private static (SKRoundRect Body, SKRoundRect Border, SKRoundRect Screen) PhoneParts(SKRect screen, int bezel)
    {
        float b = bezel, edge = Math.Max(2, (float)Math.Round(b * DeviceShape.PhoneEdge));
        float screenRadius = Math.Min(screen.Width, screen.Height) * 0.085f;
        var screenShape = new SKRoundRect(screen, screenRadius, screenRadius);
        var border = new SKRoundRect(SKRect.Inflate(screen, b, b), screenRadius + b, screenRadius + b);
        var body = new SKRoundRect(SKRect.Inflate(screen, b + edge, b + edge), screenRadius + b + edge, screenRadius + b + edge);
        return (body, border, screenShape);
    }

    private static SKRect[] PhoneButtons(SKRect screen, int bezel)
    {
        var (body, _, _) = PhoneParts(screen, bezel);
        float stick = Math.Max(2, (float)Math.Round(bezel * DeviceShape.PhoneButtons)) + 1;
        var b = body.Rect;
        if (screen.Height >= screen.Width)
        {
            // Upright: power on the right, volume up and down on the left.
            return
            [
                SKRect.Create(b.Right - 1, b.Top + b.Height * 0.24f, stick, b.Height * 0.12f),
                SKRect.Create(b.Left - stick + 1, b.Top + b.Height * 0.18f, stick, b.Height * 0.07f),
                SKRect.Create(b.Left - stick + 1, b.Top + b.Height * 0.27f, stick, b.Height * 0.07f),
            ];
        }
        // Sideways (camera on the left): the same buttons on the top and bottom edges.
        return
        [
            SKRect.Create(b.Left + b.Width * 0.64f, b.Top - stick + 1, b.Width * 0.12f, stick),
            SKRect.Create(b.Left + b.Width * 0.75f, b.Bottom - 1, b.Width * 0.07f, stick),
            SKRect.Create(b.Left + b.Width * 0.66f, b.Bottom - 1, b.Width * 0.07f, stick),
        ];
    }

    private static SKPath PhoneSilhouette(SKRect screen, int bezel)
    {
        var path = new SKPath();
        path.AddRoundRect(PhoneParts(screen, bezel).Body);
        return path;
    }

    private static void DrawPhone(SKCanvas canvas, SKRect screen, int bezel, bool dark, SKImage capture, SKRect shown, SKSamplingOptions sampling)
    {
        var (body, border, screenShape) = PhoneParts(screen, bezel);
        var light = dark ? new SKColor(0x55, 0x58, 0x5F) : new SKColor(0xEC, 0xEE, 0xF2);
        var shade = dark ? new SKColor(0x22, 0x24, 0x28) : new SKColor(0xA9, 0xAE, 0xB6);
        using var shader = SKShader.CreateLinearGradient(new SKPoint(body.Rect.Left, body.Rect.Top), new SKPoint(body.Rect.Right, body.Rect.Bottom), [light, shade], SKShaderTileMode.Clamp);
        using var metal = new SKPaint { IsAntialias = true, Shader = shader };
        foreach (var button in PhoneButtons(screen, bezel))
        {
            float r = Math.Min(button.Width, button.Height) / 2;
            canvas.DrawRoundRect(button, r, r, metal);
        }
        canvas.DrawRoundRect(body, metal);
        using (var black = new SKPaint { IsAntialias = true, Color = new SKColor(0x0B, 0x0B, 0x0D) })
            canvas.DrawRoundRect(border, black);

        canvas.Save();
        canvas.ClipRoundRect(screenShape, SKClipOperation.Intersect, antialias: true);
        canvas.DrawImage(capture, shown, screen, sampling);
        canvas.Restore();

        // The camera: a hole near the top of the screen (on the left when sideways).
        float hole = Math.Max(2, bezel * 0.34f);
        float inset = Math.Max(screenShape.Radii[0].X * 0.55f, bezel * 0.9f);
        var center = screen.Height >= screen.Width ? new SKPoint(screen.MidX, screen.Top + inset) : new SKPoint(screen.Left + inset, screen.MidY);
        using (var camera = new SKPaint { IsAntialias = true, Color = new SKColor(0x05, 0x05, 0x06) })
            canvas.DrawCircle(center, hole, camera);
    }

    // ---------------------------------------------------------------- Window bars

    private static void DrawBar(SKCanvas canvas, SKRect bar, ClipStyle style, float k)
    {
        bool dark = style.DarkFrame;
        var background = dark ? new SKColor(0x2B, 0x2B, 0x30) : new SKColor(0xF1, 0xF2, 0xF4);
        var line = dark ? new SKColor(0x1C, 0x1C, 0x20) : new SKColor(0xE1, 0xE3, 0xE7);
        var text = dark ? new SKColor(0xE6, 0xE6, 0xE9) : new SKColor(0x3A, 0x3F, 0x47);
        var muted = dark ? new SKColor(0x8C, 0x8C, 0x94) : new SKColor(0x9A, 0xA0, 0xA8);
        using var fill = new SKPaint { IsAntialias = true, Color = background };
        canvas.DrawRect(bar, fill);

        if (style.Frame == FrameKind.Windows)
        {
            DrawWindowsBar(canvas, bar, style, k, text);
            return;
        }

        using (var separator = new SKPaint { Color = line })
            canvas.DrawRect(SKRect.Create(bar.Left, bar.Bottom - k, bar.Width, k), separator);
        float middle = bar.MidY;
        for (int i = 0; i < 3; i++)
        {
            using var light = new SKPaint { IsAntialias = true, Color = TrafficLights[i] };
            canvas.DrawCircle(bar.Left + (16 + 20 * i) * k, middle, 6 * k, light);
        }

        if (style.Frame == FrameKind.MacWindow)
        {
            using var font = new SKFont(SemiBold.Value, 12.5f * k);
            using var paint = new SKPaint { IsAntialias = true, Color = text };
            DrawCenteredText(canvas, style.FrameText, bar.MidX, middle, bar.Width - 2 * 90 * k, font, paint);
            return;
        }

        // Browser: back and forward, then the address in a field in the middle.
        using (var chevron = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 1.6f * k, StrokeCap = SKStrokeCap.Round, StrokeJoin = SKStrokeJoin.Round, Color = muted })
        using (var path = new SKPath())
        {
            float x = bar.Left + 84 * k;
            path.MoveTo(x + 4 * k, middle - 5 * k); path.LineTo(x - 1 * k, middle); path.LineTo(x + 4 * k, middle + 5 * k);
            x += 22 * k;
            path.MoveTo(x - 4 * k, middle - 5 * k); path.LineTo(x + 1 * k, middle); path.LineTo(x - 4 * k, middle + 5 * k);
            canvas.DrawPath(path, chevron);
        }
        float fieldWidth = Math.Min(bar.Width * 0.5f, 420 * k);
        if (fieldWidth < 80 * k)
            return; // a very narrow capture: no room for the address
        var field = SKRect.Create(bar.MidX - fieldWidth / 2, middle - 12 * k, fieldWidth, 24 * k);
        using (var fieldPaint = new SKPaint { IsAntialias = true, Color = dark ? new SKColor(0x1C, 0x1C, 0x20) : SKColors.White })
            canvas.DrawRoundRect(field, 6 * k, 6 * k, fieldPaint);
        if (string.IsNullOrWhiteSpace(style.FrameText))
            return;
        using var addressFont = new SKFont(Regular.Value, 12 * k);
        using var addressPaint = new SKPaint { IsAntialias = true, Color = text };
        float textWidth = Math.Min(addressFont.MeasureText(style.FrameText), field.Width - 40 * k);
        float lockX = field.MidX - textWidth / 2 - 10 * k;
        DrawLock(canvas, lockX, middle, k, muted);
        DrawCenteredText(canvas, style.FrameText, field.MidX + 5 * k, middle, field.Width - 40 * k, addressFont, addressPaint);
    }

    private static void DrawWindowsBar(SKCanvas canvas, SKRect bar, ClipStyle style, float k, SKColor text)
    {
        float middle = bar.MidY;
        using (var font = new SKFont(Regular.Value, 12 * k))
        using (var paint = new SKPaint { IsAntialias = true, Color = text })
        {
            string title = Ellipsize(style.FrameText, font, bar.Width - 180 * k);
            canvas.DrawText(title, bar.Left + 16 * k, middle + TextMiddleOffset(font), SKTextAlign.Left, font, paint);
        }
        // Minimize, maximize, close: 46 px wide buttons, glyphs of 10 px, as drawn by Windows 11.
        using var glyph = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = k, Color = text };
        float x = bar.Right - 46 * 2.5f * k;
        canvas.DrawLine(x - 5 * k, middle, x + 5 * k, middle, glyph);
        x += 46 * k;
        canvas.DrawRoundRect(SKRect.Create(x - 5 * k, middle - 5 * k, 10 * k, 10 * k), 1.5f * k, 1.5f * k, glyph);
        x += 46 * k;
        canvas.DrawLine(x - 5 * k, middle - 5 * k, x + 5 * k, middle + 5 * k, glyph);
        canvas.DrawLine(x + 5 * k, middle - 5 * k, x - 5 * k, middle + 5 * k, glyph);
    }

    private static void DrawLock(SKCanvas canvas, float x, float middle, float k, SKColor color)
    {
        using var body = new SKPaint { IsAntialias = true, Color = color };
        canvas.DrawRoundRect(SKRect.Create(x - 4 * k, middle - 1 * k, 8 * k, 6.5f * k), 1.5f * k, 1.5f * k, body);
        using var shackle = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 1.3f * k, Color = color };
        using var path = new SKPath();
        path.MoveTo(x - 2.3f * k, middle - 1 * k);
        path.ArcTo(SKRect.Create(x - 2.3f * k, middle - 5.5f * k, 4.6f * k, 4.6f * k), 180, 180, false);
        canvas.DrawPath(path, shackle);
    }

    private static void DrawCenteredText(SKCanvas canvas, string? text, float x, float middle, float maxWidth, SKFont font, SKPaint paint)
    {
        if (string.IsNullOrWhiteSpace(text) || maxWidth <= 0)
            return;
        canvas.DrawText(Ellipsize(text, font, maxWidth), x, middle + TextMiddleOffset(font), SKTextAlign.Center, font, paint);
    }

    // Distance from the middle of a line to its baseline.
    private static float TextMiddleOffset(SKFont font)
    {
        font.GetFontMetrics(out var metrics);
        return -(metrics.Ascent + metrics.Descent) / 2;
    }

    private static string Ellipsize(string? text, SKFont font, float maxWidth)
    {
        text ??= "";
        if (font.MeasureText(text) <= maxWidth)
            return text;
        for (int length = text.Length - 1; length > 0; length--)
        {
            string shorter = text[..length].TrimEnd() + "…";
            if (font.MeasureText(shorter) <= maxWidth)
                return shorter;
        }
        return "";
    }

    private static SKTypeface LoadTypeface(SKFontStyle style)
    {
        foreach (string family in new[] { "Segoe UI Variable Text", "Segoe UI" })
        {
            var typeface = SKTypeface.FromFamilyName(family, style);
            if (typeface is not null && typeface.FamilyName.StartsWith("Segoe UI", StringComparison.OrdinalIgnoreCase))
                return typeface;
        }
        return SKTypeface.Default;
    }

    private static SKColor ToColor(uint rgb) => new((byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb);
}
