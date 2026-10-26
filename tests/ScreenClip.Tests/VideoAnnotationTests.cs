using ScreenClip.Core;
using ScreenClip.Core.Annotations;
using ScreenClip.Core.Video;
using SkiaSharp;

namespace ScreenClip.Tests;

public class PointerTrackTests
{
    private static TimeSpan S(double seconds) => TimeSpan.FromSeconds(seconds);

    private static PointerShape Arrow()
    {
        // 4 × 6 white, opaque: enough to see where it is drawn.
        var pixels = Enumerable.Repeat((byte)0xFF, 4 * 6 * 4).ToArray();
        return new PointerShape(4, 6, 0, 0, pixels);
    }

    [Fact]
    public void BetweenSamples_ThePointerIsOnItsWay()
    {
        var track = new PointerTrack([new(S(0), 0, 0, 0, false), new(S(0.1), 100, 50, 0, false)], [Arrow()]);
        var state = track.At(S(0.05));
        Assert.Equal(50, state.Position.X, 3);
        Assert.Equal(25, state.Position.Y, 3);
        Assert.True(state.Visible);
        // Before the first sample it is where it started; after the last, where it stopped.
        Assert.Equal(new SKPoint(0, 0), track.At(S(-1)).Position);
        Assert.Equal(new SKPoint(100, 50), track.At(S(9)).Position);
    }

    [Fact]
    public void AfterAStill_ThePointerDoesNotGlide()
    {
        // Two samples 2 s apart: the pointer jumped (it was still, then moved quickly), it did not crawl in between.
        var track = new PointerTrack([new(S(0), 0, 0, 0, false), new(S(2), 400, 0, 0, false)], [Arrow()]);
        Assert.Equal(0, track.At(S(1)).Position.X);
    }

    [Fact]
    public void Clicks_AreFound_FromTheButton()
    {
        var track = new PointerTrack(
        [
            new(S(0), 10, 10, 0, false), new(S(0.5), 20, 20, 0, true), new(S(0.6), 20, 20, 0, false),
            new(S(1), 30, 30, 0, true),
        ], [Arrow()]);
        Assert.Equal(2, track.Clicks.Count);
        Assert.Equal((S(0.5), S(0.6), new SKPoint(20, 20)), (track.Clicks[0].Down, track.Clicks[0].Up, track.Clicks[0].At));
        // Still down at the end: up at the last sample.
        Assert.Equal(S(1), track.Clicks[1].Up);
        Assert.True(track.At(S(0.55)).Pressed);
        Assert.False(track.At(S(0.7)).Pressed);
    }

    [Fact]
    public void Smoothing_TakesTheShakesOut_AndKeepsTheWay()
    {
        // A line from 0 to 300 in 1 s, shaken by ±3 px every sample.
        var samples = Enumerable.Range(0, 61).Select(i => new PointerSample(S(i / 60.0), i * 5, 100 + (i % 2 == 0 ? 3 : -3), 0, false));
        var track = new PointerTrack(samples, [Arrow()]);
        double raw = 0, smooth = 0;
        for (int i = 10; i < 50; i++)
        {
            raw += Math.Abs(track.At(S(i / 60.0)).Position.Y - 100);
            smooth += Math.Abs(track.At(S(i / 60.0), smooth: true).Position.Y - 100);
        }
        Assert.True(smooth < raw / 4, $"smooth {smooth}, raw {raw}");
        // No lag: in the middle of the way, it is where the hand was.
        Assert.Equal(track.At(S(0.5)).Position.X, track.At(S(0.5), smooth: true).Position.X, 0);
    }

    [Fact]
    public void AHiddenPointer_IsNotDrawn()
    {
        var track = new PointerTrack([new(S(0), 0, 0, 0, false), new(S(1), 0, 0, -1, false)], [Arrow()]);
        Assert.True(track.At(S(0.5)).Visible);
        Assert.False(track.At(S(1.5)).Visible);
    }
}

public class VideoTimingTests
{
    private static TimeSpan S(double seconds) => TimeSpan.FromSeconds(seconds);

    private static readonly TimeSpan Duration = S(10);

    [Fact]
    public void AnAnnotation_ShowsInItsTime_OrAlways()
    {
        var always = new Annotation { Kind = AnnotationKind.Rectangle };
        var timed = always with { Time = new ShowTime(S(2), S(4)) };
        Assert.True(always.IsShownAt(S(9)));
        Assert.False(timed.IsShownAt(S(1.9)));
        Assert.True(timed.IsShownAt(S(2)));
        Assert.False(timed.IsShownAt(S(4)));
        var layer = AnnotationLayer.Empty.Add(always, out _).Add(timed, out _);
        Assert.Single(layer.ShownAt(S(5)));
        Assert.Equal(2, layer.ShownAt(S(3)).Count);
    }

    [Fact]
    public void APointer_GoesFromAToB_AndClicksThere()
    {
        var motion = new PointerMotion { From = new(0, 0), Curved = false, Eased = false, Leave = S(1), Arrive = S(3), Click = ClickEffect.Ripple };
        var pointer = new Annotation { Kind = AnnotationKind.Pointer, Points = [new(200, 100)], Motion = motion };
        Assert.Equal(new SKPoint(0, 0), VideoTiming.PositionAt(pointer, S(0.5)));
        Assert.Equal(new SKPoint(100, 50), VideoTiming.PositionAt(pointer, S(2)));
        Assert.Equal(new SKPoint(200, 100), VideoTiming.PositionAt(pointer, S(4)));
        Assert.Null(VideoTiming.Posed(pointer, S(2.9)).Click);
        var posed = VideoTiming.Posed(pointer, S(3.1));
        Assert.Equal(new SKPoint(200, 100), posed.Start);
        Assert.NotNull(posed.Click);
        Assert.Null(VideoTiming.Posed(pointer, S(4)).Click);
        // Eased, it is slower at the ends: still near A a tenth of the way in.
        var eased = pointer with { Motion = motion with { Eased = true } };
        Assert.True(VideoTiming.PositionAt(eased, S(1.2)).X < 10);
        Assert.Equal(100, VideoTiming.PositionAt(eased, S(2)).X, 1);
    }

    [Fact]
    public void Moving_APointer_MovesItsWay()
    {
        var pointer = new Annotation
        {
            Kind = AnnotationKind.Pointer,
            Points = [new(200, 100)],
            Motion = VideoTiming.NewMotion(new SKPoint(0, 0), new SKPoint(200, 100), S(1), new AnnotationDefaults()),
        };
        var moved = pointer.MovedBy(10, 5);
        Assert.Equal(new SKPoint(10, 5), moved.Motion!.From);
        Assert.Equal(new SKPoint(210, 105), moved.Start);
        Assert.Equal(pointer.Motion!.Control1.X + 10, moved.Motion.Control1.X);
        Assert.Equal(4, AnnotationGeometry.Handles(pointer).Length);
        var from = AnnotationGeometry.WithHandle(pointer, HandleKind.MotionFrom, new SKPoint(50, 50), false);
        Assert.Equal(new SKPoint(50, 50), from.Motion!.From);
    }

    [Fact]
    public void Bars_MoveAndStretch_InsideTheVideo()
    {
        var annotation = new Annotation { Kind = AnnotationKind.Arrow, Time = new ShowTime(S(2), S(4)) };
        Assert.Equal(new ShowTime(S(5), S(7)), VideoTiming.TimeMovedBy(annotation, S(3), Duration).Time);
        // Never out of the video.
        Assert.Equal(new ShowTime(S(8), S(10)), VideoTiming.TimeMovedBy(annotation, S(30), Duration).Time);
        Assert.Equal(new ShowTime(S(0), S(2)), VideoTiming.TimeMovedBy(annotation, S(-30), Duration).Time);
        Assert.Equal(new ShowTime(S(1), S(4)), VideoTiming.WithShowStart(annotation, S(1), Duration).Time);
        Assert.Equal(S(4) - ShowTime.MinLength, VideoTiming.WithShowStart(annotation, S(5), Duration).Time!.Value.Start);
        // Stretched to the whole video, it follows the video again.
        Assert.Null(VideoTiming.WithShowEnd(VideoTiming.WithShowStart(annotation, S(0), Duration), S(10), Duration).Time);
        // A bar of the whole video, stretched: it gets a time.
        var always = annotation with { Time = null };
        Assert.Equal(new ShowTime(S(0), S(6)), VideoTiming.WithShowEnd(always, S(6), Duration).Time);
    }

    [Fact]
    public void AMotion_StaysInItsBar()
    {
        var pointer = new Annotation
        {
            Kind = AnnotationKind.Pointer,
            Points = [new(10, 10)],
            Time = new ShowTime(S(1), S(5)),
            Motion = new PointerMotion { Leave = S(2), Arrive = S(4) },
        };
        var shorter = VideoTiming.WithShowEnd(pointer, S(3), Duration);
        Assert.True(shorter.Motion!.Arrive <= S(3));
        Assert.True(shorter.Motion.Leave < shorter.Motion.Arrive);
        Assert.Equal(S(4) - ShowTime.MinLength, VideoTiming.WithLeave(pointer, S(9), Duration).Motion!.Leave);
        Assert.Equal(S(5), VideoTiming.WithArrive(pointer, S(9), Duration).Motion!.Arrive);
        var moved = VideoTiming.TimeMovedBy(pointer, S(1), Duration);
        Assert.Equal((S(3), S(5)), (moved.Motion!.Leave, moved.Motion.Arrive));
    }

    [Fact]
    public void TheFingerprint_ChangesOnlyWithWhatIsDrawn()
    {
        var track = new PointerTrack([new(S(0), 0, 0, 0, false), new(S(1), 0, 0, 0, false), new(S(1.1), 50, 0, 0, false)],
            [new PointerShape(1, 1, 0, 0, [0xFF, 0xFF, 0xFF, 0xFF])]);
        var layer = AnnotationLayer.Empty.Add(new Annotation { Kind = AnnotationKind.Rectangle, Time = new ShowTime(S(3), S(4)) }, out _);
        int still = VideoTiming.Fingerprint(layer, new VideoMoment(S(0.2), track));
        Assert.Equal(still, VideoTiming.Fingerprint(layer, new VideoMoment(S(0.8), track)));
        Assert.NotEqual(still, VideoTiming.Fingerprint(layer, new VideoMoment(S(1.05), track)));
        Assert.NotEqual(VideoTiming.Fingerprint(layer, new VideoMoment(S(2), track)), VideoTiming.Fingerprint(layer, new VideoMoment(S(3.5), track)));
    }

    [Fact]
    public void ClickEffects_LastTheirTime()
    {
        Assert.NotNull(ClickTiming.PhaseAt(S(1), S(1.2), S(1.1), ClickEffect.Halo));
        Assert.NotNull(ClickTiming.PhaseAt(S(1), S(1.2), S(1.5), ClickEffect.Halo));
        Assert.Null(ClickTiming.PhaseAt(S(1), S(1.2), S(1.7), ClickEffect.Halo));
        Assert.Null(ClickTiming.PhaseAt(S(1), S(1.2), S(1.7), ClickEffect.Ripple));
        Assert.Null(ClickTiming.PhaseAt(S(1), S(1.2), S(0.9), ClickEffect.Halo));
        Assert.Null(ClickTiming.PhaseAt(S(1), S(1.2), S(1.1), ClickEffect.None));
    }
}

public class RecordedPointerDrawingTests
{
    private static SKImage Blank(int width, int height)
    {
        using var surface = SKSurface.Create(new SKImageInfo(width, height, SKColorType.Bgra8888, SKAlphaType.Premul));
        surface.Canvas.Clear(SKColors.Black);
        return surface.Snapshot();
    }

    private static SKColor PixelAt(SKImage image, int x, int y)
    {
        using var bitmap = SKBitmap.FromImage(image);
        return bitmap.GetPixel(x, y);
    }

    private static PointerTrack Still(float x, float y, bool pressed = false)
    {
        var white = Enumerable.Repeat((byte)0xFF, 8 * 12 * 4).ToArray();
        return new PointerTrack([new(TimeSpan.Zero, x, y, 0, pressed), new(TimeSpan.FromSeconds(2), x, y, 0, false)],
            [new PointerShape(8, 12, 0, 0, white)]);
    }

    [Fact]
    public void ThePointer_IsDrawnWhereItWas_AsRecorded()
    {
        using var frame = Blank(100, 80);
        using var flat = AnnotationRenderer.Flatten(frame, AnnotationLayer.Empty, new VideoMoment(TimeSpan.FromSeconds(1), Still(40, 30)));
        Assert.Equal(SKColors.White, PixelAt(flat, 43, 35));
        Assert.Equal(SKColors.Black, PixelAt(flat, 60, 60));
        // Twice as large.
        var layer = AnnotationLayer.Empty with { Cursor = new RecordedPointerOptions { Size = 200 } };
        using var large = AnnotationRenderer.Flatten(frame, layer, new VideoMoment(TimeSpan.FromSeconds(1), Still(40, 30)));
        Assert.Equal(SKColors.White, PixelAt(large, 54, 50));
    }

    [Fact]
    public void AHiddenLayer_DrawsNothing_AndAPictureHasNoPointer()
    {
        using var frame = Blank(100, 80);
        var hidden = AnnotationLayer.Empty with { Cursor = new RecordedPointerOptions { Style = RecordedPointerStyle.Hidden } };
        using var flat = AnnotationRenderer.Flatten(frame, hidden, new VideoMoment(TimeSpan.FromSeconds(1), Still(40, 30)));
        Assert.Equal(SKColors.Black, PixelAt(flat, 43, 35));
        using var picture = AnnotationRenderer.Flatten(frame, AnnotationLayer.Empty);
        Assert.Equal(SKColors.Black, PixelAt(picture, 43, 35));
    }

    [Fact]
    public void AClick_ShowsAHalo()
    {
        using var frame = Blank(200, 200);
        var moment = new VideoMoment(TimeSpan.FromSeconds(0.1), Still(100, 100, pressed: true));
        var layer = AnnotationLayer.Empty with { Cursor = new RecordedPointerOptions { Style = RecordedPointerStyle.Windows } };
        using var flat = AnnotationRenderer.Flatten(frame, layer, moment);
        // Left of the tip, under no pointer: the yellow of the halo.
        var halo = PixelAt(flat, 96, 104);
        Assert.True(halo.Red > 40 && halo.Blue < halo.Red, $"{halo}");
    }

    [Fact]
    public void AnAnnotation_OutOfItsTime_IsNotDrawn()
    {
        using var frame = Blank(100, 80);
        var mask = new Annotation { Kind = AnnotationKind.Mask, Mask = MaskStyle.Solid, Points = [new(0, 0), new(100, 80)], Time = new ShowTime(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2)) };
        var layer = AnnotationLayer.Empty.Add(mask, out _);
        using var before = AnnotationRenderer.Flatten(frame, layer, new VideoMoment(TimeSpan.FromSeconds(0.5)));
        using var during = AnnotationRenderer.Flatten(frame, layer, new VideoMoment(TimeSpan.FromSeconds(1.5)));
        Assert.Equal(SKColors.Black, PixelAt(before, 50, 40));
        Assert.NotEqual(SKColors.Black, PixelAt(during, 50, 40));
    }
}

public class MaskTests
{
    private static SKColor PixelAt(SKImage image, int x, int y)
    {
        using var bitmap = SKBitmap.FromImage(image);
        return bitmap.GetPixel(x, y);
    }

    [Theory]
    [InlineData(MaskStyle.Blur)]
    [InlineData(MaskStyle.Pixelate)]
    public void AMask_HidesTheAnnotationsUnderIt_NotThoseOver(MaskStyle style)
    {
        using var surface = SKSurface.Create(new SKImageInfo(200, 200, SKColorType.Bgra8888, SKAlphaType.Premul));
        surface.Canvas.Clear(SKColors.Black);
        using var capture = surface.Snapshot();
        // A thick white line across, then the mask over its middle, then a red line over the mask.
        var under = new Annotation { Kind = AnnotationKind.Line, Color = Palette.White, Size = StrokeSize.Large, Points = [new(0, 100), new(200, 100)] };
        var mask = new Annotation { Kind = AnnotationKind.Mask, Mask = style, Points = [new(50, 50), new(150, 150)] };
        var over = new Annotation { Kind = AnnotationKind.Line, Color = Palette.Red, Size = StrokeSize.Large, Points = [new(100, 0), new(100, 200)] };
        var layer = AnnotationLayer.Empty.Add(under, out _).Add(mask, out _).Add(over, out _);
        using var flat = AnnotationRenderer.Flatten(capture, layer);
        // The white line, hidden: no longer white in the mask, but not the black of the capture either.
        var hidden = PixelAt(flat, 70, 100);
        Assert.True(hidden.Red is > 10 and < 250, $"{hidden}");
        // Outside the mask it is untouched, and the red line over the mask is sharp.
        Assert.Equal(SKColors.White, PixelAt(flat, 20, 100));
        Assert.Equal(new SKColor(Palette.Red), PixelAt(flat, 100, 70));
    }
}

public class HiddenLayerTests
{
    [Fact]
    public void AHiddenAnnotation_IsNeitherDrawnNorPicked()
    {
        using var surface = SKSurface.Create(new SKImageInfo(100, 100, SKColorType.Bgra8888, SKAlphaType.Premul));
        surface.Canvas.Clear(SKColors.Black);
        using var capture = surface.Snapshot();
        var mask = new Annotation { Kind = AnnotationKind.Mask, Mask = MaskStyle.Solid, Points = [new(0, 0), new(100, 100)], Hidden = true };
        var layer = AnnotationLayer.Empty.Add(mask, out var added);
        using var flat = AnnotationRenderer.Flatten(capture, layer);
        using var bitmap = SKBitmap.FromImage(flat);
        Assert.Equal(SKColors.Black, bitmap.GetPixel(50, 50));
        Assert.Null(layer.HitTest(new SKPoint(50, 50), 1, 2));
        Assert.Empty(layer.ShownAt(TimeSpan.FromSeconds(1)));
        // Shown again, it is there as before.
        Assert.NotNull(layer.Replace(added with { Hidden = false }).HitTest(new SKPoint(50, 50), 1, 2));
    }

    [Fact]
    public void TheHiddenStyleOfOld_BecomesTheEye()
    {
        var old = new RecordedPointerOptions { Style = RecordedPointerStyle.Hidden }.Normalized();
        Assert.Equal((RecordedPointerStyle.AsRecorded, true, false), (old.Style, old.Hidden, old.Shows));
        Assert.True(new RecordedPointerOptions().Shows);
    }
}
