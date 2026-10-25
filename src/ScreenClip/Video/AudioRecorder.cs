using System.Diagnostics;
using NAudio.CoreAudioApi;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace ScreenClip.Video;

/// <summary>
/// The sound of a recording: what the computer plays (WASAPI loopback) and the microphone, each optional, mixed into
/// 48 kHz 16-bit stereo. A thread clocked on the recording's own time hands it out in 20 ms chunks, with their time:
/// silence where nothing plays (the loopback sends nothing then), nothing while paused.
/// </summary>
internal sealed class AudioRecorder : IDisposable
{
    public const int Rate = 48000;
    private const int ChunkFrames = Rate / 50;
    // Read a little behind the clock, so that the packets of both devices have arrived.
    private static readonly TimeSpan Latency = TimeSpan.FromMilliseconds(120);
    private static readonly TimeSpan MaxBuffered = TimeSpan.FromMilliseconds(400);

    private readonly List<(IWaveIn Capture, BufferedWaveProvider Buffer)> _sources = [];
    private readonly MixingSampleProvider _mixer = new(WaveFormat.CreateIeeeFloatWaveFormat(Rate, 2)) { ReadFully = true };
    private readonly Action<byte[], long> _write;
    private readonly Stopwatch _clock = new();
    private Thread? _thread;
    private volatile bool _stopping;
    private volatile bool _paused;
    private long _written;

    /// <param name="write">Gets each chunk of PCM and its time in 100 ns units, on the audio thread.</param>
    public AudioRecorder(bool systemSound, bool microphone, Action<byte[], long> write)
    {
        _write = write;
        if (systemSound)
            TryAdd(() => new WasapiLoopbackCapture());
        if (microphone)
            TryAdd(() => new WasapiCapture(new MMDeviceEnumerator().GetDefaultAudioEndpoint(DataFlow.Capture, Role.Communications)) { ShareMode = AudioClientShareMode.Shared });
    }

    /// <summary>Whether there is any sound to record (a device may be missing).</summary>
    public bool HasSources => _sources.Count > 0;

    private void TryAdd(Func<IWaveIn> create)
    {
        try
        {
            var capture = create();
            var buffer = new BufferedWaveProvider(capture.WaveFormat)
            {
                DiscardOnBufferOverflow = true,
                BufferDuration = TimeSpan.FromSeconds(3),
                ReadFully = true, // silence when nothing came: the mixer keeps it
            };
            capture.DataAvailable += (_, e) => buffer.AddSamples(e.Buffer, 0, e.BytesRecorded);
            ISampleProvider samples = buffer.ToSampleProvider();
            samples = samples.WaveFormat.Channels switch
            {
                1 => new MonoToStereoSampleProvider(samples),
                2 => samples,
                _ => FirstTwoChannels(samples),
            };
            if (samples.WaveFormat.SampleRate != Rate)
                samples = new WdlResamplingSampleProvider(samples, Rate);
            _mixer.AddMixerInput(samples);
            _sources.Add((capture, buffer));
        }
        catch (Exception ex) when (ex is System.Runtime.InteropServices.COMException or InvalidOperationException or ArgumentException)
        {
            // No such device (no microphone, sound turned off): the recording goes on without it.
            Services.DevLog.Write($"recording: a sound device is missing: {ex.Message}");
        }
    }

    private static ISampleProvider FirstTwoChannels(ISampleProvider samples)
    {
        var two = new MultiplexingSampleProvider([samples], 2);
        two.ConnectInputToOutput(0, 0);
        two.ConnectInputToOutput(1, 1);
        return two;
    }

    public void Start()
    {
        foreach (var (capture, _) in _sources)
            capture.StartRecording();
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
        foreach (var (_, buffer) in _sources)
            buffer.ClearBuffer();
        _paused = false;
        _clock.Start();
    }

    private void Run()
    {
        var floats = new float[ChunkFrames * 2];
        while (!_stopping)
        {
            Thread.Sleep(10);
            if (_paused)
                continue;
            foreach (var (_, buffer) in _sources)
            {
                // A device that sent more than it should (a gap filled with silence before): drop the excess.
                if (buffer.BufferedDuration > MaxBuffered)
                    buffer.ClearBuffer();
            }
            long due = (long)((_clock.Elapsed - Latency).TotalSeconds * Rate);
            while (!_stopping && due - _written >= ChunkFrames)
            {
                _mixer.Read(floats, 0, floats.Length);
                var pcm = new byte[ChunkFrames * 4];
                for (int i = 0; i < floats.Length; i++)
                {
                    short value = (short)Math.Clamp(floats[i] * 32767f, short.MinValue, short.MaxValue);
                    pcm[2 * i] = (byte)value;
                    pcm[2 * i + 1] = (byte)(value >> 8);
                }
                _write(pcm, _written * 10_000_000 / Rate);
                _written += ChunkFrames;
            }
        }
    }

    public void Stop()
    {
        _stopping = true;
        _thread?.Join();
        foreach (var (capture, _) in _sources)
        {
            try
            {
                capture.StopRecording();
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
        foreach (var (capture, _) in _sources)
            capture.Dispose();
        _sources.Clear();
    }
}
