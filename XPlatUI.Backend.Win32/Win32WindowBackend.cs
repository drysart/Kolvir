using System.Globalization;
using System.Runtime.InteropServices;
using Microsoft.Extensions.DependencyInjection;
using SkiaSharp;

namespace XPlatUI.Backend.Win32;

/// <summary>
/// One Win32 window, built directly on <c>user32</c> so nothing here needs Windows Forms or
/// System.Drawing and the whole backend stays trimmable and NativeAOT friendly.
/// </summary>
/// <remarks>
/// <para>
/// The window owns an <see cref="XPlatUiHost"/>, rasterizes the control tree into a Skia surface
/// backed by a GDI DIB section, and forwards pointer, key and text input straight into the host.
/// The model never sees input; it only sees the events raised here.
/// </para>
/// <para>
/// The native window is created lazily, on the first <see cref="Show"/> or
/// <see cref="ShowDialog"/>: <see cref="XPlatWindow"/> constructs the backend before the caller
/// has had a chance to set <c>Title</c>, <c>ClientSize</c> or <c>Resizable</c>, so there is nothing
/// worth creating a window for yet.
/// </para>
/// <para>
/// Each window registers its own window class, because Win32 keeps the class name process-global
/// and a per-window class is the only way to give two windows different icons or styles.
/// </para>
/// </remarks>
public sealed class Win32WindowBackend : IXPlatWindowBackend
{
    private Action<object>? _shown;
    private Action<object>? _closeRequested;
    private Action<object>? _closed;
    private Action<object>? _activated;
    private Action<object>? _deactivated;
    private Action<object>? _clientSizeChanged;
    private Action<object>? _scaleChanged;
    private Action<object>? _stateChanged;
    private static int _nextClassId;

    private readonly XPlatUiHost _host = new();
    private readonly Win32ApplicationBackend _application;
    private readonly NativeMethods.WndProc _wndProc;
    private readonly Dictionary<XPlatCursor, IntPtr> _cursors = new();
    private readonly string _className;

    private IntPtr _hwnd;
    private IntPtr _moduleHandle;
    private bool _classRegistered;
    private bool _disposed;
    private List<NativeTeardownRegistration>? _nativeTeardowns;

    private string _title = string.Empty;
    private SKSize _clientSize = new(800f, 600f);
    private SKSize _minimumClientSize;
    private SKSize _maximumClientSize;
    private SKPoint? _location;
    private bool _resizable = true;
    private bool _topMost;
    private IXPlatWindowBackend? _owner;
    private XPlatWindowState _state = XPlatWindowState.Normal;
    private XPlatMenuBar? _menuBar;
    private readonly bool _useXPlatMenus;
    private readonly bool _useHardwareAcceleration;

    private XPlatIcon? _icon;
    private IntPtr _bigIcon;
    private IntPtr _smallIcon;

    private float _scale = 1f;

    private XPlatWindowChrome _chrome = XPlatWindowChrome.System;

    /// <summary>Frame insets measured from the client rect <c>DefWindowProc</c> computed, in device
    /// pixels. Left and right are the same number; the bottom is <see cref="_frameHeight"/>.</summary>
    private int _frameWidth;
    private int _frameHeight;

    /// <summary>The resize band reported by the hit test, zeroed while maximized because the screen
    /// edges are not resizable.</summary>
    private int _hitBandWidth;
    private int _hitBandHeight;
    private Action<object>? _chromeMetricsChanged;

    /// <summary>The snapshot for a decorated window: the platform owns nothing the seam reports.</summary>
    private static readonly XPlatWindowChromeMetrics SystemMetrics = new();
    private IntPtr _memoryDc;
    private IntPtr _dib;
    private IntPtr _dibBits;
    private IntPtr _previousBitmap;
    private int _bufferWidth;
    private int _bufferHeight;
    private SKSurface? _skia;
    private int _bufferGeneration;
    private int _paintedGeneration;

    private Win32GlSurface? _gl;
    private bool _glUnavailable;
    private string _gfxReason = "cpu (not started)";

    private bool _trackingLeave;
    private long _lastClickTime;
    private int _clickCount;
    private int _lastClickX;
    private int _lastClickY;

    internal Win32WindowBackend(Win32ApplicationBackend application, XPlatWindow window)
    {
        ArgumentNullException.ThrowIfNull(application);
        ArgumentNullException.ThrowIfNull(window);

        _application = application;

        // The delegate must stay rooted for as long as the window class points at it.
        _wndProc = WindowProc;

        // One class per window: the name has to be unique process-wide, and UnregisterClassW in
        // Dispose would otherwise pull the class out from under a sibling window.
        _className = "XPlatUI.Window." + Interlocked.Increment(ref _nextClassId).ToString(CultureInfo.InvariantCulture);

        _host.AddInvalidatedHandler(_ => InvalidatePending());
        _host.AddCursorChangedHandler(_ => ApplyCursor());
        XPlatTheme.AddChangedHandler(OnThemeChanged);

        // The window keeps a drawing buffer between frames and copies it to the screen with
        // BitBlt, so only the part that changed has to be painted and copied.
        _host.PartialUpdates = true;

        // Resolved once, when the window is created: the application's presentation choice does not
        // change while a window is open, and the presenters are installed from it.
        _useXPlatMenus = !(application.Application?.Options.MenuPresentation ?? XPlatMenuPresentation.Auto)
            .UsesNativeMenus();

        // Read once, the way the menu choice is: the option is a window-creation decision, and the
        // surface cannot switch between GL and the DIB after the window has allocated one.
        _useHardwareAcceleration = application.Application?.Options.GfxHardwareAcceleration ?? false;

        if (!_useHardwareAcceleration)
        {
            _glUnavailable = true;
            _gfxReason = "cpu (disabled)";
        }
    }

    /// <inheritdoc />
    public XPlatUiHost Host => _host;

    /// <summary>
    /// What this window is presenting with, for a one-line diagnostic: <c>gl</c> when the OpenGL
    /// surface came up, or <c>cpu (...)</c> naming why it did not. It is decided when the native
    /// window allocates its drawing surface, so it is meaningful from <c>Shown</c> onwards.
    /// </summary>
    public string GraphicsPath => _gl is not null ? "gl" : _gfxReason;

    /// <summary>
    /// The GL vendor and renderer strings when <see cref="GraphicsPath"/> is <c>gl</c>, or null on
    /// the DIB path. Diagnostic only, and the same lifetime rule as the path.
    /// </summary>
    public string? GraphicsRenderer => _gl?.Renderer;

    /// <inheritdoc />
    public float ScaleFactor => _scale;

    /// <inheritdoc />
    public XPlatScreen? Screen => _hwnd == IntPtr.Zero
        ? _application.PrimaryScreen
        : Win32ApplicationBackend.ScreenForWindow(_hwnd);

    /// <inheritdoc />
    public string Title
    {
        get => _title;
        set
        {
            _title = value ?? string.Empty;

            if (_hwnd != IntPtr.Zero)
            {
                NativeMethods.SetWindowTextW(_hwnd, _title);
            }
        }
    }

    /// <inheritdoc />
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

            if (_hwnd == IntPtr.Zero)
            {
                return;
            }

            var outer = OuterSizeFor(value.Width, value.Height);
            NativeMethods.SetWindowPos(
                _hwnd,
                IntPtr.Zero,
                0,
                0,
                outer.Width,
                outer.Height,
                NativeMethods.SwpNoMove | NativeMethods.SwpNoZOrder | NativeMethods.SwpNoActivate);
        }
    }

    /// <inheritdoc />
    public SKSize MinimumClientSize
    {
        get => _minimumClientSize;
        set
        {
            _minimumClientSize = value;

            // Win32 asks for the limit on every drag, so nothing has to be pushed to the window.
        }
    }

    /// <inheritdoc />
    public SKSize MaximumClientSize
    {
        get => _maximumClientSize;
        set
        {
            _maximumClientSize = value;
        }
    }

    /// <inheritdoc />
    public SKPoint Location
    {
        get
        {
            if (_hwnd != IntPtr.Zero && NativeMethods.GetWindowRect(_hwnd, out var rect))
            {
                return new SKPoint(rect.Left / _scale, rect.Top / _scale);
            }

            return _location ?? SKPoint.Empty;
        }
        set
        {
            _location = value;

            if (_hwnd == IntPtr.Zero)
            {
                return;
            }

            NativeMethods.SetWindowPos(
                _hwnd,
                IntPtr.Zero,
                (int)MathF.Round(value.X * _scale),
                (int)MathF.Round(value.Y * _scale),
                0,
                0,
                NativeMethods.SwpNoSize | NativeMethods.SwpNoZOrder | NativeMethods.SwpNoActivate);
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
            ApplyResizable();
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

            if (_hwnd == IntPtr.Zero)
            {
                return;
            }

            ApplyTopMost();
        }
    }

    /// <summary>
    /// The window's display state. <see cref="XPlatWindowState.FullScreen"/> maps to a maximized
    /// window for now; true borderless full screen needs a style save/restore that is not worth
    /// carrying until a control asks for it.
    /// </summary>
    public XPlatWindowState State
    {
        get => _hwnd == IntPtr.Zero ? _state : CurrentState();
        set
        {
            _state = value;

            if (_hwnd == IntPtr.Zero)
            {
                return;
            }

            var show = value switch
            {
                XPlatWindowState.Minimized => NativeMethods.SwMinimize,
                XPlatWindowState.Maximized or XPlatWindowState.FullScreen => NativeMethods.SwMaximize,
                _ => NativeMethods.SwRestore,
            };

            NativeMethods.ShowWindow(_hwnd, show);
        }
    }

    /// <summary>
    /// The window's menu bar, or null for none. When the application uses the library's own menus
    /// the bar is drawn inside the window, above the control tree; with native menus it is left to
    /// the platform, which on Windows draws no bar at all.
    /// </summary>
    public XPlatMenuBar? MenuBar
    {
        get => _menuBar;
        set
        {
            _menuBar = value;
            UpdateMenuBarView();
        }
    }

    /// <summary>The native window handle, or <see cref="IntPtr.Zero"/> before the window is shown.</summary>
    public IntPtr Handle => _hwnd;

    /// <summary>
    /// How much of the window the platform draws. Switching it live re-runs the frame calculation,
    /// which is what grows the client over the caption or hands it back.
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

            if (_hwnd != IntPtr.Zero && !_disposed)
            {
                ApplyChrome();
            }
        }
    }

    /// <summary>
    /// Borderless chrome is implemented here; caption buttons and window colours arrive with their own
    /// steps and are reported as unsupported until then.
    /// </summary>
    public XPlatChromeCapabilities ChromeCapabilities => new() { Borderless = true };

    /// <summary>
    /// What the platform still owns in this window. Windows keeps its frame on three edges and hands
    /// the client the top edge, so the painted inset and the claimed band are different numbers.
    /// </summary>
    public XPlatWindowChromeMetrics ChromeMetrics =>
        ChromeMetricsFor(
            _chrome == XPlatWindowChrome.Borderless,
            _frameWidth,
            _frameHeight,
            _scale,
            _state == XPlatWindowState.Maximized);

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
    private sealed class NativeTeardownRegistration(Win32WindowBackend owner, IDisposable teardown) : IDisposable
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

            if (_hwnd == IntPtr.Zero)
            {
                return;
            }

            // The owner is part of what the shell sees, so the frame is recomputed rather than just
            // the z-order adjusted: without it the taskbar keeps the old grouping.
            NativeMethods.SetWindowLongPtrW(_hwnd, NativeMethods.GwlpHwndParent, OwnerHandle);
            NativeMethods.SetWindowPos(
                _hwnd,
                IntPtr.Zero,
                0,
                0,
                0,
                0,
                NativeMethods.SwpNoMove |
                NativeMethods.SwpNoSize |
                NativeMethods.SwpNoZOrder |
                NativeMethods.SwpNoActivate |
                NativeMethods.SwpFrameChanged);
        }
    }

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

        var ownerHandle = OwnerHandle;

        if (ownerHandle != IntPtr.Zero)
        {
            NativeMethods.EnableWindow(ownerHandle, false);
        }

        try
        {
            Present();

            // The nested loop ends when this window is destroyed, which is the same signal
            // XPlatWindow uses to decide whether the dialog ever reported a close.
            _application.RunNestedLoop(() => _hwnd != IntPtr.Zero);
        }
        finally
        {
            if (ownerHandle != IntPtr.Zero)
            {
                NativeMethods.EnableWindow(ownerHandle, true);
            }
        }
    }

    /// <inheritdoc />
    public void Activate()
    {
        if (_hwnd == IntPtr.Zero)
        {
            return;
        }

        // A minimized window ignores SetForegroundWindow, so restore it first the way the taskbar
        // does; a second launch of the application must not leave the window it brought forward
        // still minimised.
        if (NativeMethods.IsIconic(_hwnd))
        {
            NativeMethods.ShowWindow(_hwnd, NativeMethods.SwRestore);
        }

        if (NativeMethods.SetForegroundWindow(_hwnd))
        {
            NativeMethods.SetFocus(_hwnd);
            return;
        }

        // Windows refuses a foreground change from a process that does not own the foreground.
        // Flashing the taskbar button is what is left, and it keeps the request visible.
        var flash = new NativeMethods.FLASHWINFO
        {
            cbSize = (uint)Marshal.SizeOf<NativeMethods.FLASHWINFO>(),
            hwnd = _hwnd,
            dwFlags = NativeMethods.FlashwAll | NativeMethods.FlashwTimerNoForeground,
            uCount = uint.MaxValue,
        };

        NativeMethods.FlashWindowEx(ref flash);
    }

    /// <inheritdoc />
    public void Close()
    {
        if (!_disposed)
        {
            _closeRequested?.Invoke(this);
        }
    }

    /// <inheritdoc />
    public void SetTitleBarAppearance(bool dark) => NativeMethods.SetDarkTitleBar(_hwnd, dark);

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
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        XPlatTheme.RemoveChangedHandler(OnThemeChanged);

        // Whatever the window registered natively has to let go while the window still exists: the
        // platform keeps calling into a registered object for as long as the window is alive.
        ReleaseNativeTeardowns();

        // The context belongs to the window's device context, so it has to go before the window
        // that owns that DC is destroyed.
        ReleaseGl();

        if (_hwnd != IntPtr.Zero)
        {
            NativeMethods.DestroyWindow(_hwnd);
            _hwnd = IntPtr.Zero;
        }

        ReleaseIcons();
        ReleaseBuffer();

        if (_classRegistered)
        {
            NativeMethods.UnregisterClassW(_className, _moduleHandle);
            _classRegistered = false;
        }

        // System cursors are shared, so the cached handles are dropped without being destroyed.
        _cursors.Clear();

        _application.RemoveWindow(this);
        _closed?.Invoke(this);
    }

    /// <summary>Forwards a key press to the tree; returns true when the UI consumed it.</summary>
    internal bool TryHandleKeyDown(IntPtr wParam)
    {
        var key = MapKey((int)wParam);
        var modifiers = CurrentModifiers();
        return key != XPlatKey.None && _host.ProcessKeyDown(key, modifiers);
    }

    /// <summary>Maps a logical client point to physical screen pixels, for native menus.</summary>
    internal NativeMethods.POINT LogicalToScreen(SKPoint point)
    {
        var screen = new NativeMethods.POINT
        {
            X = (int)MathF.Round(point.X * _scale),
            Y = (int)MathF.Round(point.Y * _scale),
        };

        NativeMethods.ClientToScreen(_hwnd, ref screen);
        return screen;
    }

    /// <summary>The native handle of the owning window, or <see cref="IntPtr.Zero"/> when there is none.</summary>
    private IntPtr OwnerHandle => (_owner as Win32WindowBackend)?._hwnd ?? IntPtr.Zero;

    private bool EnsureCreated()
    {
        if (_hwnd != IntPtr.Zero)
        {
            return true;
        }

        if (_disposed)
        {
            return false;
        }

        _moduleHandle = NativeMethods.GetModuleHandleW(null);

        var windowClass = new NativeMethods.WNDCLASSEXW
        {
            cbSize = (uint)Marshal.SizeOf<NativeMethods.WNDCLASSEXW>(),
            style = NativeMethods.CsHRedraw | NativeMethods.CsVRedraw | NativeMethods.CsDblClks,
            lpfnWndProc = Marshal.GetFunctionPointerForDelegate(_wndProc),
            hInstance = _moduleHandle,
            lpszClassName = _className,
        };

        var icon = NativeMethods.LoadIconW(IntPtr.Zero, new IntPtr(NativeMethods.IdiApplication));
        windowClass.hIcon = icon;
        windowClass.hIconSm = icon;

        if (NativeMethods.RegisterClassExW(ref windowClass) == 0)
        {
            return false;
        }

        _classRegistered = true;

        // Create the window first: its DPI can only be queried once it exists, and sizing against
        // the wrong scale would crop the logical layout on high-DPI displays.
        var provisional = OuterSizeFor(_clientSize.Width, _clientSize.Height);

        _hwnd = NativeMethods.CreateWindowExW(
            0,
            _className,
            _title,
            StyleFor(),
            0,
            0,
            Math.Max(provisional.Width, 1),
            Math.Max(provisional.Height, 1),
            OwnerHandle,
            IntPtr.Zero,
            _moduleHandle,
            IntPtr.Zero);

        if (_hwnd == IntPtr.Zero)
        {
            if (_classRegistered)
            {
                NativeMethods.UnregisterClassW(_className, _moduleHandle);
                _classRegistered = false;
            }

            return false;
        }

        _scale = ScaleOf(NativeMethods.GetDpiForWindow(_hwnd));
        PlaceInitially();
        ApplyResizable();
        ApplyIcon();

        // Allocate the drawing surface now rather than waiting for the first WM_SIZE/WM_PAINT. The
        // presentation path is a property of the window, and a caller that wants to report it - the
        // demo's gfx line, a diagnostic - should not have to wait for a message to arrive.
        if (NativeMethods.GetClientRect(_hwnd, out var client) && client.Right > 0 && client.Bottom > 0)
        {
            EnsureBuffer(client.Right, client.Bottom);
        }

        UpdateHostSize();
        return true;
    }

    private void Present()
    {
        NativeMethods.SetDarkTitleBar(_hwnd, XPlatTheme.IsDarkMode);

        // The presenters need a live HWND, so they can only be installed once the window exists.
        XPlatContextMenuPresenter.Current = _useXPlatMenus
            ? new XPlatMenuPresenter()
            : new Win32ContextMenuPresenter(_hwnd, LogicalToScreen);
        XPlatPopupService.Current = new Win32PopupService();

        UpdateMenuBarView();

        NativeMethods.ShowWindow(_hwnd, NativeMethods.SwShow);
        NativeMethods.UpdateWindow(_hwnd);

        // Windows ignores a z-order change on a hidden window, so topmost can only be applied once
        // the window is visible - which is why it is not done when the window is created.
        ApplyTopMost();

        _shown?.Invoke(this);
    }

    /// <summary>
    /// Puts the window's menu bar into the host, or takes it out, as the menu presentation requires.
    /// </summary>
    /// <remarks>
    /// Called when the bar is assigned and again when the window is presented, because the bar can
    /// arrive either side of the window being shown. An existing view is kept and re-pointed rather
    /// than replaced, so a bar that is filled in after it is assigned keeps its open-menu state.
    /// </remarks>
    private void UpdateMenuBarView()
    {
        if (!_useXPlatMenus || _menuBar is null)
        {
            _host.MenuBar = null;
            return;
        }

        if (_host.MenuBar is { } existing)
        {
            existing.MenuBar = _menuBar;
            return;
        }

        _host.MenuBar = new XPlatMenuBarView(_menuBar);
    }

    /// <summary>
    /// Puts the window in the topmost band, or takes it out of it. Only has an effect once the
    /// window is visible.
    /// </summary>
    /// <remarks>
    /// Leaving the band needs <c>HWND_NOTOPMOST</c>; <c>HWND_TOP</c> would only move the window to
    /// the top of the band it is already in, so turning <see cref="TopMost"/> off had no effect.
    /// </remarks>
    private void ApplyTopMost() =>
        NativeMethods.SetWindowPos(
            _hwnd,
            _topMost ? NativeMethods.HwndTopMost : NativeMethods.HwndNoTopMost,
            0,
            0,
            0,
            0,
            NativeMethods.SwpNoMove | NativeMethods.SwpNoSize | NativeMethods.SwpNoActivate);

    /// <summary>Puts the window on the primary display's working area, honouring an explicit location.</summary>
    private void PlaceInitially()
    {
        var outer = OuterSizeFor(_clientSize.Width, _clientSize.Height);

        int x;
        int y;

        if (_location is { } location)
        {
            x = (int)MathF.Round(location.X * _scale);
            y = (int)MathF.Round(location.Y * _scale);
        }
        else
        {
            var work = NativeMethods.GetPrimaryWorkArea();
            x = work.Left + Math.Max(((work.Right - work.Left) - outer.Width) / 2, 0);
            y = work.Top + Math.Max(((work.Bottom - work.Top) - outer.Height) / 2, 0);
        }

        NativeMethods.SetWindowPos(
            _hwnd,
            IntPtr.Zero,
            x,
            y,
            outer.Width,
            outer.Height,
            NativeMethods.SwpNoZOrder | NativeMethods.SwpNoActivate);
    }

    private void ApplyResizable()
    {
        if (_hwnd == IntPtr.Zero)
        {
            return;
        }

        var style = (uint)(long)NativeMethods.GetWindowLongPtrW(_hwnd, NativeMethods.GwlStyle);
        const uint frame = NativeMethods.WsThickFrame | NativeMethods.WsMaximizeBox | NativeMethods.WsMinimizeBox;
        style = _resizable ? style | frame : style & ~frame;

        NativeMethods.SetWindowLongPtrW(_hwnd, NativeMethods.GwlStyle, new IntPtr(style));
        NativeMethods.SetWindowPos(
            _hwnd,
            IntPtr.Zero,
            0,
            0,
            0,
            0,
            NativeMethods.SwpNoMove | NativeMethods.SwpNoSize | NativeMethods.SwpNoZOrder |
            NativeMethods.SwpNoActivate | NativeMethods.SwpFrameChanged);
    }

    private uint StyleFor() => _resizable
        ? NativeMethods.WsOverlappedWindow
        : NativeMethods.WsOverlappedWindow & ~(NativeMethods.WsThickFrame | NativeMethods.WsMaximizeBox | NativeMethods.WsMinimizeBox);

    /// <summary>
    /// Rebuilds the window's two icon handles, each sized from the window's DPI, and hands them to
    /// the shell: the small one is the title bar, the big one the taskbar and Alt-Tab. A window
    /// with no icon of its own keeps the window class's generic application icon.
    /// </summary>
    private void ApplyIcon()
    {
        if (_hwnd == IntPtr.Zero || _disposed)
        {
            return;
        }

        var dpi = NativeMethods.GetDpiForWindow(_hwnd);
        var big = CreateIconHandle(NativeMethods.GetSystemMetricsForDpi(NativeMethods.SmCxIcon, dpi));
        var small = CreateIconHandle(NativeMethods.GetSystemMetricsForDpi(NativeMethods.SmCxSmIcon, dpi));

        if (big == IntPtr.Zero || small == IntPtr.Zero)
        {
            // A half-replaced pair is worse than none, so both handles are dropped and the class
            // icon stays in place.
            DestroyIconHandle(big);
            DestroyIconHandle(small);
            return;
        }

        var previousBig = _bigIcon;
        var previousSmall = _smallIcon;
        _bigIcon = big;
        _smallIcon = small;

        NativeMethods.SendMessageW(_hwnd, NativeMethods.WmSetIcon, new IntPtr(NativeMethods.IconBig), big);
        NativeMethods.SendMessageW(_hwnd, NativeMethods.WmSetIcon, new IntPtr(NativeMethods.IconSmall), small);

        // The shell has already switched to the new handles, so the old ones are free to go.
        DestroyIconHandle(previousBig);
        DestroyIconHandle(previousSmall);
    }

    private void ReleaseIcons()
    {
        DestroyIconHandle(_bigIcon);
        DestroyIconHandle(_smallIcon);
        _bigIcon = IntPtr.Zero;
        _smallIcon = IntPtr.Zero;
    }

    private static void DestroyIconHandle(IntPtr handle)
    {
        if (handle != IntPtr.Zero)
        {
            NativeMethods.DestroyIcon(handle);
        }
    }

    /// <summary>Builds an <c>HICON</c> at <paramref name="size"/> pixels square, or <see cref="IntPtr.Zero"/>.</summary>
    private IntPtr CreateIconHandle(int size)
    {
        if (size <= 0 || _icon?.GetBitmap(size) is not { } bitmap)
        {
            return IntPtr.Zero;
        }

        var info = new NativeMethods.BITMAPINFO
        {
            bmiHeader = new NativeMethods.BITMAPINFOHEADER
            {
                biSize = (uint)Marshal.SizeOf<NativeMethods.BITMAPINFOHEADER>(),
                biWidth = size,
                // Negative height makes the DIB top-down, which is the row order Skia uses.
                biHeight = -size,
                biPlanes = 1,
                biBitCount = 32,
                biCompression = NativeMethods.BiRgb,
            },
        };

        var color = NativeMethods.CreateDIBSection(
            IntPtr.Zero,
            ref info,
            NativeMethods.DibRgbColors,
            out var bits,
            IntPtr.Zero,
            0);

        if (color == IntPtr.Zero)
        {
            return IntPtr.Zero;
        }

        // XPlatIcon hands out tightly packed premultiplied BGRA. GDI blends an icon's colour bitmap
        // as straight alpha, so the premultiplied bytes have to be expanded back or every partly
        // transparent pixel - which is every antialiased edge - comes out too dark.
        var pixels = bitmap.GetPixelSpan();
        var colorBytes = new byte[size * size * 4];

        // The monochrome mask repeats the alpha channel: a set bit means "show the desktop here".
        // Windows blends the colour bitmap's alpha itself, but the mask is what an icon falls back
        // to wherever alpha is ignored.
        var maskStride = ((size + 15) / 16) * 2;
        var maskBytes = new byte[maskStride * size];

        for (var y = 0; y < size; y++)
        {
            for (var x = 0; x < size; x++)
            {
                var offset = ((y * size) + x) * 4;
                var alpha = pixels[offset + 3];

                if (alpha == 0)
                {
                    maskBytes[(y * maskStride) + (x / 8)] |= (byte)(0x80 >> (x % 8));
                    continue;
                }

                colorBytes[offset] = Unpremultiply(pixels[offset], alpha);
                colorBytes[offset + 1] = Unpremultiply(pixels[offset + 1], alpha);
                colorBytes[offset + 2] = Unpremultiply(pixels[offset + 2], alpha);
                colorBytes[offset + 3] = alpha;
            }
        }

        Marshal.Copy(colorBytes, 0, bits, colorBytes.Length);

        var mask = NativeMethods.CreateBitmap(size, size, 1, 1, maskBytes);
        if (mask == IntPtr.Zero)
        {
            NativeMethods.DeleteObject(color);
            return IntPtr.Zero;
        }

        var iconInfo = new NativeMethods.ICONINFO
        {
            fIcon = 1,
            hbmMask = mask,
            hbmColor = color,
        };

        var handle = NativeMethods.CreateIconIndirect(ref iconInfo);

        NativeMethods.DeleteObject(mask);
        NativeMethods.DeleteObject(color);

        return handle;
    }

    /// <summary>Expands one premultiplied colour channel back to straight alpha.</summary>
    private static byte Unpremultiply(byte value, byte alpha) =>
        (byte)Math.Min(255, ((value * 255) + (alpha / 2)) / alpha);

    private XPlatWindowState CurrentState()
    {
        if (NativeMethods.IsIconic(_hwnd))
        {
            return XPlatWindowState.Minimized;
        }

        return NativeMethods.IsZoomed(_hwnd) ? XPlatWindowState.Maximized : XPlatWindowState.Normal;
    }

    private IntPtr WindowProc(IntPtr hwnd, uint message, IntPtr wParam, IntPtr lParam)
    {
        try
        {
            return HandleMessage(hwnd, message, wParam, lParam);
        }
        catch (Exception ex)
        {
            // An exception must never unwind into DispatchMessage: the native frame below this
            // delegate turns an escaping exception into a fail-fast with no hook at all.
            XPlatApplication.HandleNativeBoundary(ex);
            return IntPtr.Zero;
        }
    }

    private IntPtr HandleMessage(IntPtr hwnd, uint message, IntPtr wParam, IntPtr lParam)
    {
        // Messages raised while CreateWindowExW is still running arrive before _hwnd is assigned.
        if (_hwnd == IntPtr.Zero)
        {
            _hwnd = hwnd;
        }

        // A press on the window that dismisses a popup belongs to that popup: it closes it and
        // stops there, so the control underneath does not also act on it. That is what makes
        // clicking the combo box that owns an open drop-down close it rather than reopen it, and it
        // is what the overlay presentation gets from its dismiss catcher. A wheel turn and a move
        // or a resize are not presses, so they dismiss and are still delivered.
        switch (message)
        {
            case NativeMethods.WmLButtonDown:
            case NativeMethods.WmRButtonDown:
            case NativeMethods.WmMButtonDown:
                if (Win32PopupWindow.DismissForOwner(hwnd))
                {
                    return IntPtr.Zero;
                }

                break;

            case NativeMethods.WmMouseWheel:
            case NativeMethods.WmMouseHWheel:
            case NativeMethods.WmMove:
            case NativeMethods.WmSize:
                Win32PopupWindow.DismissForOwner(hwnd);
                break;

            case NativeMethods.WmActivate:
                if (NativeMethods.LowWord(wParam) == NativeMethods.WaInactive)
                {
                    Win32PopupWindow.DismissForOwner(hwnd);
                }

                break;
        }

        switch (message)
        {
            case NativeMethods.WmPaint:
                OnPaint(hwnd);
                return IntPtr.Zero;

            case NativeMethods.WmPrintClient:
                // Lets the window be captured into an arbitrary DC (PrintWindow, screenshot tools,
                // automated visual tests) using the same back buffer that WM_PAINT blits from.
                OnPrintClient(wParam);
                return IntPtr.Zero;

            case NativeMethods.WmEraseBkgnd:
                // The Skia back buffer always covers the whole client area.
                return new IntPtr(1);

            case NativeMethods.WmSize:
                OnSize(NativeMethods.LowWord(lParam), NativeMethods.HighWord(lParam), (uint)wParam);
                return IntPtr.Zero;

            case NativeMethods.WmDpiChanged:
                OnDpiChanged(hwnd, wParam, lParam);
                return IntPtr.Zero;

            case NativeMethods.WmNcCalcSize:
                return OnNcCalcSize(hwnd, wParam, lParam);

            case NativeMethods.WmNcHitTest:
                return OnNcHitTest(hwnd, lParam);

            case NativeMethods.WmGetMinMaxInfo:
                OnGetMinMaxInfo(lParam);
                return IntPtr.Zero;

            case NativeMethods.WmActivate:
                if (NativeMethods.LowWord(wParam) == 0)
                {
                    _deactivated?.Invoke(this);
                }
                else
                {
                    _activated?.Invoke(this);
                }

                return IntPtr.Zero;

            case NativeMethods.WmMouseMove:
                OnMouseMove(hwnd, lParam);
                return IntPtr.Zero;

            case NativeMethods.WmMouseLeave:
                _trackingLeave = false;
                _host.ProcessMouseLeave();
                return IntPtr.Zero;

            case NativeMethods.WmLButtonDown:
            case NativeMethods.WmLButtonDblClk:
                OnMouseDown(hwnd, lParam, XPlatMouseButtons.Left);
                return IntPtr.Zero;

            case NativeMethods.WmRButtonDown:
            case NativeMethods.WmRButtonDblClk:
                OnMouseDown(hwnd, lParam, XPlatMouseButtons.Right);
                return IntPtr.Zero;

            case NativeMethods.WmMButtonDown:
            case NativeMethods.WmMButtonDblClk:
                OnMouseDown(hwnd, lParam, XPlatMouseButtons.Middle);
                return IntPtr.Zero;

            case NativeMethods.WmLButtonUp:
                OnMouseUp(lParam, XPlatMouseButtons.Left);
                return IntPtr.Zero;

            case NativeMethods.WmRButtonUp:
                OnMouseUp(lParam, XPlatMouseButtons.Right);

                // Windows turns a right button release into WM_CONTEXTMENU inside the default
                // handler, so this message has to reach it or no menu is ever requested.
                return NativeMethods.DefWindowProcW(hwnd, message, wParam, lParam);

            case NativeMethods.WmMButtonUp:
                OnMouseUp(lParam, XPlatMouseButtons.Middle);
                return IntPtr.Zero;

            case NativeMethods.WmMouseWheel:
                OnMouseWheel(wParam, lParam, horizontal: false);
                return IntPtr.Zero;

            case NativeMethods.WmMouseHWheel:
                OnMouseWheel(wParam, lParam, horizontal: true);
                return IntPtr.Zero;

            case NativeMethods.WmKeyUp:
                OnKeyUp(wParam);
                return IntPtr.Zero;

            case NativeMethods.WmChar:
                OnChar(wParam);
                return IntPtr.Zero;

            case NativeMethods.WmSetCursor:
                if (NativeMethods.LowWord(lParam) == NativeMethods.HtClient)
                {
                    NativeMethods.SetCursor(CursorFor(_host.Cursor));
                    return new IntPtr(1);
                }

                break;

            case NativeMethods.WmSettingChange:
            case NativeMethods.WmThemeChanged:
                // The system appearance may have moved; RefreshTheme only acts on a real change.
                _host.RefreshTheme();
                return NativeMethods.DefWindowProcW(hwnd, message, wParam, lParam);

            case NativeMethods.WmContextMenu:
                // Let the default handler run when nothing opened a menu, so a missing menu still
                // behaves like an ordinary window instead of silently swallowing the message.
                return OnContextMenu(lParam) ? IntPtr.Zero : NativeMethods.DefWindowProcW(hwnd, message, wParam, lParam);

            case NativeMethods.WmClose:
                // The model decides: Closing may cancel, in which case the window stays up.
                _closeRequested?.Invoke(this);
                return IntPtr.Zero;

            case NativeMethods.WmDestroy:
                _hwnd = IntPtr.Zero;
                return IntPtr.Zero;
        }

        return NativeMethods.DefWindowProcW(hwnd, message, wParam, lParam);
    }

    /// <summary>
    /// Repaints whenever the palette is swapped - by the system appearance changing, or by the
    /// application assigning <see cref="XPlatTheme.Palette"/> - and keeps the title bar in step.
    /// </summary>
    private void OnThemeChanged(object sender)
    {
        NativeMethods.SetDarkTitleBar(_hwnd, XPlatTheme.IsDarkMode);
        Invalidate();
    }

    private void OnPaint(IntPtr hwnd)
    {
        var hdc = NativeMethods.BeginPaint(hwnd, out var paint);
        try
        {
            if (_gl is not null)
            {
                // The retained offscreen buffer is the frame. A damaged paint updates only the
                // region the host accumulated; presenting stamps that whole buffer onto the window
                // with one Skia blit, so a small caret update no longer forces a full raster. If
                // Windows only wants pixels back (uncover, minimise/restore), the retained buffer is
                // blitted without repainting the tree at all.
                bool stalePixels = paint.rcPaint.Right > paint.rcPaint.Left
                    && paint.rcPaint.Bottom > paint.rcPaint.Top;

                bool damaged = _host.IsUpdatePending
                    || _gl.NeedsFullRepaint
                    || _paintedGeneration != _bufferGeneration;

                if (Win32GlTrace.IsEnabled)
                {
                    Win32GlTrace.Write(
                        $"paint damaged={damaged} stale={stalePixels} retained={_gl.HasRetainedBuffer}");
                }

                if (!damaged && !stalePixels)
                {
                    // Nothing was invalidated and Windows is not asking for pixels: do not submit a
                    // frame at all.
                    return;
                }

                SKRectI glArea = SKRectI.Empty;

                if (damaged || !_gl.HasRetainedBuffer)
                {
                    // The direct tier has no retained contents of its own, so every paint it serves
                    // is a full frame. The retained tier paints only the damage.
                    glArea = Render(full: !_gl.HasRetainedBuffer);

                    if (_gl is null)
                    {
                        // The context was lost while rendering; the frame is now in the CPU buffer,
                        // so copy it out instead of leaving this paint empty until the next one.
                        Blit(hdc, glArea);
                        return;
                    }
                }

                var result = _gl.Present();

                if (result == Win32GlPresentResult.RetryDirect)
                {
                    // The retained buffer failed; repaint this one frame against the default
                    // framebuffer and present again. The next paint stays on the direct tier.
                    glArea = Render(full: true);

                    if (_gl is null)
                    {
                        Blit(hdc, glArea);
                        return;
                    }

                    result = _gl.Present();
                }

                if (result != Win32GlPresentResult.Presented)
                {
                    // A driver reset, an RDP session change or a lost device: drop the GL stack and
                    // let the next paint use the DIB, rather than throwing from a paint handler.
                    LoseGl(invalidate: true);
                }

                return;
            }

            if (_skia is null || _memoryDc == IntPtr.Zero)
            {
                return;
            }

            // The update region is everything Windows thinks is stale, which covers what the UI
            // asked for and also things it cannot know about - an uncovered window, a resize. The
            // buffer still holds the pixels of those, so they are copied back out of it rather than
            // added to the paint. Adding them would grow the paint to the whole update region, and
            // Windows reports that region as a single rectangle: two small changes at opposite ends
            // of a row would repaint everything between them.
            var stale = new SKRectI(
                (int)MathF.Floor(paint.rcPaint.Left / _scale),
                (int)MathF.Floor(paint.rcPaint.Top / _scale),
                (int)MathF.Ceiling(paint.rcPaint.Right / _scale),
                (int)MathF.Ceiling(paint.rcPaint.Bottom / _scale));

            // A buffer that was just allocated holds whatever was in that memory, so the frame it
            // starts with has to be painted in full even when the UI asked for nothing.
            if (!_host.IsUpdatePending && _paintedGeneration == _bufferGeneration)
            {
                Blit(hdc, stale);
                return;
            }

            var area = Render();
            Blit(hdc, new SKRectI(
                Math.Min(stale.Left, area.Left),
                Math.Min(stale.Top, area.Top),
                Math.Max(stale.Right, area.Right),
                Math.Max(stale.Bottom, area.Bottom)));
        }
        finally
        {
            NativeMethods.EndPaint(hwnd, ref paint);
        }
    }

    private void OnPrintClient(IntPtr hdc)
    {
        if (_gl is not null)
        {
            // WM_PRINTCLIENT hands over a foreign DC, and a GL back buffer cannot be BitBlt'd from.
            // Paint one CPU frame into a temporary DIB instead, so a screenshot or a visual test
            // still gets pixels without turning the window's own presentation back into GDI.
            RenderCpuSnapshot(hdc);
            return;
        }

        if (_skia is null || _memoryDc == IntPtr.Zero)
        {
            return;
        }

        // Printing and the like copy the window into a fresh surface that holds no previous frame,
        // so everything has to be painted and copied.
        Blit(hdc, Render(full: true));
    }

    /// <summary>Copies an area of the drawing buffer to a device context.</summary>
    /// <param name="hdc">The context to copy to.</param>
    /// <param name="area">The area to copy, in logical pixels.</param>
    private void Blit(IntPtr hdc, SKRectI area)
    {
        var left = (int)MathF.Floor(area.Left * _scale);
        var top = (int)MathF.Floor(area.Top * _scale);
        var right = (int)MathF.Ceiling(area.Right * _scale);
        var bottom = (int)MathF.Ceiling(area.Bottom * _scale);

        left = Math.Max(left, 0);
        top = Math.Max(top, 0);
        right = Math.Min(right, _bufferWidth);
        bottom = Math.Min(bottom, _bufferHeight);

        if (right <= left || bottom <= top)
        {
            return;
        }

        NativeMethods.BitBlt(hdc, left, top, right - left, bottom - top, _memoryDc, left, top, NativeMethods.SrcCopy);
    }

    /// <summary>Paints the part of the UI that changed into the drawing buffer.</summary>
    /// <param name="full">Paint everything, whatever the UI asked for.</param>
    /// <returns>The area that was painted, in logical pixels.</returns>
    private SKRectI Render(bool full = false)
    {
        if (_gl is not null && !_gl.MakeCurrent())
        {
            // The context cannot be made current - the window never paints GL on any other thread,
            // so this is a lost context, not a threading mistake. Fall back for the rest of the
            // window's life.
            LoseGl(invalidate: false);
        }

        var surface = _gl?.Surface ?? _skia;
        if (surface is null)
        {
            return SKRectI.Empty;
        }

        // A buffer that was just allocated holds whatever was in that memory, so the frame it
        // starts with has to cover all of it.
        if (_paintedGeneration != _bufferGeneration)
        {
            _paintedGeneration = _bufferGeneration;
            full = true;
        }

        if (_gl?.TakeNeedsFull() == true)
        {
            full = true;
        }

        var area = PaintHost(surface, full);

        if (Win32GlTrace.IsEnabled)
        {
            Win32GlTrace.Write($"render full={full} area={area.Width}x{area.Height} scale={_scale}");
        }

        // GL submit and swap happen in OnPaint, after the host has finished the frame.
        return area;
    }

    /// <summary>Paints the control tree into a surface at the current scale.</summary>
    private SKRectI PaintHost(SKSurface surface, bool full)
    {
        var canvas = surface.Canvas;
        canvas.Save();
        canvas.Scale(_scale);
        var area = _host.Paint(canvas, full);
        canvas.Restore();
        canvas.Flush();

        return area;
    }

    /// <summary>
    /// Paints one whole CPU frame straight into a caller-supplied DC. Only used for
    /// <c>WM_PRINTCLIENT</c> while GL is presenting.
    /// </summary>
    private void RenderCpuSnapshot(IntPtr hdc)
    {
        if (_bufferWidth <= 0 || _bufferHeight <= 0)
        {
            return;
        }

        var info = new NativeMethods.BITMAPINFO();
        info.bmiHeader.biSize = (uint)Marshal.SizeOf<NativeMethods.BITMAPINFOHEADER>();
        info.bmiHeader.biWidth = _bufferWidth;
        info.bmiHeader.biHeight = -_bufferHeight;
        info.bmiHeader.biPlanes = 1;
        info.bmiHeader.biBitCount = 32;
        info.bmiHeader.biCompression = NativeMethods.BiRgb;

        var dib = NativeMethods.CreateDIBSection(
            IntPtr.Zero,
            ref info,
            NativeMethods.DibRgbColors,
            out var bits,
            IntPtr.Zero,
            0);

        if (dib == IntPtr.Zero)
        {
            return;
        }

        var dc = NativeMethods.CreateCompatibleDC(IntPtr.Zero);
        var previous = NativeMethods.SelectObject(dc, dib);

        try
        {
            var imageInfo = new SKImageInfo(_bufferWidth, _bufferHeight, SKColorType.Bgra8888, SKAlphaType.Premul);
            using var surface = SKSurface.Create(imageInfo, bits, _bufferWidth * 4);

            if (surface is null)
            {
                return;
            }

            PaintHost(surface, full: true);
            NativeMethods.BitBlt(hdc, 0, 0, _bufferWidth, _bufferHeight, dc, 0, 0, NativeMethods.SrcCopy);
        }
        finally
        {
            if (previous != IntPtr.Zero)
            {
                NativeMethods.SelectObject(dc, previous);
            }

            NativeMethods.DeleteDC(dc);
            NativeMethods.DeleteObject(dib);
        }
    }

    private void OnSize(int width, int height, uint sizeType)
    {
        if (width <= 0 || height <= 0)
        {
            return;
        }

        EnsureBuffer(width, height);
        UpdateHostSize();
        Invalidate();
        RaiseStateChanged(sizeType);
    }

    private void RaiseStateChanged(uint sizeType)
    {
        var state = sizeType switch
        {
            NativeMethods.SizeMinimized => XPlatWindowState.Minimized,
            NativeMethods.SizeMaximized => XPlatWindowState.Maximized,
            _ => XPlatWindowState.Normal,
        };

        if (state == _state)
        {
            return;
        }

        _state = state;
        _stateChanged?.Invoke(this);

        // Maximizing removes the resize band and restoring puts it back.
        RaiseChromeMetricsChanged();
    }

    private void OnDpiChanged(IntPtr hwnd, IntPtr wParam, IntPtr lParam)
    {
        // Follow the rectangle Windows suggests so the window keeps its logical size.
        var suggested = Marshal.PtrToStructure<NativeMethods.RECT>(lParam);
        NativeMethods.SetWindowPos(
            hwnd,
            IntPtr.Zero,
            suggested.Left,
            suggested.Top,
            suggested.Right - suggested.Left,
            suggested.Bottom - suggested.Top,
            NativeMethods.SwpNoZOrder | NativeMethods.SwpNoActivate);

        // The high word of wParam is the new DPI; the low word is the direction of the change.
        var scale = ScaleOf((uint)NativeMethods.HighWord(wParam));
        if (MathF.Abs(scale - _scale) < 0.001f)
        {
            return;
        }

        _scale = scale;

        // WM_SIZE above ran against the old scale; re-measure so the host sees logical pixels.
        UpdateHostSize();
        ApplyIcon();
        _scaleChanged?.Invoke(this);

        // The frame is measured in device pixels, so every logical inset changes with the display.
        RaiseChromeMetricsChanged();
    }

    private void OnGetMinMaxInfo(IntPtr lParam)
    {
        var info = Marshal.PtrToStructure<NativeMethods.MINMAXINFO>(lParam);

        if (_minimumClientSize.Width > 0f || _minimumClientSize.Height > 0f)
        {
            var minimum = OuterSizeFor(_minimumClientSize.Width, _minimumClientSize.Height);
            info.ptMinTrackSize.X = minimum.Width;
            info.ptMinTrackSize.Y = minimum.Height;
        }

        if (_maximumClientSize.Width > 0f || _maximumClientSize.Height > 0f)
        {
            var maximum = OuterSizeFor(_maximumClientSize.Width, _maximumClientSize.Height);
            info.ptMaxTrackSize.X = maximum.Width;
            info.ptMaxTrackSize.Y = maximum.Height;
        }

        Marshal.StructureToPtr(info, lParam, false);
    }

    /// <summary>
    /// Runs whenever the non-client area is recalculated. In borderless mode it lets
    /// <c>DefWindowProc</c> compute the ordinary framed client rect, measures the frame that was
    /// used, and then grows the client up over the caption - which is what lets the application own
    /// the top edge without giving up the frame on the other three.
    /// </summary>
    /// <remarks>
    /// RULE: derive the inset from <c>rgrc[0]</c>, the PROPOSED rect. <c>rgrc[1]</c> is the current
    /// rect and is stale after a move or a maximize; the Win32 spike read it once and produced a
    /// 1086 px client in a 700 px window.
    /// </remarks>
    private IntPtr OnNcCalcSize(IntPtr hwnd, IntPtr wParam, IntPtr lParam)
    {
        if (_chrome != XPlatWindowChrome.Borderless || wParam == IntPtr.Zero)
        {
            // System chrome, or the create-time form that carries a RECT* rather than
            // NCCALCSIZE_PARAMS and cannot be overridden.
            return NativeMethods.DefWindowProcW(hwnd, NativeMethods.WmNcCalcSize, wParam, lParam);
        }

        var proposed = Marshal.PtrToStructure<NativeMethods.NCCALCSIZE_PARAMS>(lParam).rgrc0;
        var result = NativeMethods.DefWindowProcW(hwnd, NativeMethods.WmNcCalcSize, wParam, lParam);
        var parameters = Marshal.PtrToStructure<NativeMethods.NCCALCSIZE_PARAMS>(lParam);
        var client = parameters.rgrc0;

        // Measure the frame rather than computing it: SM_CXSIZEFRAME plus SM_CXPADDEDBORDER is a
        // close guess at this inset, not the number the window actually got.
        var frameWidth = client.Left - proposed.Left;
        var frameHeight = proposed.Bottom - client.Bottom;
        if (frameWidth >= 0 && frameHeight >= 0)
        {
            // The pre-size call reports zeros, so keeping the last good values is what lets a
            // maximize still offset by the right height.
            _frameWidth = frameWidth;
            _frameHeight = frameHeight;
        }

        var maximizing = IsMaximizing(hwnd, proposed);

        // Maximized the top starts lower by the bottom inset, which is what lands the client exactly
        // on the work area rather than one frame above it.
        client.Top = maximizing ? proposed.Top + _frameHeight : proposed.Top;
        parameters.rgrc0 = client;
        Marshal.StructureToPtr(parameters, lParam, false);

        // Maximized, the window's edges are the screen's, so nothing may claim a resize band there.
        _hitBandWidth = maximizing ? 0 : _frameWidth;
        _hitBandHeight = maximizing ? 0 : _frameHeight;

        return result;
    }

    /// <summary>
    /// Reports the resize band for a borderless window instead of letting <c>DefWindowProc</c> decide.
    /// The client covers the caption, so the system would classify the top edge as client area and
    /// leave nothing to drag but a sliver of the frame.
    /// </summary>
    private IntPtr OnNcHitTest(IntPtr hwnd, IntPtr lParam)
    {
        if (_chrome != XPlatWindowChrome.Borderless)
        {
            return NativeMethods.DefWindowProcW(hwnd, NativeMethods.WmNcHitTest, IntPtr.Zero, lParam);
        }

        if (_hitBandWidth <= 0 || _hitBandHeight <= 0 || !NativeMethods.GetWindowRect(hwnd, out var window))
        {
            return new IntPtr(NativeMethods.HtClient);
        }

        // lParam carries signed screen coordinates; LowWord and HighWord sign extend them.
        var x = NativeMethods.LowWord(lParam) - window.Left;
        var y = NativeMethods.HighWord(lParam) - window.Top;
        var width = window.Right - window.Left;
        var height = window.Bottom - window.Top;

        var left = x < _hitBandWidth;
        var right = x >= width - _hitBandWidth;
        var top = y < _hitBandHeight;
        var bottom = y >= height - _hitBandHeight;

        if (top && left)
        {
            return new IntPtr(NativeMethods.HtTopLeft);
        }

        if (top && right)
        {
            return new IntPtr(NativeMethods.HtTopRight);
        }

        if (bottom && left)
        {
            return new IntPtr(NativeMethods.HtBottomLeft);
        }

        if (bottom && right)
        {
            return new IntPtr(NativeMethods.HtBottomRight);
        }

        if (left)
        {
            return new IntPtr(NativeMethods.HtLeft);
        }

        if (right)
        {
            return new IntPtr(NativeMethods.HtRight);
        }

        if (top)
        {
            return new IntPtr(NativeMethods.HtTop);
        }

        return bottom ? new IntPtr(NativeMethods.HtBottom) : new IntPtr(NativeMethods.HtClient);
    }

    /// <summary>
    /// Whether the rect being calculated is a maximize. <c>IsZoomed</c> alone is not enough: during
    /// the transition the window is not maximized yet, so the proposed rect is compared with the
    /// monitor's work area as well.
    /// </summary>
    private static bool IsMaximizing(IntPtr hwnd, NativeMethods.RECT proposed)
    {
        if (NativeMethods.IsZoomed(hwnd))
        {
            return true;
        }

        var monitor = NativeMethods.MonitorFromWindow(hwnd, NativeMethods.MonitorDefaultToNearest);
        if (monitor == IntPtr.Zero)
        {
            return false;
        }

        var info = new NativeMethods.MONITORINFO
        {
            cbSize = (uint)Marshal.SizeOf<NativeMethods.MONITORINFO>(),
        };

        if (!NativeMethods.GetMonitorInfoW(monitor, ref info))
        {
            return false;
        }

        return proposed.Left == info.rcWork.Left
            && proposed.Top == info.rcWork.Top
            && proposed.Right == info.rcWork.Right
            && proposed.Bottom == info.rcWork.Bottom;
    }

    /// <summary>Re-runs the frame calculation after a chrome change and tells the application.</summary>
    private void ApplyChrome()
    {
        NativeMethods.SetWindowPos(
            _hwnd,
            IntPtr.Zero,
            0,
            0,
            0,
            0,
            NativeMethods.SwpNoMove | NativeMethods.SwpNoSize | NativeMethods.SwpNoZOrder |
            NativeMethods.SwpNoActivate | NativeMethods.SwpFrameChanged);

        Invalidate();
        RaiseChromeMetricsChanged();
    }

    private void RaiseChromeMetricsChanged() => _chromeMetricsChanged?.Invoke(this);

    /// <summary>
    /// Builds the snapshot from the measured frame, converting device pixels to logical ones.
    /// </summary>
    /// <param name="borderless">Whether the window is in borderless mode at all.</param>
    /// <param name="frameWidth">Measured left and right frame inset, in device pixels.</param>
    /// <param name="frameHeight">Measured bottom frame inset, in device pixels.</param>
    /// <param name="scale">Logical-to-device ratio of the window's display.</param>
    /// <param name="maximized">Whether the window is maximized, which removes the resize band.</param>
    /// <returns>The snapshot the seam reports.</returns>
    internal static XPlatWindowChromeMetrics ChromeMetricsFor(
        bool borderless, int frameWidth, int frameHeight, float scale, bool maximized)
    {
        if (!borderless)
        {
            return SystemMetrics;
        }

        var safeScale = scale > 0f ? scale : 1f;
        var edge = frameWidth / safeScale;
        var band = frameHeight / safeScale;

        return new XPlatWindowChromeMetrics
        {
            // The system paints the frame on three edges and the client owns the top one, so there is
            // no top inset. The DWM border line that still crosses the top edge is cosmetic and
            // belongs to BorderColor rather than to this number.
            SurfaceInset = new XPlatThickness(edge, 0f, edge, band),

            // What the band takes from the application's own pixels: the top edge only. On the other
            // three edges it lives in the frame the application never sees.
            ResizeInset = maximized ? XPlatThickness.Empty : new XPlatThickness(0f, band, 0f, 0f),

            CornerResizeExtent = maximized ? 0f : band,

            // Windows rounds its own corners on 11 and later but exposes no radius query, and a
            // square window has none, so nothing here may clip the application's drawing.
            CornerRadius = 0f,

            CaptionButtons = XPlatCaptionButtons.None,
            CaptionButtonSide = XPlatCaptionButtonSide.Right,
        };
    }

    private void OnMouseMove(IntPtr hwnd, IntPtr lParam)
    {
        if (!_trackingLeave)
        {
            var track = new NativeMethods.TRACKMOUSEEVENT
            {
                cbSize = (uint)Marshal.SizeOf<NativeMethods.TRACKMOUSEEVENT>(),
                dwFlags = NativeMethods.TmeLeave,
                hwndTrack = hwnd,
            };

            _trackingLeave = NativeMethods.TrackMouseEvent(ref track);
        }

        _host.ProcessMouseMove(ToLogical(lParam));
    }

    private void OnMouseDown(IntPtr hwnd, IntPtr lParam, XPlatMouseButtons button)
    {
        NativeMethods.SetCapture(hwnd);
        _host.ProcessMouseDown(ToLogical(lParam), button, CurrentModifiers(), NextClickCount(lParam));
    }

    private void OnMouseUp(IntPtr lParam, XPlatMouseButtons button)
    {
        _host.ProcessMouseUp(ToLogical(lParam), button, CurrentModifiers());
        NativeMethods.ReleaseCapture();
    }

    /// <summary>
    /// Forwards a wheel turn. The delta sits in the high word of <paramref name="wParam"/> in
    /// 120ths of a detent, and - unlike every other mouse message - the position is in screen
    /// pixels rather than client ones.
    /// </summary>
    private void OnMouseWheel(IntPtr wParam, IntPtr lParam, bool horizontal)
    {
        var detents = NativeMethods.HighWord(wParam) / 120f;
        var pixels = detents * XPlatUiHost.WheelPixelsPerDetent;

        // A tilt wheel's positive delta points right; the host measures toward the start.
        if (horizontal)
        {
            pixels = -pixels;
        }

        var location = ScreenToLogical(NativeMethods.LowWord(lParam), NativeMethods.HighWord(lParam));
        _host.ProcessMouseWheel(location, pixels, CurrentModifiers());
    }

    private void OnKeyUp(IntPtr wParam)
    {
        var key = MapKey((int)wParam);
        if (key != XPlatKey.None)
        {
            _host.ProcessKeyUp(key, CurrentModifiers());
        }
    }

    /// <summary>
    /// Windows asks for a context menu through its own message, after the right button release or
    /// when the keyboard menu key is pressed, which is exactly the single entry point the UI
    /// library expects. The location is in screen pixels; the host works in logical client pixels.
    /// </summary>
    private bool OnContextMenu(IntPtr lParam)
    {
        var screen = new NativeMethods.POINT
        {
            X = NativeMethods.LowWord(lParam),
            Y = NativeMethods.HighWord(lParam),
        };

        // (-1, -1) means the keyboard asked for the menu. There is no per-control anchor to hang
        // it off here, so it opens at the pointer, which is where the user is looking.
        if (screen.X == -1 && screen.Y == -1 && !NativeMethods.GetCursorPos(out screen))
        {
            return false;
        }

        return _host.ProcessContextMenu(ScreenToLogical(screen.X, screen.Y));
    }

    private void OnChar(IntPtr wParam)
    {
        var character = (char)(int)wParam;
        if (char.IsControl(character))
        {
            return;
        }

        _host.ProcessTextInput(character.ToString());
    }

    private void UpdateHostSize()
    {
        if (_hwnd == IntPtr.Zero)
        {
            return;
        }

        if (!NativeMethods.GetClientRect(_hwnd, out var client) || client.Right <= 0 || client.Bottom <= 0)
        {
            return;
        }

        var size = new SKSize(client.Right / _scale, client.Bottom / _scale);
        _clientSize = size;

        // The host is the authority on whether anything actually changed: _clientSize can already
        // hold this value because the model assigned ClientSize before the window existed, and in
        // that case the host has still never been told and the control tree has never been laid out.
        if (size == _host.ClientSize)
        {
            return;
        }

        _host.SetClientSize(size.Width, size.Height);
        _clientSizeChanged?.Invoke(this);
    }

    private void Invalidate()
    {
        if (_hwnd != IntPtr.Zero)
        {
            NativeMethods.InvalidateRect(_hwnd, IntPtr.Zero, false);
        }
    }

    /// <summary>Asks Windows to repaint just the part of the window the UI invalidated.</summary>
    /// <remarks>
    /// Invalidating the whole window would make every frame a whole frame, because the update
    /// region is added to the paint: Windows would hand back the entire client area on the next
    /// WM_PAINT, so the part that changed could never be smaller than everything.
    /// </remarks>
    private void InvalidatePending()
    {
        if (_hwnd == IntPtr.Zero)
        {
            return;
        }

        var pending = _host.PendingUpdateBounds;
        var rect = new NativeMethods.RECT
        {
            Left = Math.Clamp((int)MathF.Floor(pending.Left * _scale), 0, _bufferWidth),
            Top = Math.Clamp((int)MathF.Floor(pending.Top * _scale), 0, _bufferHeight),
            Right = Math.Clamp((int)MathF.Ceiling(pending.Right * _scale), 0, _bufferWidth),
            Bottom = Math.Clamp((int)MathF.Ceiling(pending.Bottom * _scale), 0, _bufferHeight),
        };

        if (rect.Right > rect.Left && rect.Bottom > rect.Top)
        {
            NativeMethods.InvalidateRect(_hwnd, ref rect, false);
            return;
        }

        // Nothing has been measured yet, so there is no area to aim at. Losing the frame would be
        // worse than repainting the window.
        NativeMethods.InvalidateRect(_hwnd, IntPtr.Zero, false);
    }

    private SKPoint ToLogical(IntPtr lParam) =>
        new(NativeMethods.LowWord(lParam) / _scale, NativeMethods.HighWord(lParam) / _scale);

    /// <summary>Converts a point in screen pixels to the window's own logical pixels.</summary>
    /// <param name="screenX">The x coordinate, in screen pixels.</param>
    /// <param name="screenY">The y coordinate, in screen pixels.</param>
    /// <returns>The point relative to the window's top left corner, in logical pixels.</returns>
    /// <remarks>
    /// The window owns both halves of this: the origin it sits at and the scale factor it is drawn
    /// at. Two integers rather than a point type keep the conversion from publishing a piece of the
    /// backend's interop surface along with it.
    /// </remarks>
    public SKPoint ScreenToLogical(int screenX, int screenY)
    {
        var point = new NativeMethods.POINT { X = screenX, Y = screenY };
        NativeMethods.ScreenToClient(_hwnd, ref point);
        return new SKPoint(point.X / _scale, point.Y / _scale);
    }

    private static XPlatModifiers CurrentModifiers()
    {
        var modifiers = XPlatModifiers.None;

        if ((NativeMethods.GetKeyState(NativeMethods.VkShift) & 0x8000) != 0)
        {
            modifiers |= XPlatModifiers.Shift;
        }

        if ((NativeMethods.GetKeyState(NativeMethods.VkControl) & 0x8000) != 0)
        {
            modifiers |= XPlatModifiers.Control;
        }

        if ((NativeMethods.GetKeyState(NativeMethods.VkMenu) & 0x8000) != 0)
        {
            modifiers |= XPlatModifiers.Alt;
        }

        return modifiers;
    }

    /// <summary>
    /// Win32 sends a separate double-click message, so successive presses are counted here to give
    /// the control tree the 1, 2, 3... sequence it expects.
    /// </summary>
    private int NextClickCount(IntPtr lParam)
    {
        var x = NativeMethods.LowWord(lParam);
        var y = NativeMethods.HighWord(lParam);
        var now = Environment.TickCount64;
        var slopX = NativeMethods.GetSystemMetrics(NativeMethods.SmCxDoubleClk) / 2;
        var slopY = NativeMethods.GetSystemMetrics(NativeMethods.SmCyDoubleClk) / 2;

        var withinTime = _clickCount > 0 && now - _lastClickTime <= NativeMethods.GetDoubleClickTime();
        var withinArea = Math.Abs(x - _lastClickX) <= slopX && Math.Abs(y - _lastClickY) <= slopY;

        _clickCount = withinTime && withinArea ? _clickCount + 1 : 1;
        _lastClickTime = now;
        _lastClickX = x;
        _lastClickY = y;
        return _clickCount;
    }

    private void ApplyCursor() => NativeMethods.SetCursor(CursorFor(_host.Cursor));

    private IntPtr CursorFor(XPlatCursor cursor)
    {
        if (_cursors.TryGetValue(cursor, out var handle))
        {
            return handle;
        }

        var id = cursor switch
        {
            XPlatCursor.Hand => NativeMethods.IdcHand,
            XPlatCursor.IBeam => NativeMethods.IdcIBeam,
            XPlatCursor.Cross => NativeMethods.IdcCross,
            XPlatCursor.SizeAll => NativeMethods.IdcSizeAll,
            XPlatCursor.SizeNS => NativeMethods.IdcSizeNs,
            XPlatCursor.SizeWE => NativeMethods.IdcSizeWe,
            _ => NativeMethods.IdcArrow,
        };

        handle = NativeMethods.LoadCursorW(IntPtr.Zero, new IntPtr(id));
        _cursors[cursor] = handle;
        return handle;
    }

    private static XPlatKey MapKey(int virtualKey)
    {
        if (virtualKey is >= 0x41 and <= 0x5A)
        {
            return XPlatKey.A + (virtualKey - 0x41);
        }

        if (virtualKey is >= 0x30 and <= 0x39)
        {
            return XPlatKey.D0 + (virtualKey - 0x30);
        }

        return virtualKey switch
        {
            0x09 => XPlatKey.Tab,
            0x0D => XPlatKey.Enter,
            0x20 => XPlatKey.Space,
            0x1B => XPlatKey.Escape,
            0x08 => XPlatKey.Backspace,
            0x2E => XPlatKey.Delete,
            0x25 => XPlatKey.Left,
            0x27 => XPlatKey.Right,
            0x26 => XPlatKey.Up,
            0x28 => XPlatKey.Down,
            0x24 => XPlatKey.Home,
            0x23 => XPlatKey.End,
            0x21 => XPlatKey.PageUp,
            0x22 => XPlatKey.PageDown,
            _ => XPlatKey.None,
        };
    }

    private static float ScaleOf(uint dpi) => dpi > 0 ? dpi / 96f : 1f;

    private (int Width, int Height) OuterSizeFor(float clientWidth, float clientHeight)
    {
        var rect = new NativeMethods.RECT
        {
            Right = Math.Max((int)MathF.Round(clientWidth * _scale), 0),
            Bottom = Math.Max((int)MathF.Round(clientHeight * _scale), 0),
        };

        var dpi = (uint)MathF.Round(_scale * 96f);
        NativeMethods.AdjustWindowRectExForDpi(ref rect, StyleFor(), false, 0, dpi == 0 ? 96u : dpi);

        if (_chrome == XPlatWindowChrome.Borderless)
        {
            // The client is grown over the caption, so the top inset the frame would add is not part
            // of the window any more. rect.Top is that inset negated, so dropping it leaves rect.Bottom
            // as the whole height.
            return (rect.Right - rect.Left, rect.Bottom);
        }

        return (rect.Right - rect.Left, rect.Bottom - rect.Top);
    }

    private void EnsureBuffer(int width, int height)
    {
        if (_gl is not null)
        {
            if (_gl.Resize(width, height))
            {
                _bufferWidth = width;
                _bufferHeight = height;
                _bufferGeneration++;
                return;
            }

            // A resize that cannot rebuild the render target is as final as a lost context: keep the
            // window alive on the DIB rather than throwing from WM_SIZE.
            LoseGl(invalidate: false);
        }
        else if (_useHardwareAcceleration && !_glUnavailable)
        {
            var gl = Win32Gfx.TryCreate(
                hardwareAcceleration: true,
                _hwnd,
                Win32GlProbe.Instance,
                width,
                height,
                out var reason);

            _gfxReason = reason;

            if (gl is not null)
            {
                ReleaseBuffer();
                _gl = gl;
                _bufferWidth = width;
                _bufferHeight = height;
                _bufferGeneration++;
                return;
            }

            _glUnavailable = true;
        }

        EnsureCpuBuffer(width, height);
    }

    private void EnsureCpuBuffer(int width, int height)
    {
        if (_skia is not null && _bufferWidth == width && _bufferHeight == height)
        {
            return;
        }

        ReleaseBuffer();

        var info = new NativeMethods.BITMAPINFO();
        info.bmiHeader.biSize = (uint)Marshal.SizeOf<NativeMethods.BITMAPINFOHEADER>();
        info.bmiHeader.biWidth = width;
        info.bmiHeader.biHeight = -height; // Top-down rows match Skia's memory layout.
        info.bmiHeader.biPlanes = 1;
        info.bmiHeader.biBitCount = 32;
        info.bmiHeader.biCompression = NativeMethods.BiRgb;

        _dib = NativeMethods.CreateDIBSection(IntPtr.Zero, ref info, NativeMethods.DibRgbColors, out _dibBits, IntPtr.Zero, 0);
        if (_dib == IntPtr.Zero)
        {
            return;
        }

        _memoryDc = NativeMethods.CreateCompatibleDC(IntPtr.Zero);
        _previousBitmap = NativeMethods.SelectObject(_memoryDc, _dib);

        var imageInfo = new SKImageInfo(width, height, SKColorType.Bgra8888, SKAlphaType.Premul);
        _skia = SKSurface.Create(imageInfo, _dibBits, width * 4);

        if (_skia is null)
        {
            ReleaseBuffer();
            return;
        }

        _bufferWidth = width;
        _bufferHeight = height;
        _bufferGeneration++;
    }

    private void ReleaseBuffer()
    {
        _skia?.Dispose();
        _skia = null;

        if (_memoryDc != IntPtr.Zero)
        {
            if (_previousBitmap != IntPtr.Zero)
            {
                NativeMethods.SelectObject(_memoryDc, _previousBitmap);
                _previousBitmap = IntPtr.Zero;
            }

            NativeMethods.DeleteDC(_memoryDc);
            _memoryDc = IntPtr.Zero;
        }

        if (_dib != IntPtr.Zero)
        {
            NativeMethods.DeleteObject(_dib);
            _dib = IntPtr.Zero;
        }

        _dibBits = IntPtr.Zero;
        _bufferWidth = 0;
        _bufferHeight = 0;
    }

    /// <summary>
    /// Drops the GL stack and returns the window to its retained DIB. Used for a lost context, a
    /// failed present or a render target that cannot be rebuilt; the fallback lasts for the rest of
    /// this window's life rather than risking a context that keeps failing.
    /// </summary>
    /// <param name="invalidate">Ask for a repaint, which the next WM_PAINT will satisfy with GDI.</param>
    private void LoseGl(bool invalidate)
    {
        int width = _bufferWidth;
        int height = _bufferHeight;

        ReleaseGl();
        _glUnavailable = true;
        _gfxReason = "cpu (gl context lost)";

        // The CPU buffer may be gone (it was released when GL took over), so rebuild it at the
        // current framebuffer size before anything paints again.
        EnsureCpuBuffer(width, height);

        if (invalidate)
        {
            Invalidate();
        }
    }

    private void ReleaseGl()
    {
        _gl?.Dispose();
        _gl = null;
    }

    /// <inheritdoc />
    public void RunWindowHooks(XPlatWindowPhase phase, XPlatWindow window)
    {
        ArgumentNullException.ThrowIfNull(window);

        foreach (var hook in window.Services.GetServices<IXPlatWin32WindowHook>())
        {
            hook.OnPhase(phase, window, this);
        }
    }
}
