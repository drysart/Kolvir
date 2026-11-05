namespace XPlatUI.Animations.Tests;

/// <summary>
/// A frame source a test fires by hand, and the record of what the scheduler asked it to do.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="IsRunning"/>, <see cref="Starts"/> and <see cref="Stops"/> are the whole subject of the
/// scheduler's half of these tests: an idle application must run no timer, so "the clock is running"
/// and "the clock was started once and never restarted" are the assertions that matter, not just
/// the values the animations settle on.
/// </para>
/// <para>
/// <see cref="Frame"/> does nothing while the source is stopped, which is what a real wait does. A
/// test that advances the clock and fires a frame the scheduler was not waiting for therefore proves
/// that the value did not move.
/// </para>
/// </remarks>
internal sealed class ManualFrameSource : IXPlatAnimationFrameSource
{
    private Action? _tick;

    /// <summary>The last interval the scheduler asked for.</summary>
    public TimeSpan Interval { get; private set; }

    /// <summary>True between <see cref="Start"/> and <see cref="Stop"/>.</summary>
    public bool IsRunning { get; private set; }

    /// <summary>How many times the scheduler started the wait. One per idle-to-busy edge.</summary>
    public int Starts { get; private set; }

    /// <summary>How many times the scheduler stopped a wait that was running.</summary>
    public int Stops { get; private set; }

    public void Start(TimeSpan interval, Action tick)
    {
        ArgumentNullException.ThrowIfNull(tick);

        _tick = tick;
        Interval = interval;
        IsRunning = true;
        Starts++;
    }

    public void Stop()
    {
        if (!IsRunning)
        {
            return;
        }

        IsRunning = false;
        Stops++;
    }

    /// <summary>Runs one frame, if the scheduler is waiting for one.</summary>
    public void Frame()
    {
        if (IsRunning)
        {
            _tick?.Invoke();
        }
    }

    public void Dispose() => IsRunning = false;
}
