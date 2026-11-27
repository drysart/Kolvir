namespace Kolvir.Feature.AssetLoading.Tests;

/// <summary>
/// The availability gate: what the manager asks, what a notification wakes, and what a closed gate
/// does - and does not - stop (dev-docs/ASSET-LOADING.md sections 3.2, 3.7 and 4.2).
/// </summary>
[TestClass]
[TestCategory("Feature")]
[TestCategory("AssetLoading")]
public sealed class AssetLoadAvailabilityTests : AssetLoadingTestBase
{
    [TestMethod]
    public void AVetoedRequestDoesNotStopTheOneBehindIt()
    {
        IAssetLoadManager manager = CreateManager();
        var started = new List<string>();

        var vetoed = new FakeAssetLoader<string>(AssetLoadPriority.Visible, canLoadNow: false);
        var open = new FakeAssetLoader<string>(AssetLoadPriority.Background, canLoadNow: false);

        manager.LoadAsync(vetoed, () => started.Add("vetoed"), CancellationToken.None);
        manager.LoadAsync(open, () => started.Add("open"), CancellationToken.None);

        open.SetCanLoadNow(true);

        CollectionAssert.AreEqual(
            new[] { "open" },
            started,
            "a closed gate for one loader says nothing about another, so the walk continues past it");
        Assert.AreEqual(0, vetoed.LoadCount);

        IReadOnlyList<AssetLoadSnapshot> snapshot = manager.Snapshot();

        Assert.AreEqual(2, snapshot.Count, "the vetoed request is still queued and the started one is still held");
        Assert.AreSame(vetoed, snapshot[0].Loader, "the visible request is still the next one the walk would consider");
        Assert.AreEqual(AssetLoadPhase.Queued, snapshot[0].Phase);
        Assert.IsFalse(snapshot[0].CanLoadNow, "the top of the snapshot being closed is the whole story of a stall");
        Assert.AreEqual(AssetLoadPhase.Running, snapshot[1].Phase);
    }

    [TestMethod]
    public void OpeningOneGateStartsThatRequestAndLeavesTheRestQueued()
    {
        IAssetLoadManager manager = CreateManager();

        var first = new FakeAssetLoader<string>(AssetLoadPriority.Background, canLoadNow: false);
        var second = new FakeAssetLoader<string>(AssetLoadPriority.Background, canLoadNow: false);
        var third = new FakeAssetLoader<string>(AssetLoadPriority.Background, canLoadNow: false);

        manager.LoadAsync(first, null, CancellationToken.None);
        manager.LoadAsync(second, null, CancellationToken.None);
        manager.LoadAsync(third, null, CancellationToken.None);

        second.SetCanLoadNow(true);

        Assert.AreEqual(0, first.LoadCount, "the gate that did not open wakes nothing");
        Assert.AreEqual(1, second.LoadCount, "the gate that opened starts its own request");
        Assert.AreEqual(0, third.LoadCount);

        IReadOnlyList<AssetLoadSnapshot> snapshot = manager.Snapshot();

        Assert.AreEqual(3, snapshot.Count, "nothing has left the manager: one request is running and two are queued");
        Assert.AreSame(first, snapshot[0].Loader);
        Assert.AreEqual(AssetLoadPhase.Queued, snapshot[0].Phase);
        Assert.AreSame(second, snapshot[1].Loader);
        Assert.AreEqual(AssetLoadPhase.Running, snapshot[1].Phase);
        Assert.AreSame(third, snapshot[2].Loader);
        Assert.AreEqual(AssetLoadPhase.Queued, snapshot[2].Phase);
    }

    [TestMethod]
    public void TheGateIsAskedAgainOnEveryWalkRatherThanCached()
    {
        IAssetLoadManager manager = CreateManager();

        var loader = new FakeAssetLoader<string>(canLoadNow: false);
        manager.LoadAsync(loader, null, CancellationToken.None);

        Assert.IsFalse(manager.Snapshot()[0].CanLoadNow);

        // Set without raising: the snapshot reads the answer rather than a value the manager kept, and
        // the next walk reads it the same way.
        loader.CanLoadNow = true;

        Assert.IsTrue(manager.Snapshot()[0].CanLoadNow, "CanLoadNow is read when the snapshot is taken");
        Assert.AreEqual(0, loader.LoadCount, "taking a snapshot is not a walk, so it starts nothing");

        loader.SetCanLoadNow(true);

        Assert.AreEqual(1, loader.LoadCount, "the walk that follows reads the answer rather than a value the manager kept");
    }

    [TestMethod]
    public void ANotificationThatChangesNothingStillLeavesTheQueueIntact()
    {
        IAssetLoadManager manager = CreateManager();

        var loader = new FakeAssetLoader<string>(AssetLoadPriority.Background, canLoadNow: true);
        var task = manager.LoadAsync(loader, null, CancellationToken.None);

        Assert.AreEqual(1, loader.LoadCount, "an open gate starts the request inside its own submission");

        // Nothing has changed for this loader, and the walk a notification causes must start nothing,
        // duplicate nothing and leave the one running request exactly where it is.
        loader.SetCanLoadNow(true);

        Assert.AreEqual(1, loader.LoadCount, "a walk never starts a request that is already running");
        Assert.AreEqual(1, manager.Snapshot().Count);
        Assert.AreEqual(AssetLoadPhase.Running, manager.Snapshot()[0].Phase);

        loader.Complete("done");

        Assert.AreEqual("done", task.Result);
    }
}
