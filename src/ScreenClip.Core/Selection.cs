namespace ScreenClip.Core;

/// <summary>What a capture shortcut or a button of the capture bar asks for.</summary>
public enum CaptureMode
{
    /// <summary>Drag a rectangle; a click without dragging takes the window under the pointer.</summary>
    Area,

    /// <summary>Click a window.</summary>
    Window,

    /// <summary>Click a screen (the one under the pointer).</summary>
    Screen,
}

/// <summary>The geometry of the area being selected on screen, in physical pixels.</summary>
public static class Selection
{
    /// <summary>
    /// For an “All screens” capture of <paramref name="area"/> (the rectangle around the screens): the pixels no screen
    /// shows (screens of different sizes, or placed apart) made transparent, the others opaque. 32-bit pixels, blue,
    /// green, red, alpha, a row after the other.
    /// </summary>
    public static void ClearBetweenScreens(Span<byte> bgra, PixelRect area, IReadOnlyList<PixelRect> screens)
    {
        int width = area.Width, stride = width * 4;
        var shown = new bool[width];
        for (int y = 0; y < area.Height; y++)
        {
            Array.Clear(shown);
            int screenY = area.Y + y;
            foreach (var screen in screens)
            {
                if (screenY < screen.Y || screenY >= screen.Bottom)
                    continue;
                for (int x = Math.Max(screen.X, area.X); x < Math.Min(screen.Right, area.Right); x++)
                    shown[x - area.X] = true;
            }
            var row = bgra.Slice(y * stride, stride);
            for (int x = 0; x < width; x++)
            {
                if (shown[x])
                    row[x * 4 + 3] = 255;
                else
                    row.Slice(x * 4, 4).Clear();
            }
        }
    }

    /// <summary>A drag shorter than this (in pixels, both ways) is a click.</summary>
    public const int ClickTolerance = 4;

    /// <summary>
    /// The rectangle dragged from the pixel <paramref name="anchorX"/>,<paramref name="anchorY"/> to the pixel under the
    /// pointer, both included, kept inside <paramref name="bounds"/> (the screen it started on). With
    /// <paramref name="square"/> (Shift held), a square on the side of the drag, as large as the longer side allows.
    /// </summary>
    public static PixelRect FromDrag(int anchorX, int anchorY, int pointerX, int pointerY, bool square, PixelRect bounds)
    {
        anchorX = Math.Clamp(anchorX, bounds.X, bounds.Right - 1);
        anchorY = Math.Clamp(anchorY, bounds.Y, bounds.Bottom - 1);
        pointerX = Math.Clamp(pointerX, bounds.X, bounds.Right - 1);
        pointerY = Math.Clamp(pointerY, bounds.Y, bounds.Bottom - 1);

        bool right = pointerX >= anchorX;
        bool down = pointerY >= anchorY;
        int width = Math.Abs(pointerX - anchorX) + 1;
        int height = Math.Abs(pointerY - anchorY) + 1;

        if (square)
        {
            // Room left from the anchor towards the drag, on each axis.
            int roomX = right ? bounds.Right - anchorX : anchorX - bounds.X + 1;
            int roomY = down ? bounds.Bottom - anchorY : anchorY - bounds.Y + 1;
            width = height = Math.Min(Math.Max(width, height), Math.Min(roomX, roomY));
        }

        return new PixelRect(right ? anchorX : anchorX - width + 1, down ? anchorY : anchorY - height + 1, width, height);
    }

    /// <summary><paramref name="rect"/> moved by <paramref name="dx"/>, <paramref name="dy"/>, stopping at the edges of <paramref name="bounds"/>.</summary>
    public static PixelRect Move(PixelRect rect, int dx, int dy, PixelRect bounds)
    {
        int x = Math.Clamp(rect.X + dx, bounds.X, Math.Max(bounds.X, bounds.Right - rect.Width));
        int y = Math.Clamp(rect.Y + dy, bounds.Y, Math.Max(bounds.Y, bounds.Bottom - rect.Height));
        return rect with { X = x, Y = y };
    }

    /// <summary>True when the pointer moved far enough from where the button was pressed to start a selection.</summary>
    public static bool IsDrag(int anchorX, int anchorY, int pointerX, int pointerY) =>
        Math.Abs(pointerX - anchorX) >= ClickTolerance || Math.Abs(pointerY - anchorY) >= ClickTolerance;

    /// <summary>
    /// The first of <paramref name="windows"/> (top-level windows from front to back, as Windows lists them) under the
    /// point, clipped to <paramref name="screen"/>; null when the point is on the desktop.
    /// </summary>
    public static PixelRect? WindowAt(IEnumerable<PixelRect> windows, int x, int y, PixelRect screen)
    {
        foreach (var window in windows)
        {
            if (window.Contains(x, y))
            {
                var visible = window.Intersect(screen);
                return visible.IsEmpty ? null : visible;
            }
        }
        return null;
    }
}
