namespace XPlatUI.Animations.Tests;

/// <summary>
/// The scheduler's two edges: the clock starts when the first set starts and stops when the last one
/// is no longer running, and neither edge may be crossed by something that happens inside a frame.
/// </summary>
/// <remarks>
/// <para>
/// This is the half of the feature most likely to be wrong, because both interesting cases happen
/// while the frame is already running: a set that finishes during a frame, and a set that starts
/// during a frame from another animation's callback. A scheduler that stopped the clock the moment
/// the last entry left its list would (a) leave a set that starts later in the same frame attached
/// to a stopped clock, and (b) restart the wait in the middle of the frame that started it.
/// </para>
/// <para>
/// These tests therefore assert the shape of the clock, not just the values: how many times the wait
/// was started, how many times it was stopped, and whether it is running now.
/// </para>
/// </remarks>
[TestClass]
[TestCategory("Feature")]
[TestCategory("Animations")]
public sealed class AnimationSchedulerTests : AnimationsTestBase
{
    private static readonly TimeSpan Hundred = TimeSpan.FromMilliseconds(100);

    [TestMethod]
    public void TheApplicationStartsIdleAndRunsNoTimer()
    {
        using var harness = new AnimationHarness();

        Assert.IsFalse(harness.Scheduler.IsRunning);
        Assert.AreEqual(0, harness.Frames.Starts, "nothing has asked for a frame");
        Assert.AreEqual(0, harness.Scheduler.RunningSetCount);
    }

    [TestMethod]
    public void TheClockStartsWithTheFirstSetAndStopsWithTheLast()
    {
        using var harness = new AnimationHarness();
        float first = -1f;
        float second = -1f;

        var one = harness.Set();
        one.Add(XPlatValueAnimation.Create(0f, 1f, Hundred, v => first = v));

        var two = harness.Set();
        two.Add(XPlatValueAnimation.Create(0f, 1f, Hundred, v => second = v));

        one.Start();
        two.Start();

        Assert.IsTrue(harness.Scheduler.IsRunning);
        Assert.AreEqual(1, harness.Frames.Starts, "two sets share one clock, not one clock each");
        Assert.AreEqual(2, harness.Scheduler.RunningSetCount);

        harness.Frame(100);

        Assert.AreEqual(1f, first, 1e-4f);
        Assert.AreEqual(1f, second, 1e-4f);
        Assert.IsFalse(harness.Scheduler.IsRunning, "the last set finished, so the application runs no timer");
        Assert.AreEqual(1, harness.Frames.Stops);
    }

    [TestMethod]
    public void TheClockStartsAgainWhenANewSetStarts()
    {
        using var harness = new AnimationHarness();

        var first = harness.Set();
        first.Add(XPlatValueAnimation.Create(0f, 1f, Hundred, _ => { }));
        first.Start();
        harness.Frame(100);

        Assert.IsFalse(harness.Scheduler.IsRunning);

        var second = harness.Set();
        second.Add(XPlatValueAnimation.Create(0f, 1f, Hundred, _ => { }));
        second.Start();

        Assert.IsTrue(harness.Scheduler.IsRunning, "an idle application that starts an animation gets its clock back");
        Assert.AreEqual(2, harness.Frames.Starts);

        harness.Frame(100);
        Assert.IsFalse(harness.Scheduler.IsRunning);
    }

    [TestMethod]
    public void ASetThatFinishesInsideAFrameDoesNotCutTheOtherSetsOutOfIt()
    {
        using var harness = new AnimationHarness();
        float longer = -1f;

        var shortSet = harness.Set();
        shortSet.Add(XPlatValueAnimation.Create(0f, 1f, Hundred, _ => { }));

        var longSet = harness.Set();
        longSet.Add(XPlatValueAnimation.Create(0f, 1f, TimeSpan.FromMilliseconds(200), v => longer = v));

        shortSet.Start();
        longSet.Start();

        harness.Frame(100);

        Assert.IsTrue(shortSet.IsFinished);
        Assert.IsTrue(longSet.IsRunning);
        Assert.IsTrue(
            harness.Scheduler.IsRunning,
            "one set finishing must not stop a clock another set is still attached to");
        Assert.AreEqual(0, harness.Frames.Stops);

        harness.Frame(100);

        Assert.AreEqual(1f, longer, 1e-4f, "the set that was still running kept advancing");
        Assert.IsFalse(harness.Scheduler.IsRunning);
        Assert.AreEqual(1, harness.Frames.Stops);
    }

    [TestMethod]
    public void ASetStartedFromAnAnimationCallbackKeepsTheRunningClockAndDoesNotRestartIt()
    {
        using var harness = new AnimationHarness();
        float second = -1f;
        XPlatAnimationSet? spawned = null;
        int spawnedStartFrame = -1;

        var first = harness.Set();
        first.Add(XPlatValueAnimation.Create(0f, 1f, Hundred, v =>
        {
            if (v <= 0.5f || spawned is not null)
            {
                return;
            }

            spawned = harness.Set();
            spawned.Add(XPlatValueAnimation.Create(0f, 1f, TimeSpan.FromMilliseconds(200), x => second = x));
            spawned.Start();
            spawnedStartFrame = harness.Frames.Starts;
        }));

        first.Start();
        harness.Frame(60);

        Assert.IsNotNull(spawned, "the callback started a second animation set");
        Assert.AreEqual(1, spawnedStartFrame, "starting a set mid-frame did not ask for a second wait");
        Assert.AreEqual(1, harness.Frames.Starts);
        Assert.AreEqual(0, harness.Frames.Stops, "and did not stop the one that was already running");
        Assert.IsTrue(harness.Scheduler.IsRunning);

        harness.Frame(40);
        Assert.IsTrue(first.IsFinished);
        Assert.AreEqual(0.2f, second, 1e-4f, "the spawned set has 40 ms of its 200 ms behind it");

        harness.Frame(60);
        Assert.AreEqual(0.5f, second, 1e-4f, "and it kept advancing after the set that spawned it ended");
        Assert.IsTrue(harness.Scheduler.IsRunning);
    }

    [TestMethod]
    public void AFinishedHandlerThatStartsWithANewSetKeepsTheClockRunning()
    {
        using var harness = new AnimationHarness();
        float second = -1f;
        XPlatAnimationSet? spawned = null;

        var first = harness.Set();
        first.Add(XPlatValueAnimation.Create(0f, 1f, Hundred, _ => { }));
        first.AddFinishedHandler(_ =>
        {
            if (spawned is not null)
            {
                return;
            }

            spawned = harness.Set();
            spawned.Add(XPlatValueAnimation.Create(0f, 1f, Hundred, v => second = v));
            spawned.Start();
        });

        first.Start();
        harness.Frame(100);

        Assert.IsTrue(first.IsFinished);
        Assert.IsNotNull(spawned);
        Assert.IsTrue(
            harness.Scheduler.IsRunning,
            "the last set detached during the frame, but its handler gave the clock a new one");
        Assert.AreEqual(1, harness.Frames.Starts, "so the clock never stopped and started again");
        Assert.AreEqual(0, harness.Frames.Stops);

        harness.Frame(100);
        Assert.AreEqual(1f, second, 1e-4f);
        Assert.IsFalse(harness.Scheduler.IsRunning);
        Assert.AreEqual(1, harness.Frames.Stops);
    }

    [TestMethod]
    public void ASetRestartedFromItsOwnFinishedHandlerLoopsWithoutRestartingTheClock()
    {
        using var harness = new AnimationHarness();
        int laps = 0;
        int frames = 0;
        XPlatAnimationSet set = null!;

        set = harness.Set();
        set.Add(XPlatValueAnimation.Create(0f, 1f, Hundred, _ => frames++));
        set.AddFinishedHandler(_ =>
        {
            laps++;

            if (laps < 3)
            {
                set.Restart();
            }
        });

        set.Start();
        harness.Frame(100);
        harness.Frame(100);

        Assert.AreEqual(2, laps, "the handler loops the animation");
        Assert.AreEqual(1, harness.Frames.Starts, "a loop never lets the clock stop and start again");
        Assert.AreEqual(0, harness.Frames.Stops, "and never stops it");
        Assert.IsTrue(harness.Scheduler.IsRunning);

        harness.Frame(100);

        Assert.AreEqual(3, laps);
        Assert.AreEqual(6, frames, "each of the three laps assigned its start and its end value");
        Assert.IsFalse(harness.Scheduler.IsRunning, "when the handler stops restarting, the clock stops");
        Assert.AreEqual(1, harness.Frames.Stops);
    }

    [TestMethod]
    public void PausingTheOnlyRunningSetStopsTheClockAndResumingStartsIt()
    {
        using var harness = new AnimationHarness();
        var set = harness.Set();
        set.Add(XPlatValueAnimation.Create(0f, 1f, Hundred, _ => { }));

        set.Start();
        Assert.IsTrue(harness.Frames.IsRunning);

        set.Pause();
        Assert.IsFalse(harness.Frames.IsRunning);
        Assert.AreEqual(0, harness.Scheduler.RunningSetCount);

        set.Resume();
        Assert.IsTrue(harness.Frames.IsRunning);
        Assert.AreEqual(1, harness.Scheduler.RunningSetCount);
        Assert.AreEqual(2, harness.Frames.Starts);
    }

    [TestMethod]
    public void DisposingTheSchedulerStopsTheClockAndRefusesNewSets()
    {
        var harness = new AnimationHarness();
        var set = harness.Set();
        set.Add(XPlatValueAnimation.Create(0f, 1f, Hundred, _ => { }));
        set.Start();

        harness.Scheduler.Dispose();

        Assert.IsFalse(harness.Frames.IsRunning);

        var later = harness.Set();
        later.Add(XPlatValueAnimation.Create(0f, 1f, Hundred, _ => { }));
        Assert.ThrowsExactly<ObjectDisposedException>(later.Start);
    }

    [TestMethod]
    public void TheFrameIntervalDefaultsToSixtyHertzAndIsHandedToTheFrameSource()
    {
        using var harness = new AnimationHarness();

        Assert.AreEqual(
            XPlatAnimationScheduler.DefaultFrameInterval,
            harness.Scheduler.FrameInterval,
            "a scheduler knows its own frames");

        var set = harness.Set();
        set.Add(XPlatValueAnimation.Create(0f, 1f, Hundred, _ => { }));

        harness.Scheduler.FrameInterval = TimeSpan.FromMilliseconds(5);
        set.Start();

        Assert.AreEqual(TimeSpan.FromMilliseconds(5), harness.Frames.Interval);
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => harness.Scheduler.FrameInterval = TimeSpan.Zero);
    }

    [TestMethod]
    public void AFrameBeforeTheStartTimeLeavesTheDestinationAlone()
    {
        using var harness = new AnimationHarness();
        float value = -1f;

        var set = harness.Set();
        set.Add(XPlatValueAnimation.Create(0f, 1f, Hundred, v => value = v, startTime: TimeSpan.FromMilliseconds(200)));

        set.Start();
        harness.Frame(100);

        Assert.AreEqual(-1f, value, "the animation has not begun, so nothing reached the destination");
    }
}
