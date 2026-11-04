using System.Runtime.InteropServices;
using SkiaSharp;
using XPlatUI;

namespace XPlatUI.Backend.Win32;

/// <summary>
/// A popup surface on Windows: a borderless, layered window owned by the application window, with
/// its own <see cref="XPlatUiHost"/> so a drop-down list or a tooltip can hang past the edges of the
/// window it belongs to.
/// </summary>
/// <remarks>
/// <para>
/// This is the non-modal sibling of <see cref="Win32MenuWindow"/>. A menu is modal, so it can pump
/// its own message loop and live entirely inside one call; a drop-down list is not, so this window
/// is created, shown and then left alone. Its messages arrive through the owner's ordinary message
/// loop, which is why the window class dispatches to a per-window instance looked up by handle
/// rather than to a thread-static field.
/// </para>
/// <para>
/// The window is layered (<c>WS_EX_LAYERED</c> + <c>UpdateLayeredWindow</c>) for two reasons: it is
/// the only way to get the Skia surface's alpha to the compositor, and Windows hit-tests a layered
/// window against that alpha, so fully transparent pixels already let the pointer through.
/// </para>
/// <para>
/// It never takes the focus (<c>WS_EX_NOACTIVATE</c> plus <c>MA_NOACTIVATE</c>), so the control that
/// opened it keeps the keyboard: the user can carry on typing into a combo box while its list is
/// open, exactly as the Win32 combo box allows.
/// </para>
/// </remarks>
internal sealed class Win32PopupWindow : IXPlatPopupWindow
{
    private const string ClassName = "XPlatUI.PopupWindow";

    /// <summary>
    /// Every live popup by handle. The class procedure is static, so this is how a message finds the
    /// instance that owns it.
    /// </summary>
    private static readonly Dictionary<IntPtr, Win32PopupWindow> Live = new();

    private static NativeMethods.WndProc? _wndProc;
    private static IntPtr _moduleHandle;
    private static bool _classRegistered;

    /// <summary>
    /// The window being created on this thread, so messages that arrive before
    /// <c>CreateWindowExW</c> has returned - and therefore before the handle is known - still reach
    /// the instance that is being built.
    /// </summary>
    [ThreadStatic]
    private static Win32PopupWindow? _creating;

    private readonly XPlatUiHost _host = new();
    private readonly NativeMethods.WndProc _instanceProc;
    private readonly List<IDisposable> _subscriptions = new();

    private IntPtr _hwnd;
    private IntPtr _ownerHwnd;
    private IntPtr _memoryDc;
    private IntPtr _dib;
    private IntPtr _dibBits;
    private IntPtr _previousBitmap;
    private int _bufferWidth;
    private int _bufferHeight;
    private SKSurface? _skia;

    private Func<SKPoint, NativeMethods.POINT>? _toScreen;
    private float _scale = 1f;
    private int _windowX;
    private int _windowY;
    private bool _trackingLeave;
    private bool _dismissing;
    private bool _disposed;

    private long _lastClickTime;
    private int _clickCount;
    private int _lastClickX;
    private int _lastClickY;

    public Win32PopupWindow(bool dismissOnOutsideClick)
    {
        DismissesOnOutsideClick = dismissOnOutsideClick;

        // The delegate has to stay rooted for as long as the window class can call it.
        _instanceProc = WindowProc;

        _subscriptions.Add(_host.AddInvalidatedHandler(_ => Present()));
        _subscriptions.Add(_host.AddCursorChangedHandler(_ => ApplyCursor()));
        XPlatTheme.AddChangedHandler(OnThemeChanged);
    }

    public XPlatUiHost Host => _host;

    public bool DismissesOnOutsideClick { get; }

    public event Action<object>? Dismissed;

    /// <summary>Closes every popup owned by <paramref name="ownerHwnd"/>.</summary>
    /// <returns>Whether a popup was dismissed, which is what tells the caller the press it is
    /// handling belonged to that popup.</returns>
    /// <remarks>
    /// A press on the owning window is delivered to that window rather than to the popup floating
    /// above it, so the owner is what has to notice and dismiss. Called from its message procedure.
    /// </remarks>
    public static bool DismissForOwner(IntPtr ownerHwnd)
    {
        if (Live.Count == 0)
        {
            return false;
        }

        // Copied first: dismissing disposes the popup, which removes it from the dictionary.
        var owned = new List<Win32PopupWindow>();
        foreach (var window in Live.Values)
        {
            if (window._ownerHwnd == ownerHwnd && window.DismissesOnOutsideClick)
            {
                owned.Add(window);
            }
        }

        foreach (var window in owned)
        {
            window.Dismiss();
        }

        return owned.Count > 0;
    }

    public void Show(XPlatUiHost ownerHost, SKPoint location, SKSize size)
    {
        ArgumentNullException.ThrowIfNull(ownerHost);
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (_hwnd != IntPtr.Zero)
        {
            UpdateBounds(location, size);
            return;
        }

        if (!EnsureClass())
        {
            return;
        }

        var backend = ownerHost.Window?.Backend as Win32WindowBackend;
        _ownerHwnd = backend?.Handle ?? IntPtr.Zero;
        _toScreen = backend is null ? null : backend.LogicalToScreen;
        _scale = ownerHost.Window?.ScaleFactor ?? 1f;

        // The popup's tree resolves services from the same container as the window it belongs to,
        // so a control that opens a popup of its own finds the service that way too.
        _host.Services = ownerHost.Services;
        _host.SetClientSize(size.Width, size.Height);

        var (width, height) = PhysicalSize(size);
        var screen = ToScreen(location);
        var (x, y) = Place(screen, width, height);
        _windowX = x;
        _windowY = y;

        var exStyle = NativeMethods.WsExToolWindow | NativeMethods.WsExLayered | NativeMethods.WsExNoActivate;

        if (!DismissesOnOutsideClick)
        {
            // A tooltip must not swallow the press that was meant for the control underneath it.
            exStyle |= NativeMethods.WsExTransparent;
        }

        _creating = this;

        try
        {
            _hwnd = NativeMethods.CreateWindowExW(
                exStyle,
                ClassName,
                "XPlatUI.PopupWindow",
                NativeMethods.WsPopup | NativeMethods.WsClipChildren,
                x,
                y,
                width,
                height,
                _ownerHwnd,
                IntPtr.Zero,
                _moduleHandle,
                IntPtr.Zero);
        }
        finally
        {
            _creating = null;
        }

        if (_hwnd == IntPtr.Zero)
        {
            return;
        }

        // Ownership is what keeps the popup above its owner and off the taskbar and Alt-Tab, and
        // what makes Windows destroy it if the owner goes away first.
        NativeMethods.SetWindowLongPtrW(_hwnd, NativeMethods.GwlpHwndParent, _ownerHwnd);

        EnsureBuffer(width, height);

        // Shown without activating: the owner keeps the focus, so keystrokes still reach the
        // control that opened the popup.
        NativeMethods.ShowWindow(_hwnd, NativeMethods.SwShowNoActivate);
        NativeMethods.SetWindowPos(
            _hwnd,
            NativeMethods.HwndTopMost,
            x,
            y,
            width,
            height,
            NativeMethods.SwpNoActivate | NativeMethods.SwpShowWindow);

        Present();
    }

    public void UpdateBounds(SKPoint location, SKSize size)
    {
        if (_hwnd == IntPtr.Zero || _disposed)
        {
            return;
        }

        var (width, height) = PhysicalSize(size);
        var screen = ToScreen(location);
        var (x, y) = Place(screen, width, height);
        _windowX = x;
        _windowY = y;

        _host.SetClientSize(size.Width, size.Height);
        EnsureBuffer(width, height);

        // UpdateLayeredWindow carries the destination and the size along with the pixels, so the
        // window moves and resizes in the same call that draws it.
        Present();
    }

    public void Hide()
    {
        if (_hwnd != IntPtr.Zero)
        {
            NativeMethods.ShowWindow(_hwnd, NativeMethods.SwHide);
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        XPlatTheme.RemoveChangedHandler(OnThemeChanged);

        foreach (var subscription in _subscriptions)
        {
            subscription.Dispose();
        }

        _subscriptions.Clear();

        DestroyWindow();
        ReleaseBuffer();

        _host.Root = null;
    }

    private static bool EnsureClass()
    {
        if (_classRegistered)
        {
            return true;
        }

        _moduleHandle = NativeMethods.GetModuleHandleW(null);
        _wndProc ??= WindowProc;

        var windowClass = new NativeMethods.WNDCLASSEXW
        {
            cbSize = (uint)Marshal.SizeOf<NativeMethods.WNDCLASSEXW>(),
            style = NativeMethods.CsHRedraw | NativeMethods.CsVRedraw | NativeMethods.CsDblClks,
            lpfnWndProc = Marshal.GetFunctionPointerForDelegate(_wndProc),
            hInstance = _moduleHandle,
            hCursor = NativeMethods.LoadCursorW(IntPtr.Zero, new IntPtr(NativeMethods.IdcArrow)),
            lpszClassName = ClassName,
        };

        if (NativeMethods.RegisterClassExW(ref windowClass) == 0)
        {
            return false;
        }

        _classRegistered = true;
        return true;
    }

    private static IntPtr WindowProc(IntPtr hwnd, uint message, IntPtr wParam, IntPtr lParam)
    {
        try
        {
            if (!Live.TryGetValue(hwnd, out var window))
            {
                // Messages sent while CreateWindowExW is still running arrive before the handle is
                // known to the caller, so the instance being built adopts the handle here.
                if (_creating is not { _hwnd: 0 } building)
                {
                    return NativeMethods.DefWindowProcW(hwnd, message, wParam, lParam);
                }

                building._hwnd = hwnd;
                Live[hwnd] = building;
                window = building;
            }

            return window.HandleMessage(hwnd, message, wParam, lParam);
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
        switch (message)
        {
            case NativeMethods.WmPaint:
            {
                var hdc = NativeMethods.BeginPaint(hwnd, out var paint);
                try
                {
                    // A layered window has no surface of its own to paint, so this only keeps the
                    // window valid; the visible frame comes from Present().
                    Present();
                }
                finally
                {
                    NativeMethods.EndPaint(hwnd, ref paint);
                }

                return IntPtr.Zero;
            }

            case NativeMethods.WmPrintClient:
                Present();
                return IntPtr.Zero;

            case NativeMethods.WmEraseBkgnd:
                // The Skia surface covers every pixel.
                return new IntPtr(1);

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
                return IntPtr.Zero;

            case NativeMethods.WmMButtonUp:
                OnMouseUp(lParam, XPlatMouseButtons.Middle);
                return IntPtr.Zero;

            case NativeMethods.WmMouseWheel:
                OnMouseWheel(wParam, lParam, horizontal: false);
                return IntPtr.Zero;

            case NativeMethods.WmMouseHWheel:
                OnMouseWheel(wParam, lParam, horizontal: true);
                return IntPtr.Zero;

            case NativeMethods.WmMouseActivate:
                // Clicking the popup must not take the focus away from the control that opened it.
                return new IntPtr(NativeMethods.MaNoActivate);

            case NativeMethods.WmSetCursor:
                if (NativeMethods.LowWord(lParam) == NativeMethods.HtClient)
                {
                    ApplyCursor();
                    return new IntPtr(1);
                }

                break;

            case NativeMethods.WmActivateApp:
                if (wParam == IntPtr.Zero)
                {
                    Dismiss();
                }

                return IntPtr.Zero;

            case NativeMethods.WmCaptureChanged:
                return IntPtr.Zero;

            case NativeMethods.WmSettingChange:
            case NativeMethods.WmThemeChanged:
                _host.RefreshTheme();
                return NativeMethods.DefWindowProcW(hwnd, message, wParam, lParam);

            case NativeMethods.WmDestroy:
                Live.Remove(hwnd);
                _hwnd = IntPtr.Zero;
                return IntPtr.Zero;
        }

        return NativeMethods.DefWindowProcW(hwnd, message, wParam, lParam);
    }

    private void OnThemeChanged(object sender)
    {
        _host.RefreshTheme();
        Present();
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

    private void Dismiss()
    {
        if (_dismissing || _disposed)
        {
            return;
        }

        _dismissing = true;

        try
        {
            Dismissed?.Invoke(this);
        }
        finally
        {
            _dismissing = false;
        }
    }

    private void ApplyCursor()
    {
        if (_hwnd == IntPtr.Zero)
        {
            return;
        }

        NativeMethods.SetCursor(CursorFor(_host.Cursor));
    }

    private static IntPtr CursorFor(XPlatCursor cursor)
    {
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

        // System cursors are shared and must not be destroyed, so the handle is not cached.
        return NativeMethods.LoadCursorW(IntPtr.Zero, new IntPtr(id));
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

    private SKPoint ToLogical(IntPtr lParam) =>
        new(NativeMethods.LowWord(lParam) / _scale, NativeMethods.HighWord(lParam) / _scale);

    private SKPoint ScreenToLogical(int screenX, int screenY)
    {
        var point = new NativeMethods.POINT { X = screenX, Y = screenY };
        NativeMethods.ScreenToClient(_hwnd, ref point);
        return new SKPoint(point.X / _scale, point.Y / _scale);
    }

    /// <summary>Maps a logical point in the owner's client space to physical screen pixels.</summary>
    private NativeMethods.POINT ToScreen(SKPoint location) =>
        _toScreen is { } map
            ? map(location)
            : new NativeMethods.POINT
            {
                X = (int)MathF.Round(location.X * _scale),
                Y = (int)MathF.Round(location.Y * _scale),
            };

    private (int Width, int Height) PhysicalSize(SKSize size) =>
        (
            Math.Max((int)MathF.Ceiling(size.Width * _scale), 1),
            Math.Max((int)MathF.Ceiling(size.Height * _scale), 1));

    /// <summary>
    /// Positions the popup at <paramref name="screen"/>, sliding it back inside the monitor's work
    /// area when it would hang off the right or bottom edge.
    /// </summary>
    private (int X, int Y) Place(NativeMethods.POINT screen, int width, int height)
    {
        var work = NativeMethods.GetWorkArea(_ownerHwnd);

        var x = screen.X;
        var y = screen.Y;

        if (x + width > work.Right)
        {
            x = work.Right - width;
        }

        if (y + height > work.Bottom)
        {
            y = work.Bottom - height;
        }

        x = Math.Max(x, work.Left);
        y = Math.Max(y, work.Top);
        return (x, y);
    }

    private void DestroyWindow()
    {
        if (_hwnd == IntPtr.Zero)
        {
            return;
        }

        Live.Remove(_hwnd);
        NativeMethods.DestroyWindow(_hwnd);
        _hwnd = IntPtr.Zero;
    }

    /// <summary>
    /// Renders the tree into the drawing buffer and hands the buffer to the compositor, which is
    /// the only way a layered window ever shows anything.
    /// </summary>
    private void Present()
    {
        if (_skia is null || _memoryDc == IntPtr.Zero || _hwnd == IntPtr.Zero || _disposed)
        {
            return;
        }

        var canvas = _skia.Canvas;
        canvas.Save();
        canvas.Scale(_scale);
        _host.Paint(canvas, full: true);
        canvas.Restore();
        canvas.Flush();

        var screen = NativeMethods.GetDC(IntPtr.Zero);
        if (screen == IntPtr.Zero)
        {
            return;
        }

        try
        {
            var destination = new NativeMethods.POINT { X = _windowX, Y = _windowY };
            var source = new NativeMethods.POINT { X = 0, Y = 0 };
            var size = new NativeMethods.SIZE { cx = _bufferWidth, cy = _bufferHeight };
            var blend = new NativeMethods.BLENDFUNCTION
            {
                BlendOp = NativeMethods.AcSrcOver,
                SourceConstantAlpha = 255,
                AlphaFormat = NativeMethods.AcSrcAlpha,
            };

            NativeMethods.UpdateLayeredWindow(
                _hwnd,
                screen,
                ref destination,
                ref size,
                _memoryDc,
                ref source,
                0,
                ref blend,
                NativeMethods.UlwAlpha);
        }
        finally
        {
            NativeMethods.ReleaseDC(IntPtr.Zero, screen);
        }
    }

    private void EnsureBuffer(int width, int height)
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
}
