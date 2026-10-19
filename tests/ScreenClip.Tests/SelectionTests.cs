using ScreenClip.Core;

namespace ScreenClip.Tests;

public class SelectionTests
{
    // A 1920 x 1080 screen, and a second one on its left (negative coordinates).
    private static readonly PixelRect Main = new(0, 0, 1920, 1080);
    private static readonly PixelRect Left = new(-1280, 0, 1280, 1024);

    [Fact]
    public void Drag_IncludesBothPixels()
    {
        Assert.Equal(new PixelRect(10, 20, 11, 6), Selection.FromDrag(10, 20, 20, 25, false, Main));
        Assert.Equal(new PixelRect(10, 20, 1, 1), Selection.FromDrag(10, 20, 10, 20, false, Main));
    }

    [Fact]
    public void Drag_UpAndLeft_GivesTheSameRectangle()
    {
        Assert.Equal(Selection.FromDrag(10, 20, 20, 25, false, Main), Selection.FromDrag(20, 25, 10, 20, false, Main));
    }

    [Fact]
    public void Drag_StopsAtTheEdgesOfTheScreen()
    {
        Assert.Equal(new PixelRect(1900, 1000, 20, 80), Selection.FromDrag(1900, 1000, 2500, 1300, false, Main));
        Assert.Equal(new PixelRect(-1280, 0, 81, 51), Selection.FromDrag(-1200, 50, -2000, -40, false, Left));
    }

    [Fact]
    public void Square_TakesTheLongerSide_OnTheSideOfTheDrag()
    {
        Assert.Equal(new PixelRect(100, 100, 51, 51), Selection.FromDrag(100, 100, 150, 120, true, Main));
        Assert.Equal(new PixelRect(50, 50, 51, 51), Selection.FromDrag(100, 100, 50, 80, true, Main));
    }

    [Fact]
    public void Square_StaysSquare_AtTheEdge()
    {
        var square = Selection.FromDrag(1900, 500, 2400, 900, true, Main);
        Assert.Equal(square.Width, square.Height);
        Assert.Equal(new PixelRect(1900, 500, 20, 20), square);
    }

    [Fact]
    public void Move_KeepsTheSize_AndStaysOnScreen()
    {
        var area = new PixelRect(100, 100, 200, 100);
        Assert.Equal(new PixelRect(150, 90, 200, 100), Selection.Move(area, 50, -10, Main));
        Assert.Equal(new PixelRect(1720, 0, 200, 100), Selection.Move(area, 5000, -500, Main));
    }

    [Theory]
    [InlineData(103, 100, false)]
    [InlineData(104, 100, true)]
    [InlineData(100, 96, true)]
    public void SmallMoves_AreClicks(int x, int y, bool drag)
    {
        Assert.Equal(drag, Selection.IsDrag(100, 100, x, y));
    }

    [Fact]
    public void WindowAt_TakesTheFrontmostWindow_ClippedToTheScreen()
    {
        PixelRect[] windows = [new(100, 100, 300, 200), new(0, 0, 1920, 1040), new(1800, 900, 400, 400)];
        Assert.Equal(windows[0], Selection.WindowAt(windows, 150, 150, Main));
        Assert.Equal(windows[1], Selection.WindowAt(windows, 1000, 500, Main));
        Assert.Equal(new PixelRect(1800, 900, 120, 180), Selection.WindowAt(windows, 1850, 1050, Main));
        Assert.Null(Selection.WindowAt([], 10, 10, Main));
    }
}

public class AllScreensTests
{
    [Fact]
    public void WhatNoScreenShows_IsTransparent_TheRestOpaque()
    {
        // A wide screen, and a smaller one on its right, its top aligned: the corner under the small one is empty.
        var area = new PixelRect(-4, 0, 6, 4);
        PixelRect[] screens = [new(-4, 0, 4, 4), new(0, 0, 2, 2)];
        var bgra = Enumerable.Repeat((byte)0x80, 6 * 4 * 4).ToArray(); // grey, alpha undefined (as Bgr32 leaves it)
        Selection.ClearBetweenScreens(bgra, area, screens);
        byte Alpha(int x, int y) => bgra[(y * 6 + x) * 4 + 3];
        Assert.Equal(255, Alpha(1, 1));                   // on the wide screen
        Assert.Equal(255, Alpha(5, 1));                   // on the small one
        Assert.Equal(0, Alpha(5, 3));                     // under the small one: no screen
        Assert.Equal(0, bgra[(3 * 6 + 5) * 4]);           // and no color left there
        Assert.Equal(0x80, bgra[(1 * 6 + 1) * 4]);        // colors kept elsewhere
    }
}

public class PixelRectTests
{
    [Fact]
    public void Union_HoldsAllTheScreens()
    {
        // The author's screens: one in the middle, one standing on its right, one on its left.
        PixelRect[] screens = [new(0, 0, 1920, 1080), new(1920, -557, 1080, 1920), new(-1920, 0, 1920, 1080)];
        Assert.Equal(new PixelRect(-1920, -557, 4920, 1920), PixelRect.Union(screens));
        Assert.True(PixelRect.Union([]).IsEmpty);
    }

    [Fact]
    public void Intersect_GivesTheCommonPart()
    {
        Assert.Equal(new PixelRect(50, 50, 50, 50), new PixelRect(0, 0, 100, 100).Intersect(new PixelRect(50, 50, 100, 100)));
        Assert.True(new PixelRect(0, 0, 10, 10).Intersect(new PixelRect(10, 0, 10, 10)).IsEmpty);
    }

    [Fact]
    public void Text_GoesBothWays()
    {
        var rect = new PixelRect(-1280, 12, 640, 480);
        Assert.True(PixelRect.TryParse(rect.ToString(), out var read));
        Assert.Equal(rect, read);
        Assert.False(PixelRect.TryParse("1,2,0,4", out _));
        Assert.False(PixelRect.TryParse("1,2,3", out _));
        Assert.False(PixelRect.TryParse(null, out _));
    }
}

public class PrintScreenKeyTests
{
    [Theory]
    [InlineData(1, 22621, true)]
    [InlineData(0, 26200, false)]
    [InlineData(null, 26200, true)]   // Windows 11: on unless turned off
    [InlineData(null, 19045, false)]  // Windows 10: off unless turned on
    public void WindowsKeepsTheKey_AsTheSettingSays(int? value, int build, bool used)
    {
        Assert.Equal(used, PrintScreenKey.IsUsedByWindows(value, build));
    }

    [Fact]
    public void IsNeeded_WhenAShortcutUsesPrintScreen()
    {
        Assert.True(PrintScreenKey.IsNeededBy([Hotkey.ParseOr("Ctrl+A", default), Hotkey.ParseOr("Alt+PrintScreen", default)]));
        Assert.False(PrintScreenKey.IsNeededBy([Hotkey.ParseOr("Ctrl+A", default), Hotkey.None]));
    }
}
