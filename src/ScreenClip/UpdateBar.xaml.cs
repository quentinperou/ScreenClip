using System.Windows;
using System.Windows.Controls;
using ScreenClip.Core;
using ScreenClip.Services;

namespace ScreenClip;

/// <summary>
/// “ScreenClip x.y.z is available” with What's new / Update / close, shown at the top of the settings as soon as
/// the startup check finds a newer version. Update installs from here, like in CropPdf and FolderTools.
/// </summary>
public partial class UpdateBar : UserControl
{
    // Closing a bar hides that version in every window for the rest of the run, also after a language change.
    private static SemanticVersion? s_dismissed;

    // One install at a time, whichever window it was started from.
    private static bool s_installing;

    // Raised when a bar is closed, so that the bars of the other windows close too.
    private static event EventHandler? Dismissed;

    public UpdateBar()
    {
        InitializeComponent();
        Loaded += (_, _) =>
        {
            Updater.Checked += Refresh;
            Dismissed += Refresh;
            Show();
        };
        Unloaded += (_, _) =>
        {
            Updater.Checked -= Refresh;
            Dismissed -= Refresh;
        };
    }

    private bool _installingHere;

    private void Refresh(object? sender, EventArgs e) => Show();

    private void Show(string? message = null)
    {
        if (_installingHere)
            return;
        if (Updater.Available is not { } release || release.Version == s_dismissed)
        {
            Visibility = Visibility.Collapsed;
            return;
        }
        Message.Text = message ?? Strings.Format("UpdateBarText", release.Version);
        Actions.Visibility = Visibility.Visible;
        Actions.IsEnabled = !s_installing;
        CloseButton.Visibility = Visibility.Visible;
        Progress.Visibility = Visibility.Collapsed;
        Visibility = Visibility.Visible;
    }

    private async void Install_Click(object sender, RoutedEventArgs e)
    {
        if (Updater.Available is not { } release || s_installing)
            return;

        Actions.Visibility = Visibility.Collapsed;
        CloseButton.Visibility = Visibility.Collapsed;
        Progress.Value = 0;
        Progress.IsIndeterminate = false;
        Progress.Visibility = Visibility.Visible;
        Message.Text = Strings.Get("UpdateDownloading");
        var progress = new Progress<double>(p =>
        {
            Progress.Value = p;
            if (p < 1)
                return;
            // Downloaded: the installer now waits for the UAC prompt, then installs.
            Progress.IsIndeterminate = true;
            Message.Text = Strings.Get("UpdateStarting");
        });

        _installingHere = s_installing = true;
        string message;
        try
        {
            if (await Updater.InstallAsync(release, progress))
                return; // the app is closing, the installer takes over
            message = Strings.Get("UpdateCancelled");
        }
        catch (Exception ex)
        {
            message = Strings.Format("ErrorUpdate", Updater.Describe(ex));
        }
        _installingHere = s_installing = false;
        Show(message);
    }

    private void Notes_Click(object sender, RoutedEventArgs e)
    {
        if (Updater.Available is { } release)
            Updater.OpenReleasePage(release);
    }

    private void Close_Click(object sender, RoutedEventArgs e)
    {
        s_dismissed = Updater.Available?.Version;
        Dismissed?.Invoke(this, EventArgs.Empty);
    }
}
