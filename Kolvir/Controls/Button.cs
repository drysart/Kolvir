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

    public Button()
    {
        TabStop = true;
        Cursor = Cursor.Hand;
        CornerRadius = 6f;
        Padding = new Thickness(14f, 8f);
        MinimumSize = new SKSize(0f, 32f);
    }

    public Button(string text)
        : this()
    {
        Text = text;
    }

    /// <summary>A button's own text is emphasized until the theme or an ancestor says otherwise.</summary>
    protected override Font DefaultFont => Theme.EmphasizedFont;

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

    protected override SKSize MeasureContent(SKSize availableSize)
    {
        var measured = TextRenderer.Measure(Text, Font, float.PositiveInfinity, wrap: false);
        return new SKSize(MathF.Ceiling(measured.Width), MathF.Ceiling(measured.Height));
    }

    protected override void OnPaintBackground(SKCanvas canvas, SKRect rect)
    {
        var (fill, _) = ResolveColors();
        if (fill is { } color)
        {
            Painter.Fill(canvas, rect, color, CornerRadius);
        }

        if (_style == ButtonStyle.Secondary && Enabled)
        {
            Painter.DrawBorder(canvas, rect, Theme.Border, 1f, CornerRadius);
        }

        if (IsFocused && Enabled)
        {
            Painter.DrawFocusRing(canvas, rect, Theme.FocusRing, 2f, CornerRadius);
        }
    }

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

    protected override void OnPaintBorder(SKCanvas canvas, SKRect rect)
    {
        // Borders and focus rings are drawn in OnPaintBackground so they stay behind the text.
    }

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

    protected override void OnMouseEnter()
    {
        base.OnMouseEnter();
        Invalidate();
    }

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

    protected override void OnGotFocus()
    {
        base.OnGotFocus();
        Invalidate();
    }

    protected override void OnLostFocus()
    {
        base.OnLostFocus();
        IsPressed = false;
        Invalidate();
    }

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
