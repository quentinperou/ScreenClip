using System.Globalization;

namespace ScreenClip.Core;

/// <summary>How the color picker writes the color it copies (Settings › Capture).</summary>
public enum ColorFormat
{
    /// <summary>#3A7BD5</summary>
    Hex,
    /// <summary>3A7BD5</summary>
    HexPlain,
    /// <summary>rgb(58, 123, 213)</summary>
    Rgb,
    /// <summary>hsl(215, 65%, 53%)</summary>
    Hsl,
}

/// <summary>A color written as the color picker copies it (asked by the author: the hex code, or another form).</summary>
public static class ColorCode
{
    public static string Format(byte r, byte g, byte b, ColorFormat format) => format switch
    {
        ColorFormat.HexPlain => $"{r:X2}{g:X2}{b:X2}",
        ColorFormat.Rgb => string.Create(CultureInfo.InvariantCulture, $"rgb({r}, {g}, {b})"),
        ColorFormat.Hsl => Hsl(r, g, b),
        _ => $"#{r:X2}{g:X2}{b:X2}",
    };

    // Hue in degrees, saturation and lightness in percent, rounded as CSS writes them.
    private static string Hsl(byte r, byte g, byte b)
    {
        double red = r / 255.0, green = g / 255.0, blue = b / 255.0;
        double max = Math.Max(red, Math.Max(green, blue)), min = Math.Min(red, Math.Min(green, blue));
        double lightness = (max + min) / 2, range = max - min, hue = 0, saturation = 0;
        if (range > 0)
        {
            saturation = range / (1 - Math.Abs(2 * lightness - 1));
            hue = max == red ? 60 * ((green - blue) / range % 6)
                : max == green ? 60 * ((blue - red) / range + 2)
                : 60 * ((red - green) / range + 4);
            if (hue < 0)
                hue += 360;
        }
        int h = (int)Math.Round(hue) % 360, s = (int)Math.Round(saturation * 100), l = (int)Math.Round(lightness * 100);
        return string.Create(CultureInfo.InvariantCulture, $"hsl({h}, {s}%, {l}%)");
    }
}
