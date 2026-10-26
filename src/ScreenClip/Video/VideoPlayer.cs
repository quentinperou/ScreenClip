using System.Collections.Concurrent;
using System.Diagnostics;
using System.Windows.Threading;
using NAudio.CoreAudioApi;
using NAudio.Wave;
using ScreenClip.Core.Video;
using SkiaSharp;

namespace ScreenClip.Video;

/// <summary>
/// Plays a video in the editor, through the pieces kept one after the other (what is removed is skipped): frames
/// decoded ahead on a thread, handed to the editor when they are due (it draws them with their annotations and
/// layout), and the sound through the speakers, as the editor mixes it (<see cref="SoundComposer"/>). The sound is the
/// clock when there is some, so that picture and sound stay together. A still picture animated has no frames: only
/// the time goes on, for what is drawn over it.
/// </summary>
internal sealed class VideoPlayer : IDisposable
{
    private readonly string? _path;
    private readonly IReadOnlyList<Piece> _pieces;
    private readonly TimeSpan _length;
    private readonly Action<SKImage?, TimeSpan> _show;
    private readonly Action _ended;
    private readonly Func<SoundComposer?>? _composer;
    // The frames with their time in what plays (from its start).
    private readonly BlockingCollection<(TimeSpan Time, SKImage Image)> _frames = new(boundedCapacity: 6);
    private readonly CancellationTokenSource _cancel = new();
    private readonly Stopwatch _clock = new();
    private readonly DispatcherTimer _timer = new(DispatcherPriority.Render) { Interval = TimeSpan.FromMilliseconds(10) };
    private readonly BufferedWaveProvider? _sound;
    private WasapiOut? _output;
    private Thread? _decoder, _feeder;
    private (TimeSpan Time, SKImage Image)? _pending;
    private TimeSpan? _lastShown;
    private bool _stopped;

    /// <param name="pieces">What plays, in order, in the video's own times.</param>
    /// <param name="sound">Makes the sound (null: none).</param>
    /// <param name="show">
    /// Gets each frame due (the picture is then the editor's) and the video's time, on the UI thread; about sixty times
    /// a second without a frame too, for what is drawn over it (a pointer moves while the screen stays still).
    /// </param>
    /// <param name="ended">The end was reached.</param>
    public VideoPlayer(string? path, IReadOnlyList<Piece> pieces, Func<SoundComposer?>? sound, Action<SKImage?, TimeSpan> show, Action ended)
    {
        _path = path;
        _pieces = pieces;
        _length = pieces.Aggregate(TimeSpan.Zero, (sum, p) => sum + p.Length);
        _show = show;
        _ended = ended;
        _composer = sound;
        if (sound is not null)
            _sound = new BufferedWaveProvider(new WaveFormat(SoundComposer.Rate, 16, 2)) { BufferDuration = TimeSpan.FromSeconds(4), ReadFully = true };
        _timer.Tick += (_, _) => Tick();
    }

    /// <summary>Where the playing is, in the video's own time.</summary>
    public TimeSpan Position => VideoTimeOf(Elapsed);

    // Where the playing is in what plays.
    private TimeSpan Elapsed
    {
        get
        {
            if (_output is { } output && output.PlaybackState == PlaybackState.Playing)
                return TimeSpan.FromSeconds(output.GetPosition() / (double)output.OutputWaveFormat.AverageBytesPerSecond);
            return _clock.Elapsed;
        }
    }

    private TimeSpan VideoTimeOf(TimeSpan elapsed)
    {
        foreach (var piece in _pieces)
        {
            if (elapsed < piece.Length)
                return piece.Start + elapsed;
            elapsed -= piece.Length;
        }
        return _pieces.Count > 0 ? _pieces[^1].End : TimeSpan.Zero;
    }

    public void Start()
    {
        var cancel = _cancel.Token;
        if (_path is { } path)
        {
            _decoder = new Thread(() => Decode(path, cancel)) { IsBackground = true, Name = "ScreenClip video player" };
            _decoder.Start();
        }
        else
            _frames.CompleteAdding();
        if (_sound is not null)
        {
            _feeder = new Thread(() => Feed(cancel)) { IsBackground = true, Name = "ScreenClip sound player" };
            _feeder.Start();
            try
            {
                _output = new WasapiOut(AudioClientShareMode.Shared, 80);
                _output.Init(_sound);
            }
            catch (Exception ex) when (ex is System.Runtime.InteropServices.COMException or InvalidOperationException or ArgumentException)
            {
                // No speakers: the picture plays alone, on the clock.
                _output?.Dispose();
                _output = null;
            }
        }
        // A short head start for the first frames and sound.
        Thread.Sleep(60);
        _output?.Play();
        _clock.Start();
        _timer.Start();
    }

    private void Decode(string path, CancellationToken cancel)
    {
        try
        {
            using var reader = new VideoReader(path);
            var before = TimeSpan.Zero;
            foreach (var piece in _pieces)
            {
                foreach (var frame in FramesOf(reader, piece, cancel))
                    _frames.Add((before + frame.Time, frame.Image), cancel);
                before += piece.Length;
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (SharpGen.Runtime.SharpGenException)
        {
            // A file that cannot be read further: the playing stops where it is.
        }
        finally
        {
            _frames.CompleteAdding();
        }
    }

    /// <summary>
    /// The frames of a piece, with their time from its start: the one shown at its start first (it may begin before),
    /// then the others up to its end. The pictures are the caller's.
    /// </summary>
    public static IEnumerable<(TimeSpan Time, SKImage Image)> FramesOf(VideoReader reader, Piece piece, CancellationToken cancel)
    {
        reader.Seek(piece.Start);
        (TimeSpan Time, SKImage Image)? before = null;
        try
        {
            while (reader.Next() is { } frame)
            {
                cancel.ThrowIfCancellationRequested();
                if (frame.Time < piece.Start)
                {
                    before?.Image.Dispose();
                    before = frame;
                    continue;
                }
                if (before is { } first && frame.Time > piece.Start)
                {
                    before = null;
                    yield return (TimeSpan.Zero, first.Image);
                }
                if (frame.Time >= piece.End)
                {
                    frame.Image.Dispose();
                    yield break;
                }
                yield return (frame.Time - piece.Start, frame.Image);
            }
            if (before is { } only)
            {
                before = null;
                yield return (TimeSpan.Zero, only.Image);
            }
        }
        finally
        {
            before?.Image.Dispose();
        }
    }

    private void Feed(CancellationToken cancel)
    {
        try
        {
            using var composer = _composer!();
            if (composer is null)
                return;
            while (!cancel.IsCancellationRequested && composer.Next() is { } chunk)
            {
                while (!cancel.IsCancellationRequested && _sound!.BufferedDuration > TimeSpan.FromSeconds(2))
                    Thread.Sleep(20);
                _sound!.AddSamples(chunk.Pcm, 0, chunk.Pcm.Length);
            }
        }
        catch (Exception ex) when (ex is SharpGen.Runtime.SharpGenException or InvalidDataException)
        {
        }
    }

    private void Tick()
    {
        if (_stopped)
            return;
        var now = Elapsed;
        SKImage? latest = null;
        TimeSpan latestTime = default;
        while (true)
        {
            if (_pending is null)
            {
                if (!_frames.TryTake(out var item))
                    break;
                _pending = item;
            }
            if (_pending.Value.Time > now)
                break;
            latest?.Dispose();
            (latestTime, latest) = _pending.Value;
            _pending = null;
        }
        if (latest is not null || _lastShown is not { } last || now - last >= TimeSpan.FromMilliseconds(15))
        {
            _show(latest, VideoTimeOf(now < latestTime ? latestTime : now));
            _lastShown = now;
        }
        if (now >= _length || (_path is not null && _frames.IsCompleted && _pending is null && (_output is null || _sound!.BufferedDuration <= TimeSpan.Zero)))
        {
            Stop();
            _ended();
        }
    }

    public void Stop()
    {
        if (_stopped)
            return;
        _stopped = true;
        _timer.Stop();
        _cancel.Cancel();
        _output?.Stop();
        _decoder?.Join(1000);
        _feeder?.Join(1000);
    }

    public void Dispose()
    {
        Stop();
        _output?.Dispose();
        _output = null;
        _pending?.Image.Dispose();
        while (_frames.TryTake(out var item))
            item.Image.Dispose();
        _frames.Dispose();
        _cancel.Dispose();
    }
}
