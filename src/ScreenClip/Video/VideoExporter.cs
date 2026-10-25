using ScreenClip.Core.Video;
using SkiaSharp;
using Vortice.MediaFoundation;

namespace ScreenClip.Video;

/// <summary>
/// Writes the part kept of a video as the editor shows it: every frame drawn by the editor (annotations, crop,
/// layout), then encoded as MP4 (H.264 and the sound in AAC) or as an animated GIF (no sound, at most
/// <see cref="GifFps"/> frames a second and <see cref="GifMaxWidth"/> pixels wide, for a file that stays small).
/// </summary>
internal static class VideoExporter
{
    public const int GifFps = 15;
    public const int GifMaxWidth = 960;
    private const int Fps = ScreenRecorder.Fps;
    private const long FrameTime = 10_000_000 / Fps;

    /// <param name="render">Draws a frame (the scale for a smaller GIF): the picture is the caller's to free.</param>
    /// <param name="width">The width of a frame drawn at scale 1 (the GIF is made smaller from it).</param>
    public static void Export(string source, string target, bool gif, TrimRange range, Func<SKImage, float, SKImage> render, int width,
        IProgress<double>? progress, CancellationToken cancel)
    {
        try
        {
            if (gif)
                WriteGif(source, target, range, render, Math.Min(1f, GifMaxWidth / (float)Math.Max(1, width)), progress, cancel);
            else
                WriteMp4(source, target, range, render, progress, cancel);
        }
        catch
        {
            try
            {
                File.Delete(target);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
            }
            throw;
        }
    }

    /// <summary>The frames of the part kept, with their time from its start: the one shown at the start first.</summary>
    private static IEnumerable<(TimeSpan Time, SKImage Image)> Frames(VideoReader reader, TrimRange range, CancellationToken cancel)
    {
        reader.Seek(range.Start);
        (TimeSpan Time, SKImage Image)? before = null;
        try
        {
            while (reader.Next() is { } frame)
            {
                cancel.ThrowIfCancellationRequested();
                if (frame.Time < range.Start)
                {
                    before?.Image.Dispose();
                    before = frame;
                    continue;
                }
                if (before is { } first && frame.Time > range.Start)
                {
                    before = null;
                    yield return (TimeSpan.Zero, first.Image);
                }
                if (frame.Time >= range.End)
                {
                    frame.Image.Dispose();
                    yield break;
                }
                yield return (frame.Time - range.Start, frame.Image);
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

    private static void WriteMp4(string source, string target, TrimRange range, Func<SKImage, float, SKImage> render,
        IProgress<double>? progress, CancellationToken cancel)
    {
        using var reader = new VideoReader(source);
        using var sound = reader.HasAudio ? new AudioReader(source) : null;
        sound?.Seek(range.Start);
        IMFSinkWriter? writer = null;
        SKSurface? flat = null;
        int videoStream = -1, soundStream = -1, width = 0, height = 0;
        long soundWritten = 0;          // up to where (100 ns, from the start) the sound is written
        (TimeSpan Time, byte[] Pcm)? soundBlock = null;
        long last = -1;
        try
        {
            void WriteSound(long until)
            {
                if (sound is null)
                    return;
                while (soundWritten < until)
                {
                    soundBlock ??= sound.Next();
                    if (soundBlock is not { } block || block.Time >= range.End)
                    {
                        soundWritten = long.MaxValue;
                        return;
                    }
                    soundBlock = null;
                    var (bytes, offset, count) = VideoPlayer.Trim(block.Time, block.Pcm, range.Start, range.End);
                    if (count == 0)
                        continue;
                    long time = Math.Max(0, (block.Time - range.Start).Ticks + offset / 4 * 10_000_000L / 48000);
                    using var sample = Media.Sample(bytes.AsSpan(offset, count), time, count / 4 * 10_000_000L / 48000);
                    writer!.WriteSample(soundStream, sample);
                    soundWritten = time + sample.SampleDuration;
                }
            }

            void WriteFrame(long time)
            {
                using var buffer = MediaFactory.MFCreateMemoryBuffer(width * height * 4);
                buffer.Lock(out IntPtr data, out _, out _);
                flat!.ReadPixels(new SKImageInfo(width, height, SKColorType.Bgra8888, SKAlphaType.Premul), data, width * 4, 0, 0);
                buffer.Unlock();
                buffer.CurrentLength = width * height * 4;
                using var sample = MediaFactory.MFCreateSample();
                sample.AddBuffer(buffer);
                sample.SampleTime = time;
                sample.SampleDuration = FrameTime;
                writer!.WriteSample(videoStream, sample);
                last = time;
            }

            foreach (var (time, frame) in Frames(reader, range, cancel))
            {
                SKImage picture;
                using (frame)
                    picture = render(frame, 1);
                using (picture)
                {
                    if (writer is null)
                    {
                        // H.264 wants even sizes: a pixel of the edge goes.
                        width = Math.Max(2, picture.Width & ~1);
                        height = Math.Max(2, picture.Height & ~1);
                        flat = SKSurface.Create(new SKImageInfo(width, height, SKColorType.Bgra8888, SKAlphaType.Premul));
                        writer = CreateWriter(target, width, height, sound is not null, out videoStream, out soundStream);
                    }
                    // A video has no transparency: on white, as a JPG.
                    flat!.Canvas.Clear(SKColors.White);
                    flat.Canvas.DrawImage(picture, 0, 0);
                }
                WriteFrame(time.Ticks);
                WriteSound(time.Ticks + 10_000_000);
                progress?.Report(time.TotalSeconds / Math.Max(0.001, range.Length.TotalSeconds));
            }
            if (writer is null)
                throw new InvalidDataException("No frame in this part of the video.");
            // Still at the end (nothing moved): the last frame lasts until the end.
            long end = range.Length.Ticks;
            if (end - last > FrameTime)
                WriteFrame(end - FrameTime);
            WriteSound(end);
            writer.Finalize();
            progress?.Report(1);
        }
        finally
        {
            writer?.Dispose();
            flat?.Dispose();
        }
    }

    private static IMFSinkWriter CreateWriter(string target, int width, int height, bool withSound, out int videoStream, out int soundStream)
    {
        Media.EnsureStarted();
        using var attributes = MediaFactory.MFCreateAttributes(1);
        attributes.Set(SinkWriterAttributeKeys.ReadwriteEnableHardwareTransforms, 1u);
        var writer = MediaFactory.MFCreateSinkWriterFromURL(target, null, attributes);
        using (var output = Media.VideoType(VideoFormatGuids.H264, width, height, Fps, Media.Bitrate(width, height, Fps)))
            videoStream = writer.AddStream(output);
        using (var input = Media.VideoType(VideoFormatGuids.Rgb32, width, height, Fps))
        using (var encoding = MediaFactory.MFCreateAttributes(1))
        {
            // Top-down rows, as SkiaSharp has them.
            input.Set(MediaTypeAttributeKeys.DefaultStride, (uint)(width * 4));
            encoding.Set(Media.GopSize, (uint)Fps);
            writer.SetInputMediaType(videoStream, input, encoding);
        }
        soundStream = -1;
        if (withSound)
        {
            using (var output = Media.Aac())
                soundStream = writer.AddStream(output);
            using var input = Media.Pcm();
            writer.SetInputMediaType(soundStream, input, null);
        }
        writer.BeginWriting();
        return writer;
    }

    private static void WriteGif(string source, string target, TrimRange range, Func<SKImage, float, SKImage> render, float scale,
        IProgress<double>? progress, CancellationToken cancel)
    {
        using var reader = new VideoReader(source);
        using var file = File.Create(target);
        var pacer = new GifPacer(GifFps);
        GifEncoder? gif = null;
        SKSurface? flat = null;
        byte[]? pending = null;
        int width = 0, height = 0;
        try
        {
            foreach (var (time, frame) in Frames(reader, range, cancel))
            {
                if (!pacer.Keep(time))
                {
                    frame.Dispose();
                    continue;
                }
                SKImage picture;
                using (frame)
                    picture = render(frame, scale);
                using (picture)
                {
                    if (gif is null)
                    {
                        width = picture.Width;
                        height = picture.Height;
                        flat = SKSurface.Create(new SKImageInfo(width, height, SKColorType.Bgra8888, SKAlphaType.Premul));
                        gif = new GifEncoder(file, width, height);
                    }
                    flat!.Canvas.Clear(SKColors.White);
                    flat.Canvas.DrawImage(picture, 0, 0);
                }
                if (pending is not null)
                    gif.AddFrame(pending, width * 4, pacer.Close(time));
                pending = new byte[width * height * 4];
                unsafe
                {
                    fixed (byte* p = pending)
                        flat!.ReadPixels(new SKImageInfo(width, height, SKColorType.Bgra8888, SKAlphaType.Premul), (IntPtr)p, width * 4, 0, 0);
                }
                progress?.Report(time.TotalSeconds / Math.Max(0.001, range.Length.TotalSeconds));
            }
            if (gif is null || pending is null)
                throw new InvalidDataException("No frame in this part of the video.");
            gif.AddFrame(pending, width * 4, pacer.Close(range.Length));
            gif.Finish();
            progress?.Report(1);
        }
        finally
        {
            flat?.Dispose();
        }
    }
}
