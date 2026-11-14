using SkiaSharp;

namespace Kolvir;

/// <summary>
/// The half <see cref="CheckBox"/> and <see cref="RadioButton"/> share: a glyph followed
/// by a label, with hover, pressed, focused and disabled states and a <see cref="CheckState"/> that
/// a click or the space bar changes.
/// </summary>
/// <remarks>
/// <para>
/// Only the glyph and the grouping rule are left to a derived class, so the two controls differ in
/// what they draw and in which other toggles they answer to, and agree on everything else -
/// measuring, painting order and input.
/// </para>
/// <para>
/// Like the WinForms pair, the control does not paint a background of its own: it sits on whatever
/// is behind it, so a toggle dropped onto a card looks like part of it.
/// </para>
/// </remarks>
public abstract class ToggleControl : Control
{
    private CheckState _checkState;
    private ContentAlignment _textAlign = ContentAlignment.MiddleLeft;
    private Action<object>? _checkedChanged;
    private float _glyphSize = 16f;
    private float _glyphGap = 8f;
    private bool _threeState;

    /// <summary>
    /// Creates a toggle that takes focus, auto-sizes around its glyph and label, shows a hand cursor,
    /// reserves padding for the focus ring and is at least 24 pixels tall.
    /// </summary>
    protected ToggleControl()
    {
        TabStop = true;
        AutoSize = true;
        Cursor = Cursor.Hand;

        // The focus ring is drawn just inside the bounds, so the content starts outside it.
        Padding = new Thickness(4f);
        MinimumSize = new SKSize(0f, 24f);
    }

    /// <summary>Adds a handler that runs after <see cref="CheckState"/> changes.</summary>
    public IDisposable AddCheckedChangedHandler(Action<object> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        _checkedChanged += handler;
        return new HandlerToken<Action<object>>(RemoveCheckedChangedHandler, handler);
    }

    /// <summary>Removes a handler that <see cref="AddCheckedChangedHandler"/> added.</summary>
    public void RemoveCheckedChangedHandler(Action<object> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        _checkedChanged -= handler;
    }

    /// <summary>Where the label sits in the space the glyph leaves. Defaults to <see cref="ContentAlignment.MiddleLeft"/>.</summary>
    public ContentAlignment TextAlign
    {
        get => _textAlign;
        set
        {
            if (_textAlign == value)
            {
                return;
            }

            _textAlign = value;
            Invalidate();
        }
    }

    /// <summary>The width and height of the glyph. Defaults to 16.</summary>
    public float GlyphSize
    {
        get => _glyphSize;
        set
        {
            var size = MathF.Max(8f, value);
            if (_glyphSize == size)
            {
                return;
            }

            _glyphSize = size;
            Invalidate();
            PerformLayout();
        }
    }

    /// <summary>The gap between the glyph and the label. Defaults to 8.</summary>
    public float GlyphGap
    {
        get => _glyphGap;
        set
        {
            var gap = MathF.Max(0f, value);
            if (_glyphGap == gap)
            {
                return;
            }

            _glyphGap = gap;
            Invalidate();
            PerformLayout();
        }
    }

    /// <summary>
    /// The check state. Setting <see cref="CheckState.Indeterminate"/> while
    /// <see cref="ThreeState"/> is off reads back as <see cref="CheckState.Unchecked"/>.
    /// </summary>
    public CheckState CheckState
    {
        get => _checkState;
        set
        {
            var state = _threeState || value != CheckState.Indeterminate
                ? value
                : CheckState.Unchecked;

            if (_checkState == state)
            {
                return;
            }

            _checkState = state;
            Invalidate();
            OnCheckStateChanged();
        }
    }

    /// <summary>The two-state view of <see cref="CheckState"/>: true only when it is <see cref="CheckState.Checked"/>.</summary>
    public bool Checked
    {
        get => _checkState == CheckState.Checked;
        set => CheckState = value ? CheckState.Checked : CheckState.Unchecked;
    }

    /// <summary>
    /// When true a click cycles through all three states instead of toggling between two.
    /// Defaults to false, matching the WinForms <c>CheckBox.ThreeState</c>.
    /// </summary>
    public bool ThreeState
    {
        get => _threeState;
        set
        {
            if (_threeState == value)
            {
                return;
            }

            _threeState = value;

            // A two-state toggle has no way back out of the third state, so drop it now rather
            // than leave the control painting something it cannot reach.
            if (!value && _checkState == CheckState.Indeterminate)
            {
                CheckState = CheckState.Unchecked;
            }
        }
    }

    /// <summary>
    /// When true a click or the space bar changes <see cref="CheckState"/>. When false the control
    /// still raises <see cref="Control.AddClickHandler"/> and the application decides.
    /// </summary>
    public bool AutoCheck { get; set; } = true;

    /// <summary>The area the glyph is drawn in, in client coordinates.</summary>
    protected SKRect GlyphRect
    {
        get
        {
            var client = ClientRect;
            float left = client.Left + Padding.Left;
            float top = client.Top + ((client.Height - _glyphSize) / 2f);
            return new SKRect(left, top, left + _glyphSize, top + _glyphSize);
        }
    }

    /// <summary>The label colour: <see cref="Control.ForeColor"/>, greyed out while disabled.</summary>
    protected SKColor TextColor => Enabled ? ForeColor : Theme.DisabledForeground;

    /// <summary>The glyph outline: accent while it carries a mark, the theme border otherwise.</summary>
    protected SKColor GlyphBorderColor
    {
        get
        {
            if (!Enabled)
            {
                return Theme.DisabledForeground;
            }

            return _checkState != CheckState.Unchecked || IsMouseOver
                ? Theme.Accent
                : Theme.Border;
        }
    }

    /// <summary>The glyph fill, for the states that fill it.</summary>
    protected SKColor GlyphFillColor =>
        !Enabled ? Theme.DisabledBackground
        : IsMouseOver ? Theme.AccentHover
        : Theme.Accent;

    /// <summary>The mark inside a filled glyph.</summary>
    protected SKColor GlyphMarkColor => Enabled ? Theme.AccentForeground : Theme.DisabledForeground;

    /// <inheritdoc />
    protected override SKSize MeasureContent(SKSize availableSize)
    {
        var text = TextRenderer.Measure(Text, Font, float.PositiveInfinity, wrap: false);
        return new SKSize(
            MathF.Ceiling(_glyphSize + _glyphGap + text.Width),
            MathF.Ceiling(MathF.Max(_glyphSize, text.Height)));
    }

    /// <summary>
    /// Draws the glyph through <see cref="PaintGlyph"/> and then the label in the space the glyph
    /// leaves, aligned by <see cref="TextAlign"/>.
    /// </summary>
    protected override void OnPaint(SKCanvas canvas, SKRect rect)
    {
        var glyph = GlyphRect;
        if (glyph.Width > 0f && glyph.Height > 0f)
        {
            PaintGlyph(canvas, glyph);
        }

        if (string.IsNullOrEmpty(Text))
        {
            return;
        }

        var label = new SKRect(glyph.Right + _glyphGap, rect.Top, rect.Right - Padding.Right, rect.Bottom);
        TextRenderer.Draw(canvas, Text, Font, TextColor, label, _textAlign);
    }

    /// <summary>
    /// The border slot is used for the focus ring, which is drawn last so it sits above the glyph and
    /// the label it surrounds.
    /// </summary>
    protected override void OnPaintBorder(SKCanvas canvas, SKRect rect)
    {
        // The border slot is used for the focus ring, which is drawn last so it sits above the
        // glyph and the label it surrounds.
        if (IsFocused && Enabled)
        {
            Painter.DrawFocusRing(canvas, rect, Theme.FocusRing, 2f, CornerRadius);
        }
    }

    /// <summary>Draws the glyph inside <paramref name="glyph"/>; the base colours are already resolved.</summary>
    protected abstract void PaintGlyph(SKCanvas canvas, SKRect glyph);

    /// <summary>Raises <see cref="AddCheckedChangedHandler"/>.</summary>
    protected virtual void OnCheckedChanged() => _checkedChanged?.Invoke(this);

    /// <summary>
    /// The state a click moves to. Two-state toggles cycle unchecked to checked and back; a
    /// radio button can only ever become checked.
    /// </summary>
    protected virtual CheckState NextCheckState() => _checkState switch
    {
        CheckState.Unchecked => CheckState.Checked,
        CheckState.Checked => _threeState ? CheckState.Indeterminate : CheckState.Unchecked,
        _ => CheckState.Unchecked,
    };

    /// <summary>Applies a click: changes the state when <see cref="AutoCheck"/> is on, then raises the click.</summary>
    protected virtual void Toggle()
    {
        if (AutoCheck)
        {
            CheckState = NextCheckState();
        }

        OnClick();
    }

    /// <summary>Runs after <see cref="CheckState"/> has changed and this control has been invalidated.</summary>
    protected virtual void OnCheckStateChanged() => OnCheckedChanged();

    /// <summary>
    /// A left press on an enabled toggle puts it in its pressed state and captures the mouse, so a
    /// release outside the control still ends the gesture.
    /// </summary>
    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);

        if (e.Button == MouseButtons.Left && Enabled)
        {
            IsPressed = true;
            CaptureMouse();
            Invalidate();
        }
    }

    /// <summary>
    /// Ends a left-button gesture: the pressed state and the capture are cleared and, when the
    /// release happened over the control, the toggle is applied.
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
            Toggle();
        }
    }

    /// <summary>Repaints so the glyph picks up its hover colours.</summary>
    protected override void OnMouseEnter()
    {
        base.OnMouseEnter();
        Invalidate();
    }

    /// <summary>
    /// Abandons a press the pointer has left: the pressed state and the capture are cleared, so
    /// releasing elsewhere cannot toggle the control, and the hover colours are repainted away.
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

    /// <summary>Space presses the toggle, the keyboard's equivalent of a mouse-down.</summary>
    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);

        if (e.Key == Key.Space)
        {
            IsPressed = true;
            e.Handled = true;
            Invalidate();
        }
    }

    /// <summary>Releasing Space while pressed applies the toggle, mirroring a mouse release.</summary>
    protected override void OnKeyUp(KeyEventArgs e)
    {
        base.OnKeyUp(e);

        if (e.Key == Key.Space && IsPressed)
        {
            IsPressed = false;
            e.Handled = true;
            Invalidate();
            Toggle();
        }
    }

    /// <summary>Repaints so the focus ring appears.</summary>
    protected override void OnGotFocus()
    {
        base.OnGotFocus();
        Invalidate();
    }

    /// <summary>Clears the pressed state and repaints so the focus ring goes away.</summary>
    protected override void OnLostFocus()
    {
        base.OnLostFocus();
        IsPressed = false;
        Invalidate();
    }

    /// <summary>
    /// Abandons a press while the toggle is being disabled and releases the mouse capture, then lets
    /// the base raise the enabled-changed event and repaint.
    /// </summary>
    protected override void OnEnabledChanged()
    {
        if (!Enabled)
        {
            IsPressed = false;
            ReleaseMouseCapture();
        }

        base.OnEnabledChanged();
    }
}
