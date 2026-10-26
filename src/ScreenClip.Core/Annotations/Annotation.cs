using System.Collections.Immutable;
using System.Text.Json.Serialization;
using SkiaSharp;

namespace ScreenClip.Core.Annotations;

/// <summary>What an annotation draws on the capture.</summary>
public enum AnnotationKind
{
    Arrow,
    Rectangle,
    Ellipse,
    Line,
    Pen,
    Marker,
    Text,
    Number,
    Pointer,
    Mask,
}

/// <summary>Thickness of a stroke, or size of a text or a number.</summary>
public enum StrokeSize
{
    Small,
    Medium,
    Large,
}

/// <summary>A rectangle or an ellipse: its outline, filled, or the rest of the capture darkened.</summary>
public enum ShapeStyle
{
    Outline,
    Filled,
    Spotlight,
}

public enum ArrowStyle
{
    Straight,
    Double,
    Curved,
}

/// <summary>A text alone (outlined so that it reads on anything), on a label, or in a speech bubble.</summary>
public enum TextStyle
{
    Plain,
    Label,
    Bubble,
}

/// <summary>How a mask hides what is under it. A solid block is the only one that leaves nothing to guess.</summary>
public enum MaskStyle
{
    Pixelate,
    Blur,
    Solid,
}

/// <summary>The mouse pointer drawn on the capture (the real one is never captured).</summary>
public enum PointerStyle
{
    Windows,
    Mac,
    Hand,
}

/// <summary>What a click of a pointer shows in a video: a yellow halo, a ring that spreads, or the pointer pressed.</summary>
public enum ClickEffect
{
    None,
    Halo,
    Ripple,
    Press,
}

/// <summary>
/// How the pointer recorded with a video is drawn: as it was (its real shapes: arrow, text beam, hand…), in one of the
/// drawn styles, or not at all.
/// </summary>
public enum RecordedPointerStyle
{
    AsRecorded,
    Windows,
    Mac,
    Hand,
    Hidden,
}

/// <summary>When an annotation of a video shows, in the video's time: from <see cref="Start"/> until <see cref="End"/>.</summary>
public readonly record struct ShowTime(TimeSpan Start, TimeSpan End)
{
    /// <summary>The shortest time an annotation shows.</summary>
    public static readonly TimeSpan MinLength = TimeSpan.FromMilliseconds(100);

    public TimeSpan Length => End - Start;

    public bool Contains(TimeSpan time) => time >= Start && time < End;
}

/// <summary>
/// A pointer of a video that moves (the A → B pointer of the mock-up): from <see cref="From"/> to the annotation's own
/// point, leaving at <see cref="Leave"/> and arriving at <see cref="Arrive"/>, along a curve bent by two handles or
/// straight, eased or steady, with a click when it arrives.
/// </summary>
public sealed record PointerMotion
{
    public SKPoint From { get; init; }

    public SKPoint Control1 { get; init; }

    public SKPoint Control2 { get; init; }

    public bool Curved { get; init; } = true;

    /// <summary>Slow at both ends (as a hand moves), else at the same speed all the way.</summary>
    public bool Eased { get; init; } = true;

    public TimeSpan Leave { get; init; }

    public TimeSpan Arrive { get; init; }

    public ClickEffect Click { get; init; } = ClickEffect.Halo;

    public PointerMotion MovedBy(float dx, float dy) => this with
    {
        From = new SKPoint(From.X + dx, From.Y + dy),
        Control1 = new SKPoint(Control1.X + dx, Control1.Y + dy),
        Control2 = new SKPoint(Control2.X + dx, Control2.Y + dy),
    };
}

/// <summary>Where a click is in its effect: seconds since the button went down, and since it went up (null: still down).</summary>
public readonly record struct ClickPhase(double SinceDown, double? SinceUp);

/// <summary>The options of the pointer recorded with a video (its own layer in the editor).</summary>
public sealed record RecordedPointerOptions
{
    public const int MinSize = 50, MaxSize = 400;

    [JsonConverter(typeof(JsonStringEnumConverter<RecordedPointerStyle>))]
    public RecordedPointerStyle Style { get; init; }

    /// <summary>Its size, in percent of the size it had on the screen.</summary>
    public int Size { get; init; } = 100;

    /// <summary>The small shakes of the hand smoothed out (as Screen Studio does).</summary>
    public bool Smooth { get; init; }

    [JsonConverter(typeof(JsonStringEnumConverter<ClickEffect>))]
    public ClickEffect Click { get; init; } = ClickEffect.Halo;

    /// <summary>Hidden by its eye in the list, its style kept for when it shows again.</summary>
    public bool Hidden { get; init; }

    /// <summary>It is drawn: not hidden by its eye nor by the hidden style of old.</summary>
    [JsonIgnore]
    public bool Shows => !Hidden && Style != RecordedPointerStyle.Hidden;

    public RecordedPointerOptions Normalized() => this with
    {
        Style = Enum.IsDefined(Style) && Style != RecordedPointerStyle.Hidden ? Style : RecordedPointerStyle.AsRecorded,
        Hidden = Hidden || Style == RecordedPointerStyle.Hidden,
        Size = Math.Clamp(Size, MinSize, MaxSize),
        Click = Enum.IsDefined(Click) ? Click : ClickEffect.Halo,
    };
}

/// <summary>
/// One annotation, in the pixels of the whole capture (a crop only hides what is outside). Immutable: an edit makes a
/// new one, which keeps the undo history simple.
/// </summary>
public sealed record Annotation
{
    /// <summary>Unique within its <see cref="AnnotationLayer"/>: what the selection follows.</summary>
    public int Id { get; init; }

    public AnnotationKind Kind { get; init; }

    /// <summary>
    /// Start and end (arrow, line, rectangle, ellipse, mask), the stroke (pen, marker), or one point: the top left
    /// corner of a text, the center of a number, the tip of a pointer.
    /// </summary>
    public SKPoint[] Points { get; init; } = [];

    /// <summary>ARGB.</summary>
    public uint Color { get; init; } = Palette.Red;

    public StrokeSize Size { get; init; } = StrokeSize.Medium;

    public ShapeStyle Shape { get; init; }

    public ArrowStyle Arrow { get; init; }

    /// <summary>How far a curved arrow bends: the offset of its middle, as a share of its length (the sign: the side).</summary>
    public float Bend { get; init; } = 0.3f;

    public TextStyle TextStyle { get; init; } = TextStyle.Label;

    public string Text { get; init; } = "";

    public MaskStyle Mask { get; init; }

    public int Number { get; init; } = 1;

    public PointerStyle Pointer { get; init; }

    /// <summary>Height of the pointer in pixels at 100 % (it grows with large captures, as the strokes do).</summary>
    public int PointerSize { get; init; } = AnnotationDefaults.DefaultPointerSize;

    /// <summary>A yellow ring under the pointer's tip: “click here”.</summary>
    public bool Halo { get; init; } = true;

    /// <summary>In a video, when it shows; null: all the time.</summary>
    public ShowTime? Time { get; init; }

    /// <summary>Hidden by its eye in the list (asked by the author): kept, but neither drawn nor picked.</summary>
    public bool Hidden { get; init; }

    /// <summary>In a video, a pointer that moves to its point (<see cref="Start"/>) from another one.</summary>
    public PointerMotion? Motion { get; init; }

    /// <summary>
    /// Only on a pointer posed at a moment of a video (<see cref="VideoTiming.Posed"/>): its click then, drawn with the
    /// effect of its motion.
    /// </summary>
    public ClickPhase? Click { get; init; }

    public SKPoint Start => Points.Length > 0 ? Points[0] : default;

    public SKPoint End => Points.Length > 0 ? Points[^1] : default;

    /// <summary>The rectangle of a two-point annotation (its corners in any order).</summary>
    public SKRect Rect => AnnotationGeometry.RectOf(Start, End);

    public bool IsShownAt(TimeSpan time) => !Hidden && (Time is not { } shown || shown.Contains(time));

    public Annotation MovedBy(float dx, float dy) => this with
    {
        Points = [.. Points.Select(p => new SKPoint(p.X + dx, p.Y + dy))],
        Motion = Motion?.MovedBy(dx, dy),
    };
}

/// <summary>The colors offered for annotations, ARGB.</summary>
public static class Palette
{
    public const uint Red = 0xFFE5484D;
    public const uint Orange = 0xFFF76B15;
    public const uint Yellow = 0xFFFFC53D;
    public const uint Green = 0xFF30A46C;
    public const uint Blue = 0xFF0090FF;
    public const uint Violet = 0xFF8E4EC6;
    public const uint Black = 0xFF1B1B1B;
    public const uint White = 0xFFFFFFFF;

    /// <summary>In the order of the swatches; the names are the keys of their labels (“ColorRed”…).</summary>
    public static readonly (string Name, uint Color)[] All =
    [
        ("Red", Red), ("Orange", Orange), ("Yellow", Yellow), ("Green", Green),
        ("Blue", Blue), ("Violet", Violet), ("Black", Black), ("White", White),
    ];

    /// <summary>A light color takes dark text on it (yellow, white), the others white text.</summary>
    public static bool IsLight(uint color)
    {
        double r = (color >> 16) & 0xFF, g = (color >> 8) & 0xFF, b = color & 0xFF;
        return 0.299 * r + 0.587 * g + 0.114 * b > 170;
    }
}

/// <summary>Which options an annotation has: the editor shows only those.</summary>
public static class AnnotationOptions
{
    public static bool HasColor(AnnotationKind kind) => kind is not (AnnotationKind.Pointer or AnnotationKind.Mask);

    public static bool HasSize(AnnotationKind kind) => kind is not (AnnotationKind.Pointer or AnnotationKind.Mask);

    public static bool HasStyle(AnnotationKind kind) =>
        kind is AnnotationKind.Arrow or AnnotationKind.Rectangle or AnnotationKind.Ellipse or AnnotationKind.Text
            or AnnotationKind.Mask or AnnotationKind.Pointer;

    /// <summary>Drawn by dragging from one point to another (a click alone draws nothing).</summary>
    public static bool IsDragged(AnnotationKind kind) =>
        kind is AnnotationKind.Arrow or AnnotationKind.Rectangle or AnnotationKind.Ellipse or AnnotationKind.Line
            or AnnotationKind.Mask;

    /// <summary>Drawn freehand.</summary>
    public static bool IsStroke(AnnotationKind kind) => kind is AnnotationKind.Pen or AnnotationKind.Marker;
}

/// <summary>
/// The options of the tools, as last used: a new annotation takes them, and changing the one selected changes them
/// too. Kept in the settings.
/// </summary>
public sealed record AnnotationDefaults
{
    public const int DefaultPointerSize = 48, MinPointerSize = 24, MaxPointerSize = 160;

    public uint Color { get; init; } = Palette.Red;

    /// <summary>The highlighter has its own color: yellow, while the arrows are red.</summary>
    public uint MarkerColor { get; init; } = Palette.Yellow;

    [JsonConverter(typeof(JsonStringEnumConverter<StrokeSize>))]
    public StrokeSize Size { get; init; } = StrokeSize.Medium;

    [JsonConverter(typeof(JsonStringEnumConverter<StrokeSize>))]
    public StrokeSize MarkerSize { get; init; } = StrokeSize.Medium;

    [JsonConverter(typeof(JsonStringEnumConverter<StrokeSize>))]
    public StrokeSize TextSize { get; init; } = StrokeSize.Medium;

    [JsonConverter(typeof(JsonStringEnumConverter<StrokeSize>))]
    public StrokeSize NumberSize { get; init; } = StrokeSize.Medium;

    [JsonConverter(typeof(JsonStringEnumConverter<ShapeStyle>))]
    public ShapeStyle Shape { get; init; }

    [JsonConverter(typeof(JsonStringEnumConverter<ArrowStyle>))]
    public ArrowStyle Arrow { get; init; }

    [JsonConverter(typeof(JsonStringEnumConverter<TextStyle>))]
    public TextStyle Text { get; init; } = TextStyle.Label;

    [JsonConverter(typeof(JsonStringEnumConverter<MaskStyle>))]
    public MaskStyle Mask { get; init; }

    [JsonConverter(typeof(JsonStringEnumConverter<PointerStyle>))]
    public PointerStyle Pointer { get; init; }

    public int PointerSize { get; init; } = DefaultPointerSize;

    public bool Halo { get; init; } = true;

    /// <summary>The A → B pointer of a video: a curve or straight, eased or steady, and its click.</summary>
    public bool MotionCurved { get; init; } = true;

    public bool MotionEased { get; init; } = true;

    [JsonConverter(typeof(JsonStringEnumConverter<ClickEffect>))]
    public ClickEffect MotionClick { get; init; } = ClickEffect.Halo;

    /// <summary>Values out of range (an old or edited settings file) back to something drawable.</summary>
    public AnnotationDefaults Normalized() => this with
    {
        MotionClick = Valid(MotionClick, ClickEffect.Halo),
        Size = Valid(Size, StrokeSize.Medium),
        MarkerSize = Valid(MarkerSize, StrokeSize.Medium),
        TextSize = Valid(TextSize, StrokeSize.Medium),
        NumberSize = Valid(NumberSize, StrokeSize.Medium),
        Shape = Valid(Shape, ShapeStyle.Outline),
        Arrow = Valid(Arrow, ArrowStyle.Straight),
        Text = Valid(Text, TextStyle.Label),
        Mask = Valid(Mask, MaskStyle.Pixelate),
        Pointer = Valid(Pointer, PointerStyle.Windows),
        PointerSize = Math.Clamp(PointerSize, MinPointerSize, MaxPointerSize),
        Color = Color | 0xFF000000,
        MarkerColor = MarkerColor | 0xFF000000,
    };

    private static T Valid<T>(T value, T fallback) where T : struct, Enum => Enum.IsDefined(value) ? value : fallback;

    /// <summary>A new annotation of <paramref name="kind"/> with these options.</summary>
    public Annotation Create(AnnotationKind kind, params SKPoint[] points) => new()
    {
        Kind = kind,
        Points = points,
        Color = kind == AnnotationKind.Marker ? MarkerColor : Color,
        Size = kind switch
        {
            AnnotationKind.Marker => MarkerSize,
            AnnotationKind.Text => TextSize,
            AnnotationKind.Number => NumberSize,
            _ => Size,
        },
        Shape = Shape,
        Arrow = Arrow,
        TextStyle = Text,
        Mask = Mask,
        Pointer = Pointer,
        PointerSize = PointerSize,
        Halo = Halo,
    };

    /// <summary>The options of <paramref name="annotation"/> become those of its tool.</summary>
    public AnnotationDefaults Follow(Annotation annotation) => annotation.Kind switch
    {
        AnnotationKind.Marker => this with { MarkerColor = annotation.Color, MarkerSize = annotation.Size },
        AnnotationKind.Text => this with { Color = annotation.Color, TextSize = annotation.Size, Text = annotation.TextStyle },
        AnnotationKind.Number => this with { Color = annotation.Color, NumberSize = annotation.Size },
        AnnotationKind.Arrow => this with { Color = annotation.Color, Size = annotation.Size, Arrow = annotation.Arrow },
        AnnotationKind.Rectangle or AnnotationKind.Ellipse => this with { Color = annotation.Color, Size = annotation.Size, Shape = annotation.Shape },
        AnnotationKind.Mask => this with { Mask = annotation.Mask },
        AnnotationKind.Pointer when annotation.Motion is { } motion => this with
        {
            Pointer = annotation.Pointer, PointerSize = annotation.PointerSize,
            MotionCurved = motion.Curved, MotionEased = motion.Eased, MotionClick = motion.Click,
        },
        AnnotationKind.Pointer => this with { Pointer = annotation.Pointer, PointerSize = annotation.PointerSize, Halo = annotation.Halo },
        _ => this with { Color = annotation.Color, Size = annotation.Size },
    };
}

/// <summary>
/// The annotations of a picture, in drawing order (the last one on top), and its crop. Immutable, like its
/// annotations: each edit is a new layer, kept by <see cref="AnnotationHistory"/>.
/// </summary>
public sealed record AnnotationLayer
{
    public static readonly AnnotationLayer Empty = new();

    public ImmutableList<Annotation> Items { get; init; } = [];

    /// <summary>The part of the capture kept, in its pixels; null: all of it.</summary>
    public PixelRect? Crop { get; init; }

    /// <summary>The id the next annotation gets.</summary>
    public int NextId { get; init; } = 1;

    /// <summary>A video's recorded pointer: how it is drawn (here, so that its changes are undone as the others).</summary>
    public RecordedPointerOptions Cursor { get; init; } = new();

    public bool IsEmpty => Items.IsEmpty && Crop is null;

    /// <summary>The part of a <paramref name="width"/> × <paramref name="height"/> capture that is shown and exported.</summary>
    public PixelRect Visible(int width, int height)
    {
        var all = new PixelRect(0, 0, width, height);
        if (Crop is not { } crop)
            return all;
        var kept = crop.Intersect(all);
        return kept.IsEmpty ? all : kept;
    }

    /// <summary>The layer with <paramref name="annotation"/> on top, given the next id (returned in <paramref name="added"/>).</summary>
    public AnnotationLayer Add(Annotation annotation, out Annotation added)
    {
        added = annotation with { Id = NextId };
        return this with { Items = Items.Add(added), NextId = NextId + 1 };
    }

    public Annotation? Find(int id) => Items.FirstOrDefault(a => a.Id == id);

    /// <summary>The layer with the annotation of the same id replaced by <paramref name="annotation"/>.</summary>
    public AnnotationLayer Replace(Annotation annotation)
    {
        int index = Items.FindIndex(a => a.Id == annotation.Id);
        return index < 0 ? this : this with { Items = Items.SetItem(index, annotation) };
    }

    public AnnotationLayer Remove(int id)
    {
        int index = Items.FindIndex(a => a.Id == id);
        return index < 0 ? this : this with { Items = Items.RemoveAt(index) };
    }

    /// <summary>
    /// The annotation moved to <paramref name="index"/> in the drawing order (0: under all the others), the others
    /// keeping theirs.
    /// </summary>
    public AnnotationLayer MoveTo(int id, int index)
    {
        int from = Items.FindIndex(a => a.Id == id);
        if (from < 0)
            return this;
        index = Math.Clamp(index, 0, Items.Count - 1);
        return index == from ? this : this with { Items = Items.RemoveAt(from).Insert(index, Items[from]) };
    }

    /// <summary>The annotation moved on top of the others.</summary>
    public AnnotationLayer BringToFront(int id)
    {
        int index = Items.FindIndex(a => a.Id == id);
        return index < 0 || index == Items.Count - 1 ? this : this with { Items = Items.RemoveAt(index).Add(Items[index]) };
    }

    /// <summary>The number the next “Number” annotation shows: one more than the highest.</summary>
    public int NextNumber => Items.Where(a => a.Kind == AnnotationKind.Number).Select(a => a.Number).DefaultIfEmpty(0).Max() + 1;

    /// <summary>The annotation on top under <paramref name="point"/>, if any.</summary>
    public Annotation? HitTest(SKPoint point, float scale, float tolerance)
    {
        for (int i = Items.Count - 1; i >= 0; i--)
        {
            if (!Items[i].Hidden && AnnotationGeometry.HitTest(Items[i], point, scale, tolerance))
                return Items[i];
        }
        return null;
    }
}

/// <summary>
/// Undo and redo for the annotations and the crop: every change is a whole new <see cref="AnnotationLayer"/>.
/// Changes in a row with the same merge key (a slider dragged) make one step.
/// </summary>
public sealed class AnnotationHistory
{
    public const int MaxSteps = 100;

    private readonly List<AnnotationLayer> _undo = [];
    private readonly List<AnnotationLayer> _redo = [];
    private string? _mergeKey;

    public AnnotationLayer Current { get; private set; } = AnnotationLayer.Empty;

    public bool CanUndo => _undo.Count > 0;

    public bool CanRedo => _redo.Count > 0;

    /// <summary>A new picture: its layer, and no history.</summary>
    public void Reset(AnnotationLayer layer)
    {
        Current = layer;
        _undo.Clear();
        _redo.Clear();
        _mergeKey = null;
    }

    public void Commit(AnnotationLayer next, string? mergeKey = null)
    {
        if (ReferenceEquals(next, Current))
            return;
        if (mergeKey is null || mergeKey != _mergeKey || _undo.Count == 0)
        {
            _undo.Add(Current);
            if (_undo.Count > MaxSteps)
                _undo.RemoveAt(0);
        }
        _redo.Clear();
        Current = next;
        _mergeKey = mergeKey;
    }

    public bool Undo()
    {
        if (_undo.Count == 0)
            return false;
        _redo.Add(Current);
        Current = _undo[^1];
        _undo.RemoveAt(_undo.Count - 1);
        _mergeKey = null;
        return true;
    }

    public bool Redo()
    {
        if (_redo.Count == 0)
            return false;
        _undo.Add(Current);
        Current = _redo[^1];
        _redo.RemoveAt(_redo.Count - 1);
        _mergeKey = null;
        return true;
    }
}
