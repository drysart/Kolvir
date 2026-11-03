using SkiaSharp;

namespace XPlatUI.Backend.Gtk4;

/// <summary>
/// The GTK4 half of <see cref="IXPlatPopupWindow"/>: a <see cref="Gtk.Popover"/> anchored to the
/// owning window's drawing area, drawing an <see cref="XPlatUiHost"/> of its own through a
/// <see cref="Gtk4SkiaView"/>.
/// </summary>
/// <remarks>
/// <para>
/// GTK4 removed <c>gtk_window_set_position</c>, so a top level window cannot be placed at an
/// arbitrary point on screen and the portable layer's popup coordinates would be meaningless on a
/// <see cref="Gtk.Window"/>. A popover is the primitive GTK4 has for this instead: it is its own
/// popup surface, positioned relative to a widget, and the compositor lets it extend past the edges
/// of the window it belongs to - which is exactly the behaviour a drop-down needs.
/// </para>
/// <para>
/// A popover is placed next to a rectangle in its parent's coordinate space and centred on it, so
/// <see cref="Place"/> points it at a zero-width rectangle at the popup's top edge and then offsets
/// it by half its own width. That puts the top left corner where the portable layer asked for,
/// except where GTK pulls the popover back inside the monitor.
/// </para>
/// <para>
/// The popup's drawing area is not focusable, so the control that opened the popup keeps the
/// keyboard: the popup is a surface to look at and click, and everything typed belongs to its owner.
/// </para>
/// </remarks>
internal sealed class Gtk4PopupWindow : IXPlatPopupWindow
{
    private readonly XPlatUiHost _host = new();
    private readonly Gtk4SkiaView _ownerView;
    private readonly Gtk4SkiaView _view;
    private readonly Gtk.Popover _popover;
    private readonly bool _dismissOnOutsideClick;

    private SKPoint _location;
    private SKSize _size;
    private bool _dismissing;
    private bool _disposed;

    internal Gtk4PopupWindow(Gtk4SkiaView ownerView, bool dismissOnOutsideClick)
    {
        ArgumentNullException.ThrowIfNull(ownerView);

        _ownerView = ownerView;
        _dismissOnOutsideClick = dismissOnOutsideClick;

        _view = new Gtk4SkiaView(_host);

        // Keys belong to the owner, so the popup never takes the keyboard.
        _view.InputArea.SetFocusable(false);
        _view.InputArea.SetCanFocus(false);

        _popover = Gtk.Popover.New();
        _popover.SetParent(ownerView.Widget);
        _popover.SetChild(_view.Widget);
        _popover.SetHasArrow(false);

        // GTK's own auto-hide is the outside-click dismissal, and it also pops the surface down
        // when the window loses the pointer grab.
        _popover.Autohide = dismissOnOutsideClick;
        _popover.Position = Gtk.PositionType.Bottom;
        _popover.OnClosed += OnPopoverClosed;
    }

    /// <inheritdoc />
    public XPlatUiHost Host => _host;

    /// <inheritdoc />
    public bool DismissesOnOutsideClick => _dismissOnOutsideClick;

    /// <inheritdoc />
    public event Action<object>? Dismissed;

    /// <inheritdoc />
    public void Show(XPlatUiHost ownerHost, SKPoint location, SKSize size)
    {
        ArgumentNullException.ThrowIfNull(ownerHost);
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (_popover.GetVisible())
        {
            UpdateBounds(location, size);
            return;
        }

        // The popup's tree resolves services from the same container as the window it belongs to,
        // so a control inside a popup that opens a popup of its own finds the service too.
        _host.Services = ownerHost.Services;
        _host.SetClientSize(size.Width, size.Height);
        _view.Widget.SetSizeRequest(ToPixels(size.Width), ToPixels(size.Height));

        Place(location, size);
        _popover.Popup();
    }

    /// <inheritdoc />
    public void UpdateBounds(SKPoint location, SKSize size)
    {
        if (_disposed || !_popover.GetVisible())
        {
            return;
        }

        _host.SetClientSize(size.Width, size.Height);
        _view.Widget.SetSizeRequest(ToPixels(size.Width), ToPixels(size.Height));
        Place(location, size);
    }

    /// <inheritdoc />
    public void Hide()
    {
        if (_disposed || !_popover.GetVisible())
        {
            return;
        }

        // Popping down raises Closed, which would otherwise look like the user dismissing the
        // popup and re-enter the portable layer's close path.
        _dismissing = true;

        try
        {
            _popover.Popdown();
        }
        finally
        {
            _dismissing = false;
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _popover.OnClosed -= OnPopoverClosed;

        _dismissing = true;

        try
        {
            if (_popover.GetVisible())
            {
                _popover.Popdown();
            }
        }
        catch (Exception)
        {
            // The window can already be gone during shutdown, and a popover with no parent surface
            // has nothing left to pop down.
        }
        finally
        {
            _dismissing = false;
        }

        _host.Root = null;
        _popover.Unparent();
        _view.Dispose();
    }

    private void Place(SKPoint location, SKSize size)
    {
        _location = location;
        _size = size;

        int x = ToPixels(location.X);
        int y = ToPixels(location.Y);
        int width = ToPixels(size.Width);

        // A popover is centred on the rectangle it points at, so pointing at the popup's own top
        // edge and shifting right by half the width puts its top left corner on the requested spot.
        _popover.SetPointingTo(new Gdk.Rectangle { X = x, Y = y, Width = 1, Height = 1 });
        _popover.SetOffset(width / 2, 0);
    }

    private void OnPopoverClosed(Gtk.Popover sender, EventArgs args)
    {
        if (_dismissing || _disposed)
        {
            return;
        }

        Dismissed?.Invoke(this);
    }

    /// <summary>
    /// GTK positions widgets in logical pixels, which is the same unit the portable layer uses, so
    /// a coordinate only has to be rounded.
    /// </summary>
    private static int ToPixels(float logical) => Math.Max(0, (int)MathF.Round(logical));
}
