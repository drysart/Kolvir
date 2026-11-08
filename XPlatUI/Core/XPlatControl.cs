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
    private Action<XPlatContextMenuEventArgs>? _contextMenuOpening;
    private SKRect _bounds;
    private SKSize _desiredSize;
    private SKSize _minimumSize;
    private SKSize _maximumSize = new(float.PositiveInfinity, float.PositiveInfinity);
    private XPlatThickness _margin;
    private XPlatThickness _padding;
    private string _text = string.Empty;
    private bool _visible = true;
    private bool _enabled = true;
    private bool _effectiveEnabled = true;
    private bool _effectiveVisible;
    private bool _displayed;
    private Action<object>? _displayedChanged;
    private XPlatControl? _parent;
    private float _opacity = 1f;
    private bool _autoSize;
    private bool _focusable = true;
    private bool _selectable;
    private XPlatSelectionStyle _selectionStyle = XPlatSelectionStyle.Whole;
    private int _layoutSuspends;
    private bool _layoutPending;
    private int _layoutPasses;
    private bool _invalidateDeferred;
    private bool _repaintAfterLayout;
    private bool _disposed;
    private XPlatUiHost? _host;
    private XPlatControlCollection? _controls;
    private List<IDisposable>? _resources;
    private List<SKRect>? _selectionRects;
    private SKColor? _borderColor;
    private XPlatThemeColor _foreColorRole = XPlatThemeColor.Foreground;

    // Appearance set on this control. Null (or, for the background, Inherit) means "not set here".
    private XPlatFont? _font;
    private XPlatBackground? _background;
    private SKColor? _foreground;

    // What the parent chain contributes. Resolved in one walk, then reused until something that
    // can change the answer - a value set on an ancestor, or a new parent - drops it.
    private bool _inheritedResolved;
    private XPlatFont? _inheritedFont;
    private XPlatBackground? _inheritedBackground;
    private SKColor? _inheritedForeground;

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
    /// Adds a handler that runs before this control's context menu is shown, so a control's own
    /// right-click behaviour - the menu it offers, and what it can say about where the pointer
    /// landed - is attached to the control rather than to the window.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The host raises the event for the control under the pointer, or for its nearest ancestor that
    /// has a handler, so nothing outside the control has to know which control was clicked. An
    /// ancestor can therefore give every child the same handler without repeating itself, the way
    /// <see cref="ContextMenu"/> already works.
    /// </para>
    /// <para>
    /// The handler receives the menu the control contributed in
    /// <see cref="XPlatContextMenuEventArgs.Menu"/> and adds to it - its own items in front with
    /// <see cref="XPlatContextMenu.Insert"/>, or after them with <see cref="XPlatContextMenu.Add"/> -
    /// so a spell-checking menu sits beside a text box's Cut/Copy/Paste rather than replacing it.
    /// Assigning a different menu, or clearing the one it was given, is how a handler suppresses what
    /// the control would have offered. The host's own
    /// <see cref="XPlatUiHost.AddContextMenuOpeningHandler"/> is the application-wide fallback and
    /// runs only when no control ran a handler or supplied a menu.
    /// </para>
    /// </remarks>
    /// <returns>A subscription that removes the handler when it is disposed.</returns>
    public IDisposable AddContextMenuOpeningHandler(Action<XPlatContextMenuEventArgs> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        _contextMenuOpening += handler;
        return new XPlatHandlerToken<Action<XPlatContextMenuEventArgs>>(RemoveContextMenuOpeningHandler, handler);
    }

    /// <summary>Removes a handler that <see cref="AddContextMenuOpeningHandler"/> added.</summary>
    public void RemoveContextMenuOpeningHandler(Action<XPlatContextMenuEventArgs> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        _contextMenuOpening -= handler;
    }

    /// <summary>
    /// Raises the context-menu event on this control, or on the nearest ancestor that has a handler,
    /// and reports whether one ran. A control that has no handler of its own passes the event up, so
    /// a container answers for its children.
    /// </summary>
    protected internal virtual bool RaiseContextMenuOpening(XPlatContextMenuEventArgs args)
    {
        ArgumentNullException.ThrowIfNull(args);

        if (_contextMenuOpening is { } handlers)
        {
            handlers(args);
            return true;
        }

        return Parent?.RaiseContextMenuOpening(args) ?? false;
    }

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

    public XPlatControl? Parent
    {
        get => _parent;
        internal set
        {
            if (ReferenceEquals(_parent, value))
            {
                return;
            }

            _parent = value;

            // Moving a subtree under a disabled container (or out of one) changes whether it is
            // enabled without touching a single Enabled flag, so re-read it here. The same goes
            // for a hidden container, and for a host that belongs to a different window.
            UpdateEffectiveEnabled();
            UpdateDisplayed();

            // The same for appearance: the effective font and colours come from wherever the
            // control now sits, so what the old chain contributed is no longer the answer. The
            // layout follows from the add or remove that moved it.
            InvalidateAppearanceTree();
        }
    }

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

    /// <summary>
    /// The top-left corner of this control in host coordinates, walking the parent chain so that
    /// scroll offsets and container padding are included.
    /// </summary>
    /// <remarks>
    /// The value is only meaningful while the control is attached to a host. A window popup
    /// needs it because it is positioned outside the host's coordinate space entirely.
    /// </remarks>
    public SKPoint GetPositionInHost()
    {
        float x = 0f;
        float y = 0f;

        for (XPlatControl? current = this; current is not null; current = current.Parent)
        {
            x += current.Left;
            y += current.Top;

            if (current.Parent is { } parent)
            {
                var offset = parent.GetChildOffset(current);
                x += offset.X;
                y += offset.Y;
            }
        }

        return new SKPoint(x, y);
    }

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
            // a control that moved or shrank leaves a copy of itself behind. The area is worked out
            // in the parent's coordinates and handed to the parent like any other repaint request,
            // so that a container which clips its children clips this too. Asking the host for it
            // directly would reach past a scrolling container, and the area a control scrolled out
            // of sight left behind is the whole content extent - far larger than the surface.
            var previous = ClientRectInParent(_bounds);

            _bounds = normalized;
            OnBoundsChanged();
            InvalidateAreaInParent(previous);
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
            UpdateDisplayed();
        }
    }

    /// <summary>
    /// Whether the control and every container above it are <see cref="Visible"/>. Hiding a
    /// container hides everything inside it, so this is the flag a control that does work on a
    /// timer asks about rather than the one it owns.
    /// </summary>
    /// <remarks>
    /// The getter walks the parent chain rather than caching, so a subtree moved under a hidden
    /// container answers correctly the moment it is attached. Setting <see cref="Visible"/> only
    /// records the control's own flag; <see cref="UpdateDisplayed"/> is what notices that the
    /// answer for the subtree changed and fires <see cref="OnDisplayedChanged"/> down it.
    /// </remarks>
    public bool EffectiveVisible => _visible && (Parent?.EffectiveVisible ?? true);

    /// <summary>
    /// Whether the controls inside this one can be reached - by Tab, by focus, by anything that
    /// walks the tree. True for every control that shows its children; a container that keeps them
    /// in the tree while showing nothing of them reports false.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A collapsed <see cref="XPlatExpander"/> is the case this exists for. It cannot hide its
    /// content - the section is still there, and folding is a matter of how much of it is drawn -
    /// so hiding is not available to it, and without this a Tab walk would happily enter a section
    /// that is showing nothing. Reporting it here instead means the rules that already step over an
    /// invisible or disabled subtree step over a folded section too, without knowing what a folded
    /// section is.
    /// </para>
    /// <para>
    /// Containers that hide their children outright - the pages of an
    /// <see cref="XPlatTabControl"/>, say - do not need to override it: they set
    /// <see cref="Visible"/>, which <see cref="EffectiveVisible"/> already accounts for.
    /// </para>
    /// </remarks>
    protected internal virtual bool IsContentReachable => true;

    /// <summary>
    /// Whether the user can actually see this control: it is <see cref="EffectiveVisible"/>,
    /// attached to a host, and in a window that has been shown and has not closed.
    /// </summary>
    /// <remarks>
    /// A control in a window that has never been shown is not displayed even though its own
    /// <see cref="Visible"/> is true, and neither is one whose root has been taken off a host. A
    /// host with no window of its own - the harnesses draw through one - has nothing that could
    /// hide it, so controls attached to it count as displayed.
    /// </remarks>
    public bool IsDisplayed =>
        EffectiveVisible
        && Host is not null
        && (Window is not { } window || (window.IsShown && !window.IsClosed));

    /// <summary>
    /// Whether the control can be reached right now: it is <see cref="EffectiveVisible"/> and
    /// <see cref="Enabled"/>, and no container above it has stopped showing its content.
    /// </summary>
    /// <remarks>
    /// This is the tree walk the Tab order and <see cref="XPlatUiHost.SetFocus"/> both ask, so a
    /// control inside a collapsed section is left out of the order and cannot be focused by
    /// anything else either. It is answered from the tree every time rather than cached, because a
    /// section can be folded while a control inside it holds the focus.
    /// </remarks>
    internal bool IsReachable =>
        EffectiveVisible
        && Enabled
        && (Parent is not { } parent || (parent.IsReachable && parent.IsContentReachable));

    /// <summary>
    /// Moves the focus out of this control's subtree when a control inside it holds the focus and
    /// the subtree has just stopped being reachable - the case this is for is a container that has
    /// stopped showing its content, such as an expander that was just folded.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The focus goes to the nearest control from here upwards that can still take it, which is
    /// this control itself when only its content went away - the header of a folded section - and
    /// an ancestor when this control is the one that went away. With nothing above it to take the
    /// focus it is cleared.
    /// </para>
    /// <para>
    /// Hiding or disabling a container is deliberately not one of the callers. A hidden container
    /// keeps the focus its child held, which is documented behaviour with a test behind it - a
    /// field inside a hidden panel keeps the caret so that showing the panel again resumes it - and
    /// this change does not revisit that. A collapsed expander is a different thing: the section is
    /// still there and the rest of the page is still usable, so the focus belongs somewhere the
    /// user can see.
    /// </para>
    /// </remarks>
    internal void ReleaseFocusFromSubtree()
    {
        if (Host is not { } host || host.FocusedControl is not { } focused)
        {
            return;
        }

        if (ReferenceEquals(focused, this) || !IsAncestorOf(focused))
        {
            return;
        }

        for (var candidate = this; candidate is not null; candidate = candidate.Parent)
        {
            if (candidate.Focusable && candidate.IsReachable)
            {
                host.SetFocus(candidate);
                return;
            }
        }

        host.SetFocus(null);
    }

    /// <summary>Whether <paramref name="control"/> is somewhere below this one in the tree.</summary>
    private bool IsAncestorOf(XPlatControl control)
    {
        for (var current = control.Parent; current is not null; current = current.Parent)
        {
            if (ReferenceEquals(current, this))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Whether the control accepts input. Disabling a container disables everything inside it, so
    /// this is true only when the control's own flag is set and every ancestor is enabled as well.
    /// </summary>
    /// <remarks>
    /// The getter walks the parent chain rather than caching, so a subtree moved under a disabled
    /// container answers correctly the moment it is attached. Setting the property only records the
    /// control's own flag; <see cref="UpdateEffectiveEnabled"/> is what notices that the answer for
    /// the subtree changed and fires <see cref="OnEnabledChanged"/> down it.
    /// </remarks>
    public bool Enabled
    {
        get => _enabled && (Parent?.Enabled ?? true);
        set
        {
            if (_enabled == value)
            {
                return;
            }

            _enabled = value;
            UpdateEffectiveEnabled();
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

    /// <summary>
    /// How an <see cref="XPlatStackPanel"/> places this control across the axis it does not stack
    /// along. Defaults to <see cref="XPlatAlignment.Stretch"/>, so a child spans the panel unless it
    /// asks for its own size.
    /// </summary>
    /// <remarks>
    /// Only the stack panel reads it. The other containers size their children themselves - a
    /// docked child fills its edge, a table cell fills its cell - so the value is ignored there
    /// rather than being a second opinion on a decision that has already been made.
    /// </remarks>
    public XPlatAlignment HorizontalAlignment { get; set; }

    /// <inheritdoc cref="HorizontalAlignment"/>
    public XPlatAlignment VerticalAlignment { get; set; }

    public XPlatCursor Cursor { get; set; }

    /// <summary>
    /// Returns the cursor to show while the pointer is at <paramref name="pointInLocal"/>
    /// (a point in this control's coordinate space, the same space as
    /// <see cref="XPlatMouseEventArgs.Location"/>). Controls with sub-elements - such as the
    /// links inside an <see cref="XPlatRichLabel"/> - override this to vary the cursor.
    /// </summary>
    public virtual XPlatCursor GetCursorAt(SKPoint pointInLocal) => Cursor;

    /// <summary>True when the control participates in Tab navigation. Defaults to false.</summary>
    /// <remarks>
    /// A control is a tab stop only when this and <see cref="Focusable"/> are both true; see
    /// <see cref="TabIndex"/> for the order the stops are visited in.
    /// </remarks>
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

    /// <summary>
    /// Where this control sits in the Tab order of its container. Lower values come first; the
    /// default, 0, leaves every control in the order it was added.
    /// </summary>
    /// <remarks>
    /// The index orders a control against its siblings only, the way WinForms nests tab order: each
    /// container sorts its own children by this value, so a control inside a nested panel does not
    /// sort against controls outside it. A container that is itself a <see cref="TabStop"/> takes
    /// its own place among its siblings and its children are visited straight after it, which is
    /// what makes a focusable container - a split container, say - one step in the order rather
    /// than something its children are interleaved with. Controls with equal indices keep the order
    /// they were added in, so the walk is deterministic.
    /// </remarks>
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
    /// The font this control draws with, inherited the way WinForms' <c>Font</c> is: a control that
    /// sets one overrides it for itself and for every control under it, and a control that does not
    /// asks its parent, then <see cref="DefaultFont"/> - its class default, which is where the walk
    /// ends.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The effective font is resolved when text is measured or painted, not when the control is
    /// built, so a control created before the theme was configured still adopts it, and a theme
    /// font can be changed - or a font assigned to a control in a live tree - without rebuilding
    /// anything. What the parent chain contributes is cached: one walk up the chain the first time
    /// the font is needed, dropped when an ancestor assigns a font or the control changes parent.
    /// That keeps a glyph measurement to a field read plus one virtual call on the class default.
    /// </para>
    /// <para>
    /// The font is not owned by the control: never dispose one that was inherited or that came from
    /// <see cref="XPlatTheme"/>, and keep any font assigned here alive for as long as it is in use.
    /// </para>
    /// </remarks>
    public XPlatFont Font
    {
        get
        {
            if (_font is { } local)
            {
                return local;
            }

            ResolveInheritedAppearance();
            return _inheritedFont ?? DefaultFont;
        }
        set
        {
            ArgumentNullException.ThrowIfNull(value);

            if (ReferenceEquals(_font, value))
            {
                return;
            }

            _font = value;

            // Text is the size of the font, so this is a layout invalidation, not only a repaint.
            InvalidateAppearanceTree();
            UpdateLayoutAfterAppearanceChange();
        }
    }

    /// <summary>
    /// The font a control of this class draws with when no font has been assigned to it and no
    /// ancestor sets one: the last step of the walk up the tree.
    /// </summary>
    /// <remarks>
    /// The theme supplies the roles, so this is what keeps today's look - a label is
    /// <see cref="XPlatTheme.DefaultFont"/>, a button is <see cref="XPlatTheme.EmphasizedFont"/> -
    /// without any control capturing a font at construction. It is read on every resolution, so a
    /// class is free to vary it (a compact mode, say) and a theme change is picked up immediately.
    /// </remarks>
    protected virtual XPlatFont DefaultFont => XPlatTheme.DefaultFont;

    /// <summary>Forgets the font assigned to this control, so it inherits one again.</summary>
    public void ClearFont()
    {
        if (_font is null)
        {
            return;
        }

        _font = null;
        InvalidateAppearanceTree();
        UpdateLayoutAfterAppearanceChange();
    }

    /// <summary>
    /// How this control fills its background. <see cref="XPlatBackground.Inherit"/> - the initial
    /// value - asks the parent, so a colour assigned to a container answers for everything inside
    /// it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Three assigned states, and the difference between the last two is the point:
    /// <see cref="XPlatBackground.Inherit"/> (nothing set here, keep looking up),
    /// <see cref="XPlatBackground.None"/> (paint nothing here) and an explicit colour. A
    /// <c>null</c> colour would have to mean both "no value" and "draw nothing", and then a label
    /// inside a filled panel would silently start painting a box of its own.
    /// </para>
    /// <para>
    /// A control whose class default is <see cref="XPlatBackground.None"/> - a label, a toggle, any
    /// control that draws no chrome - keeps painting nothing wherever it sits: the walk stops at its
    /// own default. A control that does paint, such as a panel or a field, takes the nearest
    /// ancestor's assigned colour first and its own default only if there is none, which is
    /// WinForms' precedence of a local value over an inherited one over a default.
    /// </para>
    /// <para>
    /// Only a colour is inherited, not <see cref="XPlatBackground.None"/>: a layout container that
    /// paints nothing is the ordinary parent of a card or a field that does, and suppressing what is
    /// inside it would take the card away with the container. A control that must not be filled
    /// assigns None to itself.
    /// </para>
    /// <para>
    /// A palette role assigned here or returned by <see cref="DefaultBackground"/> is resolved while
    /// painting, so a theme change recolours a live control with no rebuild.
    /// </para>
    /// </remarks>
    public XPlatBackground Background
    {
        get
        {
            if (_background is { } local)
            {
                return local;
            }

            var own = DefaultBackground;
            if (own.IsNone)
            {
                return XPlatBackground.None;
            }

            ResolveInheritedAppearance();
            return _inheritedBackground ?? own;
        }
        set
        {
            if (_background == value)
            {
                return;
            }

            _background = value;
            InvalidateAppearanceTree();
        }
    }

    /// <summary>
    /// Background painted while <see cref="Background"/> has never been assigned and no ancestor
    /// sets one. The base control paints nothing; containers default to the theme surface and
    /// editable fields to the input fill.
    /// </summary>
    /// <remarks>
    /// A theme role rather than a colour on purpose: the role is resolved at paint time, so a
    /// palette swap recolours every control that never overrode it.
    /// </remarks>
    protected virtual XPlatBackground DefaultBackground => XPlatBackground.None;

    /// <summary>
    /// The WinForms name for <see cref="Background"/>: null when nothing is painted, otherwise the
    /// colour that will be. Assigning null is <see cref="XPlatBackground.None"/> - an explicit "no
    /// background" - rather than a return to inheritance; use <see cref="ClearBackground"/> for that.
    /// </summary>
    public SKColor? BackColor
    {
        get => Background.Color;
        set => Background = value is { } color
            ? XPlatBackground.FromColor(color)
            : XPlatBackground.None;
    }

    /// <summary>Forgets the background assigned to this control, so it inherits one again.</summary>
    public void ClearBackground()
    {
        if (_background is null)
        {
            return;
        }

        _background = null;
        InvalidateAppearanceTree();
    }

    /// <summary>
    /// How opaque the control and everything inside it is: 0 is invisible, 1 is fully opaque.
    /// </summary>
    /// <remarks>
    /// Below 1 the control paints into a layer of its own, which is what makes the whole subtree
    /// fade as one: two children that overlap at half opacity do not darken where they meet, the
    /// way they would if each were faded on its own. The layer is an offscreen surface per paint,
    /// so leave this at 1 unless the control really is translucent. Unlike <see cref="BackColor"/>,
    /// which only blends the control's own background, this fades its content and children too.
    /// </remarks>
    public float Opacity
    {
        get => _opacity;
        set
        {
            var opacity = float.IsNaN(value) ? 1f : Math.Clamp(value, 0f, 1f);
            if (MathF.Abs(opacity - _opacity) < 0.001f)
            {
                return;
            }

            _opacity = opacity;
            Invalidate();
        }
    }

    /// <summary>
    /// Whether the control paints every pixel inside its own bounds, so that nothing behind it can
    /// show through and whatever it covers does not have to be repainted for its sake.
    /// </summary>
    /// <remarks>
    /// It is an observation about what the control paints, not a switch: it follows
    /// <see cref="BackColor"/>, <see cref="CornerRadius"/> and <see cref="Opacity"/>, so a control
    /// whose background is transparent, whose corners are rounded or which is faded is reported as
    /// not opaque without anything having to be told. Override it when a control paints over its
    /// whole bounds by other means - an <see cref="XPlatImage"/> covered by an opaque bitmap - or
    /// when it punches a hole in a background that is otherwise solid.
    /// </remarks>
    public virtual bool Opaque =>
        _opacity >= 1f && CornerRadius <= 0f && Background.Color is { Alpha: 255 };

    /// <summary>
    /// Text colour, inherited the way <see cref="Font"/> is: a colour assigned to a container
    /// answers for the text of everything inside it, and a control that assigns none keeps
    /// <see cref="ForeColorRole"/>.
    /// </summary>
    /// <remarks>
    /// Only an assigned colour is inherited, not a role. A role is the class default - a group box
    /// caption is <see cref="XPlatThemeColor.MutedForeground"/> whatever its labels are - so
    /// inheriting one would change how existing trees look. The role is resolved when it is read,
    /// which is what keeps a palette swap to a repaint.
    /// </remarks>
    public SKColor Foreground
    {
        get
        {
            if (_foreground is { } local)
            {
                return local;
            }

            ResolveInheritedAppearance();
            return _inheritedForeground ?? XPlatTheme.ColorOf(_foreColorRole);
        }
        set
        {
            if (_foreground == value)
            {
                return;
            }

            _foreground = value;
            InvalidateAppearanceTree();
        }
    }

    /// <summary>The WinForms name for <see cref="Foreground"/>.</summary>
    public SKColor ForeColor
    {
        get => Foreground;
        set => Foreground = value;
    }

    /// <summary>Palette entry used while neither this control nor an ancestor assigns a <see cref="Foreground"/>.</summary>
    public XPlatThemeColor ForeColorRole
    {
        get => _foreColorRole;
        set
        {
            if (_foreColorRole == value)
            {
                return;
            }

            _foreColorRole = value;
            Invalidate();
        }
    }

    /// <summary>Forgets the foreground colour assigned to this control, so it inherits one again.</summary>
    public void ClearForeground()
    {
        if (_foreground is null)
        {
            return;
        }

        _foreground = null;
        InvalidateAppearanceTree();
    }

    /// <summary>
    /// Collects what the parent chain contributes to the three inherited properties in one walk,
    /// and caches it. Each property takes the nearest ancestor that assigns it, which is what makes
    /// an assignment on one control answer for everything under it.
    /// </summary>
    /// <remarks>
    /// The walk stops as soon as all three have an answer, and the result is reused until
    /// <see cref="InvalidateAppearanceTree"/> drops it, so measuring a glyph costs a field read
    /// rather than a trip to the root. What is cached is only what an ancestor assigned; class
    /// defaults and palette roles are resolved when they are read, so a theme change never leaves a
    /// stale answer here.
    /// </remarks>
    private void ResolveInheritedAppearance()
    {
        if (_inheritedResolved)
        {
            return;
        }

        XPlatFont? font = null;
        XPlatBackground? background = null;
        SKColor? foreground = null;

        for (var ancestor = _parent; ancestor is not null; ancestor = ancestor._parent)
        {
            font ??= ancestor._font;

            // Only a colour is inherited, not "paint nothing". A container that draws no background
            // of its own - a layout panel, a transparent row - is the normal parent of something
            // that does, and it must not take the card inside it away with it. A control that wants
            // no background assigns None to itself.
            if (background is null && ancestor._background is { IsNone: false } inheritedBackground)
            {
                background = inheritedBackground;
            }

            foreground ??= ancestor._foreground;

            if (font is not null && background is not null && foreground is not null)
            {
                break;
            }
        }

        _inheritedFont = font;
        _inheritedBackground = background;
        _inheritedForeground = foreground;
        _inheritedResolved = true;
    }

    /// <summary>
    /// Drops the cached inherited appearance of this control and everything under it, and repaints
    /// them. Called whenever a value that can answer for a descendant changes - one assigned here,
    /// or a new parent - so no descendant ever has to watch its ancestors.
    /// </summary>
    internal void InvalidateAppearanceTree()
    {
        _inheritedResolved = false;
        _inheritedFont = null;
        _inheritedBackground = null;
        _inheritedForeground = null;

        OnAppearanceChanged();
        Invalidate();

        if (_controls is { Count: > 0 } children)
        {
            for (int i = 0; i < children.Count; i++)
            {
                children[i].InvalidateAppearanceTree();
            }
        }
    }

    /// <summary>
    /// Called when the effective appearance of this control may have changed: its own value was
    /// assigned or cleared, an ancestor's was, or it moved. A control that caches something derived
    /// from the effective font or colour - a measured row height, a set of derived fonts - drops it
    /// here rather than comparing on every read.
    /// </summary>
    protected virtual void OnAppearanceChanged()
    {
    }

    /// <summary>
    /// Re-runs layout from the top, because a font is the size of the text and a text-sized control
    /// has just changed its mind.
    /// </summary>
    internal void UpdateLayoutAfterAppearanceChange()
    {
        var root = this;
        while (root._parent is { } parent)
        {
            root = parent;
        }

        // In a window the host owns the root's own bounds; a detached tree is its own root.
        if (root._host is { } host)
        {
            host.PerformLayout();
        }

        root.PerformLayoutTree();
    }

    /// <summary>
    /// Lays this control out and then every control under it, whatever their bounds say.
    /// </summary>
    /// <remarks>
    /// A layout pass arranges a child and stops there unless the child's bounds changed, which is
    /// what keeps adding one control to a big tree cheap. A font that changed inside a box that did
    /// not move is exactly the case that rule misses: the text needs other room while the control
    /// still has the same bounds, so every container between it and the root is laid out again here.
    /// This runs when the appearance changes, not on every pass.
    /// </remarks>
    internal void PerformLayoutTree()
    {
        PerformLayout();

        if (_controls is { Count: > 0 } children)
        {
            for (int i = 0; i < children.Count; i++)
            {
                children[i].PerformLayoutTree();
            }
        }
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

    /// <summary>Arranges this control's children, unless layout is suspended.</summary>
    /// <remarks>
    /// Suspending is what makes adding many children at once affordable: every child added lays its
    /// parent out again and asks for a repaint, so filling a panel one control at a time costs a
    /// pass over a growing child list and a walk up the parent chain each time.
    /// <see cref="SuspendLayout"/> folds those into the one pass and the one repaint that
    /// <see cref="ResumeLayout()"/> does at the end.
    /// </remarks>
    public void PerformLayout()
    {
        if (_layoutSuspends > 0)
        {
            _layoutPending = true;
            return;
        }

        RunLayout();
    }

    /// <summary>
    /// Runs one layout pass, folding the repaints it causes into the one this control does at the
    /// end of it.
    /// </summary>
    /// <remarks>
    /// Arranging a child moves it, and a child that moves asks for the area it left to be
    /// repainted, so a pass over many children would otherwise walk up the parent chain once per
    /// child - which is what made a scrolling container with a large content extent slow to scroll.
    /// No frame can be painted while the pass is running anyway, and the pass is going to repaint
    /// what it arranged, so the requests are answered by one repaint of this control instead.
    /// </remarks>
    private void RunLayout()
    {
        _layoutPasses++;
        try
        {
            OnLayout();
        }
        finally
        {
            _layoutPasses--;

            if (_layoutPasses == 0 && _repaintAfterLayout)
            {
                _repaintAfterLayout = false;
                Invalidate();
            }
        }
    }

    /// <summary>Arranges this control's children. Containers override this.</summary>
    protected virtual void OnLayout()
    {
    }

    /// <summary>Defers layout and repainting until <see cref="ResumeLayout()"/>, nesting with every call.</summary>
    /// <remarks>
    /// Both are deferred because a batch that adds or removes many children asks for both per
    /// child, and neither can take effect before the batch ends. What the batch asked for is
    /// dropped or done once, when the outermost <see cref="ResumeLayout()"/> runs.
    /// </remarks>
    public void SuspendLayout() => _layoutSuspends++;

    /// <summary>Ends a <see cref="SuspendLayout"/> and lays the control out if anything asked it to.</summary>
    public void ResumeLayout() => ResumeLayout(true);

    /// <summary>Ends a <see cref="SuspendLayout"/>.</summary>
    /// <param name="performLayout">
    /// False to drop the layout that was deferred, which is what a caller that is about to arrange
    /// the control itself wants.
    /// </param>
    public void ResumeLayout(bool performLayout)
    {
        if (_layoutSuspends == 0)
        {
            throw new InvalidOperationException("ResumeLayout was called without a matching SuspendLayout.");
        }

        _layoutSuspends--;

        if (_layoutSuspends > 0)
        {
            return;
        }

        bool pending = _layoutPending;
        _layoutPending = false;

        if (performLayout && pending)
        {
            RunLayout();
        }

        // The repaints the batch deferred happen after the layout, because arranging the children
        // is what decides where they are.
        if (_invalidateDeferred)
        {
            _invalidateDeferred = false;
            Invalidate();
        }
    }

    /// <summary>
    /// True for an invisible layer that still takes presses but must not take the pointer's hover
    /// away from what it covers. The catcher behind an open popup is one: it has to see the press
    /// that dismisses the popup, while the control underneath keeps tracking the pointer.
    /// </summary>
    internal virtual bool PassesHoverThrough => false;

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

        return HitTestChildren(local) ?? (Enabled ? this : null);
    }

    /// <summary>
    /// Returns the child containing a point, given in this control's client coordinates, or null
    /// when no child does. Children are tested in reverse z-order.
    /// </summary>
    /// <remarks>
    /// A container whose children are laid out in a known order can override this to find the one
    /// candidate instead of walking all of them, which is what makes a container with a very large
    /// number of children cheap to hit test.
    /// </remarks>
    protected virtual XPlatControl? HitTestChildren(SKPoint local)
    {
        if (_controls is null)
        {
            return null;
        }

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

        return null;
    }

    /// <summary>Paints the control and, for containers, its children.</summary>
    public void Render(SKCanvas canvas)
    {
        ArgumentNullException.ThrowIfNull(canvas);

        var fadeAlpha = OpacityAlpha(_opacity);

        if (!Visible || fadeAlpha == 0 || _bounds.Width <= 0f || _bounds.Height <= 0f)
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

        // A faded control paints into a layer so the whole subtree is composited once, at
        // Opacity, rather than every fill and every child blending with what is already under it.
        using var fade = fadeAlpha < 255
            ? new SKPaint { Color = SKColors.White.WithAlpha(fadeAlpha) }
            : null;

        if (fade is not null)
        {
            canvas.SaveLayer(fade);
        }

        OnPaintBackground(canvas, local);
        PaintSelection(canvas);
        OnPaint(canvas, local);
        RenderChildren(canvas);
        OnPaintBorder(canvas, local);

        if (fade is not null)
        {
            canvas.Restore();
        }

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
        // The region is only built once the request is known to be worth making. A control that
        // moves asks for a repaint from inside its parent's layout pass, and that request is
        // always answered by the repaint the pass ends with, so building one here first meant a
        // container arranging thousands of children allocated a native region per child per pass
        // to throw every one of them away.
        var area = ToRectI(XPlatGeometry.Normalize(rect));
        if (area.Width <= 0 || area.Height <= 0 || DeferToParent())
        {
            return;
        }

        using var region = new SKRegion(area);
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

        if (region.IsEmpty || DeferToParent())
        {
            return;
        }

        // A control paints nothing outside its own client area, so nothing it can ask for lies
        // outside it either. Clipping here is what stops a child from reaching past itself, and
        // every level of the chain applies its own on the way up.
        using var area = new SKRegion(region);
        area.Op(ToRectI(ClientRect), SKRegionOperation.Intersect);

        // A hidden control is not painted, but the area it covers still belongs to the parent's
        // background, so it has to be repainted both when a control disappears and when one comes
        // back - which is why visibility does not take part in this. A child that does not cover
        // its own bounds is left in: its background, corners or content let the parent's fresh
        // background through, so repainting the parent without it would leave the child missing.
        if (!invalidateChildren && _controls is not null)
        {
            foreach (var child in _controls)
            {
                if (child.Visible && child.Opaque)
                {
                    area.Op(ToRectI(child.Bounds), SKRegionOperation.Difference);
                }
            }
        }

        if (area.IsEmpty)
        {
            return;
        }

        // The control does not talk to the host. It asks its parent, which decides what a repaint
        // of this child means and passes the answer on up; only the root of the tree, which has no
        // parent, hands the area to the host.
        using var parentArea = TranslateToParent(area);

        if (Parent is { } parent)
        {
            parent.ChildInvalidate(this, parentArea);
        }
        else
        {
            Host?.Invalidate(parentArea);
        }
    }

    /// <summary>
    /// Called by a child when part of it needs repainting, so this control can decide what that
    /// means before the area goes any further up the tree.
    /// </summary>
    /// <param name="child">The child the area came from.</param>
    /// <param name="region">
    /// The area to repaint, in this control's coordinates. It has already been clipped to the
    /// child's own bounds; the callee may narrow it in place.
    /// </param>
    /// <remarks>
    /// The default narrows the area to this control's client area and passes it on, which is what
    /// every container that merely contains its children wants. A container that arranges or clips
    /// them itself overrides this: <see cref="XPlatPanel"/> clips the area to the viewport, so
    /// content scrolled out of sight asks for nothing, and anything else the container knows - a
    /// border it draws around the child, a sibling that covers it - can be taken into account the
    /// same way before calling the base method. Widening the area is allowed too, and is how a
    /// container that decorates a child asks for the decoration to be repainted with it.
    /// </remarks>
    protected internal virtual void ChildInvalidate(XPlatControl child, SKRegion region) =>
        Invalidate(region, invalidateChildren: true);

    /// <summary>
    /// Maps a region from this control's coordinate space into its parent's, rounding each
    /// rectangle out to the whole pixels that cover it.
    /// </summary>
    private SKRegion TranslateToParent(SKRegion region)
    {
        var offset = Parent is { } parent ? parent.GetChildOffset(this) : SKPoint.Empty;
        var dx = _bounds.Left + offset.X;
        var dy = _bounds.Top + offset.Y;

        // Each rectangle is mapped and rounded out on its own rather than translating the region
        // by a rounded origin, which would lose a pixel off an edge whenever the control sits at a
        // fractional offset.
        var mapped = new SKRegion();
        using var rects = region.CreateRectIterator();

        while (rects.Next(out var rect))
        {
            mapped.Op(
                new SKRectI(
                    (int)MathF.Floor(rect.Left + dx),
                    (int)MathF.Floor(rect.Top + dy),
                    (int)MathF.Ceiling(rect.Right + dx),
                    (int)MathF.Ceiling(rect.Bottom + dy)),
                SKRegionOperation.Union);
        }

        return mapped;
    }

    /// <summary>
    /// Maps this control's client area, at <paramref name="bounds"/>, into the parent's
    /// coordinates. A control with no parent is its own root, so the two spaces are the same.
    /// </summary>
    private SKRect ClientRectInParent(SKRect bounds)
    {
        var offset = Parent is { } parent ? parent.GetChildOffset(this) : SKPoint.Empty;

        return new SKRect(
            bounds.Left + offset.X,
            bounds.Top + offset.Y,
            bounds.Right + offset.X,
            bounds.Bottom + offset.Y);
    }

    /// <summary>Asks for an area of the parent that this control has left to be repainted.</summary>
    /// <param name="area">The area, in the parent's coordinates.</param>
    /// <remarks>
    /// The area is no longer part of this control, so it cannot go through
    /// <see cref="Invalidate(SKRegion, bool)"/>: that clips to the client area the control has now.
    /// It is offered to the parent as if the child were asking for a repaint there, which is what
    /// lets a container that clips its children drop the part that was never on screen.
    /// </remarks>
    private void InvalidateAreaInParent(SKRect area)
    {
        if (area.Width <= 0f || area.Height <= 0f)
        {
            return;
        }

        // Deliberately not deferred while this control's own layout is suspended: the deferral
        // repaints the client area it ends up with, and the whole point of this call is the area
        // the control has left. A control being arranged is its parent's business anyway.
        if (FoldIntoParentLayout())
        {
            return;
        }

        using var region = new SKRegion(ToRectI(area));

        if (Parent is { } parent)
        {
            parent.ChildInvalidate(this, region);
        }
        else
        {
            Host?.Invalidate(region);
        }
    }

    /// <summary>
    /// Answers whether something already scheduled will repaint this control, which makes the
    /// request being made now unnecessary.
    /// </summary>
    /// <returns>True when the request can be dropped, having been recorded if it still matters.</returns>
    private bool DeferToParent()
    {
        // A batch that is changing the tree in bulk asks for a repaint per child it touches, and
        // no frame can be painted until the batch is done, so the requests are folded into the one
        // ResumeLayout makes rather than walked up the parent chain one at a time.
        if (_layoutSuspends > 0)
        {
            _invalidateDeferred = true;
            return true;
        }

        // The same for a request that arrives while the parent is arranging its children: that
        // pass is what moves them, so it is answered by the repaint the pass ends with rather than
        // by a walk up the tree per child. A container that clips its children has nothing on
        // screen outside its own client area, which is the area that repaint covers.
        return FoldIntoParentLayout();
    }

    /// <summary>
    /// Folds a repaint request into the layout pass the parent is running, which is what keeps a
    /// container that arranges many children from walking up the parent chain once per child.
    /// </summary>
    /// <returns>True when the parent's pass will answer the request, so there is nothing left to do.</returns>
    private bool FoldIntoParentLayout()
    {
        if (Parent is not { _layoutPasses: > 0 } arranging)
        {
            return false;
        }

        arranging._repaintAfterLayout = true;
        return true;
    }

    /// <summary>Rounds a rectangle out to the whole pixels that cover it.</summary>
    protected internal static SKRectI ToRectI(SKRect rect) => new(
        (int)MathF.Floor(rect.Left),
        (int)MathF.Floor(rect.Top),
        (int)MathF.Ceiling(rect.Right),
        (int)MathF.Ceiling(rect.Bottom));

    /// <summary>Converts a 0..1 opacity to the alpha a paint layer composites with.</summary>
    private static byte OpacityAlpha(float opacity) =>
        (byte)Math.Clamp(MathF.Round(opacity * 255f), 0f, 255f);

    internal void AttachHost(XPlatUiHost? host)
    {
        _host = host;
        UpdateDisplayed();
    }

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

        // Hover is only worth a repaint for a control whose appearance depends on it, so the
        // repaint belongs to the control rather than here: a label, a panel or a table looks the
        // same whether or not the pointer is over it, and repainting one anyway widens the next
        // frame by its whole bounds - a title label invalidated on its way past would take in
        // everything between it and whatever else changed. Controls with a hover look override
        // OnMouseEnter/OnMouseLeave and invalidate what they draw.
        OnMouseEnter();
    }

    internal void RaiseMouseLeave()
    {
        if (!IsMouseOver)
        {
            return;
        }

        IsMouseOver = false;
        OnMouseLeave();
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
        Parent?.OnChildVisibleChanged(this);
        Invalidate();
    }

    /// <summary>
    /// Called on a parent when one of its children changed visibility. Toggling visibility does not
    /// re-run layout, so a container that caches where it placed its children uses this to drop the
    /// cache rather than go on painting a child that is no longer there.
    /// </summary>
    internal virtual void OnChildVisibleChanged(XPlatControl child)
    {
    }

    protected virtual void OnEnabledChanged()
    {
        _enabledChanged?.Invoke(this);
        Invalidate();
    }

    /// <summary>
    /// Adds a handler that runs when <see cref="EffectiveVisible"/> or <see cref="IsDisplayed"/>
    /// changed, for a collaborator that is not a control and cannot override the method.
    /// </summary>
    internal IDisposable AddDisplayedChangedHandler(Action<object> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        _displayedChanged += handler;
        return new XPlatHandlerToken<Action<object>>(RemoveDisplayedChangedHandler, handler);
    }

    /// <summary>Removes a handler that <see cref="AddDisplayedChangedHandler"/> added.</summary>
    internal void RemoveDisplayedChangedHandler(Action<object> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        _displayedChanged -= handler;
    }

    /// <summary>
    /// Called when this control started or stopped being visible to the user, either because of
    /// its own flag or because of a container or a window above it. Controls that animate override
    /// this to start and stop their clock.
    /// </summary>
    /// <remarks>
    /// The call is how a control learns about a hidden ancestor, or about the window closing,
    /// without watching them itself: <see cref="UpdateDisplayed"/> walks the subtree from whatever
    /// changed.
    /// </remarks>
    protected virtual void OnDisplayedChanged() => _displayedChanged?.Invoke(this);

    /// <summary>
    /// Re-reads <see cref="EffectiveVisible"/> and <see cref="IsDisplayed"/> and, when either
    /// changed, tells this control and then its whole subtree. Every path that can change the
    /// answer - a visibility flag, a new parent, a new host, the window being shown or closed -
    /// ends here.
    /// </summary>
    internal void UpdateDisplayed()
    {
        bool effective = EffectiveVisible;
        bool displayed = IsDisplayed;
        if (effective == _effectiveVisible && displayed == _displayed)
        {
            return;
        }

        _effectiveVisible = effective;
        _displayed = displayed;
        OnDisplayedChanged();

        if (_controls is not { Count: > 0 } children)
        {
            return;
        }

        for (int i = 0; i < children.Count; i++)
        {
            children[i].UpdateDisplayed();
        }
    }

    /// <summary>
    /// Re-reads <see cref="Enabled"/> and, when it changed, tells this control and then its whole
    /// subtree. Every path that can change the answer - the local flag and the parent link - ends
    /// here, so a child never has to watch its ancestors.
    /// </summary>
    private void UpdateEffectiveEnabled()
    {
        bool enabled = Enabled;
        if (enabled == _effectiveEnabled)
        {
            return;
        }

        _effectiveEnabled = enabled;
        if (!enabled && IsFocused)
        {
            Host?.SetFocus(null);
        }

        OnEnabledChanged();

        if (_controls is not { Count: > 0 } children)
        {
            return;
        }

        for (int i = 0; i < children.Count; i++)
        {
            children[i].UpdateEffectiveEnabled();
        }
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
