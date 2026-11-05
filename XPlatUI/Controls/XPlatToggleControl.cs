using SkiaSharp;

namespace XPlatUI;

/// <summary>
/// The half <see cref="XPlatCheckBox"/> and <see cref="XPlatRadioButton"/> share: a glyph followed
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
public abstract class XPlatToggleControl : XPlatControl
{
    private XPlatCheckState _checkState;
    private XPlatContentAlignment _textAlign = XPlatContentAlignment.MiddleLeft;
    private Action<object>? _checkedChanged;
    private float _glyphSize = 16f;
    private float _glyphGap = 8f;
    private bool _threeState;

    protected XPlatToggleControl()
    {
        TabStop = true;
        AutoSize = true;
        Cursor = XPlatCursor.Hand;

        // The focus ring is drawn just inside the bounds, so the content starts outside it.
        Padding = new XPlatThickness(4f);
        MinimumSize = new SKSize(0f, 24f);
    }

    /// <summary>Adds a handler that runs after <see cref="CheckState"/> changes.</summary>
    public IDisposable AddCheckedChangedHandler(Action<object> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        _checkedChanged += handler;
        return new XPlatHandlerToken<Action<object>>(RemoveCheckedChangedHandler, handler);
    }

    /// <summary>Removes a handler that <see cref="AddCheckedChangedHandler"/> added.</summary>
    public void RemoveCheckedChangedHandler(Action<object> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        _checkedChanged -= handler;
    }

    /// <summary>Where the label sits in the space the glyph leaves. Defaults to <see cref="XPlatContentAlignment.MiddleLeft"/>.</summary>
    public XPlatContentAlignment TextAlign
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
    /// The check state. Setting <see cref="XPlatCheckState.Indeterminate"/> while
    /// <see cref="ThreeState"/> is off reads back as <see cref="XPlatCheckState.Unchecked"/>.
    /// </summary>
    public XPlatCheckState CheckState
    {
        get => _checkState;
        set
        {
            var state = _threeState || value != XPlatCheckState.Indeterminate
                ? value
                : XPlatCheckState.Unchecked;

            if (_checkState == state)
            {
                return;
            }

            _checkState = state;
            Invalidate();
            OnCheckStateChanged();
        }
    }

    /// <summary>The two-state view of <see cref="CheckState"/>: true only when it is <see cref="XPlatCheckState.Checked"/>.</summary>
    public bool Checked
    {
        get => _checkState == XPlatCheckState.Checked;
        set => CheckState = value ? XPlatCheckState.Checked : XPlatCheckState.Unchecked;
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
            if (!value && _checkState == XPlatCheckState.Indeterminate)
            {
                CheckState = XPlatCheckState.Unchecked;
            }
        }
    }

    /// <summary>
    /// When true a click or the space bar changes <see cref="CheckState"/>. When false the control
    /// still raises <see cref="XPlatControl.AddClickHandler"/> and the application decides.
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

    /// <summary>The label colour: <see cref="XPlatControl.ForeColor"/>, greyed out while disabled.</summary>
    protected SKColor TextColor => Enabled ? ForeColor : XPlatTheme.DisabledForeground;

    /// <summary>The glyph outline: accent while it carries a mark, the theme border otherwise.</summary>
    protected SKColor GlyphBorderColor
    {
        get
        {
            if (!Enabled)
            {
                return XPlatTheme.DisabledForeground;
            }

            return _checkState != XPlatCheckState.Unchecked || IsMouseOver
                ? XPlatTheme.Accent
                : XPlatTheme.Border;
        }
    }

    /// <summary>The glyph fill, for the states that fill it.</summary>
    protected SKColor GlyphFillColor =>
        !Enabled ? XPlatTheme.DisabledBackground
        : IsMouseOver ? XPlatTheme.AccentHover
        : XPlatTheme.Accent;

    /// <summary>The mark inside a filled glyph.</summary>
    protected SKColor GlyphMarkColor => Enabled ? XPlatTheme.AccentForeground : XPlatTheme.DisabledForeground;

    protected override SKSize MeasureContent(SKSize availableSize)
    {
        var text = XPlatTextRenderer.Measure(Text, Font, float.PositiveInfinity, wrap: false);
        return new SKSize(
            MathF.Ceiling(_glyphSize + _glyphGap + text.Width),
            MathF.Ceiling(MathF.Max(_glyphSize, text.Height)));
    }

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
        XPlatTextRenderer.Draw(canvas, Text, Font, TextColor, label, _textAlign);
    }

    protected override void OnPaintBorder(SKCanvas canvas, SKRect rect)
    {
        // The border slot is used for the focus ring, which is drawn last so it sits above the
        // glyph and the label it surrounds.
        if (IsFocused && Enabled)
        {
            XPlatPainter.DrawFocusRing(canvas, rect, XPlatTheme.FocusRing, 2f, CornerRadius);
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
    protected virtual XPlatCheckState NextCheckState() => _checkState switch
    {
        XPlatCheckState.Unchecked => XPlatCheckState.Checked,
        XPlatCheckState.Checked => _threeState ? XPlatCheckState.Indeterminate : XPlatCheckState.Unchecked,
        _ => XPlatCheckState.Unchecked,
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

    protected override void OnMouseDown(XPlatMouseEventArgs e)
    {
        base.OnMouseDown(e);

        if (e.Button == XPlatMouseButtons.Left && Enabled)
        {
            IsPressed = true;
            CaptureMouse();
            Invalidate();
        }
    }

    protected override void OnMouseUp(XPlatMouseEventArgs e)
    {
        base.OnMouseUp(e);

        if (e.Button != XPlatMouseButtons.Left)
        {
            return;
        }

        bool wasPressed = IsPressed;
        IsPressed = false;
        ReleaseMouseCapture();
        Invalidate();

        if (wasPressed && XPlatGeometry.Contains(ClientRect, e.Location))
        {
            Toggle();
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

    protected override void OnKeyDown(XPlatKeyEventArgs e)
    {
        base.OnKeyDown(e);

        if (e.Key == XPlatKey.Space)
        {
            IsPressed = true;
            e.Handled = true;
            Invalidate();
        }
    }

    protected override void OnKeyUp(XPlatKeyEventArgs e)
    {
        base.OnKeyUp(e);

        if (e.Key == XPlatKey.Space && IsPressed)
        {
            IsPressed = false;
            e.Handled = true;
            Invalidate();
            Toggle();
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
}
