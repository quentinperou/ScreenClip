using ScreenClip.Core;

namespace ScreenClip.Tests;

public class FileNamingTests
{
    private static readonly DateTime When = new(2026, 10, 19, 18, 12, 5);

    [Theory]
    [InlineData("Capture {date} à {time}", "Capture 2026-10-19 à 18.12.05")]
    [InlineData("Screenshot {date} at {time}", "Screenshot 2026-10-19 at 18.12.05")]
    [InlineData("{DATE}_{Time}", "2026-10-19_18.12.05")]
    [InlineData("shop", "shop")]
    public void Expand_ReplacesDateAndTime(string template, string expected)
    {
        Assert.Equal(expected, FileNaming.Expand(template, When));
    }

    [Theory]
    [InlineData("a/b:c*d?", "a b c d")]
    [InlineData("name.  ", "name")]
    [InlineData("  ", "2026-10-19 18.12.05")]
    [InlineData("???", "2026-10-19 18.12.05")]
    public void Expand_GivesAValidFileName(string template, string expected)
    {
        Assert.Equal(expected, FileNaming.Expand(template, When));
    }

    [Fact]
    public void UniquePath_NeverTakesAnExistingName()
    {
        var taken = new HashSet<string> { Path.Combine("C:\\Captures", "shot.png"), Path.Combine("C:\\Captures", "shot (2).png") };
        Assert.Equal(Path.Combine("C:\\Captures", "shot (3).png"), FileNaming.UniquePath("C:\\Captures", "shot", ".png", taken.Contains));
        Assert.Equal(Path.Combine("C:\\Captures", "other.png"), FileNaming.UniquePath("C:\\Captures", "other", ".png", taken.Contains));
    }
}
