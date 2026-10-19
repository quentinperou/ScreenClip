using System.Text.Json.Serialization;

namespace ScreenClip.Core.Composition;

/// <summary>The window drawn around the capture.</summary>
public enum FrameKind
{
    None,

    /// <summary>A browser in the macOS style: traffic lights, back/forward, address bar.</summary>
    Browser,

    /// <summary>A macOS window: traffic lights and a centered title.</summary>
    MacWindow,

    /// <summary>A Windows 11 window: title on the left, minimize / maximize / close on the right.</summary>
    Windows,

    /// <summary>A laptop (no brand): the capture on its screen, its base below. Silver, or graphite when dark.</summary>
    Laptop,

    /// <summary>A phone (no brand): the capture on its screen, turned sideways for a wide capture.</summary>
    Phone,
}

/// <summary>Shape of the exported picture; <see cref="Auto"/> fits the capture and its margin.</summary>
public enum AspectRatio
{
    Auto,
    Wide,       // 16:9
    Standard,   // 4:3
    Square,     // 1:1
    Portrait,   // 4:5
    Story,      // 9:16
}

public enum ExportFormat
{
    Png,
    Jpeg,
    Webp,
}

/// <summary>
/// How a capture is laid out. Sizes follow the capture: the margin is a share of its size, the corner radius and the
/// window bar grow with large (high DPI) captures, so that a style looks the same on any capture.
/// </summary>
public sealed record ClipStyle
{
    /// <summary>Id of one of <see cref="Backgrounds.All"/>.</summary>
    public string Background { get; init; } = "violet";

    /// <summary>File of the picture used when <see cref="Background"/> is <see cref="Backgrounds.ImageId"/>.</summary>
    public string? BackgroundImage { get; init; }

    /// <summary>Margin around the window, in % of the capture's size (0 to 25).</summary>
    public int Padding { get; init; } = 8;

    /// <summary>Corner radius of the window, in pixels at 100 % (0 to 80; never more than half the window).</summary>
    public int Radius { get; init; } = 12;

    /// <summary>Strength of the shadow under the window, 0 to 100.</summary>
    public int Shadow { get; init; } = 60;

    [JsonConverter(typeof(JsonStringEnumConverter<FrameKind>))]
    public FrameKind Frame { get; init; } = FrameKind.Browser;

    public bool DarkFrame { get; init; }

    /// <summary>The address of the browser bar, or the title of the window.</summary>
    public string FrameText { get; init; } = "";

    /// <summary>3D turn of the window around its vertical axis, in degrees: to the left below 0, to the right above.</summary>
    public int TurnDegrees { get; init; }

    /// <summary>3D turn around its horizontal axis, in degrees: seen from above over 0, from below under 0.</summary>
    public int TiltDegrees { get; init; }

    [JsonConverter(typeof(JsonStringEnumConverter<AspectRatio>))]
    public AspectRatio Ratio { get; init; } = AspectRatio.Auto;

    [JsonConverter(typeof(JsonStringEnumConverter<ExportFormat>))]
    public ExportFormat Format { get; init; } = ExportFormat.Png;

    public const int MaxPadding = 25;
    public const int MaxRadius = 80;
    public const int MaxTurn = 30;
    public const int MaxTilt = 20;

    /// <summary>The quick turns of the editor (“Left”, “Right”): a slight turn, seen a little from above.</summary>
    public static readonly (int Turn, int Tilt) TurnLeft = (-16, 4), TurnRight = (16, 4);

    public bool IsTurned => TurnDegrees != 0 || TiltDegrees != 0;

    /// <summary>
    /// The corners of the real window a frame imitates, at 100 %: Windows 11 rounds its windows by 8 px, macOS by 10
    /// (Safari and Chrome on a Mac too). Null for no frame and the devices: their corners stay as set.
    /// </summary>
    public static int? NaturalRadius(FrameKind frame) => frame switch
    {
        FrameKind.Windows => 8,
        FrameKind.MacWindow or FrameKind.Browser => 10,
        _ => null,
    };

    /// <summary>The style with <paramref name="frame"/>, its corners those of the real window (asked by the author).</summary>
    public ClipStyle WithFrame(FrameKind frame) => this with { Frame = frame, Radius = NaturalRadius(frame) ?? Radius };

    /// <summary>Width / height of <paramref name="ratio"/>, or null for <see cref="AspectRatio.Auto"/>.</summary>
    public static double? RatioValue(AspectRatio ratio) => ratio switch
    {
        AspectRatio.Wide => 16.0 / 9,
        AspectRatio.Standard => 4.0 / 3,
        AspectRatio.Square => 1,
        AspectRatio.Portrait => 4.0 / 5,
        AspectRatio.Story => 9.0 / 16,
        _ => null,
    };

    /// <summary>The style with its numbers brought back into their ranges (settings written by hand, older versions).</summary>
    public ClipStyle Normalized() => this with
    {
        Background = Backgrounds.Find(Background) is null ? Backgrounds.All[0].Id : Background,
        Padding = Math.Clamp(Padding, 0, MaxPadding),
        Radius = Math.Clamp(Radius, 0, MaxRadius),
        Shadow = Math.Clamp(Shadow, 0, 100),
        TurnDegrees = Math.Clamp(TurnDegrees, -MaxTurn, MaxTurn),
        TiltDegrees = Math.Clamp(TiltDegrees, -MaxTilt, MaxTilt),
        FrameText = FrameText ?? "",
    };
}

/// <summary>Ready-made styles for the usual uses (validated with the author on the mock-up).</summary>
public static class StylePresets
{
    public static readonly IReadOnlyList<(string Id, ClipStyle Style)> All =
    [
        ("readme", new ClipStyle { Background = "mica", Padding = 7, Radius = 10, Shadow = 40, Frame = FrameKind.None, Ratio = AspectRatio.Auto, Format = ExportFormat.Png }),
        ("post", new ClipStyle { Background = "violet", Padding = 10, Radius = 12, Shadow = 60, Frame = FrameKind.Browser, Ratio = AspectRatio.Square, Format = ExportFormat.Jpeg }),
        ("story", new ClipStyle { Background = "night", Padding = 6, Radius = 14, Shadow = 70, Frame = FrameKind.MacWindow, DarkFrame = true, Ratio = AspectRatio.Story, Format = ExportFormat.Jpeg }),
        ("slide", new ClipStyle { Background = "ocean", Padding = 11, Radius = 12, Shadow = 65, Frame = FrameKind.Browser, TurnDegrees = -16, TiltDegrees = 4, Ratio = AspectRatio.Wide, Format = ExportFormat.Png }),
    ];

    /// <summary>The style of a new user: the README preset, close to what a screenshot of an app needs.</summary>
    public static ClipStyle Default => All[0].Style;

    /// <summary>The longest name of a preset of the user.</summary>
    public const int MaxNameLength = 40;

    /// <summary>The built-in preset <paramref name="style"/> is, or null.</summary>
    public static string? IdOf(ClipStyle style) => All.FirstOrDefault(p => Matches(p.Style, style)).Id;

    /// <summary>
    /// <paramref name="preset"/> applied on top of <paramref name="current"/>: the text of the frame stays (it names the
    /// page captured, not a style), and so does the user's picture, unless the preset has a picture of its own.
    /// </summary>
    public static ClipStyle ApplyTo(ClipStyle preset, ClipStyle current) => (preset with
    {
        FrameText = current.FrameText,
        BackgroundImage = preset.Background == Backgrounds.ImageId ? preset.BackgroundImage : current.BackgroundImage,
    }).Normalized();

    /// <summary>Whether <paramref name="style"/> is what <paramref name="preset"/> gives (its tile shows it chosen).</summary>
    public static bool Matches(ClipStyle preset, ClipStyle style) => ApplyTo(preset, style) == style.Normalized();

    /// <summary>
    /// What a preset of the user keeps of <paramref name="style"/>: everything but the text of the frame, and the picture
    /// only when it is the background.
    /// </summary>
    public static ClipStyle ToSave(ClipStyle style) => (style with
    {
        FrameText = "",
        BackgroundImage = style.Background == Backgrounds.ImageId ? style.BackgroundImage : null,
    }).Normalized();

    /// <summary>A name typed by the user, tidied (spaces, length), or null when there is nothing left.</summary>
    public static string? CleanName(string? text)
    {
        string name = string.Join(' ', (text ?? "").Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        if (name.Length > MaxNameLength)
            name = name[..MaxNameLength].TrimEnd();
        return name.Length == 0 ? null : name;
    }

    /// <summary>Whether <paramref name="name"/> is one of <paramref name="taken"/>, case aside.</summary>
    public static bool IsTaken(string name, IEnumerable<string> taken) =>
        taken.Any(t => string.Equals(t, name, StringComparison.CurrentCultureIgnoreCase));

    /// <summary>The first free name of the form <paramref name="pattern"/> (“My style {0}”), counting from 1.</summary>
    public static string NewName(string pattern, IEnumerable<string> taken)
    {
        var names = taken.ToList();
        for (int n = 1; ; n++)
        {
            string name = string.Format(System.Globalization.CultureInfo.CurrentCulture, pattern, n);
            if (!IsTaken(name, names))
                return name;
        }
    }
}

/// <summary>A style saved by the user under a name, shown after the built-in presets.</summary>
public sealed record UserPreset
{
    public string Name { get; init; } = "";

    public ClipStyle Style { get; init; } = StylePresets.Default;
}
