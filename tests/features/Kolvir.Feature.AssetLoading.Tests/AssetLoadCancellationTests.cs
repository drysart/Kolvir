namespace Kolvir.Feature.AssetLoading.Tests;

/// <summary>
/// Cancellation: the two paths are different, and the manager's guarantee is its own rather than the
/// loader's (dev-docs/ASSET-LOADING.md sections 3.9 and 5).
/// </summary>
/// <remarks>
/// Nothing here asserts that the loader stopped - a loader over an already-open stream has nothing
/// to stop, and the manager does not rely on any loader honouring the token. What is asserted is what
/// the caller is told, when, and what the running load keeps costing until it really ends.
/// </remarks>
[TestClass]
[TestCategory("Feature")]
[TestCategory("AssetLoading")]
public sealed class AssetLoadCancellationTests : AssetLoadingTestBase
{
    [TestMethod]
    public void AQueuedRequestThatIsCancelledNeverStartsAndNeverRaisesOnStarted()
    {
        IAssetLoadManager manager = CreateManager();
        using var cancellation = new CancellationTokenSource();

        bool started = false;
        var loader = new FakeAssetLoader<string>(canLoadNow: false);
        Task<string> task = manager.LoadAsync(loader, () => started = true, cancellation.Token);

        Assert.AreEqual(0, loader.LoadCount, "the gate is closed, so the request is queued");
        Assert.AreEqual(1, loader.PriorityHandlerCount, "both handlers are subscribed while it is queued");
        Assert.AreEqual(1, loader.CanLoadNowHandlerCount);

        cancellation.Cancel();

        Assert.IsTrue(task.IsCanceled, "the caller is answered cancelled at once");
        Assert.IsTrue(task.IsCompleted);
        Assert.IsFalse(started, "a request cancelled while queued never raises onStarted");
        Assert.AreEqual(0, loader.LoadCount, "and never starts, which is requirement 4's first half");
        Assert.AreEqual(0, loader.PriorityHandlerCount, "nothing was started, so nothing has to be watched");
        Assert.AreEqual(0, loader.CanLoadNowHandlerCount);
        Assert.AreEqual(0, manager.Snapshot().Count);
    }

    [TestMethod]
    public void CancellingAQueuedRequestLetsTheOnesBehindItThrough()
    {
        IAssetLoadManager manager = CreateManager();
        using var cancellation = new CancellationTokenSource();

        var cancelled = new FakeAssetLoader<string>(AssetLoadPriority.Visible, canLoadNow: false);
        var behind = new FakeAssetLoader<string>(AssetLoadPriority.Background, canLoadNow: false);

        Task<string> cancelledTask = manager.LoadAsync(cancelled, null, cancellation.Token);
        manager.LoadAsync(behind, null, CancellationToken.None);

        cancellation.Cancel();
        behind.SetCanLoadNow(true);

        Assert.IsTrue(cancelledTask.IsCanceled);
        Assert.AreEqual(0, cancelled.LoadCount);
        Assert.AreEqual(1, behind.LoadCount, "a withdrawn entry leaves, and the walk continues with the entries below it");
        Assert.AreEqual(1, manager.Snapshot().Count);
        Assert.AreSame(behind, manager.Snapshot()[0].Loader);
    }

    [TestMethod]
    public void ARunningRequestThatIsCancelledAnswersItsCallerButKeepsItsSlot()
    {
        IAssetLoadManager manager = CreateManager();
        using var cancellation = new CancellationTokenSource();

        var running = new FakeAssetLoader<string>();
        Task<string> task = manager.LoadAsync(running, null, cancellation.Token);

        Assert.AreEqual(1, running.LoadCount);
        Assert.AreEqual(1, running.PriorityHandlerCount);
        Assert.AreEqual(1, running.CanLoadNowHandlerCount);

        // A second request whose gate is shut, as a factory's count would be while the first is running.
        var waiting = new FakeAssetLoader<string>(canLoadNow: false);
        manager.LoadAsync(waiting, null, CancellationToken.None);

        cancellation.Cancel();

        Assert.IsTrue(task.IsCanceled, "a running request is answered at cancellation time, not when the load ends");
        Assert.AreEqual(0, running.PriorityHandlerCount, "the queue order no longer concerns it, so that handler comes off");
        Assert.AreEqual(
            1,
            running.CanLoadNowHandlerCount,
            "the slot is still occupied, so the availability handler stays until the underlying task ends");

        Assert.AreEqual(2, manager.Snapshot().Count, "the cancelled load is still one the manager is watching");
        Assert.AreSame(running, manager.Snapshot()[0].Loader);
        Assert.AreEqual(AssetLoadPhase.Running, manager.Snapshot()[0].Phase);
        Assert.AreEqual(AssetLoadPhase.Queued, manager.Snapshot()[1].Phase);
        Assert.AreEqual(0, waiting.LoadCount, "its slot is still taken, so the request behind it does not start");

        // The loader's own count has not moved, so the gate the walk reads would still say no. Clearing
        // the answer without raising the notification is what pins where the next walk comes from: the
        // underlying task actually ending.
        waiting.CanLoadNow = true;
        running.Complete("too late");

        Assert.AreEqual(1, waiting.LoadCount, "the load really ending is what starts what it was holding back");
        Assert.IsTrue(task.IsCanceled, "and the late result is never delivered to the cancelled caller");
        Assert.AreEqual(0, running.CanLoadNowHandlerCount);
        Assert.AreEqual(1, manager.Snapshot().Count);
        Assert.AreSame(waiting, manager.Snapshot()[0].Loader);
    }

    [TestMethod]
    public void ACancelledCallersLateFailureIsNotDeliveredEither()
    {
        IAssetLoadManager manager = CreateManager();
        using var cancellation = new CancellationTokenSource();

        var running = new FakeAssetLoader<string>();
        Task<string> task = manager.LoadAsync(running, null, cancellation.Token);

        cancellation.Cancel();
        running.Fail(new InvalidOperationException("the fetch failed after nobody wanted it"));

        Assert.IsTrue(task.IsCanceled, "a cancelled caller is answered cancelled, not faulted, whatever the load did next");
        Assert.IsFalse(task.IsFaulted);
        Assert.AreEqual(0, manager.Snapshot().Count);
    }

    [TestMethod]
    public void AnAlreadyCancelledTokenAnswersTheCallerBeforeSubmissionReturns()
    {
        IAssetLoadManager manager = CreateManager();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        bool started = false;
        var loader = new FakeAssetLoader<string>();
        Task<string> task = manager.LoadAsync(loader, () => started = true, cancellation.Token);

        Assert.IsTrue(task.IsCanceled, "the caller is answered cancelled inside its own submission");
        Assert.IsFalse(started);
        Assert.AreEqual(0, loader.LoadCount);
        Assert.AreEqual(0, loader.PriorityHandlerCount, "including the registration made for it");
        Assert.AreEqual(0, loader.CanLoadNowHandlerCount);
        Assert.AreEqual(0, manager.Snapshot().Count);
    }

    [TestMethod]
    public void TheLoaderIsHandedTheCallersTokenRatherThanOneOfItsOwn()
    {
        IAssetLoadManager manager = CreateManager();
        using var cancellation = new CancellationTokenSource();

        var loader = new FakeAssetLoader<string>();
        manager.LoadAsync(loader, null, cancellation.Token);

        Assert.AreEqual(cancellation.Token, loader.Token, "the loader decides what to do with the caller's token");
    }
}
