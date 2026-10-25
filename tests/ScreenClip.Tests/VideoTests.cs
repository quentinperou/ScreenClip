using ScreenClip.Core.Video;
using SkiaSharp;

namespace ScreenClip.Tests;

public class TimecodeTests
{
    [Theory]
    [InlineData(0, ".", "0:00.0")]
    [InlineData(4.25, ".", "0:04.2")]
    [InlineData(4.25, ",", "0:04,2")]
    [InlineData(62.99, ".", "1:02.9")]
    [InlineData(3725.5, ".", "1:02:05.5")]
    public void Format_ShowsTenths(double seconds, string separator, string expected) =>
        Assert.Equal(expected, Timecode.Format(TimeSpan.FromSeconds(seconds), separator));

    [Fact]
    public void Format_WithoutTenths() => Assert.Equal("0:07", Timecode.Format(TimeSpan.FromSeconds(7.9), tenths: false));
}

public class TrimRangeTests
{
    private static readonly TimeSpan Duration = TimeSpan.FromSeconds(10);

    [Fact]
    public void TheEnds_StayInOrder_AndApart()
    {
        var trim = TrimRange.All(Duration);
        Assert.True(trim.IsAll(Duration));
        trim = trim.WithStart(TimeSpan.FromSeconds(2)).WithEnd(TimeSpan.FromSeconds(8), Duration);
        Assert.Equal((TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(8)), (trim.Start, trim.End));
        Assert.False(trim.IsAll(Duration));
        // The start cannot pass the end, nor the end the length of the video.
        Assert.Equal(TimeSpan.FromSeconds(8) - TrimRange.MinLength, trim.WithStart(TimeSpan.FromSeconds(9)).Start);
        Assert.Equal(Duration, trim.WithEnd(TimeSpan.FromSeconds(20), Duration).End);
        Assert.Equal(TimeSpan.Zero, trim.WithStart(TimeSpan.FromSeconds(-1)).Start);
        Assert.Equal(TimeSpan.FromSeconds(2), trim.Clamp(TimeSpan.FromSeconds(1)));
    }
}

public class GifTimingTests
{
    [Fact]
    public void EvenFrames_AreKept_AtTheRateAsked()
    {
        // 30 frames a second for 1 s, at most 10 a second: every third one, 10 hundredths each.
        var times = Enumerable.Range(0, 30).Select(i => TimeSpan.FromSeconds(i / 30.0)).ToList();
        var frames = GifTiming.Frames(times, TimeSpan.FromSeconds(1), 10);
        Assert.Equal(10, frames.Count);
        Assert.Equal([0, 3, 6, 9, 12, 15, 18, 21, 24, 27], frames.Select(f => f.Index));
        Assert.Equal(100, frames.Sum(f => f.Delay));
    }

    [Fact]
    public void UnevenFrames_KeepTheirLengths()
    {
        // A screen recording: frames only when something changed.
        var times = new[] { 0.0, 0.05, 2.0, 2.4 }.Select(TimeSpan.FromSeconds).ToList();
        var frames = GifTiming.Frames(times, TimeSpan.FromSeconds(3), 15);
        Assert.Equal([0, 2, 3], frames.Select(f => f.Index));
        Assert.Equal([200, 40, 60], frames.Select(f => f.Delay));
    }
}

public class GifEncoderTests
{
    private static byte[] Frame(int width, int height, Func<int, int, SKColor> color)
    {
        var pixels = new byte[width * height * 4];
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                var c = color(x, y);
                int o = (y * width + x) * 4;
                pixels[o] = c.Blue;
                pixels[o + 1] = c.Green;
                pixels[o + 2] = c.Red;
                pixels[o + 3] = 255;
            }
        }
        return pixels;
    }

    private static SKColor Close(SKBitmap bitmap, int x, int y) => bitmap.GetPixel(x, y);

    private static void AssertNear(SKColor expected, SKColor actual)
    {
        Assert.InRange(Math.Abs(expected.Red - actual.Red), 0, 12);
        Assert.InRange(Math.Abs(expected.Green - actual.Green), 0, 12);
        Assert.InRange(Math.Abs(expected.Blue - actual.Blue), 0, 12);
    }

    [Fact]
    public void Frames_AreReadBack_WithTheirLengths()
    {
        const int W = 80, H = 60;
        var first = Frame(W, H, (x, _) => x < W / 2 ? new SKColor(220, 40, 40) : new SKColor(30, 90, 200));
        var second = Frame(W, H, (x, y) => x is >= 10 and < 30 && y is >= 10 and < 30 ? new SKColor(40, 180, 90) : x < W / 2 ? new SKColor(220, 40, 40) : new SKColor(30, 90, 200));
        using var stream = new MemoryStream();
        var gif = new GifEncoder(stream, W, H);
        gif.AddFrame(first, W * 4, 50);
        gif.AddFrame(second, W * 4, 30);
        gif.AddFrame(second, W * 4, 20); // the same again: the frame before lasts longer
        gif.Finish();

        stream.Position = 0;
        using var codec = SKCodec.Create(stream);
        Assert.NotNull(codec);
        Assert.Equal(2, codec.FrameCount);
        var info = codec.FrameInfo;
        Assert.Equal([500, 500], info.Select(f => f.Duration));

        var image = new SKImageInfo(W, H, SKColorType.Bgra8888, SKAlphaType.Premul);
        using var one = new SKBitmap(image);
        codec.GetPixels(image, one.GetPixels(), new SKCodecOptions(0));
        AssertNear(new SKColor(220, 40, 40), Close(one, 5, 5));
        AssertNear(new SKColor(30, 90, 200), Close(one, 70, 50));

        // Frame 1 is drawn over frame 0, which must be in the bitmap first.
        using var two = new SKBitmap(image);
        codec.GetPixels(image, two.GetPixels(), new SKCodecOptions(0));
        codec.GetPixels(image, two.GetPixels(), new SKCodecOptions(1, 0));
        AssertNear(new SKColor(40, 180, 90), Close(two, 20, 20));
        // What did not change comes from the frame before.
        AssertNear(new SKColor(220, 40, 40), Close(two, 5, 5));
        AssertNear(new SKColor(30, 90, 200), Close(two, 70, 50));
    }

    [Fact]
    public void ManyColors_AndALargeFrame_StillDecode()
    {
        // A gradient with more than 255 colors, large enough to fill the LZW table several times.
        const int W = 320, H = 200;
        var pixels = Frame(W, H, (x, y) => new SKColor((byte)(x * 255 / W), (byte)(y * 255 / H), (byte)((x + y) % 256)));
        using var stream = new MemoryStream();
        var gif = new GifEncoder(stream, W, H);
        gif.AddFrame(pixels, W * 4, 10);
        gif.Finish();
        stream.Position = 0;
        using var bitmap = SKBitmap.Decode(stream);
        Assert.Equal((W, H), (bitmap.Width, bitmap.Height));
        var at = bitmap.GetPixel(160, 100);
        Assert.InRange(at.Red, 110, 145);
        Assert.InRange(at.Green, 110, 145);
    }
}
