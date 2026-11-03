using AppKit;
using CoreGraphics;
using Foundation;
using SkiaSharp;

namespace XPlatUI.Backend.AppKit;

/// <summary>
/// The AppKit half of <see cref="IXPlatPopupWindow"/>: a borderless child <see cref="NSWindow"/>
/// that draws an <see cref="XPlatUiHost"/> of its own through an <see cref="AppKitSkiaView"/>.
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
internal sealed class AppKitPopupWindow : IXPlatPopupWindow
{
    private readonly XPlatUiHost _host = new();
    private readonly bool _dismissOnOutsideClick;

    private PopupWindow? _window;
    private AppKitSkiaView? _view;
    private NSWindow? _ownerWindow;
    private NSObject? _clickMonitor;
    private NSObject? _resignObserver;
    private bool _dismissing;
    private bool _disposed;

    internal AppKitPopupWindow(bool dismissOnOutsideClick) => _dismissOnOutsideClick = dismissOnOutsideClick;

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
            HasShadow = true,

            // A tooltip must not swallow the press that was meant for the control underneath it,
            // which is what the Win32 side gets from WS_EX_TRANSPARENT.
            IgnoresMouseEvents = !_dismissOnOutsideClick,
        };

        // The managed side owns the window, so AppKit must not release it when it closes.
        window.ReleaseWhenClosed(false);
        window.Level = NSWindowLevel.Floating;

        var view = new AppKitSkiaView(_host)
        {
            AutoresizingMask = NSViewResizingMask.WidthSizable | NSViewResizingMask.HeightSizable,
        };

        window.ContentView = view;

        _window = window;
        _view = view;
        _ownerWindow = owner;

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

        // ContentLayoutRect is in window space with its origin at the frame's bottom left, so the
        // distance from the top of the frame down to the top of the content is what turns it into
        // an offset from the owner's top left corner.
        var content = owner.ContentLayoutRect;
        var origin = AppKitApplicationBackend.LocationOf(owner);
        double contentTop = owner.Frame.Height - content.Y - content.Height;
        float x = (float)(origin.X + content.X + location.X);
        float y = (float)(origin.Y + contentTop + location.Y);

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
        // The press belongs to whatever it landed on, so the popup is dismissed but the event is
        // still delivered.
        if (_window is null || !ReferenceEquals(theEvent.Window, _window))
        {
            Dismiss();
        }

        return theEvent;
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
