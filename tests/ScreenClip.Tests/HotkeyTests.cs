using ScreenClip.Core;

namespace ScreenClip.Tests;

public class HotkeyTests
{
    [Theory]
    [InlineData("PrintScreen", HotkeyModifiers.None, 0x2C)]
    [InlineData("Alt+PrintScreen", HotkeyModifiers.Alt, 0x2C)]
    [InlineData("Ctrl+Shift+S", HotkeyModifiers.Control | HotkeyModifiers.Shift, 0x53)]
    [InlineData("win + f12", HotkeyModifiers.Windows, 0x7B)]
    [InlineData("Ctrl+Alt+7", HotkeyModifiers.Control | HotkeyModifiers.Alt, 0x37)]
    [InlineData("Shift+PageDown", HotkeyModifiers.Shift, 0x22)]
    public void Parse_ReadsModifiersAndKey(string text, HotkeyModifiers modifiers, int key)
    {
        Assert.True(Hotkey.TryParse(text, out var hotkey));
        Assert.Equal(new Hotkey(modifiers, key), hotkey);
    }

    [Theory]
    [InlineData("Ctrl+Shift+S")]
    [InlineData("Alt+PrintScreen")]
    [InlineData("Ctrl+Alt+Shift+Win+F24")]
    [InlineData("Pause")]
    public void ToString_GivesBackTheText(string text)
    {
        Assert.Equal(text, Hotkey.ParseOr(text, Hotkey.None).ToString());
    }

    [Fact]
    public void Modifiers_AreWrittenInWindowsOrder()
    {
        Assert.Equal("Ctrl+Alt+Shift+Win+A", new Hotkey(HotkeyModifiers.Windows | HotkeyModifiers.Shift | HotkeyModifiers.Alt | HotkeyModifiers.Control, 0x41).ToString());
    }

    [Fact]
    public void EmptyText_IsNoShortcut()
    {
        Assert.True(Hotkey.TryParse("", out var hotkey));
        Assert.True(hotkey.IsNone);
        Assert.Equal("", hotkey.ToString());
        Assert.Empty(hotkey.Parts);
    }

    [Theory]
    [InlineData("Ctrl")]
    [InlineData("Ctrl+")]
    [InlineData("A+B")]
    [InlineData("Ctrl+Escape")]
    [InlineData("Hyper+A")]
    public void InvalidText_IsRejected(string text)
    {
        Assert.False(Hotkey.TryParse(text, out _));
        Assert.Equal(Hotkey.None, Hotkey.ParseOr(text, Hotkey.None));
    }

    [Theory]
    [InlineData("PrintScreen", true)]
    [InlineData("Shift+PrintScreen", true)]
    [InlineData("F9", true)]
    [InlineData("Pause", true)]
    [InlineData("A", false)]           // would stop the letter from being typed
    [InlineData("Shift+A", false)]     // capitals
    [InlineData("Space", false)]
    [InlineData("Delete", false)]
    [InlineData("Ctrl+A", true)]
    [InlineData("Alt+Space", true)]
    [InlineData("Win+Shift+X", true)]
    public void IsAllowed_KeepsTypingFree(string text, bool allowed)
    {
        Assert.Equal(allowed, Hotkey.ParseOr(text, Hotkey.None).IsAllowed);
    }

    [Fact]
    public void KeyName_CoversLettersDigitsAndFunctionKeys()
    {
        Assert.Equal("A", Hotkey.KeyName(0x41));
        Assert.Equal("Z", Hotkey.KeyName(0x5A));
        Assert.Equal("0", Hotkey.KeyName(0x30));
        Assert.Equal("F1", Hotkey.KeyName(0x70));
        Assert.Equal("F24", Hotkey.KeyName(0x87));
        Assert.Null(Hotkey.KeyName(0x10)); // Shift itself
        Assert.Null(Hotkey.KeyName(0x01)); // mouse button
    }
}
