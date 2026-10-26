using System.Collections.Immutable;
using SkiaSharp;

namespace ScreenClip.Core.Video;

/// <summary>A span of a video, in its own time: from <see cref="Start"/> to just before <see cref="End"/>.</summary>
public readonly record struct Piece(TimeSpan Start, TimeSpan End)
{
    public TimeSpan Length => End - Start;

    public bool Contains(TimeSpan time) => time >= Start && time < End;
}

/// <summary>
/// What is done to a video beyond its picture: where it is split, the pieces removed, and its sounds. The pieces
/// removed stay in place on the timeline (greyed, as the author chose), so that a time is always the video's own: the
/// annotations keep theirs, and playing and exporting skip what is removed. Immutable, kept with the annotations so
/// that it is undone as they are.
/// </summary>
public sealed record VideoEdit
{
    /// <summary>Closer than this to a split or an end, a new split is refused (a piece too short to see or grab).</summary>
    public static readonly TimeSpan MinPiece = TimeSpan.FromMilliseconds(200);

    /// <summary>Where the video is split, in order.</summary>
    public ImmutableList<TimeSpan> Splits { get; init; } = [];

    /// <summary>The pieces removed, as they were when removed (from a split or an end to the next).</summary>
    public ImmutableList<Piece> Removed { get; init; } = [];

    /// <summary>The options of each sound of the video, in the order of its sound rows (missing: the defaults).</summary>
    public ImmutableList<SoundOptions> Sounds { get; init; } = [];

    /// <summary>The pieces zoomed in, each on its own part of the picture (asked by the author), in order.</summary>
    public ImmutableList<Framing> Framings { get; init; } = [];

    /// <summary>How the picture is framed at <paramref name="time"/> (null: all of it).</summary>
    public Framing? FramingAt(TimeSpan time)
    {
        foreach (var framing in Framings)
        {
            if (framing.Range.Contains(time))
                return framing;
        }
        return null;
    }

    /// <summary>
    /// <paramref name="piece"/> framed at <paramref name="zoom"/> around (<paramref name="x"/>, <paramref name="y"/>),
    /// or as it is at 1: the framings there before are cut around it, the others stay.
    /// </summary>
    public VideoEdit WithFraming(Piece piece, double zoom, double x, double y)
    {
        var kept = new List<Framing>();
        foreach (var framing in Framings)
        {
            var range = framing.Range;
            if (range.End <= piece.Start || range.Start >= piece.End)
            {
                kept.Add(framing);
                continue;
            }
            if (range.Start < piece.Start)
                kept.Add(framing with { Range = range with { End = piece.Start } });
            if (range.End > piece.End)
                kept.Add(framing with { Range = range with { Start = piece.End } });
        }
        var framed = new Framing(piece, zoom, x, y).Normalized();
        if (framed.IsZoomed)
            kept.Add(framed);
        return this with { Framings = [.. kept.OrderBy(f => f.Range.Start)] };
    }

    public SoundOptions Sound(int index) => index >= 0 && index < Sounds.Count ? Sounds[index] : new SoundOptions();

    public VideoEdit WithSound(int index, SoundOptions options)
    {
        var sounds = Sounds;
        while (sounds.Count <= index)
            sounds = sounds.Add(new SoundOptions());
        return this with { Sounds = sounds.SetItem(index, options) };
    }

    /// <summary>The video split at <paramref name="time"/>; unchanged when too close to a split or an end.</summary>
    public VideoEdit SplitAt(TimeSpan time, TimeSpan duration)
    {
        if (time < MinPiece || time > duration - MinPiece || Splits.Any(s => (s - time).Duration() < MinPiece))
            return this;
        return this with { Splits = [.. Splits.Add(time).Sort()] };
    }

    /// <summary>The piece holding <paramref name="time"/>: from the split before it (or the start) to the next (or the end).</summary>
    public Piece PieceAt(TimeSpan time, TimeSpan duration)
    {
        var start = TimeSpan.Zero;
        var end = duration;
        foreach (var split in Splits)
        {
            if (split <= time)
                start = split;
            else
            {
                end = split;
                break;
            }
        }
        return new Piece(start, end);
    }

    /// <summary>The pieces between the splits, in order.</summary>
    public IReadOnlyList<Piece> Pieces(TimeSpan duration)
    {
        var pieces = new List<Piece>();
        var start = TimeSpan.Zero;
        foreach (var split in Splits.Where(s => s > TimeSpan.Zero && s < duration))
        {
            pieces.Add(new Piece(start, split));
            start = split;
        }
        pieces.Add(new Piece(start, duration));
        return pieces;
    }

    public bool IsRemoved(TimeSpan time) => Removed.Any(r => r.Contains(time));

    /// <summary>
    /// The piece at <paramref name="time"/> removed; unchanged when it already is, or when nothing of the part kept
    /// (<paramref name="trim"/>) would be left.
    /// </summary>
    public VideoEdit RemoveAt(TimeSpan time, TrimRange trim, TimeSpan duration)
    {
        if (IsRemoved(time))
            return this;
        var piece = PieceAt(time, duration);
        var removed = this with { Removed = [.. Removed.Add(piece).OrderBy(p => p.Start)] };
        return removed.KeptLength(trim) <= TimeSpan.Zero ? this : removed;
    }

    /// <summary>The removed piece at <paramref name="time"/> put back.</summary>
    public VideoEdit RestoreAt(TimeSpan time) =>
        this with { Removed = Removed.RemoveAll(r => r.Contains(time)) };

    /// <summary>What plays and is exported: the part kept between the handles, less the pieces removed, in order.</summary>
    public IReadOnlyList<Piece> Kept(TrimRange trim)
    {
        var kept = new List<Piece>();
        var cursor = trim.Start;
        foreach (var removed in Removed.OrderBy(r => r.Start))
        {
            if (removed.End <= cursor)
                continue;
            if (removed.Start >= trim.End)
                break;
            if (removed.Start > cursor)
                kept.Add(new Piece(cursor, removed.Start));
            cursor = removed.End > cursor ? removed.End : cursor;
            if (cursor >= trim.End)
                break;
        }
        if (cursor < trim.End)
            kept.Add(new Piece(cursor, trim.End));
        return kept;
    }

    public TimeSpan KeptLength(TrimRange trim) => Kept(trim).Aggregate(TimeSpan.Zero, (sum, p) => sum + p.Length);

    /// <summary>
    /// Where a moment of the video is in what is kept (its time in the export); a moment removed or cut off counts as
    /// the start of the next piece kept.
    /// </summary>
    public TimeSpan KeptTimeOf(TimeSpan time, TrimRange trim)
    {
        var before = TimeSpan.Zero;
        foreach (var piece in Kept(trim))
        {
            if (time < piece.Start)
                return before;
            if (time < piece.End)
                return before + (time - piece.Start);
            before += piece.Length;
        }
        return before;
    }

    /// <summary>The moment of the video at <paramref name="kept"/> into what is kept.</summary>
    public TimeSpan VideoTimeOf(TimeSpan kept, TrimRange trim)
    {
        var pieces = Kept(trim);
        foreach (var piece in pieces)
        {
            if (kept < piece.Length)
                return piece.Start + (kept < TimeSpan.Zero ? TimeSpan.Zero : kept);
            kept -= piece.Length;
        }
        return pieces.Count > 0 ? pieces[^1].End : trim.End;
    }

    /// <summary>The parts kept from <paramref name="from"/> on (where playing from there goes through).</summary>
    public IReadOnlyList<Piece> KeptFrom(TimeSpan from, TrimRange trim) =>
        [.. Kept(trim).Where(p => p.End > from).Select(p => p.Start < from ? p with { Start = from } : p)];
}

/// <summary>
/// One sound of a video (the computer's, the microphone, or its one sound): its volume, muted or not, the part of the
/// video it plays in (silence outside), and how it fades in and out there.
/// </summary>
public sealed record SoundOptions
{
    public const int MaxVolume = 200;
    public static readonly TimeSpan MaxFade = TimeSpan.FromSeconds(5);

    /// <summary>In percent: 100 as recorded, up to <see cref="MaxVolume"/>.</summary>
    public int Volume { get; init; } = 100;

    public bool Muted { get; init; }

    /// <summary>The pieces where it is silent, the picture kept (asked by the author: cut the sound alone).</summary>
    public ImmutableList<Piece> Silenced { get; init; } = [];

    /// <summary>Where it starts playing; null: from the start of the video.</summary>
    public TimeSpan? Start { get; init; }

    /// <summary>Where it stops; null: at the end of the video.</summary>
    public TimeSpan? End { get; init; }

    public TimeSpan FadeIn { get; init; }

    public TimeSpan FadeOut { get; init; }

    public bool IsDefault => Volume == 100 && !Muted && Silenced.IsEmpty && Start is null && End is null
        && FadeIn == TimeSpan.Zero && FadeOut == TimeSpan.Zero;

    /// <summary>A few milliseconds to go silent and back at the ends of a piece silenced (no click).</summary>
    public static readonly TimeSpan SilenceRamp = TimeSpan.FromMilliseconds(5);

    public bool IsSilencedAt(TimeSpan time) => Silenced.Any(p => p.Contains(time));

    /// <summary>Silent in <paramref name="piece"/>, or heard there again (what was silenced across it is put back).</summary>
    public SoundOptions WithSilenced(Piece piece, bool silent)
    {
        var others = Silenced.RemoveAll(p => p.Start < piece.End && p.End > piece.Start);
        return this with { Silenced = silent ? [.. others.Add(piece).OrderBy(p => p.Start)] : others };
    }

    /// <summary>The part of a video of <paramref name="duration"/> it plays in.</summary>
    public Piece Span(TimeSpan duration)
    {
        var start = Start is { } s ? Clamp(s, TimeSpan.Zero, duration) : TimeSpan.Zero;
        var end = End is { } e ? Clamp(e, start, duration) : duration;
        return new Piece(start, end);
    }

    /// <summary>How loud it is at <paramref name="time"/> (1: as recorded): volume, fades, silence outside its part.</summary>
    public double GainAt(TimeSpan time, TimeSpan duration)
    {
        if (Muted || Volume <= 0)
            return 0;
        var span = Span(duration);
        if (!span.Contains(time))
            return 0;
        double gain = Volume / 100.0;
        foreach (var silent in Silenced)
        {
            if (silent.Contains(time))
                return 0;
            var away = time < silent.Start ? silent.Start - time : time - silent.End;
            if (away < SilenceRamp)
                gain *= away / SilenceRamp;
        }
        if (FadeIn > TimeSpan.Zero)
            gain *= Math.Min(1, (time - span.Start).TotalSeconds / FadeIn.TotalSeconds);
        if (FadeOut > TimeSpan.Zero)
            gain *= Math.Min(1, (span.End - time).TotalSeconds / FadeOut.TotalSeconds);
        return gain;
    }

    /// <summary>Values back in range; a part that covers the whole video is all of it again (null ends).</summary>
    public SoundOptions Normalized(TimeSpan duration)
    {
        var span = Span(duration);
        return this with
        {
            Volume = Math.Clamp(Volume, 0, MaxVolume),
            Start = span.Start <= TimeSpan.Zero ? null : span.Start,
            End = span.End >= duration ? null : span.End,
            FadeIn = Clamp(FadeIn, TimeSpan.Zero, MaxFade),
            FadeOut = Clamp(FadeOut, TimeSpan.Zero, MaxFade),
        };
    }

    /// <summary>Its start moved to <paramref name="time"/>, at least <see cref="VideoEdit.MinPiece"/> before its end.</summary>
    public SoundOptions WithStart(TimeSpan time, TimeSpan duration)
    {
        var span = Span(duration);
        return (this with { Start = Clamp(time, TimeSpan.Zero, span.End - VideoEdit.MinPiece) }).Normalized(duration);
    }

    /// <summary>Its end moved to <paramref name="time"/>, at least <see cref="VideoEdit.MinPiece"/> after its start.</summary>
    public SoundOptions WithEnd(TimeSpan time, TimeSpan duration)
    {
        var span = Span(duration);
        return (this with { End = Clamp(time, span.Start + VideoEdit.MinPiece, duration) }).Normalized(duration);
    }

    private static TimeSpan Clamp(TimeSpan value, TimeSpan low, TimeSpan high) =>
        high < low ? low : value < low ? low : value > high ? high : value;
}

/// <summary>
/// A piece of video zoomed in: how much (1 to <see cref="MaxZoom"/>) and around which point of the picture (0 to 1 across
/// it, after the crop). The picture keeps its size: the part shown fills it.
/// </summary>
public readonly record struct Framing(Piece Range, double Zoom, double X, double Y)
{
    public const double MaxZoom = 4;

    public bool IsZoomed => Zoom > 1.001;

    public Framing Normalized() => this with { Zoom = Math.Clamp(Zoom, 1, MaxZoom), X = Math.Clamp(X, 0, 1), Y = Math.Clamp(Y, 0, 1) };

    /// <summary>The part of a <paramref name="width"/> × <paramref name="height"/> picture shown, kept inside it.</summary>
    public SKRect Region(float width, float height)
    {
        double zoom = Math.Clamp(Zoom, 1, MaxZoom);
        float w = (float)(width / zoom), h = (float)(height / zoom);
        float x = Math.Clamp((float)(X * width), w / 2, width - w / 2), y = Math.Clamp((float)(Y * height), h / 2, height - h / 2);
        return SKRect.Create(x - w / 2, y - h / 2, w, h);
    }

    /// <summary>The center that shows <paramref name="region"/>'s middle (a frame moved by hand).</summary>
    public Framing CenteredOn(SKRect region, float width, float height) =>
        (this with { X = region.MidX / Math.Max(1, width), Y = region.MidY / Math.Max(1, height) }).Normalized();

    /// <summary>The part shown, drawn back at the picture's size (a new picture, the caller's).</summary>
    public SKImage Apply(SKImage picture)
    {
        var region = Region(picture.Width, picture.Height);
        using var surface = SKSurface.Create(new SKImageInfo(picture.Width, picture.Height, SKColorType.Bgra8888, SKAlphaType.Premul));
        surface.Canvas.Clear(SKColors.Transparent);
        surface.Canvas.DrawImage(picture, region, SKRect.Create(picture.Width, picture.Height), new SKSamplingOptions(SKCubicResampler.Mitchell));
        return surface.Snapshot();
    }
}
