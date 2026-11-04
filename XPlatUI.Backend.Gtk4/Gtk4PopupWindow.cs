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
/// The view is wrapped in a <see cref="Gtk.ScrolledWindow"/> rather than being the popover's child
/// directly. A popover takes its size from the natural size of its child, and a
/// <see cref="Gtk.Picture"/> answers with the pixel size of the frame it is showing - which, on a
/// scaled display, is the size it was last allocated multiplied by the scale factor. Left to feed
/// each other, the two grow the popover until GTK clamps it to the monitor. A scrolled window does
/// not propagate its child's natural size, so the popover is left with the size the portable layer
/// asked for.
/// </para>
/// <para>
/// The popup's drawing area is not focusable, so the control that opened the popup keeps the
/// keyboard: the popup is a surface to look at and click, and everything typed belongs to its owner.
/// </para>
/// <para>
/// A popover takes the seat's keyboard while it is up, though, so the owner's drawing area stops
/// seeing keys even though nothing in the popup can take the focus. A capture-phase key controller
/// on the popover hands each key straight to the owner's host, which is where the portable layer
/// keeps the keyboard: an open menu is an input sink on its owner, and its arrow keys, Escape and
/// mnemonics have to reach it. A key the owner does not take is left to GTK, which is what pops a
/// popover down on Escape.
/// </para>
/// </remarks>
internal sealed class Gtk4PopupWindow : IXPlatPopupWindow
{
    /// <summary>The style class the popup stylesheet below is written against.</summary>
    private const string PopupStyleClass = "xplat-popup";

    /// <summary>
    /// A popover draws its child inside its own padding, which the theme sizes for a shadow and a
    /// rounded corner. The portable layer sizes a popup to exactly the content it contains, so that
    /// padding shows up as a border of dead space around the XPlat controls. The padding and the
    /// chrome are removed for popups only, through a style class of their own.
    /// </summary>
    /// <remarks>
    /// The fill has to be cleared on the popover's <c>contents</c> node as well as on the popover:
    /// that is the node a GTK4 theme paints the popover background on, and a background left in
    /// place there covers the page behind the popup. A menu shows that as an opaque rectangle
    /// around the menu proper, because the portable layer leaves its shadow margin transparent on
    /// purpose and expects to see the window underneath.
    /// </remarks>
    private const string PopupStyleSheet =
        "popover." + PopupStyleClass + ", popover." + PopupStyleClass + ".background" +
        " { padding: 0; margin: 0; border: none; border-radius: 0;" +
        " background: none; background-color: transparent; box-shadow: none; }\n" +
        "popover." + PopupStyleClass + " > contents, popover." + PopupStyleClass + ".background > contents" +
        " { padding: 0; margin: 0; border: none; border-radius: 0;" +
        " background: none; background-color: transparent; box-shadow: none; }\n";

    private static Gdk.Display? _styledDisplay;

    private readonly XPlatUiHost _host = new();
    private readonly Gtk4SkiaView _ownerView;
    private readonly Gtk4SkiaView _view;
    private readonly Gtk.ScrolledWindow _frame;
    private readonly Gtk.Popover _popover;
    private readonly bool _dismissOnOutsideClick;

    private bool _dismissing;
    private bool _disposed;

    internal Gtk4PopupWindow(Gtk4SkiaView ownerView, bool dismissOnOutsideClick)
    {
        ArgumentNullException.ThrowIfNull(ownerView);

        _ownerView = ownerView;
        _dismissOnOutsideClick = dismissOnOutsideClick;

        _view = new Gtk4SkiaView(_host);
        _view.DebugTag = "popup";

        // Keys belong to the owner, so the popup never takes the keyboard.
        _view.InputArea.SetFocusable(false);
        _view.InputArea.SetCanFocus(false);

        // A popover sizes itself to the natural size of its child, and a container that measures its
        // children answers with the view's size - which follows the pixel size of the frame the view
        // last painted, so on a scaled display it is the logical size multiplied by the scale
        // factor. A popover fed that size grows to fill the monitor. A scrolled window does not
        // propagate its child's natural size and, with external scrollbars, shows nothing of its
        // own, so the frame's only size is the one it is asked for.
        _frame = Gtk.ScrolledWindow.New();
        _frame.SetPolicy(Gtk.PolicyType.External, Gtk.PolicyType.External);
        _frame.SetPropagateNaturalWidth(false);
        _frame.SetPropagateNaturalHeight(false);
        _frame.SetChild(_view.Widget);
        _frame.SetSizeRequest(1, 1);

        _popover = Gtk.Popover.New();
        _popover.SetParent(ownerView.Widget);
        _popover.SetChild(_frame);
        _popover.SetHasArrow(false);
        _popover.AddCssClass(PopupStyleClass);
        EnsurePopupStyle(ownerView.Widget.GetDisplay());

        // GTK's own auto-hide is the outside-click dismissal, and it also pops the surface down
        // when the window loses the pointer grab.
        _popover.Autohide = dismissOnOutsideClick;
        _popover.Position = Gtk.PositionType.Bottom;
        _popover.OnClosed += OnPopoverClosed;

        // The popover holds the seat's keyboard, and nothing inside it can be focused, so the keys
        // are captured at the popover itself and forwarded to the host that owns the popup.
        var key = Gtk.EventControllerKey.New();
        key.SetPropagationPhase(Gtk.PropagationPhase.Capture);
        key.OnKeyPressed += OnKeyPressed;
        key.OnKeyReleased += OnKeyReleased;
        _popover.AddController(key);
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

        Apply(location, size);

        _popover.Popup();
    }

    /// <inheritdoc />
    public void UpdateBounds(SKPoint location, SKSize size)
    {
        if (_disposed || !_popover.GetVisible())
        {
            return;
        }

        Apply(location, size);
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

    private static void EnsurePopupStyle(Gdk.Display? display)
    {
        if (display is null || ReferenceEquals(display, _styledDisplay))
        {
            return;
        }

        _styledDisplay = display;

        var provider = Gtk.CssProvider.New();
        provider.LoadFromString(PopupStyleSheet);

        // 600 is GTK_STYLE_PROVIDER_PRIORITY_APPLICATION: above the theme, below the user's own
        // settings, which is the priority a library's own styling belongs at.
        Gtk.StyleContext.AddProviderForDisplay(display, provider, 600);
    }

    /// <summary>
    /// Applies a new position and size to the popup as a single change.
    /// </summary>
    /// <remarks>
    /// GTK applies popover geometry in its next layout pass, and a resize on its own makes the
    /// pass re-allocate the popover against the pointing rectangle it still has - clamped to the
    /// monitor - before the new position lands, so a popup that grows briefly shows the new size at
    /// the old place. Setting the pointing rectangle and offset first, then asking for the new
    /// size, leaves the pass nothing to see but the final geometry.
    /// </remarks>
    private void Apply(SKPoint location, SKSize size)
    {
        int width = ToPixels(size.Width);
        int height = ToPixels(size.Height);

        _host.SetClientSize(width, height);

        // Paint for the new size now: the picture fills its allocation, so without a frame at the
        // new size the pass that applies it stretches the previous one across the new bounds.
        _view.PaintNow(width, height);

        Place(location, size);

        _view.Widget.SetSizeRequest(width, height);
        _frame.SetSizeRequest(width, height);
        _frame.QueueResize();
    }

    private void Place(SKPoint location, SKSize size)
    {
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
    /// Forwards a key press to the host that owns the popup; returns true when the UI consumed it.
    /// </summary>
    private bool OnKeyPressed(Gtk.EventControllerKey sender, Gtk.EventControllerKey.KeyPressedSignalArgs args)
    {
        var ownerHost = _ownerView.Host;
        var modifiers = ModifiersOf(args.Keyval, args.State);
        var key = Gtk4KeyMap.Map(args.Keyval);

        if (key != XPlatKey.None && ownerHost.ProcessKeyDown(key, modifiers))
        {
            return true;
        }

        var text = Gtk4KeyMap.MapText(args.Keyval, args.State);
        if (text is null)
        {
            return false;
        }

        ownerHost.ProcessTextInput(text);
        return true;
    }

    private void OnKeyReleased(Gtk.EventControllerKey sender, Gtk.EventControllerKey.KeyReleasedSignalArgs args)
    {
        var key = Gtk4KeyMap.Map(args.Keyval);
        if (key != XPlatKey.None)
        {
            _ownerView.Host.ProcessKeyUp(key, ModifiersOf(args.Keyval, args.State));
        }
    }

    /// <summary>
    /// Reads the modifiers of a key event. GTK reports shift+Tab as <c>ISO_Left_Tab</c>, a key value
    /// of its own, so the shift bit is not the only place the modifier comes from.
    /// </summary>
    private static XPlatModifiers ModifiersOf(uint keyval, Gdk.ModifierType state)
    {
        var modifiers = Gtk4KeyMap.MapModifiers(state);
        return Gtk4KeyMap.ImpliesShift(keyval) ? modifiers | XPlatModifiers.Shift : modifiers;
    }

    /// <summary>
    /// GTK positions widgets in logical pixels, which is the same unit the portable layer uses, so
    /// a coordinate only has to be rounded.
    /// </summary>
    private static int ToPixels(float logical) => Math.Max(0, (int)MathF.Round(logical));
}
