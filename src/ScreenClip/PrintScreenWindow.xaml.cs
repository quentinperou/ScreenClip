using System.Windows;
using ScreenClip.Core;
using ScreenClip.Services;

namespace ScreenClip;

/// <summary>
/// Asked once, at the first start: Windows keeps Print Screen for its Snipping Tool, may ScreenClip take it? “Yes”
/// turns the Windows setting off for the user (Settings › Capture can give it back).
/// </summary>
public partial class PrintScreenWindow : Window
{
    private PrintScreenWindow()
    {
        InitializeComponent();
        MicaBackdrop.Apply(this);
        Closed += (_, _) =>
        {
            App.Settings.PrintScreenAsked = true;
            App.Settings.Save();
        };
    }

    /// <summary>True when a shortcut uses Print Screen, Windows keeps the key, and the user was never asked.</summary>
    internal static bool ShouldAsk(AppSettings settings) =>
        !settings.PrintScreenAsked
        && PrintScreenKey.IsNeededBy(Enum.GetValues<ShortcutAction>().Select(settings.GetShortcut))
        && WindowsSettings.PrintScreenUsedByWindows;

    /// <summary>Asks; <paramref name="then"/> runs once the window is closed (the editor, when started by hand).</summary>
    internal static void Ask(Action? then = null)
    {
        App.EnsureTheme();
        var window = new PrintScreenWindow();
        if (then is not null)
            window.Closed += (_, _) => then();
        window.Show();
        window.Activate();
    }

    /// <summary>
    /// Leaves Print Screen to the apps (Windows setting off) and registers the shortcuts again. Returns false when
    /// Windows still keeps the key: it then lets it go at the next sign-in.
    /// </summary>
    internal static bool TakeOver()
    {
        if (!WindowsSettings.SetPrintScreenUsedByWindows(false))
            return false;
        App.Tray.RegisterShortcuts(App.Settings);
        return !App.Tray.Refused.Any(action => App.Settings.GetShortcut(action).Key == Hotkey.PrintScreen);
    }

    private void Yes_Click(object sender, RoutedEventArgs e)
    {
        bool done = TakeOver();
        ResultText.Text = Strings.Get(done ? "PrintScreenDone" : "PrintScreenAfterSignIn");
        if (!done)
            ResultBar.SetResourceReference(StyleProperty, "WarningBar");
        WindowsSettingsLink.Visibility = done ? Visibility.Collapsed : Visibility.Visible;
        ResultBar.Visibility = Visibility.Visible;
        AskButtons.Visibility = Visibility.Collapsed;
        CloseButton.Visibility = Visibility.Visible;
        CloseButton.Focus();
    }

    private void No_Click(object sender, RoutedEventArgs e) => Close();

    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    private void WindowsSettings_Click(object sender, RoutedEventArgs e) => WindowsSettings.OpenKeyboardSettings();
}
