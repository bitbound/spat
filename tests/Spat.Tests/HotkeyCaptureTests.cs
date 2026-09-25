using Avalonia.Input;
using Spat.Libraries.Native.Windows;
using Spat.Views;

namespace Spat.Tests;

public class HotkeyCaptureTests
{
    [Theory]
    [InlineData(Key.Space, KeyModifiers.Control | KeyModifiers.Alt, "Ctrl+Alt+Space")]
    [InlineData(Key.A, KeyModifiers.Meta, "Super+A")]
    [InlineData(Key.D5, KeyModifiers.Control, "Ctrl+5")]
    [InlineData(Key.Back, KeyModifiers.Control | KeyModifiers.Shift, "Ctrl+Shift+BackSpace")]
    [InlineData(Key.F13, KeyModifiers.Control, "Ctrl+F13")]
    [InlineData(Key.Tab, KeyModifiers.Control, "Ctrl+Tab")]
    [InlineData(Key.Return, KeyModifiers.Alt, "Alt+Return")]
    [InlineData(Key.Escape, KeyModifiers.Control, "Ctrl+Escape")]
    [InlineData(Key.Delete, KeyModifiers.Control | KeyModifiers.Shift, "Ctrl+Shift+Delete")]
    [InlineData(Key.PageUp, KeyModifiers.Control, "Ctrl+PageUp")]
    [InlineData(Key.PageDown, KeyModifiers.Control, "Ctrl+PageDown")]
    [InlineData(Key.Up, KeyModifiers.Control | KeyModifiers.Alt, "Ctrl+Alt+Up")]
    [InlineData(Key.Home, KeyModifiers.None, "Home")]
    public void TryFormat_ReturnsTheSpellingTheShortcutParserWants(Key key, KeyModifiers modifiers, string expected)
    {
        Assert.True(HotkeyKeyFormatter.TryFormat(key, modifiers, out var combo));
        Assert.Equal(expected, combo);
    }

    [Theory]
    [InlineData(Key.None)]
    [InlineData(Key.LeftCtrl)]
    [InlineData(Key.RightAlt)]
    [InlineData(Key.LWin)]
    [InlineData(Key.CapsLock)]
    public void IsWaitingForKey_WhileOnlyModifiersAreDown_ReturnsTrue(Key key)
    {
        Assert.True(HotkeyKeyFormatter.IsWaitingForKey(key));
    }

    [Theory]
    [InlineData(Key.Space)]
    [InlineData(Key.A)]
    [InlineData(Key.D5)]
    public void IsWaitingForKey_OnceARealKeyArrives_ReturnsFalse(Key key)
    {
        Assert.False(HotkeyKeyFormatter.IsWaitingForKey(key));
    }

    // A captured shortcut has to be one the Windows hotkey source can register, so nothing the
    // capture UI can produce may fall outside the shortcut vocabulary the parser understands.
    [Theory]
    [InlineData(Key.Space, KeyModifiers.Control, "Ctrl+Space", (int)HotkeyModifiers.Control, VirtualKeys.Space)]
    [InlineData(Key.A, KeyModifiers.Meta, "Super+A", (int)HotkeyModifiers.Win, 0x41)]
    [InlineData(Key.D5, KeyModifiers.Control, "Ctrl+5", (int)HotkeyModifiers.Control, 0x35)]
    [InlineData(Key.Back, KeyModifiers.Control, "Ctrl+BackSpace", (int)HotkeyModifiers.Control, VirtualKeys.Back)]
    [InlineData(Key.F24, KeyModifiers.Alt, "Alt+F24", (int)HotkeyModifiers.Alt, 0x87)]
    [InlineData(Key.Return, KeyModifiers.None, "Return", (int)HotkeyModifiers.None, VirtualKeys.Return)]
    [InlineData(Key.PageUp, KeyModifiers.Control, "Ctrl+PageUp", (int)HotkeyModifiers.Control, 0x21)]
    [InlineData(Key.PageDown, KeyModifiers.Control, "Ctrl+PageDown", (int)HotkeyModifiers.Control, 0x22)]
    public void CapturedCombo_IsAcceptedByTheWindowsShortcutParser(
        Key key,
        KeyModifiers modifiers,
        string expectedCombo,
        int expectedFlags,
        uint expectedVirtualKey)
    {
        Assert.True(HotkeyKeyFormatter.TryFormat(key, modifiers, out var combo));
        Assert.Equal(expectedCombo, combo);

        var parsed = WindowsShortcutParser.Parse(combo);

        Assert.Equal((HotkeyModifiers)expectedFlags, parsed.Modifiers);
        Assert.Equal(expectedVirtualKey, parsed.KeyCode);
    }
}
