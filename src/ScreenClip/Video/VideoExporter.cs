using ScreenClip.Core.Video;
using SkiaSharp;
using Vortice.MediaFoundation;

namespace ScreenClip.Video;

/// <summary>
/// Writes what is kept of a video (its pieces, one after the other) as the editor shows it: every frame drawn by the
/// editor (annotations, crop, layout), then encoded as MP4 (H.264 and the sound the editor mixes, in AAC) or as an
/// animated GIF (no sound, at most <see cref="GifFps"/> frames a second and <see cref="GifMaxWidth"/> pixels wide, for a
/// file that stays small). A still picture animated (a pointer that moves on it) is written the same way.
/// </summary>
internal static class VideoExporter
{
    public const int GifFps = 15;
    public const int GifMaxWidth = 960;
    private const int Fps = ScreenRecorder.Fps;
    private const long FrameTime = 10_000_000 / Fps;

    /// <param name="pieces">What is kept, in order, in the video's own times.</param>
    /// <param name="render">
    /// Draws a frame at its time in the video (the scale for a smaller GIF): the picture is the caller's to free.
    /// </param>
    /// <param name="overlay">
    /// What is drawn over the frames at a time of the video, as a number that changes when it does (null: nothing
    /// changes with the time): a frame is written when the video or that changes.
    /// </param>
    /// <param name="width">The width of a frame drawn at scale 1 (the GIF is made smaller from it).</param>
    /// <param name="sound">Makes the sound of the MP4 (null: none).</param>
    public static void Export(string source, string target, bool gif, IReadOnlyList<Piece> pieces, Func<SKImage, TimeSpan, float, SKImage> render,
        Func<TimeSpan, int>? overlay, int width, Func<SoundComposer?>? sound, IProgress<double>? progress, CancellationToken cancel)
    {
        using var reader = new VideoReader(source);
        Write(Frames(reader, pieces, cancel), target, gif, pieces, render, overlay, width, sound, progress, cancel);
    }

    /// <summary>
    /// A still picture animated for <paramref name="length"/>: one frame, and a new one written whenever what is drawn
    /// over it changes (<paramref name="overlay"/>). No sound.
    /// </summary>
    public static void ExportStill(SKImage still, string target, bool gif, TimeSpan length, Func<SKImage, TimeSpan, float, SKImage> render,
        Func<TimeSpan, int>? overlay, int width, IProgress<double>? progress, CancellationToken cancel) =>
        Write(StillFrames(still), target, gif, [new Piece(TimeSpan.Zero, length)], render, overlay, width, null, progress, cancel);

    // The picture once, as a copy: the steps free each frame when they are done with it.
    private static IEnumerable<(TimeSpan Time, SKImage Image)> StillFrames(SKImage still)
    {
        using var surface = SKSurface.Create(new SKImageInfo(still.Width, still.Height, SKColorType.Bgra8888, SKAlphaType.Premul));
        surface.Canvas.DrawImage(still, 0, 0);
        yield return (TimeSpan.Zero, surface.Snapshot());
    }

    private static void Write(IEnumerable<(TimeSpan Time, SKImage Image)> frames, string target, bool gif, IReadOnlyList<Piece> pieces,
        Func<SKImage, TimeSpan, float, SKImage> render, Func<TimeSpan, int>? overlay, int width, Func<SoundComposer?>? sound,
        IProgress<double>? progress, CancellationToken cancel)
    {
        try
        {
            if (gif)
                WriteGif(frames, target, pieces, render, overlay, Math.Min(1f, GifMaxWidth / (float)Math.Max(1, width)), progress, cancel);
            else
                WriteMp4(frames, target, pieces, render, overlay, sound, progress, cancel);
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

    private static TimeSpan LengthOf(IReadOnlyList<Piece> pieces) => pieces.Aggregate(TimeSpan.Zero, (sum, p) => sum + p.Length);

    // The time of the video at a time of what is kept.
    private static TimeSpan VideoTimeOf(IReadOnlyList<Piece> pieces, TimeSpan kept)
    {
        foreach (var piece in pieces)
        {
            if (kept < piece.Length)
                return piece.Start + kept;
            kept -= piece.Length;
        }
        return pieces.Count > 0 ? pieces[^1].End : TimeSpan.Zero;
    }

    /// <summary>The frames of what is kept, with their time in it, piece after piece.</summary>
    private static IEnumerable<(TimeSpan Time, SKImage Image)> Frames(VideoReader reader, IReadOnlyList<Piece> pieces, CancellationToken cancel)
    {
        var before = TimeSpan.Zero;
        foreach (var piece in pieces)
        {
            foreach (var frame in VideoPlayer.FramesOf(reader, piece, cancel))
                yield return (before + frame.Time, frame.Image);
            before += piece.Length;
        }
    }

    /// <summary>
    /// The frames to write, <paramref name="fps"/> a second through what is kept: the video's frame shown by then, at
    /// that time in what is kept. A step where neither the frame nor the overlay changed is left out (the frame before
    /// lasts). The pictures stay this enumerator's: each is freed when the next one comes.
    /// </summary>
    private static IEnumerable<(TimeSpan Time, SKImage Image)> Steps(IEnumerable<(TimeSpan Time, SKImage Image)> source, IReadOnlyList<Piece> pieces, int fps,
        Func<TimeSpan, int>? overlay, CancellationToken cancel)
    {
        using var frames = source.GetEnumerator();
        (TimeSpan Time, SKImage Image)? shown = null, next = frames.MoveNext() ? frames.Current : null;
        bool changed = false;
        int? lastOverlay = null;
        try
        {
            long count = Math.Max(1, (long)Math.Ceiling(LengthOf(pieces).TotalSeconds * fps - 1e-6));
            for (long step = 0; step < count; step++)
            {
                cancel.ThrowIfCancellationRequested();
                var time = TimeSpan.FromSeconds(step / (double)fps);
                while (next is { } coming && coming.Time <= time)
                {
                    shown?.Image.Dispose();
                    shown = coming;
                    changed = true;
                    next = frames.MoveNext() ? frames.Current : null;
                }
                if (shown is not { } frame)
                    continue;
                int over = overlay?.Invoke(VideoTimeOf(pieces, time)) ?? 0;
                if (!changed && over == lastOverlay)
                    continue;
                changed = false;
                lastOverlay = over;
                yield return (time, frame.Image);
            }
        }
        finally
        {
            shown?.Image.Dispose();
            next?.Image.Dispose();
        }
    }

    private static void WriteMp4(IEnumerable<(TimeSpan Time, SKImage Image)> frames, string target, IReadOnlyList<Piece> pieces,
        Func<SKImage, TimeSpan, float, SKImage> render, Func<TimeSpan, int>? overlay, Func<SoundComposer?>? makeSound, IProgress<double>? progress,
        CancellationToken cancel)
    {
        using var sound = makeSound?.Invoke();
        var length = LengthOf(pieces);
        IMFSinkWriter? writer = null;
        SKSurface? flat = null;
        int videoStream = -1, soundStream = -1, width = 0, height = 0;
        long soundWritten = 0;          // up to where (100 ns, from the start) the sound is written
        long last = -1;
        try
        {
            void WriteSound(long until)
            {
                if (sound is null)
                    return;
                while (soundWritten < until)
                {
                    if (sound.Next() is not { } chunk)
                    {
                        soundWritten = long.MaxValue;
                        return;
                    }
                    using var sample = Media.Sample(chunk.Pcm, chunk.Time.Ticks, chunk.Pcm.Length / 4 * 10_000_000L / SoundComposer.Rate);
                    writer!.WriteSample(soundStream, sample);
                    soundWritten = chunk.Time.Ticks + sample.SampleDuration;
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

            foreach (var (time, frame) in Steps(frames, pieces, Fps, overlay, cancel))
            {
                using (var picture = render(frame, VideoTimeOf(pieces, time), 1))
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
                progress?.Report(time.TotalSeconds / Math.Max(0.001, length.TotalSeconds));
            }
            if (writer is null)
                throw new InvalidDataException("No frame in this part of the video.");
            // Still at the end (nothing moved): the last frame lasts until the end.
            long end = length.Ticks;
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

    private static void WriteGif(IEnumerable<(TimeSpan Time, SKImage Image)> frames, string target, IReadOnlyList<Piece> pieces,
        Func<SKImage, TimeSpan, float, SKImage> render, Func<TimeSpan, int>? overlay, float scale, IProgress<double>? progress, CancellationToken cancel)
    {
        var length = LengthOf(pieces);
        using var file = File.Create(target);
        var pacer = new GifPacer(GifFps);
        GifEncoder? gif = null;
        SKSurface? flat = null;
        byte[]? pending = null;
        int width = 0, height = 0;
        try
        {
            foreach (var (time, frame) in Steps(frames, pieces, GifFps, overlay, cancel))
            {
                if (!pacer.Keep(time))
                    continue;
                using (var picture = render(frame, VideoTimeOf(pieces, time), scale))
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
                progress?.Report(time.TotalSeconds / Math.Max(0.001, length.TotalSeconds));
            }
            if (gif is null || pending is null)
                throw new InvalidDataException("No frame in this part of the video.");
            gif.AddFrame(pending, width * 4, pacer.Close(length));
            gif.Finish();
            progress?.Report(1);
        }
        finally
        {
            flat?.Dispose();
        }
    }
}
