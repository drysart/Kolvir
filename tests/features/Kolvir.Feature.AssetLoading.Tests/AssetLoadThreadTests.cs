using Kolvir.Backend.Headless;

namespace Kolvir.Feature.AssetLoading.Tests;

/// <summary>
/// The direction the callbacks come from: a loader may load anywhere and notify from anywhere, and
/// the manager moves the notification onto the UI thread before it touches its queue.
/// </summary>
/// <remarks>
/// <para>
/// These tests run with an application present and <em>not</em> running, so the headless backend's
/// queue is the only place a post goes and <see cref="HeadlessApplicationBackend.DrainPending"/>
/// plays the part the platform loop plays in an application. That is what makes the posted path
/// observable: before the drain the manager is deliberately untouched, and after it the walk has
/// happened.
/// </para>
/// <para>
/// The realistic case is the availability notification: an https loader finishes a fetch on a pool
/// thread, decrements its per-domain count and says so from that thread - which is exactly what
/// section 4.2 asks a gate to do. Without the move, that walk would run from the pool thread while
/// the UI thread was in the middle of one.
/// </para>
/// </remarks>
[TestClass]
[TestCategory("Feature")]
[TestCategory("AssetLoading")]
public sealed class AssetLoadThreadTests : AssetLoadingTestBase
{
    [TestMethod]
    public void ACompletionDrivenFromAPoolThreadIsAppliedOnTheUiThread()
    {
        var backend = new HeadlessApplicationBackend();
        using var application = new Application(backend);

        IAssetLoadManager manager = CreateManager();
        var loader = new FakeAssetLoader<string>();
        Task<string> task = manager.LoadAsync(loader, null, CancellationToken.None);

        OnAnotherThread(() => loader.Complete("from a pool thread"));

        Assert.IsFalse(task.IsCompleted, "the outcome was queued for the UI thread, not applied where it landed");
        Assert.AreEqual(1, loader.PriorityHandlerCount, "so nothing has been released yet either");
        Assert.AreEqual(1, manager.Snapshot().Count);

        backend.DrainPending();

        Assert.AreEqual("from a pool thread", task.Result, "and the UI thread delivers it");
        Assert.AreEqual(0, loader.PriorityHandlerCount);
        Assert.AreEqual(0, loader.CanLoadNowHandlerCount);
        Assert.AreEqual(0, manager.Snapshot().Count);
    }

    [TestMethod]
    public void AGateRaisedFromAPoolThreadWalksOnTheUiThread()
    {
        var backend = new HeadlessApplicationBackend();
        using var application = new Application(backend);

        IAssetLoadManager manager = CreateManager();
        var queued = new FakeAssetLoader<string>(canLoadNow: false);
        manager.LoadAsync(queued, null, CancellationToken.None);

        Assert.AreEqual(0, queued.LoadCount, "the gate is shut, so the request is queued");

        OnAnotherThread(() => queued.SetCanLoadNow(true));

        Assert.AreEqual(0, queued.LoadCount, "the walk was queued rather than run on the thread that raised the gate");

        backend.DrainPending();

        Assert.AreEqual(1, queued.LoadCount, "the UI thread runs the walk the notification asked for");
    }

    [TestMethod]
    public void ACancellationRequestedFromAPoolThreadIsAnsweredOnTheUiThread()
    {
        var backend = new HeadlessApplicationBackend();
        using var application = new Application(backend);

        IAssetLoadManager manager = CreateManager();
        using var cancellation = new CancellationTokenSource();
        var running = new FakeAssetLoader<string>();
        Task<string> task = manager.LoadAsync(running, null, cancellation.Token);

        OnAnotherThread(cancellation.Cancel);

        Assert.IsFalse(task.IsCanceled, "the answer is queued for the UI thread rather than given on the pool thread");
        Assert.AreEqual(1, running.PriorityHandlerCount, "and the handlers are untouched until it runs");

        backend.DrainPending();

        Assert.IsTrue(task.IsCanceled, "a cancellation on the UI thread is answered inside the Cancel call itself");
        Assert.AreEqual(0, running.PriorityHandlerCount, "here it is answered on the next turn, which is the same outcome");
        Assert.AreEqual(
            1,
            running.CanLoadNowHandlerCount,
            "the slot is still counted, because the underlying task has not ended");
    }

    [TestMethod]
    public void APriorityChangeRaisedFromAPoolThreadReordersOnTheUiThread()
    {
        var backend = new HeadlessApplicationBackend();
        using var application = new Application(backend);

        IAssetLoadManager manager = CreateManager();
        var started = new List<string>();

        var first = new FakeAssetLoader<string>(AssetLoadPriority.Background, canLoadNow: false);
        var second = new FakeAssetLoader<string>(AssetLoadPriority.Background, canLoadNow: false);

        manager.LoadAsync(first, () => started.Add("first"), CancellationToken.None);
        manager.LoadAsync(second, () => started.Add("second"), CancellationToken.None);

        // Both gates open silently, so the posted priority change is the only thing that can start
        // anything: it is the notification that walks, and the walk it causes is queued rather than
        // run on the thread that raised it.
        first.CanLoadNow = true;
        second.CanLoadNow = true;

        OnAnotherThread(() => second.SetPriority(AssetLoadPriority.Visible));

        Assert.AreEqual(0, first.LoadCount, "the notification is still queued, so nothing has walked");
        Assert.AreEqual(0, second.LoadCount);

        backend.DrainPending();

        CollectionAssert.AreEqual(
            new[] { "second", "first" },
            started,
            "the UI thread applies the reorder and the walk it asks for starts the visible request first");
    }

    /// <summary>Runs an action on a thread that is not this one, and waits for it to finish.</summary>
    /// <param name="action">What a loader, a factory or a token would do from its own thread.</param>
    private static void OnAnotherThread(Action action) => Task.Run(action).GetAwaiter().GetResult();
}
