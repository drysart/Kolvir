using SkiaSharp;

namespace Kolvir;

/// <summary>Visual style of an <see cref="Button"/>.</summary>
public enum ButtonStyle
{
    /// <summary>Filled with the accent color. Use for the primary action on a view.</summary>
    Primary,

    /// <summary>Muted fill with a border. Use for secondary actions.</summary>
    Secondary,

    /// <summary>No fill or border; text only.</summary>
    Ghost,
}

/// <summary>
/// A push button with hover, pressed, focused and disabled states. It raises
/// <see cref="Control.AddClickHandler"/> on mouse release inside the button or on
/// Enter/Space while focused.
/// </summary>
public class Button : Control
{
    private ButtonStyle _style = ButtonStyle.Primary;

    /// <summary>
    /// The button's shape: rounded, and taking no room, because the only frame a button draws is the
    /// secondary style's, which is chrome of that style rather than the button's own border.
    /// </summary>
    private static readonly SimpleBorder DefaultBorder = SimpleBorder.Thin.WithWidth(0f).WithCornerRadius(6f);

    /// <summary>
    /// Creates a button with no text: a tab stop with the hand cursor, the standard button padding
    /// and a minimum height of 32 logical pixels.
    /// </summary>
    public Button()
    {
        TabStop = true;
        Cursor = Cursor.Hand;
        Border = DefaultBorder;
        Padding = new Thickness(14f, 8f);
        MinimumSize = new SKSize(0f, 32f);
    }

    /// <summary>Creates a button whose label is <paramref name="text"/>.</summary>
    public Button(string text)
        : this()
    {
        Text = text;
    }

    /// <summary>A button's own text is emphasized until the theme or an ancestor says otherwise.</summary>
    protected override Font DefaultFont => Theme.EmphasizedFont;

    /// <summary>
    /// How the button is filled and bordered. Defaults to <see cref="ButtonStyle.Primary"/>, and
    /// assigning a different value repaints the button.
    /// </summary>
    public ButtonStyle Style
    {
        get => _style;
        set
        {
            if (_style == value)
            {
                return;
            }

            _style = value;
            Invalidate();
        }
    }

    /// <summary>
    /// A button is one line of text: as wide as its label unwrapped, rounded up to whole pixels.
    /// Padding and the minimum size are added by <see cref="Control.Measure"/>.
    /// </summary>
    protected override SKSize MeasureContent(SKSize availableSize)
    {
        var measured = TextRenderer.Measure(Text, Font, float.PositiveInfinity, wrap: false);
        return new SKSize(MathF.Ceiling(measured.Width), MathF.Ceiling(measured.Height));
    }

    /// <inheritdoc />
    protected override void OnPaintBackground(SKCanvas canvas, SKRect rect)
    {
        var (fill, _) = ResolveColors();
        if (fill is { } color)
        {
            Painter.Fill(canvas, rect, color, Border.CornerRadius);
        }

        if (_style == ButtonStyle.Secondary && Enabled)
        {
            Painter.DrawBorder(canvas, rect, Theme.Border, 1f, Border.CornerRadius);
        }

        if (IsFocused && Enabled)
        {
            Painter.DrawFocusRing(canvas, rect, Theme.FocusRing, 2f, Border.CornerRadius);
        }
    }

    /// <summary>
    /// Draws the label centred in the padded content area; a button with no text draws nothing.
    /// </summary>
    protected override void OnPaint(SKCanvas canvas, SKRect rect)
    {
        if (string.IsNullOrEmpty(Text))
        {
            return;
        }

        var (_, textColor) = ResolveColors();
        var content = Geometry.Deflate(rect, Padding);
        TextRenderer.Draw(canvas, Text, Font, textColor, content, ContentAlignment.MiddleCenter);
    }

    /// <summary>Takes the mouse capture and marks the button pressed on a left-button press.</summary>
    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);

        if (e.Button == MouseButtons.Left)
        {
            IsPressed = true;
            CaptureMouse();
            Invalidate();
        }
    }

    /// <summary>
    /// Releases the capture and raises <see cref="Control.AddClickHandler"/> when a press that began
    /// on the button is released inside it.
    /// </summary>
    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);

        if (e.Button != MouseButtons.Left)
        {
            return;
        }

        bool wasPressed = IsPressed;
        IsPressed = false;
        ReleaseMouseCapture();
        Invalidate();

        if (wasPressed && Geometry.Contains(ClientRect, e.Location))
        {
            OnClick();
        }
    }

    /// <inheritdoc />
    protected override void OnMouseEnter()
    {
        base.OnMouseEnter();
        Invalidate();
    }

    /// <summary>
    /// Cancels a press the pointer has left, so a press that wanders off the button does not click.
    /// </summary>
    protected override void OnMouseLeave()
    {
        base.OnMouseLeave();

        if (IsPressed)
        {
            IsPressed = false;
            ReleaseMouseCapture();
        }

        Invalidate();
    }

    /// <summary>
    /// Marks the button pressed for Enter or Space and claims the key, which is where the press
    /// starts for a keyboard activation.
    /// </summary>
    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);

        if (e.Key is Key.Enter or Key.Space)
        {
            IsPressed = true;
            e.Handled = true;
            Invalidate();
        }
    }

    /// <summary>Raises the click for Enter or Space while the keyboard press is in effect.</summary>
    protected override void OnKeyUp(KeyEventArgs e)
    {
        base.OnKeyUp(e);

        if (e.Key is Key.Enter or Key.Space && IsPressed)
        {
            IsPressed = false;
            e.Handled = true;
            Invalidate();
            OnClick();
        }
    }

    /// <inheritdoc />
    protected override void OnGotFocus()
    {
        base.OnGotFocus();
        Invalidate();
    }

    /// <summary>Cancels a press when the button loses the focus.</summary>
    protected override void OnLostFocus()
    {
        base.OnLostFocus();
        IsPressed = false;
        Invalidate();
    }

    /// <summary>Cancels a press and releases the capture when the button becomes disabled.</summary>
    protected override void OnEnabledChanged()
    {
        if (!Enabled)
        {
            IsPressed = false;
            ReleaseMouseCapture();
        }

        base.OnEnabledChanged();
    }

    /// <summary>
    /// The fill and the text colour for the current style and state. A subclass that paints the
    /// button in parts - a split button's arrow region, for one - draws with the same answer
    /// instead of re-deriving it.
    /// </summary>
    protected (SKColor? Fill, SKColor Text) ResolveColors()
    {
        if (!Enabled)
        {
            return _style switch
            {
                ButtonStyle.Ghost => (null, Theme.DisabledForeground),
                _ => (Theme.DisabledBackground, Theme.DisabledForeground),
            };
        }

        return _style switch
        {
            ButtonStyle.Primary => (
                IsPressed ? Theme.AccentPressed : IsMouseOver ? Theme.AccentHover : Theme.Accent,
                Theme.AccentForeground),
            ButtonStyle.Secondary => (
                IsPressed ? Theme.SecondaryPressed : IsMouseOver ? Theme.SecondaryHover : Theme.Secondary,
                Theme.Foreground),
            _ => (
                IsPressed ? Theme.SecondaryPressed : IsMouseOver ? Theme.SecondaryHover : (SKColor?)null,
                Theme.Accent),
        };
    }
}
