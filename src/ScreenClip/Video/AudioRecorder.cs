using System.Diagnostics;
using NAudio.CoreAudioApi;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace ScreenClip.Video;

/// <summary>
/// The sound of a recording: what the computer plays (WASAPI loopback) and the microphone, each optional, as 48 kHz
/// 16-bit stereo. A thread clocked on the recording's own time hands it out in 20 ms chunks, with their time: silence
/// where nothing plays (the loopback sends nothing then), nothing while paused. With both sounds, each chunk comes as
/// their mix (track 0) and each sound apart (tracks 1 and 2, in <see cref="Kinds"/> order), for the editor to set
/// their levels (step 3 of the video, the author's three tracks).
/// </summary>
internal sealed class AudioRecorder : IDisposable
{
    public const int Rate = 48000;
    private const int ChunkFrames = Rate / 50;
    // Read a little behind the clock, so that the packets of both devices have arrived.
    private static readonly TimeSpan Latency = TimeSpan.FromMilliseconds(120);
    private static readonly TimeSpan MaxBuffered = TimeSpan.FromMilliseconds(400);

    private sealed record Source(SoundKind Kind, IWaveIn? Capture, BufferedWaveProvider? Buffer, ISampleProvider Samples);

    private readonly List<Source> _sources = [];
    private readonly HashSet<SoundKind> _muted = [];
    private readonly Action<int, byte[], long> _write;
    private readonly Stopwatch _clock = new();
    private Thread? _thread;
    private volatile bool _stopping;
    private volatile bool _paused;
    private long _written;

    /// <param name="write">Gets each chunk of PCM, its track and its time in 100 ns units, on the audio thread.</param>
    public AudioRecorder(bool systemSound, bool microphone, Action<int, byte[], long> write)
    {
        _write = write;
#if DEBUG
        // SCREENCLIP_RECORD_FAKESOUND=1 (Debug only): two tones instead of the devices, so that tests record no one's
        // sound: 440 Hz for the computer, 660 Hz for the microphone.
        if (Environment.GetEnvironmentVariable("SCREENCLIP_RECORD_FAKESOUND") == "1")
        {
            if (systemSound)
                _sources.Add(new Source(SoundKind.Computer, null, null, new SignalGenerator(Rate, 2) { Frequency = 440, Gain = 0.2 }));
            if (microphone)
                _sources.Add(new Source(SoundKind.Microphone, null, null, new SignalGenerator(Rate, 2) { Frequency = 660, Gain = 0.2 }));
            return;
        }
#endif
        if (systemSound)
            TryAdd(SoundKind.Computer, () => new WasapiLoopbackCapture());
        if (microphone)
            TryAdd(SoundKind.Microphone, () => new WasapiCapture(Microphones.Open()) { ShareMode = AudioClientShareMode.Shared });
    }

    /// <summary>Whether there is any sound to record (a device may be missing).</summary>
    public bool HasSources => _sources.Count > 0;

    /// <summary>The sounds recorded apart, in their tracks' order (after the mix when there are two).</summary>
    public IReadOnlyList<SoundKind> Kinds => [.. _sources.Select(s => s.Kind)];

    /// <summary>The tracks written: the mix and each sound with two sounds, else the one sound.</summary>
    public int Tracks => _sources.Count > 1 ? 1 + _sources.Count : _sources.Count;

    private void TryAdd(SoundKind kind, Func<IWaveIn> create)
    {
        try
        {
            var capture = create();
            var buffer = new BufferedWaveProvider(capture.WaveFormat)
            {
                DiscardOnBufferOverflow = true,
                BufferDuration = TimeSpan.FromSeconds(3),
                ReadFully = true, // silence when nothing came
            };
            capture.DataAvailable += (_, e) => buffer.AddSamples(e.Buffer, 0, e.BytesRecorded);
            _sources.Add(new Source(kind, capture, buffer, ToStereo48k(buffer.ToSampleProvider())));
        }
        catch (Exception ex) when (ex is System.Runtime.InteropServices.COMException or InvalidOperationException or ArgumentException)
        {
            // No such device (no microphone, sound turned off): the recording goes on without it.
            Services.DevLog.Write($"recording: a sound device is missing: {ex.Message}");
        }
    }

    /// <summary>Any sound as 48 kHz float stereo (a mono microphone on both sides).</summary>
    public static ISampleProvider ToStereo48k(ISampleProvider samples)
    {
        samples = samples.WaveFormat.Channels switch
        {
            1 => new MonoToStereoSampleProvider(samples),
            2 => samples,
            _ => FirstTwoChannels(samples),
        };
        return samples.WaveFormat.SampleRate == Rate ? samples : new WdlResamplingSampleProvider(samples, Rate);
    }

    private static ISampleProvider FirstTwoChannels(ISampleProvider samples)
    {
        var two = new MultiplexingSampleProvider([samples], 2);
        two.ConnectInputToOutput(0, 0);
        two.ConnectInputToOutput(1, 1);
        return two;
    }

    /// <summary>A sound muted from now on (the recording's bar): silence in its track and out of the mix.</summary>
    public void SetMuted(SoundKind kind, bool muted)
    {
        lock (_muted)
        {
            if (muted)
                _muted.Add(kind);
            else
                _muted.Remove(kind);
        }
    }

    public void Start()
    {
        foreach (var source in _sources)
            source.Capture?.StartRecording();
        _clock.Start();
        _thread = new Thread(Run) { IsBackground = true, Name = "ScreenClip audio", Priority = ThreadPriority.AboveNormal };
        _thread.Start();
    }

    public void Pause()
    {
        _paused = true;
        _clock.Stop();
    }

    // What was played while paused is not in the recording.
    public void Resume()
    {
        foreach (var source in _sources)
            source.Buffer?.ClearBuffer();
        _paused = false;
        _clock.Start();
    }

    private void Run()
    {
        var each = _sources.Select(_ => new float[ChunkFrames * 2]).ToArray();
        var mix = new float[ChunkFrames * 2];
        while (!_stopping)
        {
            Thread.Sleep(10);
            if (_paused)
                continue;
            foreach (var source in _sources)
            {
                // A device that sent more than it should (a gap filled with silence before): drop the excess.
                if (source.Buffer is { } buffer && buffer.BufferedDuration > MaxBuffered)
                    buffer.ClearBuffer();
            }
            long due = (long)((_clock.Elapsed - Latency).TotalSeconds * Rate);
            while (!_stopping && due - _written >= ChunkFrames)
            {
                Array.Clear(mix);
                for (int s = 0; s < _sources.Count; s++)
                {
                    int read = _sources[s].Samples.Read(each[s], 0, each[s].Length);
                    Array.Clear(each[s], read, each[s].Length - read);
                    bool muted;
                    lock (_muted)
                        muted = _muted.Contains(_sources[s].Kind);
                    if (muted)
                        Array.Clear(each[s]);
                    for (int i = 0; i < mix.Length; i++)
                        mix[i] += each[s][i];
                }
                long time = _written * 10_000_000 / Rate;
                _write(0, ToPcm(mix), time);
                if (_sources.Count > 1)
                {
                    for (int s = 0; s < _sources.Count; s++)
                        _write(1 + s, ToPcm(each[s]), time);
                }
                _written += ChunkFrames;
            }
        }
    }

    private static byte[] ToPcm(float[] samples)
    {
        var pcm = new byte[samples.Length * 2];
        for (int i = 0; i < samples.Length; i++)
        {
            short value = (short)Math.Clamp(samples[i] * 32767f, short.MinValue, short.MaxValue);
            pcm[2 * i] = (byte)value;
            pcm[2 * i + 1] = (byte)(value >> 8);
        }
        return pcm;
    }

    public void Stop()
    {
        _stopping = true;
        _thread?.Join();
        foreach (var source in _sources)
        {
            try
            {
                source.Capture?.StopRecording();
            }
            catch (Exception ex) when (ex is System.Runtime.InteropServices.COMException or InvalidOperationException)
            {
            }
        }
    }

    public void Dispose()
    {
        if (!_stopping)
            Stop();
        foreach (var source in _sources)
            source.Capture?.Dispose();
        _sources.Clear();
    }
}
