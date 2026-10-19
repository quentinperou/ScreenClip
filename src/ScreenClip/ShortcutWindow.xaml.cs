using System.Windows;
using System.Windows.Input;
using ScreenClip.Core;
using ScreenClip.Services;

namespace ScreenClip;

/// <summary>
/// Types a new shortcut for one capture action: the keys pressed show as keys, and the combination is checked (not
/// in the way of typing, not used by another action of ScreenClip, not taken by another app).
/// The shortcuts of ScreenClip are off while it is open, so that they can be typed again.
/// </summary>
public partial class ShortcutWindow : Window
{
    private readonly ShortcutAction _action;
    private Hotkey _candidate;

    internal ShortcutWindow(ShortcutAction action)
    {
        _action = action;
        InitializeComponent();
        Prompt.Text = Strings.Format("ShortcutPrompt", Strings.Get("Shortcut" + action));
        PreviewKeyDown += (_, e) => OnKey(e);
        // Windows only sends the release of Print Screen.
        PreviewKeyUp += (_, e) =>
        {
            if ((e.Key == Key.System ? e.SystemKey : e.Key) is Key.Snapshot)
                OnKey(e);
        };
    }

    /// <summary>The shortcut chosen (<see cref="Hotkey.None"/>: no shortcut), or null when cancelled.</summary>
    internal Hotkey? Result { get; private set; }

    private void OnKey(KeyEventArgs e)
    {
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        var modifiers = CurrentModifiers();

        // Escape, Enter and Tab alone keep their usual meaning in the dialog.
        if (modifiers == HotkeyModifiers.None && key is Key.Escape or Key.Enter or Key.Tab)
            return;
        e.Handled = true;

        if (key is Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt or Key.LeftShift or Key.RightShift
            or Key.LWin or Key.RWin)
        {
            // Modifiers alone: shown while the key is awaited.
            ShowKeys(ShortcutText.Keys(modifiers));
            return;
        }

        int vk = KeyInterop.VirtualKeyFromKey(key);
        if (Hotkey.KeyName(vk) is null)
            return;
        _candidate = new Hotkey(modifiers, vk);
        ShowKeys(ShortcutText.Keys(_candidate));
        Check();
    }

    private static HotkeyModifiers CurrentModifiers()
    {
        var modifiers = HotkeyModifiers.None;
        var wpf = Keyboard.Modifiers;
        if (wpf.HasFlag(ModifierKeys.Control)) modifiers |= HotkeyModifiers.Control;
        if (wpf.HasFlag(ModifierKeys.Alt)) modifiers |= HotkeyModifiers.Alt;
        if (wpf.HasFlag(ModifierKeys.Shift)) modifiers |= HotkeyModifiers.Shift;
        if (Keyboard.IsKeyDown(Key.LWin) || Keyboard.IsKeyDown(Key.RWin)) modifiers |= HotkeyModifiers.Windows;
        return modifiers;
    }

    private void ShowKeys(IEnumerable<string> keys)
    {
        var list = keys.ToList();
        Keys.ItemsSource = list;
        Placeholder.Visibility = list.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void Check()
    {
        string? problem = null;
        var usedBy = Enum.GetValues<ShortcutAction>()
            .Where(a => a != _action && App.Settings.GetShortcut(a) == _candidate)
            .Cast<ShortcutAction?>()
            .FirstOrDefault();
        if (!_candidate.IsAllowed)
            problem = Strings.Get("ShortcutNeedsModifier");
        else if (usedBy is { } other)
            problem = Strings.Format("ShortcutUsedByAction", Strings.Get("Shortcut" + other));
        else if (!App.Tray.IsAvailable(_candidate))
            problem = Strings.Get("ShortcutTaken");

        Problem.Text = problem ?? "";
        Problem.Visibility = problem is null ? Visibility.Collapsed : Visibility.Visible;
        OkButton.IsEnabled = problem is null;
    }

    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        Result = _candidate;
        DialogResult = true;
    }

    private void None_Click(object sender, RoutedEventArgs e)
    {
        Result = Hotkey.None;
        DialogResult = true;
    }
}
