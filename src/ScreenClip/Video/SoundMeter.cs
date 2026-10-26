using NAudio.CoreAudioApi;
using NAudio.Wave;

namespace ScreenClip.Video;

/// <summary>
/// How loud the computer or the microphone is now, for the capture bar's meters (step 3 of the video: to see that the
/// microphone hears before recording). It listens only while the bar shows it in video mode, never at rest.
/// </summary>
internal sealed class SoundMeter : IDisposable
{
    private readonly IWaveIn? _capture;
    private readonly System.Diagnostics.Stopwatch? _fake;
    private float _peak;

    public SoundMeter(SoundKind kind)
    {
#if DEBUG
        // SCREENCLIP_RECORD_FAKESOUND=1: a made-up level, so that tests listen to no one.
        if (Environment.GetEnvironmentVariable("SCREENCLIP_RECORD_FAKESOUND") == "1")
        {
            _fake = System.Diagnostics.Stopwatch.StartNew();
            return;
        }
#endif
        try
        {
            _capture = kind == SoundKind.Microphone
                ? new WasapiCapture(Microphones.Open()) { ShareMode = AudioClientShareMode.Shared }
                : new WasapiLoopbackCapture();
            _capture.DataAvailable += (_, e) => Measure(e.Buffer, e.BytesRecorded, _capture.WaveFormat);
            _capture.StartRecording();
        }
        catch (Exception ex) when (ex is System.Runtime.InteropServices.COMException or InvalidOperationException or ArgumentException)
        {
            // No such device: the meter stays empty.
            _capture?.Dispose();
            _capture = null;
        }
    }

    /// <summary>Whether there is a device to listen to.</summary>
    public bool Works => _capture is not null || _fake is not null;

    /// <summary>The loudest level since the last call (0 to 1).</summary>
    public float TakeLevel()
    {
        if (_fake is { } clock)
        {
            double t = clock.Elapsed.TotalSeconds;
            return (float)(0.35 + 0.3 * Math.Sin(t * 5) * Math.Sin(t * 1.3));
        }
        return Interlocked.Exchange(ref _peak, 0);
    }

    private void Measure(byte[] buffer, int count, WaveFormat format)
    {
        float peak = 0;
        if (format.Encoding == WaveFormatEncoding.IeeeFloat || (format.Encoding == WaveFormatEncoding.Extensible && format.BitsPerSample == 32))
        {
            for (int i = 0; i + 3 < count; i += 4)
                peak = Math.Max(peak, Math.Abs(BitConverter.ToSingle(buffer, i)));
        }
        else if (format.BitsPerSample == 16)
        {
            for (int i = 0; i + 1 < count; i += 2)
                peak = Math.Max(peak, Math.Abs(BitConverter.ToInt16(buffer, i) / 32768f));
        }
        float before;
        do
        {
            before = _peak;
            if (peak <= before)
                return;
        }
        while (Interlocked.CompareExchange(ref _peak, peak, before) != before);
    }

    public void Dispose()
    {
        if (_capture is null)
            return;
        try
        {
            _capture.StopRecording();
        }
        catch (Exception ex) when (ex is System.Runtime.InteropServices.COMException or InvalidOperationException)
        {
        }
        _capture.Dispose();
    }
}
