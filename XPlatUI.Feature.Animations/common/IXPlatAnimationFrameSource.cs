namespace XPlatUI.Animations;

/// <summary>
/// The repeating wait an <see cref="XPlatAnimationScheduler"/> drives its timeline with: it calls a
/// tick once per interval while it is running, and nothing at all while it is stopped.
/// </summary>
/// <remarks>
/// <para>
/// This is the seam that makes an animation testable without sleeping. Production code uses
/// <see cref="XPlatAnimationTimerFrameSource"/>, which is an <see cref="XPlatTimer"/> and therefore
/// ticks on the UI thread on every platform; a test supplies its own and fires the tick by hand
/// after moving a <see cref="TimeProvider"/> forward, so a whole animation runs in a millisecond of
/// wall time and every assertion is exact.
/// </para>
/// <para>
/// The scheduler owns exactly one frame source for the whole application and calls
/// <see cref="Start"/> when the first set begins and <see cref="Stop"/> when the last one is no
/// longer running, so an idle application runs no timer - the rule
/// <c>CONTROLS-TODO.md</c> section 2 sets for everything time-based in the library.
/// </para>
/// </remarks>
public interface IXPlatAnimationFrameSource : IDisposable
{
    /// <summary>
    /// Starts the wait, or updates the interval of one that is already running. The first
    /// <paramref name="tick"/> arrives one <paramref name="interval"/> after this call.
    /// </summary>
    /// <param name="interval">The time between frames; always positive.</param>
    /// <param name="tick">The per-frame callback. It must not block.</param>
    void Start(TimeSpan interval, Action tick);

    /// <summary>Stops the wait. A tick already in flight may still arrive.</summary>
    void Stop();
}
