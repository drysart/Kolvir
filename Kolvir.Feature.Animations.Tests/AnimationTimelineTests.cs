namespace Kolvir.Animations.Tests;

/// <summary>
/// The timeline: a set of value animations sharing one clock, and the five things an application
/// does to it.
/// </summary>
/// <remarks>
/// Every test drives time by hand - a <see cref="TestClock"/> the test advances and a frame source
/// the test fires - so an assertion about "half way through" is exactly half way through and nothing
/// sleeps. The contract decisions these tests pin down are the ones the API had to choose: what
/// <see cref="AnimationSet.End"/> does mid-flight, what a zero-length set does, whether a
/// second <see cref="AnimationSet.Start"/> rewinds, and when the callback is called at all.
/// </remarks>
[TestClass]
[TestCategory("Feature")]
[TestCategory("Animations")]
public sealed class AnimationTimelineTests : AnimationsTestBase
{
    private static readonly TimeSpan Hundred = TimeSpan.FromMilliseconds(100);

    [TestMethod]
    public void AValueAnimationReachesItsEndValue()
    {
        using var harness = new AnimationHarness();
        float value = -1f;

        var set = harness.Set();
        set.Add(ValueAnimation.Create(0f, 1f, Hundred, v => value = v));

        set.Start();

        Assert.AreEqual(0f, value, "the start value lands when the timeline starts, not a frame later");

        harness.Frame(50);
        Assert.AreEqual(0.5f, value, 1e-4f, "half the duration is half the value on a linear curve");

        harness.Frame(50);
        Assert.AreEqual(1f, value, "the last frame lands on the end value exactly");
        Assert.AreEqual(AnimationState.Finished, set.State);
        Assert.AreEqual(Hundred, set.Length);
        Assert.AreEqual(1d, set.Progress, 1e-6d);
    }

    [TestMethod]
    public void TwoAnimationsAreOffsetByTheirStartTimeOnOneTimeline()
    {
        using var harness = new AnimationHarness();
        float first = -1f;
        float second = -1f;

        var set = harness.Set();
        set.Add(ValueAnimation.Create(0f, 100f, Hundred, v => first = v));
        set.Add(ValueAnimation.Create(0f, 100f, Hundred, v => second = v, startTime: Hundred));

        Assert.AreEqual(TimeSpan.FromMilliseconds(200), set.Length, "a later animation lengthens the set");

        set.Start();
        Assert.AreEqual(-1f, second, "an animation whose start time has not arrived assigns nothing");

        harness.Frame(100);
        Assert.AreEqual(100f, first, 1e-4f, "the first animation is finished");
        Assert.AreEqual(0f, second, 1e-4f, "the second has only just begun");
        Assert.IsTrue(set.IsRunning, "the longer animation keeps the set running");

        harness.Frame(50);
        Assert.AreEqual(100f, first, 1e-4f);
        Assert.AreEqual(50f, second, 1e-4f);

        harness.Frame(50);
        Assert.AreEqual(100f, second, 1e-4f);
        Assert.IsTrue(set.IsFinished);
    }

    [TestMethod]
    public void EasingIsAppliedToTheProgressBeforeTheValueIsBlended()
    {
        using var harness = new AnimationHarness();
        float value = -1f;

        var set = harness.Set();
        set.Add(ValueAnimation.Create(
            0f,
            100f,
            Hundred,
            v => value = v,
            easing: Easing.EaseInQuad));

        set.Start();
        harness.Frame(50);

        Assert.AreEqual(
            25f,
            value,
            1e-4f,
            "EaseInQuad is t squared, so half the time is a quarter of the value");
    }

    [TestMethod]
    public void TheFinishedHandlerRunsOncePerRunAndItsTokenRemovesIt()
    {
        using var harness = new AnimationHarness();
        int finished = 0;
        int alsoFinished = 0;

        var set = harness.Set();
        set.Add(ValueAnimation.Create(0f, 1f, Hundred, _ => { }));

        using var token = set.AddFinishedHandler(_ => finished++);
        set.AddFinishedHandler(_ => alsoFinished++);

        set.Start();
        harness.Frame(100);

        Assert.AreEqual(1, finished);
        Assert.AreEqual(1, alsoFinished);

        harness.Frame(100);
        Assert.AreEqual(1, finished, "a frame after the end is not another run");

        set.End();
        Assert.AreEqual(1, finished, "ending a finished timeline is not another run");

        set.Restart();
        harness.Frame(100);
        Assert.AreEqual(2, finished, "restarting is a new run, so the handler runs again");
        Assert.AreEqual(2, alsoFinished);

        token.Dispose();
        set.Restart();
        harness.Frame(100);
        Assert.AreEqual(2, finished, "the token removed the handler it was created for");
        Assert.AreEqual(3, alsoFinished, "and left the other one alone");
    }

    [TestMethod]
    public void TheFinishedHandlerIsGivenTheSetItself()
    {
        using var harness = new AnimationHarness();
        object? sender = null;

        var set = harness.Set();
        set.AddFinishedHandler(s => sender = s);
        set.Start();

        Assert.AreSame(set, sender, "a set with no length finishes inside Start, and the sender is the set");
    }

    [TestMethod]
    public void StartingASetThatIsAlreadyRunningDoesNothing()
    {
        using var harness = new AnimationHarness();
        float value = -1f;

        var set = harness.Set();
        set.Add(ValueAnimation.Create(0f, 1f, Hundred, v => value = v));

        set.Start();
        set.Start();

        Assert.AreEqual(1, harness.Frames.Starts, "a second start does not ask for a second wait");

        harness.Frame(50);
        set.Start();

        harness.Frame(50);
        Assert.AreEqual(1f, value, 1e-4f, "a second start does not rewind the timeline");
        Assert.IsTrue(set.IsFinished);
    }

    [TestMethod]
    public void PauseFreezesTheValueAndStopsTheClockAndResumeCarriesOn()
    {
        using var harness = new AnimationHarness();
        float value = -1f;

        var set = harness.Set();
        set.Add(ValueAnimation.Create(0f, 1f, Hundred, v => value = v));

        set.Start();
        harness.Frame(50);
        Assert.AreEqual(0.5f, value, 1e-4f);

        set.Pause();

        Assert.AreEqual(AnimationState.Paused, set.State);
        Assert.IsFalse(harness.Frames.IsRunning, "a paused set lets go of the frame clock");

        harness.Frame(500);
        Assert.AreEqual(0.5f, value, 1e-4f, "time passing while paused does not move the value");
        Assert.AreEqual(TimeSpan.FromMilliseconds(50), set.Elapsed);

        set.Resume();

        Assert.IsTrue(harness.Frames.IsRunning, "resuming asks for the clock again");
        Assert.AreEqual(2, harness.Frames.Starts);

        harness.Frame(50);
        Assert.AreEqual(1f, value, 1e-4f, "the rest of the timeline plays, not a stretched one");
        Assert.IsTrue(set.IsFinished);
    }

    [TestMethod]
    public void StartOnAPausedSetResumesIt()
    {
        using var harness = new AnimationHarness();
        float value = -1f;

        var set = harness.Set();
        set.Add(ValueAnimation.Create(0f, 1f, Hundred, v => value = v));

        set.Start();
        harness.Frame(50);
        set.Pause();
        set.Start();

        Assert.AreEqual(AnimationState.Running, set.State, "start on a paused set resumes rather than rewinding");

        harness.Frame(50);
        Assert.AreEqual(1f, value, 1e-4f);
    }

    [TestMethod]
    public void RestartPutsTheTimelineBackToTheBeginning()
    {
        using var harness = new AnimationHarness();
        float value = -1f;

        var set = harness.Set();
        set.Add(ValueAnimation.Create(10f, 20f, Hundred, v => value = v));

        set.Start();
        harness.Frame(100);

        Assert.AreEqual(20f, value, 1e-4f);

        set.Restart();

        Assert.AreEqual(AnimationState.Running, set.State);
        Assert.AreEqual(10f, value, 1e-4f, "restarting assigns the start value immediately");
        Assert.AreEqual(TimeSpan.Zero, set.Elapsed);
        Assert.AreEqual(0d, set.Progress);

        harness.Frame(50);
        Assert.AreEqual(15f, value, 1e-4f);
    }

    [TestMethod]
    public void EndMidFlightSnapsToTheEndValueAndRunsTheFinishedHandler()
    {
        using var harness = new AnimationHarness();
        float value = -1f;
        int finished = 0;

        var set = harness.Set();
        set.Add(ValueAnimation.Create(0f, 1f, Hundred, v => value = v));
        set.AddFinishedHandler(_ => finished++);

        set.Start();
        harness.Frame(40);
        Assert.AreEqual(0.4f, value, 1e-4f);

        set.End();

        Assert.AreEqual(1f, value, 1e-4f, "End snaps every animation to its end value");
        Assert.AreEqual(AnimationState.Finished, set.State);
        Assert.AreEqual(1, finished, "the timeline is over however it got there");
        Assert.IsFalse(harness.Frames.IsRunning, "ending the last running set stops the clock");

        harness.Frame(100);
        Assert.AreEqual(1f, value, 1e-4f);
        Assert.AreEqual(1, finished);
    }

    [TestMethod]
    public void EndOnASetThatNeverStartedStillAssignsTheEndValue()
    {
        using var harness = new AnimationHarness();
        float value = -1f;
        int finished = 0;

        var set = harness.Set();
        set.Add(ValueAnimation.Create(0f, 1f, Hundred, v => value = v));
        set.AddFinishedHandler(_ => finished++);

        set.End();

        Assert.AreEqual(1f, value, 1e-4f);
        Assert.AreEqual(AnimationState.Finished, set.State);
        Assert.AreEqual(1, finished);
        Assert.AreEqual(0, harness.Frames.Starts, "a set that never ran never asked for a clock");
    }

    [TestMethod]
    public void AZeroDurationAnimationAppliesItsEndValueAndNeverAsksForAFrame()
    {
        using var harness = new AnimationHarness();
        float value = -1f;
        int finished = 0;

        var set = harness.Set();
        set.Add(ValueAnimation.Create(0f, 5f, TimeSpan.Zero, v => value = v));
        set.AddFinishedHandler(_ => finished++);

        Assert.AreEqual(TimeSpan.Zero, set.Length);
        Assert.AreEqual(0d, set.Progress, "nothing has played yet");

        set.Start();

        Assert.AreEqual(5f, value, 1e-4f, "a zero duration means instantly, not eventually");
        Assert.IsTrue(set.IsFinished);
        Assert.AreEqual(1, finished);
        Assert.AreEqual(0, harness.Frames.Starts, "there was nothing to wait for");
    }

    [TestMethod]
    public void AZeroDurationAnimationWithAStartTimeAppliesItsEndValueWhenThatTimeArrives()
    {
        using var harness = new AnimationHarness();
        float value = -1f;

        var set = harness.Set();
        set.Add(ValueAnimation.Create(0f, 5f, TimeSpan.Zero, v => value = v, startTime: Hundred));

        Assert.AreEqual(Hundred, set.Length, "the set is as long as the moment its last animation lands");

        set.Start();
        Assert.AreEqual(-1f, value, "the start time has not arrived, so nothing is assigned");

        harness.Frame(100);
        Assert.AreEqual(5f, value, 1e-4f);
        Assert.IsTrue(set.IsFinished);
    }

    [TestMethod]
    public void ASetWithNoAnimationsFinishesImmediately()
    {
        using var harness = new AnimationHarness();
        int finished = 0;

        var set = harness.Set();
        set.AddFinishedHandler(_ => finished++);

        Assert.AreEqual(TimeSpan.Zero, set.Length);

        set.Start();

        Assert.AreEqual(AnimationState.Finished, set.State);
        Assert.AreEqual(1, finished);
        Assert.AreEqual(1d, set.Progress);
        Assert.AreEqual(0, harness.Frames.Starts, "an empty set costs the application no timer at all");
    }

    [TestMethod]
    public void AValueIsAssignedOnlyWhenItChanges()
    {
        using var harness = new AnimationHarness();
        int assignments = 0;

        var set = harness.Set();
        set.Add(ValueAnimation.Create(0f, 1f, Hundred, _ => assignments++));

        set.Start();
        Assert.AreEqual(1, assignments, "the start value is assigned once");

        harness.Tick();
        harness.Tick();
        Assert.AreEqual(1, assignments, "frames that change nothing do not call the callback");

        harness.Frame(50);
        Assert.AreEqual(2, assignments);

        harness.Frame(50);
        Assert.AreEqual(3, assignments, "the end value is assigned once, not once per frame");
    }

    [TestMethod]
    public void ARestartReassignsAStartValueThatMatchesTheFinishedOne()
    {
        using var harness = new AnimationHarness();
        int assignments = 0;

        var set = harness.Set();
        set.Add(ValueAnimation.Create(0f, 1f, Hundred, _ => assignments++));

        set.Start();
        harness.Frame(100);
        int afterFirstRun = assignments;

        set.Restart();
        harness.Frame(100);

        // Restarting assigns the start value again even though it is the same as the one the run
        // began with, so watch for a value that never changes being reported once per run.
        Assert.IsTrue(
            assignments > afterFirstRun + 1,
            "a restarted run assigns its values again rather than assuming the destination is untouched");
    }

    [TestMethod]
    public void AddingAnAnimationOnceTheTimelineHasRunIsRefused()
    {
        using var harness = new AnimationHarness();

        var set = harness.Set();
        set.Add(ValueAnimation.Create(0f, 1f, Hundred, _ => { }));
        set.Start();

        Assert.ThrowsExactly<InvalidOperationException>(
            () => set.Add(ValueAnimation.Create(0f, 1f, Hundred, _ => { })));
    }

    [TestMethod]
    public void AddRejectsNullAndANegativeStartTimeOrDuration()
    {
        using var harness = new AnimationHarness();
        var set = harness.Set();

        Assert.ThrowsExactly<ArgumentNullException>(() => set.Add(null!));

        Assert.ThrowsExactly<ArgumentOutOfRangeException>(
            () => ValueAnimation.Create(0f, 1f, Hundred, _ => { }, startTime: TimeSpan.FromMilliseconds(-1)));

        Assert.ThrowsExactly<ArgumentOutOfRangeException>(
            () => ValueAnimation.Create(0f, 1f, TimeSpan.FromMilliseconds(-1), _ => { }));
    }

    [TestMethod]
    public void APausedOrDisposedSetDoesNotMoveAgain()
    {
        using var harness = new AnimationHarness();
        float value = -1f;
        int finished = 0;

        var set = harness.Set();
        set.Add(ValueAnimation.Create(0f, 1f, Hundred, v => value = v));
        set.AddFinishedHandler(_ => finished++);

        set.Start();
        harness.Frame(50);
        set.Dispose();

        Assert.IsTrue(set.IsFinished);
        Assert.IsFalse(harness.Frames.IsRunning, "disposing the last running set stops the clock");

        harness.Frame(100);
        Assert.AreEqual(0.5f, value, 1e-4f, "a disposed set never advances");
        Assert.AreEqual(0, finished, "disposing is not finishing, so the handler does not run");
    }

    [TestMethod]
    public void ASetReportsItsOwnProgressAsItPlays()
    {
        using var harness = new AnimationHarness();
        var set = harness.Set();
        set.Add(ValueAnimation.Create(0f, 1f, Hundred, _ => { }));

        Assert.AreEqual(0d, set.Progress);
        Assert.AreEqual(AnimationState.NotStarted, set.State);

        set.Start();
        harness.Frame(25);

        Assert.AreEqual(0.25d, set.Progress, 1e-6d);
        Assert.AreEqual(TimeSpan.FromMilliseconds(25), set.Elapsed);
    }
}
