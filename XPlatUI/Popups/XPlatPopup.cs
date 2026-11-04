using SkiaSharp;

namespace XPlatUI;

/// <summary>How a popup is presented.</summary>
public enum XPlatPopupPresentation
{
    /// <summary>
    /// A window of its own when the platform layer provides one, otherwise painted inside the
    /// owning window.
    /// </summary>
    Auto,

    /// <summary>A window of its own, so it can extend past the edges of the owning window.</summary>
    Window,

    /// <summary>Painted inside the owning window, clipped to it. What the fallback uses.</summary>
    Overlay,
}

/// <summary>
/// A transient surface that shows a control on top of everything else: a drop-down list, a tooltip,
/// a suggestion list. It is dismissed when the user presses the pointer outside it or the platform
/// reports the popup lost the user's attention.
/// </summary>
/// <remarks>
/// The popup asks the platform layer for a window when one is available, so it can hang past the
/// edges of the owning window and overlap other windows. When no platform layer is registered -
/// or when the popup is explicitly asked to stay inside the window - it is painted in the owning
/// window's overlay layer instead, which costs nothing in a build that never opens one.
/// </remarks>
public sealed class XPlatPopup : IDisposable
{
    private readonly XPlatControl? _owner;
    private readonly XPlatUiHost? _host;
    private Action<object>? _closed;
    private IXPlatPopupWindow? _window;
    private XPlatPopupDismissCatcher? _catcher;
    private XPlatControl? _shownContent;
    private SKPoint _location;
    private bool _open;
    private bool _disposed;

    /// <param name="owner">
    /// The control the popup belongs to. It supplies the host the popup is anchored to and the
    /// service provider a platform popup window is resolved from.
    /// </param>
    public XPlatPopup(XPlatControl owner)
    {
        ArgumentNullException.ThrowIfNull(owner);
        _owner = owner;
    }

    /// <param name="host">
    /// The window the popup is anchored to, for a popup that belongs to the window rather than to a
    /// control - which is what a menu opened from the menu bar or the keyboard is.
    /// </param>
    public XPlatPopup(XPlatUiHost host)
    {
        ArgumentNullException.ThrowIfNull(host);
        _host = host;
    }

    /// <summary>The control the popup belongs to, or null when it belongs to the window.</summary>
    public XPlatControl? Owner => _owner;

    /// <summary>The window the popup is anchored to.</summary>
    public XPlatUiHost Host => _owner?.Host ?? _host!;

    /// <summary>
    /// The control drawn inside the popup. It must not already be in a control tree, because the
    /// popup hosts it either in a window's host or in the overlay layer.
    /// </summary>
    public XPlatControl? Content { get; set; }

    /// <summary>How the popup should be presented. Defaults to <see cref="XPlatPopupPresentation.Auto"/>.</summary>
    public XPlatPopupPresentation Presentation { get; set; } = XPlatPopupPresentation.Auto;

    /// <summary>The presentation actually in use, which is only meaningful while the popup is open.</summary>
    public XPlatPopupPresentation ActualPresentation { get; private set; } = XPlatPopupPresentation.Overlay;

    /// <summary>
    /// The popup's size in logical pixels. While null the content is measured for each show, which
    /// is what a tooltip wants; a drop-down list assigns it so it matches its owner's width.
    /// </summary>
    public SKSize? Size { get; set; }

    /// <summary>Largest size the popup may take when it measures its content.</summary>
    public SKSize MaximumSize { get; set; } = new(float.PositiveInfinity, float.PositiveInfinity);

    /// <summary>
    /// Whether a press outside the popup closes it. True is what a menu or a drop-down list wants,
    /// because the press belongs to the popup rather than to whatever it landed on. A tooltip sets
    /// it to false so the press reaches the control underneath instead.
    /// </summary>
    public bool DismissOnOutsideClick { get; set; } = true;

    /// <summary>
    /// Whether the popup takes the pointer, as opposed to passing it through to whatever it covers.
    /// Null, the default, follows <see cref="DismissOnOutsideClick"/>: a popup that dismisses itself
    /// on an outside press takes that press, and a tooltip that does not dismiss takes nothing.
    /// </summary>
    /// <remarks>
    /// A submenu needs these apart. The chain is dismissed by the menu the user started from, so a
    /// submenu does not dismiss on its own - but it is still a menu the user clicks in, so it has to
    /// take the pointer.
    /// </remarks>
    public bool? TakesPresses { get; set; }

    /// <summary>Whether the popup is currently shown.</summary>
    public bool IsOpen => _open;

    /// <summary>Where the popup's top left corner sits, in the owning host's coordinates.</summary>
    public SKPoint Location => _location;

    /// <summary>Raised after the popup closed, whether by <see cref="Close"/> or by the platform.</summary>
    public IDisposable AddClosedHandler(Action<object> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        _closed += handler;
        return new XPlatHandlerToken<Action<object>>(RemoveClosedHandler, handler);
    }

    /// <summary>Removes a handler that <see cref="AddClosedHandler"/> added.</summary>
    public void RemoveClosedHandler(Action<object> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        _closed -= handler;
    }

    /// <summary>
    /// Shows the popup with its top left corner at <paramref name="location"/>, in logical pixels
    /// in the owning host's coordinate space.
    /// </summary>
    public void Show(SKPoint location)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (_open)
        {
            MoveTo(location);
            return;
        }

        var content = Content ?? throw new InvalidOperationException("A popup needs content before it can be shown.");

        if (content.Parent is not null)
        {
            throw new InvalidOperationException("The popup's content is already in a control tree.");
        }

        var host = Host ?? throw new InvalidOperationException("The popup is not attached to a window.");

        _location = location;

        if (Presentation != XPlatPopupPresentation.Overlay && TryShowInWindow(host, content, location))
        {
            return;
        }

        ShowInOverlay(host, content, location);
    }

    /// <summary>
    /// Moves an open popup so that its top left corner sits at <paramref name="location"/>, which
    /// is what a drop-down list does when its owner scrolls or resizes.
    /// </summary>
    /// <remarks>
    /// The popup is re-measured rather than only moved: content is free to change size while it is
    /// open, and a submenu opening is exactly that. A window popup passes the new size to its
    /// window; an overlaid one has to resize the control it hangs in the host, or the growth is
    /// clipped away at the old edge.
    /// </remarks>
    public void MoveTo(SKPoint location)
    {
        if (!_open)
        {
            return;
        }

        _location = location;

        if (_window is not null)
        {
            _window.UpdateBounds(location, MeasureContent());
            return;
        }

        if (_shownContent is { } content)
        {
            PlaceOverlay(content, location);
        }
    }

    /// <summary>Closes the popup, if it is open.</summary>
    public void Close()
    {
        if (!_open)
        {
            return;
        }

        _open = false;

        if (_catcher is { } catcher)
        {
            _catcher = null;
            (catcher.Host ?? _host)?.RemoveOverlay(catcher);
        }

        if (_window is { } window)
        {
            _window = null;
            window.Dismissed -= OnWindowDismissed;
            window.Host.Root = null;
            window.Hide();
            window.Dispose();
        }

        if (_shownContent is { } content)
        {
            _shownContent = null;
            content.Host?.RemoveOverlay(content);
        }

        _closed?.Invoke(this);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        Close();
        _disposed = true;
        _closed = null;
    }

    private bool TryShowInWindow(XPlatUiHost host, XPlatControl content, SKPoint location)
    {
        var service = host.GetService<IXPlatPopupService>() ?? XPlatPopupService.Current;
        var window = service?.CreatePopup(host, DismissOnOutsideClick, TakesPresses ?? DismissOnOutsideClick);

        if (window is null)
        {
            return false;
        }

        _window = window;
        ActualPresentation = XPlatPopupPresentation.Window;

        // A popup window is composited per pixel on every platform, so its surface starts out
        // empty and only what the content paints shows. Clearing it to the theme background
        // instead would fill the margin a menu keeps for its drop shadow, which turns the whole
        // popup - and the union of a menu and its submenu - into an opaque rectangle.
        window.Host.ClearColor = SKColors.Transparent;
        window.Host.Root = content;
        window.Dismissed += OnWindowDismissed;
        _open = true;

        try
        {
            window.Show(host, location, MeasureContent());
        }
        catch
        {
            _open = false;
            _window = null;
            window.Dismissed -= OnWindowDismissed;
            window.Host.Root = null;
            window.Dispose();
            throw;
        }

        // A press inside the window the popup belongs to is outside the popup, and a press outside
        // the popup belongs to the popup: it closes it and stops there. A window popup covers that
        // for the press that lands on the window itself, whichever platform draws the window.
        if (DismissOnOutsideClick)
        {
            _catcher = new XPlatPopupDismissCatcher(this);
            host.AddOverlay(_catcher);
        }

        return true;
    }

    private void ShowInOverlay(XPlatUiHost host, XPlatControl content, SKPoint location)
    {
        ActualPresentation = XPlatPopupPresentation.Overlay;
        _shownContent = content;
        _open = true;

        // The catcher covers the whole window underneath the popup, so a press anywhere else is
        // delivered to it instead of to the control the user was about to click.
        if (DismissOnOutsideClick)
        {
            _catcher = new XPlatPopupDismissCatcher(this);
            host.AddOverlay(_catcher);
        }

        host.AddOverlay(content);
        PlaceOverlay(content, location);
    }

    /// <summary>
    /// Puts overlaid content at <paramref name="location" />, sized to what it measures now. Called
    /// again whenever the content changes size while it is open, so the growth is never clipped at
    /// the old edge.
    /// </summary>
    private void PlaceOverlay(XPlatControl content, SKPoint location)
    {
        var size = MeasureContent();
        content.Bounds = new SKRect(location.X, location.Y, location.X + size.Width, location.Y + size.Height);
        content.PerformLayout();
        SyncCatcherBounds();
    }

    private void SyncCatcherBounds()
    {
        if (_catcher is { } catcher && catcher.Host is { } host)
        {
            catcher.Bounds = new SKRect(0f, 0f, host.ClientSize.Width, host.ClientSize.Height);
        }
    }

    private SKSize MeasureContent()
    {
        if (Size is { } size)
        {
            return size;
        }

        if (Content is not { } content)
        {
            return SKSize.Empty;
        }

        var measured = content.Measure(MaximumSize);
        return new SKSize(
            MathF.Min(measured.Width, MaximumSize.Width),
            MathF.Min(measured.Height, MaximumSize.Height));
    }

    private void OnWindowDismissed(object sender) => Close();
}

/// <summary>
/// The control that fills the window behind an overlaid popup and closes it when the user presses
/// anywhere outside. It paints nothing, so the window looks untouched.
/// </summary>
internal sealed class XPlatPopupDismissCatcher : XPlatControl
{
    private readonly XPlatPopup _popup;

    public XPlatPopupDismissCatcher(XPlatPopup popup)
    {
        _popup = popup;
        Cursor = XPlatCursor.Default;
    }

    protected override void OnMouseDown(XPlatMouseEventArgs e)
    {
        base.OnMouseDown(e);
        _popup.Close();
    }

    protected override void OnLayout()
    {
        base.OnLayout();

        if (Host is { } host)
        {
            var target = new SKRect(0f, 0f, host.ClientSize.Width, host.ClientSize.Height);

            if (!Bounds.Equals(target))
            {
                Bounds = target;
            }
        }
    }
}
