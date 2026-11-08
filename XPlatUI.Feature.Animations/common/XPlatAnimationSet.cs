namespace XPlatUI.Animations;

/// <summary>
/// One or more value animations running on a shared timeline: a thing an application starts, pauses,
/// restarts and ends, and a handler it can subscribe to for the moment the timeline is over.
/// </summary>
/// <remarks>
/// <para>
/// Every animation in a set is measured from the same zero, so <c>startTime</c> is what staggers
/// them: three tiles that each start 80 ms after the one before are three animations in one set, not
/// three sets. A set's length is the end of its last animation, so adding a later animation makes
/// the set longer rather than pushing the timeline past its own end.
/// </para>
/// <para>
/// <b>The lifecycle.</b> <see cref="Start"/> begins the timeline, <see cref="Pause"/> freezes it,
/// <see cref="Resume"/> continues it, <see cref="Restart"/> puts it back to the beginning and plays
/// it again, and <see cref="End"/> finishes it immediately. The decisions that are not implied by
/// those four words:
/// </para>
/// <list type="bullet">
/// <item><see cref="Start"/> on a set that is already running does nothing - it is not a restart -
/// and on a set that is paused it resumes, because a start on a paused set is never a request to
/// begin again. Both mean calling it twice is harmless.</item>
/// <item><see cref="Resume"/> exists even though the request listed four methods: a pause with no
/// resume is a trap, and the alternative - overloading <see cref="Start"/> to mean two things - is
/// the same trap spelled differently.</item>
/// <item><see cref="Restart"/> works from any state, including one that has already finished, and
/// counts as a new run: the finished handler runs again for it.</item>
/// <item><see cref="End"/> mid-flight snaps every animation to its end value and raises the
/// finished handler, because the timeline is over however it got there. Ending one that has already
/// finished does nothing, so the handler still runs exactly once per run.</item>
/// <item>A set whose length is zero - no animations, or only zero-duration ones at time zero -
/// finishes inside <see cref="Start"/> without ever asking for a frame, so it costs the application
/// no timer at all.</item>
/// </list>
/// <para>
/// <b>What the handler sees.</b> It is the set itself: an event with no payload has no second
/// parameter (FEATURE-COST-POLICY.md §5.4), so the shape is
/// <c>AddFinishedHandler(Action&lt;object&gt;)</c> and the argument is the
/// <see cref="XPlatAnimationSet"/> whose timeline ended. The handler runs once per run, on the UI
/// thread, after the values are already at their end. A set that ends inside a frame runs it before
/// that frame returns, so <see cref="XPlatAnimationScheduler.IsRunning"/> still reads true from
/// inside the handler; a handler that starts or restarts a set therefore keeps the clock running
/// instead of stopping and starting it again.
/// </para>
/// <para>
/// The animation list is fixed by the first <see cref="Start"/>: adding one after the timeline has
/// begun throws, because a timeline whose length changes underneath the frame it is being measured
/// against cannot be made to behave predictably. Build the set, then run it.
/// </para>
/// </remarks>
public sealed class XPlatAnimationSet : IDisposable
{
    private readonly List<XPlatValueAnimation> _animations = [];
    private Action<object>? _finished;
    private XPlatAnimationScheduler? _scheduler;
    private TimeSpan _elapsedBase;
    private TimeSpan _length;
    private long _epoch;
    private XPlatAnimationState _state = XPlatAnimationState.NotStarted;
    private bool _startedOnce;
    private bool _finishedRaised;
    private bool _disposed;

    /// <summary>
    /// Creates a set that runs on the application's scheduler - the one <c>AddAnimations()</c>
    /// registered.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// No scheduler is registered, thrown by the first <see cref="Start"/> rather than here, so a
    /// set can be built before the application that runs it exists.
    /// </exception>
    public XPlatAnimationSet()
    {
    }

    /// <summary>Creates a set that runs on <paramref name="scheduler"/>.</summary>
    /// <param name="scheduler">The clock to attach to.</param>
    /// <exception cref="ArgumentNullException"><paramref name="scheduler"/> is null.</exception>
    /// <remarks>
    /// An application normally uses the parameterless constructor; this overload is for a host that
    /// owns its own clock, and for a test that wants to move time by hand.
    /// </remarks>
    public XPlatAnimationSet(XPlatAnimationScheduler scheduler)
    {
        ArgumentNullException.ThrowIfNull(scheduler);
        _scheduler = scheduler;
    }

    /// <summary>The scheduler this set runs on.</summary>
    /// <exception cref="InvalidOperationException">No scheduler is registered.</exception>
    public XPlatAnimationScheduler Scheduler => ResolveScheduler();

    /// <summary>Where the timeline is.</summary>
    public XPlatAnimationState State => _state;

    /// <summary>True between <see cref="Start"/> and the end of the run.</summary>
    public bool IsRunning => _state == XPlatAnimationState.Running;

    /// <summary>True once the timeline has reached its end, been ended, or the set was disposed.</summary>
    public bool IsFinished => _state == XPlatAnimationState.Finished;

    /// <summary>
    /// How long the set runs: the end of its last animation, measured from the beginning of the
    /// timeline. Zero for a set with no animations.
    /// </summary>
    public TimeSpan Length => _length;

    /// <summary>
    /// How far into the timeline the set is. Frozen while paused, and equal to <see cref="Length"/>
    /// once finished.
    /// </summary>
    public TimeSpan Elapsed => _state == XPlatAnimationState.Finished ? _length : CurrentElapsed();

    /// <summary>
    /// How much of the timeline has played, from 0 to 1. A set with no length reads 1 once it has
    /// finished and 0 before that.
    /// </summary>
    public double Progress
    {
        get
        {
            if (_length <= TimeSpan.Zero)
            {
                return _state == XPlatAnimationState.Finished ? 1d : 0d;
            }

            return Math.Clamp(Elapsed.TotalSeconds / _length.TotalSeconds, 0d, 1d);
        }
    }

    /// <summary>The animations on this timeline, in the order they were added.</summary>
    public IReadOnlyList<XPlatValueAnimation> Animations => _animations;

    /// <summary>
    /// Adds an animation to the timeline, and lengthens the set when it ends after everything else.
    /// </summary>
    /// <param name="animation">The animation to add.</param>
    /// <exception cref="ArgumentNullException"><paramref name="animation"/> is null.</exception>
    /// <exception cref="InvalidOperationException">The timeline has already been started.</exception>
    /// <exception cref="ObjectDisposedException">The set has been disposed.</exception>
    public void Add(XPlatValueAnimation animation)
    {
        ArgumentNullException.ThrowIfNull(animation);
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (_startedOnce)
        {
            throw new InvalidOperationException(
                "An animation cannot be added to a set that has already run. Build the set, then call Start; " +
                "a timeline whose length changes once it is running cannot be measured against.");
        }

        _animations.Add(animation);

        if (animation.EndTime > _length)
        {
            _length = animation.EndTime;
        }
    }

    /// <summary>
    /// Starts the timeline, or resumes one that is paused. Calling it on a set that is already
    /// running does nothing, and calling it on one that has finished does nothing either - use
    /// <see cref="Restart"/> for that.
    /// </summary>
    /// <exception cref="ObjectDisposedException">The set has been disposed.</exception>
    public void Start()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (_state == XPlatAnimationState.Paused)
        {
            Resume();
            return;
        }

        if (_state != XPlatAnimationState.NotStarted)
        {
            return;
        }

        BeginRun();
    }

    /// <summary>
    /// Freezes the timeline where it is. The scheduler lets go of the set, so a paused set stops the
    /// application's frame clock when it was the last one running.
    /// </summary>
    public void Pause()
    {
        if (_state != XPlatAnimationState.Running)
        {
            return;
        }

        _elapsedBase = CurrentElapsed();
        _state = XPlatAnimationState.Paused;
        _scheduler?.Detach(this);
    }

    /// <summary>Continues a paused timeline from where it stopped. Does nothing on any other state.</summary>
    /// <exception cref="ObjectDisposedException">The set has been disposed.</exception>
    public void Resume()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (_state != XPlatAnimationState.Paused)
        {
            return;
        }

        var scheduler = ResolveScheduler();
        _epoch = scheduler.Time.GetTimestamp();
        _state = XPlatAnimationState.Running;
        scheduler.Attach(this);
    }

    /// <summary>
    /// Puts the timeline back to the beginning and plays it again, from any state. This is a new
    /// run, so the finished handler will run again when it ends.
    /// </summary>
    /// <exception cref="ObjectDisposedException">The set has been disposed.</exception>
    public void Restart()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (_state == XPlatAnimationState.Running)
        {
            _scheduler?.Detach(this);
        }

        _state = XPlatAnimationState.NotStarted;
        _elapsedBase = TimeSpan.Zero;
        BeginRun();
    }

    /// <summary>
    /// Finishes the timeline now: every animation is assigned its end value and the finished handler
    /// runs, once. Does nothing to a set that has already finished, or has been disposed.
    /// </summary>
    public void End()
    {
        if (_disposed || _state == XPlatAnimationState.Finished)
        {
            return;
        }

        if (_state == XPlatAnimationState.NotStarted)
        {
            // Ending one that never began is still a run, so the list is closed to additions and the
            // end values are assigned even when they happen to match the start values.
            _startedOnce = true;
            ResetApplied();
        }

        ApplyAt(_length);
        Finish();
    }

    /// <summary>Adds a handler that runs when the timeline ends, however it ends.</summary>
    /// <param name="handler">The handler to add. Its argument is this set.</param>
    /// <returns>A token that removes the handler when it is disposed.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="handler"/> is null.</exception>
    /// <exception cref="ObjectDisposedException">The set has been disposed.</exception>
    public IDisposable AddFinishedHandler(Action<object> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        ObjectDisposedException.ThrowIf(_disposed, this);

        _finished += handler;
        return new XPlatHandlerToken<Action<object>>(RemoveFinishedHandler, handler);
    }

    /// <summary>Removes a handler that <see cref="AddFinishedHandler"/> added.</summary>
    /// <param name="handler">The handler to remove.</param>
    /// <exception cref="ArgumentNullException"><paramref name="handler"/> is null.</exception>
    public void RemoveFinishedHandler(Action<object> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);

        _finished -= handler;
    }

    /// <summary>
    /// Stops the timeline and drops every finished handler. The set reports
    /// <see cref="XPlatAnimationState.Finished"/> afterwards and never advances again.
    /// </summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _scheduler?.Detach(this);
        _state = XPlatAnimationState.Finished;
        _finished = null;
    }

    /// <summary>Advances the timeline one frame. Called by the scheduler.</summary>
    internal void Advance()
    {
        if (_state != XPlatAnimationState.Running || _scheduler is not { } scheduler)
        {
            return;
        }

        TimeSpan elapsed = _elapsedBase + scheduler.Time.GetElapsedTime(_epoch);

        if (elapsed >= _length)
        {
            // The last frame lands exactly on the end rather than one frame short of it, so a short
            // animation that overshoots its last frame still finishes on its end value.
            elapsed = _length;
        }

        ApplyAt(elapsed);

        if (elapsed >= _length)
        {
            Finish();
        }
    }

    private void BeginRun()
    {
        _startedOnce = true;
        _finishedRaised = false;
        _elapsedBase = TimeSpan.Zero;
        ResetApplied();

        // The start values land before the first frame, not one frame after Start, so a restarted
        // animation does not flash its previous end value.
        ApplyAt(TimeSpan.Zero);

        if (_length <= TimeSpan.Zero)
        {
            Finish();
            return;
        }

        var scheduler = ResolveScheduler();
        _epoch = scheduler.Time.GetTimestamp();
        _state = XPlatAnimationState.Running;
        scheduler.Attach(this);
    }

    private void Finish()
    {
        _elapsedBase = _length;
        _state = XPlatAnimationState.Finished;
        _scheduler?.Detach(this);

        if (_finishedRaised)
        {
            return;
        }

        _finishedRaised = true;

        // Read once: a handler that removes itself, or is removed by an earlier handler, must not
        // change the list the foreach is walking.
        if (_finished is not { } handlers)
        {
            return;
        }

        try
        {
            handlers(this);
        }
        catch (Exception ex)
        {
            XPlatApplication.HandleNativeBoundary(ex);
        }
    }

    private void ApplyAt(TimeSpan elapsed)
    {
        foreach (var animation in _animations)
        {
            animation.Advance(elapsed);
        }
    }

    private void ResetApplied()
    {
        foreach (var animation in _animations)
        {
            animation.ResetApplied();
        }
    }

    private TimeSpan CurrentElapsed()
    {
        if (_state != XPlatAnimationState.Running || _scheduler is not { } scheduler)
        {
            return _elapsedBase;
        }

        TimeSpan elapsed = _elapsedBase + scheduler.Time.GetElapsedTime(_epoch);
        return elapsed > _length ? _length : elapsed;
    }

    private XPlatAnimationScheduler ResolveScheduler()
    {
        if (_scheduler is { } existing)
        {
            return existing;
        }

        return _scheduler = XPlatAnimationScheduler.FindRegistered()
            ?? throw new InvalidOperationException(
                "No XPlatAnimationScheduler is registered for this application. Call services.AddAnimations() " +
                "from XPlatApplicationOptions.ConfigureServices, or pass a scheduler to the XPlatAnimationSet " +
                "constructor.");
    }
}
