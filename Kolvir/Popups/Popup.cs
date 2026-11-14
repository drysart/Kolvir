using SkiaSharp;

namespace Kolvir;

/// <summary>How a popup is presented.</summary>
public enum PopupPresentation
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
public sealed class Popup : IDisposable
{
    private readonly Control? _owner;
    private readonly UiHost? _host;
    private Action<object>? _closed;
    private IPopupWindow? _window;
    private PopupDismissCatcher? _catcher;
    private Control? _shownContent;
    private ScaledPopupHost? _wrapper;
    private SKPoint _location;
    private float _scaleFactor = 1f;
    private bool _open;
    private bool _disposed;

    /// <summary>
    /// Creates a popup anchored to <paramref name="owner"/>, which supplies the window it is shown
    /// in and the service provider a platform popup window is resolved from.
    /// </summary>
    /// <param name="owner">
    /// The control the popup belongs to. It supplies the host the popup is anchored to and the
    /// service provider a platform popup window is resolved from.
    /// </param>
    public Popup(Control owner)
    {
        ArgumentNullException.ThrowIfNull(owner);
        _owner = owner;
    }

    /// <summary>
    /// Creates a popup anchored to a window rather than to a control, which is what a menu opened
    /// from the menu bar or the keyboard needs.
    /// </summary>
    /// <param name="host">
    /// The window the popup is anchored to, for a popup that belongs to the window rather than to a
    /// control - which is what a menu opened from the menu bar or the keyboard is.
    /// </param>
    public Popup(UiHost host)
    {
        ArgumentNullException.ThrowIfNull(host);
        _host = host;
    }

    /// <summary>The control the popup belongs to, or null when it belongs to the window.</summary>
    public Control? Owner => _owner;

    /// <summary>The window the popup is anchored to.</summary>
    public UiHost Host => _owner?.Host ?? _host!;

    /// <summary>
    /// The control drawn inside the popup. It must not already be in a control tree, because the
    /// popup hosts it either in a window's host or in the overlay layer.
    /// </summary>
    public Control? Content { get; set; }

    /// <summary>How the popup should be presented. Defaults to <see cref="PopupPresentation.Auto"/>.</summary>
    public PopupPresentation Presentation { get; set; } = PopupPresentation.Auto;

    /// <summary>The presentation actually in use, which is only meaningful while the popup is open.</summary>
    public PopupPresentation ActualPresentation { get; private set; } = PopupPresentation.Overlay;

    /// <summary>
    /// The popup's size in logical pixels, in the content's own coordinate space. While null the
    /// content is measured for each show, which is what a tooltip wants; a drop-down list assigns
    /// it so it matches its owner's width. The window or overlay the popup occupies is this size
    /// multiplied by <see cref="ScaleFactor"/>.
    /// </summary>
    public SKSize? Size { get; set; }

    /// <summary>Largest size the popup may take when it measures its content.</summary>
    /// <remarks>
    /// It is a content-space limit: a popup zoomed by 2.0 occupies twice this on screen. A caller
    /// that wants the popup to stay inside a host-space rectangle divides that rectangle by
    /// <see cref="ScaleFactor"/> before assigning it.
    /// </remarks>
    public SKSize MaximumSize { get; set; } = new(float.PositiveInfinity, float.PositiveInfinity);

    /// <summary>
    /// The multiplier the popup's content is painted, hit tested and measured at. Defaults to 1.
    /// </summary>
    /// <remarks>
    /// A control anchored inside a zoomed panel sets this to its
    /// <see cref="Control.EffectiveScaleMultiplier"/>, so the drop-down or menu reads as part
    /// of the zoomed subtree rather than as half-size chrome beside it. The content is hosted in
    /// a zoom wrapper, which means painting and hit testing come from the same container-local
    /// mechanism the panel uses; only the popup's outer size and placement are multiplied here.
    /// The value must be finite and greater than zero, and is read when the popup is shown.
    /// </remarks>
    /// <exception cref="ArgumentOutOfRangeException">
    /// The value is not finite, or is zero or negative.
    /// </exception>
    public float ScaleFactor
    {
        get => _scaleFactor;
        set
        {
            if (float.IsNaN(value) || float.IsInfinity(value) || value <= 0f)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(value),
                    value,
                    "The scale factor must be a finite, positive number.");
            }

            _scaleFactor = value;
        }
    }

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

    /// <summary>
    /// The catcher this popup installed in the owning window to see outside presses, or null when
    /// it installed none. A menu bar uses it to tell its own menu's catcher from another popup's.
    /// </summary>
    internal Control? DismissCatcher => _catcher;

    /// <summary>Where the popup's top left corner sits, in the owning host's coordinates.</summary>
    public SKPoint Location => _location;

    /// <summary>Raised after the popup closed, whether by <see cref="Close"/> or by the platform.</summary>
    public IDisposable AddClosedHandler(Action<object> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        _closed += handler;
        return new HandlerToken<Action<object>>(RemoveClosedHandler, handler);
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

        // The content is hosted in a zoom wrapper rather than the wrapper being taught to the
        // backend: painting and hit testing inside the popup then come from the same container
        // mechanism a zoomed panel uses, and the platform still sees one ordinary control tree.
        var shown = content;
        if (_scaleFactor != 1f)
        {
            shown = _wrapper = new ScaledPopupHost(content, _scaleFactor);
        }

        try
        {
            if (Presentation != PopupPresentation.Overlay && TryShowInWindow(host, shown, location))
            {
                return;
            }

            ShowInOverlay(host, shown, location);
        }
        catch
        {
            // A show that failed must not leave the caller's content parented to a wrapper nobody
            // owns, or every later attempt would see it as already in a tree.
            DetachWrapper();
            throw;
        }
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

        DetachWrapper();

        _closed?.Invoke(this);
    }

    /// <summary>Takes the caller's content back out of the zoom wrapper, if one was made.</summary>
    private void DetachWrapper()
    {
        if (_wrapper is not { } wrapper)
        {
            return;
        }

        _wrapper = null;

        if (Content is { } original && ReferenceEquals(original.Parent, wrapper))
        {
            wrapper.Controls.Remove(original);
        }

        wrapper.Dispose();
    }

    /// <summary>
    /// Closes the popup if it is open, drops its closed handlers and marks it disposed. The content
    /// is the caller's and is left in place.
    /// </summary>
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

    private bool TryShowInWindow(UiHost host, Control content, SKPoint location)
    {
        var service = host.GetService<IPopupService>() ?? PopupService.Current;
        var window = service?.CreatePopup(host, DismissOnOutsideClick, TakesPresses ?? DismissOnOutsideClick);

        if (window is null)
        {
            return false;
        }

        _window = window;
        ActualPresentation = PopupPresentation.Window;

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
            _catcher = new PopupDismissCatcher(this);
            host.AddOverlay(_catcher);
        }

        return true;
    }

    private void ShowInOverlay(UiHost host, Control content, SKPoint location)
    {
        ActualPresentation = PopupPresentation.Overlay;
        _shownContent = content;
        _open = true;

        // The catcher covers the whole window underneath the popup, so a press anywhere else is
        // delivered to it instead of to the control the user was about to click.
        if (DismissOnOutsideClick)
        {
            _catcher = new PopupDismissCatcher(this);
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
    private void PlaceOverlay(Control content, SKPoint location)
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
        SKSize size;

        if (Size is { } requested)
        {
            size = requested;
        }
        else if (Content is { } content)
        {
            var measured = content.Measure(MaximumSize);
            size = new SKSize(
                MathF.Min(measured.Width, MaximumSize.Width),
                MathF.Min(measured.Height, MaximumSize.Height));
        }
        else
        {
            return SKSize.Empty;
        }

        // Size and MaximumSize are content-space values; the window or overlay the popup occupies
        // is that size scaled, which is also the size the screen-fitting decision must use.
        return _scaleFactor == 1f
            ? size
            : new SKSize(size.Width * _scaleFactor, size.Height * _scaleFactor);
    }

    private void OnWindowDismissed(object sender) => Close();
}

/// <summary>
/// The wrapper an <see cref="Popup"/> puts its content in when the popup belongs to a zoomed
/// subtree: a panel whose children are drawn, hit tested and measured at the popup's multiplier.
/// </summary>
/// <remarks>
/// It is deliberately a container and not a backend transform. Input enters the popup at its root
/// and is divided by the wrapper's <see cref="Panel.ScaleFactor"/> on the way down, painting
/// is scaled on the way out, and the single child is arranged to fill the content space. That is
/// the same mechanism a zoomed panel uses inside a window, so a popup and its owner cannot drift
/// apart in how they treat the pointer.
/// </remarks>
internal sealed class ScaledPopupHost : Panel
{
    private readonly Control _content;

    public ScaledPopupHost(Control content, float scale)
    {
        _content = content;

        BackColor = null;
        Padding = new Thickness(0f);

        Controls.Add(content);
        ScaleFactor = scale;
    }

    /// <summary>Gives the content the whole content space and claims no more than it occupies.</summary>
    protected override SKSize ArrangeChildren(SKRect area)
    {
        _content.Bounds = area;
        return new SKSize(MathF.Max(0f, area.Width), MathF.Max(0f, area.Height));
    }
}

/// <summary>
/// The control that fills the window behind an overlaid popup and closes it when the user presses
/// anywhere outside. It paints nothing, so the window looks untouched.
/// </summary>
internal sealed class PopupDismissCatcher : Control
{
    private readonly Popup _popup;

    public PopupDismissCatcher(Popup popup)
    {
        _popup = popup;
        Cursor = Cursor.Default;
    }

    // The catcher paints nothing, so the pointer still hovers whatever it covers.
    internal override bool PassesHoverThrough => true;

    protected override void OnMouseDown(MouseEventArgs e)
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
