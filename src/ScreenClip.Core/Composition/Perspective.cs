using SkiaSharp;

namespace ScreenClip.Core.Composition;

/// <summary>The 3D turn of the window: a rotation in space, seen in perspective, kept inside its place.</summary>
public static class Perspective
{
    /// <summary>
    /// The four corners of <paramref name="rect"/> (top left, top right, bottom right, bottom left) turned by
    /// <paramref name="turnDegrees"/> around the vertical axis (to the left below 0) and <paramref name="tiltDegrees"/>
    /// around the horizontal one (seen from above over 0), then scaled down to fit in <paramref name="rect"/> again.
    /// </summary>
    public static SKPoint[] TiltedCorners(SKRect rect, double turnDegrees, double tiltDegrees)
    {
        var corners = new[] { new SKPoint(rect.Left, rect.Top), new SKPoint(rect.Right, rect.Top), new SKPoint(rect.Right, rect.Bottom), new SKPoint(rect.Left, rect.Bottom) };
        if (turnDegrees == 0 && tiltDegrees == 0)
            return corners;

        // Turned to the left, the right edge goes away from the eye; seen from above, the bottom edge comes closer.
        double turn = -turnDegrees * Math.PI / 180;
        double tiltX = -tiltDegrees * Math.PI / 180;
        double distance = 2.2 * Math.Max(rect.Width, rect.Height); // from the eye to the window
        var projected = corners.Select(c =>
        {
            double x = c.X - rect.MidX, y = c.Y - rect.MidY;
            double x1 = x * Math.Cos(turn), z1 = x * Math.Sin(turn);
            double y2 = y * Math.Cos(tiltX) - z1 * Math.Sin(tiltX), z2 = y * Math.Sin(tiltX) + z1 * Math.Cos(tiltX);
            double k = distance / (distance + z2);
            return (X: x1 * k, Y: y2 * k);
        }).ToArray();

        double minX = projected.Min(p => p.X), maxX = projected.Max(p => p.X);
        double minY = projected.Min(p => p.Y), maxY = projected.Max(p => p.Y);
        double fit = Math.Min(1, Math.Min(rect.Width / (maxX - minX), rect.Height / (maxY - minY)));
        double cx = (minX + maxX) / 2, cy = (minY + maxY) / 2;
        return projected.Select(p => new SKPoint((float)(rect.MidX + (p.X - cx) * fit), (float)(rect.MidY + (p.Y - cy) * fit))).ToArray();
    }

    /// <summary>The matrix that draws <paramref name="rect"/> onto the quadrilateral <paramref name="quad"/> (4 corners, clockwise from top left).</summary>
    public static SKMatrix RectToQuad(SKRect rect, SKPoint[] quad)
    {
        // Unit square to quadrilateral (Heckbert), then the rectangle to the unit square.
        double x0 = quad[0].X, y0 = quad[0].Y, x1 = quad[1].X, y1 = quad[1].Y, x2 = quad[2].X, y2 = quad[2].Y, x3 = quad[3].X, y3 = quad[3].Y;
        double dx1 = x1 - x2, dx2 = x3 - x2, dx3 = x0 - x1 + x2 - x3;
        double dy1 = y1 - y2, dy2 = y3 - y2, dy3 = y0 - y1 + y2 - y3;
        double g = 0, h = 0;
        if (Math.Abs(dx3) > 1e-9 || Math.Abs(dy3) > 1e-9)
        {
            double det = dx1 * dy2 - dx2 * dy1;
            g = (dx3 * dy2 - dx2 * dy3) / det;
            h = (dx1 * dy3 - dx3 * dy1) / det;
        }
        double a = x1 - x0 + g * x1, b = x3 - x0 + h * x3, c = x0;
        double d = y1 - y0 + g * y1, e = y3 - y0 + h * y3, f = y0;

        double w = rect.Width, hh = rect.Height, rx = rect.Left, ry = rect.Top;
        return new SKMatrix
        {
            ScaleX = (float)(a / w),
            SkewX = (float)(b / hh),
            TransX = (float)(c - a * rx / w - b * ry / hh),
            SkewY = (float)(d / w),
            ScaleY = (float)(e / hh),
            TransY = (float)(f - d * rx / w - e * ry / hh),
            Persp0 = (float)(g / w),
            Persp1 = (float)(h / hh),
            Persp2 = (float)(1 - g * rx / w - h * ry / hh),
        };
    }
}
