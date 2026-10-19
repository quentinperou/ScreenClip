namespace ScreenClip.Core.Composition;

public enum BackgroundKind
{
    Gradient,
    Solid,
    Transparent,

    /// <summary>The capture itself, enlarged and blurred.</summary>
    Blur,

    /// <summary>A picture chosen by the user (<see cref="ClipStyle.BackgroundImage"/>).</summary>
    Image,
}

/// <summary>A background of the swatches: colors as 0xRRGGBB, from the top left corner to the bottom right one.</summary>
public sealed record BackgroundDefinition(string Id, BackgroundKind Kind, uint From = 0, uint To = 0);

/// <summary>The backgrounds offered in the editor, in the order of its swatches (same as the mock-up).</summary>
public static class Backgrounds
{
    public const string ImageId = "image";

    public static readonly IReadOnlyList<BackgroundDefinition> All =
    [
        new("violet", BackgroundKind.Gradient, 0x7B61FF, 0xE5489A),
        new("ocean", BackgroundKind.Gradient, 0x2CC5B5, 0x1660C9),
        new("sunset", BackgroundKind.Gradient, 0xFDBA74, 0xF43F5E),
        new("lagoon", BackgroundKind.Gradient, 0xBEF264, 0x0EA5A4),
        new("sky", BackgroundKind.Gradient, 0xBAE6FD, 0xC4B5FD),
        new("peach", BackgroundKind.Gradient, 0xFFE4D6, 0xFBCFE8),
        new("night", BackgroundKind.Gradient, 0x1E293B, 0x4C1D95),
        new("mica", BackgroundKind.Gradient, 0xEEF2F8, 0xD9E0EA),
        new("blur", BackgroundKind.Blur),
        new("white", BackgroundKind.Solid, 0xFFFFFF, 0xFFFFFF),
        new("black", BackgroundKind.Solid, 0x141417, 0x141417),
        new("transparent", BackgroundKind.Transparent),
        new(ImageId, BackgroundKind.Image),
    ];

    public static BackgroundDefinition? Find(string? id) => All.FirstOrDefault(b => b.Id == id);
}
