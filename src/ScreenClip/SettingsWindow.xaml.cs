using System.Diagnostics;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Navigation;
using Microsoft.Win32;
using ScreenClip.Core;
using ScreenClip.Core.Composition;
using ScreenClip.Services;

namespace ScreenClip;

/// <summary>The pages of the settings window, in the order of its menu.</summary>
public enum SettingsPage
{
    General,
    Capture,
    Saving,
    Updates,
    About,
}

/// <summary>
/// The settings, and the only window of the app that stays open: opened from the notification menu, or by starting
/// ScreenClip again. Changes apply and are saved at once (no OK button, like Windows Settings).
/// </summary>
public partial class SettingsWindow : Window
{
    private readonly AppSettings _settings = App.Settings;
    private readonly bool _initialized;
    private bool _updating; // an update check or download is running

    public SettingsWindow(SettingsPage page = SettingsPage.General)
    {
        InitializeComponent();
        MicaBackdrop.Apply(this);
        ShowPage(page);

        StartWithWindowsBox.IsChecked = WindowsSettings.StartWithWindows;
        LanguageBox.Items.Add(new Option<string>(AppSettings.SystemLanguage, Strings.Get("LanguageSystem")));
        foreach (string code in AppSettings.SupportedLanguages)
            LanguageBox.Items.Add(new Option<string>(code, NativeName(code)));
        LanguageBox.SelectedItem = LanguageBox.Items.Cast<Option<string>>().First(o => o.Value == _settings.Language);
        ThemeBox.Items.Add(new Option<AppTheme>(AppTheme.System, Strings.Get("ThemeSystem")));
        ThemeBox.Items.Add(new Option<AppTheme>(AppTheme.Light, Strings.Get("ThemeLight")));
        ThemeBox.Items.Add(new Option<AppTheme>(AppTheme.Dark, Strings.Get("ThemeDark")));
        ThemeBox.SelectedItem = ThemeBox.Items.Cast<Option<AppTheme>>().First(o => o.Value == _settings.Theme);
        TrayClickBox.Items.Add(new Option<TrayClick>(TrayClick.Capture, Strings.Get("CaptureArea")));
        TrayClickBox.Items.Add(new Option<TrayClick>(TrayClick.Menu, Strings.Get("TrayClickMenu")));
        TrayClickBox.Items.Add(new Option<TrayClick>(TrayClick.Editor, Strings.Get("TrayClickEditor")));
        TrayClickBox.SelectedItem = TrayClickBox.Items.Cast<Option<TrayClick>>().First(o => o.Value == _settings.TrayClick);
        CopyFormatBox.Items.Add(new Option<ExportFormat>(ExportFormat.Png, "PNG"));
        CopyFormatBox.Items.Add(new Option<ExportFormat>(ExportFormat.Jpeg, "JPG"));
        CopyFormatBox.Items.Add(new Option<ExportFormat>(ExportFormat.Webp, "WebP"));
        CopyFormatBox.SelectedItem = CopyFormatBox.Items.Cast<Option<ExportFormat>>().FirstOrDefault(o => o.Value == _settings.CopyFormat)
            ?? CopyFormatBox.Items[0];

        OpenEditorBox.IsChecked = _settings.OpenEditorAfterCapture;
        ShowThumbnailBox.IsChecked = _settings.ShowThumbnail;
        ThumbnailScreenBox.Items.Add(new Option<ThumbnailScreen>(ThumbnailScreen.Capture, Strings.Get("ThumbnailScreenCapture")));
        ThumbnailScreenBox.Items.Add(new Option<ThumbnailScreen>(ThumbnailScreen.Primary, Strings.Get("ThumbnailScreenPrimary")));
        ThumbnailScreenBox.Items.Add(new Option<ThumbnailScreen>(ThumbnailScreen.Pointer, Strings.Get("ThumbnailScreenPointer")));
        ThumbnailScreenBox.SelectedItem = ThumbnailScreenBox.Items.Cast<Option<ThumbnailScreen>>().First(o => o.Value == _settings.ThumbnailScreen);
        ShowThumbnailRows();
        CopyBox.IsChecked = _settings.CopyToClipboard;
        SaveBox.IsChecked = _settings.SaveAutomatically;
        ShortcutsEnabledBox.IsChecked = _settings.ShortcutsEnabled;
        CountdownBox.IsChecked = _settings.RecordCountdown;
        FillShortcuts();
        ShowPrintScreenState();
        // Back from Windows Settings (Print Screen switch): show what changed.
        Activated += (_, _) =>
        {
            if (_initialized)
                ShowPrintScreenState();
        };

        ShowSaveFolder();
        FileNameBox.Text = _settings.EffectiveFileNameTemplate;

        ChannelBox.Items.Add(new Option<UpdateChannel>(UpdateChannel.Stable, Strings.Get("UpdateChannelStable")));
        ChannelBox.Items.Add(new Option<UpdateChannel>(UpdateChannel.Beta, Strings.Get("UpdateChannelBeta")));
        ChannelBox.SelectedItem = ChannelBox.Items.Cast<Option<UpdateChannel>>().First(o => o.Value == _settings.UpdateChannel);
        CheckAtStartupBox.IsChecked = _settings.CheckForUpdatesAtStartup;
        ShowUpdateState();
        Updater.Checked += Updater_Checked;
        Closed += (_, _) => Updater.Checked -= Updater_Checked;

        VersionText.Text = Strings.Format("AboutVersion", Updater.CurrentVersion);
        DeveloperModeBox.IsChecked = _settings.DeveloperMode;
        OpenLogButton.IsEnabled = _settings.DeveloperMode;
        _initialized = true;
    }

    /// <summary>Set when the user picked another language: the window is built again, on this page.</summary>
    public SettingsPage? LanguageChanged { get; private set; }

    public void ShowPage(SettingsPage page) => PageList.SelectedIndex = (int)page;

    private void PageList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        ScrollViewer[] pages = [GeneralPage, CapturePage, SavingPage, UpdatesPage, AboutPage];
        for (int i = 0; i < pages.Length; i++)
            pages[i].Visibility = i == PageList.SelectedIndex ? Visibility.Visible : Visibility.Collapsed;
        if (PageList.SelectedItem is ListBoxItem item)
            PageTitle.Text = System.Windows.Automation.AutomationProperties.GetName(item);
    }

    // ---------------------------------------------------------------- General

    // Languages are listed in their own language ("English", "Français"), whatever the current UI language.
    private static string NativeName(string code)
    {
        var culture = CultureInfo.GetCultureInfo(code);
        return culture.TextInfo.ToUpper(culture.NativeName[0]) + culture.NativeName[1..];
    }

    private void StartWithWindows_Changed(object sender, RoutedEventArgs e)
    {
        if (_initialized)
            WindowsSettings.StartWithWindows = StartWithWindowsBox.IsChecked == true;
    }

    private void LanguageBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_initialized || LanguageBox.SelectedItem is not Option<string> option || option.Value == _settings.Language)
            return;
        _settings.Language = option.Value;
        _settings.Save();
        // Texts are read when a window is built: this one is built again in the new language, on this page.
        LanguageChanged = (SettingsPage)PageList.SelectedIndex;
        Close();
    }

    // Unlike the language, the theme switches live: no window needs to be rebuilt.
    private void ThemeBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_initialized || ThemeBox.SelectedItem is not Option<AppTheme> option || option.Value == _settings.Theme)
            return;
        _settings.Theme = option.Value;
        _settings.Save();
        App.ApplyTheme(option.Value);
    }

    private void TrayClickBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_initialized || TrayClickBox.SelectedItem is not Option<TrayClick> option)
            return;
        _settings.TrayClick = option.Value;
        _settings.Save();
    }

    private void CopyFormatBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_initialized || CopyFormatBox.SelectedItem is not Option<ExportFormat> option)
            return;
        _settings.CopyFormat = option.Value;
        _settings.Save();
        foreach (var editor in Application.Current.Windows.OfType<EditorWindow>())
            editor.CopyFormatChanged();
    }

    // ---------------------------------------------------------------- Capture

    private void AfterCapture_Changed(object sender, RoutedEventArgs e)
    {
        if (!_initialized)
            return;
        _settings.OpenEditorAfterCapture = OpenEditorBox.IsChecked == true;
        _settings.ShowThumbnail = ShowThumbnailBox.IsChecked == true;
        _settings.CopyToClipboard = CopyBox.IsChecked == true;
        _settings.SaveAutomatically = SaveBox.IsChecked == true;
        _settings.Save();
        ShowThumbnailRows();
    }

    // The thumbnail is shown only when the editor does not open: its rows are greyed while it does.
    private void ShowThumbnailRows()
    {
        bool thumbnail = !_settings.OpenEditorAfterCapture;
        ThumbnailRow.IsEnabled = thumbnail;
        ThumbnailRow.Opacity = thumbnail ? 1 : 0.5;
        ThumbnailScreenRow.IsEnabled = thumbnail && _settings.ShowThumbnail;
        ThumbnailScreenRow.Opacity = ThumbnailScreenRow.IsEnabled ? 1 : 0.5;
    }

    private void ThumbnailScreenBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_initialized || ThumbnailScreenBox.SelectedItem is not Option<ThumbnailScreen> option)
            return;
        _settings.ThumbnailScreen = option.Value;
        _settings.Save();
    }

    private void Countdown_Changed(object sender, RoutedEventArgs e)
    {
        if (!_initialized)
            return;
        _settings.RecordCountdown = CountdownBox.IsChecked == true;
        _settings.Save();
    }

    private void ShortcutsEnabled_Changed(object sender, RoutedEventArgs e)
    {
        if (!_initialized)
            return;
        _settings.ShortcutsEnabled = ShortcutsEnabledBox.IsChecked == true;
        _settings.Save();
        App.Tray.RegisterShortcuts(_settings);
        FillShortcuts();
        ShowPrintScreenState();
    }

    /// <summary>One row per action: its name, its keys, “Change”, and why Windows refused it if it did.</summary>
    private void FillShortcuts()
    {
        // Off: the rows stay visible (what comes back when turned on) but greyed.
        ShortcutCard.IsEnabled = _settings.ShortcutsEnabled;
        ShortcutCard.Opacity = _settings.ShortcutsEnabled ? 1 : 0.5;
        ShortcutRows.Children.Clear();
        foreach (var action in Enum.GetValues<ShortcutAction>())
        {
            if (ShortcutRows.Children.Count > 0)
                ShortcutRows.Children.Add(new System.Windows.Shapes.Rectangle { Style = (Style)FindResource("CardDivider") });

            var row = new DockPanel { Margin = new Thickness(16, 12, 16, 12) };
            var icon = new System.Windows.Shapes.Path
            {
                Style = (Style)FindResource("IconPath"),
                Data = (System.Windows.Media.Geometry)FindResource(action switch
                {
                    ShortcutAction.Window => "IconWindow",
                    ShortcutAction.Screen => "IconScreen",
                    ShortcutAction.RepeatArea => "IconRepeat",
                    ShortcutAction.AllScreens => "IconAllScreens",
                    ShortcutAction.Video => "IconVideo",
                    _ => "IconArea",
                }),
                Margin = new Thickness(2, 0, 18, 0),
            };
            DockPanel.SetDock(icon, Dock.Left);
            row.Children.Add(icon);

            var change = new Button { Content = Strings.Get("ShortcutChange"), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(16, 0, 0, 0) };
            change.Click += (_, _) => ChangeShortcut(action);
            DockPanel.SetDock(change, Dock.Right);
            row.Children.Add(change);

            var hotkey = _settings.GetShortcut(action);
            var keys = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            if (hotkey.IsNone)
                keys.Children.Add(new TextBlock { Text = Strings.Get("ShortcutNoneSet"), Style = (Style)FindResource("CardDescription") });
            foreach (string key in ShortcutText.Keys(hotkey))
                keys.Children.Add(new Border { Style = (Style)FindResource("Keycap"), Child = new TextBlock { Text = key, FontSize = 12 } });
            DockPanel.SetDock(keys, Dock.Right);
            row.Children.Add(keys);

            var label = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            label.Children.Add(new TextBlock { Text = Strings.Get("Shortcut" + action) });
            if (App.Tray.Refused.Contains(action))
            {
                label.Children.Add(new TextBlock
                {
                    Text = Strings.Get(hotkey.Key == Hotkey.PrintScreen && WindowsSettings.PrintScreenUsedByWindows
                        ? "ShortcutRefusedPrintScreen" : "ShortcutRefused"),
                    Style = (Style)FindResource("CardDescription"),
                    Foreground = (System.Windows.Media.Brush)FindResource("SystemFillColorCautionBrush"),
                });
            }
            row.Children.Add(label);
            ShortcutRows.Children.Add(row);
        }
    }

    private void ChangeShortcut(ShortcutAction action)
    {
        // The current shortcuts must not fire while the new one is typed.
        App.Tray.UnregisterShortcuts();
        var dialog = new ShortcutWindow(action) { Owner = this };
        if (dialog.ShowDialog() == true && dialog.Result is { } hotkey)
        {
            _settings.SetShortcut(action, hotkey);
            _settings.Save();
        }
        App.Tray.RegisterShortcuts(_settings);
        FillShortcuts();
        ShowPrintScreenState();
    }

    /// <summary>
    /// Who has the Print Screen key: Windows' Snipping Tool (offer to take it), or the apps (offer to give it back).
    /// When Windows' setting is off but the key is still refused, Windows lets it go at the next sign-in.
    /// </summary>
    private void ShowPrintScreenState()
    {
        // Without shortcuts, who holds Print Screen does not matter to ScreenClip.
        PrintScreenCard.Visibility = _settings.ShortcutsEnabled ? Visibility.Visible : Visibility.Collapsed;
        bool windowsKeepsIt = WindowsSettings.PrintScreenUsedByWindows;
        bool needed = PrintScreenKey.IsNeededBy(Enum.GetValues<ShortcutAction>().Select(_settings.GetShortcut));
        bool refused = App.Tray.Refused.Any(a => _settings.GetShortcut(a).Key == Hotkey.PrintScreen);

        PrintScreenStatus.Text = Strings.Get(windowsKeepsIt
            ? (needed ? "PrintScreenStatusWindowsNeeded" : "PrintScreenStatusWindows")
            : refused ? "PrintScreenStatusAfterSignIn" : "PrintScreenStatusApps");
        PrintScreenButton.Content = Strings.Get(windowsKeepsIt ? "PrintScreenTakeOver" : "PrintScreenRestore");
        if (windowsKeepsIt)
            PrintScreenButton.SetResourceReference(StyleProperty, "AccentButtonStyle");
        else
            PrintScreenButton.ClearValue(StyleProperty);
        PrintScreenCard.SetResourceReference(StyleProperty, windowsKeepsIt && needed || refused ? "WarningCard" : "Card");
    }

    private void PrintScreenButton_Click(object sender, RoutedEventArgs e)
    {
        if (WindowsSettings.PrintScreenUsedByWindows)
            PrintScreenWindow.TakeOver();
        else
        {
            // Back to Windows' default: Print Screen opens the Snipping Tool again.
            WindowsSettings.SetPrintScreenUsedByWindows(true);
            App.Tray.RegisterShortcuts(_settings);
        }
        _settings.PrintScreenAsked = true;
        _settings.Save();
        FillShortcuts();
        ShowPrintScreenState();
    }

    private void WindowsKeyboardSettings_Click(object sender, RoutedEventArgs e) => WindowsSettings.OpenKeyboardSettings();

    // ---------------------------------------------------------------- Saving

    private void ShowSaveFolder()
    {
        SaveFolderText.Text = _settings.EffectiveSaveFolder;
        SaveFolderText.ToolTip = _settings.EffectiveSaveFolder;
        ResetFolderLink.Visibility = string.IsNullOrWhiteSpace(_settings.SaveFolder) ? Visibility.Collapsed : Visibility.Visible;
        SaveAutomaticallyDescription.Text = Strings.Format("SettingsSaveAutomaticallyDescription", _settings.EffectiveSaveFolder);
        ExportFolderText.Text = _settings.EffectiveExportFolder;
        ExportFolderText.ToolTip = _settings.EffectiveExportFolder;
        ResetExportFolderLink.Visibility = string.IsNullOrWhiteSpace(_settings.ExportFolder) ? Visibility.Collapsed : Visibility.Visible;
        VideoFolderText.Text = _settings.EffectiveVideoFolder;
        VideoFolderText.ToolTip = _settings.EffectiveVideoFolder;
        ResetVideoFolderLink.Visibility = string.IsNullOrWhiteSpace(_settings.VideoFolder) ? Visibility.Collapsed : Visibility.Visible;
    }

    private void BrowseVideoFolder_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog { Title = Strings.Get("SettingsVideoFolder"), InitialDirectory = _settings.EffectiveVideoFolder };
        if (dialog.ShowDialog(this) != true)
            return;
        _settings.VideoFolder = dialog.FolderName;
        _settings.Save();
        ShowSaveFolder();
    }

    private void OpenVideoFolder_Click(object sender, RoutedEventArgs e) => ClipActions.OpenFolder(_settings.EffectiveVideoFolder);

    private void ResetVideoFolder_Click(object sender, RoutedEventArgs e)
    {
        _settings.VideoFolder = null;
        _settings.Save();
        ShowSaveFolder();
    }

    private void BrowseExportFolder_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog { Title = Strings.Get("SettingsExportFolder"), InitialDirectory = _settings.EffectiveExportFolder };
        if (dialog.ShowDialog(this) != true)
            return;
        _settings.ExportFolder = dialog.FolderName;
        _settings.Save();
        ShowSaveFolder();
    }

    private void OpenExportFolder_Click(object sender, RoutedEventArgs e) => ClipActions.OpenFolder(_settings.EffectiveExportFolder);

    private void ResetExportFolder_Click(object sender, RoutedEventArgs e)
    {
        _settings.ExportFolder = null;
        _settings.Save();
        ShowSaveFolder();
    }

    private void BrowseFolder_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog { Title = Strings.Get("SettingsSaveFolder"), InitialDirectory = _settings.EffectiveSaveFolder };
        if (dialog.ShowDialog(this) != true)
            return;
        _settings.SaveFolder = dialog.FolderName;
        _settings.Save();
        ShowSaveFolder();
    }

    private void OpenFolder_Click(object sender, RoutedEventArgs e) => ClipActions.OpenFolder(_settings.EffectiveSaveFolder);

    private void ResetFolder_Click(object sender, RoutedEventArgs e)
    {
        _settings.SaveFolder = null;
        _settings.Save();
        ShowSaveFolder();
    }

    private void FileNameBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        string template = FileNameBox.Text;
        FileNameExample.Text = Strings.Format("SettingsFileNameExample", FileNaming.Expand(
            string.IsNullOrWhiteSpace(template) ? Strings.Get("DefaultFileName") : template, DateTime.Now) + ".png");
        if (!_initialized)
            return;
        // The default of the language is not written down: it follows a language change.
        _settings.FileNameTemplate = string.IsNullOrWhiteSpace(template) || template == Strings.Get("DefaultFileName") ? null : template;
        _settings.Save();
    }

    // ---------------------------------------------------------------- Updates

    private void Updater_Checked(object? sender, EventArgs e) => ShowUpdateState();

    /// <summary>Shows what the last check found, or <paramref name="status"/> (progress, error) instead.</summary>
    private void ShowUpdateState(string? status = null)
    {
        var available = Updater.Available;
        UpdateStatusText.Text = status
            ?? (available is not null ? Strings.Format("UpdateStatusAvailable", Updater.CurrentVersion, available.Version)
                : Updater.HasChecked ? Strings.Format("UpdateStatusUpToDate", Updater.CurrentVersion)
                : Strings.Format("UpdateStatusIdle", Updater.CurrentVersion));
        UpdateNotesLink.Visibility = available is not null && status is null ? Visibility.Visible : Visibility.Collapsed;

        UpdateButton.Content = available is null ? Strings.Get("UpdateCheck") : Strings.Get("UpdateInstall");
        if (available is null)
            UpdateButton.ClearValue(StyleProperty);
        else
            UpdateButton.SetResourceReference(StyleProperty, "AccentButtonStyle");
        UpdateButton.IsEnabled = !_updating;
    }

    private async void UpdateButton_Click(object sender, RoutedEventArgs e)
    {
        if (Updater.Available is { } release)
            await InstallUpdateAsync(release);
        else
            await CheckForUpdatesAsync();
    }

    private async Task CheckForUpdatesAsync()
    {
        _updating = true;
        ShowUpdateState(Strings.Get("UpdateStatusChecking"));
        string? error = null;
        try
        {
            await Updater.CheckAsync(_settings.UpdateChannel);
        }
        catch (Exception ex)
        {
            error = Strings.Format("UpdateStatusFailed", Updater.CurrentVersion, Updater.Describe(ex));
        }
        _updating = false;
        ShowUpdateState(error);
    }

    private async Task InstallUpdateAsync(ReleaseInfo release)
    {
        _updating = true;
        ShowUpdateState(Strings.Get("UpdateDownloading"));
        UpdateProgress.Value = 0;
        UpdateProgress.IsIndeterminate = false;
        UpdateProgress.Visibility = Visibility.Visible;
        var progress = new Progress<double>(p =>
        {
            UpdateProgress.Value = p;
            if (p < 1)
                return;
            // Downloaded: the installer now waits for the UAC prompt, then installs.
            UpdateProgress.IsIndeterminate = true;
            UpdateStatusText.Text = Strings.Get("UpdateStarting");
        });

        string status;
        try
        {
            if (await Updater.InstallAsync(release, progress))
                return; // the app is closing, the installer takes over
            status = Strings.Get("UpdateCancelled");
        }
        catch (Exception ex)
        {
            status = Strings.Format("ErrorUpdate", Updater.Describe(ex));
        }
        _updating = false;
        UpdateProgress.Visibility = Visibility.Collapsed;
        ShowUpdateState(status);
    }

    private void UpdateNotes_Click(object sender, RoutedEventArgs e)
    {
        if (Updater.Available is { } release)
            Updater.OpenReleasePage(release);
    }

    private void CheckAtStartup_Changed(object sender, RoutedEventArgs e)
    {
        if (!_initialized)
            return;
        _settings.CheckForUpdatesAtStartup = CheckAtStartupBox.IsChecked == true;
        _settings.Save();
    }

    private async void ChannelBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_initialized || ChannelBox.SelectedItem is not Option<UpdateChannel> option || option.Value == _settings.UpdateChannel)
            return;
        _settings.UpdateChannel = option.Value;
        _settings.Save();
        // What is offered depends on the channel: look again.
        if (!_updating)
            await CheckForUpdatesAsync();
    }

    // ---------------------------------------------------------------- Other

    private void Quit_Click(object sender, RoutedEventArgs e) => App.Quit();

    private void DeveloperMode_Changed(object sender, RoutedEventArgs e)
    {
        if (!_initialized)
            return;
        bool on = DeveloperModeBox.IsChecked == true;
        App.SetDeveloperMode(on);
        OpenLogButton.IsEnabled = on;
    }

    // The log in the app that opens text files (Notepad), created empty if there is none yet.
    private void OpenLog_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(DevLog.FilePath)!);
            if (!File.Exists(DevLog.FilePath))
                File.WriteAllText(DevLog.FilePath, "");
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(DevLog.FilePath) { UseShellExecute = true });
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception)
        {
        }
    }

    private void Link_RequestNavigate(object sender, RequestNavigateEventArgs e)
    {
        Process.Start(new ProcessStartInfo(e.Uri.AbsoluteUri) { UseShellExecute = true });
        e.Handled = true;
    }

    /// <summary>An entry of a drop-down list: a value and its text.</summary>
    private sealed record Option<T>(T Value, string Name)
    {
        public override string ToString() => Name;
    }
}
