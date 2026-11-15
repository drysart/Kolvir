using AppKit;
using CoreGraphics;
using Foundation;
using SkiaSharp;

namespace Kolvir.Backend.AppKit;

/// <summary>
/// The AppKit half of <see cref="IPopupWindow"/>: a borderless child <see cref="NSWindow"/>
/// that draws an <see cref="UiHost"/> of its own through an <see cref="AppKitSkiaView"/>.
/// </summary>
/// <remarks>
/// <para>
/// The window is a child of the owning window, which is AppKit's ownership model: a child window is
/// ordered above its parent, is ordered out with it, and is released with it. That is what lets a
/// drop-down hang past the edge of the window it belongs to, over other applications.
/// </para>
/// <para>
/// It never becomes key or main, so the control that opened the popup keeps the keyboard: the popup
/// is a surface to look at and click, and everything typed belongs to its owner.
/// </para>
/// </remarks>
internal sealed class AppKitPopupWindow : IPopupWindow
{
    /// <summary>Every popup currently on screen, so a press can be recognised as being on one.</summary>
    private static readonly List<AppKitPopupWindow> Live = new();

    private readonly UiHost _host = new();
    private readonly bool _dismissOnOutsideClick;
    private readonly bool _takesPresses;

    private PopupWindow? _window;
    private AppKitSkiaView? _view;
    private NSWindow? _ownerWindow;
    private NSObject? _clickMonitor;
    private NSObject? _resignObserver;
    private bool _dismissing;
    private bool _disposed;

    internal AppKitPopupWindow(bool dismissOnOutsideClick, bool takesPresses)
    {
        _dismissOnOutsideClick = dismissOnOutsideClick;
        _takesPresses = takesPresses;
    }

    /// <inheritdoc />
    public UiHost Host => _host;

    /// <inheritdoc />
    public bool DismissesOnOutsideClick => _dismissOnOutsideClick;

    /// <inheritdoc />
    public bool TakesPresses => _takesPresses;

    /// <inheritdoc />
    public event Action<object>? Dismissed;

    /// <inheritdoc />
    public void Show(UiHost ownerHost, SKPoint location, SKSize size)
    {
        ArgumentNullException.ThrowIfNull(ownerHost);
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (_window is not null)
        {
            UpdateBounds(location, size);
            return;
        }

        if (ownerHost.Window?.Backend is not AppKitWindowBackend { Window: { } owner })
        {
            return;
        }

        // The popup's tree resolves services from the same container as the window it belongs to,
        // so a control inside a popup that opens a popup of its own finds the service too.
        _host.Services = ownerHost.Services;
        _host.SetClientSize(size.Width, size.Height);

        var window = new PopupWindow(
            new CGRect(0, 0, size.Width, size.Height),
            NSWindowStyle.Borderless,
            NSBackingStore.Buffered,
            deferCreation: false)
        {
            IsOpaque = false,
            BackgroundColor = NSColor.Clear,

            // A window shadow is drawn from the window's own alpha shape, which on a window this
            // transparent is a hard black outline sitting outside whatever the popup drew - the
            // menu's own drop shadow, and beyond it the page. A menu draws the shadow it wants
            // itself, in the transparent margin it reserves around its surface, and every other
            // popup is flush: a layered window on Win32 and a popover on GTK have no window shadow
            // either, so leaving it off here is also what makes the three look the same.
            HasShadow = false,

            // A tooltip must not swallow the press that was meant for the control underneath it,
            // which is what the Win32 side gets from WS_EX_TRANSPARENT. A submenu is not a tooltip:
            // it is a menu of its own and has to be clickable, even though it is the menu it hangs
            // off that dismisses the chain.
            IgnoresMouseEvents = !_takesPresses,
        };

        // The popup never becomes key, so its tracking area has to be active regardless of which
        // window holds the keyboard or the pointer never reaches the controls inside it.
        window.AcceptsMouseMovedEvents = true;

        // The managed side owns the window, so AppKit must not release it when it closes.
        window.ReleaseWhenClosed(false);
        window.Level = NSWindowLevel.Floating;

        var view = new AppKitSkiaView(_host)
        {
            AutoresizingMask = NSViewResizingMask.WidthSizable | NSViewResizingMask.HeightSizable,
            TracksPointerInBackgroundWindow = true,
        };

        window.ContentView = view;

        _window = window;
        _view = view;
        _ownerWindow = owner;

        Live.Add(this);

        owner.AddChildWindow(window, NSWindowOrderingMode.Above);

        Place(location, size);

        window.OrderFront(null!);
        view.NeedsDisplay = true;

        InstallObservers();
    }

    /// <inheritdoc />
    public void UpdateBounds(SKPoint location, SKSize size)
    {
        if (_window is null || _disposed)
        {
            return;
        }

        _host.SetClientSize(size.Width, size.Height);
        Place(location, size);

        // A layer backed view is not marked dirty by a resize, so the new size has to ask for a
        // frame or the popup keeps showing the frame from before it changed size.
        _view!.NeedsDisplay = true;
    }

    /// <inheritdoc />
    public void Hide()
    {
        _window?.OrderOut(null!);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        Live.Remove(this);

        RemoveObservers();

        var window = _window;
        var view = _view;

        _window = null;
        _view = null;
        _ownerWindow = null;

        if (window is not null)
        {
            window.OrderOut(null!);
            window.ParentWindow?.RemoveChildWindow(window);
            window.Dispose();
        }

        view?.Dispose();

        _host.Root = null;

        Dismissed = null;
    }

    /// <summary>
    /// Positions the window so the popup's top left corner sits at <paramref name="location"/> in
    /// the owner's content view, clamped so it stays inside the screen's visible area.
    /// </summary>
    /// <remarks>
    /// The portable layer works in the owner's client space: logical pixels with y growing down
    /// from the top left of the window. AppKit works in screen space: points with y growing up from
    /// the bottom left of the primary display. <see cref="AppKitApplicationBackend.LocationOf"/> and
    /// <see cref="AppKitApplicationBackend.FrameFor"/> are the two conversions the rest of this
    /// backend already uses, so they are used here too rather than repeating the arithmetic - and
    /// the clamp is done in logical space, where the top and bottom of a rectangle are unambiguous.
    /// <see cref="CGRect.Top"/> and <see cref="CGRect.Bottom"/> are the minimum and maximum y, not
    /// the geometric top and bottom, so using them as edges in AppKit's y-up space pins the popup
    /// to the top of the screen.
    /// </remarks>
    private void Place(SKPoint location, SKSize size)
    {
        var window = _window!;
        var owner = _ownerWindow!;

        var client = AppKitApplicationBackend.ClientOriginOf(owner);
        float x = client.X + location.X;
        float y = client.Y + location.Y;

        if (AppKitApplicationBackend.ScreenForWindow(owner) is { } screen)
        {
            var work = screen.WorkingArea;
            x = Math.Clamp(x, work.Left, MathF.Max(work.Left, work.Right - size.Width));
            y = Math.Clamp(y, work.Top, MathF.Max(work.Top, work.Bottom - size.Height));
        }

        window.SetFrame(
            AppKitApplicationBackend.FrameFor(
                new SKPoint(x, y),
                size,
                AppKitApplicationBackend.PrimaryHeightFor(owner)),
            display: true);
    }

    private void InstallObservers()
    {
        if (_dismissOnOutsideClick && _clickMonitor is null)
        {
            // A local monitor sees every press the application dispatches, before it reaches the
            // window it landed on, which is the only way a non-activating popup can tell that the
            // press was meant for something else.
            _clickMonitor = NSEvent.AddLocalMonitorForEventsMatchingMask(
                NSEventMask.LeftMouseDown | NSEventMask.RightMouseDown | NSEventMask.OtherMouseDown,
                OnLocalMouseDown);
        }

        _resignObserver ??= NSNotificationCenter.DefaultCenter.AddObserver(
            NSApplication.DidResignActiveNotification,
            _ => Dismiss());
    }

    private void RemoveObservers()
    {
        if (_clickMonitor is { } monitor)
        {
            _clickMonitor = null;
            NSEvent.RemoveMonitor(monitor);
        }

        if (_resignObserver is { } observer)
        {
            _resignObserver = null;
            NSNotificationCenter.DefaultCenter.RemoveObserver(observer);
            observer.Dispose();
        }
    }

    private NSEvent OnLocalMouseDown(NSEvent theEvent)
    {
        if (_window is null || _disposed)
        {
            return theEvent;
        }

        // A press inside any popup of ours belongs to that popup. A submenu is a window of its own,
        // so without this a press in a submenu would look like a press outside the menu it hangs
        // off and close the whole chain before the submenu saw it.
        if (IsPopupWindow(theEvent.Window))
        {
            return theEvent;
        }

        // A press that is not on a popup dismisses it and belongs to the popup: it stops there
        // rather than being delivered to whatever it landed on, which is what the Win32 and
        // overlay presentations do too. Without that, a press on the control that opened the popup
        // closes it and then reaches that control, which acts on a popup that is already gone - a
        // combo box reopens its list on the same click that closed it.
        Dismiss();

        // Returning null is how a local monitor swallows the event, which the binding does not
        // express in its delegate signature.
        return null!;
    }

    private static bool IsPopupWindow(NSWindow? window)
    {
        if (window is null)
        {
            return false;
        }

        foreach (var popup in Live)
        {
            if (ReferenceEquals(popup._window, window))
            {
                return true;
            }
        }

        return false;
    }

    private void Dismiss()
    {
        if (_dismissing || _disposed)
        {
            return;
        }

        _dismissing = true;

        try
        {
            // Removed before the handler runs: the popup disposes this window from inside it, and
            // a monitor must not be removed while it is the one being dispatched.
            RemoveObservers();
            Dismissed?.Invoke(this);
        }
        finally
        {
            _dismissing = false;
        }
    }

    /// <summary>
    /// A borderless window that can never become key or main, so the keyboard stays with the control
    /// that opened the popup and the owner's title bar never goes inactive.
    /// </summary>
    private sealed class PopupWindow : NSWindow
    {
        internal PopupWindow(
            CGRect contentRect,
            NSWindowStyle styleMask,
            NSBackingStore bufferingType,
            bool deferCreation)
            : base(contentRect, styleMask, bufferingType, deferCreation)
        {
        }

        public override bool CanBecomeKeyWindow => false;

        public override bool CanBecomeMainWindow => false;
    }
}
