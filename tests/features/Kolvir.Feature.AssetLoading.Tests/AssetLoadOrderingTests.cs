namespace Kolvir.Feature.AssetLoading.Tests;

/// <summary>
/// The order the walk considers the queue in: priority descending, and equal priorities in submission
/// order (dev-docs/ASSET-LOADING.md sections 3.5 and 3.6).
/// </summary>
/// <remarks>
/// The gates start closed in most of these, because a loader that may start now starts inside its own
/// <c>LoadAsync</c> call and there is then no queue to look at. Closing them is what makes the queue
/// observable, and <see cref="IAssetLoadManager.Snapshot"/> is what reads it without reaching inside
/// the manager.
/// </remarks>
[TestClass]
[TestCategory("Feature")]
[TestCategory("AssetLoading")]
public sealed class AssetLoadOrderingTests : AssetLoadingTestBase
{
    [TestMethod]
    public void TheWalkStartsTheHighestPriorityFirstAndEqualPrioritiesInSubmissionOrder()
    {
        IAssetLoadManager manager = CreateManager();
        var started = new List<string>();

        var first = new FakeAssetLoader<string>(AssetLoadPriority.Background, canLoadNow: false);
        var second = new FakeAssetLoader<string>(AssetLoadPriority.Background, canLoadNow: false);
        var visible = new FakeAssetLoader<string>(AssetLoadPriority.Visible, canLoadNow: false);

        manager.LoadAsync(first, () => started.Add("first"), CancellationToken.None);
        manager.LoadAsync(second, () => started.Add("second"), CancellationToken.None);
        manager.LoadAsync(visible, () => started.Add("visible"), CancellationToken.None);

        Assert.AreEqual(0, started.Count, "a closed gate holds every request, so nothing has started");

        // Every gate opens, and one notification is what turns three queued requests into an order: the
        // walk it causes is the only one, and it starts them in the order the queue is kept in.
        first.CanLoadNow = true;
        second.CanLoadNow = true;
        visible.SetCanLoadNow(true);

        CollectionAssert.AreEqual(
            new[] { "visible", "first", "second" },
            started,
            "priority descending first, and equal priorities in the order they were submitted");
        Assert.AreEqual(1, visible.LoadCount);
        Assert.AreEqual(1, first.LoadCount);
        Assert.AreEqual(1, second.LoadCount);
    }

    [TestMethod]
    public void TheSnapshotIsOrderedTheWayTheWalkIs()
    {
        IAssetLoadManager manager = CreateManager();

        var first = new FakeAssetLoader<string>(AssetLoadPriority.Background, canLoadNow: false);
        var second = new FakeAssetLoader<string>(AssetLoadPriority.Background, canLoadNow: false);
        var visible = new FakeAssetLoader<string>(AssetLoadPriority.Visible, canLoadNow: false);

        manager.LoadAsync(first, null, CancellationToken.None);
        manager.LoadAsync(second, null, CancellationToken.None);
        manager.LoadAsync(visible, null, CancellationToken.None);

        IReadOnlyList<AssetLoadSnapshot> snapshot = manager.Snapshot();

        Assert.AreEqual(3, snapshot.Count);
        Assert.AreSame(visible, snapshot[0].Loader, "the visible request is the one the walk would start next");
        Assert.AreSame(first, snapshot[1].Loader, "equal priorities keep submission order");
        Assert.AreSame(second, snapshot[2].Loader);
        Assert.AreEqual(AssetLoadPriority.Visible, snapshot[0].Priority);
        Assert.AreEqual(0L, snapshot[1].Sequence, "the sequence numbers are the submission order");
        Assert.AreEqual(1L, snapshot[2].Sequence);
        Assert.AreEqual(AssetLoadPhase.Queued, snapshot[0].Phase);
        Assert.IsFalse(snapshot[0].CanLoadNow, "the gate is read when the snapshot is taken");
    }

    [TestMethod]
    public void RaisingAPriorityMovesTheRowWithoutMovingItAheadOfEarlierSubmissions()
    {
        IAssetLoadManager manager = CreateManager();

        var first = new FakeAssetLoader<string>(AssetLoadPriority.Background, canLoadNow: false);
        var second = new FakeAssetLoader<string>(AssetLoadPriority.Background, canLoadNow: false);

        manager.LoadAsync(first, null, CancellationToken.None);
        manager.LoadAsync(second, null, CancellationToken.None);

        Assert.AreSame(first, manager.Snapshot()[0].Loader);

        second.SetPriority(AssetLoadPriority.Visible);

        Assert.AreSame(second, manager.Snapshot()[0].Loader, "a visible request outranks a background one");

        // The point of the sequence number: putting it back where it started has to put it back where
        // it was, not behind the request it was briefly ahead of. A re-prioritising scroll would
        // otherwise reorder a whole row every time it moved one image.
        second.SetPriority(AssetLoadPriority.Background);

        IReadOnlyList<AssetLoadSnapshot> snapshot = manager.Snapshot();

        Assert.AreSame(first, snapshot[0].Loader, "the sequence is taken at enqueue and kept across a priority change");
        Assert.AreSame(second, snapshot[1].Loader);
        Assert.AreEqual(0L, snapshot[0].Sequence);
        Assert.AreEqual(1L, snapshot[1].Sequence);
    }

    [TestMethod]
    public void ARePrioritisedRequestStartsBeforeOneThatQueuedAheadOfIt()
    {
        IAssetLoadManager manager = CreateManager();
        var started = new List<string>();

        var first = new FakeAssetLoader<string>(AssetLoadPriority.Background, canLoadNow: false);
        var second = new FakeAssetLoader<string>(AssetLoadPriority.Background, canLoadNow: false);

        manager.LoadAsync(first, () => started.Add("first"), CancellationToken.None);
        manager.LoadAsync(second, () => started.Add("second"), CancellationToken.None);

        second.SetPriority(AssetLoadPriority.Visible);
        second.SetCanLoadNow(true);

        CollectionAssert.AreEqual(new[] { "second" }, started, "only the request whose gate opened starts");
        Assert.AreEqual(0, first.LoadCount);

        first.SetCanLoadNow(true);

        CollectionAssert.AreEqual(new[] { "second", "first" }, started, "and the one that was ahead of it starts next");
    }

    [TestMethod]
    public void ARunningRequestKeepsItsPrioritySlotWithoutBeingPreempted()
    {
        IAssetLoadManager manager = CreateManager();
        var started = new List<string>();

        var running = new FakeAssetLoader<string>(AssetLoadPriority.Visible);
        manager.LoadAsync(running, () => started.Add("running"), CancellationToken.None);

        var queued = new FakeAssetLoader<string>(AssetLoadPriority.Background, canLoadNow: false);
        manager.LoadAsync(queued, () => started.Add("queued"), CancellationToken.None);

        // A scroll that moves everything else up does not restart what is already in flight: no loader
        // contract here carries a resumable position, and a restart would throw away received bytes at
        // the moment the application is busiest (3.5).
        queued.SetPriority(AssetLoadPriority.Visible + 1);

        CollectionAssert.AreEqual(new[] { "running" }, started, "a running load is never preempted");
        Assert.AreEqual(1, running.LoadCount);

        queued.SetCanLoadNow(true);

        CollectionAssert.AreEqual(new[] { "running", "queued" }, started);
    }
}
