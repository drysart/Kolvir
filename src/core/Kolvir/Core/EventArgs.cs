using SkiaSharp;

namespace Kolvir;

/// <summary>
/// Event data for a pointer event over a control: a move, a button press or release, or a wheel
/// turn. <see cref="Location"/> is in the receiving control's own coordinates, so a handler can
/// test it against that control's bounds.
/// </summary>
public class MouseEventArgs
{
    /// <summary>Creates the event data for one pointer event.</summary>
    /// <param name="location">Mouse position in the receiving control's coordinate space.</param>
    /// <param name="button">
    /// The button the event is about, or <see cref="MouseButtons.None"/> for a move or a wheel turn.
    /// </param>
    /// <param name="modifiers">Modifier keys held when the event happened.</param>
    /// <param name="clickCount">
    /// How many consecutive clicks the press or release represents: 1 for a single click, 2 for a
    /// double click, and so on.
    /// </param>
    /// <param name="delta">
    /// How far a wheel turn scrolls, in logical pixels; zero for an event that is not a wheel turn.
    /// </param>
    public MouseEventArgs(
        SKPoint location,
        MouseButtons button = MouseButtons.None,
        Modifiers modifiers = Modifiers.None,
        int clickCount = 1,
        float delta = 0f)
    {
        Location = location;
        Button = button;
        Modifiers = modifiers;
        ClickCount = clickCount;
        Delta = delta;
    }

    /// <summary>Mouse position in the receiving control's coordinate space.</summary>
    public SKPoint Location { get; }

    /// <summary>
    /// The button the event is about: the one pressed or released. It is
    /// <see cref="MouseButtons.None"/> for a move and for a wheel turn.
    /// </summary>
    public MouseButtons Button { get; }

    /// <summary>Modifier keys held when the event happened.</summary>
    public Modifiers Modifiers { get; }

    /// <summary>
    /// How many consecutive clicks the press or release represents, counting from 1. A control that
    /// wants a double click tests for 2 rather than tracking the time between events itself.
    /// </summary>
    public int ClickCount { get; }

    /// <summary>
    /// How far a wheel turn scrolls, in logical pixels, with a positive value scrolling toward the
    /// start of the content (up or left). Always zero for an event that is not a wheel turn.
    /// </summary>
    /// <remarks>
    /// A wheel reports whole detents and a touchpad reports fractions of one, so the value is
    /// fractional rather than a count of anything.
    /// </remarks>
    public float Delta { get; }

    /// <summary>
    /// Set to true to mark the event as dealt with. A wheel turn is offered to the control under the
    /// pointer and then to the containers above it until one reports it handled, so setting this on
    /// a turn stops it travelling further; for the other pointer events the flag is a convention
    /// between a handler and its control, which is free to ignore it.
    /// </summary>
    public bool Handled { get; set; }
}

/// <summary>
/// Event data for a key press or release. <see cref="Key"/> is a platform-neutral identifier, so a
/// handler does not have to know Win32 virtual-key or macOS key codes.
/// </summary>
public class KeyEventArgs
{
    /// <summary>Creates the event data for one key event.</summary>
    /// <param name="key">The key that was pressed or released.</param>
    /// <param name="modifiers">Modifier keys held when the event happened.</param>
    public KeyEventArgs(Key key, Modifiers modifiers = Modifiers.None)
    {
        Key = key;
        Modifiers = modifiers;
    }

    /// <summary>The key that was pressed or released.</summary>
    public Key Key { get; }

    /// <summary>Modifier keys held when the event happened.</summary>
    public Modifiers Modifiers { get; }

    /// <summary>
    /// Set to true in a handler to make the control leave the key alone: for a key press a control
    /// runs its handlers first and skips its own handling when this is set, and the host then
    /// reports the key to the platform as consumed rather than letting the platform act on it too.
    /// </summary>
    /// <remarks>
    /// A key release runs its handlers as well, but nothing consults the flag afterwards, so setting
    /// it there has no effect.
    /// </remarks>
    public bool Handled { get; set; }
}

/// <summary>
/// Event data for text a platform produced from typing. The text can be more than one character,
/// because an input method commits a whole syllable or word at a time, so a handler inserts the
/// string rather than a single character.
/// </summary>
public class TextInputEventArgs
{
    /// <summary>Creates the event data for one piece of typed text.</summary>
    /// <param name="text">The text to insert.</param>
    public TextInputEventArgs(string text) => Text = text;

    /// <summary>The text to insert. It may span more than one character.</summary>
    public string Text { get; }

    /// <summary>
    /// Set to true in a handler to make the control leave the text alone: a control runs its
    /// handlers first and skips its own insertion when this is set, which is how a field filters
    /// input. A control that inspects the text itself, such as a numeric field, sets it for the
    /// same reason.
    /// </summary>
    public bool Handled { get; set; }
}
