namespace Spat.Libraries.Native.Windows;

/// <summary>
/// Turns user-friendly shortcuts like "Ctrl+Alt+Space" into a virtual-key code plus a modifier set.
/// </summary>
internal static class WindowsShortcutParser
{
    public static (HotkeyModifiers Modifiers, uint KeyCode) Parse(string shortcut)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(shortcut);

        var tokens = shortcut.Split('+', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);

        if (tokens.Length == 0)
        {
            throw new FormatException($"The shortcut '{shortcut}' does not name any key.");
        }

        var modifiers = HotkeyModifiers.None;
        uint keyCode = 0;
        var haveKey = false;

        foreach (var token in tokens)
        {
            if (TryReadModifier(token, out var modifier))
            {
                modifiers |= modifier;
                continue;
            }

            if (haveKey)
            {
                throw new FormatException($"The shortcut '{shortcut}' names more than one non-modifier key.");
            }

            if (!TryReadKey(token, out keyCode))
            {
                throw new FormatException($"The shortcut '{shortcut}' uses the unsupported key '{token}'.");
            }

            haveKey = true;
        }

        if (!haveKey)
        {
            throw new FormatException($"The shortcut '{shortcut}' names no key besides modifiers.");
        }

        return (modifiers, keyCode);
    }

    private static bool TryReadModifier(string token, out HotkeyModifiers modifier)
    {
        switch (token.ToLowerInvariant())
        {
            case "ctrl":
            case "control":
                modifier = HotkeyModifiers.Control;
                return true;
            case "alt":
            case "option":
                modifier = HotkeyModifiers.Alt;
                return true;
            case "shift":
                modifier = HotkeyModifiers.Shift;
                return true;
            case "win":
            case "windows":
            case "super":
            case "meta":
            case "logo":
            case "cmd":
            case "command":
                modifier = HotkeyModifiers.Win;
                return true;
            default:
                modifier = HotkeyModifiers.None;
                return false;
        }
    }

    private static bool TryReadKey(string token, out uint keyCode)
    {
        switch (token.ToLowerInvariant())
        {
            case "space": keyCode = VirtualKeys.Space; return true;
            case "tab": keyCode = VirtualKeys.Tab; return true;
            case "enter":
            case "return": keyCode = VirtualKeys.Return; return true;
            case "escape":
            case "esc": keyCode = VirtualKeys.Escape; return true;
            case "backspace": keyCode = VirtualKeys.Back; return true;
            case "delete":
            case "del": keyCode = VirtualKeys.Delete; return true;
            case "insert":
            case "ins": keyCode = VirtualKeys.Insert; return true;
            case "home": keyCode = VirtualKeys.Home; return true;
            case "end": keyCode = VirtualKeys.End; return true;
            case "pageup":
            case "page_up": keyCode = VirtualKeys.Priority; return true;
            case "pagedown":
            case "page_down": keyCode = VirtualKeys.Next; return true;
            case "up": keyCode = VirtualKeys.Up; return true;
            case "down": keyCode = VirtualKeys.Down; return true;
            case "left": keyCode = VirtualKeys.Left; return true;
            case "right": keyCode = VirtualKeys.Right; return true;
            default: break;
        }

        if (token.Length == 1)
        {
            var upper = char.ToUpperInvariant(token[0]);

            if (upper is >= 'A' and <= 'Z')
            {
                keyCode = upper;
                return true;
            }

            if (upper is >= '0' and <= '9')
            {
                keyCode = upper;
                return true;
            }
        }

        if (token.Length is > 1 and <= 3 && (token[0] is 'f' or 'F') && int.TryParse(token.AsSpan(1), out var number))
        {
            if (number is >= 1 and <= 24)
            {
                keyCode = VirtualKeys.F1 + (uint)(number - 1);
                return true;
            }
        }

        keyCode = 0;
        return false;
    }
}

[Flags]
internal enum HotkeyModifiers
{
    None = 0,
    Control = 1,
    Alt = 2,
    Shift = 4,
    Win = 8,
}

/// <summary>
/// The subset of virtual-key codes the shortcut parser can name. Values match WinUser.h.
/// </summary>
internal static class VirtualKeys
{
    public const uint Back = 0x08;
    public const uint Tab = 0x09;
    public const uint Return = 0x0D;
    public const uint Escape = 0x1B;
    public const uint Space = 0x20;
    public const uint Priority = 0x21;
    public const uint Next = 0x22;
    public const uint End = 0x23;
    public const uint Home = 0x24;
    public const uint Left = 0x25;
    public const uint Up = 0x26;
    public const uint Right = 0x27;
    public const uint Down = 0x28;
    public const uint Insert = 0x2D;
    public const uint Delete = 0x2E;
    public const uint LMenu = 0xA4;
    public const uint RMenu = 0xA5;
    public const uint LControl = 0xA2;
    public const uint RControl = 0xA3;
    public const uint LShift = 0xA0;
    public const uint RShift = 0xA1;
    public const uint LWin = 0x5B;
    public const uint RWin = 0x5C;
    public const uint F1 = 0x70;
}
