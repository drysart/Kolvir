using SkiaSharp;

namespace XPlatUI;

/// <summary>
/// Base class for every XPlat UI element. It fills the same role as
/// <c>System.Windows.Forms.Control</c> - bounds, parent/child relationships, layout,
/// painting, hit testing, focus and input - but it has no platform dependency:
/// it paints into an <see cref="SKCanvas"/> and receives input from an <see cref="XPlatUiHost"/>.
/// </summary>
/// <remarks>
/// All coordinates are logical pixels (96 DPI). The host adapter is responsible for
/// scaling the canvas and input coordinates to the device DPI.
/// </remarks>
public class XPlatControl : IDisposable
{
    private Action<object>? _click;
    private Action<object, XPlatMouseEventArgs>? _mouseDown;
    private Action<object, XPlatMouseEventArgs>? _mouseUp;
    private Action<object, XPlatMouseEventArgs>? _mouseMove;
    private Action<object, XPlatMouseEventArgs>? _mouseWheel;
    private Action<object>? _mouseEnter;
    private Action<object>? _mouseLeave;
    private Action<object, XPlatKeyEventArgs>? _keyDown;
    private Action<object, XPlatKeyEventArgs>? _keyUp;
    private Action<object, XPlatTextInputEventArgs>? _textInput;
    private Action<object>? _gotFocus;
    private Action<object>? _lostFocus;
    private Action<object>? _boundsChanged;
    private Action<object>? _textChanged;
    private Action<object>? _visibleChanged;
    private Action<object>? _enabledChanged;
    private SKRect _bounds;
    private SKSize _desiredSize;
    private SKSize _minimumSize;
    private SKSize _maximumSize = new(float.PositiveInfinity, float.PositiveInfinity);
    private XPlatThickness _margin;
    private XPlatThickness _padding;
    private string _text = string.Empty;
    private bool _visible = true;
    private bool _enabled = true;
    private bool _autoSize;
    private bool _focusable = true;
    private bool _selectable;
    private XPlatSelectionStyle _selectionStyle = XPlatSelectionStyle.Whole;
    private bool _disposed;
    private XPlatUiHost? _host;
    private XPlatControlCollection? _controls;
    private List<IDisposable>? _resources;
    private List<SKRect>? _selectionRects;
    private SKColor? _backColor;
    private bool _backColorAssigned;
    private SKColor? _foreColor;
    private SKColor? _borderColor;
    private XPlatThemeColor _foreColorRole = XPlatThemeColor.Foreground;

    public XPlatControl()
    {
        Name = GetType().Name;
    }

    public IDisposable AddClickHandler(Action<object> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        _click += handler;
        return new XPlatHandlerToken<Action<object>>(RemoveClickHandler, handler);
    }

    /// <summary>Removes a handler that <see cref="AddClickHandler"/> added.</summary>
    public void RemoveClickHandler(Action<object> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        _click -= handler;
    }

    public IDisposable AddMouseDownHandler(Action<object, XPlatMouseEventArgs> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        _mouseDown += handler;
        return new XPlatHandlerToken<Action<object, XPlatMouseEventArgs>>(RemoveMouseDownHandler, handler);
    }

    /// <summary>Removes a handler that <see cref="AddMouseDownHandler"/> added.</summary>
    public void RemoveMouseDownHandler(Action<object, XPlatMouseEventArgs> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        _mouseDown -= handler;
    }

    public IDisposable AddMouseUpHandler(Action<object, XPlatMouseEventArgs> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        _mouseUp += handler;
        return new XPlatHandlerToken<Action<object, XPlatMouseEventArgs>>(RemoveMouseUpHandler, handler);
    }

    /// <summary>Removes a handler that <see cref="AddMouseUpHandler"/> added.</summary>
    public void RemoveMouseUpHandler(Action<object, XPlatMouseEventArgs> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        _mouseUp -= handler;
    }

    public IDisposable AddMouseMoveHandler(Action<object, XPlatMouseEventArgs> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        _mouseMove += handler;
        return new XPlatHandlerToken<Action<object, XPlatMouseEventArgs>>(RemoveMouseMoveHandler, handler);
    }

    /// <summary>Removes a handler that <see cref="AddMouseMoveHandler"/> added.</summary>
    public void RemoveMouseMoveHandler(Action<object, XPlatMouseEventArgs> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        _mouseMove -= handler;
    }

    /// <summary>Adds a handler that runs when the wheel turns over the control.</summary>
    /// <remarks>
    /// Set <see cref="XPlatMouseEventArgs.Handled"/> to keep the turn from travelling on to the
    /// container above the control, which is what a control that scrolls its own content does.
    /// </remarks>
    public IDisposable AddMouseWheelHandler(Action<object, XPlatMouseEventArgs> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        _mouseWheel += handler;
        return new XPlatHandlerToken<Action<object, XPlatMouseEventArgs>>(RemoveMouseWheelHandler, handler);
    }

    /// <summary>Removes a handler that <see cref="AddMouseWheelHandler"/> added.</summary>
    public void RemoveMouseWheelHandler(Action<object, XPlatMouseEventArgs> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        _mouseWheel -= handler;
    }

    public IDisposable AddMouseEnterHandler(Action<object> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        _mouseEnter += handler;
        return new XPlatHandlerToken<Action<object>>(RemoveMouseEnterHandler, handler);
    }

    /// <summary>Removes a handler that <see cref="AddMouseEnterHandler"/> added.</summary>
    public void RemoveMouseEnterHandler(Action<object> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        _mouseEnter -= handler;
    }

    public IDisposable AddMouseLeaveHandler(Action<object> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        _mouseLeave += handler;
        return new XPlatHandlerToken<Action<object>>(RemoveMouseLeaveHandler, handler);
    }

    /// <summary>Removes a handler that <see cref="AddMouseLeaveHandler"/> added.</summary>
    public void RemoveMouseLeaveHandler(Action<object> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        _mouseLeave -= handler;
    }

    public IDisposable AddKeyDownHandler(Action<object, XPlatKeyEventArgs> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        _keyDown += handler;
        return new XPlatHandlerToken<Action<object, XPlatKeyEventArgs>>(RemoveKeyDownHandler, handler);
    }

    /// <summary>Removes a handler that <see cref="AddKeyDownHandler"/> added.</summary>
    public void RemoveKeyDownHandler(Action<object, XPlatKeyEventArgs> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        _keyDown -= handler;
    }

    public IDisposable AddKeyUpHandler(Action<object, XPlatKeyEventArgs> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        _keyUp += handler;
        return new XPlatHandlerToken<Action<object, XPlatKeyEventArgs>>(RemoveKeyUpHandler, handler);
    }

    /// <summary>Removes a handler that <see cref="AddKeyUpHandler"/> added.</summary>
    public void RemoveKeyUpHandler(Action<object, XPlatKeyEventArgs> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        _keyUp -= handler;
    }

    public IDisposable AddTextInputHandler(Action<object, XPlatTextInputEventArgs> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        _textInput += handler;
        return new XPlatHandlerToken<Action<object, XPlatTextInputEventArgs>>(RemoveTextInputHandler, handler);
    }

    /// <summary>Removes a handler that <see cref="AddTextInputHandler"/> added.</summary>
    public void RemoveTextInputHandler(Action<object, XPlatTextInputEventArgs> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        _textInput -= handler;
    }

    public IDisposable AddGotFocusHandler(Action<object> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        _gotFocus += handler;
        return new XPlatHandlerToken<Action<object>>(RemoveGotFocusHandler, handler);
    }

    /// <summary>Removes a handler that <see cref="AddGotFocusHandler"/> added.</summary>
    public void RemoveGotFocusHandler(Action<object> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        _gotFocus -= handler;
    }

    public IDisposable AddLostFocusHandler(Action<object> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        _lostFocus += handler;
        return new XPlatHandlerToken<Action<object>>(RemoveLostFocusHandler, handler);
    }

    /// <summary>Removes a handler that <see cref="AddLostFocusHandler"/> added.</summary>
    public void RemoveLostFocusHandler(Action<object> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        _lostFocus -= handler;
    }

    public IDisposable AddBoundsChangedHandler(Action<object> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        _boundsChanged += handler;
        return new XPlatHandlerToken<Action<object>>(RemoveBoundsChangedHandler, handler);
    }

    /// <summary>Removes a handler that <see cref="AddBoundsChangedHandler"/> added.</summary>
    public void RemoveBoundsChangedHandler(Action<object> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        _boundsChanged -= handler;
    }

    public IDisposable AddTextChangedHandler(Action<object> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        _textChanged += handler;
        return new XPlatHandlerToken<Action<object>>(RemoveTextChangedHandler, handler);
    }

    /// <summary>Removes a handler that <see cref="AddTextChangedHandler"/> added.</summary>
    public void RemoveTextChangedHandler(Action<object> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        _textChanged -= handler;
    }

    public IDisposable AddVisibleChangedHandler(Action<object> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        _visibleChanged += handler;
        return new XPlatHandlerToken<Action<object>>(RemoveVisibleChangedHandler, handler);
    }

    /// <summary>Removes a handler that <see cref="AddVisibleChangedHandler"/> added.</summary>
    public void RemoveVisibleChangedHandler(Action<object> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        _visibleChanged -= handler;
    }

    public IDisposable AddEnabledChangedHandler(Action<object> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        _enabledChanged += handler;
        return new XPlatHandlerToken<Action<object>>(RemoveEnabledChangedHandler, handler);
    }

    /// <summary>Removes a handler that <see cref="AddEnabledChangedHandler"/> added.</summary>
    public void RemoveEnabledChangedHandler(Action<object> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        _enabledChanged -= handler;
    }

    /// <summary>Diagnostic name; defaults to the type name.</summary>
    public string Name { get; set; }

    /// <summary>Arbitrary user data associated with the control.</summary>
    public object? Tag { get; set; }

    /// <summary>
    /// Menu shown when the control is right-clicked. When null the nearest ancestor's menu is
    /// used, so a container can give every child the same menu without repeating itself.
    /// </summary>
    public XPlatContextMenu? ContextMenu { get; set; }

    /// <summary>
    /// The menu to show when this control is right-clicked. The default returns
    /// <see cref="ContextMenu"/>, falling back to the parent's menu; controls that build their
    /// menu from their own state (an editable text box, say) override it.
    /// </summary>
    protected internal virtual XPlatContextMenu? BuildContextMenu() => ContextMenu ?? Parent?.BuildContextMenu();

    /// <summary>
    /// Child controls in z-order. Containers such as <see cref="XPlatPanel"/> arrange
    /// them; leaf controls leave the collection empty.
    /// </summary>
    public XPlatControlCollection Controls => _controls ??= new XPlatControlCollection(this);

    /// <summary>True when at least one child is attached. Avoids creating the collection for leaves.</summary>
    internal bool HasChildren => _controls is { Count: > 0 };

    /// <summary>
    /// The extra translation this control applies to one of its children when painting and hit
    /// testing it, on top of the child's <see cref="Bounds"/>. A scrolling container returns the
    /// negative scroll offset here; every other control returns <see cref="SKPoint.Empty"/>.
    /// </summary>
    /// <param name="child">The child about to be painted or hit tested.</param>
    protected internal virtual SKPoint GetChildOffset(XPlatControl child) => SKPoint.Empty;

    public XPlatControl? Parent { get; internal set; }

    /// <summary>The host this control is attached to, if any (resolved through the parent chain).</summary>
    public XPlatUiHost? Host => _host ?? Parent?.Host;

    /// <summary>
    /// The window-scoped service provider, or null when this control is not in a window. Resolved
    /// through <see cref="Host"/>, so it is only available once the control is attached.
    /// </summary>
    public IServiceProvider? Services => Host?.Services;

    /// <summary>Resolves <typeparamref name="TService"/> from <see cref="Services"/>, or null when there is none.</summary>
    /// <typeparam name="TService">The service to resolve.</typeparam>
    public TService? GetService<TService>() where TService : class => Host?.GetService<TService>();

    /// <summary>
    /// The window this control is shown in, or null when it is not in one. Resolved through
    /// <see cref="Host"/>, like <see cref="Services"/>.
    /// </summary>
    /// <remarks>Useful for parenting a dialog on the window the user is looking at.</remarks>
    public XPlatWindow? Window => Host?.Window;

    /// <summary>Text shown by the control. Simple controls use it directly.</summary>
    public virtual string Text
    {
        get => _text;
        set
        {
            value ??= string.Empty;
            if (_text == value)
            {
                return;
            }

            _text = value;
            OnTextChanged();
            Invalidate();
        }
    }

    /// <summary>Bounds relative to the parent control's client area.</summary>
    public SKRect Bounds
    {
        get => _bounds;
        set
        {
            var normalized = XPlatGeometry.Normalize(value);
            if (_bounds.Equals(normalized))
            {
                return;
            }

            // A control paints nothing outside its own bounds, so whatever it covers now is not
            // what it covered a moment ago: the area it is leaving has to be repainted as well, or
            // a control that moved or shrank leaves a copy of itself behind.
            var previous = Host is null
                ? SKRect.Empty
                : XPlatUiHost.ToHost(this, ClientRect);

            _bounds = normalized;
            OnBoundsChanged();

            if (previous.Width > 0f && previous.Height > 0f && Host is { } host)
            {
                host.IncludeInNextPaint(previous);
            }
        }
    }

    public SKPoint Location
    {
        get => new(_bounds.Left, _bounds.Top);
        set => Bounds = new SKRect(value.X, value.Y, value.X + _bounds.Width, value.Y + _bounds.Height);
    }

    public SKSize Size
    {
        get => new(_bounds.Width, _bounds.Height);
        set => Bounds = new SKRect(_bounds.Left, _bounds.Top, _bounds.Left + value.Width, _bounds.Top + value.Height);
    }

    public float Left
    {
        get => _bounds.Left;
        set => Bounds = new SKRect(value, _bounds.Top, value + _bounds.Width, _bounds.Bottom);
    }

    public float Top
    {
        get => _bounds.Top;
        set => Bounds = new SKRect(_bounds.Left, value, _bounds.Right, value + _bounds.Height);
    }

    public float Width
    {
        get => _bounds.Width;
        set => Bounds = new SKRect(_bounds.Left, _bounds.Top, _bounds.Left + MathF.Max(0f, value), _bounds.Bottom);
    }

    public float Height
    {
        get => _bounds.Height;
        set => Bounds = new SKRect(_bounds.Left, _bounds.Top, _bounds.Right, _bounds.Top + MathF.Max(0f, value));
    }

    public float Right => _bounds.Right;

    public float Bottom => _bounds.Bottom;

    /// <summary>The control's own coordinate space: (0, 0, Width, Height).</summary>
    public SKRect ClientRect => new(0f, 0f, _bounds.Width, _bounds.Height);

    /// <summary>The area available to content, i.e. <see cref="ClientRect"/> minus padding.</summary>
    public SKRect ContentRect => XPlatGeometry.Deflate(ClientRect, Padding);

    public XPlatThickness Margin
    {
        get => _margin;
        set
        {
            if (_margin == value)
            {
                return;
            }

            _margin = value;
            Invalidate();
        }
    }

    public XPlatThickness Padding
    {
        get => _padding;
        set
        {
            if (_padding == value)
            {
                return;
            }

            _padding = value;
            Invalidate();
        }
    }

    public bool Visible
    {
        get => _visible;
        set
        {
            if (_visible == value)
            {
                return;
            }

            _visible = value;
            if (!value && IsFocused)
            {
                Host?.SetFocus(null);
            }

            OnVisibleChanged();
        }
    }

    public bool Enabled
    {
        get => _enabled;
        set
        {
            if (_enabled == value)
            {
                return;
            }

            _enabled = value;
            if (!value && IsFocused)
            {
                Host?.SetFocus(null);
            }

            OnEnabledChanged();
        }
    }

    /// <summary>When true, containers size the control to its <see cref="DesiredSize"/>.</summary>
    public bool AutoSize
    {
        get => _autoSize;
        set
        {
            if (_autoSize == value)
            {
                return;
            }

            _autoSize = value;
            Invalidate();
        }
    }

    /// <summary>Size computed by the last call to <see cref="Measure"/>.</summary>
    public SKSize DesiredSize
    {
        get => _desiredSize;
        protected set => _desiredSize = value;
    }

    public SKSize MinimumSize
    {
        get => _minimumSize;
        set => _minimumSize = new SKSize(MathF.Max(0f, value.Width), MathF.Max(0f, value.Height));
    }

    public SKSize MaximumSize
    {
        get => _maximumSize;
        set => _maximumSize = new SKSize(
            value.Width <= 0f ? float.PositiveInfinity : value.Width,
            value.Height <= 0f ? float.PositiveInfinity : value.Height);
    }

    public DockStyle Dock { get; set; }

    public XPlatCursor Cursor { get; set; }

    /// <summary>
    /// Returns the cursor to show while the pointer is at <paramref name="pointInLocal"/>
    /// (a point in this control's coordinate space, the same space as
    /// <see cref="XPlatMouseEventArgs.Location"/>). Controls with sub-elements - such as the
    /// links inside an <see cref="XPlatRichLabel"/> - override this to vary the cursor.
    /// </summary>
    public virtual XPlatCursor GetCursorAt(SKPoint pointInLocal) => Cursor;

    /// <summary>True when the control participates in Tab navigation.</summary>
    public bool TabStop { get; set; }

    /// <summary>True when the control may hold keyboard focus. Defaults to true.</summary>
    /// <remarks>
    /// A control that sets this to false is still an ordinary mouse target - it takes clicks,
    /// drags and wheel turns, and it can capture the mouse - it simply never becomes the focused
    /// control: pressing it does not focus it, <see cref="Focus"/> does nothing, and Tab skips it.
    /// That is what the scrollbars an <see cref="XPlatPanel"/> creates for itself need, because the
    /// focus belongs to whatever the user is actually working in and a scrollbar must not take it
    /// away. A standalone <see cref="XPlatScrollBar"/> leaves this alone so it can be driven from
    /// the keyboard like any other slider.
    /// </remarks>
    public bool Focusable
    {
        get => _focusable;
        set
        {
            if (_focusable == value)
            {
                return;
            }

            _focusable = value;
            if (!value && IsFocused)
            {
                Host?.SetFocus(null);
            }
        }
    }

    public int TabIndex { get; set; }

    public bool IsFocused { get; private set; }

    public bool IsMouseOver { get; private set; }

    /// <summary>True while a mouse button is held down on the control.</summary>
    public bool IsPressed { get; internal set; }

    /// <summary>
    /// True when the control takes part in pointer-driven selections: pressing on it and
    /// dragging selects it, and it contributes its text to the clipboard. Controls opt in, so
    /// the default is false.
    /// </summary>
    public bool Selectable
    {
        get => _selectable;
        set
        {
            if (_selectable == value)
            {
                return;
            }

            _selectable = value;
            if (!value)
            {
                Host?.Selection.Refresh();
            }

            Invalidate();
        }
    }

    /// <summary>
    /// How the control takes part in a selection. Only meaningful when <see cref="Selectable"/> is
    /// true; the default, <see cref="XPlatSelectionStyle.Whole"/>, selects the control as a unit.
    /// </summary>
    public XPlatSelectionStyle SelectionStyle
    {
        get => _selectionStyle;
        set
        {
            if (_selectionStyle == value)
            {
                return;
            }

            _selectionStyle = value;
            Host?.Selection.Refresh();
            Invalidate();
        }
    }

    /// <summary>
    /// Number of selectable units in the control: 0 when it is not selectable, 1 for a whole
    /// control and the content length for a partial one.
    /// </summary>
    public virtual int SelectionUnitCount => !_selectable
        ? 0
        : _selectionStyle == XPlatSelectionStyle.Whole ? 1 : Text.Length;

    /// <summary>The slice of this control covered by the host's selection, or an empty range.</summary>
    public XPlatSelectionRange SelectionRange => Host?.Selection.RangeOf(this) ?? default;

    /// <summary>True when at least part of the control is selected.</summary>
    public bool IsSelected => !SelectionRange.IsEmpty;

    /// <summary>
    /// Maps a point in this control's coordinate space to the nearest selection offset, in the
    /// range 0..<see cref="SelectionUnitCount"/>. A whole control returns the middle of its single
    /// unit (0.5) for any point inside it. Controls with internal content override this.
    /// </summary>
    public virtual float GetSelectionOffset(SKPoint pointInLocal)
    {
        int units = SelectionUnitCount;
        if (units == 0)
        {
            return 0f;
        }

        if (_selectionStyle == XPlatSelectionStyle.Whole)
        {
            return 0.5f;
        }

        // Fallback for partial controls that do not map their own content: proportional.
        var content = ContentRect;
        if (content.Width <= 0f)
        {
            return 0f;
        }

        var ratio = (pointInLocal.X - content.Left) / content.Width;
        return Math.Clamp(ratio * units, 0f, units);
    }

    /// <summary>
    /// Adds the rectangles (in this control's coordinate space) that cover the given selection
    /// slice. Multi-line controls add one rectangle per line.
    /// </summary>
    public virtual void GetSelectionRects(int start, int length, List<SKRect> output)
    {
        ArgumentNullException.ThrowIfNull(output);

        if (length <= 0)
        {
            return;
        }

        if (_selectionStyle == XPlatSelectionStyle.Whole || (start <= 0 && length >= SelectionUnitCount))
        {
            output.Add(ClientRect);
        }
    }

    /// <summary>
    /// The text this control contributes to the clipboard for the given selection slice.
    /// </summary>
    public virtual string GetSelectionText(int start, int length)
    {
        if (length <= 0)
        {
            return string.Empty;
        }

        var text = Text;
        if (string.IsNullOrEmpty(text))
        {
            return string.Empty;
        }

        if (_selectionStyle == XPlatSelectionStyle.Whole)
        {
            return text;
        }

        start = Math.Clamp(start, 0, text.Length);
        length = Math.Clamp(length, 0, text.Length - start);
        return length <= 0 ? string.Empty : text.Substring(start, length);
    }

    /// <summary>
    /// The HTML this control contributes to the clipboard for the given selection slice, so that
    /// formatted text survives a copy into a rich target. Returning null - the default - means
    /// this control has nothing richer to say than <see cref="GetSelectionText"/> and the plain
    /// text is used instead, escaped.
    /// </summary>
    public virtual string? GetSelectionHtml(int start, int length) => null;

    /// <summary>
    /// Declares that this control is starting a selection of its own - a caret or an internal
    /// text selection, for example. Only one selection may exist at a time, so this clears the
    /// host's control-level selection and tells any other control to drop its internal selection
    /// before the caller proceeds.
    /// </summary>
    public void ClaimSelection() => Host?.ClaimSelection(this);

    /// <summary>
    /// Called when another control (or the host) takes over the selection, so this control can
    /// drop whatever selection state it keeps to itself. The host's selection is already cleared
    /// by the time this runs.
    /// </summary>
    protected virtual void OnSelectionCleared(EventArgs e)
    {
    }

    internal void RaiseSelectionCleared()
    {
        OnSelectionCleared(EventArgs.Empty);
        Invalidate();
    }

    /// <summary>
    /// Background fill. Null means transparent. While it has never been assigned the control
    /// paints <see cref="DefaultBackColor"/> instead, so it keeps following the theme.
    /// </summary>
    public SKColor? BackColor
    {
        get => _backColorAssigned ? _backColor : DefaultBackColor;
        set
        {
            _backColor = value;
            _backColorAssigned = true;
        }
    }

    /// <summary>
    /// Background painted while <see cref="BackColor"/> has never been assigned. The base control
    /// is transparent; containers default to the theme surface and editable fields to the input
    /// fill.
    /// </summary>
    protected virtual SKColor? DefaultBackColor => null;

    /// <summary>
    /// Text colour. While it has never been assigned the control paints
    /// <see cref="ForeColorRole"/>, which is how a label stays readable when the theme changes.
    /// </summary>
    public SKColor ForeColor
    {
        get => _foreColor ?? XPlatTheme.ColorOf(_foreColorRole);
        set => _foreColor = value;
    }

    /// <summary>
    /// Palette entry used while <see cref="ForeColor"/> has never been assigned. Defaults to
    /// <see cref="XPlatThemeColor.Foreground"/>; use <see cref="XPlatThemeColor.MutedForeground"/> for
    /// secondary text.
    /// </summary>
    public XPlatThemeColor ForeColorRole
    {
        get => _foreColorRole;
        set => _foreColorRole = value;
    }

    /// <summary>
    /// Border colour. While it has never been assigned the control paints
    /// <see cref="XPlatTheme.Border"/>.
    /// </summary>
    public SKColor BorderColor
    {
        get => _borderColor ?? XPlatTheme.Border;
        set => _borderColor = value;
    }

    public float BorderWidth { get; set; }

    public float CornerRadius { get; set; }

    /// <summary>
    /// Computes the preferred size of the control for the given available space.
    /// The result includes padding but excludes <see cref="Margin"/>.
    /// </summary>
    public virtual SKSize Measure(SKSize availableSize)
    {
        var contentAvailable = new SKSize(
            MathF.Max(0f, availableSize.Width - Padding.Horizontal),
            MathF.Max(0f, availableSize.Height - Padding.Vertical));

        var content = MeasureContent(contentAvailable);
        var size = new SKSize(
            Clamp(content.Width + Padding.Horizontal, MinimumSize.Width, MaximumSize.Width),
            Clamp(content.Height + Padding.Vertical, MinimumSize.Height, MaximumSize.Height));

        DesiredSize = size;
        return size;
    }

    /// <summary>
    /// Measures the content area (padding already removed). Override in derived controls.
    /// </summary>
    protected virtual SKSize MeasureContent(SKSize availableSize) => SKSize.Empty;

    /// <summary>Arranges this control's children. Containers override this.</summary>
    public virtual void PerformLayout()
    {
    }

    /// <summary>Returns the deepest visible, enabled control containing the point.</summary>
    /// <param name="pointInParent">Point expressed in the parent's coordinate space.</param>
    public virtual XPlatControl? HitTest(SKPoint pointInParent)
    {
        if (!Visible)
        {
            return null;
        }

        var local = new SKPoint(pointInParent.X - _bounds.Left, pointInParent.Y - _bounds.Top);
        if (!XPlatGeometry.Contains(ClientRect, local))
        {
            return null;
        }

        if (_controls is not null)
        {
            for (int i = _controls.Count - 1; i >= 0; i--)
            {
                var child = _controls[i];
                var offset = GetChildOffset(child);
                var childPoint = offset.X == 0f && offset.Y == 0f
                    ? local
                    : new SKPoint(local.X - offset.X, local.Y - offset.Y);

                var hit = child.HitTest(childPoint);
                if (hit is not null)
                {
                    return hit;
                }
            }
        }

        return Enabled ? this : null;
    }

    /// <summary>Paints the control and, for containers, its children.</summary>
    public void Render(SKCanvas canvas)
    {
        ArgumentNullException.ThrowIfNull(canvas);

        if (!Visible || _bounds.Width <= 0f || _bounds.Height <= 0f)
        {
            return;
        }

        // Nothing this control or anything inside it paints can leave its own bounds - the clip
        // below takes care of that - so a control that does not reach the clip is a whole subtree
        // that does not have to be walked.
        if (canvas.QuickReject(_bounds))
        {
            return;
        }

        canvas.Save();
        canvas.Translate(_bounds.Left, _bounds.Top);

        var local = ClientRect;
        if (CornerRadius > 0f)
        {
            canvas.ClipRoundRect(new SKRoundRect(local, CornerRadius), SKClipOperation.Intersect, antialias: true);
        }
        else
        {
            canvas.ClipRect(local);
        }

        OnPaintBackground(canvas, local);
        PaintSelection(canvas);
        OnPaint(canvas, local);
        RenderChildren(canvas);
        OnPaintBorder(canvas, local);

        canvas.Restore();
    }

    /// <summary>Moves keyboard focus to this control, unless it is not <see cref="Focusable"/>.</summary>
    public void Focus()
    {
        if (Focusable)
        {
            Host?.SetFocus(this);
        }
    }

    /// <summary>Routes all mouse input to this control until the capture is released.</summary>
    public void CaptureMouse() => Host?.CaptureMouse(this);

    public void ReleaseMouseCapture() => Host?.ReleaseMouseCapture(this);

    /// <summary>
    /// Registers a disposable that belongs to this control, so it is disposed with the control and
    /// nothing has to override <see cref="Dispose(bool)"/> just to release what it built.
    /// </summary>
    /// <param name="resource">The resource to take ownership of.</param>
    /// <returns>The same resource, so a call can wrap a construction.</returns>
    protected internal T Own<T>(T resource)
        where T : IDisposable
    {
        ArgumentNullException.ThrowIfNull(resource);

        (_resources ??= []).Add(resource);
        return resource;
    }

    /// <summary>
    /// Creates a timer owned by this control: it is disposed with the control, so a control that
    /// animates never has to override <see cref="Dispose(bool)"/> just to release its clock.
    /// </summary>
    /// <param name="interval">The interval between ticks; must be positive.</param>
    /// <param name="handler">The handler for the timer's <see cref="XPlatTimer.AddTickHandler"/> event.</param>
    /// <returns>The new timer, which starts out stopped.</returns>
    /// <remarks>
    /// Only run it while it is actually needed. A timer that keeps ticking for a control nobody is
    /// looking at is exactly the cost that makes a single application-wide tick a bad fit.
    /// </remarks>
    protected internal XPlatTimer CreateTimer(TimeSpan interval, Action<object> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);

        var timer = new XPlatTimer(interval);
        timer.AddTickHandler(handler);
        return Own(timer);
    }

    /// <summary>Requests a repaint of this control and everything it contains.</summary>
    public void Invalidate() => Invalidate(ClientRect, true);

    /// <summary>Requests a repaint of this control.</summary>
    /// <param name="invalidateChildren">
    /// True to repaint the controls inside it as well. False repaints only this control, which is
    /// what a control that changed its own appearance but not its layout wants: the children are
    /// left out of the area, so the paint does not have to visit them.
    /// </param>
    public void Invalidate(bool invalidateChildren) => Invalidate(ClientRect, invalidateChildren);

    /// <summary>Requests a repaint of an area of this control, in its own coordinates.</summary>
    /// <param name="rect">The area to repaint. Anything outside the client area is dropped.</param>
    public void Invalidate(SKRect rect) => Invalidate(rect, true);

    /// <summary>Requests a repaint of an area of this control, in its own coordinates.</summary>
    /// <param name="rect">The area to repaint. Anything outside the client area is dropped.</param>
    /// <param name="invalidateChildren">True to repaint the controls inside the area as well.</param>
    public void Invalidate(SKRect rect, bool invalidateChildren)
    {
        using var region = new SKRegion(ToRectI(XPlatGeometry.Normalize(rect)));
        Invalidate(region, invalidateChildren);
    }

    /// <summary>Requests a repaint of a region of this control, in its own coordinates.</summary>
    /// <param name="region">The area to repaint. Anything outside the client area is dropped.</param>
    public void Invalidate(SKRegion region) => Invalidate(region, true);

    /// <summary>Requests a repaint of a region of this control, in its own coordinates.</summary>
    /// <param name="region">The area to repaint. Anything outside the client area is dropped.</param>
    /// <param name="invalidateChildren">True to repaint the controls inside the area as well.</param>
    public void Invalidate(SKRegion region, bool invalidateChildren)
    {
        ArgumentNullException.ThrowIfNull(region);

        var host = Host;
        if (host is null)
        {
            Parent?.Invalidate();
            return;
        }

        if (region.IsEmpty)
        {
            return;
        }

        // A hidden control is not painted, but the area it covers still belongs to the parent's
        // background, so it has to be repainted both when a control disappears and when one comes
        // back - which is why visibility does not take part in this.
        //
        // The control is clipped to its client area, so nothing outside it can have changed, and a
        // child is painted over its own bounds, so its area can be taken back out again.
        using var area = new SKRegion(region);
        area.Op(ToRectI(ClientRect), SKRegionOperation.Intersect);

        if (!invalidateChildren && _controls is not null)
        {
            foreach (var child in _controls)
            {
                if (child.Visible)
                {
                    area.Op(ToRectI(child.Bounds), SKRegionOperation.Difference);
                }
            }
        }

        if (area.IsEmpty)
        {
            return;
        }

        // A region is whole pixels, so each rectangle is mapped and rounded out on its own rather
        // than translating the region by a rounded origin, which would lose a pixel off an edge
        // every time the control sits at a fractional offset.
        using var hostArea = new SKRegion();
        using var rects = area.CreateRectIterator();

        while (rects.Next(out var rect))
        {
            var mapped = XPlatUiHost.ToHost(this, new SKRect(rect.Left, rect.Top, rect.Right, rect.Bottom));
            hostArea.Op(ToRectI(mapped), SKRegionOperation.Union);
        }

        host.Invalidate(hostArea);
    }

    /// <summary>Rounds a rectangle out to the whole pixels that cover it.</summary>
    private static SKRectI ToRectI(SKRect rect) => new(
        (int)MathF.Floor(rect.Left),
        (int)MathF.Floor(rect.Top),
        (int)MathF.Ceiling(rect.Right),
        (int)MathF.Ceiling(rect.Bottom));

    internal void AttachHost(XPlatUiHost? host) => _host = host;

    /// <summary>Called after a child is added. Re-runs layout and repaints.</summary>
    internal virtual void OnControlAdded(XPlatControl control)
    {
        PerformLayout();
        Invalidate();
    }

    /// <summary>Called after a child is removed. Re-runs layout and repaints.</summary>
    internal virtual void OnControlRemoved(XPlatControl control)
    {
        PerformLayout();
        Invalidate();
    }

    internal void RaiseMouseDown(XPlatMouseEventArgs e)
    {
        if (Enabled)
        {
            OnMouseDown(e);
        }
    }

    internal void RaiseMouseUp(XPlatMouseEventArgs e)
    {
        if (Enabled)
        {
            OnMouseUp(e);
        }
    }

    internal void RaiseMouseMove(XPlatMouseEventArgs e)
    {
        if (Enabled)
        {
            OnMouseMove(e);
        }
    }

    /// <summary>Delivers a wheel turn. Returns true when the control consumed it.</summary>
    internal bool RaiseMouseWheel(XPlatMouseEventArgs e)
    {
        if (Enabled)
        {
            OnMouseWheel(e);
        }

        return e.Handled;
    }

    internal void RaiseMouseEnter()
    {
        if (IsMouseOver)
        {
            return;
        }

        IsMouseOver = true;
        OnMouseEnter();
        Invalidate();
    }

    internal void RaiseMouseLeave()
    {
        if (!IsMouseOver)
        {
            return;
        }

        IsMouseOver = false;
        OnMouseLeave();
        Invalidate();
    }

    /// <summary>Delivers a key press. Returns true when the control consumed the key.</summary>
    internal bool RaiseKeyDown(XPlatKeyEventArgs e)
    {
        if (Enabled)
        {
            OnKeyDown(e);
        }

        return e.Handled;
    }

    internal void RaiseKeyUp(XPlatKeyEventArgs e)
    {
        if (Enabled)
        {
            OnKeyUp(e);
        }
    }

    internal void RaiseTextInput(XPlatTextInputEventArgs e)
    {
        if (Enabled)
        {
            OnTextInput(e);
        }
    }

    internal void RaiseGotFocus()
    {
        IsFocused = true;
        OnGotFocus();
        Invalidate();
    }

    internal void RaiseLostFocus()
    {
        IsFocused = false;
        IsPressed = false;
        OnLostFocus();
        Invalidate();
    }

    protected virtual void OnPaintBackground(SKCanvas canvas, SKRect rect)
    {
        if (BackColor is { } backColor)
        {
            XPlatPainter.Fill(canvas, rect, backColor, CornerRadius);
        }
    }

    protected virtual void OnPaint(SKCanvas canvas, SKRect rect)
    {
    }

    protected virtual void OnPaintBorder(SKCanvas canvas, SKRect rect)
    {
        if (BorderWidth > 0f)
        {
            XPlatPainter.DrawBorder(canvas, rect, BorderColor, BorderWidth, CornerRadius);
        }
    }

    /// <summary>
    /// Fills the part of the host selection that covers this control, between the background and
    /// the content, so selected text stays readable.
    /// </summary>
    private void PaintSelection(SKCanvas canvas)
    {
        var range = SelectionRange;
        if (range.IsEmpty)
        {
            return;
        }

        var rects = _selectionRects ??= new List<SKRect>(4);
        rects.Clear();
        GetSelectionRects(range.Start, range.Length, rects);

        foreach (var rect in rects)
        {
            XPlatPainter.Fill(canvas, rect, XPlatTheme.Selection);
        }
    }

    /// <summary>Paints child controls in z-order.</summary>
    protected virtual void RenderChildren(SKCanvas canvas)
    {
        if (_controls is null)
        {
            return;
        }

        foreach (var child in _controls)
        {
            child.Render(canvas);
        }
    }

    protected virtual void OnMouseDown(XPlatMouseEventArgs e) => _mouseDown?.Invoke(this, e);

    protected virtual void OnMouseUp(XPlatMouseEventArgs e) => _mouseUp?.Invoke(this, e);

    protected virtual void OnMouseMove(XPlatMouseEventArgs e) => _mouseMove?.Invoke(this, e);

    /// <summary>
    /// Called when the wheel turns over the control. The default raises
    /// <see cref="AddMouseWheelHandler"/>. A control that scrolls content of its own overrides
    /// this, moves, and leaves <see cref="XPlatMouseEventArgs.Handled"/> set - and leaves it clear
    /// when it has nothing left to give, so the turn can travel to the container above it.
    /// </summary>
    protected virtual void OnMouseWheel(XPlatMouseEventArgs e) => _mouseWheel?.Invoke(this, e);

    protected virtual void OnMouseEnter() => _mouseEnter?.Invoke(this);

    protected virtual void OnMouseLeave() => _mouseLeave?.Invoke(this);

    protected virtual void OnClick() => _click?.Invoke(this);

    protected virtual void OnKeyDown(XPlatKeyEventArgs e) => _keyDown?.Invoke(this, e);

    protected virtual void OnKeyUp(XPlatKeyEventArgs e) => _keyUp?.Invoke(this, e);

    protected virtual void OnTextInput(XPlatTextInputEventArgs e) => _textInput?.Invoke(this, e);

    protected virtual void OnGotFocus() => _gotFocus?.Invoke(this);

    protected virtual void OnLostFocus() => _lostFocus?.Invoke(this);

    protected virtual void OnTextChanged()
    {
        _textChanged?.Invoke(this);
        Host?.Selection.Refresh();
        PerformLayout();
    }

    protected virtual void OnVisibleChanged()
    {
        _visibleChanged?.Invoke(this);
        Invalidate();
    }

    protected virtual void OnEnabledChanged()
    {
        _enabledChanged?.Invoke(this);
        Invalidate();
    }

    protected virtual void OnBoundsChanged()
    {
        _boundsChanged?.Invoke(this);
        PerformLayout();
        Invalidate();
    }

    private static float Clamp(float value, float min, float max)
    {
        if (max < min)
        {
            max = min;
        }

        return MathF.Min(MathF.Max(value, min), max);
    }

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    protected virtual void Dispose(bool disposing)
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        if (disposing)
        {
            if (_resources is not null)
            {
                // Reverse order, so whatever was built on top of something else goes first.
                for (int i = _resources.Count - 1; i >= 0; i--)
                {
                    _resources[i].Dispose();
                }

                _resources = null;
            }

            if (_controls is not null)
            {
                var children = _controls.ToArray();
                _controls.Clear();
                foreach (var child in children)
                {
                    child.Parent = null;
                    child.Dispose();
                }

                _controls = null;
            }
        }
    }
}
