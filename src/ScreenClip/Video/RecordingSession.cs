using System.Diagnostics;
using System.Windows;
using System.Windows.Threading;
using ScreenClip.Core;
using ScreenClip.Services;

namespace ScreenClip.Video;

/// <summary>
/// One video being recorded: the line around the area and the bar under it, a 3 second countdown, then the recorder;
/// pause, stop (the video goes to the editor), start again, discard. One at a time.
/// </summary>
internal sealed class RecordingSession
{
    private const int CountdownSeconds = 3;

    private readonly PixelRect _area;
    private readonly MonitorInfo _monitor;
    private readonly RecordingFrame _frame;
    private readonly RecordingBar _bar;
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(200) };
    private readonly Stopwatch _countdown = new();
    private ScreenRecorder? _recorder;
    private Task<ScreenRecorder>? _preparing;   // made during the countdown: Media Foundation takes a moment the first time
    private DateTime _startedAt;
    private bool _ending;

    private RecordingSession(PixelRect area, MonitorInfo monitor)
    {
        // H.264 wants even sizes: the area loses a pixel rather than gaining one off screen.
        _area = area with { Width = Math.Max(2, area.Width & ~1), Height = Math.Max(2, area.Height & ~1) };
        _monitor = monitor;
        _frame = new RecordingFrame(_area);
        _bar = new RecordingBar(_area, monitor);
        _bar.PauseClicked += (_, _) => TogglePause();
        _bar.StopClicked += (_, _) => Stop();
        _bar.RestartClicked += (_, _) => Restart();
        _bar.DiscardClicked += (_, _) => Discard();
        _timer.Tick += (_, _) => Tick();
    }

    /// <summary>The recording under way, if any: the video shortcut and the notification icon stop it.</summary>
    public static RecordingSession? Current { get; private set; }

    /// <summary>Where the recordings are written before they are saved or edited (emptied at each start of the app).</summary>
    public static string Folder => Path.Combine(ClipActions.TempFolder, "Recordings");

    public static void Begin(PixelRect area, MonitorInfo monitor)
    {
        if (Current is not null || area.Width < 16 || area.Height < 16)
            return;
        Current = new RecordingSession(area, monitor);
        Current.Show();
    }

    private void Show()
    {
        _frame.Show();
        _bar.Show();
        StartCountdown();
    }

    private void StartCountdown()
    {
        _recorder = null;
        _bar.EnableControls(false);
        _bar.ShowPaused(false);
        _preparing = Prepare();
        _countdown.Restart();
        _timer.Start();
        Tick();
    }

    // The file, the encoder and the capture, ready to start, made on a worker thread.
    private Task<ScreenRecorder> Prepare()
    {
        var settings = App.Settings;
        _startedAt = DateTime.Now;
        string name = FileNaming.Expand(settings.EffectiveVideoFileNameTemplate, _startedAt);
        bool sound = settings.RecordSystemSound, microphone = settings.RecordMicrophone;
        var area = _area;
        var bounds = _monitor.Bounds;
        return Task.Run(() =>
        {
            Directory.CreateDirectory(Folder);
            string path = FileNaming.UniquePath(Folder, name, ".mp4");
            var center = new NativeMethods.POINT { X = area.X + area.Width / 2, Y = area.Y + area.Height / 2 };
            var monitor = NativeMethods.MonitorFromPoint(center, NativeMethods.MONITOR_DEFAULTTONEAREST);
            return new ScreenRecorder(area, monitor, bounds, path, sound, microphone);
        });
    }

    private void Tick()
    {
        if (_recorder is null)
        {
            // The countdown stays on 1 while the recorder is not ready yet.
            int left = Math.Max(1, CountdownSeconds - (int)_countdown.Elapsed.TotalSeconds);
            if (_countdown.Elapsed.TotalSeconds < CountdownSeconds || _preparing is { IsCompleted: false })
            {
                _frame.ShowCount(left);
                _bar.ShowText(left.ToString(System.Globalization.CultureInfo.CurrentCulture));
                return;
            }
            _frame.ShowCount(null);
            StartRecorder();
            return;
        }
        _bar.ShowText(Format(_recorder.Elapsed));
    }

    /// <summary>“0:07”, or “1:02:07” past an hour.</summary>
    public static string Format(TimeSpan time) => time.TotalHours >= 1
        ? time.ToString(@"h\:mm\:ss", System.Globalization.CultureInfo.InvariantCulture)
        : time.ToString(@"m\:ss", System.Globalization.CultureInfo.InvariantCulture);

    private void StartRecorder()
    {
        try
        {
            var preparing = _preparing!;
            _preparing = null;
            _recorder = preparing.GetAwaiter().GetResult();
            _recorder.Start();
            DevLog.Write($"recording: {_recorder.Width} x {_recorder.Height} at {_area.X},{_area.Y}, sound {_recorder.HasSound} "
                + $"(computer {App.Settings.RecordSystemSound}, microphone {App.Settings.RecordMicrophone})");
            _bar.EnableControls(true);
            Tick();
        }
        catch (Exception ex)
        {
            // Whatever Windows' capture or encoder refuses (no graphics card, a protected screen…): said, and nothing kept.
            DevLog.Write($"recording: could not start: {ex}");
            _recorder?.Abort();
            _recorder = null;
            End();
            App.ShowHiddenEditor();
            MessageBox.Show(Strings.Format("ErrorRecording", ex.Message), "ScreenClip", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    public void TogglePause()
    {
        if (_recorder is not { } recorder || _ending)
            return;
        if (recorder.IsPaused)
            recorder.Resume();
        else
            recorder.Pause();
        _bar.ShowPaused(recorder.IsPaused);
        Tick();
    }

    /// <summary>Ends the recording: the video opens in the editor. During the countdown, nothing is recorded.</summary>
    public async void Stop()
    {
        if (_ending)
            return;
        if (_recorder is not { } recorder)
        {
            Discard();
            return;
        }
        _ending = true;
        _timer.Stop();
        _bar.ShowSaving();
        try
        {
            var length = await Task.Run(recorder.Stop);
            DevLog.Write($"recording: stopped after {length}");
        }
        catch (Exception ex)
        {
            DevLog.Write($"recording: could not be written: {ex}");
            recorder.Abort();
            End();
            MessageBox.Show(Strings.Format("ErrorRecording", ex.Message), "ScreenClip", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        recorder.Dispose();
        End();
        App.Capture.DeliverVideo(recorder.Path, _startedAt, _monitor);
    }

    private void Restart()
    {
        if (_ending)
            return;
        _recorder?.Abort();
        DropPreparing();
        StartCountdown();
    }

    // A recorder made for a countdown that was stopped: given up as soon as it is ready.
    private void DropPreparing()
    {
        if (_preparing is not { } preparing)
            return;
        _preparing = null;
        preparing.ContinueWith(task =>
        {
            if (task.IsCompletedSuccessfully)
                task.Result.Abort();
        }, TaskScheduler.Default);
    }

    private void Discard()
    {
        if (_ending)
            return;
        _ending = true;
        DevLog.Write("recording: discarded");
        _recorder?.Abort();
        DropPreparing();
        End();
        App.ShowHiddenEditor();
    }

    private void End()
    {
        _timer.Stop();
        _frame.Close();
        _bar.Close();
        Current = null;
        MemoryTrim.Soon();
    }
}
