using System.Windows;
using System.Windows.Controls;
using ScreenClip.Core.Annotations;
using ScreenClip.Core.Composition;
using ScreenClip.Core.Video;
using ScreenClip.Services;
using ScreenClip.Video;
using SkiaSharp;

namespace ScreenClip;

/// <summary>
/// The Export button and its window (asked by the author, in place of Save): the format (MP4, GIF or the frame shown
/// for a video; PNG, JPG or WebP for a picture), whether the layout is applied, the size it gives, then Copy or Save.
/// The same choices make what Ctrl+S gives, and the window's Copy (a GIF or a picture in the format chosen; no MP4).
/// The bar's Copy makes a picture in the format of the settings, with or without the layout as this window says.
/// </summary>
public partial class EditorWindow
{
    private static bool SavesGif => App.Settings.VideoFormat == VideoFormat.Gif;

    private static bool SavesFrame => App.Settings.VideoFormat == VideoFormat.Frame;

    private static bool SavesMp4 => App.Settings.VideoFormat == VideoFormat.Mp4;

    /// <summary>
    /// A video's frame is a PNG or a JPG (the author: “Image” said nothing), the style's format when it is one of them.
    /// </summary>
    private ExportFormat FrameFormat => _style.Format == ExportFormat.Jpeg ? ExportFormat.Jpeg : ExportFormat.Png;

    /// <summary>The format of the picture Export makes: the style's, or a video frame's.</summary>
    private ExportFormat PictureFormat => IsVideo ? FrameFormat : _style.Format;

    /// <summary>What Save and Copy make apply the layout (else the capture with its annotations alone).</summary>
    private static bool ExportsLayout => App.Settings.ExportLayout;

    // The format on the Export button: MP4 or GIF for a video (or the picture's for its frame), the picture's otherwise.
    private void ShowExportFormat()
    {
        SaveFormat.Text = IsVideo && !SavesFrame ? (SavesGif ? "GIF" : "MP4") : FormatName(PictureFormat);
        // The bar's Copy always makes a picture (the frame shown, for a video), in the format of the settings: the
        // author wanted it plain, “Copy as PNG”, and not changing with Export.
        CopyText.Text = CopyLabel;
        CopyButton.ToolTip = Strings.Get(IsVideo ? "CopyFrameToolTip" : "CopyPictureToolTip");
        // The window's Copy copies what it would save, but an MP4 (the author: a video is not something to paste).
        ExportCopyButton.Visibility = IsVideo && SavesMp4 ? Visibility.Collapsed : Visibility.Visible;
        ExportCopyButton.Content = IsVideo && SavesGif ? Strings.Get("CopyTheGif") : Strings.Format("CopyAs", FormatName(PictureFormat));
    }

    private static string CopyLabel => Strings.Format("CopyAs", FormatName(App.Settings.CopyFormat));

    /// <summary>Settings › Saving changed the format of Copy.</summary>
    internal void CopyFormatChanged() => ShowExportFormat();

    private void ExportButton_Click(object sender, RoutedEventArgs e)
    {
        if (_capture is null || _export is not null)
            return;
        StopPlayback();
        CommitText();
        FillExportPopup();
        // Under the button, its right edge on the button's.
        ExportPopup.HorizontalOffset = ExportButton.ActualWidth - ExportCardWidth - 16;
        ExportPopup.IsOpen = true;
    }

    private const double ExportCardWidth = 320;

    private void FillExportPopup()
    {
        _syncing = true;
        ExportFormatList.Children.Clear();
        void Add(string label, bool chosen, string? hint, Action pick)
        {
            var button = new RadioButton { Style = (Style)FindResource("Segment"), GroupName = "ExportFormat", Content = label, IsChecked = chosen, ToolTip = hint };
            button.Checked += (_, _) =>
            {
                if (_syncing)
                    return;
                pick();
                ShowExportChoices();
                RequestRender(); // the size under the picture
            };
            ExportFormatList.Children.Add(button);
        }
        if (IsVideo)
        {
            Add("MP4", SavesMp4, Strings.Get("FormatMp4ToolTip"), () => App.Settings.VideoFormat = VideoFormat.Mp4);
            Add("GIF", SavesGif, Strings.Get("FormatGifToolTip"), () => App.Settings.VideoFormat = VideoFormat.Gif);
            foreach (var format in new[] { ExportFormat.Png, ExportFormat.Jpeg })
                Add(FormatName(format), SavesFrame && FrameFormat == format, Strings.Get("FormatFrameToolTip"), () =>
                {
                    App.Settings.VideoFormat = VideoFormat.Frame;
                    Apply(_style with { Format = format });
                });
        }
        else
        {
            foreach (var format in Enum.GetValues<ExportFormat>())
                Add(FormatName(format), _style.Format == format, null, () => Apply(_style with { Format = format }));
        }
        ExportLayoutSwitch.IsChecked = ExportsLayout;
        ExportLayoutHint.Text = LayoutName();
        ShowExportPreview();
        ShowExportChoices();
        _syncing = false;
    }

    // What the choices give: the format's words (a video), the size and weight, the format on the button.
    private void ShowExportChoices()
    {
        ExportFormatHint.Text = IsVideo ? Strings.Get(SavesGif ? "FormatGifToolTip" : SavesFrame ? "FormatFrameToolTip" : "FormatMp4ToolTip") : "";
        ExportFormatHint.Visibility = IsVideo ? Visibility.Visible : Visibility.Collapsed;
        ShowExportSize();
        ShowExportFormat();
    }

    private int _weighing;

    /// <summary>
    /// The size of the file too (asked by the author): a picture is encoded on a worker to weigh it exactly; an MP4 is
    /// told from its bit rate (about); a GIF depends too much on what moves to be told.
    /// </summary>
    private void ShowExportSize()
    {
        string size = ExportOutput();
        ExportSize.Text = size;
        if (_capture is null)
            return;
        int request = ++_weighing;
        if (IsVideo && !SavesFrame)
        {
            if (SavesGif)
                return;
            var kept = _history.Current.Visible(_capture.Width, _capture.Height);
            int width = kept.Width, height = kept.Height;
            if (ExportsLayout && ClipLayout.Measure(kept.Width, kept.Height, _style) is var layout)
                (width, height) = (layout.Width, layout.Height);
            long bits = (long)(Media.Bitrate(width & ~1, height & ~1, ScreenRecorder.Fps) + (_videoHasSound ? 160_000 : 0));
            ExportSize.Text = size + " · " + Strings.Format("SizeAbout", Weight((long)(bits * _trim.Length.TotalSeconds / 8)));
            return;
        }
        var format = PictureFormat;
        var picture = RenderFull();
        Task.Run(() =>
        {
            using (picture)
            {
                long bytes = Composer.Encode(picture, format).Length;
                Dispatcher.BeginInvoke(() =>
                {
                    if (request == _weighing)
                        ExportSize.Text = size + " · " + Weight(bytes);
                });
            }
        });
    }

    /// <summary>“245 KB”, “1.2 MB”, in the UI's language.</summary>
    private static string Weight(long bytes) => bytes < 1_000_000
        ? Strings.Format("SizeKB", Math.Max(1, (int)Math.Round(bytes / 1000.0)))
        : Strings.Format("SizeMB", (bytes / 1_000_000.0).ToString("0.0", System.Globalization.CultureInfo.CurrentCulture));

    // The layout that would be applied: a preset's name when it is one, else the Layout tab's own.
    private string LayoutName()
    {
        if (StylePresets.IdOf(_style) is { } id)
            return Strings.Format("ExportLayoutPreset", Strings.Get("Preset" + char.ToUpperInvariant(id[0]) + id[1..]));
        if (App.Settings.UserPresets.FirstOrDefault(p => StylePresets.Matches(p.Style, _style)) is { } mine)
            return Strings.Format("ExportLayoutPreset", mine.Name);
        return Strings.Get("ExportLayoutCustom");
    }

    private void ExportLayout_Changed(object sender, RoutedEventArgs e)
    {
        if (!_initialized || _syncing)
            return;
        App.Settings.ExportLayout = ExportLayoutSwitch.IsChecked == true;
        ShowExportSize();
        ShowExportPreview();
        RequestRender(); // the size under the picture
    }

    // The picture Save and Copy make, drawn at the size of the Export window's preview (as the Layout tab draws its own).
    private void ShowExportPreview()
    {
        if (_capture is null)
            return;
        var dpi = System.Windows.Media.VisualTreeHelper.GetDpi(this);
        double boxWidth = (ExportCardWidth - 2 * 18 - 16) * dpi.DpiScaleX, boxHeight = (150 - 16) * dpi.DpiScaleY;
        var source = Flattened();
        if (ExportsLayout)
        {
            var layout = ClipLayout.Measure(source.Width, source.Height, _style);
            float scale = (float)Math.Min(1, Math.Min(boxWidth / layout.Width, boxHeight / layout.Height));
            using var picture = Composer.Render(source, _style, scale, BackgroundImageFor(_style));
            ExportPreview.Source = Imaging.ToWpf(picture);
            return;
        }
        double fit = Math.Min(1, Math.Min(boxWidth / source.Width, boxHeight / source.Height));
        var info = new SKImageInfo(Math.Max(1, (int)Math.Round(source.Width * fit)), Math.Max(1, (int)Math.Round(source.Height * fit)), SKColorType.Bgra8888, SKAlphaType.Premul);
        using var surface = SKSurface.Create(info);
        surface.Canvas.Clear(SKColors.Transparent);
        surface.Canvas.DrawImage(source, SKRect.Create(info.Width, info.Height), new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.Linear));
        using var small = surface.Snapshot();
        ExportPreview.Source = Imaging.ToWpf(small);
    }

    private void ExportCopy_Click(object sender, RoutedEventArgs e)
    {
        ExportPopup.IsOpen = false;
        if (IsVideo && SavesGif)
            CopyGif();
        else if (!IsVideo || SavesFrame)
            Copy(PictureFormat);
    }

    private void ExportSave_Click(object sender, RoutedEventArgs e)
    {
        ExportPopup.IsOpen = false;
        if (IsVideo)
            SaveVideo();
        else
            Save();
    }

    /// <summary>What Save and Copy make: its size, format (and length for a video).</summary>
    private string ExportOutput()
    {
        if (_capture is null)
            return "";
        var kept = _history.Current.Visible(_capture.Width, _capture.Height);
        int width = kept.Width, height = kept.Height;
        if (ExportsLayout)
        {
            var layout = ClipLayout.Measure(kept.Width, kept.Height, _style);
            (width, height) = (layout.Width, layout.Height);
        }
        if (!IsVideo || SavesFrame)
            return Strings.Format("EditorOutput", width, height, FormatName(PictureFormat));
        width &= ~1;
        height &= ~1;
        if (SavesGif && width > VideoExporter.GifMaxWidth)
            (width, height) = (VideoExporter.GifMaxWidth, (int)Math.Round(height * (double)VideoExporter.GifMaxWidth / width));
        return Strings.Format("VideoOutput", width, height, SavesGif ? "GIF" : "MP4", Timecode.Format(_trim.Length, Decimal));
    }

    /// <summary>
    /// The picture Save and Copy make (the text being typed is finished first): laid out, or the capture with its
    /// annotations alone when the layout is not applied.
    /// </summary>
    private SKImage RenderFull()
    {
        CommitText();
        return ExportsLayout
            ? Composer.Render(Flattened(), _style, 1, BackgroundImageFor(_style))
            : AnnotationRenderer.Flatten(_capture!, _history.Current, Moment);
    }
}
