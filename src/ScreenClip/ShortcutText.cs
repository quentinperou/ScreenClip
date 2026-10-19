using ScreenClip.Core;

namespace ScreenClip;

/// <summary>Shortcuts as the user reads them: the keys in the UI language (“Maj” for Shift on French keyboards).</summary>
public static class ShortcutText
{
    /// <summary>The keys of <paramref name="hotkey"/>, each translated: ["Ctrl", "Impr. écran"].</summary>
    public static IReadOnlyList<string> Keys(Hotkey hotkey) => Translate(hotkey.Parts);

    /// <summary>The modifier keys alone, translated (while the rest of a shortcut is being typed).</summary>
    public static IReadOnlyList<string> Keys(HotkeyModifiers modifiers) => Translate(Hotkey.ModifierParts(modifiers));

    private static List<string> Translate(IEnumerable<string> parts) =>
        parts.Select(part => Strings.Find("Key" + part) ?? part).ToList();

    /// <summary>"Ctrl+Impr. écran", or "" for no shortcut.</summary>
    public static string Format(Hotkey hotkey) => string.Join('+', Keys(hotkey));
}
