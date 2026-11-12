namespace Kolvir;

/// <summary>
/// A repeating timer whose <see cref="AddTickHandler"/> always runs on the UI thread, so handlers
/// may touch controls without marshalling.
/// </summary>
/// <remarks>
/// <para>
/// The wait itself happens in the platform's <see cref="ITimerBackend"/>; only the
/// <see cref="AddTickHandler"/> callback is moved to the UI thread, through
/// <see cref="Application.BeginInvoke"/>. That keeps the message loop free of any timer
/// bookkeeping and gives every platform the same behaviour.
/// </para>
/// <para>
/// Ticks are not queued up: if the loop is busy for longer than <see cref="Interval"/>, missed
/// ticks are dropped rather than replayed. Before any <see cref="Application"/> exists - or
/// once it has been disposed - ticks run inline on the thread that waited for them.
/// </para>
/// <para>
/// The underlying timer is created on the first <see cref="Start" /> and comes from the running
/// application's backend, so a timer waits the way its platform wants. A timer that never starts
/// never asks for one.
/// </para>
/// </remarks>
public sealed class Timer : IDisposable
{
    private readonly object _gate = new();
    private Action<object>? _tick;
    private ITimerBackend? _backend;
    private IApplicationBackend? _owner;
    private TimeSpan _interval;
    private bool _enabled;
    private bool _disposed;
    private int _ticking;

    /// <summary>Creates a stopped timer with a 250 ms interval.</summary>
    public Timer()
        : this(TimeSpan.FromMilliseconds(250))
    {
    }

    /// <summary>Creates a stopped timer.</summary>
    /// <param name="interval">The interval between ticks; must be positive.</param>
    public Timer(TimeSpan interval) => _interval = ValidateInterval(interval);

    /// <summary>The interval between ticks. Changing it while the timer is running restarts it.</summary>
    /// <exception cref="ArgumentOutOfRangeException">The value is not positive.</exception>
    public TimeSpan Interval
    {
        get => _interval;
        set
        {
            var interval = ValidateInterval(value);
            if (_interval == interval)
            {
                return;
            }

            _interval = interval;

            if (_enabled)
            {
                Restart();
            }
        }
    }

    /// <summary>Whether the timer is running. Setting it is the same as calling <see cref="Start"/> or <see cref="Stop"/>.</summary>
    public bool Enabled
    {
        get => _enabled;
        set
        {
            if (value)
            {
                Start();
            }
            else
            {
                Stop();
            }
        }
    }

    /// <summary>True once the timer has been disposed.</summary>
    public bool IsDisposed => _disposed;

    /// <summary>Adds a handler that runs on the UI thread every <see cref="Interval"/>.</summary>
    public IDisposable AddTickHandler(Action<object> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        _tick += handler;
        return new HandlerToken<Action<object>>(RemoveTickHandler, handler);
    }

    /// <summary>Removes a handler that <see cref="AddTickHandler"/> added.</summary>
    public void RemoveTickHandler(Action<object> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        _tick -= handler;
    }

    /// <summary>Starts the timer, or restarts it when it is already running.</summary>
    public void Start()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        _enabled = true;
        Restart();
    }

    /// <summary>Stops the timer. A tick already queued on the UI thread still arrives.</summary>
    public void Stop()
    {
        if (_disposed)
        {
            return;
        }

        _enabled = false;
        Volatile.Read(ref _backend)?.Stop();
    }

    /// <inheritdoc />
    public void Dispose()
    {
        ITimerBackend? backend;

        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _enabled = false;
            backend = _backend;
            _backend = null;
            _owner = null;
        }

        backend?.Dispose();
    }

    private static TimeSpan ValidateInterval(TimeSpan interval)
    {
        if (interval <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(interval), interval, "The interval must be positive.");
        }

        return interval;
    }

    private void Restart()
    {
        var owner = Application.Current?.Backend;
        ITimerBackend backend;
        ITimerBackend? replaced = null;

        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            if (_backend is null || !ReferenceEquals(_owner, owner))
            {
                // First start, or the application the wait belonged to has come or gone since:
                // the wait has to be built again against whoever is running now.
                replaced = _backend;
                _owner = owner;
                _backend = owner?.CreateTimer(OnTimer) ?? new ThreadPoolTimerBackend(OnTimer);
            }

            backend = _backend;
        }

        replaced?.Dispose();
        backend.Start(_interval);
    }

    private void OnTimer()
    {
        // A slow handler must not let ticks pile up behind it.
        if (_disposed || Interlocked.Exchange(ref _ticking, 1) == 1)
        {
            return;
        }

        var application = Application.Current;
        if (application is null)
        {
            RaiseTick();
            return;
        }

        application.BeginInvoke(RaiseTick);
    }

    private void RaiseTick()
    {
        try
        {
            if (!_disposed && _enabled)
            {
                _tick?.Invoke(this);
            }
        }
        finally
        {
            Volatile.Write(ref _ticking, 0);
        }
    }
}
