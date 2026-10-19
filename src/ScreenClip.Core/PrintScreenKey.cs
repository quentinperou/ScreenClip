namespace ScreenClip.Core;

/// <summary>
/// The Windows setting “Use the Print screen key to open screen capture” (Settings › Accessibility › Keyboard):
/// while it is on, Windows keeps the Print Screen key for its Snipping Tool. It is the DWORD
/// <c>HKCU\Control Panel\Keyboard\PrintScreenKeyForSnippingEnabled</c>.
/// </summary>
public static class PrintScreenKey
{
    public const string RegistryKey = @"Control Panel\Keyboard";
    public const string RegistryValue = "PrintScreenKeyForSnippingEnabled";

    /// <summary>Windows 11 build from which the setting is on when the value is missing (22H2, update of June 2023).</summary>
    public const int DefaultOnFromBuild = 22621;

    /// <summary>
    /// Whether Windows opens its Snipping Tool with Print Screen: the registry value when it is set, else Windows'
    /// default for its build (on from Windows 11 22H2, off before).
    /// </summary>
    public static bool IsUsedByWindows(object? registryValue, int windowsBuild) =>
        registryValue is int value ? value != 0 : windowsBuild >= DefaultOnFromBuild;

    /// <summary>True when one of the shortcuts needs the Print Screen key.</summary>
    public static bool IsNeededBy(IEnumerable<Hotkey> shortcuts) => shortcuts.Any(h => h.Key == Hotkey.PrintScreen);
}
