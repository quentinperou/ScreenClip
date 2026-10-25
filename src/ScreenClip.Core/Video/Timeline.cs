namespace ScreenClip.Core.Video;

/// <summary>Times of a video as the editor shows them.</summary>
public static class Timecode
{
    /// <summary>
    /// “0:04.2” (tenths of a second), “1:02:04.2” past an hour; <paramref name="separator"/> is the decimal separator of
    /// the UI language.
    /// </summary>
    public static string Format(TimeSpan time, string separator = ".", bool tenths = true)
    {
        if (time < TimeSpan.Zero)
            time = TimeSpan.Zero;
        long totalTenths = (long)Math.Floor(time.TotalSeconds * 10 + 1e-6);
        long seconds = totalTenths / 10, tenth = totalTenths % 10;
        long hours = seconds / 3600, minutes = seconds / 60 % 60, rest = seconds % 60;
        string main = hours > 0 ? $"{hours}:{minutes:00}:{rest:00}" : $"{minutes}:{rest:00}";
        return tenths ? $"{main}{separator}{tenth}" : main;
    }
}

/// <summary>The part of a video kept: its start and its end, at least <see cref="MinLength"/> apart.</summary>
public readonly record struct TrimRange(TimeSpan Start, TimeSpan End)
{
    public static readonly TimeSpan MinLength = TimeSpan.FromMilliseconds(200);

    public TimeSpan Length => End - Start;

    public static TrimRange All(TimeSpan duration) => new(TimeSpan.Zero, duration);

    /// <summary>The start moved to <paramref name="time"/>, never past the end (less the shortest length).</summary>
    public TrimRange WithStart(TimeSpan time) =>
        this with { Start = Clamp(time, TimeSpan.Zero, End - MinLength) };

    /// <summary>The end moved to <paramref name="time"/>, never before the start, nor after <paramref name="duration"/>.</summary>
    public TrimRange WithEnd(TimeSpan time, TimeSpan duration) =>
        this with { End = Clamp(time, Start + MinLength, duration) };

    public bool IsAll(TimeSpan duration) => Start <= TimeSpan.Zero && End >= duration;

    public TimeSpan Clamp(TimeSpan time) => Clamp(time, Start, End);

    private static TimeSpan Clamp(TimeSpan time, TimeSpan low, TimeSpan high) =>
        high < low ? low : time < low ? low : time > high ? high : time;
}

/// <summary>
/// Which frames of a video a GIF keeps (at most so many a second), and how long each one shows, frame after frame as
/// the video is read. The lengths are in hundredths of a second (the unit of GIF) and add up to the whole length,
/// rounding included.
/// </summary>
public sealed class GifPacer(int fps)
{
    private readonly double _step = 1.0 / Math.Max(1, fps);
    private double _nextDue = double.MinValue;
    private double? _origin;
    private long _shown;

    /// <summary>Whether the frame at <paramref name="time"/> is kept.</summary>
    public bool Keep(TimeSpan time)
    {
        double t = time.TotalSeconds;
        if (t + 1e-6 < _nextDue)
            return false;
        _origin ??= t;
        _nextDue = Math.Max(_nextDue, t) + _step;
        return true;
    }

    /// <summary>The length of the frame kept last, shown until <paramref name="until"/> (the next kept one, or the end).</summary>
    public int Close(TimeSpan until)
    {
        long target = (long)Math.Round((until.TotalSeconds - (_origin ?? 0)) * 100);
        int delay = (int)Math.Max(1, target - _shown);
        _shown += delay;
        return delay;
    }
}

/// <summary>Which frames a GIF keeps, and how long each one shows, for frames known in advance.</summary>
public static class GifTiming
{
    /// <summary>
    /// From frames at <paramref name="times"/> (in order, the video's own, often uneven) ending at <paramref name="end"/>:
    /// the ones kept, at most <paramref name="fps"/> a second, each with its length in hundredths of a second.
    /// </summary>
    public static List<(int Index, int Delay)> Frames(IReadOnlyList<TimeSpan> times, TimeSpan end, int fps)
    {
        var pacer = new GifPacer(fps);
        var kept = new List<int>();
        for (int i = 0; i < times.Count; i++)
        {
            if (pacer.Keep(times[i]))
                kept.Add(i);
        }
        var result = new List<(int, int)>(kept.Count);
        for (int k = 0; k < kept.Count; k++)
            result.Add((kept[k], pacer.Close(k + 1 < kept.Count ? times[kept[k + 1]] : end)));
        return result;
    }
}
