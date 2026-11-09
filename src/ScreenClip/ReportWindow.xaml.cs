using System.Windows;
using ScreenClip.Services;

namespace ScreenClip;

/// <summary>
/// Reports a problem on GitHub (Settings › About, the notification menu, or after an error): the details the issue will
/// hold, to read first, then Open on GitHub (the user writes what happened there and sends it, or not), or Copy.
/// </summary>
public partial class ReportWindow : Window
{
    private static ReportWindow? s_open;

    private readonly Exception? _error;
    private readonly string _details;

    private ReportWindow(Exception? error)
    {
        _error = error;
        InitializeComponent();
        MicaBackdrop.Apply(this);
        _details = BugReporter.Details();
        DetailsBox.Text = _details.Replace("\n", Environment.NewLine);
        if (error is not null)
        {
            Title = Strings.Get("ReportErrorTitle");
            Heading.Text = Title;
            ErrorText.Text = ErrorLog.Clean(error.Message);
            ErrorBar.Visibility = Visibility.Visible;
            CloseButton.Content = Strings.Get("Close");
        }
    }

    /// <summary>
    /// Opens the window, for <paramref name="error"/> when one was just met. Already open: it comes forward as it is
    /// (an error met meanwhile is in the details of the next one).
    /// </summary>
    internal static void Open(Exception? error = null, Window? owner = null)
    {
        if (s_open is { } open)
        {
            open.Activate();
            return;
        }

        App.EnsureTheme();
        var window = new ReportWindow(error);
        if (owner is { IsVisible: true })
        {
            window.Owner = owner;
            window.WindowStartupLocation = WindowStartupLocation.CenterOwner;
        }
        // Opened for an error, it shows without taking the keyboard from what is being typed (seen in a test).
        window.ShowActivated = error is null;
        s_open = window;
        window.Closed += (_, _) =>
        {
            s_open = null;
            MemoryTrim.Soon();
        };
        window.Show();
        if (error is null)
            window.Activate();
    }

    private void Open_Click(object sender, RoutedEventArgs e)
    {
        if (BugReporter.Open(_details, _error))
            ShowStatus(Strings.Get("ReportTooLong")); // stays open: the user has to paste them
        else
            Close();
    }

    private void Copy_Click(object sender, RoutedEventArgs e)
    {
        if (BugReporter.Copy(BugReporter.ForClipboard(_details)))
            ShowStatus(Strings.Get("ReportCopied"));
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    private void ShowStatus(string text)
    {
        Status.Text = text;
        Status.Visibility = Visibility.Visible;
    }
}
