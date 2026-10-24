using SkiaSharp;

namespace ScreenClip.Core;

/// <summary>The fonts of what ScreenClip draws (window titles, addresses, annotations): Windows 11's own.</summary>
internal static class Typefaces
{
    private static readonly Lazy<SKTypeface> RegularFace = new(() => Load(SKFontStyle.Normal));
    private static readonly Lazy<SKTypeface> SemiBoldFace = new(() => Load(new SKFontStyle(SKFontStyleWeight.SemiBold, SKFontStyleWidth.Normal, SKFontStyleSlant.Upright)));
    private static readonly Lazy<SKTypeface> BoldFace = new(() => Load(SKFontStyle.Bold));

    public static SKTypeface Regular => RegularFace.Value;

    public static SKTypeface SemiBold => SemiBoldFace.Value;

    public static SKTypeface Bold => BoldFace.Value;

    private static SKTypeface Load(SKFontStyle style)
    {
        foreach (string family in new[] { "Segoe UI Variable Text", "Segoe UI" })
        {
            var typeface = SKTypeface.FromFamilyName(family, style);
            if (typeface is not null && typeface.FamilyName.StartsWith("Segoe UI", StringComparison.OrdinalIgnoreCase))
                return typeface;
        }
        return SKTypeface.Default;
    }
}
