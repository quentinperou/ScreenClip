namespace ScreenClip.Core;

/// <summary>Modifier keys of a shortcut. The values are those of <c>RegisterHotKey</c> (MOD_ALT…).</summary>
[Flags]
public enum HotkeyModifiers
{
    None = 0,
    Alt = 1,
    Control = 2,
    Shift = 4,
    Windows = 8,
}

/// <summary>
/// A global shortcut: modifiers and a Windows virtual-key code. Saved in the settings as text in English,
/// whatever the UI language: "Ctrl+Shift+PrintScreen", "Alt+F2", "" for no shortcut.
/// </summary>
public readonly record struct Hotkey(HotkeyModifiers Modifiers, int Key)
{
    public const int PrintScreen = 0x2C;

    public static readonly Hotkey None = default;

    public bool IsNone => Key == 0;

    // Names of the keys a shortcut may use, by virtual-key code. Letters, digits and F-keys are added below.
    private static readonly Dictionary<int, string> NamedKeys = new()
    {
        [0x13] = "Pause",
        [0x20] = "Space",
        [0x21] = "PageUp",
        [0x22] = "PageDown",
        [0x23] = "End",
        [0x24] = "Home",
        [PrintScreen] = "PrintScreen",
        [0x2D] = "Insert",
        [0x2E] = "Delete",
    };

    private static readonly (HotkeyModifiers Modifier, string Name)[] ModifierNames =
    [
        (HotkeyModifiers.Control, "Ctrl"),
        (HotkeyModifiers.Alt, "Alt"),
        (HotkeyModifiers.Shift, "Shift"),
        (HotkeyModifiers.Windows, "Win"),
    ];

    /// <summary>The name of a key that can be used in a shortcut, or null (modifier, mouse button, unknown key).</summary>
    public static string? KeyName(int key) => key switch
    {
        >= 0x30 and <= 0x39 => ((char)key).ToString(),          // 0-9
        >= 0x41 and <= 0x5A => ((char)key).ToString(),          // A-Z
        >= 0x70 and <= 0x87 => "F" + (key - 0x70 + 1),          // F1-F24
        _ => NamedKeys.GetValueOrDefault(key),
    };

    /// <summary>
    /// The parts of the shortcut in display order, in English: ["Ctrl", "Shift", "PrintScreen"]. The app translates
    /// the ones that need it ("Shift" is "Maj" on French keyboards).
    /// </summary>
    public IReadOnlyList<string> Parts
    {
        get
        {
            if (IsNone)
                return [];
            var parts = ModifierParts(Modifiers).ToList();
            parts.Add(KeyName(Key) ?? $"0x{Key:X2}");
            return parts;
        }
    }

    /// <summary>The names of <paramref name="modifiers"/> in display order: ["Ctrl", "Shift"].</summary>
    public static IEnumerable<string> ModifierParts(HotkeyModifiers modifiers) =>
        ModifierNames.Where(m => modifiers.HasFlag(m.Modifier)).Select(m => m.Name);

    public override string ToString() => string.Join('+', Parts);

    /// <summary>Reads "Ctrl+Alt+F2"; an empty text is <see cref="None"/>. Case and spaces do not matter.</summary>
    public static bool TryParse(string? text, out Hotkey hotkey)
    {
        hotkey = None;
        if (string.IsNullOrWhiteSpace(text))
            return true;

        var modifiers = HotkeyModifiers.None;
        int key = 0;
        foreach (string raw in text.Split('+'))
        {
            string part = raw.Trim();
            var modifier = ModifierNames.FirstOrDefault(m => m.Name.Equals(part, StringComparison.OrdinalIgnoreCase)).Modifier;
            if (modifier != HotkeyModifiers.None)
            {
                modifiers |= modifier;
                continue;
            }
            if (key != 0 || FindKey(part) is not { } found)
                return false;
            key = found;
        }
        if (key == 0)
            return false;
        hotkey = new Hotkey(modifiers, key);
        return true;
    }

    /// <summary>
    /// Whether the shortcut can be global without getting in the way of typing: letters, digits, Space and the
    /// editing keys need Ctrl, Alt or Win (Shift alone gives capitals); Print Screen, Pause and F1–F24 can be alone.
    /// </summary>
    public bool IsAllowed
    {
        get
        {
            if (IsNone || KeyName(Key) is null)
                return false;
            bool standalone = Key is PrintScreen or 0x13 || Key is >= 0x70 and <= 0x87;
            return standalone || (Modifiers & ~HotkeyModifiers.Shift) != HotkeyModifiers.None;
        }
    }

    /// <summary>The shortcut read from <paramref name="text"/>, or <paramref name="fallback"/> when it cannot be read.</summary>
    public static Hotkey ParseOr(string? text, Hotkey fallback) => TryParse(text, out var hotkey) ? hotkey : fallback;

    private static int? FindKey(string name)
    {
        for (int key = 1; key < 0xFF; key++)
        {
            if (KeyName(key) is { } known && known.Equals(name, StringComparison.OrdinalIgnoreCase))
                return key;
        }
        return null;
    }
}
