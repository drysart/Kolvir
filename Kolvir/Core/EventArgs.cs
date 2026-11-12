using SkiaSharp;

namespace Kolvir;

public class MouseEventArgs
{
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

    public MouseButtons Button { get; }

    public Modifiers Modifiers { get; }

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

    public bool Handled { get; set; }
}

public class KeyEventArgs
{
    public KeyEventArgs(Key key, Modifiers modifiers = Modifiers.None)
    {
        Key = key;
        Modifiers = modifiers;
    }

    public Key Key { get; }

    public Modifiers Modifiers { get; }

    public bool Handled { get; set; }
}

public class TextInputEventArgs
{
    public TextInputEventArgs(string text) => Text = text;

    public string Text { get; }

    public bool Handled { get; set; }
}
