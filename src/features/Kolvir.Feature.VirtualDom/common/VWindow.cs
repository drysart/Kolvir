using SkiaSharp;

namespace Kolvir.VirtualDom;

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
/// again.
/// </para>
/// <para>
/// <see cref="Content"/> is the one place the application hierarchy meets the control hierarchy. It is
/// an ordinary <see cref="VNode"/> tree, including a bare <see cref="VComponent"/> root, and is
/// reconciled by the control mount inside the host panel that becomes <see cref="Kolvir.Window.Content"/>.
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
        if (!_title.Equals(previous._title))
        {
            window.Title = _title.IsSpecified ? _title.Value : pristine.Title;
        }

        if (!_clientSize.Equals(previous._clientSize))
        {
            window.ClientSize = _clientSize.IsSpecified ? _clientSize.Value : pristine.ClientSize;
        }

        if (!_location.Equals(previous._location))
        {
            window.Location = _location.IsSpecified ? _location.Value : pristine.Location;
        }

        if (!_state.Equals(previous._state))
        {
            window.State = _state.IsSpecified ? _state.Value : pristine.State;
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

        if (!_minimumClientSize.Equals(previous._minimumClientSize))
        {
            window.MinimumClientSize = _minimumClientSize.IsSpecified
                ? _minimumClientSize.Value
                : pristine.MinimumClientSize;
        }

        if (!_maximumClientSize.Equals(previous._maximumClientSize))
        {
            window.MaximumClientSize = _maximumClientSize.IsSpecified
                ? _maximumClientSize.Value
                : pristine.MaximumClientSize;
        }
    }
}
