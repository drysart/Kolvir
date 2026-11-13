using Microsoft.Extensions.DependencyInjection;
using Kolvir.Backend.Headless;
using Kolvir.Undo;

namespace Kolvir.Audio.Tests;

/// <summary>
/// A timer the test fires by hand.
/// </summary>
/// <remarks>
/// The null device's timeline is the one part of this feature that is genuinely time-based: a voice
/// on the silent device has no other way to know its clip ran out. A test that waited for a real timer
/// would be a slow test about the scheduler, so the test owns the wait instead and asserts on the
/// interval the timeline asked for.
/// </remarks>
internal sealed class ManualTimerBackend : ITimerBackend
{
    private Action? _tick;

    /// <summary>The last interval the timeline asked for.</summary>
    public TimeSpan Interval { get; private set; }

    /// <summary>True between <see cref="Start"/> and <see cref="Stop"/>.</summary>
    public bool IsRunning { get; private set; }

    /// <summary>Binds the callback a new timeline timer wants and hands this backend back.</summary>
    public ITimerBackend Attach(Action tick)
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

    /// <summary>Runs one interval's worth of the callback, if the timeline has it running.</summary>
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
/// <see cref="ManualTimerBackend"/>. Everything else is the headless backend's own behaviour.
/// </summary>
internal sealed class TestApplicationBackend : IApplicationBackend
{
    private readonly HeadlessApplicationBackend _inner = new();

    /// <summary>The timer the feature was given. Every request returns it, so a test drives one.</summary>
    public ManualTimerBackend Timer { get; } = new();

    /// <summary>Runs the work the feature queued for the UI thread.</summary>
    public void Drain() => _inner.DrainPending();

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

    public IWindowBackend CreateWindow(Window window) => _inner.CreateWindow(window);

    public ITimerBackend CreateTimer(Action tick) => Timer.Attach(tick);

    public void RegisterServices(IServiceCollection services) => _inner.RegisterServices(services);

    public IReadOnlyList<Screen> GetScreens() => _inner.GetScreens();

    public void Shutdown() => _inner.Shutdown();

    public void RunApplicationHooks(ApplicationPhase phase, Application application) =>
        _inner.RunApplicationHooks(phase, application);
}

/// <summary>
/// A clock that only moves when a test says so, one tick per millisecond.
/// </summary>
/// <remarks>
/// Every position this feature reports is computed from <see cref="UndoClock.Current"/>, so a test
/// drives the sound instead of waiting for it. Nothing in this suite sleeps.
/// </remarks>
internal sealed class AudioTestClock : TimeProvider
{
    private long _ticks;

    public override long TimestampFrequency => 1_000;

    public override long GetTimestamp() => _ticks;

    public override DateTimeOffset GetUtcNow() => DateTimeOffset.UnixEpoch.AddMilliseconds(_ticks);

    /// <summary>Moves the clock forward by <paramref name="milliseconds"/>.</summary>
    public void Advance(long milliseconds) => _ticks += milliseconds;
}
