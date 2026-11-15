using Microsoft.Extensions.DependencyInjection;
using Kolvir.Backend.Headless;

namespace Kolvir.Animations.Tests;

/// <summary>
/// What happens when an animation's callback, or the finished handler, throws - and how an
/// application turns the feature on.
/// </summary>
/// <remarks>
/// <para>
/// A frame arrives from a platform timer callback, so an exception that unwinds out of it would
/// unwind into native code, where nothing can recover it. The library's answer is the same one drag
/// and drop uses: <see cref="Application.HandleNativeBoundary" /> routes the failure to
/// <see cref="Application.AddUnhandledExceptionHandler"/> when there is an application, and
/// rethrows when there is not. These tests pin both halves of that.
/// </para>
/// <para>
/// The opt-in is tested through a real container rather than by construction, because "registered as
/// a singleton reachable from the application" is the claim: a set built with no scheduler of its own
/// has to find the one <c>AddAnimations()</c> registered.
/// </para>
/// </remarks>
[TestClass]
[TestCategory("Feature")]
[TestCategory("Animations")]
public sealed class AnimationFailureTests : AnimationsTestBase
{
    private static readonly TimeSpan Hundred = TimeSpan.FromMilliseconds(100);

    [TestMethod]
    public void ACallbackThatThrowsIsReportedAndTheRestOfTheFrameStillRuns()
    {
        using var application = new Application(new HeadlessApplicationBackend());
        Exception? reported = null;
        application.AddUnhandledExceptionHandler((_, e) =>
        {
            reported = e.Exception;
            e.Handled = true;
        });

        using var harness = new AnimationHarness();
        int frames = 0;
        bool after = false;

        var set = harness.Set();
        set.Add(ValueAnimation.Create(0f, 1f, Hundred, _ => throw new InvalidOperationException("boom")));
        set.Add(ValueAnimation.Create(0f, 1f, Hundred, v =>
        {
            frames++;
            after = v >= 0f;
        }));

        set.Start();
        harness.Frame(50);

        Assert.IsNotNull(reported, "the failure reached the application's unhandled-exception handler");
        Assert.IsInstanceOfType<InvalidOperationException>(reported);
        Assert.IsTrue(after, "the animation after the failing one still ran");
        Assert.AreEqual(2, frames, "and it ran once for the start value and once for the frame");
        Assert.IsTrue(set.IsRunning, "one bad callback does not stop the timeline");
    }

    [TestMethod]
    public void ACallbackThatThrowsIsNotRetriedEveryFrame()
    {
        using var application = new Application(new HeadlessApplicationBackend());
        int reported = 0;
        application.AddUnhandledExceptionHandler((_, e) =>
        {
            reported++;
            e.Handled = true;
        });

        using var harness = new AnimationHarness();
        int calls = 0;

        var set = harness.Set();
        set.Add(ValueAnimation.Create(0f, 1f, Hundred, v =>
        {
            calls++;
            if (v == 0f)
            {
                throw new InvalidOperationException("boom");
            }
        }));

        set.Start();
        harness.Tick();
        harness.Tick();

        Assert.AreEqual(1, calls, "the value did not change, so the callback was not called again");
        Assert.AreEqual(1, reported, "and the failure was reported once, not once per frame");
    }

    [TestMethod]
    public void AFinishedHandlerThatThrowsStopsTheHandlersAfterIt()
    {
        using var application = new Application(new HeadlessApplicationBackend());
        Exception? reported = null;
        application.AddUnhandledExceptionHandler((_, e) =>
        {
            reported = e.Exception;
            e.Handled = true;
        });

        using var harness = new AnimationHarness();
        bool second = false;

        var set = harness.Set();
        set.Add(ValueAnimation.Create(0f, 1f, Hundred, _ => { }));
        set.AddFinishedHandler(_ => throw new InvalidOperationException("boom"));
        set.AddFinishedHandler(_ => second = true);

        set.Start();
        harness.Frame(100);

        Assert.IsNotNull(reported);
        Assert.IsFalse(second, "a throwing handler stops those after it, as a C# event would");
        Assert.IsTrue(set.IsFinished, "the timeline still finished");
    }

    [TestMethod]
    public void WithoutAnApplicationTheFailureUnwindsToTheCaller()
    {
        using var harness = new AnimationHarness();

        var set = harness.Set();
        set.Add(ValueAnimation.Create(0f, 1f, Hundred, v =>
        {
            if (v > 0f)
            {
                throw new InvalidOperationException("boom");
            }
        }));

        set.Start();

        var thrown = Assert.ThrowsExactly<InvalidOperationException>(() => harness.Frame(50));
        Assert.AreEqual("boom", thrown.Message, "with no application there is nowhere to report it, so it surfaces");
    }

    [TestMethod]
    public void AddAnimationsRegistersTheSchedulerASetFindsOnItsOwn()
    {
        using var application = new Application(new HeadlessApplicationBackend(), new ApplicationOptions
        {
            ConfigureServices = services => services.AddAnimations(),
        });

        var registered = application.Services.GetRequiredService<AnimationScheduler>();
        var again = application.Services.GetRequiredService<AnimationScheduler>();

        Assert.AreSame(registered, again, "one clock for the application");

        var set = new AnimationSet();
        Assert.AreSame(registered, set.Scheduler, "a set built without a scheduler uses the application's");
    }

    [TestMethod]
    public void AddAnimationsUsesAClockTheApplicationRegisteredFirst()
    {
        var clock = new TestClock();

        using var application = new Application(new HeadlessApplicationBackend(), new ApplicationOptions
        {
            ConfigureServices = services =>
            {
                services.AddSingleton<TimeProvider>(clock);
                services.AddAnimations();
            },
        });

        var scheduler = application.Services.GetRequiredService<AnimationScheduler>();

        Assert.AreSame(clock, scheduler.Time, "an application or a test that supplies a clock keeps it");
    }

    [TestMethod]
    public void ASetWithNoRegisteredSchedulerSaysWhatToCall()
    {
        using var application = new Application(new HeadlessApplicationBackend());

        var set = new AnimationSet();
        set.Add(ValueAnimation.Create(0f, 1f, Hundred, _ => { }));

        var thrown = Assert.ThrowsExactly<InvalidOperationException>(set.Start);

        StringAssert.Contains(thrown.Message, "AddAnimations");
    }
}
