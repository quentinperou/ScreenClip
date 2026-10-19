using System.Diagnostics;
using Microsoft.Win32;
using ScreenClip.Core;
using static ScreenClip.Services.NativeMethods;

namespace ScreenClip.Services;

/// <summary>The Windows settings the app reads or changes: the Print Screen key, and the start with Windows.</summary>
internal static class WindowsSettings
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string RunValue = "ScreenClip";

    // Set while ScreenClip has taken Print Screen from the Snipping Tool: the uninstaller then gives it back
    // (installer/ScreenClip.iss), so that the key does not end up doing nothing.
    private const string AppKey = @"Software\ScreenClip";
    private const string TookPrintScreenValue = "TookPrintScreen";

    // ---------------------------------------------------------------- Print Screen key

    /// <summary>Whether Windows keeps Print Screen for its Snipping Tool (see <see cref="PrintScreenKey"/>).</summary>
    public static bool PrintScreenUsedByWindows
    {
        get
        {
            using var key = Registry.CurrentUser.OpenSubKey(PrintScreenKey.RegistryKey);
            return PrintScreenKey.IsUsedByWindows(key?.GetValue(PrintScreenKey.RegistryValue), Environment.OSVersion.Version.Build);
        }
    }

    /// <summary>
    /// Gives the Print Screen key to Windows' Snipping Tool (true, Windows' default) or leaves it to the apps (false):
    /// the same value as the switch of Windows Settings, for the current user only, no administrator rights needed.
    /// Windows may only apply it after the next sign-in. Returns false if the value could not be written.
    /// </summary>
    public static bool SetPrintScreenUsedByWindows(bool used)
    {
        try
        {
            using (var key = Registry.CurrentUser.CreateSubKey(PrintScreenKey.RegistryKey))
                key.SetValue(PrintScreenKey.RegistryValue, used ? 1 : 0, RegistryValueKind.DWord);
            using var app = Registry.CurrentUser.CreateSubKey(AppKey);
            if (used)
                app.DeleteValue(TookPrintScreenValue, throwOnMissingValue: false);
            else
                app.SetValue(TookPrintScreenValue, 1, RegistryValueKind.DWord);
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException or System.Security.SecurityException)
        {
            return false;
        }
        // What Settings does after a keyboard change; harmless if Windows does not listen to it for this value.
        SendMessageTimeout(HWND_BROADCAST, WM_SETTINGCHANGE, IntPtr.Zero, PrintScreenKey.RegistryKey, SMTO_ABORTIFHUNG, 1000, out _);
        return true;
    }

    /// <summary>Opens Windows Settings at the page of the Print Screen switch (Accessibility › Keyboard).</summary>
    public static void OpenKeyboardSettings() =>
        Process.Start(new ProcessStartInfo("ms-settings:easeofaccess-keyboard") { UseShellExecute = true });

    // ---------------------------------------------------------------- Start with Windows

    /// <summary>ScreenClip starts, in the notification area, when the user signs in (HKCU Run key).</summary>
    public static bool StartWithWindows
    {
        get
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey);
            return key?.GetValue(RunValue) is string;
        }
        set
        {
            try
            {
                using var key = Registry.CurrentUser.CreateSubKey(RunKey);
                if (value && Environment.ProcessPath is { } exe)
                    key.SetValue(RunValue, $"\"{exe}\" {App.BackgroundArgument}");
                else
                    key.DeleteValue(RunValue, throwOnMissingValue: false);
            }
            catch (Exception ex) when (ex is UnauthorizedAccessException or IOException or System.Security.SecurityException)
            {
            }
        }
    }

    /// <summary>
    /// True when this exe is the installed one (Program Files): only then is the start with Windows turned on by
    /// default. A development build never registers itself.
    /// </summary>
    /// <summary>
    /// Windows' own Screenshots folder, where Win+Print Screen and the Snipping Tool save: asked to Windows, since
    /// OneDrive may have moved it, and created by Windows when missing (with its translated name, “Captures d'écran”).
    /// </summary>
    public static string ScreenshotsFolder
    {
        get
        {
            int result = SHGetKnownFolderPath(in FOLDERID_Screenshots, KF_FLAG_CREATE, IntPtr.Zero, out var path);
            try
            {
                if (result == 0 && System.Runtime.InteropServices.Marshal.PtrToStringUni(path) is { Length: > 0 } folder)
                    return folder;
            }
            finally
            {
                System.Runtime.InteropServices.Marshal.FreeCoTaskMem(path);
            }
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), "Screenshots");
        }
    }

    public static bool IsInstalled =>
        Environment.ProcessPath is { } exe
        && exe.StartsWith(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), StringComparison.OrdinalIgnoreCase);
}
