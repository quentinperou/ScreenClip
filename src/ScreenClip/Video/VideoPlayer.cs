using System.Collections.Concurrent;
using System.Diagnostics;
using System.Windows.Threading;
using NAudio.CoreAudioApi;
using NAudio.Wave;
using SkiaSharp;

namespace ScreenClip.Video;

/// <summary>
/// Plays a part of a video in the editor: frames decoded ahead on a thread, handed to the editor when they are due
/// (it draws them with their annotations and layout), and the sound through the speakers. The sound is the clock when
/// there is some, so that picture and sound stay together.
/// </summary>
internal sealed class VideoPlayer : IDisposable
{
    private readonly string _path;
    private readonly TimeSpan _from, _to;
    private readonly Action<SKImage?, TimeSpan> _show;
    private readonly Action _ended;
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

    /// <param name="show">
    /// Gets each frame due (the picture is then the editor's) and the time, on the UI thread; about sixty times a second
    /// without a frame too, for what is drawn over it (a pointer moves while the screen stays still).
    /// </param>
    /// <param name="ended">The end of the part was reached.</param>
    public VideoPlayer(string path, TimeSpan from, TimeSpan to, bool sound, Action<SKImage?, TimeSpan> show, Action ended)
    {
        _path = path;
        _from = from;
        _to = to;
        _show = show;
        _ended = ended;
        if (sound)
            _sound = new BufferedWaveProvider(new WaveFormat(48000, 16, 2)) { BufferDuration = TimeSpan.FromSeconds(4), ReadFully = true };
        _timer.Tick += (_, _) => Tick();
    }

    /// <summary>Where the playing is.</summary>
    public TimeSpan Position
    {
        get
        {
            if (_output is { } output && output.PlaybackState == PlaybackState.Playing)
                return _from + TimeSpan.FromSeconds(output.GetPosition() / (double)output.OutputWaveFormat.AverageBytesPerSecond);
            return _from + _clock.Elapsed;
        }
    }

    public void Start()
    {
        var cancel = _cancel.Token;
        _decoder = new Thread(() => Decode(cancel)) { IsBackground = true, Name = "ScreenClip video player" };
        _decoder.Start();
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

    private void Decode(CancellationToken cancel)
    {
        try
        {
            using var reader = new VideoReader(_path);
            reader.Seek(_from);
            (TimeSpan Time, SKImage Image)? before = null;
            while (!cancel.IsCancellationRequested && reader.Next() is { } frame)
            {
                if (frame.Time < _from)
                {
                    // The frame shown at the start may begin before it.
                    before?.Image.Dispose();
                    before = frame;
                    continue;
                }
                if (before is { } first)
                {
                    _frames.Add((_from, first.Image), cancel);
                    before = null;
                }
                if (frame.Time >= _to)
                {
                    frame.Image.Dispose();
                    break;
                }
                _frames.Add(frame, cancel);
            }
            if (before is { } only)
                _frames.Add((_from, only.Image), cancel);
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

    private void Feed(CancellationToken cancel)
    {
        try
        {
            using var reader = new AudioReader(_path);
            reader.Seek(_from);
            while (!cancel.IsCancellationRequested && reader.Next() is { } block)
            {
                var (bytes, offset, count) = Trim(block.Time, block.Pcm, _from, _to);
                if (block.Time >= _to)
                    break;
                if (count == 0)
                    continue;
                while (!cancel.IsCancellationRequested && _sound!.BufferedDuration > TimeSpan.FromSeconds(2))
                    Thread.Sleep(20);
                _sound!.AddSamples(bytes, offset, count);
            }
        }
        catch (SharpGen.Runtime.SharpGenException)
        {
        }
    }

    /// <summary>The bytes of a block of sound at <paramref name="time"/> that fall between <paramref name="from"/> and <paramref name="to"/>.</summary>
    public static (byte[] Bytes, int Offset, int Count) Trim(TimeSpan time, byte[] pcm, TimeSpan from, TimeSpan to)
    {
        const int Frame = 4, Rate = 48000;
        int frames = pcm.Length / Frame;
        int skip = time < from ? (int)Math.Min(frames, Math.Round((from - time).TotalSeconds * Rate)) : 0;
        int keep = (int)Math.Clamp(Math.Round((to - time).TotalSeconds * Rate), 0, frames);
        return (pcm, skip * Frame, Math.Max(0, keep - skip) * Frame);
    }

    private void Tick()
    {
        if (_stopped)
            return;
        var now = Position;
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
            _show(latest, now < latestTime ? latestTime : now);
            _lastShown = now;
        }
        if (now >= _to || (_frames.IsCompleted && _pending is null && (_output is null || _sound!.BufferedDuration <= TimeSpan.Zero || now >= _to)))
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
