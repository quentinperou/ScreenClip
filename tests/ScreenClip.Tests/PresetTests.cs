using ScreenClip.Core.Composition;

namespace ScreenClip.Tests;

public class PresetTests
{
    private static readonly ClipStyle Mine = new()
    {
        Background = "sunset", Padding = 14, Radius = 30, Shadow = 20, Frame = FrameKind.Windows, DarkFrame = true,
        TurnDegrees = 8, Ratio = AspectRatio.Square, Format = ExportFormat.Webp,
    };

    [Fact]
    public void ApplyingAPreset_KeepsTheFrameTextAndThePicture()
    {
        var current = new ClipStyle { FrameText = "example.com/app", BackgroundImage = @"C:\pictures\wall.jpg" };
        var applied = StylePresets.ApplyTo(Mine, current);
        Assert.Equal(Mine with { FrameText = "example.com/app", BackgroundImage = @"C:\pictures\wall.jpg" }, applied);
    }

    [Fact]
    public void APresetWithItsOwnPicture_BringsIt()
    {
        var preset = Mine with { Background = Backgrounds.ImageId, BackgroundImage = @"C:\pictures\brand.png" };
        var applied = StylePresets.ApplyTo(preset, new ClipStyle { BackgroundImage = @"C:\pictures\wall.jpg" });
        Assert.Equal(@"C:\pictures\brand.png", applied.BackgroundImage);
    }

    [Fact]
    public void APreset_MatchesTheStyleItGave_AndNoOther()
    {
        var current = new ClipStyle { FrameText = "my page" };
        var applied = StylePresets.ApplyTo(Mine, current);
        Assert.True(StylePresets.Matches(Mine, applied));
        Assert.False(StylePresets.Matches(Mine, applied with { Shadow = 21 }));
        Assert.Equal("slide", StylePresets.IdOf(StylePresets.ApplyTo(StylePresets.All[3].Style, current)));
        Assert.Null(StylePresets.IdOf(applied));
    }

    [Fact]
    public void ASavedPreset_LeavesOutTheFrameText_AndAPictureNotInUse()
    {
        var saved = StylePresets.ToSave(Mine with { FrameText = "my page", BackgroundImage = @"C:\pictures\wall.jpg" });
        Assert.Equal("", saved.FrameText);
        Assert.Null(saved.BackgroundImage);

        var withPicture = StylePresets.ToSave(Mine with { Background = Backgrounds.ImageId, BackgroundImage = @"C:\pictures\wall.jpg" });
        Assert.Equal(@"C:\pictures\wall.jpg", withPicture.BackgroundImage);
    }

    [Theory]
    [InlineData(FrameKind.Windows, 8)]
    [InlineData(FrameKind.MacWindow, 10)]
    [InlineData(FrameKind.Browser, 10)]
    [InlineData(FrameKind.None, 33)]
    [InlineData(FrameKind.Laptop, 33)]
    public void AWindowFrame_TakesTheCornersOfTheRealWindow(FrameKind frame, int radius)
    {
        var style = new ClipStyle { Frame = FrameKind.None, Radius = 33 }.WithFrame(frame);
        Assert.Equal(frame, style.Frame);
        Assert.Equal(radius, style.Radius);
    }

    [Theory]
    [InlineData("  My   README  style ", "My README style")]
    [InlineData("   ", null)]
    [InlineData(null, null)]
    [InlineData("A name far too long for a tile of the editor, really", "A name far too long for a tile of the ed")]
    public void Names_AreTidied(string? typed, string? expected)
    {
        Assert.Equal(expected, StylePresets.CleanName(typed));
    }

    [Fact]
    public void ANewName_IsTheFirstFreeOne()
    {
        Assert.Equal("My style 1", StylePresets.NewName("My style {0}", []));
        Assert.Equal("My style 3", StylePresets.NewName("My style {0}", ["my style 1", "My style 2", "Other"]));
        Assert.True(StylePresets.IsTaken("POST", ["post"]));
    }
}
