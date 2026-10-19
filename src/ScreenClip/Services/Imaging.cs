using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using SkiaSharp;

namespace ScreenClip.Services;

/// <summary>Pictures between WPF (captures, files, clipboard, the preview) and SkiaSharp (the composition).</summary>
internal static class Imaging
{
    /// <summary>The extensions offered by “Import an image” (Windows reads them, HEIC with its codec).</summary>
    public const string OpenFilter = "*.png;*.jpg;*.jpeg;*.bmp;*.gif;*.webp;*.tif;*.tiff;*.heic;*.heif";

    /// <summary>A SkiaSharp copy of <paramref name="source"/>, in 32-bit BGRA.</summary>
    public static SKImage ToSkia(BitmapSource source)
    {
        bool opaque = source.Format == PixelFormats.Bgr32 || source.Format == PixelFormats.Bgr24;
        BitmapSource bgra = source.Format == PixelFormats.Bgra32 || source.Format == PixelFormats.Pbgra32 || source.Format == PixelFormats.Bgr32
            ? source
            : new FormatConvertedBitmap(source, PixelFormats.Bgra32, null, 0);
        int width = bgra.PixelWidth, height = bgra.PixelHeight, stride = width * 4;
        var pixels = new byte[stride * height];
        bgra.CopyPixels(pixels, stride, 0);
        if (opaque)
        {
            // Bgr32 leaves the fourth byte undefined (often 0): make every pixel opaque.
            for (int i = 3; i < pixels.Length; i += 4)
                pixels[i] = 255;
        }
        var alpha = opaque ? SKAlphaType.Opaque : bgra.Format == PixelFormats.Pbgra32 ? SKAlphaType.Premul : SKAlphaType.Unpremul;
        return SKImage.FromPixelCopy(new SKImageInfo(width, height, SKColorType.Bgra8888, alpha), pixels, stride);
    }

    /// <summary>Copies <paramref name="image"/> into <paramref name="target"/>, made again only when the size changes.</summary>
    public static WriteableBitmap CopyTo(SKImage image, WriteableBitmap? target)
    {
        if (target is null || target.PixelWidth != image.Width || target.PixelHeight != image.Height)
            target = new WriteableBitmap(image.Width, image.Height, 96, 96, PixelFormats.Pbgra32, null);
        target.Lock();
        try
        {
            var info = new SKImageInfo(image.Width, image.Height, SKColorType.Bgra8888, SKAlphaType.Premul);
            image.ReadPixels(info, target.BackBuffer, target.BackBufferStride, 0, 0);
            target.AddDirtyRect(new Int32Rect(0, 0, image.Width, image.Height));
        }
        finally
        {
            target.Unlock();
        }
        return target;
    }

    /// <summary>A frozen WPF bitmap of <paramref name="image"/> (for the clipboard and drag and drop).</summary>
    public static BitmapSource ToWpf(SKImage image)
    {
        var bitmap = CopyTo(image, null);
        bitmap.Freeze();
        return bitmap;
    }

    /// <summary>Reads a picture file; throws when it is not one Windows can read.</summary>
    public static BitmapSource Load(string path)
    {
        using var stream = File.OpenRead(path);
        var decoder = BitmapDecoder.Create(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
        var frame = decoder.Frames[0];
        frame.Freeze();
        return frame;
    }

    /// <summary>The picture on the clipboard: PNG first (keeps transparency), then a bitmap, then a picture file copied in File Explorer.</summary>
    public static BitmapSource? FromClipboard()
    {
        try
        {
            if (Clipboard.GetData("PNG") is MemoryStream png)
            {
                var frame = BitmapFrame.Create(png, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
                frame.Freeze();
                return frame;
            }
            if (Clipboard.ContainsImage() && Clipboard.GetImage() is { } image)
                return image;
            if (Clipboard.ContainsFileDropList())
            {
                foreach (string? file in Clipboard.GetFileDropList())
                {
                    if (file is not null && IsPictureFile(file))
                        return Load(file);
                }
            }
        }
        catch (Exception ex) when (ex is COMException or ExternalException or NotSupportedException or IOException or FileFormatException)
        {
        }
        return null;
    }

    public static bool IsPictureFile(string path) =>
        OpenFilter.Split(';').Any(pattern => path.EndsWith(pattern[1..], StringComparison.OrdinalIgnoreCase));
}
