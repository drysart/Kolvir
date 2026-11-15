using AppKit;
using CoreGraphics;
using Foundation;
using Microsoft.Extensions.DependencyInjection;
using SkiaSharp;

namespace Kolvir.Backend.AppKit;

/// <summary>
/// The AppKit half of <see cref="Window"/>: one <c>NSWindow</c> whose content view is an
/// <see cref="AppKitSkiaView"/>, plus the <see cref="UiHost"/> that paints into it.
/// </summary>
/// <remarks>
/// <para>
/// The native window is created lazily, on the first <see cref="Show"/> or
/// <see cref="ShowDialog"/>, so every property can be set before anything exists and the initial
/// frame, style and limits are applied once instead of being patched afterwards.
/// </para>
/// <para>
/// <see cref="Close"/> only reports the request: AppKit's close button is routed through
/// <c>WindowShouldClose</c> for the same reason, so the model always gets to raise its cancellable
/// closing event before the window is destroyed.
/// </para>
/// </remarks>
public sealed class AppKitWindowBackend : IWindowBackend
{
    private Action<object>? _shown;
    private Action<object>? _closeRequested;
    private Action<object>? _closed;
    private Action<object>? _activated;
    private Action<object>? _deactivated;
    private Action<object>? _clientSizeChanged;
    private Action<object>? _scaleChanged;
    private Action<object>? _stateChanged;
    private readonly AppKitApplicationBackend _application;
    private readonly UiHost _host = new();
    private readonly WindowDelegate _delegate;

    private NSWindow? _window;
    private AppKitSkiaView? _view;
    private IContextMenuPresenter? _presenter;
    private readonly bool _useMenus;

    private string _title = string.Empty;
    private SKSize _clientSize = new(800f, 600f);
    private SKSize _minimumClientSize;
    private SKSize _maximumClientSize;
    private SKPoint? _location;
    private bool _resizable = true;
    private bool _topMost;
    private IWindowBackend? _owner;
    private NSWindow? _attachedOwner;
    private WindowState _state = WindowState.Normal;
    private WindowState _reportedState = WindowState.Normal;
    private MenuBar? _menuBar;
    private bool? _darkChrome;
    private Icon? _icon;

    private float _scale = 1f;
    private bool _disposed;
    private bool _modal;

    private Action<object>? _chromeMetricsChanged;
    private WindowChrome _chrome = WindowChrome.System;
    private CaptionButtons _captionButtons = CaptionButtons.None;

    internal AppKitWindowBackend(AppKitApplicationBackend application, Window window)
    {
        ArgumentNullException.ThrowIfNull(application);
        ArgumentNullException.ThrowIfNull(window);

        _application = application;

        // The delegate must stay rooted for as long as the window points at it, and AppKit holds
        // only an unretained reference.
        _delegate = new WindowDelegate(this);

        // Resolved once, when the window is created. The library's menus are the default on every
        // platform; the system menu bar is only used when the application asks for it explicitly.
        _useMenus = !(application.Application?.Options.MenuPresentation ?? MenuPresentation.Auto)
            .UsesNativeMenus();
    }

    /// <inheritdoc />
    public UiHost Host => _host;

    /// <summary>
    /// What this window is presenting with, for a one-line diagnostic: <c>metal</c> when the Metal
    /// tiers are live, or <c>cpu (...)</c> with the step that put it there; <c>cpu (no window)</c>
    /// before the native window exists.
    /// </summary>
    public string GraphicsPath => _view?.GraphicsPath ?? "cpu (no window)";

    /// <summary>
    /// The Metal device name when <see cref="GraphicsPath"/> is <c>metal</c>, or null on the CPU
    /// path.
    /// </summary>
    public string? GraphicsRenderer => _view?.GraphicsRenderer;

    /// <inheritdoc />
    public float ScaleFactor => _scale;

    /// <inheritdoc />
    public Screen? Screen => _window is null
        ? _application.PrimaryScreen
        : AppKitApplicationBackend.ScreenForWindow(_window) ?? _application.PrimaryScreen;

    /// <inheritdoc />
    public string Title
    {
        get => _title;
        set
        {
            _title = value ?? string.Empty;

            if (_window is not null)
            {
                _window.Title = _title;
            }
        }
    }

    /// <inheritdoc />
    /// <remarks>
    /// macOS has no per-window icon: a window's icon is the application's, shown in the Dock and
    /// the app switcher. Setting this assigns <c>NSApplication.ApplicationIconImage</c>, so it
    /// changes the whole process rather than this window alone.
    /// </remarks>
    public Icon? Icon
    {
        get => _icon;
        set
        {
            _icon = value;

            if (AppKitIcons.Create(_icon) is { } image)
            {
                NSApplication.SharedApplication.ApplicationIconImage = image;
            }
        }
    }

    /// <inheritdoc />
    public SKSize ClientSize
    {
        get => _clientSize;
        set
        {
            if (value.Width <= 0f || value.Height <= 0f || _clientSize == value)
            {
                return;
            }

            _clientSize = value;

            // AppKit sizes windows in points, which is the same logical space the library uses,
            // so no scale conversion is involved here at all.
            _window?.SetContentSize(new CGSize(value.Width, value.Height));
        }
    }

    /// <inheritdoc />
    public SKSize MinimumClientSize
    {
        get => _minimumClientSize;
        set
        {
            _minimumClientSize = value;
            ApplySizeLimits();
        }
    }

    /// <inheritdoc />
    public SKSize MaximumClientSize
    {
        get => _maximumClientSize;
        set
        {
            _maximumClientSize = value;
            ApplySizeLimits();
        }
    }

    /// <inheritdoc />
    public SKPoint Location
    {
        get => _window is not null ? AppKitApplicationBackend.LocationOf(_window) : _location ?? SKPoint.Empty;
        set
        {
            _location = value;

            if (_window is not null)
            {
                _window.SetFrameOrigin(OriginFor(_window, value));
            }
        }
    }

    /// <inheritdoc />
    public bool Resizable
    {
        get => _resizable;
        set
        {
            if (_resizable == value)
            {
                return;
            }

            _resizable = value;

            if (_window is not null)
            {
                var style = _window.StyleMask;
                _window.StyleMask = value ? style | NSWindowStyle.Resizable : style & ~NSWindowStyle.Resizable;
            }

            // The resize band comes and goes with the style bit, so the metrics change with it.
            RaiseChromeMetricsChanged();
        }
    }

    /// <inheritdoc />
    public bool TopMost
    {
        get => _topMost;
        set
        {
            if (_topMost == value)
            {
                return;
            }

            _topMost = value;

            if (_window is not null)
            {
                _window.Level = value ? NSWindowLevel.Floating : NSWindowLevel.Normal;
            }
        }
    }

    /// <inheritdoc />
    public IWindowBackend? Owner
    {
        get => _owner;
        set
        {
            if (ReferenceEquals(_owner, value))
            {
                return;
            }

            _owner = value;
            AttachOwner();
        }
    }

    /// <summary>
    /// The window's display state. <see cref="WindowState.FullScreen"/> maps to a zoomed
    /// window for now; true borderless full screen needs a style save/restore that is not worth
    /// carrying until a control asks for it.
    /// </summary>
    public WindowState State
    {
        get => _window is null ? _state : CurrentState();
        set
        {
            _state = value;

            if (_window is null)
            {
                return;
            }

            switch (value)
            {
                case WindowState.Minimized:
                    _window.Miniaturize(null!);
                    break;

                case WindowState.Maximized:
                case WindowState.FullScreen:
                    if (_window.IsMiniaturized)
                    {
                        _window.Deminiaturize(null!);
                    }

                    if (!_window.IsZoomed)
                    {
                        _window.Zoom(null!);
                    }

                    break;

                default:
                    if (_window.IsMiniaturized)
                    {
                        _window.Deminiaturize(null!);
                    }

                    if (_window.IsZoomed)
                    {
                        _window.Zoom(null!);
                    }

                    break;
            }
        }
    }

    /// <inheritdoc />
    public MenuBar? MenuBar
    {
        get => _menuBar;
        set
        {
            if (ReferenceEquals(_menuBar, value))
            {
                return;
            }

            _menuBar = value;
            ApplyMenuBar();
        }
    }

    /// <summary>
    /// Keeps the system bar in step with the window. macOS has one menu bar for the whole process,
    /// owned by AppKit and drawn at the top of the screen, so this is the only bar the platform can
    /// show: it is never drawn inside the Skia surface, whichever presentation the application asked
    /// for. It carries the application menu, and the key equivalents the standard Edit commands rely
    /// on come from being in it.
    /// </summary>
    private void ApplyMenuBar() => _application.RebuildMenuBar();

    /// <summary>The presenter that shows this window's context menus.</summary>
    internal IContextMenuPresenter? Presenter => _presenter;

    /// <summary>The native window, or null before it has been shown.</summary>
    internal NSWindow? Window => _window;

    /// <inheritdoc />
    public void Show()
    {
        if (!EnsureCreated())
        {
            return;
        }

        Present();
    }

    /// <inheritdoc />
    public void ShowDialog()
    {
        if (!EnsureCreated())
        {
            return;
        }

        Present();

        _modal = true;
        _application.EnterModal();

        try
        {
            // A nested run loop, which is the macOS spelling of a modal window. It returns as soon
            // as this window stops the session, which Dispose does when the model closes it.
            NSApplication.SharedApplication.RunModalForWindow(_window!);
        }
        finally
        {
            _application.ExitModal();
            _modal = false;
        }
    }

    /// <inheritdoc />
    public void Activate()
    {
        if (_window is null)
        {
            return;
        }

        _window.MakeKeyAndOrderFront(null!);

        // A modal session takes the first responder with it, so a window that comes back from one
        // is key but types nowhere. Making the host view first responder is what "gives it focus"
        // means here: key events arrive by way of the view.
        _window.MakeFirstResponder(_view);
        NSApplication.SharedApplication.Activate();
    }

    /// <inheritdoc />
    public void Close()
    {
        if (!_disposed)
        {
            _closeRequested?.Invoke(this);
        }
    }

    /// <summary>
    /// Borderless chrome and AppKit's own caption buttons; window colours are not a macOS idea, so
    /// they keep the seam's throwing defaults.
    /// </summary>
    public ChromeCapabilities ChromeCapabilities => new() { Borderless = true, CaptionButtons = true };

    /// <summary>
    /// How much of the window AppKit draws. Borderless keeps the window titled - the traffic lights,
    /// the shadow, the rounded corners and the resize band all come with the title bar - and lets the
    /// content view run underneath it.
    /// </summary>
    public WindowChrome Chrome
    {
        get => _chrome;
        set
        {
            if (_chrome == value)
            {
                return;
            }

            _chrome = value;
            ApplyWindowChrome();
            RaiseChromeMetricsChanged();
        }
    }

    /// <summary>
    /// What AppKit still owns in this window: nothing it paints, but three points on every edge that
    /// it claims for resizing, and a corner radius it clips the content to.
    /// </summary>
    public WindowChromeMetrics ChromeMetrics
    {
        get
        {
            if (_chrome != WindowChrome.Borderless)
            {
                return SystemChromeMetrics;
            }

            var buttons = CaptionButtonLayout();

            return new WindowChromeMetrics
            {
                // Nothing: a full-size content view covers the window, so there is not a pixel of it
                // AppKit paints over.
                SurfaceInset = default,

                // Three points on every edge, measured against the platform: AppKit resizes from
                // inside the application's own pixels, unlike GTK whose band sits outside them. A
                // window that cannot be resized claims nothing, which is also what Win32 reports
                // there - the style bit is what creates the band.
                ResizeInset = _resizable
                    ? new Thickness(ResizeBandWidth, ResizeBandWidth, ResizeBandWidth, ResizeBandWidth)
                    : default,

                // Corners are claimed much more deeply than edges, so this cannot be derived from the
                // band above.
                CornerResizeExtent = _resizable ? CornerResizeExtentPoints : 0f,

                // The window's own radius, which clips anything the application draws near a corner.
                CornerRadius = CornerRadiusPoints,

                CaptionButtons = buttons is null ? CaptionButtons.None : CaptionButtons.Platform,
                CaptionButtonSide = CaptionButtonSide.Left,
                CaptionButtonStrip = buttons?.Strip ?? SKRect.Empty,
                CaptionButtonBounds = buttons?.Buttons ?? [],
            };
        }
    }

    /// <summary>
    /// Whether AppKit's traffic lights stay, or the window goes bare for the application to draw its
    /// own buttons.
    /// </summary>
    /// <remarks>
    /// AppKit already draws these buttons over the application's own pixels and claims their clicks,
    /// so <see cref="CaptionButtons.Platform"/> is the window it already has and
    /// <see cref="CaptionButtons.None"/> hides all three. Nothing is drawn here either way.
    /// </remarks>
    public CaptionButtons CaptionButtons
    {
        get => _captionButtons;
        set
        {
            if (_captionButtons == value)
            {
                return;
            }

            _captionButtons = value;
            ApplyCaptionButtons();
            RaiseChromeMetricsChanged();
        }
    }

    /// <summary>
    /// Starts a window drag from the press being handled.
    /// </summary>
    /// <remarks>
    /// Unlike GTK and Win32, AppKit hands the event back: <c>performWindowDragWithEvent:</c> takes the
    /// press AppKit is currently dispatching, which is the very one the application is inside when it
    /// calls this, so nothing has to be cached. The call runs the drag loop and returns when the drag
    /// ends, which is also why it cannot be started outside a press.
    /// </remarks>
    /// <returns>True when a drag was started.</returns>
    public bool TryBeginDrag()
    {
        // Deliberately not limited to borderless: an application's own title bar is a drag handle in
        // either chrome, and Win32 and GTK both start the move whatever the window is wearing.
        if (_window is null || _disposed)
        {
            return false;
        }

        if (NSApplication.SharedApplication.CurrentEvent is not { } current
            || current.Type != NSEventType.LeftMouseDown)
        {
            return false;
        }

        _window.PerformWindowDrag(current);
        return true;
    }

    /// <summary>
    /// Reports that nothing was started: AppKit has no public way to begin an edge drag of its own,
    /// and its band - see <see cref="WindowChromeMetrics.ResizeInset"/> - is what resizes a
    /// window.
    /// </summary>
    /// <param name="edge">Ignored.</param>
    /// <returns>False, always.</returns>
    public bool TryBeginResize(ResizeEdge edge) => false;

    /// <summary>Adds a handler that runs when <see cref="ChromeMetrics"/> changes.</summary>
    public IDisposable AddChromeMetricsChangedHandler(Action<object> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        _chromeMetricsChanged += handler;
        return new HandlerToken<Action<object>>(RemoveChromeMetricsChangedHandler, handler);
    }

    /// <summary>Removes a handler that <see cref="AddChromeMetricsChangedHandler"/> added.</summary>
    public void RemoveChromeMetricsChangedHandler(Action<object> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        _chromeMetricsChanged -= handler;
    }

    /// <inheritdoc />
    public void SetTitleBarAppearance(bool dark)
    {
        _darkChrome = dark;
        ApplyChrome();
    }

    /// <inheritdoc />
    public IDisposable AddShownHandler(Action<object> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        _shown += handler;
        return new HandlerToken<Action<object>>(RemoveShownHandler, handler);
    }

    /// <summary>Removes a handler that <see cref="AddShownHandler"/> added.</summary>
    public void RemoveShownHandler(Action<object> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        _shown -= handler;
    }

    /// <inheritdoc />
    public IDisposable AddCloseRequestedHandler(Action<object> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        _closeRequested += handler;
        return new HandlerToken<Action<object>>(RemoveCloseRequestedHandler, handler);
    }

    /// <summary>Removes a handler that <see cref="AddCloseRequestedHandler"/> added.</summary>
    public void RemoveCloseRequestedHandler(Action<object> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        _closeRequested -= handler;
    }

    /// <inheritdoc />
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

    /// <inheritdoc />
    public IDisposable AddActivatedHandler(Action<object> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        _activated += handler;
        return new HandlerToken<Action<object>>(RemoveActivatedHandler, handler);
    }

    /// <summary>Removes a handler that <see cref="AddActivatedHandler"/> added.</summary>
    public void RemoveActivatedHandler(Action<object> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        _activated -= handler;
    }

    /// <inheritdoc />
    public IDisposable AddDeactivatedHandler(Action<object> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        _deactivated += handler;
        return new HandlerToken<Action<object>>(RemoveDeactivatedHandler, handler);
    }

    /// <summary>Removes a handler that <see cref="AddDeactivatedHandler"/> added.</summary>
    public void RemoveDeactivatedHandler(Action<object> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        _deactivated -= handler;
    }

    /// <inheritdoc />
    public IDisposable AddClientSizeChangedHandler(Action<object> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        _clientSizeChanged += handler;
        return new HandlerToken<Action<object>>(RemoveClientSizeChangedHandler, handler);
    }

    /// <summary>Removes a handler that <see cref="AddClientSizeChangedHandler"/> added.</summary>
    public void RemoveClientSizeChangedHandler(Action<object> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        _clientSizeChanged -= handler;
    }

    /// <inheritdoc />
    public IDisposable AddScaleChangedHandler(Action<object> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        _scaleChanged += handler;
        return new HandlerToken<Action<object>>(RemoveScaleChangedHandler, handler);
    }

    /// <summary>Removes a handler that <see cref="AddScaleChangedHandler"/> added.</summary>
    public void RemoveScaleChangedHandler(Action<object> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        _scaleChanged -= handler;
    }

    /// <inheritdoc />
    public IDisposable AddStateChangedHandler(Action<object> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        _stateChanged += handler;
        return new HandlerToken<Action<object>>(RemoveStateChangedHandler, handler);
    }

    /// <summary>Removes a handler that <see cref="AddStateChangedHandler"/> added.</summary>
    public void RemoveStateChangedHandler(Action<object> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        _stateChanged -= handler;
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        var window = _window;
        var view = _view;

        DetachOwner();

        _window = null;
        _view = null;
        _presenter = null;

        if (window is not null)
        {
            // Ending the session has to come first: RunModalForWindow owns the loop until it does.
            if (_modal)
            {
                _modal = false;
                NSApplication.SharedApplication.StopModal();
            }

            window.Delegate = null!;

            // Detach the view before its drawing buffers go away, so AppKit cannot ask it to draw
            // a frame it no longer has anything to draw with.
            window.ContentView = null!;
            window.OrderOut(null!);
            window.Close();
        }

        view?.Dispose();

        _application.RemoveWindow(this);
        _closed?.Invoke(this);
    }

    /// <summary>Prints frame rate and memory use every few seconds when tracing is on.</summary>
    internal void ReportStatistics() => _view?.ReportStatistics();

    /// <summary>
    /// Makes the window a drag destination, and takes it back out again when set to null. A window
    /// that is never given a sink is never registered for any dragged type, which is what keeps the
    /// cost of being a drag destination off an application that never asked for one.
    /// </summary>
    public IAppKitDragSink? DragSink
    {
        get => _view?.DragSink;
        set
        {
            if (_view is not null)
            {
                _view.DragSink = value;
            }
        }
    }

    private bool EnsureCreated()
    {
        if (_window is not null)
        {
            return true;
        }

        if (_disposed)
        {
            return false;
        }

        var style = NSWindowStyle.Titled | NSWindowStyle.Closable | NSWindowStyle.Miniaturizable;

        if (_resizable)
        {
            style |= NSWindowStyle.Resizable;
        }

        // A borderless window's content view runs under the title bar, so the style has to say so
        // before the window is built: adding it afterwards makes AppKit re-lay the window out.
        if (_chrome == WindowChrome.Borderless)
        {
            style |= NSWindowStyle.FullSizeContentView;
        }

        var window = new NSWindow(
            new CGRect(0, 0, _clientSize.Width, _clientSize.Height),
            style,
            NSBackingStore.Buffered,
            deferCreation: false)
        {
            Title = _title,
        };

        // The managed side owns the window, so AppKit must not release it when it closes. The
        // property setter is obsolete; the method is the supported spelling of the same thing.
        window.ReleaseWhenClosed(false);
        window.Delegate = _delegate;

        // The same application option the Win32 backend honours. Its platform default is off on
        // macOS (Metal is slower on light text-heavy pages and much faster on image-heavy ones, so
        // the application decides), and an explicit assignment or --no-hardware-acceleration is
        // already applied there. Resolved once, when the window is created, because the
        // presentation choice does not change while a window is open.
        var useHardwareAcceleration = _application.Application?.Options.GfxHardwareAcceleration ?? false;

        var view = new AppKitSkiaView(_host, useHardwareAcceleration)
        {
            AutoresizingMask = NSViewResizingMask.WidthSizable | NSViewResizingMask.HeightSizable,
        };

        window.ContentView = view;

        _window = window;
        _view = view;

        // The style mask carries the borderless bit already; the rest of the shape - transparent title
        // bar, hidden title - is set here, before the window is on screen.
        ApplyWindowChrome();
        _scale = (float)window.BackingScaleFactor;
        _reportedState = CurrentState();

        // The layer and backing scale exist now, so the presentation path can be decided before
        // the window reports itself shown and the demo prints its one line.
        view.PrepareGraphics();

        // A context menu is anchored to the view that shows it, so it can only be built now.
        _presenter = _useMenus
            ? new MenuPresenter()
            : new AppKitContextMenuPresenter(view);

        // The bar may have been assigned before there was a window to draw it in.
        ApplyMenuBar();

        ApplySizeLimits();

        if (_topMost)
        {
            window.Level = NSWindowLevel.Floating;
        }

        // The child-window relationship is what makes an owned window stay above its owner and go
        // with it when the owner is ordered out; AppKit has nothing else for it.
        AttachOwner();

        Place(window);

        // A window that has never been focused is not the active window yet, but it is the one the
        // menu bar should describe as soon as it is.
        _application.NotifyWindowActivated(this);

        return true;
    }

    private void Present()
    {
        var window = _window!;

        ApplyChrome();

        window.MakeKeyAndOrderFront(null!);
        window.MakeFirstResponder(_view);

        // -[NSApplication activate] does nothing unless the application is already frontmost, so a
        // window shown by a background process would never become active. The running-application
        // proxy is the API that still brings one forward.
        if (!NSApplication.SharedApplication.Active)
        {
            NSRunningApplication.CurrentApplication?.Activate(NSApplicationActivationOptions.ActivateIgnoringOtherWindows);
        }

        _shown?.Invoke(this);
    }

    /// <summary>
    /// Makes this window a child of its owner, or detaches it when the owner changed. A child
    /// window is ordered above its parent and is ordered out with it, which is the whole of
    /// AppKit's ownership model; closing is the model's job.
    /// </summary>
    private void AttachOwner()
    {
        if (_window is null)
        {
            return;
        }

        var parent = (_owner as AppKitWindowBackend)?.Window;

        if (ReferenceEquals(parent, _attachedOwner))
        {
            return;
        }

        _attachedOwner?.RemoveChildWindow(_window);
        _attachedOwner = parent;
        parent?.AddChildWindow(_window, NSWindowOrderingMode.Above);
    }

    /// <summary>Breaks the child-window relationship, if there is one.</summary>
    private void DetachOwner()
    {
        if (_attachedOwner is { } parent && _window is { } window)
        {
            parent.RemoveChildWindow(window);
        }

        _attachedOwner = null;
    }

    /// <summary>The three points of application pixel AppKit claims on each edge for resizing.</summary>
    /// <remarks>
    /// Measured against the platform rather than derived: AppKit's resize band lies on top of the
    /// application's own pixels, so an application drawing its own grip has to keep controls out of
    /// this much of its edge.
    /// </remarks>
    private const float ResizeBandWidth = 3f;

    /// <summary>The corner radius at which AppKit claims a diagonal resize, measured at about 13 points.</summary>
    private const float CornerResizeExtentPoints = 13f;

    /// <summary>The radius of the window's rounded corners, which clips the content drawn under it.</summary>
    private const float CornerRadiusPoints = 11f;

    /// <summary>What a window with AppKit's own chrome reports: it owns nothing of its own.</summary>
    private static readonly WindowChromeMetrics SystemChromeMetrics = new();

    /// <summary>
    /// Puts the window into the shape its <see cref="Chrome"/> asks for.
    /// </summary>
    /// <remarks>
    /// A borderless window stays <em>titled</em>: the traffic lights, the shadow, the rounded corners
    /// and the resize band are all part of the titled style, so removing it would take away every one
    /// of them. What goes is the space the title bar reserves - <c>FullSizeContentView</c> lets the
    /// content view run under the title bar, a transparent title bar stops it painting a strip over
    /// the content, and a hidden title stops it drawing text there.
    /// </remarks>
    private void ApplyWindowChrome()
    {
        if (_window is null || _disposed)
        {
            return;
        }

        var borderless = _chrome == WindowChrome.Borderless;
        var style = _window.StyleMask;

        _window.StyleMask = borderless
            ? style | NSWindowStyle.FullSizeContentView
            : style & ~NSWindowStyle.FullSizeContentView;

        _window.TitlebarAppearsTransparent = borderless;
        _window.TitleVisibility = borderless ? NSWindowTitleVisibility.Hidden : NSWindowTitleVisibility.Visible;

        // Changing the style mask re-lays the frame out, and the content view has to end up covering
        // whatever the window now is.
        if (_view is { } view)
        {
            view.Frame = _window.ContentView?.Frame ?? view.Frame;
        }

        // The buttons come and go with the chrome: a borderless window may ask to own them.
        ApplyCaptionButtons();
    }

    private void RaiseChromeMetricsChanged() => _chromeMetricsChanged?.Invoke(this);

    /// <summary>The three traffic lights, in the order AppKit lays them out.</summary>
    private static (CaptionButtonKind Kind, NSWindowButton Which)[] StandardButtons() =>
    [
        (CaptionButtonKind.Close, NSWindowButton.CloseButton),
        (CaptionButtonKind.Minimize, NSWindowButton.MiniaturizeButton),
        (CaptionButtonKind.Maximize, NSWindowButton.ZoomButton),
    ];

    /// <summary>Whether AppKit's own caption buttons are on screen for this window.</summary>
    private bool ShowsCaptionButtons =>
        _chrome == WindowChrome.Borderless && _captionButtons == CaptionButtons.Platform;

    /// <summary>Shows or hides AppKit's own caption buttons for what the window currently asks for.</summary>
    /// <remarks>
    /// A decorated window always keeps them - they are part of the chrome AppKit owns - so only a
    /// borderless window can ask for none.
    /// </remarks>
    private void ApplyCaptionButtons()
    {
        if (_window is null || _disposed)
        {
            return;
        }

        var show = _chrome != WindowChrome.Borderless || _captionButtons == CaptionButtons.Platform;

        foreach (var (_, which) in StandardButtons())
        {
            if (_window.StandardWindowButton(which) is { } button)
            {
                button.Hidden = !show;
            }
        }
    }

    /// <summary>
    /// The traffic lights and the band they sit in, read back from AppKit rather than computed.
    /// </summary>
    /// <returns>Null when the window has no platform buttons to report.</returns>
    private (SKRect Strip, IReadOnlyList<CaptionButton> Buttons)? CaptionButtonLayout()
    {
        if (!ShowsCaptionButtons || _window is null)
        {
            return null;
        }

        var buttons = new List<CaptionButton>(3);
        var union = SKRect.Empty;

        foreach (var (kind, which) in StandardButtons())
        {
            if (_window.StandardWindowButton(which) is not { Hidden: false } button)
            {
                continue;
            }

            if (!TryClientRect(button, out var bounds))
            {
                continue;
            }

            buttons.Add(new CaptionButton(kind, bounds));
            union = union == SKRect.Empty ? bounds : SKRect.Union(union, bounds);
        }

        if (buttons.Count == 0)
        {
            return null;
        }

        // Win32 and GTK both report a band that starts at the window's own edge, so the strip is the
        // union of the buttons taken up to the top edge. AppKit paints nothing in it and claims none
        // of it - the buttons themselves are what claim presses - but an application keeping a top
        // row of its own clear of the lights wants the whole band.
        return (new SKRect(union.Left, 0f, union.Right, union.Bottom), buttons);
    }

    /// <summary>A view's frame in client coordinates.</summary>
    /// <remarks>
    /// The content view is flipped, so its coordinate space already runs down from the window's top
    /// edge and conversion is all that is needed.
    /// </remarks>
    private bool TryClientRect(NSView view, out SKRect rect)
    {
        rect = SKRect.Empty;

        if (_view is null)
        {
            return false;
        }

        var frame = view.ConvertRectToView(view.Bounds, _view);
        if (frame.Width <= 0 || frame.Height <= 0)
        {
            return false;
        }

        rect = new SKRect((float)frame.X, (float)frame.Y, (float)frame.Right, (float)frame.Bottom);
        return true;
    }

    /// <summary>Makes the title bar and other AppKit chrome match the palette the library draws with.</summary>
    private void ApplyChrome()
    {
        if (_window is null || _darkChrome is not { } dark)
        {
            return;
        }

        // When the two agree the window simply inherits the system appearance, which keeps the
        // vibrant and high contrast variants AppKit would otherwise lose.
        //
        // The factory has to be appearanceNamed: rather than initWithAppearanceNamed:bundle: - the
        // latter looks for an appearance resource inside a bundle, so a system appearance name
        // makes it return nil and the binding throws.
        _window.Appearance = dark == _application.SystemTheme.IsDarkMode
            ? null
            : NSAppearance.GetAppearance(dark ? NSAppearance.NameDarkAqua : NSAppearance.NameAqua);
    }

    private void ApplySizeLimits()
    {
        if (_window is null)
        {
            return;
        }

        _window.ContentMinSize = new CGSize(_minimumClientSize.Width, _minimumClientSize.Height);
        _window.ContentMaxSize = _maximumClientSize.Width > 0f && _maximumClientSize.Height > 0f
            ? new CGSize(_maximumClientSize.Width, _maximumClientSize.Height)
            : new CGSize(double.MaxValue, double.MaxValue);
    }

    /// <summary>Centres the window on the main display, or puts it where the model asked.</summary>
    private void Place(NSWindow window)
    {
        if (_location is { } location)
        {
            window.SetFrameOrigin(OriginFor(window, location));
            return;
        }

        window.Center();
    }

    private static CGPoint OriginFor(NSWindow window, SKPoint location)
    {
        var screens = NSScreen.Screens;
        var primaryHeight = screens is { Length: > 0 } ? screens[0].Frame.Height : window.Frame.Height;

        return new CGPoint(
            location.X,
            primaryHeight - location.Y - window.Frame.Height);
    }

    private WindowState CurrentState()
    {
        if (_window is null)
        {
            return _state;
        }

        if (_window.IsMiniaturized)
        {
            return WindowState.Minimized;
        }

        return _window.IsZoomed ? WindowState.Maximized : WindowState.Normal;
    }

    private void RaiseStateChangedIfNeeded()
    {
        var state = CurrentState();
        if (state == _reportedState)
        {
            return;
        }

        _reportedState = state;
        _stateChanged?.Invoke(this);

        // Maximising changes what the window clips and how much of its edge it claims, so the metrics
        // changed with the state.
        RaiseChromeMetricsChanged();
    }

    /// <summary>
    /// The window delegate. AppKit's decisions are all reported back to the backend, which decides
    /// whether to forward them to the model, so no lifecycle policy lives here.
    /// </summary>
    private sealed class WindowDelegate : NSWindowDelegate
    {
        private readonly AppKitWindowBackend _backend;

        public WindowDelegate(AppKitWindowBackend backend) => _backend = backend;

        /// <summary>
        /// The close button and Cmd+W. Answering false keeps AppKit from destroying the window
        /// behind the model's back: the request goes to the model, which disposes it if nothing
        /// cancels the close.
        /// </summary>
        public override bool WindowShouldClose(NSObject sender)
        {
            _backend.Close();
            return false;
        }

        public override void DidBecomeKey(NSNotification notification)
        {
            _backend._application.NotifyWindowActivated(_backend);
            _backend._activated?.Invoke(_backend);
        }

        public override void DidResignKey(NSNotification notification)
        {
            _backend._application.NotifyWindowDeactivated(_backend);
            _backend._deactivated?.Invoke(_backend);
        }

        public override void DidResize(NSNotification notification)
        {
            var content = _backend._window?.ContentView;
            if (content is not null)
            {
                _backend._clientSize = new SKSize((float)content.Bounds.Width, (float)content.Bounds.Height);
            }

            _backend._clientSizeChanged?.Invoke(_backend);
            _backend.RaiseStateChangedIfNeeded();
        }

        /// <summary>
        /// Fires when the window moves to a display with a different scale factor, and also when
        /// the display's own scale changes, which is the only signal either case gives.
        /// </summary>
        public override void DidChangeBackingProperties(NSNotification notification)
        {
            var scale = _backend._window is { } window ? (float)window.BackingScaleFactor : 1f;
            if (Math.Abs(scale - _backend._scale) < 0.001f)
            {
                return;
            }

            _backend._scale = scale;
            _backend._scaleChanged?.Invoke(_backend);
            _backend.RaiseChromeMetricsChanged();
        }

        public override void DidMiniaturize(NSNotification notification) => _backend.RaiseStateChangedIfNeeded();

        public override void DidDeminiaturize(NSNotification notification) => _backend.RaiseStateChangedIfNeeded();

        public override void DidEndLiveResize(NSNotification notification) => _backend.RaiseStateChangedIfNeeded();
    }

    /// <inheritdoc />
    public void RunWindowHooks(WindowPhase phase, Window window)
    {
        ArgumentNullException.ThrowIfNull(window);

        foreach (var hook in window.Services.GetServices<IAppKitWindowHook>())
        {
            hook.OnPhase(phase, window, this);
        }
    }
}
