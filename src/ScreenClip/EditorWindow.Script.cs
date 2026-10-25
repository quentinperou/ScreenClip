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
                    case "undo":
                        Undo();
                        break;
                    case "redo":
                        Redo();
                        break;
                    case "wait":
                        await Task.Delay((int)numbers[0]);
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
