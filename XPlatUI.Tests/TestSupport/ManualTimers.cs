using Microsoft.Extensions.DependencyInjection;
using XPlatUI;
using XPlatUI.Backend.Headless;

namespace XPlatUI.Tests;

/// <summary>
/// A timer the test fires by hand.
/// </summary>
/// <remarks>
/// <para>
/// Popup delays are the one part of the library that is genuinely time-based: a tool tip waits
/// before it appears and then hides itself again. <see cref="XPlatUndoClock"/> does not reach them -
/// a timer asks the application backend for its wait - so a test that wants an exact delay owns the
/// timer instead of waiting for one.
/// </para>
/// <para>
/// <see cref="Fire"/> runs the callback the library supplied, which is what a real timer does one
/// interval later. The library still decides when to start and stop it, so an assertion about
/// <see cref="IsRunning"/> and <see cref="Interval"/> is an assertion about the control's timing.
/// </para>
/// </remarks>
internal sealed class ManualTimerBackend : IXPlatTimerBackend
{
    private Action? _tick;

    /// <summary>The last interval the library asked for.</summary>
    public TimeSpan Interval { get; private set; }

    /// <summary>True between <see cref="Start"/> and <see cref="Stop"/>.</summary>
    public bool IsRunning { get; private set; }

    /// <summary>How many times the library started the timer.</summary>
    public int Starts { get; private set; }

    /// <summary>
    /// How many timers the library bound to this backend. A control that reuses one
    /// <see cref="XPlatTimer"/> across many starts attaches once, so a count above one is a timer
    /// that was thrown away and rebuilt - which is what a leak per visibility change looks like.
    /// </summary>
    public int Attachments { get; private set; }

    /// <summary>Binds the tick a new <see cref="XPlatTimer"/> wants and hands this backend back.</summary>
    public IXPlatTimerBackend Attach(Action tick)
    {
        _tick = tick;
        Attachments++;
        return this;
    }

    public void Start(TimeSpan interval)
    {
        Interval = interval;
        IsRunning = true;
        Starts++;
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
/// The headless application backend with one difference: every timer it hands out is a
/// <see cref="ManualTimerBackend"/>. Everything else is the headless backend's own behaviour, which
/// is what keeps these tests about the control rather than about the harness.
/// </summary>
internal sealed class TestApplicationBackend : IXPlatApplicationBackend
{
    private readonly HeadlessApplicationBackend _inner = new();

    /// <summary>The timer the library was given. Every request returns it, so a test drives one.</summary>
    public ManualTimerBackend Timer { get; } = new();

    /// <summary>The wrapped headless backend, for anything a test wants to reach directly.</summary>
    public HeadlessApplicationBackend Headless => _inner;

    /// <summary>
    /// Runs the work the library queued for the UI thread. A timer tick is moved to the UI thread
    /// through <see cref="XPlatApplication.BeginInvoke"/>, and a headless run drains that queue
    /// rather than looping, so a test asks for it explicitly.
    /// </summary>
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
