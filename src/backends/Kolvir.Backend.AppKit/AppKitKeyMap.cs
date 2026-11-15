using AppKit;

namespace Kolvir.Backend.AppKit;

/// <summary>
/// Translates between AppKit and the platform independent input types the UI library understands.
/// </summary>
internal static class AppKitKeyMap
{
    /// <summary>
    /// Maps a macOS virtual key code to an <see cref="Key"/>. The numbers are hardware key
    /// codes from Carbon's Events.h, not character codes, so this is a lookup table rather than
    /// anything derived from the event's characters.
    /// </summary>
    public static Key ToKey(ushort keyCode) => keyCode switch
    {
        0x00 => Key.A,
        0x01 => Key.S,
        0x02 => Key.D,
        0x03 => Key.F,
        0x04 => Key.H,
        0x05 => Key.G,
        0x06 => Key.Z,
        0x07 => Key.X,
        0x08 => Key.C,
        0x09 => Key.V,
        0x0B => Key.B,
        0x0C => Key.Q,
        0x0D => Key.W,
        0x0E => Key.E,
        0x0F => Key.R,
        0x10 => Key.Y,
        0x11 => Key.T,
        0x12 => Key.D1,
        0x13 => Key.D2,
        0x14 => Key.D3,
        0x15 => Key.D4,
        0x16 => Key.D6,
        0x17 => Key.D5,
        0x18 => Key.None,   // Equal
        0x19 => Key.D9,
        0x1A => Key.D7,
        0x1B => Key.None,   // Minus
        0x1C => Key.D8,
        0x1D => Key.D0,
        0x1F => Key.O,
        0x20 => Key.U,
        0x21 => Key.None,   // Left Bracket
        0x22 => Key.I,
        0x23 => Key.P,
        0x24 => Key.Enter,
        0x25 => Key.L,
        0x26 => Key.J,
        0x27 => Key.None,   // Quote
        0x28 => Key.K,
        0x29 => Key.None,   // Semicolon
        0x2A => Key.None,   // Backslash
        0x2B => Key.None,   // Comma
        0x2C => Key.None,   // Slash
        0x2D => Key.N,
        0x2E => Key.M,
        0x2F => Key.None,   // Period
        0x30 => Key.Tab,
        0x31 => Key.Space,
        0x32 => Key.None,   // Backquote
        0x33 => Key.Backspace,
        0x35 => Key.Escape,
        0x37 => Key.None,   // Command: a modifier, reported through FlagsChanged.
        0x38 => Key.None,   // Shift
        0x39 => Key.None,   // Caps Lock
        0x3A => Key.None,   // Option
        0x3B => Key.None,   // Control
        0x3C => Key.None,   // Right Shift
        0x3D => Key.None,   // Right Option
        0x3E => Key.None,   // Right Control
        0x3F => Key.None,   // Function
        0x40 => Key.None,   // F17
        0x4C => Key.Enter,  // Keypad Enter
        0x4E => Key.None,   // Keypad Minus
        0x4F => Key.None,   // F18
        0x50 => Key.None,   // F19
        0x51 => Key.None,   // Keypad Equal
        0x52 => Key.D0,     // Keypad 0
        0x53 => Key.D1,
        0x54 => Key.D2,
        0x55 => Key.D3,
        0x56 => Key.D4,
        0x57 => Key.D5,
        0x58 => Key.D6,
        0x59 => Key.D7,
        0x5A => Key.None,   // F20
        0x5B => Key.D8,
        0x5C => Key.D9,
        0x60 => Key.None,   // F5
        0x61 => Key.None,   // F6
        0x62 => Key.None,   // F7
        0x63 => Key.None,   // F3
        0x64 => Key.None,   // F8
        0x65 => Key.None,   // F9
        0x67 => Key.None,   // F11
        0x69 => Key.None,   // F13
        0x6A => Key.None,   // F16
        0x6B => Key.None,   // F14
        0x6D => Key.None,   // F10
        0x6F => Key.None,   // F12
        0x71 => Key.None,   // F15
        0x73 => Key.Home,
        0x74 => Key.PageUp,
        0x75 => Key.Delete, // Forward Delete
        0x76 => Key.None,   // F4
        0x77 => Key.End,
        0x78 => Key.None,   // F2
        0x79 => Key.PageDown,
        0x7A => Key.None,   // F1
        0x7B => Key.Left,
        0x7C => Key.Right,
        0x7D => Key.Down,
        0x7E => Key.Up,
        _ => Key.None,
    };

    /// <summary>
    /// The modifier keys held during an event. Command is deliberately not reported: it drives the
    /// menu bar's key equivalents rather than the UI's own Ctrl-style shortcuts, so mapping it onto
    /// <see cref="Modifiers.Control"/> would fire shortcuts the user did not ask for.
    /// </summary>
    public static Modifiers ToModifiers(NSEventModifierMask flags)
    {
        var modifiers = Modifiers.None;
        if ((flags & NSEventModifierMask.ShiftKeyMask) != 0)
        {
            modifiers |= Modifiers.Shift;
        }

        if ((flags & NSEventModifierMask.ControlKeyMask) != 0)
        {
            modifiers |= Modifiers.Control;
        }

        if ((flags & NSEventModifierMask.AlternateKeyMask) != 0)
        {
            modifiers |= Modifiers.Alt;
        }

        return modifiers;
    }

    /// <summary>
    /// The text an event inserts. Modifier-only keys and the non-text keys (arrows, Escape, Return,
    /// function keys) arrive as control characters or private-use code points; those are dropped
    /// because the UI library handles them through <see cref="Key"/> instead.
    /// </summary>
    public static string ToText(NSEvent evt)
    {
        // The characters already carry the effect of Shift, the keyboard layout and Option dead
        // keys (Option+E then E gives an accented letter), so they are used as they arrive.
        var characters = evt.Characters;

        if (string.IsNullOrEmpty(characters))
        {
            return string.Empty;
        }

        return ContainsOnlyText(characters) ? characters : string.Empty;
    }

    private static bool ContainsOnlyText(string characters)
    {
        foreach (var c in characters)
        {
            // C0 controls, Delete, and the function-key range (NSUpArrowFunctionKey and friends,
            // which start at U+F700) never represent typed text.
            if (c < 0x20 || c == 0x7F || (c >= 0xF700 && c <= 0xF8FF))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// The key equivalent character for a shortcut key, or null for a key AppKit cannot spell that
    /// way. AppKit describes a menu shortcut as a character rather than a key code, which is why
    /// the arrows and the other non-text keys use the function-key code points.
    /// </summary>
    public static string? ToKeyEquivalent(Key key) => key switch
    {
        >= Key.A and <= Key.Z => ((char)('a' + ((int)key - (int)Key.A))).ToString(),
        >= Key.D0 and <= Key.D9 => ((char)('0' + ((int)key - (int)Key.D0))).ToString(),
        Key.Tab => "\t",
        Key.Enter => "\r",
        Key.Escape => "\u001B",
        Key.Backspace => "\b",
        Key.Delete => "\u007F",
        Key.Left => "\uF702",
        Key.Right => "\uF703",
        Key.Up => "\uF700",
        Key.Down => "\uF701",
        Key.Home => "\uF729",
        Key.End => "\uF72B",
        Key.PageUp => "\uF72C",
        Key.PageDown => "\uF72D",
        _ => null,
    };

    /// <summary>
    /// The AppKit modifier mask for a shortcut. <see cref="Modifiers.Control"/> is the
    /// library's accelerator modifier, which macOS spells Command.
    /// </summary>
    public static NSEventModifierMask ToKeyEquivalentMask(Modifiers modifiers)
    {
        var mask = (NSEventModifierMask)0;

        if ((modifiers & Modifiers.Control) != 0)
        {
            mask |= NSEventModifierMask.CommandKeyMask;
        }

        if ((modifiers & Modifiers.Shift) != 0)
        {
            mask |= NSEventModifierMask.ShiftKeyMask;
        }

        if ((modifiers & Modifiers.Alt) != 0)
        {
            mask |= NSEventModifierMask.AlternateKeyMask;
        }

        return mask;
    }
}