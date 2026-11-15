namespace Kolvir;

/// <summary>
/// A keyboard accelerator: one key plus the modifiers held with it, shown next to a menu item and
/// dispatched by the window that owns the menu.
/// </summary>
/// <remarks>
/// <para>
/// A shortcut always carries at least one modifier, because a bare key belongs to the focused
/// control: Tab moves the focus and a letter types, and neither of those is an accelerator.
/// </para>
/// <para>
/// <see cref="Modifiers.Control"/> means the platform's accelerator modifier rather than the
/// literal Control key. <see cref="DisplayText"/> spells it "Cmd" on macOS and "Ctrl" everywhere
/// else, and the macOS backend gives a menu item a Command key equivalent, which is what actually
/// fires there. The macOS input pipeline never reports Command as a modifier for the same reason,
/// so <see cref="Matches"/> stays an exact comparison and no key is claimed twice.
/// </para>
/// </remarks>
public readonly struct Shortcut : IEquatable<Shortcut>
{
    /// <summary>
    /// Creates a shortcut for <paramref name="key"/>, with Control - the platform's accelerator
    /// modifier - held unless <paramref name="modifiers"/> says otherwise.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="key"/> is <see cref="Key.None"/>.</exception>
    /// <exception cref="ArgumentException"><paramref name="modifiers"/> is <see cref="Modifiers.None"/>.</exception>
    public Shortcut(Key key, Modifiers modifiers = Modifiers.Control)
    {
        if (key == Key.None)
        {
            throw new ArgumentOutOfRangeException(nameof(key), key, "A shortcut needs a key.");
        }

        if (modifiers == Modifiers.None)
        {
            throw new ArgumentException(
                "A shortcut needs at least one modifier; a bare key belongs to the focused control.",
                nameof(modifiers));
        }

        Key = key;
        Modifiers = modifiers;
    }

    /// <summary>The key that triggers the shortcut.</summary>
    public Key Key { get; }

    /// <summary>The modifiers that have to be held with <see cref="Key"/>.</summary>
    public Modifiers Modifiers { get; }

    /// <summary>True when <paramref name="key"/> pressed with <paramref name="modifiers"/> is this shortcut.</summary>
    public bool Matches(Key key, Modifiers modifiers) => key == Key && modifiers == Modifiers;

    /// <summary>
    /// The shortcut as the user should see it, e.g. "Ctrl+Shift+F", or "Cmd+Shift+F" on macOS.
    /// </summary>
    public string DisplayText
    {
        get
        {
            var mac = OperatingSystem.IsMacOS();
            var parts = new List<string>(4);

            if ((Modifiers & Modifiers.Control) != 0)
            {
                parts.Add(mac ? "Cmd" : "Ctrl");
            }

            if ((Modifiers & Modifiers.Shift) != 0)
            {
                parts.Add("Shift");
            }

            if ((Modifiers & Modifiers.Alt) != 0)
            {
                parts.Add(mac ? "Option" : "Alt");
            }

            parts.Add(KeyName(Key));
            return string.Join('+', parts);
        }
    }

    /// <summary>True when both shortcuts name the same key with the same modifiers.</summary>
    /// <param name="other">The shortcut to compare with.</param>
    /// <returns>True when they match.</returns>
    public bool Equals(Shortcut other) => Key == other.Key && Modifiers == other.Modifiers;

    /// <summary>
    /// True when <paramref name="obj"/> is a <see cref="Shortcut"/> naming the same key with the
    /// same modifiers.
    /// </summary>
    /// <param name="obj">The object to compare with.</param>
    /// <returns>True when it is an equal <see cref="Shortcut"/>.</returns>
    public override bool Equals(object? obj) => obj is Shortcut other && Equals(other);

    /// <summary>A hash consistent with <see cref="Equals(Shortcut)"/>.</summary>
    /// <returns>The hash code.</returns>
    public override int GetHashCode() => HashCode.Combine(Key, Modifiers);

    /// <summary>True when the two shortcuts name the same key with the same modifiers.</summary>
    /// <param name="left">The first shortcut.</param>
    /// <param name="right">The second shortcut.</param>
    /// <returns>The result of <see cref="Equals(Shortcut)"/>.</returns>
    public static bool operator ==(Shortcut left, Shortcut right) => left.Equals(right);

    /// <summary>True when the two shortcuts differ in the key or in the modifiers.</summary>
    /// <param name="left">The first shortcut.</param>
    /// <param name="right">The second shortcut.</param>
    /// <returns>The negation of <see cref="Equals(Shortcut)"/>.</returns>
    public static bool operator !=(Shortcut left, Shortcut right) => !left.Equals(right);

    /// <summary>The same text <see cref="DisplayText"/> returns.</summary>
    public override string ToString() => DisplayText;

    /// <summary>The name a shortcut hint uses for <paramref name="key"/>.</summary>
    private static string KeyName(Key key) => key switch
    {
        Key.Tab => "Tab",
        Key.Enter => "Enter",
        Key.Space => "Space",
        Key.Escape => "Esc",
        Key.Backspace => "Backspace",
        Key.Delete => "Del",
        Key.Left => "Left",
        Key.Right => "Right",
        Key.Up => "Up",
        Key.Down => "Down",
        Key.Home => "Home",
        Key.End => "End",
        Key.PageUp => "Page Up",
        Key.PageDown => "Page Down",
        >= Key.D0 and <= Key.D9 => ((char)('0' + ((int)key - (int)Key.D0))).ToString(),
        _ => key.ToString(),
    };
}
