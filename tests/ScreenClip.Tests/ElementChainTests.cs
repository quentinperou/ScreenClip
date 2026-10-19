using ScreenClip.Core;

namespace ScreenClip.Tests;

public class ElementChainTests
{
    private static readonly PixelRect Screen = new(0, 0, 1920, 1080);
    private static readonly PixelRect Window = new(0, 0, 1920, 1040);

    [Fact]
    public void Chain_GoesFromTheSmallestToTheWindow()
    {
        PixelRect[] path = [new(0, 120, 1920, 920), new(16, 190, 1338, 753), new(16, 190, 1337, 752), new(500, 500, 40, 40)];
        var chain = ElementChain.Build(path, Window, Screen);
        // The video and its player are the same box to the eye: one of them is kept.
        Assert.Equal([new(500, 500, 40, 40), new(16, 190, 1338, 753), new(0, 120, 1920, 920), Window], chain);
    }

    [Fact]
    public void TinyElements_AreLeftOut()
    {
        var chain = ElementChain.Build([new(100, 100, 600, 400), new(110, 110, 8, 30)], Window, Screen);
        Assert.Equal([new(100, 100, 600, 400), Window], chain);
    }

    [Fact]
    public void Elements_AreClippedToTheWindowAndTheScreen()
    {
        // A page much longer than the window (scrolled), on a window that goes past the screen.
        var window = new PixelRect(-8, -8, 1936, 1100);
        var chain = ElementChain.Build([new(0, -2000, 1905, 4452), new(600, 300, 400, 300)], window, Screen);
        Assert.Equal([new(600, 300, 400, 300), new(0, 0, 1905, 1080), new(0, 0, 1920, 1080)], chain);
    }

    [Fact]
    public void Level_StaysInTheChain()
    {
        var chain = ElementChain.Build([new(100, 100, 600, 400)], Window, Screen);
        Assert.Equal(0, ElementChain.Clamp(chain, -3));
        Assert.Equal(1, ElementChain.Clamp(chain, 9));
        Assert.Equal(0, ElementChain.Clamp([], 2));
    }

    [Fact]
    public void BoxesSeenOnThePicture_NestWithTheElements()
    {
        // The app exposes only its page; the picture shows a panel and a card in it.
        PixelRect[] elements = [new(0, 120, 1920, 920)];
        PixelRect[] boxes = [new(1369, 190, 520, 640), new(1380, 300, 300, 90)];
        var chain = ElementChain.Build(elements, boxes, Window, Screen);
        Assert.Equal([new(1380, 300, 300, 90), new(1369, 190, 520, 640), new(0, 120, 1920, 920), Window], chain);
    }

    [Fact]
    public void BoxesThatCrossAnElementOrEachOther_AreLeftOut()
    {
        PixelRect[] elements = [new(100, 100, 800, 600), new(200, 200, 300, 200)];
        // Across the 300 x 200 element; then two boxes across each other; one good box inside the element.
        PixelRect[] boxes = [new(150, 150, 300, 200), new(220, 220, 200, 100), new(260, 210, 200, 100), new(270, 250, 40, 40)];
        var chain = ElementChain.Build(elements, boxes, Window, Screen);
        Assert.Equal([new(270, 250, 40, 40), new(200, 200, 300, 200), new(100, 100, 800, 600), Window], chain);
    }

    [Fact]
    public void WindowOffScreen_GivesNothing()
    {
        Assert.Empty(ElementChain.Build([new(3000, 0, 100, 100)], new PixelRect(3000, 0, 500, 500), Screen));
    }
}
