namespace Kolvir.Backend.Gtk4;

/// <summary>
/// Translates GDK key values into the library's <see cref="Key"/> values. GDK key values are
/// stable across platforms (they descend from X11), so the mapping is a plain switch.
/// </summary>
internal static class Gtk4KeyMap
{
    private const uint KeyBackspace = 0xFF08;
    private const uint KeyTab = 0xFF09;

    /// <summary>
    /// X11 gives shift+Tab a key value of its own rather than reporting <see cref="KeyTab"/> with
    /// the shift bit, so a shifted Tab arrives here instead of as Tab.
    /// </summary>
    private const uint KeyIsoLeftTab = 0xFE20;

    private const uint KeyReturn = 0xFF0D;
    private const uint KeyEscape = 0xFF1B;
    private const uint KeySpace = 0x0020;
    private const uint KeyHome = 0xFF50;
    private const uint KeyLeft = 0xFF51;
    private const uint KeyUp = 0xFF52;
    private const uint KeyRight = 0xFF53;
    private const uint KeyDown = 0xFF54;
    private const uint KeyPageUp = 0xFF55;
    private const uint KeyPageDown = 0xFF56;
    private const uint KeyEnd = 0xFF57;
    private const uint KeyKpEnter = 0xFF8D;
    private const uint KeyDelete = 0xFFFF;

    public static Key Map(uint keyval)
    {
        if (keyval is >= 'a' and <= 'z')
        {
            return Key.A + (int)(keyval - 'a');
        }

        if (keyval is >= 'A' and <= 'Z')
        {
            return Key.A + (int)(keyval - 'A');
        }

        if (keyval is >= '0' and <= '9')
        {
            return Key.D0 + (int)(keyval - '0');
        }

        return keyval switch
        {
            KeyTab or KeyIsoLeftTab => Key.Tab,
            KeyReturn or KeyKpEnter => Key.Enter,
            KeySpace => Key.Space,
            KeyEscape => Key.Escape,
            KeyBackspace => Key.Backspace,
            KeyDelete => Key.Delete,
            KeyLeft => Key.Left,
            KeyRight => Key.Right,
            KeyUp => Key.Up,
            KeyDown => Key.Down,
            KeyHome => Key.Home,
            KeyEnd => Key.End,
            KeyPageUp => Key.PageUp,
            KeyPageDown => Key.PageDown,
            _ => Key.None,
        };
    }

    public static Modifiers MapModifiers(Gdk.ModifierType state)
    {
        var modifiers = Modifiers.None;

        if ((state & Gdk.ModifierType.ShiftMask) != 0)
        {
            modifiers |= Modifiers.Shift;
        }

        if ((state & Gdk.ModifierType.ControlMask) != 0)
        {
            modifiers |= Modifiers.Control;
        }

        if ((state & Gdk.ModifierType.AltMask) != 0)
        {
            modifiers |= Modifiers.Alt;
        }

        return modifiers;
    }

    /// <summary>
    /// True when the key value itself says that shift is held. X11 only does this for
    /// <see cref="KeyIsoLeftTab"/>, whose whole meaning is shift+Tab, and it is not guaranteed to
    /// set the shift bit in the event state alongside it.
    /// </summary>
    public static bool ImpliesShift(uint keyval) => keyval == KeyIsoLeftTab;

    /// <summary>
    /// Returns the text a key press produces, or null when it produces none. Layouts, dead keys and
    /// compose sequences are left to GDK so that non-US keyboards behave the way users expect.
    /// </summary>
    public static string? MapText(uint keyval, Gdk.ModifierType state)
    {
        // Ctrl/Alt/Super combinations are accelerators, not text.
        if ((state & (Gdk.ModifierType.ControlMask | Gdk.ModifierType.AltMask | Gdk.ModifierType.SuperMask)) != 0)
        {
            return null;
        }

        var unicode = Gdk.Functions.KeyvalToUnicode(keyval);
        if (unicode is 0 or 0xFFFF || unicode < 0x20 || unicode == 0x7F)
        {
            return null;
        }

        return char.ConvertFromUtf32((int)unicode);
    }
}