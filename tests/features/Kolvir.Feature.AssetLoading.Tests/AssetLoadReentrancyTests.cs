namespace Kolvir.Feature.AssetLoading.Tests;

/// <summary>
/// The walk survives re-entry without recursing, which is the one thing a queue like this gets wrong
/// when it is written the obvious way (dev-docs/ASSET-LOADING.md section 3.8).
/// </summary>
/// <remarks>
/// Three shapes of re-entry, and they are different: a loader completing synchronously, inside the
/// walk's own "start this one"; a started callback enqueueing while the walk is running; and a
/// completion that frees a slot, whose caller continuation enqueues another request. Recursing in any
/// of them would be unbounded, and would start loads over the list the outer walk is walking.
/// </remarks>
[TestClass]
[TestCategory("Feature")]
[TestCategory("AssetLoading")]
public sealed class AssetLoadReentrancyTests : AssetLoadingTestBase
{
    [TestMethod]
    public void ASynchronousCompletionDoesNotCorruptTheWalk()
    {
        IAssetLoadManager manager = CreateManager();
        var started = new List<string>();

        var first = new FakeAssetLoader<string> { CompletesOnStart = true, OnStartResult = "first" };
        var second = new FakeAssetLoader<string> { CompletesOnStart = true, OnStartResult = "second" };

        Task<string> firstTask = manager.LoadAsync(first, () => started.Add("first"), CancellationToken.None);

        Assert.IsTrue(firstTask.IsCompleted, "a loader that completes on the spot is answered before the call returns");

        Task<string> secondTask = manager.LoadAsync(second, () => started.Add("second"), CancellationToken.None);

        CollectionAssert.AreEqual(new[] { "first", "second" }, started);
        Assert.AreEqual("first", firstTask.Result);
        Assert.AreEqual("second", secondTask.Result);
        Assert.AreEqual(0, manager.Snapshot().Count, "both requests have left the manager");
    }

    [TestMethod]
    public void AStartedCallbackThatEnqueuesIsServedByTheSameWalk()
    {
        IAssetLoadManager manager = CreateManager();
        var nested = new FakeAssetLoader<string>();
        Task<string>? nestedTask = null;

        var outer = new FakeAssetLoader<string>();

        Task<string> outerTask = manager.LoadAsync(
            outer,
            () => nestedTask = manager.LoadAsync(nested, null, CancellationToken.None),
            CancellationToken.None);

        Assert.IsNotNull(nestedTask, "the started callback ran inside the submission");
        Assert.AreEqual(1, nested.LoadCount, "the walk that was already running started the request it was handed");
        Assert.AreEqual(1, outer.LoadCount);
        Assert.AreEqual(2, manager.Snapshot().Count);

        outer.Complete("outer");
        nested.Complete("nested");

        Assert.AreEqual("outer", outerTask.Result);
        Assert.AreEqual("nested", nestedTask.Result);
    }

    [TestMethod]
    public void ACompletionThatEnqueuesIsServedByTheWalkItInterrupted()
    {
        IAssetLoadManager manager = CreateManager();

        // A load that is already running, and a continuation on its task that queues another request
        // when the result arrives.
        var running = new FakeAssetLoader<string>();
        Task<string> runningTask = manager.LoadAsync(running, null, CancellationToken.None);

        var third = new FakeAssetLoader<string>();
        Task<string>? thirdTask = null;

        runningTask.ContinueWith(
            _ => thirdTask = manager.LoadAsync(third, null, CancellationToken.None),
            TaskContinuationOptions.ExecuteSynchronously);

        // The second submission's started callback completes the first load, so the completion - and
        // the enqueue inside its continuation - happens inside a walk, which is the case the flag pair
        // exists for.
        var trigger = new FakeAssetLoader<string>();
        manager.LoadAsync(trigger, () => running.Complete("running"), CancellationToken.None);

        Assert.IsNotNull(thirdTask, "the completion ran inside the walk");
        Assert.AreEqual(1, third.LoadCount, "and the enqueue it made was started by the same walk");
        Assert.AreEqual("running", runningTask.Result);
        Assert.AreEqual(2, manager.Snapshot().Count, "the trigger and the request it caused are the ones left");
    }
}
