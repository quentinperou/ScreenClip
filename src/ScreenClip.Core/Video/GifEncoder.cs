namespace ScreenClip.Core.Video;

/// <summary>
/// Writes an animated GIF that loops, frame after frame, from 32-bit BGRA pixels. Made for screen recordings: each
/// frame keeps only the rectangle that changed (the rest stays from the frame before, through a transparent color), a
/// frame with no change lengthens the one before, and each frame has its own 255 colors (median cut).
/// </summary>
public sealed class GifEncoder
{
    private const int MaxColors = 255; // index 255 is the transparent one

    private readonly Stream _stream;
    private byte[]? _previous;         // the last frame given, to see what changed
    private Pending? _pending;         // the last frame, written once its length is known

    private sealed class Pending
    {
        public required int X, Y, Width, Height;
        public required byte[] Indices;
        public required (byte R, byte G, byte B)[] Palette;
        public required bool HasTransparency;
        public int Delay;
    }

    public GifEncoder(Stream stream, int width, int height)
    {
        if (width <= 0 || height <= 0 || width > 65535 || height > 65535)
            throw new ArgumentOutOfRangeException(nameof(width));
        _stream = stream;
        Width = width;
        Height = height;
        Write("GIF89a"u8);
        WriteShort(width);
        WriteShort(height);
        _stream.WriteByte(0x00); // no global color table: each frame has its own
        _stream.WriteByte(0);    // background color index
        _stream.WriteByte(0);    // pixel aspect ratio
        // NETSCAPE2.0: loop forever.
        Write([0x21, 0xFF, 0x0B]);
        Write("NETSCAPE2.0"u8);
        Write([0x03, 0x01, 0x00, 0x00, 0x00]);
    }

    public int Width { get; }

    public int Height { get; }

    /// <summary>Adds a frame shown for <paramref name="delayCentiseconds"/> (1/100 s, the unit of GIF).</summary>
    public void AddFrame(ReadOnlySpan<byte> bgra, int stride, int delayCentiseconds)
    {
        delayCentiseconds = Math.Max(1, delayCentiseconds);
        var pixels = new byte[Width * Height * 4];
        for (int y = 0; y < Height; y++)
            bgra.Slice(y * stride, Width * 4).CopyTo(pixels.AsSpan(y * Width * 4));

        // What changed since the frame before: only that rectangle is written.
        int left = 0, top = 0, right = Width - 1, bottom = Height - 1;
        bool[]? same = null;
        if (_previous is not null)
        {
            same = new bool[Width * Height];
            left = Width;
            top = Height;
            right = -1;
            bottom = -1;
            for (int y = 0; y < Height; y++)
            {
                for (int x = 0; x < Width; x++)
                {
                    int i = y * Width + x, o = i * 4;
                    if (pixels[o] == _previous[o] && pixels[o + 1] == _previous[o + 1] && pixels[o + 2] == _previous[o + 2])
                    {
                        same[i] = true;
                        continue;
                    }
                    left = Math.Min(left, x);
                    right = Math.Max(right, x);
                    top = Math.Min(top, y);
                    bottom = Math.Max(bottom, y);
                }
            }
            if (right < 0)
            {
                // Nothing changed: the frame before lasts longer.
                _pending!.Delay += delayCentiseconds;
                return;
            }
        }
        Flush();
        _previous = pixels;

        int width = right - left + 1, height = bottom - top + 1;
        var colors = new List<int>(width * height);
        bool transparency = false;
        for (int y = top; y <= bottom; y++)
        {
            for (int x = left; x <= right; x++)
            {
                int i = y * Width + x;
                if (same is not null && same[i])
                {
                    transparency = true;
                    continue;
                }
                int o = i * 4;
                colors.Add((pixels[o + 2] << 16) | (pixels[o + 1] << 8) | pixels[o]);
            }
        }
        var palette = Quantizer.Palette(colors, MaxColors);
        var lookup = new Quantizer.Lookup(palette);
        var indices = new byte[width * height];
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                int i = (top + y) * Width + left + x;
                if (same is not null && same[i])
                {
                    indices[y * width + x] = MaxColors;
                    continue;
                }
                int o = i * 4;
                indices[y * width + x] = lookup.IndexOf(pixels[o + 2], pixels[o + 1], pixels[o]);
            }
        }
        _pending = new Pending
        {
            X = left,
            Y = top,
            Width = width,
            Height = height,
            Indices = indices,
            Palette = palette,
            HasTransparency = transparency,
            Delay = delayCentiseconds,
        };
    }

    /// <summary>Writes the last frame and the end of the file.</summary>
    public void Finish()
    {
        Flush();
        _stream.WriteByte(0x3B);
        _stream.Flush();
    }

    private void Flush()
    {
        if (_pending is not { } frame)
            return;
        _pending = null;

        // Graphic control: kept under the next frame (disposal 1), its length, the transparent index.
        Write([0x21, 0xF9, 0x04, (byte)(0x04 | (frame.HasTransparency ? 0x01 : 0x00))]);
        WriteShort(Math.Min(frame.Delay, 65535));
        _stream.WriteByte(MaxColors);
        _stream.WriteByte(0);

        // Image descriptor and its color table: 256 entries, the last one for transparency.
        _stream.WriteByte(0x2C);
        WriteShort(frame.X);
        WriteShort(frame.Y);
        WriteShort(frame.Width);
        WriteShort(frame.Height);
        _stream.WriteByte(0x80 | 7); // local color table of 2^(7+1) = 256 colors
        for (int i = 0; i < 256; i++)
        {
            var (r, g, b) = i < frame.Palette.Length ? frame.Palette[i] : ((byte)0, (byte)0, (byte)0);
            Write([r, g, b]);
        }
        Lzw.Write(_stream, frame.Indices, 8);
    }

    private void Write(ReadOnlySpan<byte> bytes) => _stream.Write(bytes);

    private void WriteShort(int value)
    {
        _stream.WriteByte((byte)value);
        _stream.WriteByte((byte)(value >> 8));
    }
}

/// <summary>Median cut on 5-bit-per-channel colors, and a lookup from a color to its nearest entry.</summary>
internal static class Quantizer
{
    private sealed class Box
    {
        public required List<int> Bins;   // 15-bit colors
        public long Count;
    }

    /// <summary>At most <paramref name="max"/> colors that stand for <paramref name="colors"/> (0xRRGGBB).</summary>
    public static (byte R, byte G, byte B)[] Palette(List<int> colors, int max)
    {
        var count = new long[32768];
        var sumR = new long[32768];
        var sumG = new long[32768];
        var sumB = new long[32768];
        foreach (int c in colors)
        {
            int r = (c >> 16) & 0xFF, g = (c >> 8) & 0xFF, b = c & 0xFF;
            int bin = ((r >> 3) << 10) | ((g >> 3) << 5) | (b >> 3);
            count[bin]++;
            sumR[bin] += r;
            sumG[bin] += g;
            sumB[bin] += b;
        }
        var used = new List<int>();
        for (int bin = 0; bin < count.Length; bin++)
        {
            if (count[bin] > 0)
                used.Add(bin);
        }
        if (used.Count == 0)
            return [(0, 0, 0)];

        var boxes = new List<Box> { new() { Bins = used, Count = colors.Count } };
        while (boxes.Count < max)
        {
            // Split the box with the most pixels over the widest range.
            Box? widest = null;
            int axis = 0;
            long best = 0;
            foreach (var box in boxes)
            {
                if (box.Bins.Count < 2)
                    continue;
                for (int a = 0; a < 3; a++)
                {
                    int shift = 10 - 5 * a;
                    int low = 31, high = 0;
                    foreach (int bin in box.Bins)
                    {
                        int v = (bin >> shift) & 31;
                        low = Math.Min(low, v);
                        high = Math.Max(high, v);
                    }
                    long score = (long)(high - low) * box.Count;
                    if (high > low && score > best)
                    {
                        best = score;
                        widest = box;
                        axis = shift;
                    }
                }
            }
            if (widest is null)
                break;
            int ax = axis;
            widest.Bins.Sort((p, q) => ((p >> ax) & 31).CompareTo((q >> ax) & 31));
            long half = widest.Count / 2, seen = 0;
            int cut = 1;
            for (int i = 0; i < widest.Bins.Count - 1; i++)
            {
                seen += count[widest.Bins[i]];
                cut = i + 1;
                if (seen >= half)
                    break;
            }
            var first = widest.Bins.GetRange(0, cut);
            var second = widest.Bins.GetRange(cut, widest.Bins.Count - cut);
            boxes.Remove(widest);
            boxes.Add(new Box { Bins = first, Count = first.Sum(bin => count[bin]) });
            boxes.Add(new Box { Bins = second, Count = second.Sum(bin => count[bin]) });
        }

        return [.. boxes.Select(box =>
        {
            long n = 0, r = 0, g = 0, b = 0;
            foreach (int bin in box.Bins)
            {
                n += count[bin];
                r += sumR[bin];
                g += sumG[bin];
                b += sumB[bin];
            }
            return ((byte)(r / n), (byte)(g / n), (byte)(b / n));
        })];
    }

    /// <summary>The nearest palette entry of a color, worked out once per 15-bit color.</summary>
    public sealed class Lookup((byte R, byte G, byte B)[] palette)
    {
        private readonly short[] _cache = Enumerable.Repeat((short)-1, 32768).ToArray();

        public byte IndexOf(byte r, byte g, byte b)
        {
            int bin = ((r >> 3) << 10) | ((g >> 3) << 5) | (b >> 3);
            if (_cache[bin] >= 0)
                return (byte)_cache[bin];
            int best = 0, bestDistance = int.MaxValue;
            for (int i = 0; i < palette.Length; i++)
            {
                int dr = palette[i].R - r, dg = palette[i].G - g, db = palette[i].B - b;
                int distance = dr * dr * 3 + dg * dg * 4 + db * db * 2;
                if (distance < bestDistance)
                {
                    bestDistance = distance;
                    best = i;
                }
            }
            _cache[bin] = (short)best;
            return (byte)best;
        }
    }
}

/// <summary>The LZW compression of GIF: variable-width codes, packed from the low bits, in blocks of 255 bytes.</summary>
internal static class Lzw
{
    public static void Write(Stream stream, byte[] indices, int minimumCodeSize)
    {
        stream.WriteByte((byte)minimumCodeSize);
        int clear = 1 << minimumCodeSize, end = clear + 1;
        var block = new byte[255];
        int blockLength = 0, bitBuffer = 0, bitCount = 0;

        void Emit(int code, int size)
        {
            bitBuffer |= code << bitCount;
            bitCount += size;
            while (bitCount >= 8)
            {
                block[blockLength++] = (byte)bitBuffer;
                bitBuffer >>= 8;
                bitCount -= 8;
                if (blockLength == 255)
                {
                    stream.WriteByte(255);
                    stream.Write(block, 0, 255);
                    blockLength = 0;
                }
            }
        }

        var table = new Dictionary<int, int>(4096);
        int codeSize = minimumCodeSize + 1, next = end + 1;
        Emit(clear, codeSize);
        if (indices.Length > 0)
        {
            int prefix = indices[0];
            for (int i = 1; i < indices.Length; i++)
            {
                int key = (prefix << 8) | indices[i];
                if (table.TryGetValue(key, out int code))
                {
                    prefix = code;
                    continue;
                }
                Emit(prefix, codeSize);
                if (next < 4095) // as giflib: some decoders stumble on the 4096th code
                {
                    table[key] = next++;
                    if (next > (1 << codeSize) && codeSize < 12)
                        codeSize++;
                }
                else
                {
                    // The table is full: start again.
                    Emit(clear, codeSize);
                    table.Clear();
                    codeSize = minimumCodeSize + 1;
                    next = end + 1;
                }
                prefix = indices[i];
            }
            Emit(prefix, codeSize);
        }
        Emit(end, codeSize);
        if (bitCount > 0)
            Emit(0, 8 - bitCount);
        if (blockLength > 0)
        {
            stream.WriteByte((byte)blockLength);
            stream.Write(block, 0, blockLength);
        }
        stream.WriteByte(0);
    }
}
