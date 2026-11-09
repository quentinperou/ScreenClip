using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;
using ScreenClip.Core;
using static ScreenClip.Services.NativeMethods;

namespace ScreenClip.Services;

/// <summary>
/// A bug report (asked by the author: a user's ScreenClip did not start, and nobody could tell why): the technical
/// details of this ScreenClip, opened as a new issue on GitHub, where the user writes what happened and sends it, or
/// not. The details are in English, whatever the language of the app (the author reads them); they hold no capture,
/// no file name, no folder (only whether one was changed), and the user's folder and names are hidden.
/// </summary>
internal static class BugReporter
{
    private const int DevLogLines = 60;

    /// <summary>The details: this ScreenClip and its Windows, the screens, the settings, the last errors, the log.</summary>
    public static string Details()
    {
        var text = new StringBuilder();
        // Each part on its own: a report is often asked for when something is broken.
        void Part(Action write)
        {
            try
            {
                write();
            }
            catch (Exception ex)
            {
                text.AppendLine($"({ex.GetType().Name}: {ex.Message})");
            }
        }

        var s = App.Settings;
        Part(() => text.AppendLine($"ScreenClip {ErrorLog.Version}, {(WindowsSettings.IsInstalled ? "installed" : "not installed (run from another folder)")}, "
            + $"updates {s.UpdateChannel}{(s.CheckForUpdatesAtStartup ? "" : " (not checked at startup)")}"));
        Part(() => text.AppendLine($"Windows {Environment.OSVersion.Version} {RuntimeInformation.OSArchitecture}, "
            + $".NET {Environment.Version}, process {RuntimeInformation.ProcessArchitecture}"));
        Part(() => text.AppendLine($"Language {s.Language} (shown {CultureInfo.CurrentUICulture.Name}), theme {s.Theme}"));
        Part(() => text.AppendLine($"Screens: {string.Join(", ", ScreenGrabber.Monitors().Select(Screen))}"));
        Part(() =>
        {
            using var process = Process.GetCurrentProcess();
            text.AppendLine($"Running for {Since(process.StartTime)}, {process.WorkingSet64 / (1024 * 1024)} MB in memory, "
                + $"started with Windows {Yes(WindowsSettings.StartWithWindows)}, developer mode {Yes(s.DeveloperMode)}");
        });
        text.AppendLine();
        Part(() =>
        {
            IReadOnlyList<ShortcutAction> refused = App.Tray is { } tray ? tray.Refused : [];
            text.AppendLine($"Shortcuts {Yes(s.ShortcutsEnabled)}: " + string.Join(", ", Enum.GetValues<ShortcutAction>()
                .Select(a => $"{a} {s.GetShortcut(a)}{(refused.Contains(a) ? " (refused)" : "")}")));
            text.AppendLine($"Print Screen kept by Windows: {YesNo(WindowsSettings.PrintScreenUsedByWindows)}, asked: {YesNo(s.PrintScreenAsked)}");
        });
        Part(() => text.AppendLine($"Icon click {s.TrayClick}, editor after a capture {Yes(s.OpenEditorAfterCapture)}, "
            + $"thumbnail {Yes(s.ShowThumbnail)} ({s.ThumbnailScreen} screen), copy {Yes(s.CopyToClipboard)}, saved {Yes(s.SaveAutomatically)}"));
        Part(() => text.AppendLine($"Folders: captures {Changed(s.SaveFolder)}, pictures {Changed(s.ExportFolder)}, "
            + $"videos {Changed(s.VideoFolder)}, file name {Changed(s.FileNameTemplate)}"));
        Part(() => text.AppendLine($"Video: computer sound {Yes(s.RecordSystemSound)}, microphone {Yes(s.RecordMicrophone)} "
            + $"({(string.IsNullOrEmpty(s.MicrophoneId) ? "Windows' default" : "chosen")}), pointer apart {Yes(s.RecordPointerApart)}, "
            + $"countdown {Yes(s.RecordCountdown)}, export {s.VideoFormat}"));
        Part(() => text.AppendLine($"Editor: tab {s.EditorTab}, {s.UserPresets.Count} presets of the user, layout on export {Yes(s.ExportLayout)}, "
            + $"copy as {s.CopyFormat}, picker {s.ColorFormat}"));

        var errors = ErrorLog.Read();
        text.AppendLine();
        text.AppendLine(errors.Count == 0 ? "No error kept." : $"Last errors ({errors.Count}, the latest first):");
        foreach (string error in errors.Reverse())
            text.AppendLine().AppendLine(error);

        if (DevLog.Enabled)
        {
            Part(() =>
            {
                if (!File.Exists(DevLog.FilePath))
                    return;
                var lines = File.ReadAllLines(DevLog.FilePath).TakeLast(DevLogLines).ToList();
                text.AppendLine().AppendLine($"Developer log, the last {lines.Count} lines:");
                foreach (string line in lines)
                    text.AppendLine(ErrorLog.Clean(line));
            });
        }
        return ErrorLog.Clean(text.ToString().TrimEnd().Replace("\r\n", "\n"));
    }

    /// <summary>
    /// Opens the new issue in the browser, its body filled in with <paramref name="details"/>. True when they had to
    /// be cut to fit in the address: they are then put on the clipboard whole, and the issue says so.
    /// </summary>
    public static bool Open(string details, Exception? error = null)
    {
        string title = error is null ? Strings.Get("ReportIssueTitle") : Strings.Format("ReportIssueErrorTitle", error.GetType().Name);
        string head = $"### {Strings.Get("ReportWhatHappened")}\n\n<!-- {Strings.Get("ReportWhatHappenedHint")} -->\n\n\n\n"
            + $"### {Strings.Get("ReportTechnical")}\n\n<details><summary>ScreenClip {ErrorLog.Version}, Windows {Environment.OSVersion.Version}</summary>\n\n````text\n";
        const string tail = "\n````\n\n</details>\n";
        string url = BugReport.NewIssueUrl(UpdateFeed.Repository, title, head, details, tail, Strings.Get("ReportCut"), out bool cut);
        if (cut)
            Copy(ForClipboard(details));
        OpenUrl(url);
        return cut;
    }

    /// <summary>The details as pasted into an issue or a message: a block of text.</summary>
    public static string ForClipboard(string details) => $"````text\n{details}\n````\n";

    /// <summary>
    /// Asks with the plain Windows message box (for an error at startup or a crash) and opens the issue on Yes. No is
    /// the default: Enter typed for another app does not open the browser.
    /// </summary>
    public static void Ask(string question, Exception error)
    {
        try
        {
            if (MessageBoxW(IntPtr.Zero, question, "ScreenClip", MB_YESNO | MB_ICONERROR | MB_DEFBUTTON2 | MB_TOPMOST) == IDYES)
                Open(Details(), error);
        }
        catch (Exception)
        {
        }
    }

    /// <summary>Puts <paramref name="text"/> on the clipboard, from any thread (the clipboard needs an STA one).</summary>
    public static bool Copy(string text)
    {
        if (Thread.CurrentThread.GetApartmentState() == ApartmentState.STA)
            return ClipActions.CopyText(text);
        bool done = false;
        var thread = new Thread(() => done = ClipActions.CopyText(text));
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join(TimeSpan.FromSeconds(3));
        return done;
    }

    private static void OpenUrl(string url)
    {
#if DEBUG
        // SCREENCLIP_URL_FILE (Debug only): the address written there instead, so that tests open no browser.
        if (Environment.GetEnvironmentVariable("SCREENCLIP_URL_FILE") is { Length: > 0 } file)
        {
            File.WriteAllText(file, url);
            return;
        }
#endif
        Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
    }

    private static string Screen(MonitorInfo monitor)
    {
        var center = new POINT { X = monitor.Bounds.X + monitor.Bounds.Width / 2, Y = monitor.Bounds.Y + monitor.Bounds.Height / 2 };
        string scale = GetDpiForMonitor(MonitorFromPoint(center, MONITOR_DEFAULTTONEAREST), MDT_EFFECTIVE_DPI, out uint dpi, out _) == 0
            ? $" at {dpi * 100 / 96} %" : "";
        return $"{monitor.Bounds.Width}×{monitor.Bounds.Height}{scale}{(monitor.IsPrimary ? " (primary)" : "")}";
    }

    private static string Since(DateTime start)
    {
        var time = DateTime.Now - start;
        return time.TotalHours >= 1 ? $"{(int)time.TotalHours} h {time.Minutes} min" : $"{time.Minutes} min {time.Seconds} s";
    }

    private static string Yes(bool on) => on ? "on" : "off";

    private static string YesNo(bool yes) => yes ? "yes" : "no";

    private static string Changed(string? value) => string.IsNullOrWhiteSpace(value) ? "default" : "changed";
}
