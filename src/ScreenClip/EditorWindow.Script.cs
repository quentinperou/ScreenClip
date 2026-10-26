#if DEBUG
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using ScreenClip.Core.Annotations;
using ScreenClip.Core.Video;
using SkiaSharp;

namespace ScreenClip;

/// <summary>
/// Debug builds only: plays a script of pointer actions in the Edit tab (<c>SCREENCLIP_EDIT_SCRIPT=&lt;file&gt;</c>), so
/// that drawing can be checked without taking the mouse of the person at the PC (WPF ignores posted mouse buttons).
/// One command per line, points in the capture's pixels:
/// <c>tab edit|layout</c>, <c>tool Arrow</c>, <c>drag x1 y1 x2 y2 [shift]</c>, <c>path x1 y1 x2 y2 …</c>,
/// <c>click x y [count]</c>, <c>type text with \n</c>, <c>color Blue</c>, <c>key Escape|Delete|Enter</c>,
/// <c>undo</c>, <c>redo</c>, <c>wait ms</c>, <c>shot file.png</c> (the window), <c>export file.png</c> (the picture);
/// a video: <c>seek s</c>, <c>trim s1 s2</c>, <c>play</c>, <c>pause</c>, <c>exportvideo file.mp4|gif</c>,
/// <c>track row s1 s2</c> (a press at s1 on a track, dragged to s2), <c>cursor</c> (the recorded pointer chosen),
/// <c>cursorstyle Windows</c>, <c>cursorsize 200</c>, <c>cursorsmooth on</c>, <c>cursorclick Ripple</c>,
/// <c>motion on|off</c>, <c>state</c> (the annotations and their times, in the log);
/// <c>copydata file</c> (what Copy would put on the clipboard, written to the file; the clipboard untouched),
/// <c>leave file</c> (asks as a recording kept nowhere leaves; the answer, True or False, written to the file),
/// <c>dialog n</c> (presses the dialog's button n), <c>dialogkey Escape|Enter</c>, <c>traymenu file</c>,
/// <c>settingsshot file</c> (Settings › Saving);
/// <c>done</c> is written next to the script at the end.
/// </summary>
public partial class EditorWindow
{
    private async void RunScript(string path)
    {
        await Task.Delay(1200);
        foreach (string raw in File.ReadAllLines(path))
        {
            string line = raw.Trim();
            if (line.Length == 0 || line.StartsWith('#'))
                continue;
            string[] parts = line.Split(' ', 2);
            string command = parts[0], rest = parts.Length > 1 ? parts[1] : "";
            float[] numbers = rest.Split(' ', StringSplitOptions.RemoveEmptyEntries)
                .Select(n => float.TryParse(n, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float v) ? v : float.NaN)
                .Where(v => !float.IsNaN(v)).ToArray();
            try
            {
                switch (command)
                {
                    case "tab":
                        SetTab(rest == "edit");
                        break;
                    case "tool":
                        SetTool(Enum.Parse<EditTool>(rest));
                        break;
                    case "drag":
                    {
                        bool shift = rest.EndsWith("shift", StringComparison.Ordinal);
                        await ScriptStroke([new SKPoint(numbers[0], numbers[1]), new SKPoint(numbers[2], numbers[3])], shift, steps: 10);
                        break;
                    }
                    case "path":
                    {
                        var points = Enumerable.Range(0, numbers.Length / 2).Select(i => new SKPoint(numbers[2 * i], numbers[2 * i + 1])).ToArray();
                        await ScriptStroke(points, false, steps: 4);
                        break;
                    }
                    case "click":
                        await Settle();
                        PointerDown(ToView(new SKPoint(numbers[0], numbers[1])), numbers.Length > 2 ? (int)numbers[2] : 1, capture: false);
                        await Settle();
                        EndDrag();
                        break;
                    case "type":
                        await Settle();
                        if (_textEditor is { } box)
                            box.Text = rest.Replace("\\n", "\n");
                        break;
                    case "color":
                    {
                        uint color = Palette.All.First(c => c.Name == rest).Color;
                        ChangeOption(a => a with { Color = color }, "color");
                        break;
                    }
                    case "style":
                        if (Enum.TryParse<ArrowStyle>(rest, out var arrow))
                            ChangeOption(a => a with { Arrow = arrow }, "style");
                        else if (Enum.TryParse<ShapeStyle>(rest, out var shape))
                            ChangeOption(a => a with { Shape = shape }, "style");
                        else if (Enum.TryParse<TextStyle>(rest, out var text))
                            ChangeOption(a => a with { TextStyle = text }, "style");
                        else if (Enum.TryParse<MaskStyle>(rest, out var mask))
                            ChangeOption(a => a with { Mask = mask }, "style");
                        else if (Enum.TryParse<PointerStyle>(rest, out var pointer))
                            ChangeOption(a => a with { Pointer = pointer }, "style");
                        break;
                    case "key":
                        if (rest == "Escape" && _textEditor is not null)
                            CommitText();
                        else
                            ScriptKey(rest);
                        break;
                    case "zoom":
                        SetZoom(rest == "fit" ? null : numbers[0] / 100);
                        break;
                    case "play":
                        StartPlayback();
                        break;
                    case "pause":
                        StopPlayback();
                        break;
                    case "seek":
                        RequestFrame(TimeSpan.FromSeconds(numbers[0]));
                        break;
                    case "trim":
                        _trim = TrimRange.All(_duration).WithEnd(TimeSpan.FromSeconds(numbers[1]), _duration).WithStart(TimeSpan.FromSeconds(numbers[0]));
                        RequestFrame(_trim.Start);
                        RequestRender();
                        break;
                    case "exportvideo":
                        await ExportVideo(rest, gif: rest.EndsWith(".gif", StringComparison.OrdinalIgnoreCase));
                        break;
                    case "track":
                    {
                        await Settle();
                        double y = numbers[0] * (TrackHeight + TrackGap) + TrackHeight / 2;
                        var from = new Point(XOf(TimeSpan.FromSeconds(numbers[1])), y);
                        var to = new Point(XOf(TimeSpan.FromSeconds(numbers[2])), y);
                        if (TrackDown(from))
                        {
                            for (int step = 1; step <= 10; step++)
                            {
                                TrackMove(new Point(from.X + (to.X - from.X) * step / 10, y), pressed: true);
                                await Task.Delay(15);
                            }
                            TrackUp();
                        }
                        break;
                    }
                    case "cursor":
                        SelectCursor();
                        break;
                    case "reorder":
                    {
                        // A row of the list dragged to the slot given, through the mouse code: the row takes the mouse
                        // as a button does when pressed, then the list takes it from the row.
                        await Settle();
                        var rows = AnnotationRows();
                        var row = rows[(int)numbers[0]];
                        double y(int slot) => slot < rows.Count ? rows[slot].TranslatePoint(new Point(0, 2), AnnotationList).Y : AnnotationList.ActualHeight - 2;
                        var from = row.TranslatePoint(new Point(20, row.ActualHeight / 2), AnnotationList);
                        row.CaptureMouse();
                        ListPress(from, row);
                        for (int step = 1; step <= 5; step++)
                        {
                            ListDrag(new Point(20, from.Y + (y((int)numbers[1]) - from.Y) * step / 5));
                            await Task.Delay(15);
                        }
                        ListDrop();
                        break;
                    }
                    case "exportshot":
                    {
                        // The window while a video is exported, then the export cancelled.
                        Directory.CreateDirectory(Services.ClipActions.TempFolder);
                        var exporting = ExportVideo(Path.Combine(Services.ClipActions.TempFolder, "exportshot.mp4"), gif: false);
                        await Task.Delay(500);
                        await Settle();
                        SaveWindowPicture(rest);
                        _export?.Cancel();
                        await exporting;
                        break;
                    }
                    case "preset":
                        Apply(Core.Composition.StylePresets.ApplyTo(Core.Composition.StylePresets.All.First(p => p.Id == rest).Style, _style));
                        break;
                    case "exportsize":
                    {
                        // What Save and Copy make from the tab shown.
                        await Settle();
                        using var picture = RenderFull();
                        File.AppendAllText(path + ".log", $"export from the {(_editTab ? "edit" : "layout")} tab: {picture.Width} x {picture.Height}, capture {_capture!.Width} x {_capture.Height}\n");
                        break;
                    }
                    case "eye":
                    {
                        // The eye of a row of the list (0: the top one), clicked.
                        await Settle();
                        var row = (System.Windows.Controls.RadioButton)AnnotationList.Children[(int)numbers[0]];
                        var eye = ((System.Windows.Controls.DockPanel)row.Content).Children.OfType<System.Windows.Controls.Button>().First();
                        eye.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
                        break;
                    }
                    case "toast":
                        ShowSaved(rest, capture: false);
                        break;
                    case "hover":
                        await Settle();
                        PointerMove(ToView(new SKPoint(numbers[0], numbers[1])), pressed: false, shift: false);
                        break;
                    case "hitball":
                    {
                        // What a click on the playhead's ball reaches: the timeline, to grab the playhead.
                        await Settle();
                        var ball = Timeline.TranslatePoint(new Point(XOf(_playhead), 4), this);
                        var hit = VisualTreeHelper.HitTest(this, ball)?.VisualHit as DependencyObject;
                        bool timeline = hit is not null && (hit == Timeline || Timeline.IsAncestorOf(hit));
                        File.AppendAllText(path + ".log", $"ball at {ball.X:0},{ball.Y:0}: {hit?.GetType().Name}, the timeline {timeline}\n");
                        break;
                    }
                    case "format" when rest is "png" or "jpg":
                        App.Settings.VideoFormat = Services.VideoFormat.Frame;
                        Apply(_style with { Format = rest == "jpg" ? Core.Composition.ExportFormat.Jpeg : Core.Composition.ExportFormat.Png });
                        ShowExportFormat();
                        break;
                    case "format":
                        App.Settings.VideoFormat = Enum.Parse<Services.VideoFormat>(rest, ignoreCase: true);
                        ShowExportFormat();
                        RequestRender();
                        break;
                    case "exportlayout":
                        App.Settings.ExportLayout = rest == "on";
                        RequestRender();
                        break;
                    case "exportwindow":
                        // The Export window open, for a shot of the editor with it (it is a popup of its own).
                        ExportButton_Click(this, new RoutedEventArgs());
                        await Task.Delay(400);
                        break;
                    case "exportwindowshot":
                    {
                        // The Export window alone: its own visual, as a picture.
                        await Settle();
                        var card = (FrameworkElement)ExportPopup.Child;
                        var dpi = VisualTreeHelper.GetDpi(this);
                        var bitmap = new RenderTargetBitmap((int)(card.ActualWidth * dpi.DpiScaleX), (int)(card.ActualHeight * dpi.DpiScaleY), 96 * dpi.DpiScaleX, 96 * dpi.DpiScaleY, PixelFormats.Pbgra32);
                        bitmap.Render(card);
                        var encoder = new PngBitmapEncoder();
                        encoder.Frames.Add(BitmapFrame.Create(bitmap));
                        using (var stream = File.Create(rest))
                            encoder.Save(stream);
                        ExportPopup.IsOpen = false;
                        break;
                    }
                    case "scrub":
                    {
                        // The playhead dragged from s1 to s2 in ms milliseconds, a mouse move every 16 ms, as a hand
                        // does: how many different frames were shown on the way.
                        await Settle();
                        int before = _framesShown, steps = Math.Max(1, (int)(numbers[2] / 16));
                        var watch = System.Diagnostics.Stopwatch.StartNew();
                        _grip = TimelineGrip.Playhead;
                        for (int i = 0; i <= steps; i++)
                        {
                            MoveGrip(XOf(TimeSpan.FromSeconds(numbers[0] + (numbers[1] - numbers[0]) * i / steps)));
                            await Task.Delay(16);
                        }
                        _grip = TimelineGrip.None;
                        double moving = watch.Elapsed.TotalMilliseconds;
                        await Task.Delay(300);
                        File.AppendAllText(path + ".log", $"scrub {numbers[0]}-{numbers[1]} s in {moving:0} ms: {_framesShown - before} frames shown for {steps + 1} moves "
                            + $"({(_framesShown - before) * 1000 / moving:0} a second)\n");
                        break;
                    }
                    case "seekbench":
                    {
                        // How long the frame at a time takes to read: random times, then small steps forward and back.
                        var clip = _videoClip!;
                        var random = new Random(1);
                        var lines = new List<string>();
                        foreach (bool hardware in new[] { false, true })
                        await Task.Run(() =>
                        {
                            Video.VideoReader.Hardware = hardware;
                            lines.Add(hardware ? "graphics card:" : "processor:");
                            using var reader = new Video.VideoReader(clip.Path);
                            var watch = new System.Diagnostics.Stopwatch();
                            void Time(string label, Func<TimeSpan> next, int count)
                            {
                                var times = new List<double>();
                                for (int i = 0; i < count; i++)
                                {
                                    var at = next();
                                    watch.Restart();
                                    using var frame = reader.FrameAt(at);
                                    times.Add(watch.Elapsed.TotalMilliseconds);
                                }
                                times.Sort();
                                lines.Add($"{label}: median {times[times.Count / 2]:0} ms, max {times[^1]:0} ms, mean {times.Average():0} ms");
                            }
                            double seconds = reader.Duration.TotalSeconds;
                            Time("random", () => TimeSpan.FromSeconds(random.NextDouble() * seconds), 20);
                            double forward = 1;
                            Time("forward 1/20 s", () => TimeSpan.FromSeconds(forward += 0.05), 40);
                            double back = seconds - 0.5;
                            Time("back 1/20 s", () => TimeSpan.FromSeconds(back -= 0.05), 40);
                            // The colors against Media Foundation's own RGB, on a frame in the middle.
                            var middle = TimeSpan.FromSeconds(seconds / 2);
                            using var ours = reader.FrameAt(middle);
                            Video.VideoReader.ForceRgb32 = true;
                            using var theirs = new Video.VideoReader(clip.Path);
                            Video.VideoReader.ForceRgb32 = false;
                            using var reference = theirs.FrameAt(middle);
                            using var a = SKBitmap.FromImage(ours!);
                            using var b = SKBitmap.FromImage(reference!);
                            long total = 0, worst = 0;
                            var pa = a.GetPixelSpan();
                            var pb = b.GetPixelSpan();
                            for (int i = 0; i < pa.Length; i++)
                            {
                                if (i % 4 == 3)
                                    continue;
                                int d = Math.Abs(pa[i] - pb[i]);
                                total += d;
                                worst = Math.Max(worst, d);
                            }
                            lines.Add($"colors against Media Foundation: mean {total / (pa.Length * 0.75):0.00}, worst {worst}");
                        });
                        File.AppendAllText(path + ".log", $"seekbench {_capture!.Width} x {_capture.Height}, {_duration.TotalSeconds:0.0} s\n" + string.Concat(lines.Select(l => "  " + l + "\n")));
                        break;
                    }
                    case "renderbench":
                    {
                        // How long the picture takes to draw once the frame is there (the Edit view or the Layout tab).
                        var watch = System.Diagnostics.Stopwatch.StartNew();
                        int count = (int)numbers[0];
                        for (int i = 0; i < count; i++)
                        {
                            var copy = SKImage.FromBitmap(SKBitmap.FromImage(_capture!));
                            ShowFrame(copy);
                            _renderQueued = false;
                            RenderPreview();
                        }
                        File.AppendAllText(path + ".log", $"renderbench {(_editTab ? "edit" : "layout")}: {watch.Elapsed.TotalMilliseconds / count:0.0} ms a frame\n");
                        break;
                    }
                    case "cursorstyle":
                        ChangeCursor(o => o with { Style = Enum.Parse<RecordedPointerStyle>(rest) }, "style");
                        break;
                    case "cursorsize":
                        ChangeCursor(o => o with { Size = (int)numbers[0] }, "size");
                        break;
                    case "cursorsmooth":
                        ChangeCursor(o => o with { Smooth = rest == "on" }, "smooth");
                        break;
                    case "cursorclick":
                        ChangeCursor(o => o with { Click = Enum.Parse<ClickEffect>(rest) }, "click");
                        break;
                    case "motion":
                        MotionSwitch.IsChecked = rest == "on";
                        break;
                    case "state":
                    {
                        var lines = _history.Current.Items.Select(a =>
                            $"#{a.Id} {a.Kind} at {a.Start.X:0},{a.Start.Y:0} time {(a.Time is { } t ? $"{t.Start.TotalSeconds:0.00}-{t.End.TotalSeconds:0.00}" : "all")}"
                            + (a.Motion is { } m ? $" motion from {m.From.X:0},{m.From.Y:0} {m.Leave.TotalSeconds:0.00}-{m.Arrive.TotalSeconds:0.00} click {m.Click}" : ""));
                        var cursor = _history.Current.Cursor;
                        File.AppendAllText(path + ".log", $"state: duration {_duration.TotalSeconds:0.00}, playhead {_playhead.TotalSeconds:0.00}, cursor {cursor.Style} {cursor.Size}% smooth {cursor.Smooth} click {cursor.Click}, "
                            + $"track {(Track is { } track ? $"{track.Samples.Count} samples, {track.Shapes.Count} shapes, {track.Clicks.Count} clicks" : "none")}\n"
                            + string.Concat(lines.Select(l => "  " + l + "\n")));
                        break;
                    }
                    case "undo":
                        Undo();
                        break;
                    case "redo":
                        Redo();
                        break;
                    case "wait":
                        await Task.Delay((int)numbers[0]);
                        break;
                    case "copydata":
                    {
                        await Settle();
                        var data = CopyData(App.Settings.CopyFormat);
                        var lines = data.GetFormats(false).ToList();
                        if (data.GetDataPresent(DataFormats.FileDrop))
                            lines.AddRange(data.GetFileDropList().Cast<string>().Select(f => "file " + f + " " + new FileInfo(f).Length));
                        File.WriteAllLines(rest, lines);
                        break;
                    }
                    case "leave":
                    {
                        string answerFile = rest;
                        _ = LeaveVideo().ContinueWith(t => File.WriteAllText(answerFile, t.Result.ToString()),
                            TaskScheduler.FromCurrentSynchronizationContext());
                        break;
                    }
                    case "dialog":
                        CloseDialog((int)numbers[0]);
                        break;
                    case "settingsshot":
                    {
                        // Settings › Saving, its own window as a picture, then closed.
                        App.OpenSettings(SettingsPage.Saving);
                        await Task.Delay(800);
                        var settings = Application.Current.Windows.OfType<SettingsWindow>().First();
                        var content = (FrameworkElement)settings.Content;
                        var dpi = VisualTreeHelper.GetDpi(settings);
                        var bitmap = new RenderTargetBitmap((int)(content.ActualWidth * dpi.DpiScaleX), (int)(content.ActualHeight * dpi.DpiScaleY), 96 * dpi.DpiScaleX, 96 * dpi.DpiScaleY, PixelFormats.Pbgra32);
                        var background = new DrawingVisual();
                        using (var context = background.RenderOpen())
                        {
                            context.DrawRectangle((Brush)FindResource("SolidBackgroundFillColorBaseBrush"), null, new Rect(0, 0, content.ActualWidth, content.ActualHeight));
                            context.DrawRectangle(new VisualBrush(content), null, new Rect(0, 0, content.ActualWidth, content.ActualHeight));
                        }
                        bitmap.Render(background);
                        var encoder = new PngBitmapEncoder();
                        encoder.Frames.Add(BitmapFrame.Create(bitmap));
                        using (var stream = File.Create(rest))
                            encoder.Save(stream);
                        settings.Close();
                        break;
                    }
                    case "traymenu":
                    {
                        // The notification menu, laid out apart (opening it would need the foreground).
                        var menu = TrayMenu.Build();
                        menu.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
                        menu.Arrange(new Rect(menu.DesiredSize));
                        menu.UpdateLayout();
                        await Settle();
                        var dpi = VisualTreeHelper.GetDpi(this);
                        var bitmap = new RenderTargetBitmap((int)(menu.ActualWidth * dpi.DpiScaleX), (int)(menu.ActualHeight * dpi.DpiScaleY), 96 * dpi.DpiScaleX, 96 * dpi.DpiScaleY, PixelFormats.Pbgra32);
                        bitmap.Render(menu);
                        var encoder = new PngBitmapEncoder();
                        encoder.Frames.Add(BitmapFrame.Create(bitmap));
                        using (var stream = File.Create(rest))
                            encoder.Save(stream);
                        break;
                    }
                    case "dialogkey":
                        if (_dialog is not null)
                            CloseDialog(rest == "Escape" ? _dialogCancel : 0);
                        break;
                    case "shot":
                        await Settle();
                        SaveWindowPicture(rest);
                        break;
                    case "export":
                    {
                        await Settle();
                        using var picture = RenderFull();
                        File.WriteAllBytes(rest, Core.Composition.Composer.Encode(picture, Core.Composition.ExportFormat.Png));
                        break;
                    }
                }
            }
            catch (Exception ex)
            {
                File.AppendAllText(path + ".log", $"{line}: {ex}\n");
            }
            await Settle();
        }
        File.WriteAllText(path + ".done", "");
    }

    private async Task ScriptStroke(SKPoint[] points, bool shift, int steps)
    {
        await Settle();
        PointerDown(ToView(points[0]), 1, capture: false);
        for (int i = 1; i < points.Length; i++)
        {
            for (int step = 1; step <= steps; step++)
            {
                float t = (float)step / steps;
                var p = new SKPoint(points[i - 1].X + (points[i].X - points[i - 1].X) * t, points[i - 1].Y + (points[i].Y - points[i - 1].Y) * t);
                PointerMove(ToView(p), pressed: true, shift);
                await Task.Delay(15);
            }
        }
        await Settle();
        EndDrag();
    }

    private void ScriptKey(string key)
    {
        switch (key)
        {
            case "Escape" when _selectedId is not null:
                Select(null);
                break;
            case "Escape":
                SetTool(EditTool.Select);
                break;
            case "Delete":
                DeleteSelected();
                break;
            case "Enter" when _tool == EditTool.Crop:
                SetTool(EditTool.Select);
                break;
        }
    }

    // Lets the queued drawing happen before the next step.
    private Task Settle() => Dispatcher.InvokeAsync(() => { }, System.Windows.Threading.DispatcherPriority.ContextIdle).Task;

    private void SaveWindowPicture(string file)
    {
        var dpi = VisualTreeHelper.GetDpi(this);
        var content = (FrameworkElement)Content;
        var bitmap = new RenderTargetBitmap((int)(content.ActualWidth * dpi.DpiScaleX), (int)(content.ActualHeight * dpi.DpiScaleY), 96 * dpi.DpiScaleX, 96 * dpi.DpiScaleY, PixelFormats.Pbgra32);
        var background = new DrawingVisual();
        using (var context = background.RenderOpen())
        {
            context.DrawRectangle((Brush)FindResource("SolidBackgroundFillColorBaseBrush"), null, new Rect(0, 0, content.ActualWidth, content.ActualHeight));
            context.DrawRectangle(new VisualBrush(content), null, new Rect(0, 0, content.ActualWidth, content.ActualHeight));
        }
        bitmap.Render(background);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = File.Create(file);
        encoder.Save(stream);
    }
}
#endif
