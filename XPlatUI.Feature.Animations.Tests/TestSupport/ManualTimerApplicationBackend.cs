using Microsoft.Extensions.DependencyInjection;
using XPlatUI.Backend.Headless;

namespace XPlatUI.Animations.Tests;

/// <summary>
/// A timer the test fires by hand.
/// </summary>
/// <remarks>
/// The toast's countdown is the one timer an animated control in this feature still runs, and its
/// race with a dismissal is what the tests have to settle. Firing the timer rather than waiting for
/// one keeps the assertion about the control - how many removals it caused - rather than about how
/// long the test slept.
/// </remarks>
internal sealed class ManualTimerBackend : IXPlatTimerBackend
{
    private Action? _tick;

    /// <summary>The last interval the library asked for.</summary>
    public TimeSpan Interval { get; private set; }

    /// <summary>True between <see cref="Start"/> and <see cref="Stop"/>.</summary>
    public bool IsRunning { get; private set; }

    /// <summary>Binds the tick a new <see cref="XPlatTimer"/> wants and hands this backend back.</summary>
    public IXPlatTimerBackend Attach(Action tick)
    {
        _tick = tick;
        return this;
    }

    public void Start(TimeSpan interval)
    {
        Interval = interval;
        IsRunning = true;
    }

    public void Stop() => IsRunning = false;

    /// <summary>Runs one interval's worth of the callback, if the library has the timer running.</summary>
    public void Fire()
    {
        if (IsRunning)
        {
            _tick?.Invoke();
        }
    }

    public void Dispose() => IsRunning = false;
}

/// <summary>
/// The headless application backend with one difference: the timer it hands out is a
/// <see cref="ManualTimerBackend"/>, so a test owns the countdown. Everything else is the headless
/// backend's own behaviour, which keeps these tests about the control rather than about the harness.
/// </summary>
internal sealed class ManualTimerApplicationBackend : IXPlatApplicationBackend
{
    private readonly HeadlessApplicationBackend _inner = new();

    /// <summary>The timer the library was given. Every request returns it, so a test drives one.</summary>
    public ManualTimerBackend Timer { get; } = new();

    /// <summary>The wrapped headless backend, for the work queue a timer tick arrives on.</summary>
    public HeadlessApplicationBackend Headless => _inner;

    /// <summary>Runs the work the library queued for the UI thread, which is where a tick lands.</summary>
    public void Drain() => _inner.DrainPending();

    public void Initialize(XPlatApplication application) => _inner.Initialize(application);

    public int Run() => _inner.Run();

    public bool TryClaimSingleInstance() => _inner.TryClaimSingleInstance();

    public IDisposable AddActivatedHandler(Action<object> handler) => _inner.AddActivatedHandler(handler);

    public void RemoveActivatedHandler(Action<object> handler) => _inner.RemoveActivatedHandler(handler);

    public IDisposable AddSecondInstanceHandler(Action<object, XPlatSecondInstanceEventArgs> handler) =>
        _inner.AddSecondInstanceHandler(handler);

    public void RemoveSecondInstanceHandler(Action<object, XPlatSecondInstanceEventArgs> handler) =>
        _inner.RemoveSecondInstanceHandler(handler);

    public void Quit(int exitCode) => _inner.Quit(exitCode);

    public void Invoke(Action action, bool synchronous) => _inner.Invoke(action, synchronous);

    public IXPlatWindowBackend CreateWindow(XPlatWindow window) => _inner.CreateWindow(window);

    public IXPlatTimerBackend CreateTimer(Action tick) => Timer.Attach(tick);

    public void RegisterServices(IServiceCollection services) => _inner.RegisterServices(services);

    public IReadOnlyList<XPlatScreen> GetScreens() => _inner.GetScreens();

    public void Shutdown() => _inner.Shutdown();

    public void RunApplicationHooks(XPlatApplicationPhase phase, XPlatApplication application) =>
        _inner.RunApplicationHooks(phase, application);
}
