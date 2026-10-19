namespace ScreenClip.Core;

/// <summary>
/// The UI elements under the pointer, from the smallest to the window (a video, its player, the column, the page…),
/// as offered to a click in Area mode; the mouse wheel moves along it.
/// </summary>
public static class ElementChain
{
    /// <summary>Elements smaller than this (in pixels, either way) are not offered: too small to be a capture.</summary>
    public const int MinimumSize = 12;

    // Two elements whose edges are this close are the same thing to the eye (a box and the frame around it).
    private const int SameEdges = 4;

    /// <summary>The chain of <paramref name="elements"/> alone (see the other overload).</summary>
    public static IReadOnlyList<PixelRect> Build(IEnumerable<PixelRect> elements, PixelRect window, PixelRect screen) =>
        Build(elements, [], window, screen);

    /// <summary>
    /// The rectangles to offer, from the smallest to the window: the <paramref name="elements"/> the app exposes (UI
    /// Automation) and the <paramref name="boxes"/> seen on the picture (<see cref="BoxDetector"/>), clipped to the
    /// window and to the screen, each one inside the next, without the tiny ones nor those that look like the next one;
    /// the window itself last. A box that crosses an element or another box is a guess that went wrong: left out.
    /// </summary>
    public static IReadOnlyList<PixelRect> Build(IEnumerable<PixelRect> elements, IEnumerable<PixelRect> boxes, PixelRect window, PixelRect screen)
    {
        var bounds = window.Intersect(screen);
        if (bounds.IsEmpty)
            return [];
        var exposed = elements.Select(e => e.Intersect(bounds)).Where(e => !e.IsEmpty).ToList();
        var seen = boxes.Select(b => b.Intersect(bounds)).Where(b => !b.IsEmpty).ToList();
        var trusted = seen.Where(b => !exposed.Any(e => Cross(b, e)) && !seen.Any(o => Cross(b, o))).ToList();

        var chain = new List<PixelRect> { bounds };
        foreach (var rect in exposed.Concat(trusted).OrderByDescending(r => (long)r.Width * r.Height))
        {
            var last = chain[^1];
            if (rect.Width < MinimumSize || rect.Height < MinimumSize || Looks(rect, last) || !Inside(rect, last))
                continue;
            chain.Add(rect.Intersect(last));
        }
        chain.Reverse();
        return chain;
    }

    /// <summary>The element at <paramref name="level"/> (0 = the smallest), kept inside the chain.</summary>
    public static int Clamp(IReadOnlyList<PixelRect> chain, int level) => Math.Clamp(level, 0, Math.Max(0, chain.Count - 1));

    private static bool Looks(PixelRect a, PixelRect b) =>
        Math.Abs(a.X - b.X) <= SameEdges && Math.Abs(a.Y - b.Y) <= SameEdges
        && Math.Abs(a.Right - b.Right) <= SameEdges && Math.Abs(a.Bottom - b.Bottom) <= SameEdges;

    private static bool Inside(PixelRect a, PixelRect b) =>
        a.X >= b.X - SameEdges && a.Y >= b.Y - SameEdges && a.Right <= b.Right + SameEdges && a.Bottom <= b.Bottom + SameEdges;

    // Neither holds the other: they overlap across (both hold the pointer, so they cannot be apart).
    private static bool Cross(PixelRect a, PixelRect b) => !Inside(a, b) && !Inside(b, a);
}
