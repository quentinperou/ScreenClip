namespace ScreenClip.Core.Composition;

/// <summary>
/// Where everything goes in the exported picture, in its pixels: the picture size, the window (or device) around the
/// capture, the capture itself, the window bar, the corner radius. The capture is never resized: it keeps its own
/// pixels, the picture grows around it.
/// </summary>
/// <param name="CaptureX">Where the shown part of the capture goes (its top left corner).</param>
/// <param name="Scale">How much the window bar and the corners grow with the capture (1 up to a 1600 px capture).</param>
/// <param name="Bezel">Thickness of the black border of a device's screen (0 for a window).</param>
/// <param name="Shown">The part of the capture that is drawn: all of it, except in a device (see <see cref="DeviceShape.Screen"/>).</param>
public readonly record struct ClipLayout(
    int Width, int Height,
    int WindowX, int WindowY, int WindowWidth, int WindowHeight,
    int CaptureX, int CaptureY,
    int BarHeight, double Scale, double Radius, int Bezel, PixelRect Shown)
{
    /// <summary>Height of each window bar at 100 %.</summary>
    public static int BarHeightOf(FrameKind frame) => frame switch
    {
        FrameKind.Browser => 40,
        FrameKind.MacWindow => 32,
        FrameKind.Windows => 36,
        _ => 0,
    };

    public static bool IsDevice(FrameKind frame) => frame is FrameKind.Laptop or FrameKind.Phone;

    /// <summary>The layout of a <paramref name="captureWidth"/> × <paramref name="captureHeight"/> capture in <paramref name="style"/>.</summary>
    public static ClipLayout Measure(int captureWidth, int captureHeight, ClipStyle style)
    {
        var shown = DeviceShape.Screen(style.Frame, captureWidth, captureHeight);
        (captureWidth, captureHeight) = (shown.Width, shown.Height);
        // The bar and the corners follow large (high DPI) captures, so that a 4K capture does not get a thin bar.
        double scale = Math.Clamp(Math.Max(captureWidth, captureHeight) / 1600.0, 1, 3);
        var device = DeviceShape.Of(style.Frame, captureWidth, captureHeight);
        int bar = (int)Math.Round(BarHeightOf(style.Frame) * scale);
        var (left, top, right, bottom) = device?.Insets ?? (0, bar, 0, 0);
        int windowWidth = left + captureWidth + right;
        int windowHeight = top + captureHeight + bottom;

        int padding = (int)Math.Round(Math.Clamp(style.Padding, 0, ClipStyle.MaxPadding) / 100.0 * (captureWidth + captureHeight) / 2);
        int width = windowWidth + 2 * padding;
        int height = windowHeight + 2 * padding;
        if (ClipStyle.RatioValue(style.Ratio) is { } ratio)
        {
            // The margin is the least there is: the picture widens or heightens to the ratio.
            if ((double)width / height < ratio)
                width = (int)Math.Round(height * ratio);
            else
                height = (int)Math.Round(width / ratio);
        }

        int windowX = (width - windowWidth) / 2, windowY = (height - windowHeight) / 2;
        return new ClipLayout(width, height, windowX, windowY, windowWidth, windowHeight, windowX + left, windowY + top,
            device is null ? bar : 0, scale, Math.Clamp(style.Radius, 0, ClipStyle.MaxRadius) * scale, device?.Bezel ?? 0, shown);
    }
}

/// <summary>
/// The proportions of the devices drawn around a capture, all from the capture's size: the screen is the capture
/// (cropped to the proportions of a real screen), the device grows around it.
/// </summary>
public sealed record DeviceShape(FrameKind Kind, int Bezel, (int Left, int Top, int Right, int Bottom) Insets)
{
    /// <summary>Laptop: part of the screen's border above the capture (camera), relative to the bezel.</summary>
    public const double LaptopTopBezel = 1.3;

    /// <summary>Laptop: the base is this much wider than the screen, and this many bezels high.</summary>
    public const double LaptopBaseWidth = 1.14, LaptopBaseHeight = 1.15;

    /// <summary>Phone: the metal edge around the black border, and the buttons sticking out of it, relative to the bezel.</summary>
    public const double PhoneEdge = 0.22, PhoneButtons = 0.28;

    /// <summary>Width / height of a laptop screen: from 3:2 to 16:9.</summary>
    public const double LaptopMinRatio = 3.0 / 2, LaptopMaxRatio = 16.0 / 9;

    /// <summary>Long side / short side of a phone screen: from 16:9 to 20:9.</summary>
    public const double PhoneMinRatio = 16.0 / 9, PhoneMaxRatio = 20.0 / 9;

    /// <summary>
    /// The part of the capture a device shows. Its screen keeps the proportions of a real one: a capture of any shape
    /// gave a device of that shape, a phone as wide as it was high (seen by the author). Too tall, the capture keeps
    /// its top (where a page starts); too wide, its middle. Outside a device, the whole capture.
    /// </summary>
    public static PixelRect Screen(FrameKind frame, int captureWidth, int captureHeight)
    {
        var whole = new PixelRect(0, 0, captureWidth, captureHeight);
        if (captureWidth <= 0 || captureHeight <= 0)
            return whole;
        (double min, double max) = frame switch
        {
            FrameKind.Laptop => (LaptopMinRatio, LaptopMaxRatio),
            // Upright or sideways, as the capture is.
            FrameKind.Phone when captureHeight >= captureWidth => (1 / PhoneMaxRatio, 1 / PhoneMinRatio),
            FrameKind.Phone => (PhoneMinRatio, PhoneMaxRatio),
            _ => (0, double.PositiveInfinity),
        };
        double ratio = (double)captureWidth / captureHeight;
        if (ratio > max)
        {
            int width = Math.Min(captureWidth, (int)Math.Round(captureHeight * max));
            return new PixelRect((captureWidth - width) / 2, 0, width, captureHeight);
        }
        if (ratio < min)
            return new PixelRect(0, 0, captureWidth, Math.Min(captureHeight, (int)Math.Round(captureWidth / min)));
        return whole;
    }

    public static DeviceShape? Of(FrameKind frame, int captureWidth, int captureHeight)
    {
        switch (frame)
        {
            case FrameKind.Laptop:
            {
                int bezel = Math.Max(10, (int)Math.Round(0.028 * Math.Max(captureWidth, captureHeight)));
                int bodyWidth = captureWidth + 2 * bezel;
                int overhang = (int)Math.Round(bodyWidth * (LaptopBaseWidth - 1) / 2);
                int top = (int)Math.Round(bezel * LaptopTopBezel);
                int bottom = bezel + (int)Math.Round(bezel * LaptopBaseHeight);
                return new DeviceShape(frame, bezel, (bezel + overhang, top, bezel + overhang, bottom));
            }
            case FrameKind.Phone:
            {
                int bezel = Math.Max(10, (int)Math.Round(0.045 * Math.Min(captureWidth, captureHeight)));
                int edge = Math.Max(2, (int)Math.Round(bezel * PhoneEdge));
                int buttons = Math.Max(2, (int)Math.Round(bezel * PhoneButtons));
                int side = bezel + edge;
                // Buttons on the long sides: left and right upright, top and bottom sideways.
                return captureHeight >= captureWidth
                    ? new DeviceShape(frame, bezel, (side + buttons, side, side + buttons, side))
                    : new DeviceShape(frame, bezel, (side, side + buttons, side, side + buttons));
            }
            default:
                return null;
        }
    }
}
