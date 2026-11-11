using System.Globalization;
using System.Text;
using Microsoft.Extensions.DependencyInjection;
using SkiaSharp;

namespace XPlatUI.Backend.Gtk4;

/// <summary>
/// A GTK4 window hosting a SkiaSharp-drawn control tree.
/// </summary>
/// <remarks>
/// <para>
/// The native window is created lazily, on the first <see cref="Show"/> or
/// <see cref="ShowDialog"/>. That matters because the model allows windows to be configured - and
/// even created - before the application runs, and GTK cannot build a window before the display is
/// open.
/// </para>
/// <para>
/// GTK4 has no way to place a window on screen: <c>gtk_window_set_position</c> was removed in GTK4
/// because it fights the compositor, so <see cref="Location"/> is recorded but only honoured where
/// the platform allows it (it is not). <see cref="TopMost"/> is likewise a window-manager decision
/// on Wayland and is only stored. Both are kept so the model's API is uniform across platforms.
/// </para>
/// </remarks>
public sealed class Gtk4WindowBackend : IXPlatWindowBackend
{
    private Action<object>? _shown;
    private Action<object>? _graphicsPathChanged;
    private Action<object>? _closeRequested;
    private Action<object>? _closed;
    private Action<object>? _activated;
    private Action<object>? _deactivated;
    private Action<object>? _clientSizeChanged;
    private Action<object>? _scaleChanged;
    private Action<object>? _stateChanged;
    private readonly Gtk4ApplicationBackend _application;
    private readonly XPlatUiHost _host = new();

    private Gtk.ApplicationWindow? _window;
    private Gtk.Box? _container;
    private Gtk4SkiaView? _view;
    private IXPlatContextMenuPresenter? _presenter;
    private Gtk4MenuBar? _menuBarWidget;
    private GLib.MainLoop? _modalLoop;
    private IXPlatWindowBackend? _owner;
    private readonly bool _useXPlatMenus;

    private string _title = string.Empty;
    private SKSize _clientSize = new(800f, 600f);
    private SKSize _minimumClientSize;
    private SKSize _maximumClientSize;
    private SKPoint? _location;
    private bool _resizable = true;
    private bool _topMost;
    private XPlatWindowState _state = XPlatWindowState.Normal;
    private XPlatWindowState _reportedState = XPlatWindowState.Normal;
    private XPlatMenuBar? _menuBar;
    private bool? _darkChrome;
    private XPlatIcon? _icon;

    private float _scale = 1f;
    private bool _disposed;
    private bool _presentPending;
    private bool _menuBarAttached;
    private List<NativeTeardownRegistration>? _nativeTeardowns;
    private bool _active;

    private Action<object>? _chromeMetricsChanged;
    private XPlatWindowChrome _chrome = XPlatWindowChrome.System;
    private SKColor? _captionColor;
    private SKColor? _borderColor;

    /// <summary>
    /// The widget that takes the title bar's place when the window is borderless: an empty box, which
    /// asks for no space at all.
    /// </summary>
    /// <remarks>
    /// It has to be a widget rather than <c>set_titlebar(null)</c>, because null restores GTK's own
    /// title bar instead of removing one. An empty <see cref="Gtk.Box"/> is enough: it has no minimum
    /// size of its own, so the strip collapses as soon as the theme's <c>min-height</c> on the title
    /// bar node is overridden - which is what <see cref="ChromeCss"/> does.
    /// </remarks>
    private Gtk.Widget? _zeroTitlebar;

    /// <summary>This window's stylesheet, and the class that scopes it to this window.</summary>
    private Gtk.CssProvider? _chromeProvider;
    private string? _chromeClass;

    private static int _chromeClassCounter;

    internal Gtk4WindowBackend(Gtk4ApplicationBackend application, XPlatWindow window)
    {
        ArgumentNullException.ThrowIfNull(application);
        ArgumentNullException.ThrowIfNull(window);

        _application = application;

        // Resolved once, when the window is created: the application's presentation choice does not
        // change while a window is open, and the presenters are installed from it.
        _useXPlatMenus = !(application.Application?.Options.MenuPresentation ?? XPlatMenuPresentation.Auto)
            .UsesNativeMenus();
    }

    /// <inheritdoc />
    public XPlatUiHost Host => _host;

    /// <inheritdoc />
    public float ScaleFactor => _scale;

    /// <summary>
    /// What this window is presenting with, for a one-line diagnostic: <c>gl</c> when the GL seam
    /// came up, or <c>cpu (...)</c> naming why it did not. It is decided on the first render pass,
    /// so it is meaningful from <c>Shown</c> onwards.
    /// </summary>
    public string GraphicsPath => _view?.GraphicsPath ?? "cpu (no window)";

    /// <summary>
    /// The GL vendor and renderer when <see cref="GraphicsPath"/> is <c>gl</c>, or null on the CPU
    /// path. Diagnostic only, and the same lifetime rule as the path.
    /// </summary>
    public string? GraphicsRenderer => _view?.GraphicsRenderer;

    /// <summary>
    /// Adds a handler that runs once <see cref="GraphicsPath"/> has stopped saying
    /// <c>cpu (not created)</c>.
    /// </summary>
    /// <remarks>
    /// The Win32 and AppKit backends allocate their drawing surface while showing the window, so
    /// their path is already final when <c>Shown</c> runs. GTK4 cannot: a GL context only exists
    /// once the area has been allocated and drawn, which happens after <c>Shown</c>. A caller that
    /// wants the footer or the <c>BENCH</c> line to name the path therefore reads it here as well -
    /// and a window that never asked for acceleration reports immediately.
    /// </remarks>
    /// <param name="handler">The handler, called with this backend.</param>
    /// <returns>A token that removes the handler.</returns>
    public IDisposable AddGraphicsPathChangedHandler(Action<object> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);

        if (_view is { } view)
        {
            return view.AddGraphicsPathChangedHandler(handler);
        }

        _graphicsPathChanged += handler;
        return new XPlatHandlerToken<Action<object>>(RemoveGraphicsPathChangedHandler, handler);
    }

    /// <summary>Removes a handler that <see cref="AddGraphicsPathChangedHandler"/> added.</summary>
    public void RemoveGraphicsPathChangedHandler(Action<object> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);

        _graphicsPathChanged -= handler;
        _view?.RemoveGraphicsPathChangedHandler(handler);
    }

    private void OnViewGraphicsPathChanged(object sender) => _graphicsPathChanged?.Invoke(this);

    /// <inheritdoc />
    public XPlatScreen? Screen => _window is null
        ? _application.PrimaryScreen
        : Gtk4ApplicationBackend.ScreenForWindow(_window) ?? _application.PrimaryScreen;

    /// <summary>The native window, or null before it is created. Used to parent native dialogs.</summary>
    public Gtk.Window? Window => _window;

    /// <summary>The presenter that shows context menus for this window.</summary>
    internal IXPlatContextMenuPresenter? Presenter => _presenter;

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
    /// GTK4 takes an icon by name from the desktop's theme and has no binding that accepts a bitmap
    /// for a window, so only <see cref="XPlatIcon.Name"/> reaches the title bar; a pixel-only icon
    /// is stored and ignored. X11 window managers honour the name, and a Wayland compositor takes
    /// the icon from the application's desktop entry instead.
    /// </remarks>
    public XPlatIcon? Icon
    {
        get => _icon;
        set
        {
            _icon = value;
            ApplyIcon();
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

            // GTK sizes windows in logical pixels, which is the same space the library uses, so no
            // scale conversion is involved beyond the ring, which is laid out and so has to be paid
            // for on top of the client.
            _window?.SetDefaultSize(
                ToPixels(value.Width) + (2 * BorderWidth),
                ToPixels(value.Height) + (2 * BorderWidth));

            // The drawing area reports its own size once GTK allocates it, but that happens a frame
            // after the window is presented - later than the Shown event, which the other two
            // backends raise with a host that already has its size. Seeding the host here keeps a
            // popup opened from a Shown handler from being clamped to a 0 x 0 client area.
            if (_host.ClientSize.Width <= 0f || _host.ClientSize.Height <= 0f)
            {
                _host.SetClientSize(value.Width, value.Height);
            }
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
            // GTK has no maximum window size: the compositor, not the application, decides how big
            // a window may get. The value is recorded so the model round-trips it.
            _maximumClientSize = value;
        }
    }

    /// <inheritdoc />
    public SKPoint Location
    {
        get
        {
            if (_window is not null && _window.GetBounds(out var x, out var y, out _, out _))
            {
                return new SKPoint(x, y);
            }

            return _location ?? SKPoint.Empty;
        }
        set => _location = value;
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
                _window.Resizable = value;
            }
        }
    }

    /// <summary>
    /// True to keep the window above all others. GTK4 removed <c>gtk_window_set_keep_above</c> and
    /// Wayland leaves stacking to the compositor, so the value is recorded and has no effect here.
    /// </summary>
    public bool TopMost
    {
        get => _topMost;
        set => _topMost = value;
    }

    /// <inheritdoc />
    public IXPlatWindowBackend? Owner
    {
        get => _owner;
        set
        {
            if (ReferenceEquals(_owner, value))
            {
                return;
            }

            _owner = value;
            SetTransient(_owner);
        }
    }

    /// <summary>
    /// The window's display state. GTK reports full screen and maximize separately, and both are
    /// honoured; minimize is polled because GTK does not notify on it.
    /// </summary>
    public XPlatWindowState State
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
                case XPlatWindowState.Minimized:
                    _window.Minimize();
                    break;

                case XPlatWindowState.Maximized:
                    _window.Unminimize();
                    _window.Maximize();
                    break;

                case XPlatWindowState.FullScreen:
                    _window.Unminimize();
                    _window.Fullscreen();
                    break;

                default:
                    _window.Unminimize();
                    if (_window.IsFullscreen())
                    {
                        _window.Unfullscreen();
                    }

                    if (_window.IsMaximized())
                    {
                        _window.Unmaximize();
                    }

                    break;
            }

            RaiseStateChangedIfNeeded();
        }
    }

    /// <inheritdoc />
    public XPlatMenuBar? MenuBar
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
    /// Borderless chrome and the two window colours are implemented here; caption buttons arrive with
    /// their own step and are reported as unsupported until then.
    /// </summary>
    public XPlatChromeCapabilities ChromeCapabilities => new() { Borderless = true, WindowColors = true };

    /// <summary>
    /// How much of the window GTK draws. Borderless keeps the client-side decorations - so the
    /// compositor keeps the resize band, the shadow and the window's behaviours - and collapses the
    /// title bar to nothing.
    /// </summary>
    public XPlatWindowChrome Chrome
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
    /// The colour GTK paints the window's own surface in.
    /// </summary>
    /// <remarks>
    /// Null leaves the theme's colour, which is what a decorated window wants. GTK has no separate
    /// caption surface to colour, so this is the window surface itself - which in a borderless window
    /// is covered by the application's content and therefore only shows where the application leaves
    /// the window unpainted. It is implemented for parity with the platforms that do have a caption
    /// strip; on GTK the visible half of <see cref="XPlatChromeCapabilities.WindowColors"/> is
    /// <see cref="BorderColor"/>.
    /// </remarks>
    public SKColor? CaptionColor
    {
        get => _captionColor;
        set
        {
            if (_captionColor == value)
            {
                return;
            }

            _captionColor = value;
            ApplyChromeCss();
        }
    }

    /// <summary>
    /// The colour of the one-pixel ring GTK draws round the window, which follows the rounded corners.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Null draws no ring at all, and then the whole window is the application's. A colour costs one
    /// logical pixel on every edge: GTK's ring is a <c>border</c> on the window, which is laid out, so
    /// the window is asked for two pixels more than the client it holds and
    /// <see cref="ChromeMetrics"/> reports the ring as <see cref="XPlatWindowChromeMetrics.SurfaceInset"/>.
    /// </para>
    /// <para>
    /// A <c>border</c> rather than an <c>outline</c>: an outline takes no space, which is tempting, but
    /// it is painted with the window's background and the child draws over half of it, so the ring came
    /// and went depending on whether the application had repainted. A border is outside the child's
    /// allocation, so nothing can draw over it.
    /// </para>
    /// </remarks>
    public SKColor? BorderColor
    {
        get => _borderColor;
        set
        {
            if (_borderColor == value)
            {
                return;
            }

            var hadRing = BorderWidth;
            _borderColor = value;
            ApplyChromeCss();

            // The ring is laid out, so gaining or losing one changes the client a borderless window
            // holds: the window is re-asked for the same client rather than being left two pixels
            // different.
            if (hadRing != BorderWidth)
            {
                ApplyRequestedSize();
                RaiseChromeMetricsChanged();
            }
        }
    }

    /// <summary>
    /// What GTK still owns in this window. On GTK that is nothing inside the surface: the resize band
    /// is 12 logical pixels deep and lies entirely outside the application's pixels, so every pixel
    /// the application paints is its own to hit test.
    /// </summary>
    public XPlatWindowChromeMetrics ChromeMetrics
    {
        get
        {
            if (_chrome != XPlatWindowChrome.Borderless)
            {
                return SystemChromeMetrics;
            }

            return new XPlatWindowChromeMetrics
            {
                // One logical pixel on every edge when a ring was asked for, and nothing at all when
                // it was not: GTK's ring is laid out, so the child is that much smaller.
                SurfaceInset = BorderWidth == 0
                    ? default
                    : new XPlatThickness(BorderWidth, BorderWidth, BorderWidth, BorderWidth),

                // Empty: the band is the ring outside the content, and the application never paints
                // there. The number is worth knowing all the same, so it is named below.
                ResizeInset = default,

                CornerResizeExtent = 0f,

                // Set explicitly by this backend rather than left to the theme, because the two GTK
                // versions measured here default to different radii (about 10 logical pixels on 4.14,
                // about 12 on 4.22) and an application clipping its own chrome has to know which.
                CornerRadius = BorderlessCornerRadius,

                CaptionButtons = XPlatCaptionButtons.None,
                CaptionButtonSide = XPlatCaptionButtonSide.Right,
                CaptionButtonStrip = SKRect.Empty,
                CaptionButtonBounds = [],
            };
        }
    }

    /// <summary>Adds a handler that runs when <see cref="ChromeMetrics"/> changes.</summary>
    public IDisposable AddChromeMetricsChangedHandler(Action<object> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        _chromeMetricsChanged += handler;
        return new XPlatHandlerToken<Action<object>>(RemoveChromeMetricsChangedHandler, handler);
    }

    /// <summary>Removes a handler that <see cref="AddChromeMetricsChangedHandler"/> added.</summary>
    public void RemoveChromeMetricsChangedHandler(Action<object> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        _chromeMetricsChanged -= handler;
    }

    /// <inheritdoc />
    public void Show()
    {
        // GTK refuses a window created before GApplication::startup, and the model shows windows
        // before it enters the loop. Creation is therefore deferred to the activate signal, which
        // is GTK's equivalent of AppKit's finishLaunching.
        _presentPending = true;

        if (!_application.IsLoopRunning)
        {
            return;
        }

        PresentPending();
    }

    /// <inheritdoc />
    public void ShowDialog()
    {
        if (!EnsureCreated())
        {
            return;
        }

        _window!.Modal = true;
        Present();

        // A nested main loop is GTK's version of a modal session: the parent window stops accepting
        // input, the rest of the application keeps painting, and the call returns when the dialog is
        // closed by Dispose.
        var loop = GLib.MainLoop.New(GLib.MainContext.Default(), false);
        _modalLoop = loop;
        _application.EnterModal(loop);

        try
        {
            loop.Run();
        }
        finally
        {
            _modalLoop = null;
            _application.ExitModal(loop);
        }
    }

    /// <inheritdoc />
    public void Activate()
    {
        if (_window is null)
        {
            return;
        }

        _window.Present();
        _view?.Focus();
    }

    /// <summary>
    /// Asks the model to close the window. GTK's own close button is vetoed for the same reason:
    /// the model has to raise its cancellable closing event before anything is destroyed.
    /// </summary>
    public void Close()
    {
        if (!_disposed)
        {
            _closeRequested?.Invoke(this);
        }
    }

    /// <inheritdoc />
    public void SetTitleBarAppearance(bool dark)
    {
        _darkChrome = dark;
        ApplyChrome();
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
        var menuBar = _menuBarWidget;
        var presenter = _presenter;
        var loop = _modalLoop;

        _window = null;
        _container = null;
        _view = null;
        _presenter = null;
        _menuBarWidget = null;
        _modalLoop = null;

        // End the nested loop first: everything below would otherwise run while it is still the
        // active loop.
        if (loop is not null)
        {
            _application.ExitModal(loop);

            if (loop.IsRunning())
            {
                loop.Quit();
            }
        }

        // Only the native presenter holds a live popover; the portable one owns no native state, so
        // its menus close with the popups that the window is about to tear down.
        (presenter as Gtk4ContextMenuPresenter)?.Dismiss();

        if (XPlatPopupService.Current is Gtk4PopupService)
        {
            XPlatPopupService.Current = null;
        }

        // Whatever the window registered natively has to let go while the window still exists: the
        // platform keeps calling into a registered object for as long as the window is alive.
        ReleaseNativeTeardowns();

        if (view is not null)
        {
            view.RemoveMetricsChangedHandler(OnViewMetricsChanged);
            view.RemoveGraphicsPathChangedHandler(OnViewGraphicsPathChanged);
            view.Dispose();
        }

        menuBar?.Dispose();

        // The stylesheet is installed on the display, which outlives this window, so it comes off
        // before the window it was written for goes away.
        if (_chromeProvider is { } provider)
        {
            if (Gdk.Display.GetDefault() is { } providerDisplay)
            {
                Gtk.StyleContext.RemoveProviderForDisplay(providerDisplay, provider);
            }

            _chromeProvider = null;
        }

        if (window is not null)
        {
            window.OnNotify -= OnWindowNotify;
            window.Destroy();
        }

        _application.RemoveWindow(this);
        _closed?.Invoke(this);
    }

    /// <summary>Called by the application backend once the main loop exists.</summary>
    internal void PresentPending()
    {
        if (!_presentPending || _disposed)
        {
            return;
        }

        if (!EnsureCreated())
        {
            return;
        }

        _presentPending = false;
        Present();
    }

    /// <summary>Called by the application backend's poll timer; only reports under XPLATUI_DEBUG.</summary>
    internal void ReportStatistics()
    {
        _view?.ReportStatistics();
        RaiseStateChangedIfNeeded();
    }

    /// <summary>The view that shows the host, or null before the window has been shown.</summary>
    public Gtk4SkiaView? View => _view;

    /// <summary>
    /// Registers a native object that has to be released while the native window still exists.
    /// </summary>
    /// <param name="teardown">The object to dispose. It is disposed exactly once.</param>
    /// <returns>
    /// A registration that releases <paramref name="teardown"/> early when it is disposed, and that
    /// is harmless to dispose more than once. A caller that never disposes it leaves the release to
    /// the window's own disposal, which is the one point that still runs when a window is disposed
    /// without ever being closed.
    /// </returns>
    /// <remarks>
    /// The platform keeps calling into a registered native object for as long as the window is
    /// alive, so it has to be released before the window is destroyed. This is how a feature takes
    /// part in that without the backend knowing what it registered: the object is an
    /// <see cref="IDisposable"/> and nothing here is named after the feature that put it there.
    /// Registrations are released last in, first out.
    /// </remarks>
    public IDisposable RegisterNativeTeardown(IDisposable teardown)
    {
        ArgumentNullException.ThrowIfNull(teardown);

        var registration = new NativeTeardownRegistration(this, teardown);
        (_nativeTeardowns ??= []).Add(registration);
        return registration;
    }

    /// <summary>Releases everything registered with <see cref="RegisterNativeTeardown"/>.</summary>
    private void ReleaseNativeTeardowns()
    {
        var registrations = _nativeTeardowns;
        _nativeTeardowns = null;

        if (registrations is null)
        {
            return;
        }

        // What was layered on the window comes off in the reverse of the order it went on.
        for (var index = registrations.Count - 1; index >= 0; index--)
        {
            registrations[index].Dispose();
        }
    }

    /// <summary>One entry in the window's list, releasable early and harmless to dispose twice.</summary>
    private sealed class NativeTeardownRegistration(Gtk4WindowBackend owner, IDisposable teardown) : IDisposable
    {
        private IDisposable? _teardown = teardown;

        public void Dispose()
        {
            var current = _teardown;
            _teardown = null;

            if (current is null)
            {
                return;
            }

            owner._nativeTeardowns?.Remove(this);
            current.Dispose();
        }
    }

    internal void RaiseActivatedIfNeeded(bool active)
    {
        if (_active == active)
        {
            return;
        }

        _active = active;

        if (active)
        {
            // Becoming active is how a minimized window comes back: GTK4 does not tell us that the
            // window manager restored it, but it always restores focus with it.
            if (_state == XPlatWindowState.Minimized)
            {
                _state = XPlatWindowState.Normal;
                RaiseStateChangedIfNeeded();
            }

            _application.NotifyWindowActivated(this);
            _activated?.Invoke(this);
            return;
        }

        _application.NotifyWindowDeactivated(this);
        _deactivated?.Invoke(this);
    }

    /// <inheritdoc />
    public IDisposable AddShownHandler(Action<object> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        _shown += handler;
        return new XPlatHandlerToken<Action<object>>(RemoveShownHandler, handler);
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
        return new XPlatHandlerToken<Action<object>>(RemoveCloseRequestedHandler, handler);
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
        return new XPlatHandlerToken<Action<object>>(RemoveClosedHandler, handler);
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
        return new XPlatHandlerToken<Action<object>>(RemoveActivatedHandler, handler);
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
        return new XPlatHandlerToken<Action<object>>(RemoveDeactivatedHandler, handler);
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
        return new XPlatHandlerToken<Action<object>>(RemoveClientSizeChangedHandler, handler);
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
        return new XPlatHandlerToken<Action<object>>(RemoveScaleChangedHandler, handler);
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
        return new XPlatHandlerToken<Action<object>>(RemoveStateChangedHandler, handler);
    }

    /// <summary>Removes a handler that <see cref="AddStateChangedHandler"/> added.</summary>
    public void RemoveStateChangedHandler(Action<object> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        _stateChanged -= handler;
    }

    private bool EnsureCreated()
    {
        if (_window is not null)
        {
            return true;
        }

        if (_disposed || _application.NativeApplication is not { } native)
        {
            return false;
        }

        var window = Gtk.ApplicationWindow.New(native);
        window.Title = _title;
        window.Resizable = _resizable;
        window.SetDefaultSize(ToPixels(_clientSize.Width), ToPixels(_clientSize.Height));

        // Read once, the way the menu choice is: the option is a window-creation decision, and the
        // view cannot switch back to GL after it has presented through the CPU texture.
        var useHardwareAcceleration = _application.Application?.Options.GfxHardwareAcceleration ?? false;

        var view = new Gtk4SkiaView(_host, useHardwareAcceleration);

        // The drawing area lives in a vertical box so a per-window menu bar can be packed above it.
        var container = Gtk.Box.New(Gtk.Orientation.Vertical, 0);
        container.Append(view.Widget);
        window.SetChild(container);

        // Returning true keeps GTK from destroying the window behind the model's back.
        window.OnCloseRequest += (_, _) =>
        {
            Close();
            return true;
        };

        window.OnNotify += OnWindowNotify;

        view.AddMetricsChangedHandler(OnViewMetricsChanged);
        view.AddGraphicsPathChangedHandler(OnViewGraphicsPathChanged);

        _window = window;
        _container = container;
        _view = view;
        _presenter = _useXPlatMenus
            ? new XPlatMenuPresenter()
            : new Gtk4ContextMenuPresenter(view.Widget);

        _scale = view.ScaleFactor;
        _reportedState = CurrentState();

        ApplySizeLimits();
        ApplyMenuBar();
        ApplyWindowChrome();
        ApplyChrome();
        ApplyIcon();

        return true;
    }

    private void Present()
    {
        if (_window is null)
        {
            return;
        }

        ApplyChrome();
        ApplyWindowChrome();
        SetTransient(_owner);
        _window.Present();

        // A popup is a popover anchored to this window's drawing area, so the service can only be
        // installed once that widget exists. Like the context menu presenter it is process wide and
        // stateless: it resolves the owning window from the host it is handed.
        XPlatPopupService.Current = new Gtk4PopupService();

        // GTK reports the window as active only once it has been mapped, which is later than the
        // Shown event, so a menu opened from a Shown handler would otherwise find no presenter.
        // Moving between windows still hands the presenter to whichever one is active.
        XPlatContextMenuPresenter.Current = _presenter;

        // A state requested before the window existed still has to be honoured, and a window can
        // only be minimized or maximized once it has been mapped.
        if (_state != XPlatWindowState.Normal)
        {
            State = _state;
        }

        _view?.Focus();
        _shown?.Invoke(this);
    }

    private void SetTransient(IXPlatWindowBackend? owner)
    {
        if (_window is null)
        {
            return;
        }

        // Null clears the relationship, which is what re-parenting a live window needs; GTK keeps
        // the window manager in step without any extra call.
        _window.SetTransientFor(owner is Gtk4WindowBackend { Window: { } parent } ? parent : null);
    }

    private void ApplyChrome()
    {
        if (_darkChrome is not { } dark)
        {
            return;
        }

        // GTK keeps a single appearance setting for the whole process, so every window asks for the
        // same theme. They always agree, because the palette itself is global.
        if (_application.SystemTheme is Gtk4SystemTheme theme)
        {
            theme.ApplyToGtk(dark);
        }
    }

    /// <summary>
    /// <c>GTK_STYLE_PROVIDER_PRIORITY_APPLICATION</c>, one above it.
    /// </summary>
    /// <remarks>
    /// The binding does not expose GTK's priority constants, so the ABI value is used directly: 600 is
    /// the application's own priority, and a window's chrome has to beat both the theme below it and
    /// the library's palette beside it.
    /// </remarks>
    private const uint CssPriorityApplication = 601u;

    /// <summary>How much of the client the ring takes on each edge, in logical pixels.</summary>
    /// <remarks>
    /// Zero unless a borderless window was given a ring colour: GTK's ring is a CSS border on the
    /// window, and a border is laid out, so the child it holds is that much smaller.
    /// </remarks>
    private int BorderWidth =>
        _chrome == XPlatWindowChrome.Borderless && _borderColor is not null ? 1 : 0;

    /// <summary>
    /// Asks the window for the client the application wanted, plus whatever the ring takes.
    /// </summary>
    /// <remarks>
    /// A window's requested size is the whole window, decorations included, so a client of 900 with a
    /// one-pixel ring has to ask for 902. Without this the application would silently get 898 - the
    /// same two pixels per axis the spike measured when the ring was first drawn - and a client size
    /// would stop meaning what it says.
    /// </remarks>
    private void ApplyRequestedSize()
    {
        if (_window is null)
        {
            return;
        }

        var border = BorderWidth;
        _window.SetDefaultSize(
            ToPixels(_clientSize.Width) + (2 * border),
            ToPixels(_clientSize.Height) + (2 * border));
    }

    /// <summary>The radius a borderless window's content is clipped to, in logical pixels.</summary>
    /// <remarks>
    /// Stated in the stylesheet rather than inherited from the theme, because the two GTK versions
    /// measured for this feature default to different radii - about 10 logical pixels on 4.14 and
    /// about 12 on 4.22 - and an application that clips its own chrome has to be told which one it is
    /// drawing inside.
    /// </remarks>
    private const float BorderlessCornerRadius = 12f;

    /// <summary>What a window with GTK's own chrome reports: it owns nothing of its own.</summary>
    private static readonly XPlatWindowChromeMetrics SystemChromeMetrics = new();

    /// <summary>
    /// Puts the window into the shape its <see cref="Chrome"/> asks for: GTK's own title bar, or
    /// nothing where it would be.
    /// </summary>
    /// <remarks>
    /// A borderless window stays <em>decorated</em>. GTK's client-side decoration is what gives it the
    /// resize band, the shadow and the rounded corners, so removing it would take away all three; only
    /// the title bar goes, and the ring the decoration draws stays under the application's control
    /// through <see cref="BorderColor"/>.
    /// </remarks>
    private void ApplyWindowChrome()
    {
        if (_window is null || _disposed)
        {
            return;
        }

        if (_chrome == XPlatWindowChrome.Borderless)
        {
            _window.SetDecorated(true);
            _zeroTitlebar ??= Gtk.Box.New(Gtk.Orientation.Horizontal, 0);
            _window.SetTitlebar(_zeroTitlebar);
        }
        else
        {
            // Null is not "no title bar": GTK reads it as the default one, which is exactly what
            // System chrome means. The widget has to stay alive for the borderless case to come back.
            _window.SetTitlebar(null);
        }

        ApplyChromeCss();

        // The ring is part of the window, so the size has to be asked for with it included.
        ApplyRequestedSize();
        ApplySizeLimits();
    }

    /// <summary>Installs, rewrites or removes this window's stylesheet.</summary>
    private void ApplyChromeCss()
    {
        if (_window is null || _disposed)
        {
            return;
        }

        _chromeClass ??= "xplatui-window-"
            + (++_chromeClassCounter).ToString(CultureInfo.InvariantCulture);

        if (_chrome != XPlatWindowChrome.Borderless)
        {
            // A decorated window is the theme's business from top to bottom.
            _window.RemoveCssClass(_chromeClass);

            if (_chromeProvider is { } stale)
            {
                if (Gdk.Display.GetDefault() is { } staleDisplay)
                {
                    Gtk.StyleContext.RemoveProviderForDisplay(staleDisplay, stale);
                }

                _chromeProvider = null;
            }

            return;
        }

        if (Gdk.Display.GetDefault() is not { } display)
        {
            return;
        }

        _window.AddCssClass(_chromeClass);

        if (_chromeProvider is null)
        {
            _chromeProvider = Gtk.CssProvider.New();
            Gtk.StyleContext.AddProviderForDisplay(display, _chromeProvider, CssPriorityApplication);
        }

        _chromeProvider.LoadFromString(ChromeCss(_chromeClass));
    }

    /// <summary>
    /// The stylesheet a borderless window needs: the title bar collapsed to nothing, the corners
    /// clipped to a stated radius, and whichever colours the application asked for.
    /// </summary>
    /// <param name="cssClass">The class that scopes every rule to this window.</param>
    /// <returns>The stylesheet.</returns>
    private string ChromeCss(string cssClass)
    {
        var css = new StringBuilder();

        // Which of these nodes GTK uses for the title bar is theme dependent, so all of them are
        // collapsed. The widget alone is not enough - measured, a zero-measure widget without this
        // still reserved 46 logical pixels, because the theme's min-height applies to the node it
        // sits in - and this without the widget is not enough either, so both are needed.
        css.AppendLine($"window.{cssClass} titlebar,");
        css.AppendLine($"window.{cssClass} .titlebar,");
        css.AppendLine($"window.{cssClass} headerbar,");
        css.AppendLine($"window.{cssClass} .headerbar,");
        css.AppendLine($"window.{cssClass} windowtitlebar,");
        css.AppendLine($"window.{cssClass} windowtitlebar > * {{");
        css.AppendLine("  min-height: 0;");
        css.AppendLine("  min-width: 0;");
        css.AppendLine("  padding: 0;");
        css.AppendLine("  margin: 0;");
        css.AppendLine("  border: 0;");
        css.AppendLine("  background: none;");
        css.AppendLine("  box-shadow: none;");
        css.AppendLine("}");

        css.AppendLine($"window.{cssClass}.csd {{ border-radius: {BorderlessCornerRadius:0.###}px; }}");

        // Maximised and fullscreen windows are square: the compositor squares the corners it owns,
        // and a radius here would clip the content to a shape the window no longer has.
        css.AppendLine($"window.{cssClass}.csd.maximized,");
        css.AppendLine($"window.{cssClass}.csd.fullscreen {{ border-radius: 0; }}");

        if (_borderColor is { } border)
        {
            // Laid out, so it is outside the child and nothing can paint over it. The window is asked
            // for two pixels more than the client to pay for it - see ApplyRequestedSize.
            css.AppendLine($"window.{cssClass}.csd {{ border: {BorderWidth}px solid {CssColor(border)}; }}");
        }

        if (_captionColor is { } caption)
        {
            // The window's own surface, which is what shows through the corner arcs the content is
            // clipped to.
            css.AppendLine($"window.{cssClass}.csd {{ background-color: {CssColor(caption)}; }}");
        }

        return css.ToString();
    }

    /// <summary>One colour in GTK's syntax, alpha only when it is not opaque.</summary>
    private static string CssColor(SKColor color) => color.Alpha == 255
        ? $"#{color.Red:X2}{color.Green:X2}{color.Blue:X2}"
        : FormattableString.Invariant($"rgba({color.Red}, {color.Green}, {color.Blue}, {color.Alpha / 255.0:0.###})");

    private void RaiseChromeMetricsChanged() => _chromeMetricsChanged?.Invoke(this);

    /// <summary>Hands the icon's theme name to the window manager, which is all GTK4 accepts.</summary>
    private void ApplyIcon()
    {
        if (_window is not null && _icon?.Name is { Length: > 0 } name)
        {
            _window.SetIconName(name);
        }
    }

    private void ApplySizeLimits()
    {
        // GTK's size request is the minimum size a widget will accept, and it applies to the window,
        // so the ring has to be added the same way it is for the requested size.
        var border = BorderWidth;
        _window?.SetSizeRequest(
            ToPixels(_minimumClientSize.Width) + (2 * border),
            ToPixels(_minimumClientSize.Height) + (2 * border));
    }

    private void ApplyMenuBar()
    {
        if (_useXPlatMenus)
        {
            ApplyXPlatMenuBar();
            return;
        }

        if (_container is null)
        {
            return;
        }

        if (_menuBar is null || _menuBar.Menus.Count == 0)
        {
            if (_menuBarAttached && _menuBarWidget is not null)
            {
                _container.Remove(_menuBarWidget.Widget);
                _menuBarAttached = false;
            }

            _menuBarWidget?.Dispose();
            _menuBarWidget = null;
            return;
        }

        _menuBarWidget ??= new Gtk4MenuBar();
        _menuBarWidget.SetMenus(_menuBar);

        if (!_menuBarAttached)
        {
            _container.Prepend(_menuBarWidget.Widget);
            _menuBarAttached = true;
        }
    }

    /// <summary>
    /// Puts the bar inside the Skia surface, so GTK4 draws the same strip Windows does. The host
    /// owns it, which means it needs no native widget and survives the window being re-presented.
    /// </summary>
    private void ApplyXPlatMenuBar()
    {
        if (_menuBar is null || _menuBar.Menus.Count == 0)
        {
            _host.MenuBar = null;
            return;
        }

        // Re-point an existing view rather than replacing it, so a bar that is filled in after it is
        // assigned keeps the open-menu state.
        if (_host.MenuBar is { } existing)
        {
            existing.MenuBar = _menuBar;
            return;
        }

        _host.MenuBar = new XPlatMenuBarView(_menuBar);
    }

    private void OnViewMetricsChanged(object sender)
    {
        if (_view is null || _disposed)
        {
            return;
        }

        var width = _view.Width;
        var height = _view.Height;
        if (width <= 0 || height <= 0)
        {
            return;
        }

        var sizeChanged = Math.Abs(width - _clientSize.Width) > 0.5f || Math.Abs(height - _clientSize.Height) > 0.5f;
        _clientSize = new SKSize(width, height);

        var scale = _view.ScaleFactor;
        var scaleChanged = Math.Abs(scale - _scale) > 0.001f;
        _scale = scale;

        if (sizeChanged)
        {
            _clientSizeChanged?.Invoke(this);
        }

        if (scaleChanged)
        {
            _scaleChanged?.Invoke(this);
            RaiseChromeMetricsChanged();
        }

        RaiseStateChangedIfNeeded();
    }

    private void OnWindowNotify(GObject.Object sender, GObject.Object.NotifySignalArgs args)
    {
        switch (args.Pspec?.GetName())
        {
            case "is-active":
                RaiseActivatedIfNeeded(_window?.GetIsActive() ?? false);
                break;

            case "maximized" or "fullscreened":
                RaiseStateChangedIfNeeded();
                break;
        }
    }

    private XPlatWindowState CurrentState()
    {
        if (_window is null)
        {
            return _state;
        }

        if (_window.IsFullscreen())
        {
            return XPlatWindowState.FullScreen;
        }

        if (_window.IsMaximized())
        {
            return XPlatWindowState.Maximized;
        }

        return _state == XPlatWindowState.Minimized ? XPlatWindowState.Minimized : XPlatWindowState.Normal;
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

        // The corner radius and the outline both depend on whether the window is maximised, so a
        // state change is a chrome metrics change as well.
        RaiseChromeMetricsChanged();
    }

    private static int ToPixels(float value) => (int)MathF.Round(MathF.Max(value, 0f));

    /// <inheritdoc />
    public void RunWindowHooks(XPlatWindowPhase phase, XPlatWindow window)
    {
        ArgumentNullException.ThrowIfNull(window);

        foreach (var hook in window.Services.GetServices<IXPlatGtk4WindowHook>())
        {
            hook.OnPhase(phase, window, this);
        }
    }
}
