using System.Runtime;
using System.Windows;
using System.Windows.Threading;
using static ScreenClip.Services.NativeMethods;

namespace ScreenClip.Services;

/// <summary>
/// Gives memory back to Windows once the app is idle again (after a capture, after closing Settings): the captures
/// are large bitmaps, and a resident app should weigh as little as possible while it waits.
/// </summary>
internal static class MemoryTrim
{
    private static DispatcherTimer? s_timer;

    /// <summary>Trims a couple of seconds from now, unless something else asks again meanwhile.</summary>
    public static void Soon()
    {
        if (s_timer is null)
        {
            s_timer = new DispatcherTimer(DispatcherPriority.ApplicationIdle) { Interval = TimeSpan.FromSeconds(2) };
            s_timer.Tick += (_, _) =>
            {
                s_timer.Stop();
                if (IsIdle())
                    Now();
            };
        }
        s_timer.Stop();
        s_timer.Start();
    }

    public static void Now()
    {
        GCSettings.LargeObjectHeapCompactionMode = GCLargeObjectHeapCompactionMode.CompactOnce;
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        // Pages not used since are handed back; they come back from memory (not the disk) if needed again.
        SetProcessWorkingSetSize(GetCurrentProcess(), new IntPtr(-1), new IntPtr(-1));
    }

    // Not while a capture, a thumbnail or a window is on screen: they still need their bitmaps.
    private static bool IsIdle() => Application.Current?.Windows.Count == 0;
}
