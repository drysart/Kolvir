using Microsoft.Extensions.DependencyInjection;
using SkiaSharp;
using Kolvir;
using Kolvir.Backend.Headless;

namespace Kolvir.Tests;

/// <summary>
/// The headless application backend with one difference: the window it hands out supports the two
/// window colours and records the concrete value it was given, so a test can assert what
/// <see cref="Window"/> resolved without a native window.
/// </summary>
internal sealed class ChromeColorApplicationBackend : IApplicationBackend
{
    private readonly HeadlessApplicationBackend _inner = new();

    /// <summary>The window backend the library was given, once a window has been created.</summary>
    public ChromeColorWindowBackend Window { get; private set; } = null!;

    public void Initialize(Application application) => _inner.Initialize(application);

    public int Run() => _inner.Run();

    public bool TryClaimSingleInstance() => _inner.TryClaimSingleInstance();

    public IDisposable AddActivatedHandler(Action<object> handler) => _inner.AddActivatedHandler(handler);

    public void RemoveActivatedHandler(Action<object> handler) => _inner.RemoveActivatedHandler(handler);

    public IDisposable AddSecondInstanceHandler(Action<object, SecondInstanceEventArgs> handler) =>
        _inner.AddSecondInstanceHandler(handler);

    public void RemoveSecondInstanceHandler(Action<object, SecondInstanceEventArgs> handler) =>
        _inner.RemoveSecondInstanceHandler(handler);

    public void Quit(int exitCode) => _inner.Quit(exitCode);

    public void Invoke(Action action, bool synchronous) => _inner.Invoke(action, synchronous);

    public IWindowBackend CreateWindow(Window window) =>
        Window = new ChromeColorWindowBackend(_inner.CreateWindow(window));

    public ITimerBackend CreateTimer(Action tick) => _inner.CreateTimer(tick);

    public void RegisterServices(IServiceCollection services) => _inner.RegisterServices(services);

    public IReadOnlyList<Screen> GetScreens() => _inner.GetScreens();

    public void Shutdown() => _inner.Shutdown();

    public void RunApplicationHooks(ApplicationPhase phase, Application application) =>
        _inner.RunApplicationHooks(phase, application);
}

/// <summary>
/// A headless window that accepts the two window colours and keeps the last concrete value it was
/// given. Everything else is the wrapped backend's own behaviour.
/// </summary>
internal sealed class ChromeColorWindowBackend : IWindowBackend
{
    private readonly IWindowBackend _inner;
    private SKColor? _captionColor;
    private SKColor? _borderColor;

    public ChromeColorWindowBackend(IWindowBackend inner) => _inner = inner;

    /// <summary>How many times the window handed the backend a caption colour, changed or not.</summary>
    public int CaptionColorPushes { get; private set; }

    /// <summary>How many times the window handed the backend a border colour, changed or not.</summary>
    public int BorderColorPushes { get; private set; }

    /// <summary>The last caption colour handed to the backend, or null for the platform's own.</summary>
    public SKColor? CaptionColor
    {
        get => _captionColor;
        set
        {
            CaptionColorPushes++;
            _captionColor = value;
        }
    }

    /// <summary>The last border colour handed to the backend, or null for the platform's own.</summary>
    public SKColor? BorderColor
    {
        get => _borderColor;
        set
        {
            BorderColorPushes++;
            _borderColor = value;
        }
    }

    /// <summary>Reports the one capability this backend exists to exercise.</summary>
    public ChromeCapabilities ChromeCapabilities => new() { WindowColors = true };

    public UiHost Host => _inner.Host;

    public float ScaleFactor => _inner.ScaleFactor;

    public Screen? Screen => _inner.Screen;

    public string Title { get => _inner.Title; set => _inner.Title = value; }

    public Icon? Icon { get => _inner.Icon; set => _inner.Icon = value; }

    public SKSize ClientSize { get => _inner.ClientSize; set => _inner.ClientSize = value; }

    public SKSize MinimumClientSize { get => _inner.MinimumClientSize; set => _inner.MinimumClientSize = value; }

    public SKSize MaximumClientSize { get => _inner.MaximumClientSize; set => _inner.MaximumClientSize = value; }

    public SKPoint Location { get => _inner.Location; set => _inner.Location = value; }

    public bool Resizable { get => _inner.Resizable; set => _inner.Resizable = value; }

    public bool TopMost { get => _inner.TopMost; set => _inner.TopMost = value; }

    public WindowState State { get => _inner.State; set => _inner.State = value; }

    public IWindowBackend? Owner { get => _inner.Owner; set => _inner.Owner = value; }

    public MenuBar? MenuBar { get => _inner.MenuBar; set => _inner.MenuBar = value; }

    public WindowChrome Chrome { get => _inner.Chrome; set => _inner.Chrome = value; }

    public CaptionButtons CaptionButtons { get => _inner.CaptionButtons; set => _inner.CaptionButtons = value; }

    public WindowChromeMetrics ChromeMetrics => _inner.ChromeMetrics;

    public bool TryBeginDrag() => _inner.TryBeginDrag();

    public bool TryBeginResize(ResizeEdge edge) => _inner.TryBeginResize(edge);

    public IDisposable AddChromeMetricsChangedHandler(Action<object> handler) =>
        _inner.AddChromeMetricsChangedHandler(handler);

    public void RemoveChromeMetricsChangedHandler(Action<object> handler) =>
        _inner.RemoveChromeMetricsChangedHandler(handler);

    public void Show() => _inner.Show();

    public void ShowDialog() => _inner.ShowDialog();

    public void Activate() => _inner.Activate();

    public void Close() => _inner.Close();

    public void SetTitleBarAppearance(bool dark) => _inner.SetTitleBarAppearance(dark);

    public IDisposable AddShownHandler(Action<object> handler) => _inner.AddShownHandler(handler);

    public void RemoveShownHandler(Action<object> handler) => _inner.RemoveShownHandler(handler);

    public IDisposable AddCloseRequestedHandler(Action<object> handler) => _inner.AddCloseRequestedHandler(handler);

    public void RemoveCloseRequestedHandler(Action<object> handler) => _inner.RemoveCloseRequestedHandler(handler);

    public IDisposable AddClosedHandler(Action<object> handler) => _inner.AddClosedHandler(handler);

    public void RemoveClosedHandler(Action<object> handler) => _inner.RemoveClosedHandler(handler);

    public IDisposable AddActivatedHandler(Action<object> handler) => _inner.AddActivatedHandler(handler);

    public void RemoveActivatedHandler(Action<object> handler) => _inner.RemoveActivatedHandler(handler);

    public IDisposable AddDeactivatedHandler(Action<object> handler) => _inner.AddDeactivatedHandler(handler);

    public void RemoveDeactivatedHandler(Action<object> handler) => _inner.RemoveDeactivatedHandler(handler);

    public IDisposable AddClientSizeChangedHandler(Action<object> handler) =>
        _inner.AddClientSizeChangedHandler(handler);

    public void RemoveClientSizeChangedHandler(Action<object> handler) =>
        _inner.RemoveClientSizeChangedHandler(handler);

    public IDisposable AddLocationChangedHandler(Action<object> handler) => _inner.AddLocationChangedHandler(handler);

    public void RemoveLocationChangedHandler(Action<object> handler) =>
        _inner.RemoveLocationChangedHandler(handler);

    public IDisposable AddScaleChangedHandler(Action<object> handler) => _inner.AddScaleChangedHandler(handler);

    public void RemoveScaleChangedHandler(Action<object> handler) => _inner.RemoveScaleChangedHandler(handler);

    public IDisposable AddStateChangedHandler(Action<object> handler) => _inner.AddStateChangedHandler(handler);

    public void RemoveStateChangedHandler(Action<object> handler) => _inner.RemoveStateChangedHandler(handler);

    public void RunWindowHooks(WindowPhase phase, Window window) => _inner.RunWindowHooks(phase, window);

    public void Dispose() => _inner.Dispose();
}
