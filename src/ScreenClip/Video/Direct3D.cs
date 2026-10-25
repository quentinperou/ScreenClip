using System.Runtime.InteropServices;
using Vortice.Direct3D;
using Vortice.Direct3D11;
using Vortice.DXGI;
using Vortice.MediaFoundation;
using Windows.Graphics.Capture;
using Windows.Graphics.DirectX.Direct3D11;
using WinRT;

namespace ScreenClip.Video;

/// <summary>
/// Direct3D 11 for the video: the device the capture and the encoder share, and the bridges between Direct3D (Vortice)
/// and Windows.Graphics.Capture (WinRT).
/// </summary>
internal static class Direct3D
{
    private static readonly Guid GraphicsCaptureItemIid = new("79C3F95B-31F7-4EC2-A464-632EF5D30760");

    [ComImport, Guid("3628E81B-3CAC-4C60-B7F4-23CE0E0C3356"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IGraphicsCaptureItemInterop
    {
        IntPtr CreateForWindow([In] IntPtr window, [In] ref Guid iid);

        IntPtr CreateForMonitor([In] IntPtr monitor, [In] ref Guid iid);
    }

    [ComImport, Guid("A9B3D012-3DF2-4EE3-B8D1-8695F457D3C1"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IDirect3DDxgiInterfaceAccess
    {
        IntPtr GetInterface([In] ref Guid iid);
    }

    [DllImport("d3d11.dll", ExactSpelling = true)]
    private static extern int CreateDirect3D11DeviceFromDXGIDevice(IntPtr dxgiDevice, out IntPtr graphicsDevice);

    /// <summary>
    /// A device on the graphics card, safe to use from the capture thread and the encoder at once; the software one
    /// when there is no card that can.
    /// </summary>
    public static ID3D11Device CreateDevice(out ID3D11DeviceContext context)
    {
        FeatureLevel[] levels = [FeatureLevel.Level_11_1, FeatureLevel.Level_11_0, FeatureLevel.Level_10_1];
        var flags = DeviceCreationFlags.BgraSupport | DeviceCreationFlags.VideoSupport;
        if (D3D11.D3D11CreateDevice(IntPtr.Zero, DriverType.Hardware, flags, levels, out var device, out context).Failure)
            D3D11.D3D11CreateDevice(IntPtr.Zero, DriverType.Warp, DeviceCreationFlags.BgraSupport, levels, out device, out context).CheckError();
        using (var multithread = device!.QueryInterface<ID3D11Multithread>())
            multithread.SetMultithreadProtected(true);
        return device;
    }

    /// <summary>The same device, as Windows.Graphics.Capture wants it.</summary>
    public static IDirect3DDevice ToWinRT(ID3D11Device device)
    {
        using var dxgi = device.QueryInterface<IDXGIDevice>();
        Marshal.ThrowExceptionForHR(CreateDirect3D11DeviceFromDXGIDevice(dxgi.NativePointer, out var pointer));
        try
        {
            return MarshalInterface<IDirect3DDevice>.FromAbi(pointer);
        }
        finally
        {
            Marshal.Release(pointer);
        }
    }

    /// <summary>The texture behind a captured frame.</summary>
    public static ID3D11Texture2D TextureOf(IDirect3DSurface surface)
    {
        var access = surface.As<IDirect3DDxgiInterfaceAccess>();
        var iid = typeof(ID3D11Texture2D).GUID;
        return new ID3D11Texture2D(access.GetInterface(ref iid));
    }

    /// <summary>What Windows.Graphics.Capture films for a monitor (its HMONITOR).</summary>
    public static GraphicsCaptureItem ForMonitor(IntPtr monitor)
    {
        var interop = GraphicsCaptureItem.As<IGraphicsCaptureItemInterop>();
        var iid = GraphicsCaptureItemIid;
        var pointer = interop.CreateForMonitor(monitor, ref iid);
        try
        {
            return GraphicsCaptureItem.FromAbi(pointer);
        }
        finally
        {
            Marshal.Release(pointer);
        }
    }
}

/// <summary>Media Foundation, started once for the app's lifetime (the first video recorded or opened starts it).</summary>
internal static class Media
{
    private static readonly Lazy<bool> Started = new(() =>
    {
        MediaFactory.MFStartup(false).CheckError();
        return true;
    });

    public static void EnsureStarted() => _ = Started.Value;

    /// <summary>CODECAPI_AVEncMPVGOPSize: the frames between two key frames (where the editor can seek to).</summary>
    public static readonly Guid GopSize = new("95f31b26-95a4-41aa-9303-246a7fc6eef1");

    public static IMFMediaType VideoType(Guid subtype, int width, int height, int fps, uint bitrate = 0)
    {
        var type = MediaFactory.MFCreateMediaType();
        type.Set(MediaTypeAttributeKeys.MajorType, MediaTypeGuids.Video);
        type.Set(MediaTypeAttributeKeys.Subtype, subtype);
        type.Set(MediaTypeAttributeKeys.InterlaceMode, 2u); // progressive
        if (bitrate > 0)
            type.Set(MediaTypeAttributeKeys.AvgBitrate, bitrate);
        MediaFactory.MFSetAttributeSize(type, MediaTypeAttributeKeys.FrameSize, (uint)width, (uint)height);
        MediaFactory.MFSetAttributeRatio(type, MediaTypeAttributeKeys.FrameRate, (uint)fps, 1);
        MediaFactory.MFSetAttributeRatio(type, MediaTypeAttributeKeys.PixelAspectRatio, 1, 1);
        return type;
    }

    /// <summary>16-bit stereo PCM at 48 kHz: what the recorder mixes, and what an export writes.</summary>
    public static IMFMediaType Pcm()
    {
        var type = MediaFactory.MFCreateMediaType();
        type.Set(MediaTypeAttributeKeys.MajorType, MediaTypeGuids.Audio);
        type.Set(MediaTypeAttributeKeys.Subtype, AudioFormatGuids.Pcm);
        type.Set(MediaTypeAttributeKeys.AudioNumChannels, 2u);
        type.Set(MediaTypeAttributeKeys.AudioSamplesPerSecond, 48000u);
        type.Set(MediaTypeAttributeKeys.AudioBitsPerSample, 16u);
        type.Set(MediaTypeAttributeKeys.AudioBlockAlignment, 4u);
        type.Set(MediaTypeAttributeKeys.AudioAvgBytesPerSecond, 192000u);
        return type;
    }

    /// <summary>AAC, 160 kbit/s stereo.</summary>
    public static IMFMediaType Aac()
    {
        var type = MediaFactory.MFCreateMediaType();
        type.Set(MediaTypeAttributeKeys.MajorType, MediaTypeGuids.Audio);
        type.Set(MediaTypeAttributeKeys.Subtype, AudioFormatGuids.Aac);
        type.Set(MediaTypeAttributeKeys.AudioNumChannels, 2u);
        type.Set(MediaTypeAttributeKeys.AudioSamplesPerSecond, 48000u);
        type.Set(MediaTypeAttributeKeys.AudioBitsPerSample, 16u);
        type.Set(MediaTypeAttributeKeys.AudioAvgBytesPerSecond, 20000u);
        return type;
    }

    /// <summary>A sample of <paramref name="bytes"/> from memory.</summary>
    public static IMFSample Sample(ReadOnlySpan<byte> bytes, long time, long duration)
    {
        using var buffer = MediaFactory.MFCreateMemoryBuffer(bytes.Length);
        buffer.Lock(out IntPtr data, out _, out _);
        unsafe
        {
            bytes.CopyTo(new Span<byte>((void*)data, bytes.Length));
        }
        buffer.Unlock();
        buffer.CurrentLength = bytes.Length;
        var sample = MediaFactory.MFCreateSample();
        sample.AddBuffer(buffer);
        sample.SampleTime = time;
        sample.SampleDuration = duration;
        return sample;
    }

    /// <summary>A bit rate for H.264 that keeps the text of a screen sharp: 0.12 bit per pixel and frame.</summary>
    public static uint Bitrate(int width, int height, int fps) =>
        (uint)Math.Clamp((long)width * height * fps * 12 / 100, 2_000_000, 50_000_000);
}
