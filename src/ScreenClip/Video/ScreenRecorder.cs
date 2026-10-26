using System.Diagnostics;
using ScreenClip.Core;
using ScreenClip.Core.Video;
using Vortice.Direct3D11;
using Vortice.DXGI;
using Vortice.MediaFoundation;
using Windows.Graphics.Capture;
using Windows.Graphics.DirectX;

namespace ScreenClip.Video;

/// <summary>
/// Records a part of a monitor into an MP4 (H.264 by the graphics card when it can, AAC): the monitor is filmed by
/// Windows.Graphics.Capture (frames only when something changes, at most <see cref="Fps"/> a second), cut to the area
/// on the graphics card, and handed to Media Foundation. Pausing takes the paused time out. The pointer is filmed with
/// the screen, or recorded apart (<see cref="PointerRecorder"/>) for the editor to draw it again.
/// </summary>
internal sealed class ScreenRecorder : IDisposable
{
    public const int Fps = 30;
    private const long FrameTime = 10_000_000 / Fps;

    private readonly ID3D11Device _device;
    private readonly ID3D11DeviceContext _context;
    private readonly IMFDXGIDeviceManager _manager;
    private readonly IMFSinkWriter _writer;
    private readonly int _videoStream;
    private readonly int _audioStream = -1;
    private readonly AudioRecorder? _audio;
    private readonly PointerRecorder? _pointer;
    private readonly GraphicsCaptureItem _item;
    private readonly Direct3D11CaptureFramePool _pool;
    private readonly GraphicsCaptureSession _session;
    private readonly int _left, _top;
    private readonly object _lock = new();
    private ID3D11Texture2D? _lastFrame;
    private long _start;
    private long _pausedAt;
    private long _pausedTotal;
    private long _lastTime = -1;
    private bool _stopped;

    /// <param name="area">The part filmed, in screen pixels, on <paramref name="monitorBounds"/>.</param>
    /// <param name="pointerApart">The pointer is not filmed but recorded apart (<see cref="PointerTrack"/>).</param>
    public ScreenRecorder(PixelRect area, IntPtr monitor, PixelRect monitorBounds, string path, bool systemSound, bool microphone, bool pointerApart)
    {
        Path = path;
        // H.264 wants even sizes.
        Width = Math.Max(2, area.Width & ~1);
        Height = Math.Max(2, area.Height & ~1);
        _left = area.X - monitorBounds.X;
        _top = area.Y - monitorBounds.Y;

        Media.EnsureStarted();
        _device = Direct3D.CreateDevice(out _context);
        _manager = MediaFactory.MFCreateDXGIDeviceManager();
        _manager.ResetDevice(_device);

        using (var attributes = MediaFactory.MFCreateAttributes(2))
        {
            attributes.Set(SinkWriterAttributeKeys.ReadwriteEnableHardwareTransforms, 1u);
            attributes.Set(SinkWriterAttributeKeys.D3DManager, _manager);
            _writer = MediaFactory.MFCreateSinkWriterFromURL(path, null, attributes);
        }
        using (var output = Media.VideoType(VideoFormatGuids.H264, Width, Height, Fps, Media.Bitrate(Width, Height, Fps)))
            _videoStream = _writer.AddStream(output);
        using (var input = Media.VideoType(VideoFormatGuids.Argb32, Width, Height, Fps))
        using (var encoding = MediaFactory.MFCreateAttributes(1))
        {
            // A key frame every third of a second: the editor decodes from the one before the playhead (measured on a
            // 1800 × 1000 video moving all over: going back with a key frame every second, 70 ms a move of the playhead).
            encoding.Set(Media.GopSize, (uint)(Fps / 3));
            _writer.SetInputMediaType(_videoStream, input, encoding);
        }

        if (systemSound || microphone)
        {
            var audio = new AudioRecorder(systemSound, microphone, WriteAudio);
            if (audio.HasSources)
            {
                using (var output = Media.Aac())
                    _audioStream = _writer.AddStream(output);
                using (var input = Media.Pcm())
                    _writer.SetInputMediaType(_audioStream, input, null);
                _audio = audio;
            }
            else
                audio.Dispose();
        }
        _writer.BeginWriting();

        _item = Direct3D.ForMonitor(monitor);
        _pool = Direct3D11CaptureFramePool.CreateFreeThreaded(Direct3D.ToWinRT(_device), DirectXPixelFormat.B8G8R8A8UIntNormalized, 2, _item.Size);
        _session = _pool.CreateCaptureSession(_item);
        _session.IsCursorCaptureEnabled = !pointerApart;
        if (pointerApart)
            _pointer = new PointerRecorder(area, VideoTime);
        try
        {
            // No yellow frame around the screen (Windows 11): ScreenClip draws its own around the area.
            _session.IsBorderRequired = false;
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or NotSupportedException or System.Runtime.InteropServices.COMException or InvalidCastException)
        {
        }
        _pool.FrameArrived += OnFrame;
    }

    public string Path { get; }

    public int Width { get; }

    public int Height { get; }

    public bool HasSound => _audio is not null;

    /// <summary>The pointer recorded apart, once stopped (null when it was filmed).</summary>
    public PointerTrack? PointerTrack => _pointer?.Track;

    public bool IsPaused => _pausedAt != 0;

    /// <summary>The time recorded so far (pauses left out).</summary>
    public TimeSpan Elapsed
    {
        get
        {
            if (_start == 0)
                return TimeSpan.Zero;
            long now = IsPaused ? _pausedAt : Now();
            return TimeSpan.FromTicks(Math.Max(0, now - _start - _pausedTotal));
        }
    }

    // QueryPerformanceCounter in 100 ns units: the clock of the captured frames.
    private static long Now() => (long)(Stopwatch.GetTimestamp() * (10_000_000.0 / Stopwatch.Frequency));

    // The time of the video now, for the pointer; nothing while paused or stopped.
    private long? VideoTime() => _start == 0 || IsPaused || _stopped ? null : Now() - _start - _pausedTotal;

    public void Start()
    {
        _start = Now();
        _session.StartCapture();
        _audio?.Start();
        _pointer?.Start();
    }

    public void Pause()
    {
        if (IsPaused || _stopped)
            return;
        _pausedAt = Now();
        _audio?.Pause();
    }

    public void Resume()
    {
        if (!IsPaused || _stopped)
            return;
        _pausedTotal += Now() - _pausedAt;
        _pausedAt = 0;
        _audio?.Resume();
    }

    private void OnFrame(Direct3D11CaptureFramePool pool, object _)
    {
        using var frame = pool.TryGetNextFrame();
        if (frame is null || _stopped || IsPaused)
            return;
        long time = frame.SystemRelativeTime.Ticks - _start - _pausedTotal;
        if (time < 0 || (_lastTime >= 0 && time - _lastTime < FrameTime * 9 / 10))
            return; // at most Fps frames a second
        using var source = Direct3D.TextureOf(frame.Surface);
        var size = source.Description;
        if (_left + Width > size.Width || _top + Height > size.Height)
            return; // the screen changed size: nothing to cut there
        var copy = _device.CreateTexture2D(new Texture2DDescription
        {
            Width = (uint)Width,
            Height = (uint)Height,
            MipLevels = 1,
            ArraySize = 1,
            Format = Format.B8G8R8A8_UNorm,
            SampleDescription = new SampleDescription(1, 0),
            Usage = ResourceUsage.Default,
            BindFlags = BindFlags.ShaderResource | BindFlags.RenderTarget,
        });
        _context.CopySubresourceRegion(copy, 0, 0, 0, 0, source, 0, new Vortice.Mathematics.Box(_left, _top, 0, _left + Width, _top + Height, 1));
        lock (_lock)
        {
            if (_stopped)
            {
                copy.Dispose();
                return;
            }
            WriteFrame(copy, time);
            _lastFrame?.Dispose();
            _lastFrame = copy;
            _lastTime = time;
        }
    }

    private void WriteFrame(ID3D11Texture2D texture, long time)
    {
        using var buffer = MediaFactory.MFCreateDXGISurfaceBuffer(typeof(ID3D11Texture2D).GUID, texture, 0, false);
        using (var buffer2d = buffer.QueryInterface<IMF2DBuffer>())
            buffer.CurrentLength = buffer2d.ContiguousLength;
        using var sample = MediaFactory.MFCreateSample();
        sample.AddBuffer(buffer);
        sample.SampleTime = time;
        sample.SampleDuration = FrameTime;
        _writer.WriteSample(_videoStream, sample);
    }

    private void WriteAudio(byte[] pcm, long time)
    {
        using var sample = Media.Sample(pcm, time, pcm.Length / 4 * 10_000_000L / AudioRecorder.Rate);
        lock (_lock)
        {
            if (!_stopped)
                _writer.WriteSample(_audioStream, sample);
        }
    }

    /// <summary>
    /// Ends the recording and writes the file (a moment for long ones: call it off the UI thread). The last frame
    /// lasts until now, even when nothing moved on the screen since.
    /// </summary>
    public TimeSpan Stop()
    {
        var elapsed = Elapsed;
        if (IsPaused)
            Resume();
        _audio?.Stop();
        _pointer?.Stop();
        _session.Dispose();
        _pool.Dispose();
        lock (_lock)
        {
            if (_stopped)
                return elapsed;
            long end = Math.Max(0, elapsed.Ticks);
            if (_lastFrame is not null && end - _lastTime >= FrameTime)
                WriteFrame(_lastFrame, end - FrameTime);
            _stopped = true;
            _writer.Finalize();
        }
        return elapsed;
    }

    /// <summary>Gives up the recording: nothing is kept.</summary>
    public void Abort()
    {
        _audio?.Stop();
        _pointer?.Stop();
        _session.Dispose();
        _pool.Dispose();
        lock (_lock)
            _stopped = true;
        Dispose();
        try
        {
            File.Delete(Path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }

    public void Dispose()
    {
        _audio?.Dispose();
        _lastFrame?.Dispose();
        _lastFrame = null;
        _writer.Dispose();
        _manager.Dispose();
        _context.Dispose();
        _device.Dispose();
    }
}
