using SkiaSharp;

namespace Kolvir.Feature.VirtualDom;

/// <summary>
/// A description of one window, which the application reconciler creates and patches.
/// </summary>
/// <remarks>
/// <para>
/// The application-level counterpart of a generated control node. Its properties are tracked in the
/// same way: assigning one says what the window should be, leaving it alone says nothing, and a
/// property that a previous render assigned and this one does not is restored to the value the
/// <see cref="Window"/> had when it was created. That is what keeps a user's resize from being
/// snapped back by a render that repeats the size it asked for: an unchanged assignment is not applied
/// again. Assigned geometry is a suggestion until <see cref="GeometryToken"/> changes, then it is a
/// command.
/// </para>
/// <para>
/// <see cref="Content"/> is the one place the application hierarchy meets the control hierarchy. It is
/// an ordinary <see cref="VNode"/> tree, including a bare <see cref="VComponent"/> root, and is
/// reconciled by the control mount inside the host panel that becomes <see cref="Kolvir.Window.Content"/>.
/// </para>
/// <para>
/// The event handlers on this node report changes made outside the render: the user's move or
/// resize, the platform's minimize or maximize, and focus moving to or away from the window. A
/// change the mount applied from this node's own properties never raises them, including a
/// <see cref="GeometryToken"/> re-assert and the pristine restore when an assignment is withdrawn.
/// The handlers come from the most recently rendered node, so a closure over current state is
/// current without recreating the window.
/// </para>
/// <para>
/// There is deliberately no <c>Closed</c> handler. An application closes a window by omitting its
/// node from a later render, so a closed notification would only tell the model what it already
/// did itself. The window's disappearance is the model's action, not an outside change.
/// </para>
/// <para>
/// Owned windows are not supported yet. A window node has no owner property today; when that shape
/// arrives it will be a property on this node rather than a child node, and a window will be able to
/// name another <see cref="VWindow"/> as its owner.
/// </para>
/// </remarks>
/// <example>
/// <code>
/// new VWindow
/// {
///     Key = document,
///     Title = document.Title.Value,
///     ClientSize = new SKSize(480f, 320f),
///     CloseRequested = _ =&gt; Close(document),
///     Content = new VComponent(new DocumentPage(document)),
/// }
/// </code>
/// </example>
public sealed class VWindow : ApplicationVNode
{
    private Specified<string> _title;
    private Specified<SKSize> _clientSize;
    private Specified<SKPoint> _location;
    private Specified<WindowState> _state;
    private Specified<bool> _resizable;
    private Specified<bool> _topMost;
    private Specified<Icon?> _icon;
    private Specified<MenuBar?> _menuBar;
    private Specified<SKSize> _minimumClientSize;
    private Specified<SKSize> _maximumClientSize;

    /// <summary>The title bar text.</summary>
    public string Title
    {
        get => _title.Value;
        set => _title = new Specified<string>(value);
    }

    /// <summary>The client area size, in logical pixels.</summary>
    public SKSize ClientSize
    {
        get => _clientSize.Value;
        set => _clientSize = new Specified<SKSize>(value);
    }

    /// <summary>The top-left corner in logical screen coordinates.</summary>
    public SKPoint Location
    {
        get => _location.Value;
        set => _location = new Specified<SKPoint>(value);
    }

    /// <summary>The display state.</summary>
    public WindowState State
    {
        get => _state.Value;
        set => _state = new Specified<WindowState>(value);
    }

    /// <summary>False for a fixed-size window.</summary>
    public bool Resizable
    {
        get => _resizable.Value;
        set => _resizable = new Specified<bool>(value);
    }

    /// <summary>True to keep the window above all others.</summary>
    public bool TopMost
    {
        get => _topMost.Value;
        set => _topMost = new Specified<bool>(value);
    }

    /// <summary>The window's icon, or null for the platform's generic icon.</summary>
    public Icon? Icon
    {
        get => _icon.Value;
        set => _icon = new Specified<Icon?>(value);
    }

    /// <summary>The window's menu bar, or null to inherit the application-wide bar.</summary>
    public MenuBar? MenuBar
    {
        get => _menuBar.Value;
        set => _menuBar = new Specified<MenuBar?>(value);
    }

    /// <summary>Smallest client area the user may drag the window to, in logical pixels.</summary>
    public SKSize MinimumClientSize
    {
        get => _minimumClientSize.Value;
        set => _minimumClientSize = new Specified<SKSize>(value);
    }

    /// <summary>Largest client area the user may drag the window to, in logical pixels.</summary>
    public SKSize MaximumClientSize
    {
        get => _maximumClientSize.Value;
        set => _maximumClientSize = new Specified<SKSize>(value);
    }

    /// <summary>
    /// Changes when this render wants to take the window's geometry back from the user.
    /// </summary>
    /// <remarks>
    /// <para>
    /// By default assigned geometry is a suggestion: a render that repeats the same
    /// <see cref="ClientSize"/>, <see cref="Location"/>, <see cref="State"/>,
    /// <see cref="MinimumClientSize"/> or <see cref="MaximumClientSize"/> does not re-apply it, so a
    /// user who dragged or resized the window keeps that arrangement. When this token differs from
    /// the previous render's token, every geometry property this node assigns is applied again even
    /// though its value is unchanged - the suggestion becomes a command. An unchanged token applies
    /// nothing.
    /// </para>
    /// <para>
    /// The token is compared the way <see cref="ApplicationVNode.Key"/> is, with
    /// <see cref="object.Equals(object)"/>, so a stable object, a string, or a monotonically
    /// increasing counter all work. A counter is the usual choice: increment it from a "tidy
    /// windows" or "reset layout" action. Only geometry is affected; content and every other
    /// property keep the ordinary changed-value rule. An assignment that is withdrawn still restores
    /// the window's pristine value, whether the token changed or not.
    /// </para>
    /// </remarks>
    public object? GeometryToken { get; init; }

    /// <summary>The window's body, reconciled by the control mount.</summary>
    /// <remarks>
    /// A plain <see cref="VNode"/> is mounted through the tree reconciler. A bare
    /// <see cref="VComponent"/> root is mounted through the component reconciler, into the same host
    /// panel, so <c>Content = new VComponent(page)</c> works without a wrapper panel.
    /// </remarks>
    public VNode? Content { get; init; }

    /// <summary>
    /// Raised when the user asks to close the window with the platform's close button.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The framework always refuses a close it did not itself initiate, so the window stays open and
    /// this handler decides what happens next. With no handler wired, nothing happens. The handler is
    /// read from the most recently rendered node, so a closure over current state is current without
    /// recreating the window.
    /// </para>
    /// <para>
    /// The handler runs outside a render, which makes changing the model and calling
    /// <see cref="ApplicationMount.Invalidate"/> from it legal. The window disappears when a later
    /// render omits it, not when this handler returns.
    /// </para>
    /// </remarks>
    public Action<VWindow>? CloseRequested { get; init; }

    /// <summary>
    /// Raised when the window moves to a new screen position.
    /// </summary>
    /// <remarks>
    /// The payload is the new logical position. The handler is read from the latest node, and it is
    /// not raised for a <see cref="Location"/> the mount applied from that node or for the pristine
    /// location restored when the assignment is withdrawn. GTK4 cannot observe a move, so this
    /// never fires there.
    /// </remarks>
    public Action<VWindow, SKPoint>? Moved { get; init; }

    /// <summary>
    /// Raised when the client area is resized.
    /// </summary>
    /// <remarks>
    /// The payload is the new logical size. The handler is read from the latest node, and it is not
    /// raised for a <see cref="ClientSize"/> the mount applied from that node or for the pristine
    /// size restored when the assignment is withdrawn.
    /// </remarks>
    public Action<VWindow, SKSize>? Resized { get; init; }

    /// <summary>
    /// Raised when the window is minimized, maximized, restored, or otherwise changes display state.
    /// </summary>
    /// <remarks>
    /// The payload is the new <see cref="WindowState"/>. Minimized, maximized and restored all use
    /// this one handler. It is read from the latest node and is not raised for a <see cref="State"/>
    /// the mount applied or for the pristine state restored when the assignment is withdrawn.
    /// </remarks>
    public Action<VWindow, WindowState>? StateChanged { get; init; }

    /// <summary>Raised when the window becomes the active window.</summary>
    /// <remarks>
    /// The handler is read from the latest node. It reports focus moving to the window; it is not a
    /// geometry change and is not affected by the mount's property suppression.
    /// </remarks>
    public Action<VWindow>? Activated { get; init; }

    /// <summary>Raised when the window stops being the active window.</summary>
    /// <remarks>
    /// The handler is read from the latest node. It reports focus moving away from the window; it is
    /// not a geometry change and is not affected by the mount's property suppression.
    /// </remarks>
    public Action<VWindow>? Deactivated { get; init; }

    /// <summary>Applies every property this node assigns to a freshly created window.</summary>
    /// <param name="window">The window to configure.</param>
    internal void ApplyTo(Window window)
    {
        if (_title.IsSpecified)
        {
            window.Title = _title.Value;
        }

        if (_clientSize.IsSpecified)
        {
            window.ClientSize = _clientSize.Value;
        }

        if (_location.IsSpecified)
        {
            window.Location = _location.Value;
        }

        if (_state.IsSpecified)
        {
            window.State = _state.Value;
        }

        if (_resizable.IsSpecified)
        {
            window.Resizable = _resizable.Value;
        }

        if (_topMost.IsSpecified)
        {
            window.TopMost = _topMost.Value;
        }

        if (_icon.IsSpecified)
        {
            window.Icon = _icon.Value;
        }

        if (_menuBar.IsSpecified)
        {
            window.MenuBar = _menuBar.Value;
        }

        if (_minimumClientSize.IsSpecified)
        {
            window.MinimumClientSize = _minimumClientSize.Value;
        }

        if (_maximumClientSize.IsSpecified)
        {
            window.MaximumClientSize = _maximumClientSize.Value;
        }
    }

    /// <summary>Patches a window from the node that produced it on the previous render.</summary>
    /// <param name="previous">The node the window was patched from last time.</param>
    /// <param name="window">The window to patch.</param>
    /// <param name="pristine">What the window's properties were before any node touched them.</param>
    internal void PatchFrom(VWindow previous, Window window, in WindowPristine pristine)
    {
        // A changed token turns the assigned geometry from a suggestion into a command: every value
        // the node assigns is applied even when it equals the previous render's value, while a
        // property neither node assigns is still left alone.
        bool reapplyGeometry = !object.Equals(GeometryToken, previous.GeometryToken);

        if (!_title.Equals(previous._title))
        {
            window.Title = _title.IsSpecified ? _title.Value : pristine.Title;
        }

        if (reapplyGeometry || !_clientSize.Equals(previous._clientSize))
        {
            if (_clientSize.IsSpecified)
            {
                window.ClientSize = _clientSize.Value;
            }
            else if (previous._clientSize.IsSpecified)
            {
                window.ClientSize = pristine.ClientSize;
            }
        }

        if (reapplyGeometry || !_location.Equals(previous._location))
        {
            if (_location.IsSpecified)
            {
                window.Location = _location.Value;
            }
            else if (previous._location.IsSpecified)
            {
                window.Location = pristine.Location;
            }
        }

        if (reapplyGeometry || !_state.Equals(previous._state))
        {
            if (_state.IsSpecified)
            {
                window.State = _state.Value;
            }
            else if (previous._state.IsSpecified)
            {
                window.State = pristine.State;
            }
        }

        if (!_resizable.Equals(previous._resizable))
        {
            window.Resizable = _resizable.IsSpecified ? _resizable.Value : pristine.Resizable;
        }

        if (!_topMost.Equals(previous._topMost))
        {
            window.TopMost = _topMost.IsSpecified ? _topMost.Value : pristine.TopMost;
        }

        if (!_icon.Equals(previous._icon))
        {
            window.Icon = _icon.IsSpecified ? _icon.Value : pristine.Icon;
        }

        if (!_menuBar.Equals(previous._menuBar))
        {
            window.MenuBar = _menuBar.IsSpecified ? _menuBar.Value : pristine.MenuBar;
        }

        if (reapplyGeometry || !_minimumClientSize.Equals(previous._minimumClientSize))
        {
            if (_minimumClientSize.IsSpecified)
            {
                window.MinimumClientSize = _minimumClientSize.Value;
            }
            else if (previous._minimumClientSize.IsSpecified)
            {
                window.MinimumClientSize = pristine.MinimumClientSize;
            }
        }

        if (reapplyGeometry || !_maximumClientSize.Equals(previous._maximumClientSize))
        {
            if (_maximumClientSize.IsSpecified)
            {
                window.MaximumClientSize = _maximumClientSize.Value;
            }
            else if (previous._maximumClientSize.IsSpecified)
            {
                window.MaximumClientSize = pristine.MaximumClientSize;
            }
        }
    }
}
