using SkiaSharp;
using Vortice.MediaFoundation;

namespace ScreenClip.Video;

/// <summary>A video just recorded: its file in the temp folder, when, and its copy in the videos folder once saved.</summary>
internal sealed record VideoClip(string Path, DateTime TakenAt, string? SavedPath);

/// <summary>
/// Reads the frames of a video (Media Foundation, decoded by the graphics card when it can) as SkiaSharp pictures:
/// in order, or the one shown at a given time. One thread at a time: the editor, the player and an export each open
/// their own.
/// </summary>
internal sealed class VideoReader : IDisposable
{
    private readonly IMFSourceReader _reader;

    public VideoReader(string path)
    {
        Media.EnsureStarted();
        using (var attributes = MediaFactory.MFCreateAttributes(1))
        {
            attributes.Set(SourceReaderAttributeKeys.EnableVideoProcessing, 1u);
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

        using (var rgb = MediaFactory.MFCreateMediaType())
        {
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
    public void Seek(TimeSpan time) => _reader.SetCurrentPosition(Math.Max(0, time.Ticks));

    /// <summary>The next frame and its time, or null at the end.</summary>
    public (TimeSpan Time, SKImage Image)? Next()
    {
        using var sample = ReadSample(out long time);
        return sample is null ? null : (TimeSpan.FromTicks(time), ToImage(sample));
    }

    /// <summary>The time of the next frame, without making a picture of it (to skip frames), or null at the end.</summary>
    public TimeSpan? Skip()
    {
        using var sample = ReadSample(out long time);
        return sample is null ? null : TimeSpan.FromTicks(time);
    }

    /// <summary>The frame shown at <paramref name="time"/>: the last one that starts at or before it.</summary>
    public SKImage? FrameAt(TimeSpan time)
    {
        Seek(time);
        IMFSample? shown = null;
        try
        {
            while (ReadSample(out long at) is { } sample)
            {
                if (at > time.Ticks && shown is not null)
                {
                    sample.Dispose();
                    break;
                }
                shown?.Dispose();
                shown = sample;
                if (at > time.Ticks)
                    break; // the first frame comes after the time asked: it is the one shown
            }
            return shown is null ? null : ToImage(shown);
        }
        finally
        {
            shown?.Dispose();
        }
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
            if (sample is not null)
                return sample;
            // A gap or a format change: read on.
        }
    }

    // The decoded rows may be wider than the picture (H.264 works in blocks of 16): the pitch comes from the buffer.
    private SKImage ToImage(IMFSample sample)
    {
        var info = new SKImageInfo(Width, Height, SKColorType.Bgra8888, SKAlphaType.Opaque);
        using var buffer = sample.GetBufferByIndex(0);
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
                    return Copy(info, scan0, pitch);
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
            int rows = length % Height == 0 ? Height : (Height + 15) & ~15;
            return Copy(info, data, length / rows);
        }
        finally
        {
            buffer.Unlock();
        }
    }

    /// <summary>
    /// The picture from rows <paramref name="pitch"/> bytes apart (negative: bottom-up). The fourth byte of RGB32 is not
    /// alpha and is often 0: it is set to opaque, or SkiaSharp would copy it as is into what it draws.
    /// </summary>
    private static unsafe SKImage Copy(SKImageInfo info, IntPtr scan0, int pitch)
    {
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

    public void Dispose() => _reader.Dispose();
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
