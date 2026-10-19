using ScreenClip.Core.Composition;
using SkiaSharp;

namespace ScreenClip.Tests;

public class ClipLayoutTests
{
    [Fact]
    public void Auto_AddsTheMarginAndTheBar_AroundTheCapture()
    {
        var layout = ClipLayout.Measure(1000, 600, new ClipStyle { Padding = 10, Frame = FrameKind.Browser, Ratio = AspectRatio.Auto });
        // Margin: 10 % of the mean side (800) = 80; bar: 40 px (a capture under 1600 px keeps the 100 % size).
        Assert.Equal(40, layout.BarHeight);
        Assert.Equal((1000 + 160, 600 + 40 + 160), (layout.Width, layout.Height));
        Assert.Equal((80, 80, 1000, 640), (layout.WindowX, layout.WindowY, layout.WindowWidth, layout.WindowHeight));
    }

    [Theory]
    [InlineData(AspectRatio.Square, 1.0)]
    [InlineData(AspectRatio.Wide, 16.0 / 9)]
    [InlineData(AspectRatio.Story, 9.0 / 16)]
    [InlineData(AspectRatio.Portrait, 4.0 / 5)]
    public void FixedRatio_WidensOrHeightens_WithoutShrinkingTheMargin(AspectRatio ratio, double expected)
    {
        var style = new ClipStyle { Padding = 5, Frame = FrameKind.None, Ratio = ratio };
        var layout = ClipLayout.Measure(1200, 700, style);
        var auto = ClipLayout.Measure(1200, 700, style with { Ratio = AspectRatio.Auto });
        Assert.Equal(expected, (double)layout.Width / layout.Height, 2);
        Assert.True(layout.Width >= auto.Width && layout.Height >= auto.Height);
        // The window stays in the middle, at its own size.
        Assert.Equal((1200, 700), (layout.WindowWidth, layout.WindowHeight));
        Assert.Equal((layout.Width - 1200) / 2, layout.WindowX);
    }

    [Fact]
    public void LargeCaptures_GetALargerBarAndCorners()
    {
        var style = new ClipStyle { Frame = FrameKind.Windows, Radius = 10 };
        var small = ClipLayout.Measure(800, 500, style);
        var large = ClipLayout.Measure(3840, 2160, style);
        Assert.Equal(36, small.BarHeight);
        Assert.Equal(10, small.Radius);
        Assert.Equal(2.4, large.Scale, 3);
        Assert.Equal(86, large.BarHeight);
        Assert.Equal(24, large.Radius, 3);
    }

    [Theory]
    [InlineData(FrameKind.Laptop, 1200, 750)]
    [InlineData(FrameKind.Phone, 600, 1300)]
    [InlineData(FrameKind.Phone, 1300, 600)]
    public void Devices_GrowAroundTheCapture(FrameKind frame, int width, int height)
    {
        var layout = ClipLayout.Measure(width, height, new ClipStyle { Frame = frame, Padding = 5 });
        Assert.Equal(0, layout.BarHeight);
        Assert.True(layout.Bezel >= 10);
        // The capture sits inside the device, which sits inside the picture.
        Assert.True(layout.CaptureX > layout.WindowX && layout.CaptureY > layout.WindowY);
        Assert.True(layout.CaptureX + width < layout.WindowX + layout.WindowWidth);
        Assert.True(layout.CaptureY + height < layout.WindowY + layout.WindowHeight);
        Assert.True(layout.WindowX > 0 && layout.WindowX + layout.WindowWidth < layout.Width);
        if (frame == FrameKind.Laptop)
            Assert.True(layout.WindowY + layout.WindowHeight - (layout.CaptureY + height) > layout.Bezel, "the base below the screen");
    }

    [Fact]
    public void NoFrame_HasNoBar()
    {
        Assert.Equal(0, ClipLayout.Measure(500, 500, new ClipStyle { Frame = FrameKind.None }).BarHeight);
    }

    [Fact]
    public void Normalized_BringsNumbersBackInRange()
    {
        var style = new ClipStyle { Background = "nope", Padding = 99, Radius = -3, Shadow = 300, FrameText = null!, TurnDegrees = -90, TiltDegrees = 45 }.Normalized();
        Assert.Equal(Backgrounds.All[0].Id, style.Background);
        Assert.Equal((ClipStyle.MaxPadding, 0, 100, ""), (style.Padding, style.Radius, style.Shadow, style.FrameText));
        Assert.Equal((-ClipStyle.MaxTurn, ClipStyle.MaxTilt), (style.TurnDegrees, style.TiltDegrees));
    }

    [Fact]
    public void Presets_AreFoundBack_WhateverTheText()
    {
        var post = StylePresets.All.Single(p => p.Id == "post").Style;
        Assert.Equal("post", StylePresets.IdOf(post with { FrameText = "example.com" }));
        Assert.Null(StylePresets.IdOf(post with { Padding = post.Padding + 1 }));
    }
}

public class PerspectiveTests
{
    private static readonly SKRect Window = SKRect.Create(100, 50, 400, 300);

    [Fact]
    public void RectToQuad_SendsEachCornerToItsPlace()
    {
        SKPoint[] quad = [new(120, 70), new(480, 40), new(470, 380), new(110, 330)];
        var matrix = Perspective.RectToQuad(Window, quad);
        SKPoint[] corners = [new(Window.Left, Window.Top), new(Window.Right, Window.Top), new(Window.Right, Window.Bottom), new(Window.Left, Window.Bottom)];
        for (int i = 0; i < 4; i++)
        {
            var mapped = matrix.MapPoint(corners[i]);
            Assert.Equal(quad[i].X, mapped.X, 2);
            Assert.Equal(quad[i].Y, mapped.Y, 2);
        }
    }

    [Theory]
    [InlineData(-16, 4)]
    [InlineData(16, 4)]
    [InlineData(-30, -20)]
    [InlineData(30, 20)]
    public void Turn_StaysInsideTheWindowPlace_AndTurnsTheRightWay(int turn, int tilt)
    {
        var corners = Perspective.TiltedCorners(Window, turn, tilt);
        Assert.All(corners, c => Assert.True(Window.Contains(c.X + (c.X > Window.MidX ? -0.01f : 0.01f), c.Y + (c.Y > Window.MidY ? -0.01f : 0.01f))));
        float leftEdge = corners[3].Y - corners[0].Y, rightEdge = corners[2].Y - corners[1].Y;
        float topEdge = corners[1].X - corners[0].X, bottomEdge = corners[2].X - corners[3].X;
        // Turned to the left, the right edge is farther, so shorter; seen from above, the bottom edge is closer, so longer.
        Assert.True(turn < 0 ? rightEdge < leftEdge : leftEdge < rightEdge);
        Assert.True(tilt > 0 ? bottomEdge > topEdge : bottomEdge < topEdge);
    }

    [Fact]
    public void NoTilt_KeepsTheRectangle()
    {
        var corners = Perspective.TiltedCorners(Window, 0, 0);
        Assert.Equal(new SKPoint(Window.Left, Window.Top), corners[0]);
        Assert.Equal(new SKPoint(Window.Right, Window.Bottom), corners[2]);
    }
}

public class ComposerTests
{
    // A 200 x 100 capture, plain green.
    private static SKImage Capture()
    {
        using var surface = SKSurface.Create(new SKImageInfo(200, 100));
        surface.Canvas.Clear(new SKColor(0, 200, 0));
        return surface.Snapshot();
    }

    private static SKColor PixelAt(SKImage image, int x, int y)
    {
        using var bitmap = SKBitmap.FromImage(image);
        return bitmap.GetPixel(x, y);
    }

    [Fact]
    public void Render_HasTheLayoutSize_TheCaptureInTheMiddle_AndTheBackgroundAround()
    {
        using var capture = Capture();
        var style = new ClipStyle { Background = "white", Padding = 20, Shadow = 0, Frame = FrameKind.None, Radius = 0 };
        using var picture = Composer.Render(capture, style);
        var layout = ClipLayout.Measure(200, 100, style);
        Assert.Equal((layout.Width, layout.Height), (picture.Width, picture.Height));
        Assert.Equal(new SKColor(255, 255, 255), PixelAt(picture, 2, 2));
        Assert.Equal(new SKColor(0, 200, 0), PixelAt(picture, picture.Width / 2, picture.Height / 2));
    }

    [Fact]
    public void Preview_IsTheScaledPicture()
    {
        using var capture = Capture();
        var style = new ClipStyle { Ratio = AspectRatio.Square };
        using var picture = Composer.Render(capture, style, 0.5f);
        var layout = ClipLayout.Measure(200, 100, style);
        Assert.Equal(Math.Round(layout.Width * 0.5), picture.Width);
        Assert.Equal(picture.Width, picture.Height);
    }

    [Fact]
    public void Transparent_StaysTransparent_InPng_AndBecomesWhite_InJpeg()
    {
        using var capture = Capture();
        using var picture = Composer.Render(capture, new ClipStyle { Background = "transparent", Shadow = 0, Padding = 20 });
        Assert.Equal(0, PixelAt(picture, 1, 1).Alpha);

        using var png = SKImage.FromEncodedData(Composer.Encode(picture, ExportFormat.Png));
        Assert.Equal(0, PixelAt(png, 1, 1).Alpha);
        using var jpeg = SKImage.FromEncodedData(Composer.Encode(picture, ExportFormat.Jpeg));
        var corner = PixelAt(jpeg, 1, 1);
        Assert.True(corner.Red > 245 && corner.Green > 245 && corner.Blue > 245);
        Assert.NotNull(SKImage.FromEncodedData(Composer.Encode(picture, ExportFormat.Webp)));
    }

    [Theory]
    [InlineData(FrameKind.Browser, false, 0, "blur")]
    [InlineData(FrameKind.MacWindow, true, -16, "night")]
    [InlineData(FrameKind.Windows, false, 25, "image")]
    [InlineData(FrameKind.Laptop, true, -10, "ocean")]
    [InlineData(FrameKind.Phone, false, 0, "white")]
    public void EveryCombination_Renders(FrameKind frame, bool dark, int turn, string background)
    {
        using var capture = Capture();
        var style = new ClipStyle { Frame = frame, DarkFrame = dark, TurnDegrees = turn, TiltDegrees = turn / 4, Background = background, FrameText = "example.com — a very long address that does not fit" };
        using var picture = Composer.Render(capture, style, 1, backgroundImage: capture);
        Assert.True(picture.Width > 200 && picture.Height > 100);
    }
}
