using ScreenClip.Core;
using ScreenClip.Core.Annotations;
using SkiaSharp;

namespace ScreenClip.Tests;

public class AnnotationLayerTests
{
    private static Annotation Rect(float x, float y, float w, float h) =>
        new AnnotationDefaults().Create(AnnotationKind.Rectangle, new SKPoint(x, y), new SKPoint(x + w, y + h));

    [Fact]
    public void Add_GivesEachAnnotationItsOwnId_AndPutsItOnTop()
    {
        var layer = AnnotationLayer.Empty.Add(Rect(0, 0, 10, 10), out var first).Add(Rect(5, 5, 10, 10), out var second);
        Assert.Equal((1, 2, 3), (first.Id, second.Id, layer.NextId));
        Assert.Equal([1, 2], layer.Items.Select(a => a.Id));
        Assert.True(AnnotationLayer.Empty.IsEmpty);
        Assert.False(layer.IsEmpty);
    }

    [Fact]
    public void Replace_Remove_BringToFront_FollowTheId()
    {
        var layer = AnnotationLayer.Empty.Add(Rect(0, 0, 10, 10), out var a).Add(Rect(20, 0, 10, 10), out var b);
        var moved = layer.Replace(a.MovedBy(5, 0));
        Assert.Equal(new SKPoint(5, 0), moved.Find(a.Id)!.Start);
        Assert.Equal([b.Id, a.Id], moved.BringToFront(a.Id).Items.Select(x => x.Id));
        Assert.Equal([b.Id], moved.Remove(a.Id).Items.Select(x => x.Id));
        // An unknown id changes nothing.
        Assert.Same(layer, layer.Remove(99));
        Assert.Same(layer, layer.BringToFront(b.Id));
    }

    [Fact]
    public void MoveTo_ReordersTheLayers()
    {
        var layer = AnnotationLayer.Empty.Add(Rect(0, 0, 10, 10), out var a).Add(Rect(20, 0, 10, 10), out var b).Add(Rect(40, 0, 10, 10), out var c);
        Assert.Equal([b.Id, c.Id, a.Id], layer.MoveTo(a.Id, 2).Items.Select(x => x.Id));
        Assert.Equal([c.Id, a.Id, b.Id], layer.MoveTo(c.Id, 0).Items.Select(x => x.Id));
        Assert.Equal([a.Id, c.Id, b.Id], layer.MoveTo(b.Id, 9).Items.Select(x => x.Id));
        Assert.Same(layer, layer.MoveTo(b.Id, 1));
        Assert.Same(layer, layer.MoveTo(99, 0));
    }

    [Fact]
    public void NextNumber_IsOneMoreThanTheHighest()
    {
        var defaults = new AnnotationDefaults();
        var layer = AnnotationLayer.Empty;
        Assert.Equal(1, layer.NextNumber);
        layer = layer.Add(defaults.Create(AnnotationKind.Number, new SKPoint(10, 10)) with { Number = 1 }, out _)
            .Add(defaults.Create(AnnotationKind.Number, new SKPoint(40, 10)) with { Number = 4 }, out _);
        Assert.Equal(5, layer.NextNumber);
    }

    [Fact]
    public void Visible_IsTheCropInsideTheCapture_OrAllOfIt()
    {
        Assert.Equal(new PixelRect(0, 0, 800, 600), AnnotationLayer.Empty.Visible(800, 600));
        var cropped = AnnotationLayer.Empty with { Crop = new PixelRect(100, 50, 900, 300) };
        Assert.Equal(new PixelRect(100, 50, 700, 300), cropped.Visible(800, 600));
        // A crop outside the capture keeps the whole capture.
        Assert.Equal(new PixelRect(0, 0, 800, 600), (AnnotationLayer.Empty with { Crop = new PixelRect(900, 0, 50, 50) }).Visible(800, 600));
    }

    [Fact]
    public void HitTest_FindsTheOneOnTop()
    {
        var filled = new AnnotationDefaults { Shape = ShapeStyle.Filled };
        var layer = AnnotationLayer.Empty
            .Add(filled.Create(AnnotationKind.Rectangle, new SKPoint(0, 0), new SKPoint(100, 100)), out var below)
            .Add(filled.Create(AnnotationKind.Rectangle, new SKPoint(50, 50), new SKPoint(150, 150)), out var above);
        Assert.Equal(above.Id, layer.HitTest(new SKPoint(75, 75), 1, 2)!.Id);
        Assert.Equal(below.Id, layer.HitTest(new SKPoint(25, 25), 1, 2)!.Id);
        Assert.Null(layer.HitTest(new SKPoint(300, 300), 1, 2));
    }
}

public class AnnotationHistoryTests
{
    private static AnnotationLayer With(AnnotationLayer layer) =>
        layer.Add(new AnnotationDefaults().Create(AnnotationKind.Number, new SKPoint(1, 1)), out _);

    [Fact]
    public void UndoAndRedo_GoThroughTheSteps()
    {
        var history = new AnnotationHistory();
        var one = With(AnnotationLayer.Empty);
        var two = With(one);
        history.Commit(one);
        history.Commit(two);
        Assert.True(history.Undo());
        Assert.Same(one, history.Current);
        Assert.True(history.Undo());
        Assert.Same(AnnotationLayer.Empty, history.Current);
        Assert.False(history.Undo());
        Assert.True(history.Redo());
        Assert.Same(one, history.Current);
        // A new change drops what could be redone.
        history.Commit(With(one));
        Assert.False(history.CanRedo);
    }

    [Fact]
    public void ChangesWithTheSameKey_MakeOneStep()
    {
        var history = new AnnotationHistory();
        var start = With(AnnotationLayer.Empty);
        history.Commit(start);
        history.Commit(start with { Crop = new PixelRect(0, 0, 10, 10) }, "size");
        history.Commit(start with { Crop = new PixelRect(0, 0, 20, 20) }, "size");
        history.Commit(start with { Crop = new PixelRect(0, 0, 30, 30) }, "size");
        history.Undo();
        Assert.Same(start, history.Current);
    }

    [Fact]
    public void Reset_ForgetsTheSteps_AndTheHistoryKeepsAtMostItsLimit()
    {
        var history = new AnnotationHistory();
        var layer = AnnotationLayer.Empty;
        for (int i = 0; i < AnnotationHistory.MaxSteps + 20; i++)
        {
            layer = With(layer);
            history.Commit(layer);
        }
        int undone = 0;
        while (history.Undo())
            undone++;
        Assert.Equal(AnnotationHistory.MaxSteps, undone);
        history.Reset(AnnotationLayer.Empty);
        Assert.False(history.CanUndo || history.CanRedo);
    }
}

public class AnnotationGeometryTests
{
    private static readonly AnnotationDefaults Defaults = new();

    [Fact]
    public void Scale_FollowsLargeCaptures()
    {
        Assert.Equal(1, AnnotationGeometry.ScaleOf(1200, 800));
        Assert.Equal(2.4f, AnnotationGeometry.ScaleOf(3840, 2160), 3);
        Assert.Equal(3, AnnotationGeometry.ScaleOf(9000, 2000));
    }

    [Fact]
    public void AnOutline_IsHitOnItsEdge_NotInItsMiddle()
    {
        var outline = Defaults.Create(AnnotationKind.Rectangle, new SKPoint(0, 0), new SKPoint(200, 100));
        Assert.True(AnnotationGeometry.HitTest(outline, new SKPoint(201, 50), 1, 3));
        Assert.False(AnnotationGeometry.HitTest(outline, new SKPoint(100, 50), 1, 3));
        var filled = outline with { Shape = ShapeStyle.Filled };
        Assert.True(AnnotationGeometry.HitTest(filled, new SKPoint(100, 50), 1, 3));

        var ellipse = Defaults.Create(AnnotationKind.Ellipse, new SKPoint(0, 0), new SKPoint(200, 100));
        Assert.True(AnnotationGeometry.HitTest(ellipse, new SKPoint(100, 1), 1, 3));
        Assert.False(AnnotationGeometry.HitTest(ellipse, new SKPoint(100, 50), 1, 3));
    }

    [Fact]
    public void ALine_IsHitNearIt_AndAPenStrokeAlongItsPoints()
    {
        var line = Defaults.Create(AnnotationKind.Line, new SKPoint(0, 0), new SKPoint(100, 100));
        Assert.True(AnnotationGeometry.HitTest(line, new SKPoint(52, 50), 1, 3));
        Assert.False(AnnotationGeometry.HitTest(line, new SKPoint(80, 20), 1, 3));
        var pen = Defaults.Create(AnnotationKind.Pen, new SKPoint(0, 0), new SKPoint(50, 0), new SKPoint(50, 50));
        Assert.True(AnnotationGeometry.HitTest(pen, new SKPoint(51, 30), 1, 3));
        Assert.False(AnnotationGeometry.HitTest(pen, new SKPoint(20, 30), 1, 3));
    }

    [Fact]
    public void ACurvedArrow_PassesThroughItsBendHandle()
    {
        var arrow = new AnnotationDefaults { Arrow = ArrowStyle.Curved }.Create(AnnotationKind.Arrow, new SKPoint(0, 0), new SKPoint(200, 0));
        var handles = AnnotationGeometry.Handles(arrow);
        Assert.Equal([HandleKind.Start, HandleKind.Bend, HandleKind.End], handles.Select(h => h.Kind));
        var middle = handles[1].Point;
        Assert.True(AnnotationGeometry.HitTest(arrow, middle, 1, 1));
        // Dragging the handle further bends it further; back on the line straightens it.
        var bent = AnnotationGeometry.WithHandle(arrow, HandleKind.Bend, new SKPoint(100, middle.Y * 2), false);
        Assert.Equal(arrow.Bend * 2, bent.Bend, 3);
        Assert.Equal(0, AnnotationGeometry.WithHandle(arrow, HandleKind.Bend, new SKPoint(100, 0), false).Bend, 3);
        Assert.Equal(middle.Y * 2, AnnotationGeometry.CurveMiddle(bent).Y, 2);
    }

    [Fact]
    public void ACornerHandle_KeepsTheOppositeCorner_AndShiftMakesASquare()
    {
        var rect = Defaults.Create(AnnotationKind.Rectangle, new SKPoint(10, 10), new SKPoint(110, 60));
        var resized = AnnotationGeometry.WithHandle(rect, HandleKind.TopLeft, new SKPoint(0, 0), false);
        Assert.Equal(new SKRect(0, 0, 110, 60), resized.Rect);
        var square = AnnotationGeometry.WithHandle(rect, HandleKind.BottomRight, new SKPoint(200, 40), true);
        Assert.Equal(square.Rect.Width, square.Rect.Height);
        Assert.Equal(new SKPoint(10, 10), new SKPoint(square.Rect.Left, square.Rect.Top));
    }

    [Theory]
    [InlineData(100, 3, 100, 0)]      // nearly flat: flat
    [InlineData(3, 100, 0, 100)]      // nearly upright: upright
    [InlineData(100, 90, 95, 95)]     // nearly diagonal: diagonal, the drag projected on it
    public void Shift_GivesSteepsOf45Degrees(float x, float y, float expectedX, float expectedY)
    {
        var point = AnnotationGeometry.Constrain(new SKPoint(0, 0), new SKPoint(x, y), true);
        Assert.Equal(expectedX, point.X, 0);
        Assert.Equal(expectedY, point.Y, 0);
    }

    [Fact]
    public void ThePointer_HasItsTipOnItsPoint()
    {
        var pointer = Defaults.Create(AnnotationKind.Pointer, new SKPoint(300, 200)) with { Halo = false, PointerSize = 48 };
        var bounds = AnnotationGeometry.Bounds(pointer, 1);
        Assert.Equal(300, bounds.Left, 0);
        Assert.Equal(200, bounds.Top, 0);
        Assert.InRange(bounds.Height, 32, 48);
        // A larger pointer, and the halo around the tip.
        var large = pointer with { PointerSize = 96, Halo = true };
        Assert.True(AnnotationGeometry.Bounds(large, 1).Height > bounds.Height * 1.8);
        Assert.True(AnnotationGeometry.Bounds(large, 1).Left < 300);
    }

    [Fact]
    public void ATextBox_GrowsWithItsLines()
    {
        var one = Defaults.Create(AnnotationKind.Text, new SKPoint(0, 0)) with { Text = "Click here" };
        var two = one with { Text = "Click here\nthen there" };
        var boxOne = AnnotationGeometry.TextBox(one, 1);
        var boxTwo = AnnotationGeometry.TextBox(two, 1);
        Assert.True(boxOne.Width > 60);
        Assert.Equal(boxOne.Height + AnnotationGeometry.LineHeight(AnnotationGeometry.FontSize(one, 1)), boxTwo.Height, 1);
        // Empty, it still has room for the caret.
        Assert.True(AnnotationGeometry.TextBox(one with { Text = "" }, 1).Width > 0);
    }
}

public class AnnotationDefaultsTests
{
    [Fact]
    public void Create_TakesTheOptionsOfItsTool()
    {
        var defaults = new AnnotationDefaults { Color = Palette.Blue, MarkerColor = Palette.Green, TextSize = StrokeSize.Large, Arrow = ArrowStyle.Double };
        Assert.Equal(Palette.Blue, defaults.Create(AnnotationKind.Arrow).Color);
        Assert.Equal(ArrowStyle.Double, defaults.Create(AnnotationKind.Arrow).Arrow);
        Assert.Equal(Palette.Green, defaults.Create(AnnotationKind.Marker).Color);
        Assert.Equal(StrokeSize.Large, defaults.Create(AnnotationKind.Text).Size);
        Assert.Equal(StrokeSize.Medium, defaults.Create(AnnotationKind.Rectangle).Size);
    }

    [Fact]
    public void Follow_KeepsWhatTheSelectedOneWasGiven()
    {
        var defaults = new AnnotationDefaults();
        var marker = defaults.Create(AnnotationKind.Marker) with { Color = Palette.Blue, Size = StrokeSize.Large };
        var followed = defaults.Follow(marker);
        Assert.Equal((Palette.Blue, StrokeSize.Large), (followed.MarkerColor, followed.MarkerSize));
        // The other tools keep theirs.
        Assert.Equal((Palette.Red, StrokeSize.Medium), (followed.Color, followed.Size));
        var pointer = defaults.Create(AnnotationKind.Pointer) with { Pointer = PointerStyle.Hand, PointerSize = 96, Halo = false };
        Assert.Equal((PointerStyle.Hand, 96, false), (defaults.Follow(pointer).Pointer, defaults.Follow(pointer).PointerSize, defaults.Follow(pointer).Halo));
    }

    [Fact]
    public void Normalized_BringsBackWhatIsOutOfRange()
    {
        var odd = new AnnotationDefaults { PointerSize = 900, Shape = (ShapeStyle)42, Color = 0x00E5484D }.Normalized();
        Assert.Equal(AnnotationDefaults.MaxPointerSize, odd.PointerSize);
        Assert.Equal(ShapeStyle.Outline, odd.Shape);
        Assert.Equal(Palette.Red, odd.Color);
    }

    [Fact]
    public void LightColors_TakeDarkText()
    {
        Assert.True(Palette.IsLight(Palette.Yellow));
        Assert.True(Palette.IsLight(Palette.White));
        Assert.False(Palette.IsLight(Palette.Red));
        Assert.False(Palette.IsLight(Palette.Blue));
    }
}

public class AnnotationRendererTests
{
    private static readonly AnnotationDefaults Defaults = new();

    private static SKImage Plain(int width, int height, SKColor color)
    {
        using var surface = SKSurface.Create(new SKImageInfo(width, height, SKColorType.Bgra8888, SKAlphaType.Premul));
        surface.Canvas.Clear(color);
        return surface.Snapshot();
    }

    private static SKColor Pixel(SKImage image, int x, int y)
    {
        using var bitmap = SKBitmap.FromImage(image);
        return bitmap.GetPixel(x, y);
    }

    [Fact]
    public void Flatten_DrawsOnTheCapture_AndCutsItToTheCrop()
    {
        using var capture = Plain(400, 300, SKColors.White);
        var layer = AnnotationLayer.Empty
            .Add(Defaults.Create(AnnotationKind.Rectangle, new SKPoint(50, 50), new SKPoint(150, 150)), out _)
            with { Crop = new PixelRect(40, 40, 200, 150) };
        using var flat = AnnotationRenderer.Flatten(capture, layer);
        Assert.Equal((200, 150), (flat.Width, flat.Height));
        // The outline at x = 50 of the capture is at x = 10 of the cropped picture; inside stays white.
        Assert.Equal(new SKColor(Palette.Red), Pixel(flat, 10, 60));
        Assert.Equal(SKColors.White, Pixel(flat, 60, 60));
    }

    [Fact]
    public void ASpotlight_DarkensEverythingButItself()
    {
        using var capture = Plain(300, 200, SKColors.White);
        var layer = AnnotationLayer.Empty.Add(
            new AnnotationDefaults { Shape = ShapeStyle.Spotlight }.Create(AnnotationKind.Rectangle, new SKPoint(100, 50), new SKPoint(200, 150)), out _);
        using var flat = AnnotationRenderer.Flatten(capture, layer);
        Assert.Equal(SKColors.White, Pixel(flat, 150, 100));
        Assert.True(Pixel(flat, 20, 20).Red < 128);
    }

    [Fact]
    public void Masks_HideWhatIsUnder()
    {
        // Black and white stripes, one pixel wide: pixelated, they average to grey; a solid mask is dark.
        using var surface = SKSurface.Create(new SKImageInfo(240, 120, SKColorType.Bgra8888, SKAlphaType.Premul));
        surface.Canvas.Clear(SKColors.White);
        using (var black = new SKPaint { Color = SKColors.Black })
        {
            for (int x = 0; x < 240; x += 2)
                surface.Canvas.DrawRect(x, 0, 1, 120, black);
        }
        using var capture = surface.Snapshot();
        var pixelate = Defaults.Create(AnnotationKind.Mask, new SKPoint(0, 0), new SKPoint(120, 120));
        var solid = pixelate with { Mask = MaskStyle.Solid, Points = [new SKPoint(120, 0), new SKPoint(240, 120)] };
        var layer = AnnotationLayer.Empty.Add(pixelate, out _).Add(solid, out _);
        using var flat = AnnotationRenderer.Flatten(capture, layer);
        var grey = Pixel(flat, 30, 30);
        Assert.InRange(grey.Red, 100, 156);
        Assert.Equal(grey.Red, Pixel(flat, 31, 30).Red);
        Assert.True(Pixel(flat, 180, 60).Red < 40);
    }

    [Fact]
    public void TheMarker_DarkensWhatIsUnder_AsAHighlighter()
    {
        using var capture = Plain(200, 100, SKColors.White);
        var layer = AnnotationLayer.Empty.Add(Defaults.Create(AnnotationKind.Marker, new SKPoint(20, 50), new SKPoint(180, 50)), out _);
        using var flat = AnnotationRenderer.Flatten(capture, layer);
        var marked = Pixel(flat, 100, 50);
        // Yellow on white: red and green stay high, blue drops.
        Assert.True(marked.Red > 200 && marked.Green > 150 && marked.Blue < 150);
    }

    [Fact]
    public void ThePointer_IsDrawnAtItsTip()
    {
        using var capture = Plain(200, 200, new SKColor(0x30, 0x60, 0xC0));
        var pointer = Defaults.Create(AnnotationKind.Pointer, new SKPoint(50, 50)) with { Halo = false, PointerSize = 96 };
        using var flat = AnnotationRenderer.Flatten(capture, AnnotationLayer.Empty.Add(pointer, out _));
        // The Windows arrow is white inside, just below and right of its tip.
        var inside = Pixel(flat, 56, 75);
        Assert.True(inside.Red > 230 && inside.Green > 230 && inside.Blue > 230);
        Assert.Equal(new SKColor(0x30, 0x60, 0xC0), Pixel(flat, 40, 40));
    }

    [Fact]
    public void EveryKind_Draws()
    {
        using var capture = Plain(600, 400, SKColors.White);
        var layer = AnnotationLayer.Empty;
        foreach (var kind in Enum.GetValues<AnnotationKind>())
        {
            var annotation = Defaults.Create(kind, new SKPoint(100, 100), new SKPoint(300, 250)) with { Text = "Hello" };
            layer = layer.Add(annotation, out _);
        }
        using var flat = AnnotationRenderer.Flatten(capture, layer);
        Assert.Equal((600, 400), (flat.Width, flat.Height));
    }
}
