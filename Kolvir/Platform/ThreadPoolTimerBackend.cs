namespace Kolvir;

/// <summary>
/// The portable <see cref="ITimerBackend"/>: a <see cref="System.Threading.Timer"/> that calls
/// the tick on a pool thread.
/// </summary>
/// <remarks>
/// This is the answer for a backend whose platform timer is already fine-grained - macOS and Linux
/// both honour a sub-millisecond interval - and for the headless backend, where the interval only
/// has to be plausible. On Windows it is coarse below the system timer period, so
/// <see cref="IApplicationBackend.CreateTimer"/> there returns something else for those
/// intervals.
/// </remarks>
public sealed class ThreadPoolTimerBackend : ITimerBackend
{
    private readonly System.Threading.Timer _timer;
    private readonly Action _tick;
    private bool _disposed;

    /// <summary>Creates a stopped timer.</summary>
    /// <param name="tick">The callback to run once per interval. It must not block.</param>
    public ThreadPoolTimerBackend(Action tick)
    {
        ArgumentNullException.ThrowIfNull(tick);

        _tick = tick;
        _timer = new System.Threading.Timer(
            static state => ((ThreadPoolTimerBackend)state!).Ticked(),
            this,
            Timeout.Infinite,
            Timeout.Infinite);
    }

    /// <inheritdoc />
    public void Start(TimeSpan interval)
    {
        if (_disposed)
        {
            return;
        }

        try
        {
            _timer.Change(interval, interval);
        }
        catch (ObjectDisposedException)
        {
            // Raced with Dispose: the timer is gone and nothing is left to start.
        }
    }

    /// <inheritdoc />
    public void Stop()
    {
        if (_disposed)
        {
            return;
        }

        try
        {
            _timer.Change(Timeout.Infinite, Timeout.Infinite);
        }
        catch (ObjectDisposedException)
        {
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _timer.Dispose();
    }

    private void Ticked()
    {
        if (!_disposed)
        {
            _tick();
        }
    }
}
