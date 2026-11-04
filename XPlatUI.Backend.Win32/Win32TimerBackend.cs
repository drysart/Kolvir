namespace XPlatUI.Backend.Win32;

/// <summary>
/// The Win32 <see cref="IXPlatTimerBackend"/>: a high-resolution waitable timer for intervals the
/// default system timer period cannot express, and a pool timer for everything else.
/// </summary>
/// <remarks>
/// <para>
/// The system timer runs at 64 Hz, so <see cref="System.Threading.Timer"/> cannot tick faster than
/// 15.625 ms however small the interval is: a 4 ms interval still fires every 15.6 ms. A waitable
/// timer created with <c>CREATE_WAITABLE_TIMER_HIGH_RESOLUTION</c> (Windows 10 1803 and later) is
/// not bound by that period, so a sub-period interval really does tick at its own rate.
/// </para>
/// <para>
/// That flag is preferred to <c>timeBeginPeriod</c>, which raises the period for the whole process
/// and, before Windows 10 2004, for every other process on the machine too. Nothing here changes
/// process state, so an interval is only ever as accurate as its own timer.
/// </para>
/// <para>
/// The waitable timer counts in whole milliseconds, so a sub-period interval is honoured to the
/// nearest millisecond - 4.17 ms becomes 4 ms. That is well inside what a display can show, and
/// anything that needs the real elapsed time already measures it.
/// </para>
/// </remarks>
internal sealed class Win32TimerBackend : IXPlatTimerBackend
{
    /// <summary>64 Hz: the period a default waitable timer or a pool timer rounds up to.</summary>
    private static readonly TimeSpan SystemTimerPeriod = TimeSpan.FromMilliseconds(15.625);

    private readonly Action _tick;
    private readonly Action<IXPlatTimerBackend> _forget;
    private readonly object _gate = new();

    private ThreadPoolTimerBackend? _pool;
    private HighResolutionTimer? _fine;
    private bool _disposed;

    /// <param name="tick">The callback to run once per interval.</param>
    /// <param name="forget">Called when the timer is disposed, so the backend can drop its reference.</param>
    public Win32TimerBackend(Action tick, Action<IXPlatTimerBackend> forget)
    {
        _tick = tick;
        _forget = forget;
    }

    /// <inheritdoc />
    public void Start(TimeSpan interval)
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            if (interval < SystemTimerPeriod)
            {
                // Null when the OS will not give us a high-resolution timer, in which case the
                // pool timer below is the best that can be done.
                _fine ??= HighResolutionTimer.TryCreate(_tick);

                if (_fine is not null)
                {
                    _pool?.Stop();
                    _fine.Start(interval);
                    return;
                }
            }

            _fine?.Stop();
            (_pool ??= new ThreadPoolTimerBackend(_tick)).Start(interval);
        }
    }

    /// <inheritdoc />
    public void Stop()
    {
        lock (_gate)
        {
            _fine?.Stop();
            _pool?.Stop();
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _fine?.Dispose();
            _fine = null;
            _pool?.Dispose();
            _pool = null;
        }

        _forget(this);
    }

    /// <summary>
    /// A waitable timer waited on by a thread of its own: the handle signals every period and the
    /// thread turns each signal into a tick.
    /// </summary>
    private sealed class HighResolutionTimer : IDisposable
    {
        /// <summary>How long a wait lasts before the thread re-checks whether it is still wanted.</summary>
        private const uint WakeupInterval = 100;

        private readonly Action _tick;
        private readonly Thread _thread;
        private readonly IntPtr _handle;
        private readonly object _gate = new();

        private bool _armed;
        private volatile bool _disposed;

        private HighResolutionTimer(Action tick, IntPtr handle)
        {
            _tick = tick;
            _handle = handle;
            _thread = new Thread(Run)
            {
                IsBackground = true,
                Name = "XPlatUI high-resolution timer",
            };

            _thread.Start();
        }

        /// <summary>Creates the timer, or returns null when the OS has no high-resolution timer.</summary>
        public static HighResolutionTimer? TryCreate(Action tick)
        {
            var handle = NativeMethods.CreateWaitableTimerExW(
                IntPtr.Zero,
                IntPtr.Zero,
                NativeMethods.CreateWaitableTimerHighResolution,
                NativeMethods.TimerAllAccess);

            return handle == IntPtr.Zero ? null : new HighResolutionTimer(tick, handle);
        }

        public void Start(TimeSpan interval)
        {
            lock (_gate)
            {
                if (_disposed)
                {
                    return;
                }

                _armed = true;

                // Armed here rather than by the waiting thread so the first tick is not delayed by
                // however long that thread takes to come back around its loop.
                var due = -(long)(interval.TotalMilliseconds * 10_000d);
                var period = Math.Max(1, (int)Math.Round(interval.TotalMilliseconds));
                NativeMethods.SetWaitableTimer(_handle, ref due, period, IntPtr.Zero, IntPtr.Zero, false);
            }
        }

        public void Stop()
        {
            lock (_gate)
            {
                _armed = false;

                if (!_disposed)
                {
                    NativeMethods.CancelWaitableTimer(_handle);
                }
            }
        }

        public void Dispose()
        {
            lock (_gate)
            {
                if (_disposed)
                {
                    return;
                }

                _disposed = true;
                _armed = false;
                NativeMethods.CancelWaitableTimer(_handle);
            }

            // The wait is bounded, so this joins promptly. A tick that is running on this thread
            // has nothing to join.
            if (_thread != Thread.CurrentThread)
            {
                _thread.Join((int)WakeupInterval + 50);
            }

            NativeMethods.CloseHandle(_handle);
        }

        private void Run()
        {
            while (true)
            {
                var signalled = NativeMethods.WaitForSingleObject(_handle, WakeupInterval) == NativeMethods.WaitObject0;

                if (_disposed)
                {
                    return;
                }

                if (!signalled)
                {
                    continue;
                }

                bool armed;
                lock (_gate)
                {
                    armed = _armed;
                }

                if (armed)
                {
                    _tick();
                }
            }
        }
    }
}
