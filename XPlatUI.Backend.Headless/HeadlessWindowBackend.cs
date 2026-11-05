using SkiaSharp;

namespace XPlatUI.Backend.Headless;

/// <summary>
/// The window half for a machine with no display. It owns a real <see cref="XPlatUiHost"/> - the
/// same one the native harnesses drive - so the control tree is laid out and painted exactly as it
/// would be on screen; only the surface it paints into is different.
/// </summary>
/// <remarks>
/// Nothing here is asynchronous: <see cref="Show"/> marks the window visible and returns, and
/// <see cref="SavePng"/> paints on demand. There is no chrome, no input and no DPI change.
/// </remarks>
public sealed class HeadlessWindowBackend : IXPlatWindowBackend
{
    private Action<object>? _shown;
    private Action<object>? _closeRequested;
    private Action<object>? _closed;
    private Action<object>? _activated;
    private Action<object>? _deactivated;
    private Action<object>? _clientSizeChanged;
    private Action<object>? _scaleChanged;
    private Action<object>? _stateChanged;
    private readonly XPlatScreen _screen;
    private XPlatMenuBar? _menuBar;
    private SKSize _clientSize;
    private SKSize _minimumClientSize;
    private SKSize _maximumClientSize = new(float.PositiveInfinity, float.PositiveInfinity);
    private XPlatWindowState _state;
    private bool _disposed;

    /// <summary>Creates the backend for <paramref name="window"/>, reporting <paramref name="screen"/>.</summary>
    /// <param name="window">The model window this backend serves.</param>
    /// <param name="screen">The display to report, normally the backend's synthetic one.</param>
    public HeadlessWindowBackend(XPlatWindow window, XPlatScreen screen)
    {
        ArgumentNullException.ThrowIfNull(window);
        ArgumentNullException.ThrowIfNull(screen);

        Window = window;
        _screen = screen;
        Host = new XPlatUiHost();
    }

    /// <summary>The model window this backend serves.</summary>
    public XPlatWindow Window { get; }

    /// <inheritdoc />
    public XPlatUiHost Host { get; }

    /// <inheritdoc />
    public float ScaleFactor => 1f;

    /// <inheritdoc />
    public XPlatScreen? Screen => _screen;

    /// <inheritdoc />
    public string Title { get; set; } = string.Empty;

    /// <inheritdoc />
    /// <remarks>Stored only: a synthetic window has no chrome to draw an icon in.</remarks>
    public XPlatIcon? Icon { get; set; }

    /// <inheritdoc />
    /// <remarks>Forwards to <see cref="XPlatUiHost.SetClientSize"/>, which is what drives layout.</remarks>
    public SKSize ClientSize
    {
        get => _clientSize;
        set
        {
            if (_clientSize == value)
            {
                return;
            }

            _clientSize = value;
            Host.SetClientSize(value.Width, value.Height);
            _clientSizeChanged?.Invoke(this);
        }
    }

    /// <inheritdoc />
    public SKSize MinimumClientSize
    {
        get => _minimumClientSize;
        set => _minimumClientSize = value;
    }

    /// <inheritdoc />
    public SKSize MaximumClientSize
    {
        get => _maximumClientSize;
        set => _maximumClientSize = value;
    }

    /// <inheritdoc />
    public SKPoint Location { get; set; }

    /// <inheritdoc />
    public bool Resizable { get; set; } = true;

    /// <inheritdoc />
    public bool TopMost { get; set; }

    /// <inheritdoc />
    public XPlatWindowState State
    {
        get => _state;
        set
        {
            if (_state == value)
            {
                return;
            }

            _state = value;
            _stateChanged?.Invoke(this);
        }
    }

    /// <inheritdoc />
    public IXPlatWindowBackend? Owner { get; set; }

    /// <inheritdoc />
    /// <remarks>
    /// The bar is drawn into the same surface as the rest of the window, so a headless render shows
    /// the strip the native backends draw instead of skipping it.
    /// </remarks>
    public XPlatMenuBar? MenuBar
    {
        get => _menuBar;
        set
        {
            _menuBar = value;
            Host.MenuBar = value is null ? null : new XPlatMenuBarView(value);
        }
    }

    /// <summary>True once the window is the current one on the stack.</summary>
    public bool IsVisible { get; private set; }

    /// <summary>The appearance last passed to <see cref="SetTitleBarAppearance"/>.</summary>
    public bool TitleBarIsDark { get; private set; }

    /// <inheritdoc />
    public void Show()
    {
        IsVisible = true;
        _shown?.Invoke(this);
    }

    /// <inheritdoc />
    /// <remarks>Nothing is modal without a display: this shows the window and returns immediately.</remarks>
    public void ShowDialog() => Show();

    /// <inheritdoc />
    public void Activate()
    {
        if (!_disposed)
        {
            _activated?.Invoke(this);
        }
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
    public void SetTitleBarAppearance(bool dark) => TitleBarIsDark = dark;

    /// <summary>
    /// Runs the handlers <see cref="AddDeactivatedHandler"/> added. There is no window manager to
    /// move focus away, so a test that needs them asks for them.
    /// </summary>
    public void SimulateDeactivate()
    {
        if (!_disposed)
        {
            _deactivated?.Invoke(this);
        }
    }

    /// <summary>
    /// Runs the handlers <see cref="AddScaleChangedHandler"/> added, standing in for a window
    /// dragged onto a display with a different scale factor.
    /// </summary>
    public void SimulateScaleChange()
    {
        if (!_disposed)
        {
            _scaleChanged?.Invoke(this);
        }
    }

    /// <summary>
    /// Delivers a wheel turn, standing in for a real one. There is no pointer to route by, so the
    /// location is given explicitly.
    /// </summary>
    /// <param name="location">Where the pointer is, in logical client coordinates.</param>
    /// <param name="delta">
    /// How far to scroll, in logical pixels; positive scrolls toward the start of the content.
    /// </param>
    /// <param name="modifiers">The modifier keys held at the time.</param>
    /// <returns><c>true</c> if a control consumed the turn.</returns>
    public bool SimulateMouseWheel(SKPoint location, float delta, XPlatModifiers modifiers = XPlatModifiers.None)
    {
        return !_disposed && Host.ProcessMouseWheel(location, delta, modifiers);
    }

    /// <summary>
    /// Paints the control tree into <paramref name="canvas"/> at the current client size, exactly
    /// as a native backend would paint it into its framebuffer.
    /// </summary>
    /// <param name="canvas">The canvas to paint into.</param>
    public void RenderTo(SKCanvas canvas)
    {
        ArgumentNullException.ThrowIfNull(canvas);
        Host.Paint(canvas);
    }

    /// <summary>Paints the control tree and writes it out as a PNG.</summary>
    /// <param name="path">The file to write.</param>
    public void SavePng(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        var width = Math.Max(1, (int)MathF.Round(_clientSize.Width));
        var height = Math.Max(1, (int)MathF.Round(_clientSize.Height));
        var info = new SKImageInfo(width, height, SKColorType.Bgra8888, SKAlphaType.Premul);

        using var surface = SKSurface.Create(info);
        RenderTo(surface.Canvas);

        using var image = surface.Snapshot();
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        using var stream = File.Create(path);
        data.SaveTo(stream);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        IsVisible = false;
        _closed?.Invoke(this);
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

    /// <inheritdoc />
    public void RunWindowHooks(XPlatWindowPhase phase, XPlatWindow window)
    {
        // Empty on purpose: this backend declares no typed window hooks yet.
    }
}
