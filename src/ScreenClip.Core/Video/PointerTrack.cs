using SkiaSharp;

namespace ScreenClip.Core.Video;

/// <summary>A shape the mouse pointer took during a recording: its picture (BGRA, premultiplied) and its hot spot.</summary>
public sealed record PointerShape(int Width, int Height, int HotX, int HotY, byte[] Pixels);

/// <summary>The pointer at one moment of a recording: its tip in the video's pixels, its shape (−1: hidden), a button down.</summary>
public readonly record struct PointerSample(TimeSpan Time, float X, float Y, int Shape, bool Pressed);

/// <summary>A click of the recorded pointer: when the button went down and up, and where.</summary>
public readonly record struct PointerClick(TimeSpan Down, TimeSpan Up, SKPoint At);

/// <summary>The recorded pointer at a time: where its tip is, its shape (−1: hidden), a button down.</summary>
public readonly record struct PointerState(SKPoint Position, int Shape, bool Pressed)
{
    public bool Visible => Shape >= 0;
}

/// <summary>
/// The mouse pointer recorded apart from a video (positions, shapes, clicks), so that the editor can draw it again in
/// another style or size, smoothed, with its clicks shown. Its times are the video's (the pauses left out).
/// </summary>
public sealed class PointerTrack
{
    /// <summary>Samples further apart than this are not joined: the pointer stood still in between.</summary>
    public static readonly TimeSpan MaxGap = TimeSpan.FromMilliseconds(250);

    // The smoothed path: positions every 10 ms, averaged over a bell of 70 ms each side (no lag: the whole track is
    // known, so the average looks ahead as much as behind).
    private const double SmoothStep = 0.01, SmoothSigma = 0.07;

    private readonly PointerSample[] _samples;
    private readonly SKImage?[] _images;
    private SKPoint[]? _smooth;

    public PointerTrack(IEnumerable<PointerSample> samples, IReadOnlyList<PointerShape> shapes)
    {
        _samples = [.. samples.OrderBy(s => s.Time)];
        Shapes = shapes;
        _images = new SKImage?[shapes.Count];
        Clicks = FindClicks(_samples);
    }

    public IReadOnlyList<PointerSample> Samples => _samples;

    public IReadOnlyList<PointerShape> Shapes { get; }

    public IReadOnlyList<PointerClick> Clicks { get; }

    public bool IsEmpty => _samples.Length == 0;

    /// <summary>The pointer at <paramref name="time"/>: between two samples, where it was on its way.</summary>
    public PointerState At(TimeSpan time, bool smooth = false)
    {
        if (_samples.Length == 0)
            return new PointerState(default, -1, false);
        int i = IndexAt(time);
        if (i < 0)
            return new PointerState(new SKPoint(_samples[0].X, _samples[0].Y), _samples[0].Shape, false);
        var sample = _samples[i];
        var position = Interpolate(i, time);
        if (smooth && sample.Shape >= 0)
            position = Smoothed(time, position);
        return new PointerState(position, sample.Shape, sample.Pressed);
    }

    /// <summary>The height of a shape's picture (32 for a hidden pointer: the usual size).</summary>
    public int HeightOf(int shape) => shape >= 0 && shape < Shapes.Count ? Shapes[shape].Height : 32;

    /// <summary>The picture of a shape, made once.</summary>
    public SKImage? ImageOf(int shape)
    {
        if (shape < 0 || shape >= Shapes.Count)
            return null;
        lock (_images)
        {
            if (_images[shape] is null)
            {
                var s = Shapes[shape];
                _images[shape] = SKImage.FromPixelCopy(new SKImageInfo(s.Width, s.Height, SKColorType.Bgra8888, SKAlphaType.Premul), s.Pixels, s.Width * 4);
            }
            return _images[shape];
        }
    }

    // The last sample at or before the time, −1 before the first.
    private int IndexAt(TimeSpan time)
    {
        int low = 0, high = _samples.Length - 1, found = -1;
        while (low <= high)
        {
            int middle = (low + high) / 2;
            if (_samples[middle].Time <= time)
            {
                found = middle;
                low = middle + 1;
            }
            else
                high = middle - 1;
        }
        return found;
    }

    private SKPoint Interpolate(int i, TimeSpan time)
    {
        var a = _samples[i];
        if (i + 1 >= _samples.Length)
            return new SKPoint(a.X, a.Y);
        var b = _samples[i + 1];
        var gap = b.Time - a.Time;
        if (gap <= TimeSpan.Zero || gap > MaxGap || a.Shape < 0 || b.Shape < 0)
            return new SKPoint(a.X, a.Y);
        float f = (float)Math.Clamp((time - a.Time) / gap, 0, 1);
        return new SKPoint(a.X + (b.X - a.X) * f, a.Y + (b.Y - a.Y) * f);
    }

    private SKPoint Smoothed(TimeSpan time, SKPoint raw)
    {
        var path = LazyInitializer.EnsureInitialized(ref _smooth, BuildSmooth);
        if (path.Length == 0)
            return raw;
        double f = (time - _samples[0].Time).TotalSeconds / SmoothStep;
        if (f <= 0)
            return path[0];
        if (f >= path.Length - 1)
            return path[^1];
        int k = (int)f;
        float u = (float)(f - k);
        return new SKPoint(path[k].X + (path[k + 1].X - path[k].X) * u, path[k].Y + (path[k + 1].Y - path[k].Y) * u);
    }

    private SKPoint[] BuildSmooth()
    {
        var start = _samples[0].Time;
        int count = (int)Math.Ceiling((_samples[^1].Time - start).TotalSeconds / SmoothStep) + 1;
        var raw = new SKPoint[count];
        var run = new int[count];   // the stretch of time it belongs to: the average never crosses a hidden pointer
        int current = 0;
        bool wasHidden = false;
        for (int k = 0; k < count; k++)
        {
            var time = start + TimeSpan.FromSeconds(k * SmoothStep);
            int i = Math.Max(0, IndexAt(time));
            bool hidden = _samples[i].Shape < 0;
            if (hidden != wasHidden)
                current++;
            wasHidden = hidden;
            raw[k] = Interpolate(i, time);
            run[k] = hidden ? -1 : current;
        }
        int reach = (int)Math.Ceiling(3 * SmoothSigma / SmoothStep);
        var weights = new double[reach + 1];
        for (int j = 0; j <= reach; j++)
            weights[j] = Math.Exp(-0.5 * Math.Pow(j * SmoothStep / SmoothSigma, 2));
        var smooth = new SKPoint[count];
        for (int k = 0; k < count; k++)
        {
            if (run[k] < 0)
            {
                smooth[k] = raw[k];
                continue;
            }
            double x = 0, y = 0, total = 0;
            for (int j = Math.Max(0, k - reach); j <= Math.Min(count - 1, k + reach); j++)
            {
                if (run[j] != run[k])
                    continue;
                double w = weights[Math.Abs(j - k)];
                x += raw[j].X * w;
                y += raw[j].Y * w;
                total += w;
            }
            smooth[k] = new SKPoint((float)(x / total), (float)(y / total));
        }
        return smooth;
    }

    private static PointerClick[] FindClicks(PointerSample[] samples)
    {
        var clicks = new List<PointerClick>();
        PointerSample? down = null;
        foreach (var sample in samples)
        {
            if (sample.Pressed && down is null)
                down = sample;
            else if (!sample.Pressed && down is { } d)
            {
                clicks.Add(new PointerClick(d.Time, sample.Time, new SKPoint(d.X, d.Y)));
                down = null;
            }
        }
        if (down is { } last)
            clicks.Add(new PointerClick(last.Time, samples[^1].Time, new SKPoint(last.X, last.Y)));
        return [.. clicks];
    }
}
