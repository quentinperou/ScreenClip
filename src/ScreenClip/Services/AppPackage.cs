using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Microsoft.Win32;
using Windows.ApplicationModel;
using Windows.ApplicationModel.Activation;
using Windows.Storage;

namespace ScreenClip.Services;

/// <summary>
/// The Microsoft Store version: the same exe, run from its MSIX package (store\AppxManifest.xml, built by
/// scripts\build-store.ps1), as TextCase and NowPlaying do. In the package, writes to HKCU and new files under AppData
/// go to the package's own copy, which Windows and the other apps never read: so the start with Windows is the startup
/// task of the manifest, the Print Screen key is changed in Windows Settings by the user, the files handed to other
/// apps go to the package's temporary folder, and the Store updates the app. The version installed from GitHub, if it
/// is on the PC too, is offered for removal. The WinRT parts are only loaded in the package: each one is in a method of
/// its own, never compiled outside it (the GitHub version keeps nothing more in memory).
/// </summary>
internal static class AppPackage
{
    private const int AppModelErrorNoPackage = 15700;

    // The version installed from GitHub: Inno Setup's uninstall entry (AppId of installer\ScreenClip.iss + "_is1").
    private const string ClassicUninstallKey = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\{4F0B6C2E-9A37-4C1D-B5E8-3D7A2F61C0B9}_is1";

    /// <summary>The startup task declared in store\AppxManifest.xml.</summary>
    private const string StartupTaskId = "ScreenClipStartup";

    /// <summary>True when this process runs with a package identity: the Store version.</summary>
    public static bool IsPackaged { get; } = ReadIsPackaged();

    /// <summary>The page of the app in the Microsoft Store (it shows "Update" when there is one).</summary>
    public static string StorePage => $"ms-windows-store://pdp/?PFN={ReadFamilyName()}";

    /// <summary>
    /// The folder where files for other apps go (copied, dragged, recorded): %TEMP% outside the package; in the
    /// package, its own temporary folder, a real folder that the other apps can read.
    /// </summary>
    public static string TempRoot => IsPackaged ? PackageTempFolder() : Path.GetTempPath();

    /// <summary>
    /// Where a file this app wrote under %LOCALAPPDATA% really is, for another app to open it (the log in Notepad): in
    /// the package, a new file went to the package's copy of the folder.
    /// </summary>
    public static string RealPath(string path)
    {
        if (!IsPackaged)
            return path;
        try
        {
            string local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            string relative = Path.GetRelativePath(local, path);
            if (relative.StartsWith("..", StringComparison.Ordinal) || Path.IsPathRooted(relative))
                return path;
            string copy = Path.Combine(PackageCacheFolder(), "Local", relative);
            return File.Exists(copy) ? copy : path;
        }
        catch (Exception ex) when (ex is COMException or InvalidOperationException or ArgumentException)
        {
            return path;
        }
    }

    /// <summary>Started by its startup task when the user signed in: nothing to open, as with --background.</summary>
    public static bool LaunchedAtStartup()
    {
        if (!IsPackaged)
            return false;
        try
        {
            return ActivatedByStartupTask();
        }
        catch (Exception ex) when (ex is COMException or InvalidOperationException)
        {
            return false;
        }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static string PackageTempFolder() => ApplicationData.Current.TemporaryFolder.Path;

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static string PackageCacheFolder() => ApplicationData.Current.LocalCacheFolder.Path;

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static bool ActivatedByStartupTask() =>
        AppInstance.GetActivatedEventArgs()?.Kind == ActivationKind.StartupTask;

    /// <summary>The state of the startup task (in the package only).</summary>
    public static async Task<StartupState> GetStartupAsync()
    {
        try
        {
            return Map((await StartupTask.GetAsync(StartupTaskId)).State);
        }
        catch (Exception ex) when (ex is COMException or ArgumentException)
        {
            return StartupState.Off;
        }
    }

    /// <summary>
    /// Turns the startup task on or off. Windows asks nothing for a desktop app, but once the user turned it off in
    /// Task Manager or Settings the app cannot turn it on again (<see cref="StartupState.OffByUser"/>).
    /// </summary>
    public static async Task<StartupState> SetStartupAsync(bool on)
    {
        try
        {
            var task = await StartupTask.GetAsync(StartupTaskId);
            if (on)
                return Map(await task.RequestEnableAsync());
            task.Disable();
            return Map(task.State);
        }
        catch (Exception ex) when (ex is COMException or ArgumentException)
        {
            return StartupState.Off;
        }
    }

    private static StartupState Map(StartupTaskState state) => state switch
    {
        StartupTaskState.Enabled => StartupState.On,
        StartupTaskState.DisabledByUser => StartupState.OffByUser,
        StartupTaskState.EnabledByPolicy or StartupTaskState.DisabledByPolicy => StartupState.ByPolicy,
        _ => StartupState.Off,
    };

    /// <summary>The uninstaller of the version installed from GitHub, when it is on this PC.</summary>
    public static string? ClassicUninstaller()
    {
        try
        {
            using var machine = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64);
            using var key = machine.OpenSubKey(ClassicUninstallKey);
            return key?.GetValue("UninstallString") is string command && command.Trim('"') is var exe && File.Exists(exe) ? exe : null;
        }
        catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException or IOException)
        {
            return null;
        }
    }

    /// <summary>
    /// Starts the uninstaller of the version installed from GitHub (its own questions, its administrator prompt). It
    /// closes every ScreenClip, this one too.
    /// </summary>
    public static void StartClassicUninstall(string uninstaller)
    {
        try
        {
            // Through File Explorer: started by this process, it would run inside the package, its changes to the
            // registry kept apart from the real one (it gives the Print Screen key back to Windows).
            Process.Start(new ProcessStartInfo("explorer.exe", $"\"{uninstaller}\"") { UseShellExecute = false });
        }
        catch (System.ComponentModel.Win32Exception ex)
        {
            DevLog.Write($"uninstall of the GitHub version: {ex.Message}");
        }
    }

    private static bool ReadIsPackaged()
    {
        int length = 0;
        return GetCurrentPackageFullName(ref length, null) != AppModelErrorNoPackage;
    }

    private static string ReadFamilyName()
    {
        int length = 0;
        GetCurrentPackageFamilyName(ref length, null);
        var name = new char[length];
        return GetCurrentPackageFamilyName(ref length, name) == 0 ? new string(name, 0, Math.Max(0, length - 1)) : "";
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetCurrentPackageFullName(ref int packageFullNameLength, char[]? packageFullName);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetCurrentPackageFamilyName(ref int packageFamilyNameLength, char[]? packageFamilyName);
}

/// <summary>Whether the Store version starts with Windows (its startup task).</summary>
internal enum StartupState
{
    Off,
    On,
    /// <summary>Turned off by the user in Task Manager or Settings: only there can it be turned on again.</summary>
    OffByUser,
    /// <summary>Set by the administrator of the PC.</summary>
    ByPolicy,
}
