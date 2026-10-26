using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using ScreenClip.Core.Annotations;
using ScreenClip.Core.Video;
using ScreenClip.Video;
using SkiaSharp;

namespace ScreenClip;

/// <summary>
/// Step 3 of the video (asked by the author): its pieces and its sounds. The timeline is split at the playhead
/// (✂, Ctrl+B) and a piece chosen there is removed (Delete): it stays in place, greyed, with ↺ to put it back, and
/// playing and the export skip it. Under the tracks, each sound (the computer and the microphone apart, or the video's
/// one sound) has a row: its waveform, its bar whose ends cut it at the start or the end, its fades; chosen, its volume,
/// fades and mute are in the Edit panel. All of it is in the layer, so that it is undone as the rest.
/// </summary>
public partial class EditorWindow
{
    private const double SoundHeight = 30, SoundGap = 3;
    private static readonly TimeSpan PeakStep = TimeSpan.FromMilliseconds(10);

    private enum SoundGrip { Start, End }

    private sealed record SoundDrag(int Row, SoundGrip Grip, AnnotationLayer Base);

    private IReadOnlyList<(int? Track, SoundKind Kind)> _soundRows = [];
    private float[]?[] _peaks = [];               // each sound's loudest level every 10 ms, read in the background
    private int _soundSelected = -1;              // the sound row chosen (its options in the Edit panel)
    private SoundDrag? _soundDrag;
    private bool _pieceChosen;                    // a piece of the video chosen on the timeline: framed, removed by Delete
    private TimeSpan _pieceAt;                    // where a sound row or a track was clicked: the piece Delete acts on

    private VideoEdit Edit => Layer.Video;

    // ---------------------------------------------------------------- Pieces

    private void Split_Click(object sender, RoutedEventArgs e) => SplitHere();

    private void RemovePiece_Click(object sender, RoutedEventArgs e) => RemovePieceHere();

    /// <summary>Splits the video at the playhead (refused near a split or an end).</summary>
    private void SplitHere()
    {
        if (!IsVideo || _export is not null)
            return;
        StopPlayback();
        var edit = Edit.SplitAt(_playhead, _duration);
        if (ReferenceEquals(edit, Edit))
        {
            ShowStatus(Strings.Get("SplitRefused"));
            return;
        }
        _pieceChosen = true;
        CommitVideo(edit);
    }

    /// <summary>Removes the piece at the playhead (not the last one kept).</summary>
    private void RemovePieceHere()
    {
        if (!IsVideo || _export is not null || Edit.Splits.Count == 0)
            return;
        StopPlayback();
        if (Edit.IsRemoved(_playhead))
        {
            CommitVideo(Edit.RestoreAt(_playhead));
            return;
        }
        var edit = Edit.RemoveAt(_playhead, _trim, _duration);
        if (ReferenceEquals(edit, Edit))
        {
            ShowStatus(Strings.Get("RemovePieceRefused"));
            return;
        }
        CommitVideo(edit);
    }

    private void CommitVideo(VideoEdit edit, string? key = null) => Commit(_history.Current with { Video = edit }, key);

    /// <summary>The piece at the playhead chosen (a click on the frames): its frame and zoom in the Edit panel.</summary>
    private void ChoosePiece()
    {
        _pieceChosen = true;
        _selectedId = null;
        _cursorSelected = false;
        _soundSelected = -1;
        if (_tool != EditTool.Select)
            SetTool(EditTool.Select);
        ShowEditPanel();
        ShowTracks();
        ShowSounds();
        RequestRender();
    }

    // ---------------------------------------------------------------- A piece framed (zoomed in)

    /// <summary>How the piece at the playhead is framed (all of it when not zoomed), its range being that piece.</summary>
    private Framing PieceFraming(AnnotationLayer layer)
    {
        var piece = layer.Video.PieceAt(_playhead, _duration);
        return layer.Video.FramingAt(_playhead) is { } framing ? framing with { Range = piece } : new Framing(piece, 1, 0.5, 0.5);
    }

    // The part shown, in the capture's pixels (inside the crop).
    private SKRect FramingRegion(AnnotationLayer layer)
    {
        var kept = layer.Visible(_capture!.Width, _capture.Height);
        var region = PieceFraming(layer).Region(kept.Width, kept.Height);
        region.Offset(kept.X, kept.Y);
        return region;
    }

    private Rect FramingRect(AnnotationLayer layer)
    {
        var region = FramingRegion(layer);
        return new Rect(ToView(new SKPoint(region.Left, region.Top)), ToView(new SKPoint(region.Right, region.Bottom)));
    }

    // A corner of the frame zooms, its inside moves it.
    private DragMode? FramingGripAt(SKPoint point)
    {
        if (_capture is null)
            return null;
        var region = FramingRegion(Layer);
        float reach = Tolerance(8);
        foreach (var corner in new[] { new SKPoint(region.Left, region.Top), new SKPoint(region.Right, region.Top), new SKPoint(region.Left, region.Bottom), new SKPoint(region.Right, region.Bottom) })
        {
            if (Core.Annotations.AnnotationGeometry.Distance(corner, point) <= reach)
                return DragMode.FramingZoom;
        }
        return region.Contains(point) ? DragMode.FramingMove : null;
    }

    private void ShowPiecePanel()
    {
        var framing = PieceFraming(Layer);
        _syncingPiece = true;
        PieceZoomSlider.Value = Math.Round(framing.Zoom * 100);
        PieceZoomValue.Text = Strings.Format("EditorPercent", (int)Math.Round(framing.Zoom * 100));
        _syncingPiece = false;
        bool split = Edit.Splits.Count > 0;
        PieceRemoveButton.Visibility = Show(split);
        bool removed = Edit.IsRemoved(_playhead);
        PieceRemoveGlyph.Text = removed ? "\uE7A7" : "\uE74D";
        PieceRemoveText.Text = Strings.Get(removed ? "MenuRestorePiece" : "MenuRemovePiece");
        PieceResetButton.IsEnabled = framing.IsZoomed;
    }

    private bool _syncingPiece;

    private void PieceZoom_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (!_initialized || _syncingPiece || !IsVideo || !_pieceChosen)
            return;
        double zoom = Math.Round(PieceZoomSlider.Value) / 100;
        PieceZoomValue.Text = Strings.Format("EditorPercent", (int)Math.Round(zoom * 100));
        var framing = PieceFraming(_history.Current);
        CommitVideo(Edit.WithFraming(framing.Range, zoom, framing.X, framing.Y), "pieceZoom" + framing.Range.Start.Ticks);
    }

    private void PieceReset_Click(object sender, RoutedEventArgs e)
    {
        if (!IsVideo)
            return;
        var framing = PieceFraming(_history.Current);
        CommitVideo(Edit.WithFraming(framing.Range, 1, 0.5, 0.5));
    }

    // ---------------------------------------------------------------- A sound silenced in a piece

    /// <summary>The sound <paramref name="row"/> silent in the piece at <paramref name="time"/>, or heard there again.</summary>
    private void ToggleSilence(int row, TimeSpan time)
    {
        if (!IsVideo || _export is not null || row < 0)
            return;
        StopPlayback();
        var options = Edit.Sound(row);
        var piece = Edit.PieceAt(time, _duration);
        CommitVideo(Edit.WithSound(row, options.WithSilenced(piece, !options.IsSilencedAt(time))));
    }

    private static System.Windows.Shapes.Rectangle SplitLine(double height) =>
        new() { Width = 2, Height = height, Fill = new SolidColorBrush(Color.FromArgb(0xE0, 0xFF, 0xFF, 0xFF)), RadiusX = 1, RadiusY = 1, IsHitTestVisible = false };

    // ---------------------------------------------------------------- Menus (a right click on the frames, a track, a sound)

    private void Timeline_MouseRightButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (!IsVideo || _export is not null)
            return;
        var time = TimeAt(e.GetPosition(Timeline).X);
        ShowVideoMenu(Timeline, time, soundRow: -1, annotation: null);
        e.Handled = true;
    }

    private void TrackCanvas_MouseRightButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (!IsTimed || _export is not null)
            return;
        var at = e.GetPosition(TrackCanvas);
        var annotation = TrackRowAt(at, out bool cursorRow);
        ShowVideoMenu(TrackCanvas, TimeAt(at.X), soundRow: -1, annotation, cursorRow);
        e.Handled = true;
    }

    private void SoundCanvas_MouseRightButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (!IsVideo || _export is not null)
            return;
        var at = e.GetPosition(SoundCanvas);
        ShowVideoMenu(SoundCanvas, TimeAt(at.X), SoundRowAt(at.Y), annotation: null);
        e.Handled = true;
    }

    // What can be done there: split, remove or put back the piece, frame it; a sound: silence it in the piece, mute it;
    // a track: hide or show its layer, as its eye in the list (asked by the author), delete it. A picture animated has
    // its tracks only.
    private ContextMenu? ShowVideoMenu(FrameworkElement target, TimeSpan time, int soundRow, Annotation? annotation, bool cursorRow = false)
    {
        StopPlayback();
        var menu = new ContextMenu { PlacementTarget = target };
        // An icon: a glyph of the icon font, or a drawing of its own (the Crop tool's, as on the tool rail).
        void Add(string text, object icon, Action action, bool enabled = true)
        {
            var item = new MenuItem
            {
                Header = text,
                IsEnabled = enabled,
                Icon = icon as UIElement ?? new TextBlock { Text = (string)icon, FontFamily = new FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets"), FontSize = 14 },
            };
            item.Click += (_, _) => action();
            menu.Items.Add(item);
        }
        var edit = Edit;
        // The pieces and the sounds are a video's; a picture animated has its tracks only.
        if (IsVideo)
        {
            Add(Strings.Get("MenuSplitHere"), "\uE8C6", () =>
            {
                RequestFrame(time);
                SplitHere();
            });
            if (edit.Splits.Count > 0)
            {
                bool removed = edit.IsRemoved(time);
                Add(Strings.Get(removed ? "MenuRestorePiece" : "MenuRemovePiece"), removed ? "\uE7A7" : "\uE74D", () =>
                {
                    RequestFrame(time);
                    RemovePieceHere();
                });
            }
            if (soundRow >= 0)
            {
                var options = edit.Sound(soundRow);
                bool silenced = options.IsSilencedAt(time);
                menu.Items.Add(new Separator());
                Add(Strings.Get(silenced ? "MenuUnsilencePiece" : "MenuSilencePiece"), silenced ? SoundGlyph(_soundRows[soundRow].Kind) : "\uE74F", () => ToggleSilence(soundRow, time));
                Add(Strings.Get(options.Muted ? "SoundUnmute" : "SoundMute"), options.Muted ? SoundGlyph(_soundRows[soundRow].Kind) : "\uE74F", () => ToggleMute(soundRow));
            }
            else
            {
                menu.Items.Add(new Separator());
                bool zoomed = edit.FramingAt(time) is { IsZoomed: true };
                var cropIcon = ToolPath(Tools.First(t => t.Tool == EditTool.Crop).Icon, 18);
                cropIcon.Stroke = (Brush)FindResource("TextFillColorPrimaryBrush");
                Add(Strings.Get("MenuFramePiece"), cropIcon, () =>
                {
                    RequestFrame(time);
                    if (!_editTab)
                        SetTab(true);
                    ChoosePiece();
                });
                if (zoomed)
                    Add(Strings.Get("MenuResetFraming"), "\uE9A6", () => CommitVideo(Edit.WithFraming(Edit.PieceAt(time, _duration), 1, 0.5, 0.5)));
            }
        }
        if (annotation is not null)
        {
            if (menu.Items.Count > 0)
                menu.Items.Add(new Separator());
            string name = AnnotationName(annotation);
            Add(Strings.Format(annotation.Hidden ? "LayerShow" : "LayerHide", name), annotation.Hidden ? "\uE7B3" : "\uED1A",
                () => Commit(_history.Current.Replace(annotation with { Hidden = !annotation.Hidden })));
            Add(Strings.Get("AnnotDelete"), "\uE74D", () => Commit(_history.Current.Remove(annotation.Id)));
        }
        else if (cursorRow)
        {
            if (menu.Items.Count > 0)
                menu.Items.Add(new Separator());
            bool hidden = _history.Current.Cursor.Hidden;
            Add(Strings.Format(hidden ? "LayerShow" : "LayerHide", Strings.Get("RecordedPointer")), hidden ? "\uE7B3" : "\uED1A",
                () => ChangeCursor(o => o with { Hidden = !o.Hidden }, "hidden"));
        }
        if (menu.Items.Count == 0)
            return null;
        menu.IsOpen = true;
        return menu;
    }

    // The ↺ of a removed piece under the pointer on the timeline, if any: where to put it back.
    private Piece? RestoreButtonAt(double x, double y)
    {
        double middle = (Timeline.ActualHeight + 10) / 2;
        foreach (var piece in Edit.Removed)
        {
            double center = (XOf(piece.Start) + XOf(piece.End)) / 2;
            if (XOf(piece.End) - XOf(piece.Start) >= 24 && Math.Abs(x - center) <= 12 && Math.Abs(y - middle) <= 12)
                return piece;
        }
        return null;
    }

    private void ShowPieceButtons()
    {
        bool split = IsVideo && Edit.Splits.Count > 0;
        RemovePieceButton.IsEnabled = split;
        bool removed = IsVideo && Edit.IsRemoved(_playhead);
        RemovePieceGlyph.Text = removed ? "" : "";
        RemovePieceButton.ToolTip = Strings.Get(removed ? "RestorePieceToolTip" : "RemovePieceToolTip");
        System.Windows.Automation.AutomationProperties.SetName(RemovePieceButton, Strings.Get(removed ? "RestorePieceToolTip" : "RemovePieceToolTip"));
    }

    // Lines across the frames at the splits; the pieces removed greyed and hatched, with ↺; the piece chosen outlined.
    private void ShowPieces(double top, double bottom)
    {
        var edit = Edit;
        foreach (var piece in edit.Removed)
        {
            double x1 = XOf(piece.Start), x2 = XOf(piece.End);
            Add(new System.Windows.Shapes.Rectangle { Width = Math.Max(0, x2 - x1), Height = bottom - top, Fill = RemovedBrush }, x1, top);
            if (x2 - x1 >= 24)
            {
                double middle = (top + bottom) / 2;
                Add(new System.Windows.Shapes.Ellipse { Width = 20, Height = 20, Fill = Brushes.White, Opacity = 0.92 }, (x1 + x2) / 2 - 10, middle - 10);
                Add(new TextBlock { Text = "", FontFamily = new FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets"), FontSize = 11, Foreground = Brushes.Black }, (x1 + x2) / 2 - 5.5, middle - 7);
            }
        }
        foreach (var split in edit.Splits)
            Add(new System.Windows.Shapes.Rectangle { Width = 2, Height = bottom - top + 6, Fill = Brushes.White, RadiusX = 1, RadiusY = 1 }, XOf(split) - 1, top - 3);
        if (_pieceChosen && edit.Splits.Count > 0)
        {
            var piece = edit.PieceAt(_playhead, _duration);
            double x1 = XOf(piece.Start), x2 = XOf(piece.End);
            Add(new System.Windows.Shapes.Rectangle { Width = Math.Max(0, x2 - x1 - 2), Height = bottom - top - 2, Stroke = Brushes.White, StrokeThickness = 2, StrokeDashArray = [3, 2], RadiusX = 3, RadiusY = 3 }, x1 + 1, top + 1);
        }
    }

    private static readonly Brush RemovedBrush = MakeRemovedBrush();

    // Dark, with light stripes: removed, but still there.
    private static Brush MakeRemovedBrush()
    {
        var stripes = new DrawingBrush(new GeometryDrawing(new SolidColorBrush(Color.FromArgb(0x55, 0xFF, 0xFF, 0xFF)), null,
            Geometry.Parse("M0,0 L2,0 L8,6 L8,8 L6,8 L0,2 Z")))
        {
            TileMode = TileMode.Tile,
            Viewport = new Rect(0, 0, 8, 8),
            ViewportUnits = BrushMappingMode.Absolute,
        };
        stripes.Freeze();
        var brush = new DrawingBrush(new DrawingGroup
        {
            Children =
            {
                new GeometryDrawing(new SolidColorBrush(Color.FromArgb(0xC8, 0x18, 0x18, 0x1C)), null, new RectangleGeometry(new Rect(0, 0, 8, 8))),
                new GeometryDrawing(stripes, null, new RectangleGeometry(new Rect(0, 0, 8, 8))),
            },
        })
        {
            TileMode = TileMode.Tile,
            Viewport = new Rect(0, 0, 8, 8),
            ViewportUnits = BrushMappingMode.Absolute,
        };
        brush.Freeze();
        return brush;
    }

    // ---------------------------------------------------------------- Sounds: what they are

    private string SoundName(int row) => _soundRows.Count > row ? Strings.Get("Sound" + _soundRows[row].Kind) : "";

    private static string SoundGlyph(SoundKind kind) => kind == SoundKind.Microphone ? "" : kind == SoundKind.Computer ? "" : "";

    private static Color SoundColor(SoundKind kind) => kind switch
    {
        SoundKind.Computer => Color.FromRgb(0x1D, 0x9E, 0x75),
        SoundKind.Microphone => Color.FromRgb(0x7F, 0x77, 0xDD),
        _ => Color.FromRgb(0x37, 0x8A, 0xDD),
    };

    /// <summary>The sounds of the video opened, and their waveforms read in the background.</summary>
    private void LoadSounds(VideoClip clip, bool hasSound)
    {
        _soundRows = hasSound ? SoundTracks.Rows(clip.Path) : [];
        _peaks = new float[]?[_soundRows.Count];
        _soundSelected = -1;
        var rows = _soundRows;
        var peaks = _peaks;
        Task.Run(() =>
        {
            for (int row = 0; row < rows.Count; row++)
            {
                try
                {
                    peaks[row] = ReadPeaks(clip.Path, rows[row].Track);
                }
                catch (Exception ex) when (ex is SharpGen.Runtime.SharpGenException or InvalidDataException or IOException)
                {
                    // No waveform for this one: its row works without it.
                    Services.DevLog.Write($"editor: waveform not read: {ex.Message}");
                    continue;
                }
                Dispatcher.BeginInvoke(() =>
                {
                    if (ReferenceEquals(_peaks, peaks))
                        ShowSounds();
                });
            }
        });
    }

    // The loudest level of a sound every 10 ms (0 to 1).
    private static float[] ReadPeaks(string path, int? track)
    {
        using var reader = new AudioReader(path, track);
        var peaks = new List<float>();
        while (reader.Next() is { } block)
        {
            long first = (long)(block.Time / PeakStep);
            int frames = block.Pcm.Length / 4;
            for (int f = 0; f < frames; f++)
            {
                long bin = first + (long)(f / (SoundComposer.Rate * PeakStep.TotalSeconds));
                if (bin < 0)
                    continue;
                while (peaks.Count <= bin)
                    peaks.Add(0);
                // As ints: a full-scale sample (-32768) has no positive short (Math.Abs of it throws).
                float level = Math.Max(Math.Abs((int)BitConverter.ToInt16(block.Pcm, f * 4)), Math.Abs((int)BitConverter.ToInt16(block.Pcm, f * 4 + 2))) / 32768f;
                if (level > peaks[(int)bin])
                    peaks[(int)bin] = level;
            }
        }
        return [.. peaks];
    }

    /// <summary>What the player and the export mix: each sound's track and options (null: no sound).</summary>
    private Func<SoundComposer?>? SoundMaker(IReadOnlyList<Piece> pieces)
    {
        if (_videoClip is not { } clip || _soundRows.Count == 0)
            return null;
        var rows = _soundRows.Select((r, i) => (r.Track, Edit.Sound(i))).ToList();
        var duration = _duration;
        return () => new SoundComposer(clip.Path, rows, pieces, duration);
    }

    // ---------------------------------------------------------------- Sounds: the rows

    /// <summary>The sound rows again: their labels on the left, their bars and waveforms under the timeline.</summary>
    private void ShowSounds()
    {
        SoundCanvas.Children.Clear();
        SoundLabels.Children.Clear();
        bool show = IsVideo && _soundRows.Count > 0;
        SoundCanvas.Visibility = SoundLabels.Visibility = Show(show);
        double width = Timeline.ActualWidth;
        if (!show || width <= 0)
            return;
        SoundCanvas.Width = width;
        SoundCanvas.Height = _soundRows.Count * (SoundHeight + SoundGap) - SoundGap;
        var accent = (Brush)FindResource("AccentFillColorDefaultBrush");
        var text = (Brush)FindResource("TextFillColorPrimaryBrush");
        var secondary = (Brush)FindResource("TextFillColorSecondaryBrush");
        var layer = _live ?? Layer;
        for (int row = 0; row < _soundRows.Count; row++)
        {
            var kind = _soundRows[row].Kind;
            var options = layer.Video.Sound(row);
            double y = row * (SoundHeight + SoundGap);
            var color = SoundColor(kind);
            var span = options.Span(_duration);
            double x1 = XOf(span.Start), x2 = Math.Max(x1 + 4, XOf(span.End));
            var bar = new Border
            {
                Width = x2 - x1,
                Height = SoundHeight,
                CornerRadius = new CornerRadius(5),
                Background = new SolidColorBrush(Color.FromArgb(0x30, color.R, color.G, color.B)),
                BorderBrush = row == _soundSelected ? accent : new SolidColorBrush(Color.FromArgb(0xB0, color.R, color.G, color.B)),
                BorderThickness = new Thickness(row == _soundSelected ? 2 : 1),
                Opacity = options.Muted ? 0.45 : 1,
                ToolTip = SoundName(row),
            };
            SoundPlace(bar, x1, y);
            if (row < _peaks.Length && _peaks[row] is { } peaks)
                SoundPlace(Waveform(peaks, options, x1, x2, color), 0, y);
            // The fades, as slopes at the ends of the bar.
            double fadeIn = Math.Min(x2 - x1, XOf(span.Start + options.FadeIn) - x1), fadeOut = Math.Min(x2 - x1, x2 - XOf(span.End - options.FadeOut));
            var slope = new SolidColorBrush(Color.FromArgb(0x50, 0x10, 0x10, 0x14));
            if (fadeIn > 1)
                SoundPlace(new System.Windows.Shapes.Polygon { Points = [new Point(0, 0), new Point(fadeIn, 0), new Point(0, SoundHeight)], Fill = slope, IsHitTestVisible = false }, x1, y);
            if (fadeOut > 1)
                SoundPlace(new System.Windows.Shapes.Polygon { Points = [new Point(0, 0), new Point(fadeOut, 0), new Point(fadeOut, SoundHeight)], Fill = slope, IsHitTestVisible = false }, x2 - fadeOut, y);

            // Its label: mute, name, volume.
            var mute = new Button
            {
                Padding = new Thickness(4, 2, 4, 2),
                Margin = new Thickness(0, 0, 6, 0),
                Background = Brushes.Transparent,
                BorderThickness = new Thickness(0),
                Content = new TextBlock { Text = options.Muted ? "" : SoundGlyph(kind), FontFamily = new FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets"), FontSize = 14, Foreground = text },
                ToolTip = Strings.Get(options.Muted ? "SoundUnmute" : "SoundMute"),
            };
            System.Windows.Automation.AutomationProperties.SetName(mute, Strings.Get(options.Muted ? "SoundUnmute" : "SoundMute") + " · " + SoundName(row));
            int index = row;
            mute.Click += (_, _) => ToggleMute(index);
            var label = new DockPanel { Height = SoundHeight, Margin = new Thickness(0, row == 0 ? 0 : SoundGap, 0, 0), LastChildFill = true };
            DockPanel.SetDock(mute, Dock.Left);
            label.Children.Add(mute);
            var volume = new TextBlock { Text = Strings.Format("EditorPercent", options.Volume), FontSize = 11, Foreground = secondary, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(6, 0, 10, 0) };
            DockPanel.SetDock(volume, Dock.Right);
            label.Children.Add(volume);
            label.Children.Add(new TextBlock { Text = SoundName(row), FontSize = 12, Foreground = text, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis });
            SoundLabels.Children.Add(label);
        }
        // What plays not: cut off by the handles, darkened; the pieces removed, hatched.
        var cut = new SolidColorBrush(Color.FromArgb(0x70, 0x10, 0x10, 0x14));
        SoundPlace(new System.Windows.Shapes.Rectangle { Width = Math.Max(0, XOf(_trim.Start)), Height = SoundCanvas.Height, Fill = cut, IsHitTestVisible = false }, 0, 0);
        SoundPlace(new System.Windows.Shapes.Rectangle { Width = Math.Max(0, width - XOf(_trim.End)), Height = SoundCanvas.Height, Fill = cut, IsHitTestVisible = false }, XOf(_trim.End), 0);
        foreach (var piece in Edit.Removed)
            SoundPlace(new System.Windows.Shapes.Rectangle { Width = Math.Max(0, XOf(piece.End) - XOf(piece.Start)), Height = SoundCanvas.Height, Fill = RemovedBrush, IsHitTestVisible = false }, XOf(piece.Start), 0);
        // Each sound silenced in a piece (the picture kept), and the piece of the sound chosen.
        for (int row = 0; row < _soundRows.Count; row++)
        {
            double y = row * (SoundHeight + SoundGap);
            foreach (var silent in layer.Video.Sound(row).Silenced)
                SoundPlace(new System.Windows.Shapes.Rectangle { Width = Math.Max(0, XOf(silent.End) - XOf(silent.Start)), Height = SoundHeight, Fill = RemovedBrush, RadiusX = 4, RadiusY = 4, IsHitTestVisible = false }, XOf(silent.Start), y);
        }
        if (_soundSelected >= 0 && Edit.Splits.Count > 0)
        {
            var chosen = Edit.PieceAt(_pieceAt, _duration);
            SoundPlace(new System.Windows.Shapes.Rectangle { Width = Math.Max(0, XOf(chosen.End) - XOf(chosen.Start) - 2), Height = SoundHeight - 2, Stroke = Brushes.White, StrokeThickness = 2, StrokeDashArray = [3, 2], RadiusX = 4, RadiusY = 4, IsHitTestVisible = false }, XOf(chosen.Start) + 1, _soundSelected * (SoundHeight + SoundGap) + 1);
        }
        foreach (var split in Edit.Splits)
            SoundPlace(SplitLine(SoundCanvas.Height), XOf(split) - 1, 0);
        _soundPlayhead = new System.Windows.Shapes.Rectangle { Width = 2, Height = SoundCanvas.Height + SoundGap + 2, Fill = new SolidColorBrush(Color.FromRgb(0xE5, 0x48, 0x4D)), IsHitTestVisible = false };
        SoundPlace(_soundPlayhead, XOf(_playhead) - 1, -SoundGap - 2);
    }

    private System.Windows.Shapes.Rectangle? _soundPlayhead;

    private void MoveSoundPlayhead()
    {
        if (_soundPlayhead is not null)
            Canvas.SetLeft(_soundPlayhead, XOf(_playhead) - 1);
    }

    private void SoundPlace(UIElement element, double x, double y)
    {
        Canvas.SetLeft(element, x);
        Canvas.SetTop(element, y);
        SoundCanvas.Children.Add(element);
    }

    // The waveform in the bar, as loud as it plays (volume and fades): a level per pixel, up and down from the middle.
    private System.Windows.Shapes.Path Waveform(float[] peaks, SoundOptions options, double x1, double x2, Color color)
    {
        double middle = SoundHeight / 2, reach = SoundHeight / 2 - 4;
        var geometry = new StreamGeometry();
        using (var context = geometry.Open())
        {
            var top = new List<Point>();
            for (double x = x1; x <= x2; x += 1)
            {
                var from = TimeAt(x);
                var to = TimeAt(x + 1);
                int a = (int)(from / PeakStep), b = Math.Max(a + 1, (int)(to / PeakStep));
                float peak = 0;
                for (int i = Math.Max(0, a); i < Math.Min(peaks.Length, b); i++)
                    peak = Math.Max(peak, peaks[i]);
                // A square root: quiet sounds (a voice far from the microphone) still show.
                double level = Math.Min(1, Math.Sqrt(peak * options.GainAt(from, _duration)));
                top.Add(new Point(x, middle - Math.Max(0.5, level * reach)));
            }
            if (top.Count > 1)
            {
                context.BeginFigure(top[0], true, true);
                context.PolyLineTo(top.Skip(1).ToList(), false, false);
                context.PolyLineTo(top.AsEnumerable().Reverse().Select(p => new Point(p.X, 2 * middle - p.Y)).ToList(), false, false);
            }
        }
        geometry.Freeze();
        return new System.Windows.Shapes.Path { Data = geometry, Fill = new SolidColorBrush(Color.FromArgb(0xC0, color.R, color.G, color.B)), IsHitTestVisible = false };
    }

    // ---------------------------------------------------------------- Sounds: chosen, dragged

    private void ToggleMute(int row)
    {
        if (!IsVideo || _export is not null)
            return;
        StopPlayback();
        var options = Edit.Sound(row);
        CommitVideo(Edit.WithSound(row, options with { Muted = !options.Muted }));
    }

    private void SelectSound(int row)
    {
        CommitText();
        CancelDrag();
        if (!_editTab)
            SetTab(true);
        if (_tool != EditTool.Select)
            SetTool(EditTool.Select);
        _selectedId = null;
        _cursorSelected = false;
        _soundSelected = row;
        _pieceChosen = false;
        ShowEditPanel();
        ShowTracks();
        ShowSounds();
        ShowTimeline();
        RequestRender();
    }

    private SoundGrip? SoundGripAt(int row, double x)
    {
        var span = Edit.Sound(row).Span(_duration);
        double x1 = XOf(span.Start), x2 = XOf(span.End);
        if (Math.Abs(x - x1) <= 6 && Math.Abs(x - x1) <= Math.Abs(x - x2))
            return SoundGrip.Start;
        return Math.Abs(x - x2) <= 6 ? SoundGrip.End : null;
    }

    private int SoundRowAt(double y)
    {
        int row = (int)Math.Floor(y / (SoundHeight + SoundGap));
        return row >= 0 && row < _soundRows.Count ? row : -1;
    }

    private void SoundCanvas_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (SoundDown(e.GetPosition(SoundCanvas)))
        {
            SoundCanvas.CaptureMouse();
            e.Handled = true;
        }
    }

    private void SoundCanvas_MouseMove(object sender, MouseEventArgs e) =>
        SoundMove(e.GetPosition(SoundCanvas), e.LeftButton == MouseButtonState.Pressed);

    private void SoundCanvas_MouseLeftButtonUp(object sender, MouseButtonEventArgs e) => SoundUp();

    private void SoundCanvas_LostMouseCapture(object sender, MouseEventArgs e)
    {
        if (_soundDrag is not null)
            SoundUp();
    }

    /// <summary>A press on a sound row: chosen, and an end of its bar grabbed. True when dragging.</summary>
    private bool SoundDown(Point at)
    {
        if (!IsVideo || _export is not null)
            return false;
        StopPlayback();
        int row = SoundRowAt(at.Y);
        if (row < 0)
            return false;
        _pieceAt = TimeAt(at.X);
        SelectSound(row);
        if (SoundGripAt(row, at.X) is not { } grip)
            return false;
        _soundDrag = new SoundDrag(row, grip, _history.Current);
        return true;
    }

    private void SoundMove(Point at, bool pressed)
    {
        if (!IsVideo)
            return;
        if (_soundDrag is not { } drag)
        {
            int row = SoundRowAt(at.Y);
            SoundCanvas.Cursor = row >= 0 && SoundGripAt(row, at.X) is not null ? Cursors.SizeWE : Cursors.Arrow;
            return;
        }
        if (!pressed)
        {
            SoundUp();
            return;
        }
        var time = TimeAt(at.X);
        var options = drag.Base.Video.Sound(drag.Row);
        options = drag.Grip == SoundGrip.Start ? options.WithStart(time, _duration) : options.WithEnd(time, _duration);
        _live = drag.Base with { Video = drag.Base.Video.WithSound(drag.Row, options) };
        // The playhead shows where the sound starts or stops now.
        var span = options.Span(_duration);
        RequestFrame(_trim.Clamp(drag.Grip == SoundGrip.Start ? span.Start : span.End - TimeSpan.FromMilliseconds(1)));
        ShowSounds();
    }

    private void SoundUp()
    {
        if (_soundDrag is null)
            return;
        _soundDrag = null;
        if (SoundCanvas.IsMouseCaptured)
            SoundCanvas.ReleaseMouseCapture();
        if (_live is { } changed)
            Commit(changed);
    }

    // ---------------------------------------------------------------- Sounds: the Edit panel

    private bool _syncingSound;

    private void ShowSoundPanel()
    {
        var options = Edit.Sound(_soundSelected);
        _syncingSound = true;
        SoundVolumeSlider.Value = options.Volume;
        SoundVolumeValue.Text = Strings.Format("EditorPercent", options.Volume);
        SoundFadeInSlider.Value = options.FadeIn.TotalSeconds;
        SoundFadeInValue.Text = Seconds(options.FadeIn);
        SoundFadeOutSlider.Value = options.FadeOut.TotalSeconds;
        SoundFadeOutValue.Text = Seconds(options.FadeOut);
        SoundMuteSwitch.IsChecked = options.Muted;
        bool silenced = options.IsSilencedAt(_pieceAt);
        SoundSilenceText.Text = Strings.Get(silenced ? "MenuUnsilencePiece" : "MenuSilencePiece");
        SoundSilenceButton.Visibility = Show(Edit.Splits.Count > 0);
        _syncingSound = false;
    }

    private static string Seconds(TimeSpan time) =>
        Strings.Format("EditorSeconds", time.TotalSeconds.ToString("0.0", System.Globalization.CultureInfo.CurrentCulture));

    private void ChangeSound(Func<SoundOptions, SoundOptions> change, string option)
    {
        if (!_initialized || _syncingSound || !IsVideo || _soundSelected < 0)
            return;
        var options = change(Edit.Sound(_soundSelected)).Normalized(_duration);
        CommitVideo(Edit.WithSound(_soundSelected, options), "sound" + option + _soundSelected);
    }

    private void SoundVolume_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (!_initialized)
            return;
        int volume = (int)Math.Round(SoundVolumeSlider.Value);
        SoundVolumeValue.Text = Strings.Format("EditorPercent", volume);
        ChangeSound(o => o with { Volume = volume }, "volume");
    }

    private void SoundFadeIn_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (!_initialized)
            return;
        var fade = TimeSpan.FromSeconds(Math.Round(SoundFadeInSlider.Value, 1));
        SoundFadeInValue.Text = Seconds(fade);
        ChangeSound(o => o with { FadeIn = fade }, "fadeIn");
    }

    private void SoundFadeOut_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (!_initialized)
            return;
        var fade = TimeSpan.FromSeconds(Math.Round(SoundFadeOutSlider.Value, 1));
        SoundFadeOutValue.Text = Seconds(fade);
        ChangeSound(o => o with { FadeOut = fade }, "fadeOut");
    }

    private void SoundSilence_Click(object sender, RoutedEventArgs e) => ToggleSilence(_soundSelected, _pieceAt);

    private void SoundMute_Changed(object sender, RoutedEventArgs e) =>
        ChangeSound(o => o with { Muted = SoundMuteSwitch.IsChecked == true }, "mute");
}
