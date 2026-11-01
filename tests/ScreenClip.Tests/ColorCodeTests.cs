using ScreenClip.Core;

namespace ScreenClip.Tests;

public class ColorCodeTests
{
    [Theory]
    [InlineData(ColorFormat.Hex, "#3A7BD5")]
    [InlineData(ColorFormat.HexPlain, "3A7BD5")]
    [InlineData(ColorFormat.Rgb, "rgb(58, 123, 213)")]
    [InlineData(ColorFormat.Hsl, "hsl(215, 65%, 53%)")]
    public void AColor_IsWrittenInEachFormat(ColorFormat format, string expected) =>
        Assert.Equal(expected, ColorCode.Format(58, 123, 213, format));

    [Theory]
    [InlineData(255, 255, 255, "hsl(0, 0%, 100%)")]
    [InlineData(0, 0, 0, "hsl(0, 0%, 0%)")]
    [InlineData(255, 0, 0, "hsl(0, 100%, 50%)")]
    [InlineData(0, 255, 0, "hsl(120, 100%, 50%)")]
    [InlineData(0, 0, 255, "hsl(240, 100%, 50%)")]
    [InlineData(255, 0, 128, "hsl(330, 100%, 50%)")]
    public void Hsl_IsAsCssWritesIt(byte r, byte g, byte b, string expected) =>
        Assert.Equal(expected, ColorCode.Format(r, g, b, ColorFormat.Hsl));

    [Fact]
    public void Hex_HasTwoDigitsAChannel() =>
        Assert.Equal("#0A0B0C", ColorCode.Format(10, 11, 12, ColorFormat.Hex));
}
