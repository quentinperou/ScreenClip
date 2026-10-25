using System.Text.RegularExpressions;
using System.Xml.Linq;
using ScreenClip.Core;
using ScreenClip.Core.Annotations;
using ScreenClip.Core.Composition;

namespace ScreenClip.Tests;

/// <summary>Keeps the English and French UI strings (src/ScreenClip/Properties/*.resx) in step with each other and with the code.</summary>
public class LocalizationTests
{
    private static readonly string AppDir = Path.Combine(FindRepositoryRoot(), "src", "ScreenClip");

    private static string FindRepositoryRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "ScreenClip.slnx")))
            dir = dir.Parent;
        return dir?.FullName ?? throw new DirectoryNotFoundException("Repository root (ScreenClip.slnx) not found.");
    }

    private static Dictionary<string, string> ReadStrings(string fileName) =>
        XDocument.Load(Path.Combine(AppDir, "Properties", fileName)).Root!
            .Elements("data")
            .ToDictionary(d => (string)d.Attribute("name")!, d => (string)d.Element("value")!);

    private static HashSet<string> Placeholders(string text) =>
        Regex.Matches(text, @"\{\d+\}").Select(m => m.Value).ToHashSet();

    [Fact]
    public void FrenchTranslation_HasExactlyTheEnglishKeys()
    {
        Assert.Equal(
            ReadStrings("Strings.resx").Keys.Order(),
            ReadStrings("Strings.fr.resx").Keys.Order());
    }

    [Fact]
    public void Translations_UseTheSamePlaceholders()
    {
        var french = ReadStrings("Strings.fr.resx");
        foreach (var (key, english) in ReadStrings("Strings.resx"))
            Assert.True(Placeholders(english).SetEquals(Placeholders(french[key])), $"Placeholders differ for \"{key}\".");
    }

    [Fact]
    public void EveryKeyUsedByTheApp_Exists()
    {
        var keys = ReadStrings("Strings.resx").Keys.ToHashSet();
        var sources = Directory.EnumerateFiles(AppDir, "*", SearchOption.AllDirectories)
            .Where(f => f.EndsWith(".cs") || f.EndsWith(".xaml"))
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")
                     && !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}"))
            .Select(File.ReadAllText)
            .ToList();
        var used = sources
            .SelectMany(text => Regex.Matches(text, @"\{local:Tr (\w+)\}|Strings\.(?:Get|Format|Find)\(""(\w+)""[,)]|SetResourceReference\(\w+, ""(\w+)""\)"))
            .Select(m => m.Groups.Values.Skip(1).First(g => g.Success).Value)
            .Where(key => !key.EndsWith("Style") && !key.EndsWith("Bar") && !key.EndsWith("Card"))
            .ToList();
        // Strings.Get(condition ? "X" : "Y") inside a call.
        used.AddRange(sources
            .SelectMany(text => Regex.Matches(text, @"Strings\.(?:Get|Format)\(\w+ \? ""(\w+)"" : ""(\w+)""\)"))
            .SelectMany(m => new[] { m.Groups[1].Value, m.Groups[2].Value }));

        Assert.NotEmpty(used);
        Assert.All(used, key => Assert.Contains(key, keys));
    }

    [Fact]
    public void KeysBuiltAtRunTime_Exist()
    {
        var keys = ReadStrings("Strings.resx").Keys.ToHashSet();
        // Strings.Get("Shortcut" + action): one per capture action.
        foreach (string action in new[] { "Area", "Window", "Screen", "RepeatArea", "AllScreens", "Video" })
            Assert.Contains("Shortcut" + action, keys);
        // The editor: a name per background swatch, preset and window frame.
        static string Capitalize(string id) => char.ToUpperInvariant(id[0]) + id[1..];
        foreach (var background in Backgrounds.All)
            Assert.Contains("Background" + Capitalize(background.Id), keys);
        foreach (var (id, _) in StylePresets.All)
            Assert.Contains("Preset" + Capitalize(id), keys);
        foreach (var frame in Enum.GetValues<FrameKind>())
            Assert.Contains("Frame" + frame, keys);
        // The annotation tools, their options and colors.
        foreach (string tool in Enum.GetNames<AnnotationKind>().Append("Select").Append("Crop"))
        {
            Assert.Contains("Tool" + tool, keys);
            Assert.Contains("Tool" + tool + "Help", keys);
        }
        foreach (var (name, _) in Palette.All)
            Assert.Contains("Color" + name, keys);
        foreach (var size in Enum.GetValues<StrokeSize>())
        {
            Assert.Contains("Size" + size, keys);
            Assert.Contains("Scale" + size, keys);
        }
        foreach (var style in Enum.GetValues<ShapeStyle>())
            Assert.Contains("Shape" + style, keys);
        foreach (var style in Enum.GetValues<ArrowStyle>())
            Assert.Contains("Arrow" + style, keys);
        foreach (var style in Enum.GetValues<TextStyle>())
            Assert.Contains("TextStyle" + style, keys);
        foreach (var style in Enum.GetValues<MaskStyle>())
            Assert.Contains("Mask" + style, keys);
        foreach (var style in Enum.GetValues<PointerStyle>())
            Assert.Contains("Pointer" + style, keys);
        // ShortcutText: the keys that read differently in French (modifiers, Print Screen, editing keys).
        foreach (string part in new[] { "Ctrl", "Alt", "Shift", "Win", "PrintScreen", "Space", "Delete", "Insert", "Home", "End", "PageUp", "PageDown", "Pause" })
        {
            Assert.Contains("Key" + part, keys);
            Assert.True(Hotkey.TryParse(part is "Ctrl" or "Alt" or "Shift" or "Win" ? part + "+F1" : part, out _), part);
        }
    }
}
