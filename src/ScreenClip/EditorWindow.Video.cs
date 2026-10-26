using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Microsoft.Win32;
using ScreenClip.Core;
using ScreenClip.Core.Annotations;
using ScreenClip.Core.Composition;
using ScreenClip.Core.Video;
using ScreenClip.Services;
using ScreenClip.Video;
using SkiaSharp;

namespace ScreenClip;

/// <summary>
/// A video in the editor: the frame at the playhead is the picture both tabs show (annotations, crop and layout apply
/// to every frame), a timeline to move in it and cut its start and end, playing with the sound, and the export of
/// the part kept as MP4 or GIF.
/// </summary>
public partial class EditorWindow
{
    private enum TimelineGrip { None, Playhead, Start, End }

    private VideoClip? _videoClip;
    private VideoReader? _frameReader;          // the frames shown when the playhead moves, read on a worker thread
    private readonly object _frameLock = new();
    private TimeSpan? _wantedFrame;
    private TimeSpan? _shownFrameAt;             // when the frame shown starts (a move inside it reads nothing)
    private bool _frameWorker;
    private TimeSpan _duration;
    private TrimRange _trim;
    private TimeSpan _playhead;
    private bool _videoHasSound;
    private VideoPlayer? _player;
    private TimelineGrip _grip;
    private CancellationTokenSource? _export;
    private Task? _exportTask;
    private bool _videoKept;                     // saved or copied: it may leave the editor without a question
    private bool _leaving;
    private int _playKey;                        // what was drawn over the frame last, while playing

    private bool IsVideo => _videoClip is not null;

#if DEBUG
    private int _framesShown;                    // for the script player's scrub test
#endif

    private static string Decimal => System.Globalization.CultureInfo.CurrentCulture.NumberFormat.NumberDecimalSeparator;

    // ---------------------------------------------------------------- Opening and closing

    /// <summary>Opens a video: its first frame is shown, all of it kept.</summary>
    internal async void LoadVideo(VideoClip clip)
    {
        if (!await LeaveVideo())
            return;
        CloseVideo();
        VideoReader? reader = null;
        SKImage? first;
        try
        {
            reader = new VideoReader(clip.Path);
            first = reader.FrameAt(TimeSpan.Zero);
        }
        catch (Exception ex) when (ex is SharpGen.Runtime.SharpGenException or IOException or UnauthorizedAccessException)
        {
            reader?.Dispose();
            DevLog.Write($"editor: video not opened: {ex}");
            _ = ShowMessageAsync(Strings.Format("ErrorOpenVideo", ex.Message));
            return;
        }
        if (first is null)
        {
            reader.Dispose();
            _ = ShowMessageAsync(Strings.Format("ErrorOpenVideo", "—"));
            return;
        }
        DevLog.Write($"editor: video {reader.Width} x {reader.Height}, {reader.Duration}, sound {reader.HasAudio}");
        _videoClip = clip;
        _frameReader = reader;
        _duration = reader.Duration;
        _videoHasSound = reader.HasAudio;
        LoadSounds(clip, reader.HasAudio);
        _pieceChosen = false;
        _trim = TrimRange.All(_duration);
        _playhead = TimeSpan.Zero;
        Timeline.ToolTip = Strings.Get("VideoTimelineToolTip");
        SetCapture(first, clip.TakenAt);
        // The recorded pointer as it was (asked by the author: each new capture starts from the defaults).
        _history.Reset(AnnotationLayer.Empty);
        _videoKept = clip.Kept;
        ShowAsRecordedSwatch();
        ShowPlaying(false);
        BuildThumbnails(clip.Path);
        ShowTimeline();
        ShowTracks();
        ShowSounds();
        ShowEditPanel();
    }

    /// <summary>A recording kept nowhere yet (its pointer recorded apart, or nothing saved nor copied).</summary>
    private bool NeedsKeeping => IsVideo && !_videoKept;

    /// <summary>
    /// Before a recording kept nowhere leaves the editor: save it, leave it, or keep it here. True when it may go.
    /// </summary>
    private async Task<bool> LeaveVideo()
    {
        if (!NeedsKeeping)
            return true;
        StopPlayback();
        int answer = await AskAsync(Strings.Get("VideoNotKeptTitle"), Strings.Get("VideoNotKeptText"),
            Strings.Get("ExportSave"), Strings.Get("VideoNotKeptDrop"), Strings.Get("Cancel"));
        return answer switch
        {
            1 => true,
            0 => await SaveVideoAsync(),
            _ => false,
        };
    }

    /// <summary>Back to pictures: the playing, an export and the reader stop.</summary>
    private void CloseVideo()
    {
        if (!IsVideo)
            return;
        StopPlayback();
        _export?.Cancel();
        try
        {
            _exportTask?.Wait(3000);
        }
        catch (AggregateException)
        {
        }
        lock (_frameLock)
        {
            _frameReader?.Dispose();
            _frameReader = null;
            _wantedFrame = null;
        }
        _videoClip = null;
        _videoKept = false;
        _shownFrameAt = null;
        _cursorSelected = false;
        _trackDrag = null;
        _soundDrag = null;
        _soundRows = [];
        _peaks = [];
        _soundSelected = -1;
        _pieceChosen = false;
        ThumbStrip.Source = null;
        TimelineMarks.Children.Clear();
        ShowTracks();
        ShowSounds();
        ShowState();
    }

    // ---------------------------------------------------------------- Frames

    /// <summary>The frame at the playhead, as the picture of both tabs (annotations and layout kept).</summary>
    private void ShowFrame(SKImage frame)
    {
#if DEBUG
        _framesShown++;
#endif
        _capture?.Dispose();
        _capture = frame;
        RequestRender();
    }

    // Moving the playhead asks for its frame; a worker reads it (from the key frame before), the latest asked wins. A
    // picture animated has no frame to read: only what is drawn over it moves.
    private void RequestFrame(TimeSpan time)
    {
        _playhead = time;
        ShowTimeline();
        // What is drawn over the frame moves with the time even inside one frame (the pointer, an A → B pointer).
        if (HasTimedOverlay)
            RequestRender();
        if (!IsVideo)
            return;
        lock (_frameLock)
        {
            _wantedFrame = time;
            if (_frameWorker)
                return;
            _frameWorker = true;
        }
        Task.Run(() =>
        {
            while (true)
            {
                SKImage? frame;
                TimeSpan at;
                lock (_frameLock)
                {
                    if (_wantedFrame is not { } wanted || _frameReader is null)
                    {
                        _frameWorker = false;
                        return;
                    }
                    _wantedFrame = null;
                    try
                    {
                        frame = _frameReader.FrameAt(wanted, _shownFrameAt, out _, out at);
                    }
                    catch (SharpGen.Runtime.SharpGenException)
                    {
                        frame = null;
                        at = default;
                    }
                }
                if (frame is not null)
                {
                    Dispatcher.BeginInvoke(() =>
                    {
                        if (IsVideo && _player is null)
                        {
                            ShowFrame(frame);
                            lock (_frameLock)
                                _shownFrameAt = at;
                        }
                        else
                            frame.Dispose();
                    });
                }
            }
        });
    }

    // A strip of small frames along the timeline, made in the background.
    private void BuildThumbnails(string path)
    {
        int count = Math.Clamp((int)(Timeline.ActualWidth / 72), 6, 24);
        var duration = _duration;
        Task.Run(() =>
        {
            try
            {
                using var reader = new VideoReader(path);
                const int Height = 72;
                int width = Math.Max(1, (int)Math.Round(Height * (double)reader.Width / reader.Height));
                using var strip = SKSurface.Create(new SKImageInfo(width * count, Height, SKColorType.Bgra8888, SKAlphaType.Premul));
                strip.Canvas.Clear(SKColors.Black);
                for (int i = 0; i < count; i++)
                {
                    using var frame = reader.FrameAt(duration * ((i + 0.5) / count));
                    if (frame is not null)
                        strip.Canvas.DrawImage(frame, SKRect.Create(i * width, 0, width, Height), new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.Linear));
                }
                using var picture = strip.Snapshot();
                var source = Imaging.ToWpf(picture);
                Dispatcher.BeginInvoke(() =>
                {
                    if (_videoClip?.Path == path)
                        ThumbStrip.Source = source;
                });
            }
            catch (Exception ex) when (ex is SharpGen.Runtime.SharpGenException or IOException or UnauthorizedAccessException)
            {
                // No strip: the timeline works without it.
            }
        });
    }

    // ---------------------------------------------------------------- Timeline

    private void Timeline_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (IsAnimation && e.PreviousSize.Width <= 0)
            ShowStillStrip();
        ShowTimeline();
        ShowTracks();
        ShowSounds();
    }

    private double XOf(TimeSpan time) =>
        Span <= TimeSpan.Zero ? 0 : Timeline.ActualWidth * Math.Clamp(time / Span, 0, 1);

    private TimeSpan TimeAt(double x) =>
        Timeline.ActualWidth <= 0 ? TimeSpan.Zero : Span * Math.Clamp(x / Timeline.ActualWidth, 0, 1);

    /// <summary>
    /// The part kept (between the yellow handles), what is cut (darkened), the splits and the pieces removed, and the
    /// playhead; its time is in what is kept (the export's), with its length.
    /// </summary>
    private void ShowTimeline()
    {
        if (!IsTimed)
            return;
        // An animation's length is the field next to it.
        VideoTimeText.Text = IsVideo
            ? $"{Timecode.Format(Edit.KeptTimeOf(_playhead, _trim), Decimal)} / {Timecode.Format(Edit.KeptLength(_trim), Decimal)}"
            : $"{Timecode.Format(_playhead, Decimal)} /";
        ShowPieceButtons();
        double width = Timeline.ActualWidth, height = Timeline.ActualHeight;
        TimelineMarks.Children.Clear();
        if (width <= 0)
            return;
        // The timeline reaches 10 px above the frames: the playhead's ball, on top, is grabbed too (asked by the author).
        const double Above = 10;
        double start = XOf(_trim.Start), end = XOf(_trim.End), top = Above + 6, bottom = height - 6;
        var cut = new SolidColorBrush(Color.FromArgb(0xB0, 0x10, 0x10, 0x14));
        var yellow = new SolidColorBrush(Color.FromRgb(0xFF, 0xC5, 0x3D));
        Add(new System.Windows.Shapes.Rectangle { Width = Math.Max(0, start), Height = bottom - top, Fill = cut }, 0, top);
        Add(new System.Windows.Shapes.Rectangle { Width = Math.Max(0, width - end), Height = bottom - top, Fill = cut }, end, top);
        ShowPieces(top, bottom);
        Add(new System.Windows.Shapes.Rectangle { Width = Math.Max(0, end - start), Height = bottom - top + 4, Stroke = yellow, StrokeThickness = 2, RadiusX = 3, RadiusY = 3 }, start, top - 2);
        // An animation starts at its start: its end alone is pulled, to make it longer or shorter.
        foreach (double x in IsVideo ? new[] { start, end } : [end])
        {
            Add(new System.Windows.Shapes.Rectangle { Width = 10, Height = height - Above, Fill = yellow, RadiusX = 3, RadiusY = 3 }, x - 5, Above);
            Add(new System.Windows.Shapes.Rectangle { Width = 2, Height = 14, Fill = new SolidColorBrush(Color.FromRgb(0x8A, 0x61, 0x00)), RadiusX = 1, RadiusY = 1 }, x - 1, (height + Above) / 2 - 7);
        }
        double playhead = XOf(_playhead);
        var red = new SolidColorBrush(Color.FromRgb(0xE5, 0x48, 0x4D));
        // Down to the tracks under the timeline (4 px below it), where their own line goes on.
        Add(new System.Windows.Shapes.Rectangle { Width = 2, Height = height - Above + 7, Fill = red }, playhead - 1, Above - 3);
        Add(new System.Windows.Shapes.Ellipse { Width = 12, Height = 12, Fill = red }, playhead - 6, Above - 8);
        MoveTrackPlayhead();
        MoveSoundPlayhead();
    }

    private void Add(UIElement element, double x, double y)
    {
        Canvas.SetLeft(element, x);
        Canvas.SetTop(element, y);
        TimelineMarks.Children.Add(element);
    }

    private TimelineGrip GripAt(double x)
    {
        double start = XOf(_trim.Start), end = XOf(_trim.End);
        if (!IsVideo)
            return Math.Abs(x - end) <= 8 ? TimelineGrip.End : TimelineGrip.Playhead;
        // The handle nearer the pointer wins (they may touch).
        if (Math.Abs(x - start) <= 8 && Math.Abs(x - start) <= Math.Abs(x - end))
            return TimelineGrip.Start;
        if (Math.Abs(x - end) <= 8)
            return TimelineGrip.End;
        return TimelineGrip.Playhead;
    }

    private void Timeline_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (!IsTimed || _export is not null)
            return;
        StopPlayback();
        var at = e.GetPosition(Timeline);
        // The ↺ of a piece removed puts it back.
        if (RestoreButtonAt(at.X, at.Y) is { } removed)
        {
            CommitVideo(Edit.RestoreAt(removed.Start));
            e.Handled = true;
            return;
        }
        double x = at.X;
        _grip = GripAt(x);
        // The piece there is chosen (asked by the author: a click on the video chooses it): its frame and zoom in the
        // Edit panel, Delete removes it.
        if (_grip == TimelineGrip.Playhead && IsVideo)
            ChoosePiece();
        Timeline.CaptureMouse();
        MoveGrip(x);
        e.Handled = true;
    }

    private void Timeline_MouseMove(object sender, MouseEventArgs e)
    {
        if (!IsTimed)
            return;
        double x = e.GetPosition(Timeline).X;
        if (_grip == TimelineGrip.None)
        {
            Timeline.Cursor = GripAt(x) == TimelineGrip.Playhead ? Cursors.Hand : Cursors.SizeWE;
            return;
        }
        if (e.LeftButton != MouseButtonState.Pressed)
        {
            EndGrip();
            return;
        }
        MoveGrip(x);
    }

    private void Timeline_MouseLeftButtonUp(object sender, MouseButtonEventArgs e) => EndGrip();

    private void MoveGrip(double x)
    {
        var time = TimeAt(x);
        switch (_grip)
        {
            case TimelineGrip.Start:
                _trim = _trim.WithStart(time);
                RequestFrame(_trim.Start);
                ShowTracks();
                ShowSounds();
                break;
            case TimelineGrip.End when !IsVideo:
                // An animation's end: its length, set when the button goes up (one step to undo).
                _duration = time < MinAnimation ? MinAnimation : time > MaxAnimation ? MaxAnimation : time;
                _trim = TrimRange.All(_duration);
                RequestFrame(_duration);
                ShowTracks();
                ShowAnimationLength();
                break;
            case TimelineGrip.End:
                _trim = _trim.WithEnd(time, _duration);
                // The last frame kept: just before the end.
                RequestFrame(_trim.End - TimeSpan.FromMilliseconds(1));
                ShowTracks();
                ShowSounds();
                break;
            case TimelineGrip.Playhead:
                RequestFrame(_trim.Clamp(time));
                break;
        }
        RequestRender(); // the length shown under the picture
    }

    private void EndGrip()
    {
        bool length = _grip == TimelineGrip.End && IsAnimation;
        _grip = TimelineGrip.None;
        if (Timeline.IsMouseCaptured)
            Timeline.ReleaseMouseCapture();
        if (length)
            SetAnimationLength(_duration);
    }

    // ---------------------------------------------------------------- Playing

    private void Play_Click(object sender, RoutedEventArgs e) => TogglePlay();

    private void TogglePlay()
    {
        if (_player is not null)
            StopPlayback();
        else
            StartPlayback();
    }

    private void StartPlayback()
    {
        if (!IsTimed || _export is not null)
            return;
        CommitText();
        CancelDrag();
        // The player shows its own frames: the next frame asked is read again.
        lock (_frameLock)
            _shownFrameAt = null;
        // From the playhead through what is kept (the pieces removed skipped); from the start of it when the playhead is
        // at its end.
        var pieces = Edit.KeptFrom(_playhead, _trim);
        if (pieces.Aggregate(TimeSpan.Zero, (sum, p) => sum + p.Length) < TimeSpan.FromMilliseconds(100))
            pieces = Edit.Kept(_trim);
        var end = pieces.Count > 0 ? pieces[^1].End : _trim.End;
        _playKey = 0;
        _player = new VideoPlayer(_videoClip?.Path, pieces, IsVideo ? SoundMaker(pieces) : null,
            (frame, time) =>
            {
                _playhead = time;
                int key = VideoTiming.Fingerprint(_history.Current, Moment!);
                if (frame is not null)
                    ShowFrame(frame);
                else if (key != _playKey)
                    RequestRender();
                _playKey = key;
                ShowTimeline();
            },
            () => Dispatcher.BeginInvoke(() =>
            {
                StopPlayback();
                _playhead = end;
                ShowTimeline();
            }));
        ShowPlaying(true);
        _player.Start();
    }

    private void StopPlayback()
    {
        if (_player is not { } player)
            return;
        _player = null;
        var at = player.Position;
        player.Dispose();
        ShowPlaying(false);
        if (IsTimed)
            RequestFrame(_trim.Clamp(at));
    }

    private void ShowPlaying(bool playing)
    {
        PlayGlyph.Text = playing ? "" : "";
        PlayButton.ToolTip = Strings.Get(playing ? "VideoPause" : "VideoPlay");
        System.Windows.Automation.AutomationProperties.SetName(PlayButton, Strings.Get(playing ? "VideoPause" : "VideoPlay"));
    }

    // ---------------------------------------------------------------- Export

    /// <summary>Exports the part kept, as the editor shows it; false when cancelled or refused (said).</summary>
    private async Task<bool> ExportVideo(string target, bool gif)
    {
        if (!IsTimed || _capture is null || _export is not null)
            return false;
        var source = _videoClip?.Path;
        // A picture animated: the picture itself, copied by the export before it starts.
        var still = source is null ? _capture : null;
        StopPlayback();
        CommitText();
        // What the frames are drawn with, fixed now: the export runs on another thread.
        var layer = _history.Current;
        var style = _style;
        var background = BackgroundImageFor(style);
        var pieces = Edit.Kept(_trim);
        var length = Edit.KeptLength(_trim);
        var sound = gif || !IsVideo ? null : SoundMaker(pieces);
        var track = Track;
        var kept = layer.Visible(_capture.Width, _capture.Height);
        // The layout applied, or the video with its annotations alone (the Export window's switch).
        bool laidOut = ExportsLayout;
        int width = laidOut ? ClipLayout.Measure(kept.Width, kept.Height, style).Width : kept.Width;
        bool pointer = track is { IsEmpty: false } && layer.Cursor.Shows;
        // A piece zoomed in: the part shown, at the picture's size.
        SKImage Framed(SKImage flat, TimeSpan time, bool owned)
        {
            if (layer.Video.FramingAt(time) is not { } framing)
                return flat;
            var framed = framing.Apply(flat);
            if (owned)
                flat.Dispose();
            return framed;
        }
        SKImage Render(SKImage frame, TimeSpan time, float scale)
        {
            if (!laidOut)
            {
                var alone = Framed(AnnotationRenderer.Flatten(frame, layer, new VideoMoment(time, track)), time, owned: true);
                if (scale >= 1)
                    return alone;
                // Smaller for a GIF.
                using (alone)
                {
                    var info = new SKImageInfo(Math.Max(1, (int)Math.Round(alone.Width * scale)), Math.Max(1, (int)Math.Round(alone.Height * scale)), SKColorType.Bgra8888, SKAlphaType.Premul);
                    using var surface = SKSurface.Create(info);
                    surface.Canvas.DrawImage(alone, SKRect.Create(info.Width, info.Height), new SKSamplingOptions(SKCubicResampler.Mitchell));
                    return surface.Snapshot();
                }
            }
            var flat = layer.IsEmpty && !pointer ? frame : AnnotationRenderer.Flatten(frame, layer, new VideoMoment(time, track));
            flat = Framed(flat, time, owned: !ReferenceEquals(flat, frame));
            try
            {
                return Composer.Render(flat, style, scale, background);
            }
            finally
            {
                if (!ReferenceEquals(flat, frame))
                    flat.Dispose();
            }
        }
        // What is drawn over the frames changes with the time (annotations that come and go, the pointer): a frame is
        // written whenever it changes, not only when the screen did.
        bool timed = pointer || layer.Items.Any(a => a.Time is not null || a.Motion is not null) || !layer.Video.Framings.IsEmpty;
        Func<TimeSpan, int>? overlay = timed ? time => HashCode.Combine(VideoTiming.Fingerprint(layer, new VideoMoment(time, track)), layer.Video.FramingAt(time)) : null;

        var cancel = new CancellationTokenSource();
        _export = cancel;
        SetBusy(true);
        ExportTitle.Text = Strings.Get(gif ? "ExportingGif" : "ExportingVideo");
        ExportProgress.Value = 0;
        ExportPercent.Text = "";
        ExportOverlay.Visibility = Visibility.Visible;
        var progress = new Progress<double>(p =>
        {
            ExportProgress.Value = p;
            ExportPercent.Text = Strings.Format("EditorPercent", (int)Math.Round(p * 100));
        });
        var watch = System.Diagnostics.Stopwatch.StartNew();
        try
        {
            _exportTask = Task.Run(() =>
            {
                if (source is not null)
                    VideoExporter.Export(source, target, gif, pieces, Render, overlay, width, sound, progress, cancel.Token);
                else
                    VideoExporter.ExportStill(still!, target, gif, length, Render, overlay, width, progress, cancel.Token);
            });
            await _exportTask;
            DevLog.Write($"export: {(gif ? "GIF" : "MP4")} of {length} in {pieces.Count} piece(s), {watch.Elapsed.TotalSeconds:0.0} s, {new FileInfo(target).Length / 1024} KB");
            return true;
        }
        catch (OperationCanceledException)
        {
            DevLog.Write("export: cancelled");
            return false;
        }
        catch (Exception ex) when (ex is SharpGen.Runtime.SharpGenException or IOException or UnauthorizedAccessException or InvalidDataException or ArgumentException)
        {
            DevLog.Write($"export: failed: {ex}");
            _ = ShowMessageAsync(Strings.Format("ErrorExport", ex.Message));
            return false;
        }
        finally
        {
            _export = null;
            _exportTask = null;
            cancel.Dispose();
            ExportOverlay.Visibility = Visibility.Collapsed;
            SetBusy(false);
        }
    }

    private void ExportCancel_Click(object sender, RoutedEventArgs e) => _export?.Cancel();

    // While exporting, nothing that would change what is exported.
    private void SetBusy(bool busy)
    {
        foreach (var element in new UIElement[] { ToolRail, EditPanel, LayoutPanel, VideoBar, TabBar, ExportButton, CopyButton, NewCaptureButton })
            element.IsEnabled = !busy;
        Stage.IsHitTestVisible = !busy;
        if (!busy)
            ShowState();
    }

    private string VideoFileName => FileNaming.Expand(App.Settings.EffectiveVideoFileNameTemplate, _takenAt);

    private async void SaveVideo() => await SaveVideoAsync();

    /// <summary>The save dialog, then the video (or a picture of the frame) written; true when written.</summary>
    private async Task<bool> SaveVideoAsync()
    {
        if (!IsTimed || _export is not null)
            return false;
        StopPlayback();
        string folder = App.Settings.EffectiveVideoExportFolder;
        try
        {
            Directory.CreateDirectory(folder);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
        var dialog = new SaveFileDialog
        {
            Title = Strings.Get("Save"),
            Filter = $"{Strings.Get("FilterMp4")}|*.mp4|{Strings.Get("FilterGif")}|*.gif|" +
                $"{Strings.Format("FilterThisFrame", Strings.Get("FilterPng"))}|*.png|" +
                $"{Strings.Format("FilterThisFrame", Strings.Get("FilterJpeg"))}|*.jpg;*.jpeg|" +
                $"{Strings.Format("FilterThisFrame", Strings.Get("FilterWebp"))}|*.webp",
            // The format chosen in the Export window first.
            FilterIndex = SavesGif ? 2 : SavesFrame ? 3 + (int)FrameFormat : 1,
            FileName = VideoFileName + (SavesGif ? ".gif" : SavesFrame ? Composer.Extension(FrameFormat) : ".mp4"),
            InitialDirectory = folder,
            AddExtension = true,
            DefaultExt = SavesGif ? ".gif" : SavesFrame ? Composer.Extension(FrameFormat) : ".mp4",
        };
        if (dialog.ShowDialog(this) != true)
            return false;
        string extension = Path.GetExtension(dialog.FileName).ToLowerInvariant();
        if (extension is ".mp4" or ".gif")
        {
            // The one chosen in the dialog becomes the format of the next ones, as for pictures.
            App.Settings.VideoFormat = extension == ".gif" ? VideoFormat.Gif : VideoFormat.Mp4;
            ShowExportFormat();
            if (!await ExportVideo(dialog.FileName, gif: extension == ".gif"))
                return false;
            _videoKept = true;
            ShowSaved(dialog.FileName, capture: false);
            return true;
        }
        // A picture of the frame shown.
        var format = extension switch
        {
            ".jpg" or ".jpeg" => ExportFormat.Jpeg,
            ".webp" => ExportFormat.Webp,
            _ => ExportFormat.Png,
        };
        try
        {
            using var picture = RenderFull();
            File.WriteAllBytes(dialog.FileName, Composer.Encode(picture, format));
            ShowSaved(dialog.FileName, capture: false);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _ = ShowMessageAsync(Strings.Format("ErrorSave", ex.Message));
            return false;
        }
    }

    // The GIF made as shown, in the temp folder, then on the clipboard as a file (chats take it so, and it moves).
    private async void CopyGif()
    {
        if (!IsTimed || _export is not null)
            return;
        try
        {
            Directory.CreateDirectory(ClipActions.TempFolder);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return;
        }
        string path = FileNaming.UniquePath(ClipActions.TempFolder, VideoFileName, ".gif");
        if (!await ExportVideo(path, gif: true))
            return;
        bool copied = ClipActions.CopyFile(path);
        ShowStatus(Strings.Get(copied ? "GifCopied" : "CopyFailed"));
    }
}
