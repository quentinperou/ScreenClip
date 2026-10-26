using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using ScreenClip.Core;
using static ScreenClip.Services.NativeMethods;

namespace ScreenClip.Services;

/// <summary>
/// The resident part of the app: one hidden Win32 window (no WPF window, nothing drawn) that owns the notification
/// icon and the global shortcuts, and closes the app when Windows or an installer asks. Nothing runs at rest: the
/// window only wakes up on a message.
/// </summary>
internal sealed class TrayHost : IDisposable
{
    private const int IconId = 1;
    private const int CallbackMessage = WM_APP + 1;
    private const int ErrorHotkeyAlreadyRegistered = 1409;

    private readonly HwndSource _window;
    private readonly int _taskbarCreated;
    private IntPtr _icon;
    private bool _iconAdded;
    private readonly Dictionary<ShortcutAction, Hotkey> _registered = [];

    public TrayHost()
    {
        // A top-level popup that is never shown: unlike a message-only window, it receives the WM_CLOSE of the
        // Restart Manager (installer of an update) and the end-of-session messages.
        var parameters = new HwndSourceParameters("ScreenClip")
        {
            WindowStyle = WS_POPUP,
            ExtendedWindowStyle = WS_EX_TOOLWINDOW,
            Width = 0,
            Height = 0,
        };
        _window = new HwndSource(parameters);
        _window.AddHook(WndProc);
        _taskbarCreated = RegisterWindowMessage("TaskbarCreated");
        _icon = LoadSmallIcon();
        AddIcon();
    }

    public IntPtr Handle => _window.Handle;

    /// <summary>Left click on the notification icon (or Enter/Space on it from the keyboard).</summary>
    public event EventHandler? IconClicked;

    /// <summary>Right click on the notification icon (or the menu key on it).</summary>
    public event EventHandler? MenuRequested;

    /// <summary>A capture shortcut was pressed.</summary>
    public event EventHandler<ShortcutAction>? ShortcutPressed;

    // ---------------------------------------------------------------- Shortcuts

    /// <summary>
    /// Registers the shortcuts of the settings, replacing the previous ones. Returns those Windows refused (taken by
    /// another app, or Print Screen kept by Windows for its Snipping Tool).
    /// </summary>
    public IReadOnlyList<ShortcutAction> RegisterShortcuts(AppSettings settings)
    {
        UnregisterShortcuts();
        var refused = new List<ShortcutAction>();
        if (!settings.ShortcutsEnabled)
        {
            Refused = refused;
            return refused;
        }
        foreach (var action in Enum.GetValues<ShortcutAction>())
        {
            var hotkey = settings.GetShortcut(action);
            if (hotkey.IsNone)
                continue;
            // The same keys for two actions: the first one wins.
            if (_registered.ContainsValue(hotkey)
                || !RegisterHotKey(Handle, (int)action + 1, (int)hotkey.Modifiers | MOD_NOREPEAT, hotkey.Key))
            {
                refused.Add(action);
                continue;
            }
            _registered[action] = hotkey;
        }
        Refused = refused;
        return refused;
    }

    /// <summary>The shortcuts Windows refused at the last registration.</summary>
    public IReadOnlyList<ShortcutAction> Refused { get; private set; } = [];

    /// <summary>While a new shortcut is being typed in Settings, the current ones must not fire.</summary>
    public void UnregisterShortcuts()
    {
        foreach (var action in _registered.Keys)
            UnregisterHotKey(Handle, (int)action + 1);
        _registered.Clear();
    }

    /// <summary>Whether Windows accepts <paramref name="hotkey"/> right now (not taken by another app).</summary>
    public bool IsAvailable(Hotkey hotkey)
    {
        const int probeId = 0x7F00;
        if (!RegisterHotKey(Handle, probeId, (int)hotkey.Modifiers | MOD_NOREPEAT, hotkey.Key))
            return Marshal.GetLastWin32Error() != ErrorHotkeyAlreadyRegistered;
        UnregisterHotKey(Handle, probeId);
        return true;
    }

    // ---------------------------------------------------------------- Notification icon

    private void AddIcon()
    {
        var data = NewIconData();
        data.uFlags = NIF_MESSAGE | NIF_ICON | NIF_TIP | NIF_SHOWTIP;
        data.uCallbackMessage = CallbackMessage;
        data.hIcon = _icon;
        data.szTip = "ScreenClip";
        _iconAdded = Shell_NotifyIcon(NIM_ADD, ref data);
        data.uVersion = NOTIFYICON_VERSION_4;
        Shell_NotifyIcon(NIM_SETVERSION, ref data);
    }

    private NOTIFYICONDATA NewIconData() => new()
    {
        cbSize = Marshal.SizeOf<NOTIFYICONDATA>(),
        hWnd = Handle,
        uID = IconId,
        szTip = "",
        szInfo = "",
        szInfoTitle = "",
    };

    // The icon of the exe (Assets\ScreenClip.ico) at the size of the notification area. 32512 is the resource id
    // the .NET SDK gives the application icon.
    private static IntPtr LoadSmallIcon()
    {
        int size = GetSystemMetricsForDpi(SM_CXSMICON, GetDpiForSystem());
        if (LoadIconWithScaleDown(GetModuleHandle(null), new IntPtr(32512), size, size, out IntPtr icon) == 0)
            return icon;
        var small = new IntPtr[1];
        return Environment.ProcessPath is { } exe && ExtractIconEx(exe, 0, null, small, 1) > 0 ? small[0] : IntPtr.Zero;
    }

    // ---------------------------------------------------------------- Messages

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        switch (msg)
        {
            case WM_HOTKEY:
                int id = wParam.ToInt32() - 1;
                if (Enum.IsDefined(typeof(ShortcutAction), id))
                    ShortcutPressed?.Invoke(this, (ShortcutAction)id);
                handled = true;
                break;

            case CallbackMessage:
                // NOTIFYICON_VERSION_4: the event is in the low word of lParam.
                int iconEvent = lParam.ToInt32() & 0xFFFF;
                if (iconEvent is NIN_SELECT or NIN_KEYSELECT)
                    IconClicked?.Invoke(this, EventArgs.Empty);
                else if (iconEvent == WM_CONTEXTMENU)
                    MenuRequested?.Invoke(this, EventArgs.Empty);
                handled = true;
                break;

            case WM_QUERYENDSESSION:
                handled = true;
                return new IntPtr(1);

            case WM_ENDSESSION when wParam != IntPtr.Zero:
            case WM_CLOSE:
                // End of the Windows session, or the installer of an update closing the app (Restart Manager).
                handled = true;
                App.Quit();
                break;

            default:
                // Explorer restarted: the notification area is new and empty.
                if (msg == _taskbarCreated && _taskbarCreated != 0)
                    AddIcon();
                break;
        }
        return IntPtr.Zero;
    }

    public void Dispose()
    {
        UnregisterShortcuts();
        if (_iconAdded)
        {
            var data = NewIconData();
            Shell_NotifyIcon(NIM_DELETE, ref data);
            _iconAdded = false;
        }
        if (_icon != IntPtr.Zero)
        {
            DestroyIcon(_icon);
            _icon = IntPtr.Zero;
        }
        _window.Dispose();
    }
}
