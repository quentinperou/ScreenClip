using System.Collections.Specialized;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Microsoft.Win32;
using ScreenClip.Core;
using ScreenClip.Core.Composition;
using ScreenClip.Services;
using SkiaSharp;

namespace ScreenClip;

/// <summary>
/// The layout editor: the capture (or an imported picture) on a background, in a window frame, with its margin,
/// corners, shadow and tilt; then copy it, save it or drag it to an app. Opened when ScreenClip is started by hand,
/// from a thumbnail, or from the notification menu. Every change applies at once and is kept for the next time.
/// </summary>
public partial class EditorWindow : Window
{
    private static readonly AspectRatio[] Ratios = [AspectRatio.Auto, AspectRatio.Wide, AspectRatio.Standard, AspectRatio.Square, AspectRatio.Portrait, AspectRatio.Story];

    private ClipStyle _style;
    private SKImage? _capture;
    private DateTime _takenAt = DateTime.Now;
    private SKImage? _backgroundImage;
    private string? _backgroundImagePath;
    private WriteableBitmap? _previewBitmap;
    private readonly bool _initialized;
    private bool _syncing;
    private bool _renderQueued;
    private bool _draggingOut;
    private Point? _pressedAt;
    private string? _statusPath;

    private readonly Dictionary<string, RadioButton> _presetTiles = [];
    private readonly List<RadioButton> _userTiles = [];
    private int? _naming;                                    // the preset being renamed; null: a new one
    private readonly Dictionary<string, SKImage?> _miniatureBackgrounds = [];
    private readonly List<(Image Image, ClipStyle Style)> _keepBackgroundMiniatures = []; // redrawn with the background
    private SKImage? _demoCapture;
    private readonly Dictionary<string, RadioButton> _backgroundTiles = [];
    private readonly Dictionary<FrameKind, RadioButton> _frameTiles = [];
    private readonly Dictionary<AspectRatio, RadioButton> _ratioButtons = [];
    private Border? _imageSwatch;
    private Border? _blurSwatch;
    private string? _imageSwatchPath;

    public EditorWindow()
    {
        InitializeComponent();
        MicaBackdrop.Apply(this);
        _style = App.Settings.EditorStyle.Normalized();

        BuildPresets();
        BuildBackgrounds();
        BuildFrames();
        BuildRatios();
        ShowStyle();
        ShowState();

        PreviewKeyDown += Editor_PreviewKeyDown;
        DragOver += Editor_DragOver;
        Drop += Editor_Drop;
        DpiChanged += (_, _) => RequestRender();
        Closed += (_, _) =>
        {
            App.Settings.Save();
            _capture?.Dispose();
            _backgroundImage?.Dispose();
            _demoCapture?.Dispose();
            foreach (var image in _miniatureBackgrounds.Values)
                image?.Dispose();
            _miniatureBackgrounds.Clear();
            _capture = _backgroundImage = _demoCapture = null;
            Preview.Source = null;
            _previewBitmap = null;
        };
        _initialized = true;
    }

    // ---------------------------------------------------------------- The picture

    /// <summary>Shows <paramref name="image"/> (a capture, an imported or pasted picture) to be laid out.</summary>
    internal void Load(BitmapSource image, DateTime takenAt)
    {
        _capture?.Dispose();
        _capture = Imaging.ToSkia(image);
        _takenAt = takenAt;
        // The “blurred capture” swatch shows this capture blurred: what it gives is seen at once.
        if (_blurSwatch is not null)
        {
            using var blurred = Composer.BlurredSwatch(_capture, 48);
            _blurSwatch.Background = new ImageBrush(Imaging.ToWpf(blurred)) { Stretch = Stretch.UniformToFill };
        }
        HideStatus();
        ShowState();
        RequestRender();
    }

    private bool LoadFile(string path)
    {
        try
        {
            Load(Imaging.Load(path), File.GetLastWriteTime(path));
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException or FileFormatException or ArgumentException)
        {
            MessageBox.Show(this, Strings.Format("ErrorOpenImage", ex.Message), "ScreenClip", MessageBoxButton.OK, MessageBoxImage.Warning);
            return false;
        }
    }

    /// <summary>Opens <paramref name="path"/> in the editor (command line, File Explorer, a drop).</summary>
    internal void Open(string path) => LoadFile(path);

    private void ShowState()
    {
        bool hasPicture = _capture is not null;
        EmptyState.Visibility = hasPicture ? Visibility.Collapsed : Visibility.Visible;
        Preview.Visibility = hasPicture ? Visibility.Visible : Visibility.Collapsed;
        DragHint.Visibility = hasPicture ? Visibility.Visible : Visibility.Collapsed;
        CopyButton.IsEnabled = SaveButton.IsEnabled = hasPicture;
        if (!hasPicture)
            OutputText.Text = "";
    }

    private void Import_Click(object sender, RoutedEventArgs e) => Import();

    private void Import()
    {
        var dialog = new OpenFileDialog
        {
            Title = Strings.Get("EditorImport"),
            Filter = $"{Strings.Get("FilterImages")}|{Imaging.OpenFilter}",
        };
        if (dialog.ShowDialog(this) == true)
            LoadFile(dialog.FileName);
    }

    private void Paste()
    {
        if (Imaging.FromClipboard() is { } image)
            Load(image, DateTime.Now);
        else
            ShowStatus(Strings.Get("EditorNoImageInClipboard"), null);
    }

    // ---------------------------------------------------------------- New capture

    private void NewCapture_Click(object sender, RoutedEventArgs e)
    {
        var menu = new ContextMenu { PlacementTarget = NewCaptureButton, Placement = PlacementMode.Bottom };
        var actions = new List<(ShortcutAction, string, string)>
        {
            (ShortcutAction.Area, "CaptureArea", "IconArea"),
            (ShortcutAction.Window, "CaptureWindow", "IconWindow"),
            (ShortcutAction.Screen, "CaptureScreen", "IconScreen"),
        };
        if (ScreenGrabber.Monitors().Count > 1)
            actions.Add((ShortcutAction.AllScreens, "CaptureAllScreens", "IconAllScreens"));
        foreach (var (action, text, icon) in actions)
        {
            var item = new MenuItem
            {
                Header = Strings.Get(text),
                InputGestureText = ShortcutText.Format(App.Settings.GetShortcut(action)),
                Icon = new System.Windows.Shapes.Path { Data = (Geometry)FindResource(icon), Style = (Style)FindResource("IconPath") },
            };
            item.Click += (_, _) => StartCapture(action);
            menu.Items.Add(item);
        }
        menu.IsOpen = true;
    }

    private void CaptureArea_Click(object sender, RoutedEventArgs e) => StartCapture(ShortcutAction.Area);

    // The editor steps aside while the screen is frozen, and comes back with the capture.
    private void StartCapture(ShortcutAction action)
    {
        Hide();
        App.Capture.RunForEditor(action, clip =>
        {
            if (clip is not null)
                Load(clip.Image, clip.TakenAt);
            Show();
            Activate();
        });
    }

    // ---------------------------------------------------------------- Style controls

    // The built-in presets, then the user's (right click: rename, replace, delete), then the tile that saves the
    // current style. Each one is drawn as it renders, on a small made-up app: what a preset gives is seen at once.
    private void BuildPresets()
    {
        PresetList.Children.Clear();
        _presetTiles.Clear();
        _userTiles.Clear();
        _keepBackgroundMiniatures.Clear();
        foreach (var (id, style) in StylePresets.All)
        {
            var tile = PresetTile(style, Strings.Get("Preset" + Capitalize(id)));
            _presetTiles[id] = tile;
            PresetList.Children.Add(tile);
        }

        var presets = App.Settings.UserPresets;
        for (int i = 0; i < presets.Count; i++)
        {
            int index = i;
            var tile = PresetTile(presets[i].Style, presets[i].Name);
            var menu = new ContextMenu();
            menu.Items.Add(MenuItem("PresetRename", "\uE8AC", () => AskPresetName(index, tile)));
            menu.Items.Add(MenuItem("PresetReplace", "\uE72C", () => ReplacePreset(index)));
            menu.Items.Add(new Separator());
            menu.Items.Add(MenuItem("PresetDelete", "\uE74D", () => DeletePreset(index)));
            tile.ContextMenu = menu;
            _userTiles.Add(tile);
            PresetList.Children.Add(tile);
        }

        var dashes = new System.Windows.Shapes.Rectangle { RadiusX = 4, RadiusY = 4, StrokeThickness = 1, StrokeDashArray = [3, 2] };
        dashes.SetResourceReference(System.Windows.Shapes.Shape.StrokeProperty, "ControlStrokeColorSecondaryBrush");
        var add = new Button
        {
            Style = (Style)FindResource("AddTile"),
            Margin = new Thickness(1),
            ToolTip = Strings.Get("PresetAddToolTip"),
            Content = new StackPanel
            {
                Children =
                {
                    new Grid
                    {
                        Width = 58,
                        Height = 38,
                        Children = { dashes, new TextBlock { Text = "\uE710", Style = (Style)FindResource("IconText"), FontSize = 14, HorizontalAlignment = HorizontalAlignment.Center } },
                    },
                    PresetLabel(Strings.Get("PresetAdd")),
                },
            },
        };
        System.Windows.Automation.AutomationProperties.SetName(add, Strings.Get("PresetAddToolTip"));
        add.Click += (_, _) => AskPresetName(null, add);
        PresetList.Children.Add(add);
    }

    private RadioButton PresetTile(ClipStyle style, string name)
    {
        var tile = Tile("Preset", new StackPanel { Children = { PresetMiniature(style), PresetLabel(name) } }, name);
        tile.Checked += (_, _) =>
        {
            if (!_syncing)
                Apply(StylePresets.ApplyTo(style, _style));
        };
        return tile;
    }

    private static TextBlock PresetLabel(string text) => new()
    {
        Text = text,
        FontSize = 12,
        TextAlignment = TextAlignment.Center,
        TextTrimming = TextTrimming.CharacterEllipsis,
        Margin = new Thickness(0, 4, 0, 0),
    };

    private static MenuItem MenuItem(string text, string glyph, Action action)
    {
        var item = new MenuItem
        {
            Header = Strings.Get(text),
            Icon = new TextBlock { Text = glyph, FontSize = 14, Style = (Style)Application.Current.FindResource("IconText") },
        };
        item.Click += (_, _) => action();
        return item;
    }

    // The preset drawn by the composer itself, twice the size of the tile (sharp up to 200 %).
    private Image PresetMiniature(ClipStyle style)
    {
        var image = new Image { Width = 58, Height = 38, Stretch = Stretch.Uniform };
        DrawMiniature(image, style);
        if (style.Background == StylePresets.CurrentBackground)
            _keepBackgroundMiniatures.Add((image, style));
        return image;
    }

    private void DrawMiniature(Image image, ClipStyle style)
    {
        const double Density = 2;
        _demoCapture ??= DemoCapture();
        // A preset that keeps the background is drawn on the one chosen now.
        var shown = style.Background == StylePresets.CurrentBackground
            ? style with { FrameText = "", Background = _style.Background, BackgroundImage = _style.BackgroundImage }
            : style with { FrameText = "" };
        var layout = ClipLayout.Measure(_demoCapture.Width, _demoCapture.Height, shown);
        float scale = (float)Math.Min(image.Width * Density / layout.Width, image.Height * Density / layout.Height);
        using var picture = Composer.Render(_demoCapture, shown, scale, MiniatureBackground(shown));
        image.Source = Imaging.ToWpf(picture);
        image.Tag = (shown.Background, shown.BackgroundImage);
    }

    // A made-up app for the miniatures: a bar, a colorful card, lines of text.
    private static SKImage DemoCapture()
    {
        using var surface = SKSurface.Create(new SKImageInfo(480, 300));
        var canvas = surface.Canvas;
        canvas.Clear(SKColors.White);
        using var paint = new SKPaint { IsAntialias = true, Color = new SKColor(0xEE, 0xF0, 0xF4) };
        canvas.DrawRect(0, 0, 480, 46, paint);
        using (var shader = SKShader.CreateLinearGradient(new SKPoint(30, 80), new SKPoint(230, 260),
            [new SKColor(0x7B, 0x61, 0xFF), new SKColor(0xE5, 0x48, 0x9A)], SKShaderTileMode.Clamp))
        {
            paint.Shader = shader;
            canvas.DrawRoundRect(30, 80, 200, 180, 14, 14, paint);
            paint.Shader = null;
        }
        paint.Color = new SKColor(0xD5, 0xD9, 0xE0);
        for (int i = 0; i < 5; i++)
            canvas.DrawRoundRect(260, 90 + i * 34, i % 2 == 0 ? 180 : 140, 14, 7, 7, paint);
        return surface.Snapshot();
    }

    // The picture of a preset's background, read small, once.
    private SKImage? MiniatureBackground(ClipStyle style)
    {
        if (style.Background != Backgrounds.ImageId || style.BackgroundImage is not { } path)
            return null;
        if (!_miniatureBackgrounds.TryGetValue(path, out var image))
        {
            try
            {
                var small = new BitmapImage();
                small.BeginInit();
                small.UriSource = new Uri(path);
                small.DecodePixelWidth = 256;
                small.CacheOption = BitmapCacheOption.OnLoad;
                small.EndInit();
                small.Freeze();
                image = Imaging.ToSkia(small);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException or UriFormatException or FileFormatException or ArgumentException)
            {
                image = null;
            }
            _miniatureBackgrounds[path] = image;
        }
        return image;
    }

    // ---------------------------------------------------------------- The user's presets

    // Asks the name of a new preset (index null) or of the one renamed, under its tile.
    private void AskPresetName(int? index, UIElement tile)
    {
        var presets = App.Settings.UserPresets;
        if (index is { } i && i >= presets.Count)
            return;
        _naming = index;
        PresetNameBox.Text = index is { } renamed
            ? presets[renamed].Name
            : StylePresets.NewName(Strings.Get("PresetDefaultName"), presets.Select(p => p.Name));
        CheckPresetName();
        PresetNamePopup.PlacementTarget = tile;
        PresetNamePopup.IsOpen = true;
        Dispatcher.BeginInvoke(() =>
        {
            PresetNameBox.Focus();
            PresetNameBox.SelectAll();
        }, DispatcherPriority.Input);
    }

    // A name is needed, and one that no other preset has (the built-in ones included).
    private bool CheckPresetName()
    {
        string? name = StylePresets.CleanName(PresetNameBox.Text);
        var others = StylePresets.All.Select(p => Strings.Get("Preset" + Capitalize(p.Id)))
            .Concat(App.Settings.UserPresets.Where((_, i) => i != _naming).Select(p => p.Name));
        bool taken = name is not null && StylePresets.IsTaken(name, others);
        PresetNameTaken.Visibility = taken ? Visibility.Visible : Visibility.Collapsed;
        PresetNameOk.IsEnabled = name is not null && !taken;
        return PresetNameOk.IsEnabled;
    }

    private void PresetNameBox_TextChanged(object sender, TextChangedEventArgs e) => CheckPresetName();

    private void PresetNameBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            SavePresetName();
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            PresetNamePopup.IsOpen = false;
            e.Handled = true;
        }
    }

    private void PresetNameOk_Click(object sender, RoutedEventArgs e) => SavePresetName();

    private void PresetNameCancel_Click(object sender, RoutedEventArgs e) => PresetNamePopup.IsOpen = false;

    private void SavePresetName()
    {
        if (!CheckPresetName())
            return;
        string name = StylePresets.CleanName(PresetNameBox.Text)!;
        var presets = App.Settings.UserPresets;
        if (_naming is { } i && i < presets.Count)
            presets[i] = presets[i] with { Name = name };
        else
            presets.Add(new UserPreset { Name = name, Style = StylePresets.ToSave(_style) });
        PresetNamePopup.IsOpen = false;
        PresetsChanged();
    }

    private void ReplacePreset(int index)
    {
        var presets = App.Settings.UserPresets;
        if (index >= presets.Count)
            return;
        presets[index] = presets[index] with { Style = StylePresets.ToSave(_style) };
        PresetsChanged();
    }

    private void DeletePreset(int index)
    {
        var presets = App.Settings.UserPresets;
        if (index >= presets.Count
            || MessageBox.Show(this, Strings.Format("PresetDeleteQuestion", presets[index].Name), "ScreenClip",
                MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No) != MessageBoxResult.Yes)
            return;
        presets.RemoveAt(index);
        PresetsChanged();
    }

    private void PresetsChanged()
    {
        App.Settings.Save();
        BuildPresets();
        ShowStyle();
    }

    private void BuildBackgrounds()
    {
        foreach (var background in Backgrounds.All)
        {
            var swatch = new Border
            {
                Height = 34,
                CornerRadius = new CornerRadius(6),
                BorderThickness = new Thickness(1),
                Background = BackgroundBrush(background),
            };
            swatch.SetResourceReference(Border.BorderBrushProperty, "ControlStrokeColorDefaultBrush");
            if (background.Kind == BackgroundKind.Image)
                swatch.Child = new TextBlock { Text = "", Style = (Style)FindResource("IconText"), HorizontalAlignment = HorizontalAlignment.Center, FontSize = 15 };
            var tile = Tile("Background", swatch, Strings.Get("Background" + Capitalize(background.Id)));
            if (background.Kind == BackgroundKind.Blur)
                _blurSwatch = swatch;
            if (background.Kind == BackgroundKind.Image)
            {
                _imageSwatch = swatch;
                // Chosen: the picture already picked, else pick one. Clicked again once chosen: pick another.
                bool wasChecked = false;
                tile.PreviewMouseLeftButtonDown += (_, _) => wasChecked = tile.IsChecked == true;
                tile.Click += (_, _) =>
                {
                    if (wasChecked)
                        PickBackgroundImage();
                    wasChecked = false;
                };
                tile.Checked += (_, _) =>
                {
                    if (_syncing)
                        return;
                    if (_style.BackgroundImage is { } path && File.Exists(path))
                        Apply(_style with { Background = Backgrounds.ImageId });
                    else
                        PickBackgroundImage();
                };
            }
            else
            {
                tile.Checked += (_, _) =>
                {
                    if (!_syncing)
                        Apply(_style with { Background = background.Id });
                };
            }
            _backgroundTiles[background.Id] = tile;
            BackgroundList.Children.Add(tile);
        }
    }

    private void BuildFrames()
    {
        foreach (var frame in Enum.GetValues<FrameKind>())
        {
            var label = new TextBlock { Text = Strings.Get("Frame" + frame), FontSize = 12, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 4, 0, 0) };
            var tile = Tile("Frame", new StackPanel { Margin = new Thickness(0, 4, 0, 2), Children = { FrameMiniature(frame), label } }, Strings.Get("Frame" + frame));
            tile.Checked += (_, _) =>
            {
                if (!_syncing)
                    Apply(_style.WithFrame(frame));
            };
            _frameTiles[frame] = tile;
            FrameList.Children.Add(tile);
        }
    }

    private void BuildRatios()
    {
        foreach (var ratio in Ratios)
        {
            var button = new RadioButton
            {
                Style = (Style)FindResource("Segment"),
                GroupName = "Ratio",
                Content = ratio switch
                {
                    AspectRatio.Wide => "16:9",
                    AspectRatio.Standard => "4:3",
                    AspectRatio.Square => "1:1",
                    AspectRatio.Portrait => "4:5",
                    AspectRatio.Story => "9:16",
                    _ => Strings.Get("RatioAuto"),
                },
            };
            button.Checked += (_, _) =>
            {
                if (!_syncing)
                    Apply(_style with { Ratio = ratio });
            };
            _ratioButtons[ratio] = button;
            RatioList.Children.Add(button);
        }
    }

    private RadioButton Tile(string group, UIElement content, string? toolTip)
    {
        var tile = new RadioButton { Style = (Style)FindResource("Tile"), GroupName = group, Content = content, Margin = new Thickness(1) };
        if (toolTip is not null)
        {
            tile.ToolTip = toolTip;
            System.Windows.Automation.AutomationProperties.SetName(tile, toolTip);
        }
        return tile;
    }

    // A small drawing of each window frame: its bar, with traffic lights or the Windows buttons; or of the device.
    private static FrameworkElement FrameMiniature(FrameKind frame)
    {
        var black = new SolidColorBrush(Color.FromRgb(0x1A, 0x1A, 0x1D));
        var metal = new LinearGradientBrush(Color.FromRgb(0xE4, 0xE6, 0xEA), Color.FromRgb(0xA8, 0xAD, 0xB5), 90);
        if (frame == FrameKind.Laptop)
        {
            return new StackPanel
            {
                Height = 32,
                Children =
                {
                    new Border { Width = 38, Height = 26, CornerRadius = new CornerRadius(3, 3, 0, 0), Background = black, Padding = new Thickness(2.5, 3, 2.5, 2), Child = new Border { Background = Brushes.White } },
                    new Border { Width = 48, Height = 4, CornerRadius = new CornerRadius(0, 0, 2, 2), Background = metal },
                },
            };
        }
        if (frame == FrameKind.Phone)
        {
            return new Border
            {
                Width = 20,
                Height = 32,
                CornerRadius = new CornerRadius(4),
                Background = metal,
                Padding = new Thickness(1),
                Child = new Border { CornerRadius = new CornerRadius(3.5), Background = black, Padding = new Thickness(1.5), Child = new Border { CornerRadius = new CornerRadius(2), Background = Brushes.White } },
            };
        }
        var window = new Border { Width = 46, Height = 32, CornerRadius = new CornerRadius(4), BorderThickness = new Thickness(1), Background = Brushes.White, ClipToBounds = true };
        window.SetResourceReference(Border.BorderBrushProperty, "ControlStrokeColorDefaultBrush");
        if (frame == FrameKind.None)
            return window;
        var bar = new StackPanel { Orientation = Orientation.Horizontal, Height = frame == FrameKind.Browser ? 9 : 7, Background = new SolidColorBrush(Color.FromRgb(0xEC, 0xEE, 0xF2)), VerticalAlignment = VerticalAlignment.Top };
        if (frame == FrameKind.Windows)
        {
            bar.FlowDirection = FlowDirection.RightToLeft;
            foreach (var _ in Enumerable.Range(0, 3))
                bar.Children.Add(new Border { Width = 3, Height = 3, Margin = new Thickness(1.5, 0, 1.5, 0), BorderThickness = new Thickness(0.8), BorderBrush = Brushes.DimGray, VerticalAlignment = VerticalAlignment.Center });
        }
        else
        {
            foreach (var color in new[] { Color.FromRgb(0xFF, 0x5F, 0x57), Color.FromRgb(0xFE, 0xBC, 0x2E), Color.FromRgb(0x28, 0xC8, 0x40) })
                bar.Children.Add(new System.Windows.Shapes.Ellipse { Width = 3.5, Height = 3.5, Fill = new SolidColorBrush(color), Margin = new Thickness(frame == FrameKind.Browser ? 2 : 1.6, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center });
        }
        window.Child = new Grid { Children = { bar } };
        return window;
    }

    /// <summary>The swatch of a background, as WPF draws it.</summary>
    private Brush BackgroundBrush(BackgroundDefinition background)
    {
        static Color ToColor(uint rgb) => Color.FromRgb((byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb);
        switch (background.Kind)
        {
            case BackgroundKind.Gradient:
            case BackgroundKind.Solid:
                return new LinearGradientBrush(ToColor(background.From), ToColor(background.To), new Point(0, 0), new Point(1, 1));
            case BackgroundKind.Blur:
                return new LinearGradientBrush(new GradientStopCollection
                {
                    new(Color.FromRgb(0xA8, 0xE6, 0xDD), 0), new(Color.FromRgb(0xEE, 0xF3, 0xF8), 0.5), new(Color.FromRgb(0x9D, 0xB8, 0xEE), 1),
                }, new Point(0, 0), new Point(1, 1));
            case BackgroundKind.Transparent:
                // A checkerboard, as image editors show transparency.
                var checker = new DrawingBrush
                {
                    TileMode = TileMode.Tile,
                    Viewport = new Rect(0, 0, 10, 10),
                    ViewportUnits = BrushMappingMode.Absolute,
                    Drawing = new DrawingGroup
                    {
                        Children =
                        {
                            new GeometryDrawing(Brushes.White, null, new RectangleGeometry(new Rect(0, 0, 2, 2))),
                            new GeometryDrawing(new SolidColorBrush(Color.FromRgb(0xD9, 0xDC, 0xE1)), null, new GeometryGroup
                            {
                                Children = { new RectangleGeometry(new Rect(0, 0, 1, 1)), new RectangleGeometry(new Rect(1, 1, 1, 1)) },
                            }),
                        },
                    },
                };
                return checker;
            default:
                return Brushes.Transparent;
        }
    }

    /// <summary>Applies a new style: controls, preview, and kept for the next time.</summary>
    private void Apply(ClipStyle style)
    {
        _style = style.Normalized();
        App.Settings.EditorStyle = _style;
        ShowStyle();
        RequestRender();
    }

    /// <summary>Puts the controls in the state of the style (without applying anything back).</summary>
    private void ShowStyle()
    {
        _syncing = true;
        // A built-in preset first; else the first of the user's that gives this style.
        string? preset = StylePresets.IdOf(_style);
        foreach (var (id, tile) in _presetTiles)
            tile.IsChecked = id == preset;
        int saved = preset is null ? App.Settings.UserPresets.FindIndex(p => StylePresets.Matches(p.Style, _style)) : -1;
        for (int i = 0; i < _userTiles.Count; i++)
            _userTiles[i].IsChecked = i == saved;
        foreach (var (id, tile) in _backgroundTiles)
            tile.IsChecked = id == _style.Background;
        foreach (var (frame, tile) in _frameTiles)
            tile.IsChecked = frame == _style.Frame;
        foreach (var (ratio, button) in _ratioButtons)
            button.IsChecked = ratio == _style.Ratio;

        LightFrame.IsChecked = !_style.DarkFrame;
        DarkFrame.IsChecked = _style.DarkFrame;
        bool device = ClipLayout.IsDevice(_style.Frame);
        FrameOptions.Visibility = _style.Frame == FrameKind.None ? Visibility.Collapsed : Visibility.Visible;
        FrameTextRow.Visibility = device ? Visibility.Collapsed : Visibility.Visible;
        RadiusRow.Visibility = device ? Visibility.Collapsed : Visibility.Visible;
        FrameTextLabel.Text = Strings.Get(_style.Frame == FrameKind.Browser ? "EditorAddress" : "EditorWindowTitle");
        if (FrameTextBox.Text != _style.FrameText)
            FrameTextBox.Text = _style.FrameText;

        PaddingSlider.Value = _style.Padding;
        RadiusSlider.Value = _style.Radius;
        ShadowSlider.Value = _style.Shadow;
        PaddingValue.Text = Strings.Format("EditorPercent", _style.Padding);
        RadiusValue.Text = Strings.Format("EditorPixels", _style.Radius);
        ShadowValue.Text = Strings.Format("EditorPercent", _style.Shadow);

        var turn = (_style.TurnDegrees, _style.TiltDegrees);
        TiltLeft.IsChecked = turn == ClipStyle.TurnLeft;
        TiltNone.IsChecked = turn == (0, 0);
        TiltRight.IsChecked = turn == ClipStyle.TurnRight;
        TurnSlider.Value = _style.TurnDegrees;
        TiltSlider.Value = _style.TiltDegrees;
        TurnValue.Text = Degrees(_style.TurnDegrees);
        TiltValue.Text = Degrees(_style.TiltDegrees);
        FormatPng.IsChecked = _style.Format == ExportFormat.Png;
        FormatJpeg.IsChecked = _style.Format == ExportFormat.Jpeg;
        FormatWebp.IsChecked = _style.Format == ExportFormat.Webp;
        ShowImageSwatch();
        foreach (var (image, style) in _keepBackgroundMiniatures)
        {
            if (!Equals(image.Tag, (_style.Background, _style.BackgroundImage)))
                DrawMiniature(image, style);
        }
        _syncing = false;
    }

    // The swatch of the user's picture shows that picture, once there is one.
    private void ShowImageSwatch()
    {
        if (_imageSwatch is null || _style.BackgroundImage == _imageSwatchPath)
            return;
        _imageSwatchPath = _style.BackgroundImage;
        try
        {
            var thumbnail = new BitmapImage();
            thumbnail.BeginInit();
            thumbnail.UriSource = new Uri(_imageSwatchPath!);
            thumbnail.DecodePixelWidth = 64;
            thumbnail.CacheOption = BitmapCacheOption.OnLoad;
            thumbnail.EndInit();
            _imageSwatch.Background = new ImageBrush(thumbnail) { Stretch = Stretch.UniformToFill };
            _imageSwatch.Child = null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException or ArgumentNullException or UriFormatException or FileFormatException)
        {
        }
    }

    private void Option_Changed(object sender, RoutedEventArgs e)
    {
        if (!_initialized || _syncing)
            return;
        Apply(_style with
        {
            DarkFrame = DarkFrame.IsChecked == true,
            Format = FormatJpeg.IsChecked == true ? ExportFormat.Jpeg : FormatWebp.IsChecked == true ? ExportFormat.Webp : ExportFormat.Png,
        });
    }

    private void Slider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (!_initialized || _syncing)
            return;
        Apply(_style with
        {
            Padding = (int)PaddingSlider.Value,
            Radius = (int)RadiusSlider.Value,
            Shadow = (int)ShadowSlider.Value,
            TurnDegrees = (int)TurnSlider.Value,
            TiltDegrees = (int)TiltSlider.Value,
        });
    }

    // Left, None, Right: quick turns; the sliders then show them and can fine-tune them.
    private void Tilt_Checked(object sender, RoutedEventArgs e)
    {
        if (!_initialized || _syncing)
            return;
        var (turn, tilt) = sender == TiltLeft ? ClipStyle.TurnLeft : sender == TiltRight ? ClipStyle.TurnRight : (0, 0);
        Apply(_style with { TurnDegrees = turn, TiltDegrees = tilt });
    }

    private void AngleSlider_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        ((Slider)sender).Value = 0;
        e.Handled = true;
    }

    // “−16°”, with a real minus sign.
    private static string Degrees(int value) =>
        Strings.Format("EditorDegrees", value < 0 ? "\u2212" + -value : value.ToString(System.Globalization.CultureInfo.CurrentCulture));

    private void FrameTextBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (!_initialized || _syncing || FrameTextBox.Text == _style.FrameText)
            return;
        Apply(_style with { FrameText = FrameTextBox.Text });
    }

    private void Reset_Click(object sender, RoutedEventArgs e) => Apply(StylePresets.Reset(_style));

    private void PickBackgroundImage()
    {
        var dialog = new OpenFileDialog
        {
            Title = Strings.Get("EditorPickBackground"),
            Filter = $"{Strings.Get("FilterImages")}|{Imaging.OpenFilter}",
        };
        if (dialog.ShowDialog(this) == true)
            Apply(_style with { Background = Backgrounds.ImageId, BackgroundImage = dialog.FileName });
        else
            ShowStyle(); // the tile was checked by the click: back to the current background
    }

    // The user's background picture, read once.
    private SKImage? BackgroundImageFor(ClipStyle style)
    {
        if (style.Background != Backgrounds.ImageId || style.BackgroundImage is not { } path)
            return null;
        if (path != _backgroundImagePath)
        {
            _backgroundImage?.Dispose();
            _backgroundImage = null;
            _backgroundImagePath = path;
            try
            {
                _backgroundImage = Imaging.ToSkia(Imaging.Load(path));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException or FileFormatException or ArgumentException)
            {
            }
        }
        return _backgroundImage;
    }

    // ---------------------------------------------------------------- Preview

    private void PreviewHost_SizeChanged(object sender, SizeChangedEventArgs e) => RequestRender();

    // Several changes in a row (a slider dragged) give one drawing, once the UI is idle.
    private void RequestRender()
    {
        if (_renderQueued)
            return;
        _renderQueued = true;
        Dispatcher.BeginInvoke(RenderPreview, DispatcherPriority.Background);
    }

    private void RenderPreview()
    {
        _renderQueued = false;
        if (_capture is null)
        {
            Preview.Source = null;
            return;
        }
        var layout = ClipLayout.Measure(_capture.Width, _capture.Height, _style);
        var dpi = VisualTreeHelper.GetDpi(this);
        double width = PreviewHost.ActualWidth * dpi.DpiScaleX, height = PreviewHost.ActualHeight * dpi.DpiScaleY;
        if (width < 16 || height < 16)
            return;

        // Drawn at the size it is shown, in screen pixels: sharp, and quick enough to follow a slider.
        float scale = (float)Math.Min(1, Math.Min(width / layout.Width, height / layout.Height));
        using var picture = Composer.Render(_capture, _style, scale, BackgroundImageFor(_style));
        _previewBitmap = Imaging.CopyTo(picture, _previewBitmap);
        Preview.Source = _previewBitmap;
        Preview.Width = picture.Width / dpi.DpiScaleX;
        Preview.Height = picture.Height / dpi.DpiScaleY;
        OutputText.Text = Strings.Format("EditorOutput", layout.Width, layout.Height, FormatName(_style.Format));
    }

    private static string FormatName(ExportFormat format) => format switch
    {
        ExportFormat.Jpeg => "JPG",
        ExportFormat.Webp => "WebP",
        _ => "PNG",
    };

    // ---------------------------------------------------------------- Export

    private SKImage RenderFull() => Composer.Render(_capture!, _style, 1, BackgroundImageFor(_style));

    private string FileName => FileNaming.Expand(App.Settings.EffectiveFileNameTemplate, _takenAt);

    private void Copy_Click(object sender, RoutedEventArgs e) => Copy();

    // PNG for the apps that read it (transparency kept), and a bitmap on white for the others.
    private async void Copy()
    {
        if (_capture is null)
            return;
        using var picture = RenderFull();
        var data = new DataObject();
        data.SetData("PNG", new MemoryStream(Composer.Encode(picture, ExportFormat.Png)), autoConvert: false);
        using (var flat = Composer.OnWhite(picture))
            data.SetImage(Imaging.ToWpf(flat));
        bool copied = ClipActions.Copy(data);
        ShowStatus(Strings.Get(copied ? "EditorCopied" : "CopyFailed"), null);
        CopyText.Text = Strings.Get(copied ? "Copied" : "Copy");
        await Task.Delay(1500);
        CopyText.Text = Strings.Get("Copy");
    }

    private void Save_Click(object sender, RoutedEventArgs e) => Save();

    private void Save()
    {
        if (_capture is null)
            return;
        // The pictures laid out go apart from the captures (Settings › Saving).
        string folder = App.Settings.EffectiveExportFolder;
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
            Filter = $"{Strings.Get("FilterPng")}|*.png|{Strings.Get("FilterJpeg")}|*.jpg;*.jpeg|{Strings.Get("FilterWebp")}|*.webp",
            FilterIndex = (int)_style.Format + 1,
            FileName = FileName + Composer.Extension(_style.Format),
            InitialDirectory = folder,
            AddExtension = true,
            DefaultExt = Composer.Extension(_style.Format),
        };
        if (dialog.ShowDialog(this) != true)
            return;

        // The extension typed wins over the list; the format chosen becomes the one of the style.
        var format = Path.GetExtension(dialog.FileName).ToLowerInvariant() switch
        {
            ".jpg" or ".jpeg" => ExportFormat.Jpeg,
            ".webp" => ExportFormat.Webp,
            ".png" => ExportFormat.Png,
            _ => (ExportFormat)(dialog.FilterIndex - 1),
        };
        if (format != _style.Format)
            Apply(_style with { Format = format });
        try
        {
            using var picture = RenderFull();
            File.WriteAllBytes(dialog.FileName, Composer.Encode(picture, format));
            ShowStatus(Strings.Format("EditorSaved", Path.GetFileName(dialog.FileName)), dialog.FileName);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            MessageBox.Show(this, Strings.Format("ErrorSave", ex.Message), "ScreenClip", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    /// <summary>What happened last, under the preview; with a link to the file when one was written.</summary>
    private void ShowStatus(string text, string? path)
    {
        _statusPath = path;
        StatusText.Text = text;
        StatusText.Visibility = Visibility.Visible;
        StatusLink.Visibility = path is null ? Visibility.Collapsed : Visibility.Visible;
    }

    private void HideStatus() => StatusText.Visibility = StatusLink.Visibility = Visibility.Collapsed;

    private void StatusLink_Click(object sender, RoutedEventArgs e)
    {
        if (_statusPath is not null)
            ClipActions.ShowInExplorer(_statusPath);
    }

    // ---------------------------------------------------------------- Drag and drop

    private void Preview_MouseLeftButtonDown(object sender, MouseButtonEventArgs e) => _pressedAt = e.GetPosition(this);

    // The laid-out picture dragged to an app or a folder: a file in the export format, and the picture.
    private void Preview_MouseMove(object sender, MouseEventArgs e)
    {
        if (_pressedAt is not { } start || e.LeftButton != MouseButtonState.Pressed || _capture is null)
        {
            _pressedAt = null;
            return;
        }
        var delta = e.GetPosition(this) - start;
        if (Math.Abs(delta.X) < SystemParameters.MinimumHorizontalDragDistance && Math.Abs(delta.Y) < SystemParameters.MinimumVerticalDragDistance)
            return;
        _pressedAt = null;

        using var picture = RenderFull();
        Directory.CreateDirectory(ClipActions.TempFolder);
        string path = FileNaming.UniquePath(ClipActions.TempFolder, FileName, Composer.Extension(_style.Format));
        File.WriteAllBytes(path, Composer.Encode(picture, _style.Format));
        var data = new DataObject();
        data.SetFileDropList(new StringCollection { path });
        using (var flat = Composer.OnWhite(picture))
            data.SetImage(Imaging.ToWpf(flat));
        _draggingOut = true;
        try
        {
            DragDrop.DoDragDrop(Preview, data, DragDropEffects.Copy);
        }
        finally
        {
            _draggingOut = false;
        }
    }

    // A picture dropped on the editor (a file, or a picture from a browser) is opened.
    private void Editor_DragOver(object sender, DragEventArgs e)
    {
        e.Effects = !_draggingOut && (DroppedFile(e.Data) is not null || e.Data.GetDataPresent(DataFormats.Bitmap))
            ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    private void Editor_Drop(object sender, DragEventArgs e)
    {
        if (_draggingOut)
            return;
        if (DroppedFile(e.Data) is { } file)
            LoadFile(file);
        else if (e.Data.GetData(DataFormats.Bitmap) is BitmapSource bitmap)
            Load(bitmap, DateTime.Now);
        Activate();
    }

    private static string? DroppedFile(IDataObject data) =>
        data.GetData(DataFormats.FileDrop) is string[] files ? files.FirstOrDefault(Imaging.IsPictureFile) : null;

    // ---------------------------------------------------------------- Keys and other

    private void Editor_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (Keyboard.Modifiers != ModifierKeys.Control)
            return;
        bool inTextBox = Keyboard.FocusedElement is TextBox;
        switch (e.Key)
        {
            case Key.O:
                Import();
                e.Handled = true;
                break;
            case Key.V when !inTextBox:
                Paste();
                e.Handled = true;
                break;
            case Key.C when !inTextBox:
                Copy();
                e.Handled = true;
                break;
            case Key.S:
                Save();
                e.Handled = true;
                break;
        }
    }

    private void Settings_Click(object sender, RoutedEventArgs e) => App.OpenSettings();

    private static string Capitalize(string id) => char.ToUpperInvariant(id[0]) + id[1..];
}
