using Kolvir;
using Kolvir.Backend.Headless;

namespace Kolvir.Tests;

/// <summary>
/// The <see cref="SynchronizationContext"/> <see cref="Application.Run()"/> installs: it is current
/// while the loop runs, it queues, it routes exceptions like any other UI callback, and it is gone
/// once the loop has ended.
/// </summary>
/// <remarks>
/// <para>
/// Every assertion is synchronous and made from a shown handler. That is not a convenience: the
/// headless backend drains its queue once and returns rather than looping, so an <c>await</c> in the
/// handler itself suspends, <see cref="Application.Run()"/> tears the application down without the
/// await having resumed, and the continuation would be drained with no context installed. The
/// continuation is instead driven by a <see cref="TaskCompletionSource"/> the test completes, and the
/// queue is drained inside the handler - <see cref="HeadlessApplicationBackend.DrainPending"/> takes a
/// batch under its lock and runs it outside, so a nested call from a callback takes the fresh queue,
/// drains it and returns without deadlocking, and the outer pass continues with its own batch.
/// </para>
/// <para>
/// The thread id is asserted explicitly rather than a flag being enough, because a continuation that
/// resumed on a pool thread and set a flag would pass a flag-only test - which is the exact failure
/// this feature exists to prevent.
/// </para>
/// </remarks>
[TestClass]
[TestCategory("Headless")]
public sealed class SynchronizationContextTests : HeadlessTestBase
{
    [TestMethod]
    public void RunInstallsTheContextAndTheSameInstanceForEveryRun()
    {
        SynchronizationContext? first = null;
        SynchronizationContext? second = null;

        using (var scene = HeadlessScene.Create(new Panel()))
        {
            scene.RunWhileShown(() =>
            {
                first = SynchronizationContext.Current;

                Assert.IsNotNull(first, "an application that runs should have a context");
                Assert.IsInstanceOfType<UiSynchronizationContext>(first);
                Assert.AreSame(first, first.CreateCopy(), "one UI thread means one context, not a copy");
            });
        }

        Assert.IsNull(SynchronizationContext.Current, "the context should be gone once Run returns");

        // A second application in the same process runs on the same UI thread, so it gets the same
        // answer to "where does a continuation resume".
        using (var scene = HeadlessScene.Create(new Panel()))
        {
            scene.RunWhileShown(() => second = SynchronizationContext.Current);
        }

        Assert.AreSame(first, second, "every run installs the one context this process has");
    }

    [TestMethod]
    public void APostFromTheUiThreadIsQueuedRatherThanRunInline()
    {
        var backend = new TestApplicationBackend();
        using var scene = HeadlessScene.Create(backend, new Panel());
        var ran = false;

        scene.RunWhileShown(() =>
        {
            SynchronizationContext.Current!.Post(_ => ran = true, null);

            Assert.IsFalse(ran, "a post from the UI thread is still queued, so it cannot overtake queued work");

            backend.Drain();

            Assert.IsTrue(ran, "and it runs when the queue is drained");
        });
    }

    [TestMethod]
    public void AContinuationAfterAnAwaitResumesOnTheUiThread()
    {
        var backend = new TestApplicationBackend();
        using var scene = HeadlessScene.Create(backend, new Panel());

        int uiThreadId = 0;
        int continuationThreadId = 0;
        var continued = false;

        scene.RunWhileShown(() =>
        {
            uiThreadId = Environment.CurrentManagedThreadId;

            var completion = new TaskCompletionSource();
            var body = ContinueAsync(completion.Task, () =>
            {
                continued = true;
                continuationThreadId = Environment.CurrentManagedThreadId;
            });

            // Completed from a pool thread, which is what an awaited call into anything but the UI
            // thread looks like: no context is current there, so the continuation has to be posted
            // back rather than inlined on the thread that completed the task.
            using var completionCallReturned = new ManualResetEventSlim();
            ThreadPool.QueueUserWorkItem(_ =>
            {
                completion.SetResult();
                completionCallReturned.Set();
            });

            Assert.IsTrue(completionCallReturned.Wait(TimeSpan.FromSeconds(10)), "the task should complete");
            Assert.IsFalse(continued, "nothing may run the continuation but the UI thread's queue");

            backend.Drain();

            Assert.IsTrue(continued, "the continuation should have run when the queue drained");
            Assert.AreEqual(
                uiThreadId,
                continuationThreadId,
                "the continuation must resume on the UI thread, not on the pool thread that completed the task");

            GC.KeepAlive(body);
        });
    }

    [TestMethod]
    public void AnExceptionInAPostedCallbackReachesTheUnhandledHandler()
    {
        var backend = new TestApplicationBackend();
        using var scene = HeadlessScene.Create(backend, new Panel());

        Exception? reported = null;
        scene.Application.AddUnhandledExceptionHandler((_, e) =>
        {
            reported = e.Exception;
            e.Handled = true;
        });

        scene.RunWhileShown(() =>
        {
            SynchronizationContext.Current!.Post(_ => throw new InvalidOperationException("posted"), null);

            // Vacuously true if the drain itself threw, which is the failure this asserts against:
            // the exception has to leave through the handler, not unwind into the pump.
            backend.Drain();

            Assert.IsNotNull(reported, "a posted callback that throws should be reported");
            Assert.IsInstanceOfType<InvalidOperationException>(reported);
            Assert.AreEqual("posted", reported?.Message);
        });
    }

    [TestMethod]
    public void ASendFromTheUiThreadRunsInline()
    {
        using var scene = HeadlessScene.Create(new Panel());
        var ran = false;

        scene.RunWhileShown(() =>
        {
            SynchronizationContext.Current!.Send(_ => ran = true, null);

            Assert.IsTrue(ran, "a send from the UI thread has nothing to wait for, so it runs immediately");
        });
    }

    [TestMethod]
    public void ASendAfterRunHasReturnedThrowsInsteadOfHanging()
    {
        SynchronizationContext? context = null;

        using (var scene = HeadlessScene.Create(new Panel()))
        {
            scene.RunWhileShown(() => context = SynchronizationContext.Current);
        }

        Assert.IsNotNull(context);

        // Waiting for a loop that has ended would never return, and running the callback on this
        // thread would be the cross-thread violation the context exists to prevent.
        Assert.ThrowsExactly<InvalidOperationException>(() => context.Send(_ => { }, null));
    }

    [TestMethod]
    public void APostAfterRunHasReturnedIsDroppedRatherThanThrown()
    {
        SynchronizationContext? context = null;

        using (var scene = HeadlessScene.Create(new Panel()))
        {
            scene.RunWhileShown(() => context = SynchronizationContext.Current);
        }

        Assert.IsNotNull(context);

        var ran = false;

        // A continuation whose task completed during teardown arrives here. There is no loop left to
        // run it and nothing to wait for, so it is dropped: arriving late is not an error the
        // application can act on, and throwing would turn a shutdown into a crash.
        context.Post(_ => ran = true, null);

        Assert.IsFalse(ran, "there is no queue left, and a late post is not a failure");
    }

    /// <summary>
    /// The body an application's <c>await</c> becomes: it resumes under whatever context was current
    /// when it suspended, and reports the thread it resumed on.
    /// </summary>
    private static async Task ContinueAsync(Task task, Action onResumed)
    {
        await task;
        onResumed();
    }
}
