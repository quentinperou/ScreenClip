namespace ScreenClip.Core;

/// <summary>
/// A rectangle of physical screen pixels, in the coordinates of the virtual screen (the primary monitor starts at
/// 0,0; a monitor on its left has negative X). <see cref="Right"/> and <see cref="Bottom"/> are exclusive.
/// </summary>
public readonly record struct PixelRect(int X, int Y, int Width, int Height)
{
    public int Right => X + Width;
    public int Bottom => Y + Height;
    public bool IsEmpty => Width <= 0 || Height <= 0;

    public bool Contains(int x, int y) => x >= X && x < Right && y >= Y && y < Bottom;

    public bool Contains(PixelRect other) =>
        other.X >= X && other.Y >= Y && other.Right <= Right && other.Bottom <= Bottom;

    /// <summary>The common part of both rectangles; empty when they do not overlap.</summary>
    public PixelRect Intersect(PixelRect other)
    {
        int x = Math.Max(X, other.X);
        int y = Math.Max(Y, other.Y);
        int right = Math.Min(Right, other.Right);
        int bottom = Math.Min(Bottom, other.Bottom);
        return right > x && bottom > y ? new PixelRect(x, y, right - x, bottom - y) : default;
    }

    /// <summary>The smallest rectangle that holds all of <paramref name="rects"/>; empty for none.</summary>
    public static PixelRect Union(IEnumerable<PixelRect> rects)
    {
        int left = int.MaxValue, top = int.MaxValue, right = int.MinValue, bottom = int.MinValue;
        foreach (var r in rects)
        {
            if (r.IsEmpty)
                continue;
            left = Math.Min(left, r.X);
            top = Math.Min(top, r.Y);
            right = Math.Max(right, r.Right);
            bottom = Math.Max(bottom, r.Bottom);
        }
        return right > left && bottom > top ? new PixelRect(left, top, right - left, bottom - top) : default;
    }

    /// <summary>"x,y,width,height", as kept in the settings (last area).</summary>
    public override string ToString() => $"{X},{Y},{Width},{Height}";

    public static bool TryParse(string? text, out PixelRect rect)
    {
        rect = default;
        var parts = text?.Split(',');
        if (parts is not { Length: 4 }
            || !int.TryParse(parts[0], out int x) || !int.TryParse(parts[1], out int y)
            || !int.TryParse(parts[2], out int width) || !int.TryParse(parts[3], out int height)
            || width <= 0 || height <= 0)
            return false;
        rect = new PixelRect(x, y, width, height);
        return true;
    }
}
