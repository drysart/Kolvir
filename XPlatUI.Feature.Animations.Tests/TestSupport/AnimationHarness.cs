namespace XPlatUI.Animations.Tests;

/// <summary>
/// Everything a test needs to drive an animation by hand: a clock, a frame source the test fires,
/// and the scheduler the two are wired into.
/// </summary>
/// <remarks>
/// The scheduler is constructed directly rather than resolved from a container, which is the point
/// of both seams: the same <see cref="XPlatAnimationScheduler"/> the application gets is the one the
/// test drives, so what the tests prove is the production path and not a stand-in for it.
/// </remarks>
internal sealed class AnimationHarness : IDisposable
{
    public AnimationHarness() => Scheduler = new XPlatAnimationScheduler(Clock, Frames);

    /// <summary>The clock, moved by <see cref="Advance"/>, <see cref="Frame"/> and <see cref="Tick"/>.</summary>
    public TestClock Clock { get; } = new();

    /// <summary>The wait the scheduler starts and stops.</summary>
    public ManualFrameSource Frames { get; } = new();

    /// <summary>The scheduler under test.</summary>
    public XPlatAnimationScheduler Scheduler { get; }

    /// <summary>Creates a set that runs on this harness's scheduler.</summary>
    public XPlatAnimationSet Set() => new(Scheduler);

    /// <summary>Moves the clock forward and runs a frame, in that order.</summary>
    /// <param name="milliseconds">How far into the timeline the frame is.</param>
    public void Frame(long milliseconds)
    {
        Clock.Advance(milliseconds);
        Frames.Frame();
    }

    /// <summary>Runs a frame without moving the clock, which is a frame that changed nothing.</summary>
    public void Tick() => Frames.Frame();

    public void Dispose() => Scheduler.Dispose();
}
