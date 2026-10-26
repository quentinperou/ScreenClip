using System.Globalization;
using System.Windows;
using System.Windows.Threading;
using ScreenClip.Services;
using ScreenClip.Video;

namespace ScreenClip;

public partial class App : Application
{
    /// <summary>Started with Windows (Run key): nothing to show, the app just waits in the notification area.</summary>
    public const string BackgroundArgument = "--background";

    // Windows display language, kept to come back to it when the user picks "System language".
    private static readonly CultureInfo SystemUICulture = CultureInfo.CurrentUICulture;

    private static bool s_themeApplied;
    private SingleInstance? _instance;

    /// <summary>The settings, loaded once at startup; the settings window changes and saves this instance.</summary>
    public static AppSettings Settings { get; private set; } = new();

    internal static TrayHost Tray { get; private set; } = null!;

    internal static CaptureController Capture { get; private set; } = null!;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        _instance = SingleInstance.TryStart(e.Args, args => Dispatcher.BeginInvoke(() => OpenEditor(PictureArgument(args))));
        if (_instance is null)
        {
            Shutdown(); // already running: it has been told to open its editor
            return;
        }

        Settings = AppSettings.Load();

        // UI language: the one chosen in the settings, else the Windows one (French, otherwise English).
        // SCREENCLIP_LANG=en|fr forces one, handy to test both.
        if (Environment.GetEnvironmentVariable("SCREENCLIP_LANG") is { Length: > 0 } forced)
            ApplyLanguage(forced);
        else
            ApplyLanguage(Settings.Language);

        DevLog.Enabled = Settings.DeveloperMode;
        DispatcherUnhandledException += (_, args) =>
        {
            DevLog.Write($"error: {args.Exception}");
            MessageBox.Show(args.Exception.Message, "ScreenClip", MessageBoxButton.OK, MessageBoxImage.Error);
            args.Handled = true;
        };

        ClipActions.CleanTempFolder();
        Capture = new CaptureController();
        Tray = new TrayHost();
        Tray.IconClicked += (_, _) => IconClicked();
        Tray.MenuRequested += (_, _) => TrayMenu.Show();
        Tray.ShortcutPressed += (_, action) => Capture.Run(action);
        var refused = Tray.RegisterShortcuts(Settings);
        DevLog.Write($"ScreenClip {Updater.CurrentVersion} started, Windows {Environment.OSVersion.Version}, "
            + $"screens {string.Join(" ", ScreenGrabber.Monitors().Select(m => m.Bounds + (m.IsPrimary ? " (primary)" : "")))}"
            + (refused.Count > 0 ? $", shortcuts refused: {string.Join(", ", refused)}" : ""));

        // Start with Windows: on by default for the installed app, once; the user can turn it off in Settings.
        if (!Settings.StartupConfigured && WindowsSettings.IsInstalled)
        {
            WindowsSettings.StartWithWindows = true;
            Settings.StartupConfigured = true;
            Settings.Save();
        }

        // Started by hand (or with a picture): the editor. Started with Windows: nothing, the app waits.
        bool background = e.Args.Contains(BackgroundArgument);
        string? picture = PictureArgument(e.Args);
        Action? then = background ? null : () => OpenEditor(picture);
        if (PrintScreenWindow.ShouldAsk(Settings))
            PrintScreenWindow.Ask(then);
        else
            then?.Invoke();

        _ = Updater.CheckAtStartupAsync();
        MemoryTrim.Soon();
#if DEBUG
        if (double.TryParse(Environment.GetEnvironmentVariable("SCREENCLIP_RECORD_TEST"), NumberStyles.Float, CultureInfo.InvariantCulture, out double seconds))
            Dispatcher.BeginInvoke(() => RecordingTest.Run(seconds), DispatcherPriority.ApplicationIdle);
#endif
    }

    protected override void OnExit(ExitEventArgs e)
    {
        Tray?.Dispose();
        _instance?.Dispose();
        base.OnExit(e);
    }

    /// <summary>
    /// Loads the Fluent theme (light/dark/system) the first time a window or the menu opens: until then the app,
    /// waiting in the notification area, does not hold the theme's resources. SCREENCLIP_THEME=Light|Dark forces one.
    /// </summary>
    public static void EnsureTheme()
    {
        if (s_themeApplied)
            return;
        s_themeApplied = true;
        ApplyTheme(Environment.GetEnvironmentVariable("SCREENCLIP_THEME") switch
        {
            "Light" => AppTheme.Light,
            "Dark" => AppTheme.Dark,
            _ => Settings.Theme,
        });
    }

    /// <summary>Sets the theme of every window, at once: all colors come from the Fluent theme through DynamicResource.</summary>
    public static void ApplyTheme(AppTheme theme) =>
        Current.ThemeMode = theme switch
        {
            AppTheme.Light => ThemeMode.Light,
            AppTheme.Dark => ThemeMode.Dark,
            _ => ThemeMode.System,
        };

    /// <summary>Sets the UI language: <see cref="AppSettings.SystemLanguage"/> or a culture name ("en", "fr").</summary>
    public static void ApplyLanguage(string language)
    {
        var culture = language == AppSettings.SystemLanguage ? SystemUICulture : CultureInfo.GetCultureInfo(language);
        CultureInfo.DefaultThreadCurrentUICulture = culture;
        CultureInfo.CurrentUICulture = culture;
    }

    /// <summary>
    /// Opens the settings, or brings them to the front. Texts are read once, when a window is built: after a language
    /// change, the window is built again on the same page.
    /// </summary>
    public static void OpenSettings(SettingsPage? page = null)
    {
        if (Current.Windows.OfType<SettingsWindow>().FirstOrDefault() is { } open)
        {
            if (page is { } requested)
                open.ShowPage(requested);
            if (open.WindowState == WindowState.Minimized)
                open.WindowState = WindowState.Normal;
            open.Activate();
            return;
        }

        EnsureTheme();
        var window = new SettingsWindow(page ?? SettingsPage.General);
        window.Closed += (_, _) =>
        {
            if (window.LanguageChanged is { } reopenOn)
            {
                ApplyLanguage(Settings.Language);
                Current.Dispatcher.BeginInvoke(() => OpenSettings(reopenOn), DispatcherPriority.ApplicationIdle);
            }
            else
                MemoryTrim.Soon();
        };
        window.Show();
        window.Activate();
    }

    /// <summary>A picture file given on the command line (“Open with ScreenClip”), if any.</summary>
    private static string? PictureArgument(string[] args) =>
        args.FirstOrDefault(a => !a.StartsWith("--", StringComparison.Ordinal) && Imaging.IsPictureFile(a) && File.Exists(a));

    /// <summary>
    /// Opens the editor, or brings it to the front, with <paramref name="picturePath"/> or <paramref name="clip"/> in it
    /// when given (otherwise it keeps what it shows, or offers to import a picture).
    /// </summary>
    // A left click on the notification icon: as Settings › General says. The capture waits a moment, so that a
    // flyout of hidden icons the click came from is gone from the screen.
    private static void IconClicked()
    {
        // While a video is recorded, a click on the icon stops it.
        if (Video.RecordingSession.Current is { } recording)
        {
            recording.Stop();
            return;
        }
        switch (Settings.TrayClick)
        {
            case TrayClick.Menu:
                TrayMenu.Show();
                break;
            case TrayClick.Editor:
                OpenEditor();
                break;
            default:
                Capture.RunAfterMenu(ShortcutAction.Area);
                break;
        }
    }

    // The editor last in front: “Open ScreenClip” brings it back.
    private static EditorWindow? _lastEditor;

    /// <summary>
    /// Opens a capture, a video or a picture in an editor, or with nothing to open brings the last editor to the front.
    /// What an editor holds is never replaced (asked by the author: a capture made while editing lost the work): an
    /// editor with nothing in it takes it, else a new one opens beside the others.
    /// </summary>
    internal static void OpenEditor(string? picturePath = null, Clip? clip = null, VideoClip? video = null, bool import = false)
    {
        EnsureTheme();
        bool opening = clip is not null || video is not null || picturePath is not null || import;
        var editor = opening ? EmptyEditor() : LatestEditor() ?? NewEditor();
        ShowHiddenEditors(except: editor);
        if (clip is not null)
        {
            editor.Load(clip.Image, clip.TakenAt);
            // Saved automatically: where it went, to open it or its folder.
            if (clip.SavedPath is { } saved)
                editor.ShowSaved(saved, capture: true);
        }
        else if (video is not null)
        {
            editor.LoadVideo(video);
            if (video.SavedPath is { } saved)
                editor.ShowSaved(saved, capture: true);
        }
        else if (picturePath is not null)
            editor.Open(picturePath);
        editor.Show();
        if (editor.WindowState == WindowState.Minimized)
            editor.WindowState = WindowState.Normal;
        editor.Activate();
        // “Lay out a picture…” in the menu: the file to open is asked once the menu is gone.
        if (import)
            editor.Dispatcher.BeginInvoke(editor.Import);
    }

    /// <summary>
    /// What ScreenClip hides from captures (the thumbnail, the recording's bar and line) stays visible: developer mode,
    /// or SCREENCLIP_CAPTURABLE=1 (README pictures, scripts).
    /// </summary>
    internal static bool CapturableWindows =>
        Settings.DeveloperMode || Environment.GetEnvironmentVariable("SCREENCLIP_CAPTURABLE") == "1";

    internal static void SetDeveloperMode(bool on)
    {
        if (!on)
            DevLog.Write("developer mode off");
        Settings.DeveloperMode = on;
        Settings.Save();
        DevLog.Enabled = on;
        if (on)
            DevLog.Write($"developer mode on, ScreenClip {Updater.CurrentVersion}, Windows {Environment.OSVersion.Version}");
    }

    private static EditorWindow? LatestEditor() =>
        _lastEditor ?? Current.Windows.OfType<EditorWindow>().LastOrDefault();

    // An editor with nothing in it (the one hidden for this capture first), else a new one.
    private static EditorWindow EmptyEditor() =>
        Current.Windows.OfType<EditorWindow>().Where(e => !e.HasContent)
            .OrderBy(e => e.IsVisible).ThenBy(e => e != _lastEditor).FirstOrDefault() ?? NewEditor();

    private static EditorWindow NewEditor()
    {
        var editor = new EditorWindow();
        // A little below and right of the editor in front, where no other one is (centered when there is none).
        if (LatestEditor() is { IsVisible: true, WindowState: WindowState.Normal } last)
        {
            var others = Current.Windows.OfType<EditorWindow>().Where(e => e != editor && e.IsVisible).ToList();
            double left = last.Left + 32, top = last.Top + 32;
            while (others.Any(e => Math.Abs(e.Left - left) < 4 && Math.Abs(e.Top - top) < 4))
            {
                left += 32;
                top += 32;
            }
            if (left + editor.Width <= SystemParameters.VirtualScreenLeft + SystemParameters.VirtualScreenWidth
                && top + editor.Height <= SystemParameters.VirtualScreenTop + SystemParameters.VirtualScreenHeight)
            {
                editor.WindowStartupLocation = WindowStartupLocation.Manual;
                editor.Left = left;
                editor.Top = top;
            }
        }
        editor.Activated += (_, _) => _lastEditor = editor;
        editor.Closed += (_, _) =>
        {
            if (_lastEditor == editor)
                _lastEditor = null;
            MemoryTrim.Soon();
        };
        _lastEditor ??= editor;
        return editor;
    }

    /// <summary>The editors hidden for a capture or a recording come back (it was cancelled, or opens elsewhere).</summary>
    internal static void ShowHiddenEditors(EditorWindow? except = null)
    {
        foreach (var editor in Current.Windows.OfType<EditorWindow>().ToList())
        {
            if (editor != except && !editor.IsVisible)
                editor.Show();
        }
    }

    /// <summary>ScreenClip is quitting: the editor closes without asking about a video not saved.</summary>
    internal static bool Quitting { get; private set; }

    /// <summary>Quits ScreenClip: the notification icon and the shortcuts go with it.</summary>
    public static void Quit()
    {
        Quitting = true;
        Current.Shutdown();
    }
}
