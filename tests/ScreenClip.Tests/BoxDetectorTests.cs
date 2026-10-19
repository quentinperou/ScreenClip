using ScreenClip.Core;
using SkiaSharp;

namespace ScreenClip.Tests;

public class BoxDetectorTests
{
    private static readonly SKColor Page = new(15, 15, 15);

    // A screen picture drawn with SkiaSharp, read back as the 32-bit pixels the app grabs.
    private static BoxDetector Picture(int width, int height, Action<SKCanvas> draw)
    {
        using var bitmap = new SKBitmap(new SKImageInfo(width, height, SKColorType.Bgra8888, SKAlphaType.Premul));
        using (var canvas = new SKCanvas(bitmap))
        {
            canvas.Clear(Page);
            draw(canvas);
        }
        return new BoxDetector(bitmap.GetPixelSpan(), width, height, bitmap.RowBytes);
    }

    private static void Fill(SKCanvas canvas, SKRect rect, SKColor color, float radius = 0)
    {
        using var paint = new SKPaint { Color = color, IsAntialias = true };
        canvas.DrawRoundRect(rect, radius, radius, paint);
    }

    private static bool Near(PixelRect box, int x, int y, int width, int height, int tolerance = 1) =>
        Math.Abs(box.X - x) <= tolerance && Math.Abs(box.Y - y) <= tolerance
        && Math.Abs(box.Right - (x + width)) <= tolerance && Math.Abs(box.Bottom - (y + height)) <= tolerance;

    [Fact]
    public void AFilledBlock_IsFound()
    {
        // A video player: black on the dark page of YouTube.
        var detector = Picture(400, 300, c => Fill(c, SKRect.Create(50, 40, 200, 120), SKColors.Black));
        Assert.Contains(new PixelRect(50, 40, 200, 120), detector.BoxesAt(100, 100));
    }

    [Fact]
    public void RoundedCorners_AreFollowed()
    {
        var detector = Picture(400, 300, c => Fill(c, SKRect.Create(60, 50, 220, 140), new SKColor(40, 40, 40), radius: 12));
        Assert.Contains(detector.BoxesAt(150, 120), b => Near(b, 60, 50, 220, 140));
    }

    [Fact]
    public void NestedBlocks_AreAllFound_TheLargestFirst()
    {
        var detector = Picture(400, 300, c =>
        {
            // A panel with a frame line, a card inside it.
            using var line = new SKPaint { Color = new SKColor(70, 70, 70), Style = SKPaintStyle.Stroke, StrokeWidth = 1 };
            c.DrawRect(20.5f, 20.5f, 319, 239, line);
            Fill(c, SKRect.Create(60, 60, 100, 70), new SKColor(45, 45, 45));
        });
        var inCard = detector.BoxesAt(100, 90);
        Assert.True(inCard.Count >= 2);
        Assert.True(Near(inCard[0], 20, 20, 320, 240), $"the panel first: {inCard[0]}");
        Assert.Contains(new PixelRect(60, 60, 100, 70), inCard);

        var inPanel = detector.BoxesAt(250, 200);
        Assert.DoesNotContain(new PixelRect(60, 60, 100, 70), inPanel);
        Assert.Contains(inPanel, b => Near(b, 20, 20, 320, 240));
    }

    [Fact]
    public void TwoCardsSideBySide_StayTwo()
    {
        var detector = Picture(400, 200, c =>
        {
            Fill(c, SKRect.Create(20, 20, 100, 100), new SKColor(50, 50, 50));
            Fill(c, SKRect.Create(136, 20, 100, 100), new SKColor(50, 50, 50));
        });
        var boxes = detector.BoxesAt(60, 60);
        Assert.Equal([new PixelRect(20, 20, 100, 100)], boxes);
    }

    [Fact]
    public void Text_IsNotABlock()
    {
        var detector = Picture(500, 200, c =>
        {
            using var font = new SKFont(SKTypeface.Default, 16);
            using var paint = new SKPaint { Color = SKColors.White, IsAntialias = true };
            for (int line = 0; line < 6; line++)
                c.DrawText("Lorem ipsum dolor sit amet, consectetur adipiscing elit", 10, 30 + line * 24, font, paint);
        });
        Assert.Empty(detector.BoxesAt(120, 60));
    }

    [Fact]
    public void APointOutside_GivesNothing()
    {
        var detector = Picture(300, 200, c => Fill(c, SKRect.Create(50, 50, 100, 80), SKColors.Black));
        Assert.Empty(detector.BoxesAt(250, 20));
        Assert.Empty(detector.BoxesAt(-1, 5));
        Assert.Empty(detector.BoxesAt(300, 5));
    }
}
