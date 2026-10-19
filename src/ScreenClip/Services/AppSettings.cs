using System.Text.Json;
using System.Text.Json.Serialization;
using ScreenClip.Core;
using ScreenClip.Core.Composition;

namespace ScreenClip.Services;

/// <summary>Light or dark appearance; <see cref="System"/> follows Windows.</summary>
public enum AppTheme
{
    System,
    Light,
    Dark,
}

/// <summary>What a left click on the notification icon does (a right click always opens the menu).</summary>
public enum TrayClick
{
    Capture,
    Menu,
    Editor,
}

/// <summary>The screen where the thumbnail appears after a capture.</summary>
public enum ThumbnailScreen
{
    /// <summary>The screen the capture was made on.</summary>
    Capture,
    Primary,
    /// <summary>The screen under the pointer when the thumbnail appears.</summary>
    Pointer,
}

/// <summary>A capture shortcut of the settings, in the order of the Settings page and of the tray menu.</summary>
public enum ShortcutAction
{
    Area,
    Window,
    Screen,
    RepeatArea,
}

/// <summary>
/// User preferences, saved in %LOCALAPPDATA%\ScreenClip\settings.json. The app keeps one instance for its whole run
/// (<see cref="App.Settings"/>): the settings window changes it and saves it.
/// </summary>
public sealed class AppSettings
{
    /// <summary>Follow the Windows display language.</summary>
    public const string SystemLanguage = "system";

    /// <summary>UI languages with a translation (Properties/Strings*.resx). English is the default.</summary>
    public static readonly string[] SupportedLanguages = ["en", "fr"];

    /// <summary>Default shortcuts, validated by the author with the mock-up: everything on Print Screen.</summary>
    public static readonly IReadOnlyDictionary<ShortcutAction, Hotkey> DefaultShortcuts = new Dictionary<ShortcutAction, Hotkey>
    {
        [ShortcutAction.Area] = new(HotkeyModifiers.None, Hotkey.PrintScreen),
        [ShortcutAction.Window] = new(HotkeyModifiers.Alt, Hotkey.PrintScreen),
        [ShortcutAction.Screen] = new(HotkeyModifiers.Control, Hotkey.PrintScreen),
        [ShortcutAction.RepeatArea] = new(HotkeyModifiers.Shift, Hotkey.PrintScreen),
    };

    private static readonly string FilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ScreenClip", "settings.json");

    /// <summary><see cref="SystemLanguage"/> or one of <see cref="SupportedLanguages"/>.</summary>
    public string Language { get; set; } = SystemLanguage;

    [JsonConverter(typeof(JsonStringEnumConverter<AppTheme>))]
    public AppTheme Theme { get; set; } = AppTheme.System;

    /// <summary>
    /// A left click on the notification icon: an area capture by default (the app's main action, as Windows apps do
    /// with a left click), or the menu, or the editor (asked by the author: the choice in Settings).
    /// </summary>
    [JsonConverter(typeof(JsonStringEnumConverter<TrayClick>))]
    public TrayClick TrayClick { get; set; } = TrayClick.Capture;

    // ---------------------------------------------------------------- After a capture

    /// <summary>A thumbnail in the bottom left corner of the screen: copy, save, drag to an app.</summary>
    public bool ShowThumbnail { get; set; } = true;

    /// <summary>Which screen shows it (asked by the author: the capture's one, or always the same).</summary>
    [JsonConverter(typeof(JsonStringEnumConverter<ThumbnailScreen>))]
    public ThumbnailScreen ThumbnailScreen { get; set; } = ThumbnailScreen.Capture;

    /// <summary>The capture goes to the clipboard at once.</summary>
    public bool CopyToClipboard { get; set; } = true;

    /// <summary>The capture is saved in <see cref="SaveFolder"/> at once.</summary>
    public bool SaveAutomatically { get; set; }

    // ---------------------------------------------------------------- Shortcuts

    /// <summary>
    /// The global shortcuts are on. Off, ScreenClip takes no key at all; captures stay possible from its notification
    /// menu (asked by the author).
    /// </summary>
    public bool ShortcutsEnabled { get; set; } = true;

    /// <summary>The shortcuts as text ("Alt+PrintScreen", "" for none); missing ones take the default.</summary>
    public Dictionary<ShortcutAction, string> Shortcuts { get; set; } = [];

    /// <summary>Asked once whether ScreenClip may take the Print Screen key from Windows.</summary>
    public bool PrintScreenAsked { get; set; }

    // ---------------------------------------------------------------- Saving

    /// <summary>
    /// Folder of the captures (saved automatically, or with Save on the thumbnail); null: Windows' Screenshots folder,
    /// where people look for their screenshots (asked by the author).
    /// </summary>
    public string? SaveFolder { get; set; }

    /// <summary>Folder where the editor saves the pictures laid out; null: Pictures\ScreenClip, apart from the captures.</summary>
    public string? ExportFolder { get; set; }

    /// <summary>Name of the saved captures, with {date} and {time}; null: the default of the UI language.</summary>
    public string? FileNameTemplate { get; set; }

    // ---------------------------------------------------------------- Editor

    /// <summary>The layout of the editor, as last used: the next capture opens with it.</summary>
    public ClipStyle EditorStyle { get; set; } = StylePresets.Default;

    /// <summary>The styles the user saved under a name, in the order they were made.</summary>
    public List<UserPreset> UserPresets { get; set; } = [];

    // ---------------------------------------------------------------- Other

    /// <summary>The last area selected, for “Capture the last area again”.</summary>
    public string? LastArea { get; set; }

    /// <summary>
    /// Set once the “Start with Windows” default (on) has been applied, so that turning it off in Settings stays off.
    /// </summary>
    public bool StartupConfigured { get; set; }

    /// <summary>Ask GitHub for a newer version when the app starts.</summary>
    public bool CheckForUpdatesAtStartup { get; set; } = true;

    /// <summary>Releases offered as updates: stable only, or betas too.</summary>
    [JsonConverter(typeof(JsonStringEnumConverter<UpdateChannel>))]
    public UpdateChannel UpdateChannel { get; set; } = UpdateChannel.Stable;

    // Settings of a newer version (a dev build, a beta) are written back as they are, not dropped.
    [JsonExtensionData]
    public Dictionary<string, JsonElement>? OtherSettings { get; set; }

    [JsonIgnore]
    public string EffectiveSaveFolder => string.IsNullOrWhiteSpace(SaveFolder) ? DefaultSaveFolder : SaveFolder;

    public static string DefaultSaveFolder => WindowsSettings.ScreenshotsFolder;

    [JsonIgnore]
    public string EffectiveExportFolder => string.IsNullOrWhiteSpace(ExportFolder) ? DefaultExportFolder : ExportFolder;

    public static string DefaultExportFolder =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), "ScreenClip");

    [JsonIgnore]
    public string EffectiveFileNameTemplate =>
        string.IsNullOrWhiteSpace(FileNameTemplate) ? Strings.Get("DefaultFileName") : FileNameTemplate;

    public Hotkey GetShortcut(ShortcutAction action) =>
        Shortcuts.TryGetValue(action, out string? text) ? Hotkey.ParseOr(text, DefaultShortcuts[action]) : DefaultShortcuts[action];

    public void SetShortcut(ShortcutAction action, Hotkey hotkey) => Shortcuts[action] = hotkey.ToString();

    // A missing or damaged file just means default settings: preferences are never worth an error message.
    public static AppSettings Load()
    {
        try
        {
            if (File.Exists(FilePath) && JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(FilePath)) is { } settings)
            {
                if (!SupportedLanguages.Contains(settings.Language))
                    settings.Language = SystemLanguage;
                settings.Shortcuts ??= [];
                settings.EditorStyle = (settings.EditorStyle ?? StylePresets.Default).Normalized();
                settings.UserPresets = [.. (settings.UserPresets ?? [])
                    .Where(p => p is not null && StylePresets.CleanName(p.Name) is not null)
                    .Select(p => p with { Name = StylePresets.CleanName(p.Name)!, Style = (p.Style ?? StylePresets.Default).Normalized() })];
                return settings;
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or NotSupportedException)
        {
        }
        return new AppSettings();
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }
}
