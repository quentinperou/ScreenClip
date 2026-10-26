using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;

using ScreenClip.Core;
using ScreenClip.Services;

namespace ScreenClip;

/// <summary>
/// The menu of the notification icon (Fluent theme, light or dark). It is built each time it opens, so that it always
/// shows the current shortcuts and language, and nothing of it stays in memory afterwards.
/// </summary>
internal static class TrayMenu
{
    private static ContextMenu? s_open;

    public static void Show()
    {
        if (s_open is { IsOpen: true })
        {
            s_open.IsOpen = false;
            return;
        }

        App.EnsureTheme();
        // A WPF menu of an app with no window in front loses the mouse capture as it opens, and closes at once. So the
        // menu belongs to a tiny invisible window under the pointer, activated first (a click on the notification
        // icon gives the app the right to come to the front), and closed with the menu.
        var host = new Window
        {
            WindowStyle = WindowStyle.None,
            AllowsTransparency = true,
            Background = System.Windows.Media.Brushes.Transparent,
            ShowInTaskbar = false,
            Topmost = true,
            ResizeMode = ResizeMode.NoResize,
            WindowStartupLocation = WindowStartupLocation.Manual,
            Width = 1,
            Height = 1,
            Title = "ScreenClip",
        };
        host.SourceInitialized += (_, _) =>
        {
            // At the pointer, but inside the work area: the menu then opens above the taskbar (or beside it). Put at
            // the pointer itself, on the icon, it reached into the taskbar, which sometimes covered its end (the author).
            NativeMethods.GetCursorPos(out var pointer);
            var info = new NativeMethods.MONITORINFO { cbSize = Marshal.SizeOf<NativeMethods.MONITORINFO>() };
            if (NativeMethods.GetMonitorInfo(NativeMethods.MonitorFromPoint(pointer, NativeMethods.MONITOR_DEFAULTTONEAREST), ref info))
            {
                pointer.X = Math.Clamp(pointer.X, info.rcWork.Left, info.rcWork.Right - 1);
                pointer.Y = Math.Clamp(pointer.Y, info.rcWork.Top, info.rcWork.Bottom - 1);
            }
            NativeMethods.SetWindowPos(new WindowInteropHelper(host).Handle, NativeMethods.HWND_TOPMOST,
                pointer.X, pointer.Y, 1, 1, NativeMethods.SWP_NOACTIVATE);
        };
        host.Show();
        NativeMethods.SetForegroundWindow(new WindowInteropHelper(host).Handle);
        host.Activate();

        var menu = Build();
        menu.PlacementTarget = host;
        // Under that point, or above it when there is no room (the taskbar at the bottom).
        menu.Placement = PlacementMode.Bottom;
        // Not the popup itself: activating it would deactivate the host, which closes the menu.
        menu.Closed += (_, _) =>
        {
            s_open = null;
            host.Close();
            MemoryTrim.Soon();
        };
        s_open = menu;
        menu.IsOpen = true;
    }

    internal static ContextMenu Build()
    {
        var settings = App.Settings;
        var menu = new ContextMenu { MinWidth = 300 };
        // The app itself, plainly (asked by the author): the editor, as it is.
        menu.Items.Add(Item(Strings.Get("OpenApp"), AppIcon(), Hotkey.None, () => App.OpenEditor()));
        menu.Items.Add(new Separator());

        menu.Items.Add(Item(Strings.Get("CaptureArea"), PathIcon("IconArea"), settings.GetShortcut(ShortcutAction.Area),
            () => App.Capture.RunAfterMenu(ShortcutAction.Area)));
        menu.Items.Add(Item(Strings.Get("CaptureWindow"), PathIcon("IconWindow"), settings.GetShortcut(ShortcutAction.Window),
            () => App.Capture.RunAfterMenu(ShortcutAction.Window)));
        menu.Items.Add(Item(Strings.Get("CaptureScreen"), PathIcon("IconScreen"), settings.GetShortcut(ShortcutAction.Screen),
            () => App.Capture.RunAfterMenu(ShortcutAction.Screen)));
        if (ScreenGrabber.Monitors().Count > 1)
            menu.Items.Add(Item(Strings.Get("CaptureAllScreens"), PathIcon("IconAllScreens"), settings.GetShortcut(ShortcutAction.AllScreens),
                () => App.Capture.RunAfterMenu(ShortcutAction.AllScreens)));
        var repeat = Item(Strings.Get("CaptureRepeatArea"), PathIcon("IconRepeat"), settings.GetShortcut(ShortcutAction.RepeatArea),
            () => App.Capture.RunAfterMenu(ShortcutAction.RepeatArea));
        repeat.IsEnabled = App.Capture.HasLastArea;
        menu.Items.Add(repeat);
        menu.Items.Add(App.Capture.IsRecording
            ? Item(Strings.Get("StopRecording"), GlyphIcon("\uE71A"), settings.GetShortcut(ShortcutAction.Video), () => Video.RecordingSession.Current?.Stop())
            : Item(Strings.Get("CaptureVideo"), PathIcon("IconVideo"), settings.GetShortcut(ShortcutAction.Video),
                () => App.Capture.RunAfterMenu(ShortcutAction.Video)));

        menu.Items.Add(new Separator());
        menu.Items.Add(Item(Strings.Get("OpenEditor"), GlyphIcon(""), Hotkey.None, () => App.OpenEditor(import: true)));
        menu.Items.Add(Item(Strings.Get("OpenCapturesFolder"), PathIcon("IconFolder"), Hotkey.None,
            () => ClipActions.OpenFolder(settings.EffectiveSaveFolder)));

        menu.Items.Add(new Separator());
        menu.Items.Add(Item(Strings.Get("Settings"), GlyphIcon(""), Hotkey.None, () => App.OpenSettings()));
        menu.Items.Add(Item(Strings.Get("Quit"), GlyphIcon(""), Hotkey.None, App.Quit));
        return menu;
    }

    private static MenuItem Item(string header, UIElement icon, Hotkey shortcut, Action action)
    {
        var item = new MenuItem
        {
            Header = header,
            Icon = icon,
            InputGestureText = App.Settings.ShortcutsEnabled ? ShortcutText.Format(shortcut) : "",
        };
        item.Click += (_, _) => action();
        return item;
    }

    private static System.Windows.Shapes.Path PathIcon(string key) => new()
    {
        Data = (System.Windows.Media.Geometry)Application.Current.FindResource(key),
        Style = (Style)Application.Current.FindResource("IconPath"),
    };

    // The app's icon, from a frame of the .ico large enough for 200 % (scaled down smoothly).
    private static Image AppIcon()
    {
        var decoder = BitmapDecoder.Create(new Uri("pack://application:,,,/Assets/ScreenClip.ico"), BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
        var frame = decoder.Frames.Where(f => f.PixelWidth >= 32).OrderBy(f => f.PixelWidth).FirstOrDefault() ?? decoder.Frames[0];
        var image = new Image { Source = frame, Width = 16, Height = 16 };
        RenderOptions.SetBitmapScalingMode(image, BitmapScalingMode.HighQuality);
        return image;
    }

    private static TextBlock GlyphIcon(string glyph) => new()
    {
        Text = glyph,
        FontSize = 16,
        Style = (Style)Application.Current.FindResource("IconText"),
    };
}
