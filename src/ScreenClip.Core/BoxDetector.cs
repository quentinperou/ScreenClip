namespace ScreenClip.Core;

/// <summary>
/// The rectangular blocks drawn on a picture of the screen (a video player, a panel, a card, a picture, a field): four
/// long straight lines where the color changes, meeting at corners, rounded ones too. It sees what UI Automation
/// cannot: Chrome and Edge show the elements of their pages only to screen readers (asked by the author: “find the
/// blocks of the page, even by looking at the picture”), and pictures, games or remote desktops expose none. Built
/// once per window during a capture, then asked for the blocks under the pointer.
/// </summary>
public sealed class BoxDetector
{
    /// <summary>Two neighboring pixels this different (on one channel at least) are on both sides of a line.</summary>
    public const int Contrast = 10;

    /// <summary>Lines shorter than this are not the side of a block (letters, icons).</summary>
    public const int MinimumSide = 20;

    // Holes allowed in a line: a pixel of the same color on both sides (smoothing, content touching the edge).
    private const int Gap = 3;

    // A rounded corner: the straight part of a side stops this short of the corner, at most.
    private const int Corner = 24;

    // A side may start or end this much past the other ones (a frame line drawn one pixel out).
    private const int Slack = 2;

    private readonly Run[]?[] _rows;    // _rows[y]: the lines between the pixel rows y - 1 and y
    private readonly Run[]?[] _columns; // _columns[x]: the lines between the pixel columns x - 1 and x

    private readonly record struct Run(int Start, int End);

    /// <param name="pixels">32-bit pixels, blue, green, red, then a byte that is ignored; <paramref name="stride"/> bytes a row.</param>
    public BoxDetector(ReadOnlySpan<byte> pixels, int width, int height, int stride)
    {
        Width = width;
        Height = height;
        _rows = new Run[]?[height];
        _columns = new Run[]?[width];

        var rowRuns = new List<Run>();
        var columnRuns = new List<Run>?[width];
        var columnStart = new int[width];
        var columnLast = new int[width];
        Array.Fill(columnStart, -1);
        Array.Fill(columnLast, int.MinValue / 2);

        for (int y = 0; y < height; y++)
        {
            var row = pixels.Slice(y * stride, width * 4);

            // Horizontal lines: between this row and the one above, along the row.
            if (y > 0)
            {
                var above = pixels.Slice((y - 1) * stride, width * 4);
                rowRuns.Clear();
                int start = -1, last = int.MinValue / 2;
                for (int x = 0; x < width; x++)
                {
                    if (!Differs(row, x * 4, above, x * 4))
                        continue;
                    if (x - last > Gap + 1)
                    {
                        Keep(rowRuns, start, last);
                        start = x;
                    }
                    last = x;
                }
                Keep(rowRuns, start, last);
                if (rowRuns.Count > 0)
                    _rows[y] = [.. rowRuns];
            }

            // Vertical lines: between each pixel and the one on its left, followed down every column at once (the
            // picture is read row by row, the way it lies in memory).
            for (int x = 1; x < width; x++)
            {
                if (!Differs(row, x * 4, row, (x - 1) * 4))
                    continue;
                if (y - columnLast[x] > Gap + 1)
                {
                    Keep(columnRuns[x] ??= [], columnStart[x], columnLast[x]);
                    columnStart[x] = y;
                }
                columnLast[x] = y;
            }
        }
        for (int x = 1; x < width; x++)
        {
            if (columnStart[x] >= 0)
                Keep(columnRuns[x] ??= [], columnStart[x], columnLast[x]);
            if (columnRuns[x] is { Count: > 0 } runs)
                _columns[x] = [.. runs];
        }
    }

    public int Width { get; }

    public int Height { get; }

    /// <summary>The blocks that hold the point, in the picture's pixels, the largest first.</summary>
    public IReadOnlyList<PixelRect> BoxesAt(int x, int y)
    {
        if (x < 0 || y < 0 || x >= Width || y >= Height)
            return [];
        var lefts = SidesAcross(1, x + 1, y);
        var rights = SidesAcross(x + 1, Width, y);
        var boxes = new HashSet<PixelRect>();
        foreach (var (left, l) in lefts)
        {
            foreach (var (right, r) in rights)
            {
                int width = right - left;
                // Both sides start and end together, give or take a rounded corner: one block, not two lines that
                // happen to pass the same row.
                if (width < MinimumSide || Math.Abs(l.Start - r.Start) > Corner || Math.Abs(l.End - r.End) > Corner)
                    continue;
                int inset = Math.Min(Corner, width / 4);

                // The top: the line across nearest to where the sides begin, going up (a rounded corner puts it a
                // little above them), then the rows of a frame line right above it, so that the frame is in.
                int highest = Math.Max(1, Math.Max(l.Start, r.Start) - Corner);
                int top = -1;
                for (int row = Math.Min(y, Math.Min(l.Start, r.Start) + Slack); row >= highest; row--)
                {
                    if (Crosses(_rows[row], left + inset, right - inset))
                    {
                        top = row;
                        break;
                    }
                }
                if (top < 0)
                    continue;
                while (top - 1 >= highest && Crosses(_rows[top - 1], left + inset, right - inset))
                    top--;

                // The bottom, the same way down (the line below the last row of the block).
                int lowest = Math.Min(Height - 1, Math.Min(l.End, r.End) + Corner);
                int bottom = -1;
                for (int row = Math.Max(y + 1, Math.Max(l.End, r.End) - Slack); row <= lowest; row++)
                {
                    if (Crosses(_rows[row], left + inset, right - inset))
                    {
                        bottom = row;
                        break;
                    }
                }
                if (bottom < 0)
                    continue;
                while (bottom + 1 <= lowest && Crosses(_rows[bottom + 1], left + inset, right - inset))
                    bottom++;

                int height = bottom - top;
                int round = Math.Min(Corner, height / 4);
                if (height < MinimumSide
                    || l.Start > top + round || r.Start > top + round || l.End < bottom - round || r.End < bottom - round)
                    continue;
                // The lines across reach the sides, but for the corners: as round as the sides say (where they start
                // and end). Square corners leave no room: the top of a card does not make a block with the next card.
                int fit = Math.Max(Math.Max(0, Math.Max(l.Start, r.Start) - top), Math.Max(0, bottom - Math.Min(l.End, r.End))) + Slack + 1;
                if (!Crosses(_rows[top], left + fit, right - fit) || !Crosses(_rows[bottom], left + fit, right - fit))
                    continue;
                boxes.Add(new PixelRect(left, top, width, height));
            }
        }
        return [.. boxes.OrderByDescending(b => (long)b.Width * b.Height)];
    }

    // The vertical lines of the columns [from, to) that pass row y.
    private List<(int X, Run Run)> SidesAcross(int from, int to, int y)
    {
        var sides = new List<(int, Run)>();
        for (int x = from; x < to; x++)
        {
            if (_columns[x] is not { } runs)
                continue;
            foreach (var run in runs)
            {
                if (run.Start <= y && y < run.End)
                    sides.Add((x, run));
            }
        }
        return sides;
    }

    // Whether one of the lines covers [from, to).
    private static bool Crosses(Run[]? runs, int from, int to)
    {
        if (runs is null)
            return false;
        foreach (var run in runs)
        {
            if (run.Start <= from && run.End >= to)
                return true;
        }
        return false;
    }

    private static void Keep(List<Run> runs, int start, int last)
    {
        if (start >= 0 && last + 1 - start >= MinimumSide)
            runs.Add(new Run(start, last + 1));
    }

    private static bool Differs(ReadOnlySpan<byte> a, int i, ReadOnlySpan<byte> b, int j) =>
        Math.Abs(a[i] - b[j]) >= Contrast || Math.Abs(a[i + 1] - b[j + 1]) >= Contrast || Math.Abs(a[i + 2] - b[j + 2]) >= Contrast;
}
