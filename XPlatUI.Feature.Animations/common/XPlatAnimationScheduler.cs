namespace XPlatUI.Animations;

/// <summary>
/// The one frame clock an application's animations share: every <see cref="XPlatAnimationSet"/>
/// attaches to it, it advances all of them from the same frame, and it runs no timer at all while
/// nothing is animating.
/// </summary>
/// <remarks>
/// <para>
/// <c>AddAnimations()</c> registers one of these as a singleton, which is the whole opt-in: an
/// application that does not call it has no scheduler, no clock and no animation code in its
/// executable. One clock for the application rather than one per control is what makes a scene of
/// several animations move together instead of each drifting on its own timer, and it is the reason
/// the request asked for an application-level scheduler rather than a per-set one.
/// </para>
/// <para>
/// <b>Idle means no timer.</b> The clock starts when the first set starts and stops when the last
/// one finishes, pauses or is disposed, so an application sitting still runs nothing - the rule
/// <c>CONTROLS-TODO.md</c> section 2 applies to everything time-based in the library. The two edges
/// that are easy to get wrong are handled here rather than in the set:
/// </para>
/// <list type="bullet">
/// <item>A set that finishes inside a frame detaches itself while the frame is still running, so the
/// decision to stop is taken once, at the end of the frame, against the sets that are still running.
/// A set that started from another animation's callback during that same frame therefore keeps the
/// clock alive instead of losing a race with the stop.</item>
/// <item>A set that starts while the clock is already running attaches without asking for another
/// <see cref="IXPlatAnimationFrameSource.Start"/>, so a callback that starts an animation cannot
/// restart the wait underneath the frame it is running in.</item>
/// </list>
/// <para>
/// One consequence is worth stating because it is observable: a set that finishes inside a frame
/// runs its finished handler before the frame is over, so <see cref="IsRunning"/> still reports true
/// from inside that handler and the clock stops when the frame returns. A set that finishes outside
/// a frame - from <see cref="XPlatAnimationSet.End"/> called by application code, say - stops the
/// clock before the call returns. The visible difference is only in that instant; what matters is
/// that no frame is ever cut short and that the clock is never stopped and started again for one
/// frame's worth of work.
/// </para>
/// <para>
/// Time comes from a <see cref="System.TimeProvider"/> rather than from a frame counter, so a frame
/// the platform is late for makes the animation skip ahead instead of slowing down, and so a test
/// can move time by hand and drive a whole animation without sleeping. The wait itself comes from an
/// <see cref="IXPlatAnimationFrameSource"/>, which is the second seam and the one a test owns when it
/// wants to decide exactly when a frame happens.
/// </para>
/// <para>
/// Every member is UI-thread only, apart from <see cref="Dispose"/>, which is safe from any thread.
/// </para>
/// </remarks>
public sealed class XPlatAnimationScheduler : IDisposable
{
    private readonly List<XPlatAnimationSet> _sets = [];
    private readonly IXPlatAnimationFrameSource _frames;
    private TimeSpan _frameInterval = DefaultFrameInterval;
    private bool _clockRunning;
    private bool _ticking;
    private bool _disposed;

    /// <summary>Creates a scheduler that reads the system clock and waits with an <see cref="XPlatTimer"/>.</summary>
    public XPlatAnimationScheduler()
        : this(TimeProvider.System, new XPlatAnimationTimerFrameSource())
    {
    }

    /// <summary>Creates a scheduler over a clock and a frame source, for an application or a test that owns them.</summary>
    /// <param name="time">The clock the timeline is measured against.</param>
    /// <param name="frames">The wait the frames arrive on. The scheduler owns it and disposes it.</param>
    /// <exception cref="ArgumentNullException"><paramref name="time"/> or <paramref name="frames"/> is null.</exception>
    public XPlatAnimationScheduler(TimeProvider time, IXPlatAnimationFrameSource frames)
    {
        ArgumentNullException.ThrowIfNull(time);
        ArgumentNullException.ThrowIfNull(frames);

        Time = time;
        _frames = frames;
    }

    /// <summary>
    /// How often a frame arrives: sixty a second, which is a frame per screen refresh on the displays
    /// most applications run on and is smooth enough that a short animation does not visibly step.
    /// </summary>
    /// <remarks>
    /// Matching the display's refresh rate would be better and is deliberately not attempted: no
    /// backend reports it, and a rate the platform's timer cannot honour would make the animation
    /// both late and uneven. A single interval for every display is the smaller lie.
    /// </remarks>
    public static TimeSpan DefaultFrameInterval { get; } = TimeSpan.FromSeconds(1d / 60d);

    /// <summary>The clock the timeline is measured against.</summary>
    public TimeProvider Time { get; }

    /// <summary>
    /// The time between frames. Changing it while the clock is running restarts the wait at the new
    /// interval, so the next frame arrives a whole new interval later.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">The value is not positive.</exception>
    public TimeSpan FrameInterval
    {
        get => _frameInterval;
        set
        {
            if (value <= TimeSpan.Zero)
            {
                throw new ArgumentOutOfRangeException(nameof(value), value, "The frame interval must be positive.");
            }

            _frameInterval = value;

            if (_clockRunning)
            {
                _frames.Start(_frameInterval, Tick);
            }
        }
    }

    /// <summary>
    /// True while the application is running a frame clock, i.e. while at least one animation set is
    /// running. False on an idle application, which is the whole point of the scheduler.
    /// </summary>
    public bool IsRunning => _clockRunning;

    /// <summary>How many animation sets the clock is currently advancing.</summary>
    internal int RunningSetCount => _sets.Count;

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _sets.Clear();
        _clockRunning = false;
        _frames.Dispose();
    }

    /// <summary>Adds <paramref name="set"/> to the frame and starts the clock if it is not running.</summary>
    /// <param name="set">The set that has just started or resumed.</param>
    internal void Attach(XPlatAnimationSet set)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (_sets.Contains(set))
        {
            return;
        }

        _sets.Add(set);
        EnsureRunning();
    }

    /// <summary>Removes <paramref name="set"/> from the frame, stopping the clock when it was the last one.</summary>
    /// <param name="set">The set that finished, paused or was disposed.</param>
    internal void Detach(XPlatAnimationSet set)
    {
        if (_disposed || !_sets.Remove(set))
        {
            return;
        }

        StopIfIdle();
    }

    private void EnsureRunning()
    {
        if (_clockRunning || _sets.Count == 0)
        {
            return;
        }

        // Set before asking for the wait: a frame source that ticks from Start would otherwise see
        // a scheduler that does not believe it is running.
        _clockRunning = true;
        _frames.Start(_frameInterval, Tick);
    }

    private void StopIfIdle()
    {
        // A set finishing inside a frame takes the last entry out of the list while the frame is
        // still walking it. Stopping there would cut the frame short and, worse, would stop the
        // clock just before a callback that is about to start another set. The decision is taken
        // once, by Tick, after every set has had its frame.
        if (!_clockRunning || _ticking || _sets.Count != 0)
        {
            return;
        }

        _clockRunning = false;
        _frames.Stop();
    }

    private void Tick()
    {
        if (_disposed || _ticking)
        {
            return;
        }

        _ticking = true;

        try
        {
            if (_sets.Count > 0)
            {
                // A snapshot, because a callback may finish its own set or start another one, and
                // neither should change who gets this frame.
                foreach (var set in _sets.ToArray())
                {
                    set.Advance();
                }
            }
        }
        finally
        {
            _ticking = false;
        }

        StopIfIdle();
    }
}
