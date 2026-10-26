using System.Runtime.InteropServices;
using ScreenClip.Core;
using ScreenClip.Core.Video;

namespace ScreenClip.Video;

/// <summary>
/// Records the mouse pointer apart from a video filmed without it: where its tip is (in the video's pixels), its shape
/// (the arrow, the text beam, the hand…: each new one kept as a picture) and its buttons, so that the editor can draw
/// it again, as it was or in another style. A thread looks about a hundred times a second while the video records.
/// </summary>
internal sealed class PointerRecorder
{
    private readonly PixelRect _area;
    private readonly Func<long?> _clock;
    private readonly List<PointerSample> _samples = [];
    private readonly List<PointerShape> _shapes = [];
    private readonly Dictionary<IntPtr, int> _shapeOf = [];
    private Thread? _thread;
    private volatile bool _stopped;

#if DEBUG
    /// <summary>
    /// Debug builds: the pointer's place and button from a script instead of the real mouse (the recording test films a
    /// window of its own, not the screen of the person at the PC); its shape is still the real one.
    /// </summary>
    public static Func<TimeSpan, (float X, float Y, bool Pressed)>? Fake { get; set; }
#endif

    /// <param name="area">The part filmed, in screen pixels: the samples are relative to its top left corner.</param>
    /// <param name="clock">The video's time (100 ns), or null while it is paused.</param>
    public PointerRecorder(PixelRect area, Func<long?> clock)
    {
        _area = area;
        _clock = clock;
    }

    public PointerTrack? Track { get; private set; }

    public void Start()
    {
        _thread = new Thread(Run) { IsBackground = true, Name = "ScreenClip pointer", Priority = ThreadPriority.AboveNormal };
        _thread.Start();
    }

    /// <summary>Stops looking, and makes <see cref="Track"/>.</summary>
    public PointerTrack Stop()
    {
        _stopped = true;
        _thread?.Join(1000);
        _thread = null;
        return Track ??= new PointerTrack(_samples, _shapes);
    }

    private void Run()
    {
        PointerSample? last = null;
        long lastLook = -1;
        while (!_stopped)
        {
            if (_clock() is { } now)
            {
                var sample = Look(now);
                if (last is not { } before || before.X != sample.X || before.Y != sample.Y || before.Shape != sample.Shape || before.Pressed != sample.Pressed)
                {
                    // It stood still until the last look: said there, so that its way is not drawn across that time.
                    if (last is { } still && lastLook > still.Time.Ticks)
                        _samples.Add(still with { Time = TimeSpan.FromTicks(lastLook) });
                    _samples.Add(sample);
                    last = sample;
                }
                lastLook = now;
            }
            Thread.Sleep(5);
        }
    }

    private PointerSample Look(long now)
    {
        var info = new Native.CURSORINFO { cbSize = Marshal.SizeOf<Native.CURSORINFO>() };
        bool known = Native.GetCursorInfo(ref info);
        int shape = known && (info.flags & Native.CURSOR_SHOWING) != 0 ? ShapeOf(info.hCursor) : -1;
        float x = info.ptScreenPos.X - _area.X, y = info.ptScreenPos.Y - _area.Y;
        bool pressed = (Native.GetAsyncKeyState(Native.VK_LBUTTON) & 0x8000) != 0 || (Native.GetAsyncKeyState(Native.VK_RBUTTON) & 0x8000) != 0;
#if DEBUG
        if (Fake is { } fake)
            (x, y, pressed) = fake(TimeSpan.FromTicks(now));
        // The real pointer hidden (hidden while typing): the arrow of Windows.
        if (Fake is not null && shape < 0)
            shape = ShapeOf(Native.LoadCursor(IntPtr.Zero, Native.IDC_ARROW));
#endif
        return new PointerSample(TimeSpan.FromTicks(now), x, y, shape, pressed);
    }

    private int ShapeOf(IntPtr cursor)
    {
        if (cursor == IntPtr.Zero)
            return -1;
        if (_shapeOf.TryGetValue(cursor, out int index))
            return index;
        index = -1;
        if (Picture(cursor) is { } shape)
        {
            index = _shapes.Count;
            _shapes.Add(shape);
        }
        _shapeOf[cursor] = index;
        return index;
    }

    /// <summary>
    /// The picture of a pointer, drawn twice by Windows, on black and on white: where both agree the pixel is opaque,
    /// where they differ by everything it is see-through. Where black turns white and white black, the pointer inverts
    /// the screen (the text beam of old Windows pointers): drawn black, outlined in white, so that it shows on anything.
    /// </summary>
    private static PointerShape? Picture(IntPtr cursor)
    {
        if (!Native.GetIconInfo(cursor, out var icon))
            return null;
        try
        {
            bool monochrome = icon.hbmColor == IntPtr.Zero;
            if (Native.GetObject(monochrome ? icon.hbmMask : icon.hbmColor, Marshal.SizeOf<Native.BITMAP>(), out var bitmap) == 0)
                return null;
            int width = bitmap.bmWidth, height = monochrome ? bitmap.bmHeight / 2 : bitmap.bmHeight;
            if (width <= 0 || height <= 0 || width > 256 || height > 256)
                return null;
            var onBlack = Draw(cursor, width, height, 0x00);
            var onWhite = Draw(cursor, width, height, 0xFF);
            if (onBlack is null || onWhite is null)
                return null;
            var pixels = new byte[width * height * 4];
            var inverted = new bool[width * height];
            for (int i = 0; i < width * height; i++)
            {
                int o = i * 4;
                int b = (onBlack[o] + onBlack[o + 1] + onBlack[o + 2]) / 3, w = (onWhite[o] + onWhite[o + 1] + onWhite[o + 2]) / 3;
                if (w + 64 < b)
                {
                    inverted[i] = true;
                    pixels[o + 3] = 0xFF;   // black
                    continue;
                }
                int alpha = Math.Clamp(255 - (w - b), 0, 255);
                pixels[o] = (byte)Math.Min(onBlack[o], alpha);
                pixels[o + 1] = (byte)Math.Min(onBlack[o + 1], alpha);
                pixels[o + 2] = (byte)Math.Min(onBlack[o + 2], alpha);
                pixels[o + 3] = (byte)alpha;
            }
            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    int i = y * width + x;
                    if (inverted[i] || pixels[i * 4 + 3] != 0)
                        continue;
                    bool next = (x > 0 && inverted[i - 1]) || (x + 1 < width && inverted[i + 1]) || (y > 0 && inverted[i - width]) || (y + 1 < height && inverted[i + width]);
                    if (next)
                        pixels[i * 4] = pixels[i * 4 + 1] = pixels[i * 4 + 2] = pixels[i * 4 + 3] = 0xFF;
                }
            }
            return new PointerShape(width, height, icon.xHotspot, icon.yHotspot, pixels);
        }
        finally
        {
            if (icon.hbmMask != IntPtr.Zero)
                Native.DeleteObject(icon.hbmMask);
            if (icon.hbmColor != IntPtr.Zero)
                Native.DeleteObject(icon.hbmColor);
        }
    }

    // The pointer drawn by Windows on a background of one grey (0: black, 255: white), as BGRA rows from the top.
    private static byte[]? Draw(IntPtr cursor, int width, int height, byte background)
    {
        IntPtr dc = Native.CreateCompatibleDC(IntPtr.Zero);
        if (dc == IntPtr.Zero)
            return null;
        IntPtr dib = IntPtr.Zero, old = IntPtr.Zero;
        try
        {
            var header = new Native.BITMAPINFOHEADER
            {
                biSize = Marshal.SizeOf<Native.BITMAPINFOHEADER>(),
                biWidth = width,
                biHeight = -height, // top-down
                biPlanes = 1,
                biBitCount = 32,
            };
            dib = Native.CreateDIBSection(dc, ref header, 0, out IntPtr bits, IntPtr.Zero, 0);
            if (dib == IntPtr.Zero)
                return null;
            old = Native.SelectObject(dc, dib);
            var pixels = new byte[width * height * 4];
            Array.Fill(pixels, background);
            Marshal.Copy(pixels, 0, bits, pixels.Length);
            if (!Native.DrawIconEx(dc, 0, 0, cursor, width, height, 0, IntPtr.Zero, Native.DI_NORMAL))
                return null;
            Native.GdiFlush();
            Marshal.Copy(bits, pixels, 0, pixels.Length);
            return pixels;
        }
        finally
        {
            if (old != IntPtr.Zero)
                Native.SelectObject(dc, old);
            if (dib != IntPtr.Zero)
                Native.DeleteObject(dib);
            Native.DeleteDC(dc);
        }
    }

    private static class Native
    {
        public const int CURSOR_SHOWING = 1, VK_LBUTTON = 0x01, VK_RBUTTON = 0x02, DI_NORMAL = 3, IDC_ARROW = 32512;

        [StructLayout(LayoutKind.Sequential)]
        public struct POINT
        {
            public int X, Y;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct CURSORINFO
        {
            public int cbSize;
            public int flags;
            public IntPtr hCursor;
            public POINT ptScreenPos;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct ICONINFO
        {
            public bool fIcon;
            public int xHotspot;
            public int yHotspot;
            public IntPtr hbmMask;
            public IntPtr hbmColor;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct BITMAP
        {
            public int bmType, bmWidth, bmHeight, bmWidthBytes;
            public short bmPlanes, bmBitsPixel;
            public IntPtr bmBits;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct BITMAPINFOHEADER
        {
            public int biSize, biWidth, biHeight;
            public short biPlanes, biBitCount;
            public int biCompression, biSizeImage, biXPelsPerMeter, biYPelsPerMeter, biClrUsed, biClrImportant;
        }

        [DllImport("user32.dll")]
        public static extern bool GetCursorInfo(ref CURSORINFO info);

        [DllImport("user32.dll")]
        public static extern short GetAsyncKeyState(int key);

        [DllImport("user32.dll")]
        public static extern IntPtr LoadCursor(IntPtr instance, int name);

        [DllImport("user32.dll")]
        public static extern bool GetIconInfo(IntPtr icon, out ICONINFO info);

        [DllImport("user32.dll")]
        public static extern bool DrawIconEx(IntPtr dc, int x, int y, IntPtr icon, int width, int height, int step, IntPtr brush, int flags);

        [DllImport("gdi32.dll")]
        public static extern int GetObject(IntPtr bitmap, int size, out BITMAP result);

        [DllImport("gdi32.dll")]
        public static extern IntPtr CreateCompatibleDC(IntPtr dc);

        [DllImport("gdi32.dll")]
        public static extern bool DeleteDC(IntPtr dc);

        [DllImport("gdi32.dll")]
        public static extern IntPtr CreateDIBSection(IntPtr dc, ref BITMAPINFOHEADER header, int usage, out IntPtr bits, IntPtr section, int offset);

        [DllImport("gdi32.dll")]
        public static extern IntPtr SelectObject(IntPtr dc, IntPtr gdiObject);

        [DllImport("gdi32.dll")]
        public static extern bool DeleteObject(IntPtr gdiObject);

        [DllImport("gdi32.dll")]
        public static extern bool GdiFlush();
    }
}
