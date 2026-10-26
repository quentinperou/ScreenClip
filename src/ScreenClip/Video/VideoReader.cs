using SkiaSharp;
using Vortice.Direct3D11;
using Vortice.MediaFoundation;

namespace ScreenClip.Video;

/// <summary>A video just recorded: its file in the temp folder, when, and its copy in the videos folder once saved.</summary>
/// <param name="Kept">Saved or copied already: nothing is asked before it leaves the editor.</param>
internal sealed record VideoClip(string Path, DateTime TakenAt, string? SavedPath, Core.Video.PointerTrack? Pointer = null, bool Kept = false);

/// <summary>
/// Reads the frames of a video (Media Foundation) as SkiaSharp pictures: in order, or the one shown at a given time.
/// The decoder's own frames (NV12) are read, and only the one asked for becomes a picture: going to a time decodes
/// from the key frame before it, and turning each of those frames into RGB would cost far more than decoding them
/// (measured: 220 ms a move of the playhead in a large video). A time a little ahead of the last one goes on from there,
/// without going back to the key frame. One thread at a time: the editor, the player and an export each open their own.
/// </summary>
internal sealed class VideoReader : IDisposable
{
    // Ahead of the frame read last by at most this, decoding on is quicker than going back to a key frame.
    private static readonly long ForwardReach = TimeSpan.FromSeconds(1).Ticks;

    private readonly IMFSourceReader _reader;
    private readonly ID3D11Device? _device;
    private readonly IMFDXGIDeviceManager? _manager;
    private readonly bool _nv12;
    private int _codedHeight;
    private int _stride;
    private YuvToRgb _colors;
    private IMFSample? _current, _next;
    private long _currentTime, _nextTime;

#if DEBUG
    /// <summary>Debug builds: the frames turned into RGB by Media Foundation, to compare the colors.</summary>
    public static bool ForceRgb32 { get; set; }
#endif

    /// <summary>Decoded by the graphics card when it can (off: by the processor, to compare).</summary>
    public static bool Hardware { get; set; } = true;

    public VideoReader(string path)
    {
        Media.EnsureStarted();
        using (var attributes = MediaFactory.MFCreateAttributes(2))
        {
            if (Hardware && Direct3D.CreateVideoDevice() is { } device)
            {
                _device = device;
                _manager = MediaFactory.MFCreateDXGIDeviceManager();
                _manager.ResetDevice(device);
                attributes.Set(SourceReaderAttributeKeys.D3DManager, _manager);
                attributes.Set(SinkWriterAttributeKeys.ReadwriteEnableHardwareTransforms, 1u);
            }
            _reader = MediaFactory.MFCreateSourceReaderFromURL(path, attributes);
        }
        using (var native = _reader.GetNativeMediaType(SourceReaderIndex.FirstVideoStream, 0))
        {
            MediaFactory.MFGetAttributeSize(native, MediaTypeAttributeKeys.FrameSize, out uint width, out uint height);
            Width = (int)width;
            Height = (int)height;
        }
        try
        {
            using var audio = _reader.GetNativeMediaType(SourceReaderIndex.FirstAudioStream, 0);
            HasAudio = true;
        }
        catch (SharpGen.Runtime.SharpGenException)
        {
            HasAudio = false;
        }
        var duration = _reader.GetPresentationAttribute(SourceReaderIndex.MediaSource, PresentationDescriptionAttributeKeys.Duration);
        Duration = TimeSpan.FromTicks(Convert.ToInt64(duration.Value, System.Globalization.CultureInfo.InvariantCulture));

        try
        {
#if DEBUG
            if (ForceRgb32)
                throw new SharpGen.Runtime.SharpGenException("Media Foundation's RGB asked");
#endif
            using var nv12 = MediaFactory.MFCreateMediaType();
            nv12.Set(MediaTypeAttributeKeys.MajorType, MediaTypeGuids.Video);
            nv12.Set(MediaTypeAttributeKeys.Subtype, VideoFormatGuids.NV12);
            _reader.SetCurrentMediaType(SourceReaderIndex.FirstVideoStream, nv12);
            _nv12 = true;
            ReadFormat();
        }
        catch (SharpGen.Runtime.SharpGenException)
        {
            // A decoder that gives no NV12: Media Foundation turns its frames into RGB itself (slower).
            _reader.Dispose();
            _manager?.Dispose();
            _device?.Dispose();
            _manager = null;
            _device = null;
            using var attributes = MediaFactory.MFCreateAttributes(1);
            attributes.Set(SourceReaderAttributeKeys.EnableVideoProcessing, 1u);
            _reader = MediaFactory.MFCreateSourceReaderFromURL(path, attributes);
            using var rgb = MediaFactory.MFCreateMediaType();
            rgb.Set(MediaTypeAttributeKeys.MajorType, MediaTypeGuids.Video);
            rgb.Set(MediaTypeAttributeKeys.Subtype, VideoFormatGuids.Rgb32);
            _reader.SetCurrentMediaType(SourceReaderIndex.FirstVideoStream, rgb);
        }
        _reader.SetStreamSelection(SourceReaderIndex.AllStreams, false);
        _reader.SetStreamSelection(SourceReaderIndex.FirstVideoStream, true);
    }

    public int Width { get; }

    public int Height { get; }

    public TimeSpan Duration { get; }

    public bool HasAudio { get; }

    /// <summary>Goes to the key frame at or before <paramref name="time"/>: the next frames read start there.</summary>
    public void Seek(TimeSpan time)
    {
        Drop();
        _reader.SetCurrentPosition(Math.Max(0, time.Ticks));
    }

    /// <summary>The next frame and its time, or null at the end.</summary>
    public (TimeSpan Time, SKImage Image)? Next()
    {
        Drop();
        using var sample = ReadSample(out long time);
        return sample is null ? null : (TimeSpan.FromTicks(time), ToImage(sample));
    }

    /// <summary>The frame shown at <paramref name="time"/>: the last one that starts at or before it.</summary>
    public SKImage? FrameAt(TimeSpan time) => FrameAt(time, null, out _);

    /// <summary>
    /// The frame shown at <paramref name="time"/>, or null with <paramref name="same"/> set when it is the one that
    /// starts at <paramref name="unlessAt"/> (already shown: no picture to make). <paramref name="shownAt"/> is when
    /// the frame found starts.
    /// </summary>
    public SKImage? FrameAt(TimeSpan time, TimeSpan? unlessAt, out bool same) => FrameAt(time, unlessAt, out same, out _);

    public SKImage? FrameAt(TimeSpan time, TimeSpan? unlessAt, out bool same, out TimeSpan shownAt)
    {
        same = false;
        shownAt = default;
        long t = time.Ticks;
        bool ahead = _current is not null && t >= _currentTime && t - _currentTime <= ForwardReach;
        if (!ahead)
        {
            Seek(time);
            _current = ReadSample(out _currentTime);
            if (_current is null)
                return null;
        }
        // On while the next frame starts at or before the time (the first one after a seek is shown even if later).
        while (true)
        {
            if (_next is null)
            {
                _next = ReadSample(out _nextTime);
                if (_next is null)
                    break;
            }
            if (_nextTime > t)
                break;
            _current!.Dispose();
            (_current, _currentTime, _next) = (_next, _nextTime, null);
        }
        shownAt = TimeSpan.FromTicks(_currentTime);
        if (unlessAt == shownAt)
        {
            same = true;
            return null;
        }
        return ToImage(_current!);
    }

    // The frames held to go on from (FrameAt) are dropped when the reader is moved or read in order.
    private void Drop()
    {
        _current?.Dispose();
        _next?.Dispose();
        _current = _next = null;
    }

    private void ReadFormat()
    {
        using var type = _reader.GetCurrentMediaType(SourceReaderIndex.FirstVideoStream);
        MediaFactory.MFGetAttributeSize(type, MediaTypeAttributeKeys.FrameSize, out uint codedWidth, out uint codedHeight);
        _codedHeight = (int)codedHeight;
        _stride = (int)codedWidth;
        try
        {
            _stride = (int)type.GetUInt32(MediaTypeAttributeKeys.DefaultStride);
        }
        catch (SharpGen.Runtime.SharpGenException)
        {
        }
        uint matrix = 0, range = 0;
        try
        {
            matrix = type.GetUInt32(MediaTypeAttributeKeys.YuvMatrix);
        }
        catch (SharpGen.Runtime.SharpGenException)
        {
        }
        try
        {
            range = type.GetUInt32(MediaTypeAttributeKeys.VideoNominalRange);
        }
        catch (SharpGen.Runtime.SharpGenException)
        {
        }
        // MFVideoTransferMatrix: 1 = BT.709, 2 = BT.601; unknown: 709 for HD, as encoders choose. MFNominalRange 1: 0–255.
        bool bt709 = matrix == 1 || (matrix != 2 && Height >= 720);
        _colors = new YuvToRgb(bt709, fullRange: range == 1);
    }

    private IMFSample? ReadSample(out long time)
    {
        while (true)
        {
            var sample = _reader.ReadSample(SourceReaderIndex.FirstVideoStream, SourceReaderControlFlag.None, out _, out var flags, out time);
            if (flags.HasFlag(SourceReaderFlag.EndOfStream) || flags.HasFlag(SourceReaderFlag.Error))
            {
                sample?.Dispose();
                return null;
            }
            if (_nv12 && flags.HasFlag(SourceReaderFlag.CurrentMediaTypeChanged))
                ReadFormat();
            if (sample is not null)
                return sample;
            // A gap or a format change: read on.
        }
    }

    private SKImage ToImage(IMFSample sample)
    {
        using var buffer = sample.GetBufferByIndex(0);
        if (_nv12)
        {
            // A frame of the graphics card is copied to memory here; its planes are as tall as the card made them.
            IMF2DBuffer2? sized = null;
            try
            {
                sized = buffer.QueryInterface<IMF2DBuffer2>();
            }
            catch (SharpGen.Runtime.SharpGenException)
            {
            }
            if (sized is not null)
            {
                using (sized)
                {
                    sized.Lock2DSize(Buffer2DLockFlags.Read, out IntPtr scan0, out int pitch, out _, out int size);
                    try
                    {
                        return FromNv12(scan0, pitch, pitch > 0 ? size / pitch * 2 / 3 : _codedHeight);
                    }
                    finally
                    {
                        sized.Unlock2D();
                    }
                }
            }
        }
        IMF2DBuffer? buffer2d = null;
        try
        {
            buffer2d = buffer.QueryInterface<IMF2DBuffer>();
        }
        catch (SharpGen.Runtime.SharpGenException)
        {
        }
        if (buffer2d is not null)
        {
            using (buffer2d)
            {
                buffer2d.Lock2D(out IntPtr scan0, out int pitch);
                try
                {
                    return _nv12 ? FromNv12(scan0, pitch, _codedHeight) : FromRgb32(scan0, pitch);
                }
                finally
                {
                    buffer2d.Unlock2D();
                }
            }
        }
        buffer.Lock(out IntPtr data, out _, out int length);
        try
        {
            if (_nv12)
                return FromNv12(data, _stride, _codedHeight);
            // The decoded rows may be wider than the picture (H.264 works in blocks of 16).
            int rows = length % Height == 0 ? Height : (Height + 15) & ~15;
            return FromRgb32(data, length / rows);
        }
        finally
        {
            buffer.Unlock();
        }
    }

    /// <summary>
    /// A frame from its NV12 planes (the luma, then the chroma of each 2 × 2 block, after the decoder's rows), turned
    /// into RGB on all the cores: the only frame turned so, the one shown.
    /// </summary>
    private unsafe SKImage FromNv12(IntPtr scan0, int pitch, int planeHeight)
    {
        var info = new SKImageInfo(Width, Height, SKColorType.Bgra8888, SKAlphaType.Opaque);
        using var bitmap = new SKBitmap(info);
        byte* luma = (byte*)scan0, chroma = luma + (long)pitch * Math.Max(planeHeight, Height);
        byte* target = (byte*)bitmap.GetPixels();
        int rowBytes = bitmap.RowBytes, width = Width, height = Height;
        var colors = _colors;
        Parallel.For(0, (height + 1) / 2, pair =>
        {
            byte* uv = chroma + (long)pair * pitch;
            for (int y = pair * 2; y < Math.Min(height, pair * 2 + 2); y++)
            {
                byte* yRow = luma + (long)y * pitch;
                uint* row = (uint*)(target + (long)y * rowBytes);
                for (int x = 0; x < width; x++)
                    row[x] = colors.Pixel(yRow[x], uv[x & ~1], uv[x | 1]);
            }
        });
        bitmap.SetImmutable();
        return SKImage.FromBitmap(bitmap);
    }

    /// <summary>
    /// The picture from RGB32 rows <paramref name="pitch"/> bytes apart. Their fourth byte is not alpha and is often 0:
    /// it is set to opaque, or SkiaSharp would copy it as is into what it draws.
    /// </summary>
    private unsafe SKImage FromRgb32(IntPtr scan0, int pitch)
    {
        var info = new SKImageInfo(Width, Height, SKColorType.Bgra8888, SKAlphaType.Opaque);
        int row = info.Width * 4, size = row * info.Height;
        var pixels = System.Buffers.ArrayPool<byte>.Shared.Rent(size);
        try
        {
            fixed (byte* target = pixels)
            {
                for (int y = 0; y < info.Height; y++)
                    Buffer.MemoryCopy((byte*)(scan0 + y * pitch), target + y * row, row, row);
                var words = (uint*)target;
                for (int i = 0, count = size / 4; i < count; i++)
                    words[i] |= 0xFF000000;
            }
            return SKImage.FromPixelCopy(info, pixels.AsSpan(0, size), row);
        }
        finally
        {
            System.Buffers.ArrayPool<byte>.Shared.Return(pixels);
        }
    }

    public void Dispose()
    {
        Drop();
        _reader.Dispose();
        _manager?.Dispose();
        _device?.Dispose();
    }
}

/// <summary>Y′CbCr to RGB, in fixed point (16 bits after the point): BT.709 or BT.601, video range (16–235) or full.</summary>
internal readonly struct YuvToRgb
{
    private readonly int _y, _rv, _gu, _gv, _bu, _yOffset;

    public YuvToRgb(bool bt709, bool fullRange)
    {
        double kr = bt709 ? 0.2126 : 0.299, kb = bt709 ? 0.0722 : 0.114, kg = 1 - kr - kb;
        double ys = fullRange ? 1 : 255.0 / 219, cs = fullRange ? 1 : 255.0 / 224;
        _y = (int)Math.Round(ys * 65536);
        _rv = (int)Math.Round(2 * (1 - kr) * cs * 65536);
        _bu = (int)Math.Round(2 * (1 - kb) * cs * 65536);
        _gu = (int)Math.Round(2 * (1 - kb) * kb / kg * cs * 65536);
        _gv = (int)Math.Round(2 * (1 - kr) * kr / kg * cs * 65536);
        _yOffset = fullRange ? 0 : 16;
    }

    /// <summary>BGRA, opaque.</summary>
    public uint Pixel(byte y, byte u, byte v)
    {
        int c = (y - _yOffset) * _y + 32768, d = u - 128, e = v - 128;
        int r = (c + _rv * e) >> 16, g = (c - _gu * d - _gv * e) >> 16, b = (c + _bu * d) >> 16;
        return 0xFF000000u | (uint)Clamp(r) << 16 | (uint)Clamp(g) << 8 | (uint)Clamp(b);
    }

    private static int Clamp(int value) => value < 0 ? 0 : value > 255 ? 255 : value;
}

/// <summary>The sound of a video, as 48 kHz 16-bit stereo PCM, from a given time.</summary>
internal sealed class AudioReader : IDisposable
{
    private readonly IMFSourceReader _reader;

    public AudioReader(string path)
    {
        Media.EnsureStarted();
        _reader = MediaFactory.MFCreateSourceReaderFromURL(path, null);
        _reader.SetStreamSelection(SourceReaderIndex.AllStreams, false);
        _reader.SetStreamSelection(SourceReaderIndex.FirstAudioStream, true);
        using var pcm = Media.Pcm();
        _reader.SetCurrentMediaType(SourceReaderIndex.FirstAudioStream, pcm);
    }

    public void Seek(TimeSpan time) => _reader.SetCurrentPosition(Math.Max(0, time.Ticks));

    /// <summary>The next block of sound and its time, or null at the end.</summary>
    public (TimeSpan Time, byte[] Pcm)? Next()
    {
        while (true)
        {
            using var sample = _reader.ReadSample(SourceReaderIndex.FirstAudioStream, SourceReaderControlFlag.None, out _, out var flags, out long time);
            if (flags.HasFlag(SourceReaderFlag.EndOfStream) || flags.HasFlag(SourceReaderFlag.Error))
                return null;
            if (sample is null)
                continue;
            using var buffer = sample.ConvertToContiguousBuffer();
            buffer.Lock(out IntPtr data, out _, out int length);
            try
            {
                var pcm = new byte[length];
                System.Runtime.InteropServices.Marshal.Copy(data, pcm, 0, length);
                return (TimeSpan.FromTicks(time), pcm);
            }
            finally
            {
                buffer.Unlock();
            }
        }
    }

    public void Dispose() => _reader.Dispose();
}
