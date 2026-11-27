namespace Kolvir.Feature.AssetLoading.Tests;

/// <summary>
/// What happens to a result that has no caller left to receive it, and what must not happen to one
/// that was delivered (dev-docs/ASSET-LOADING.md sections 3.9 and 4.1).
/// </summary>
/// <remarks>
/// The manager is the only holder of a dropped result, so it is the one that releases it - and the
/// half a careless fix would break is the other way round, a delivered result the manager has to leave
/// alone. The payload is a real <see cref="ImageData"/> over a source that records its own disposal,
/// which is the type this feature exists to produce. Nothing here waits: the loader's task is completed
/// by the test, and the queued case never starts one at all.
/// </remarks>
[TestClass]
[TestCategory("Feature")]
[TestCategory("AssetLoading")]
public sealed class AssetLoadResultDisposalTests : AssetLoadingTestBase
{
    [TestMethod]
    public void ARunningRequestCancelledBeforeItsResultArrivesHasThatResultDisposed()
    {
        IAssetLoadManager manager = CreateManager();
        using var cancellation = new CancellationTokenSource();
        var image = new TestImage();
        var loader = new FakeAssetLoader<ImageData>();

        Task<ImageData> task = manager.LoadAsync(loader, null, cancellation.Token);

        cancellation.Cancel();

        Assert.IsTrue(task.IsCanceled, "the caller is answered at cancellation time rather than when the load ends (3.9)");

        loader.Complete(image.Data);

        Assert.IsTrue(task.IsCanceled, "the late result is never delivered, so the answer does not change");
        Assert.IsTrue(image.IsDisposed, "the manager was the only holder, so the result it drops is its to release");
        Assert.AreEqual(0, manager.Snapshot().Count);
    }

    [TestMethod]
    public void ADeliveredResultIsTheCallersAndIsNotDisposedByTheManager()
    {
        IAssetLoadManager manager = CreateManager();
        var image = new TestImage();
        var loader = new FakeAssetLoader<ImageData>();

        Task<ImageData> task = manager.LoadAsync(loader, null, CancellationToken.None);

        loader.Complete(image.Data);

        Assert.AreSame(image.Data, task.Result, "the caller is handed the instance the loader produced");
        Assert.IsFalse(image.IsDisposed, "and owns it from then on - the manager releases only what it drops (4.1)");
    }

    [TestMethod]
    public void AQueuedRequestCancelledBeforeItStartsHasNoResultToDispose()
    {
        IAssetLoadManager manager = CreateManager();
        using var cancellation = new CancellationTokenSource();
        var loader = new FakeAssetLoader<ImageData>(canLoadNow: false);

        Task<ImageData> task = manager.LoadAsync(loader, null, cancellation.Token);

        cancellation.Cancel();

        Assert.IsTrue(task.IsCanceled);
        Assert.AreEqual(0, loader.LoadCount, "a request cancelled while queued never starts, so no result is ever produced");
        Assert.AreEqual(0, loader.CanLoadNowHandlerCount, "and nothing is left watching for one");
        Assert.AreEqual(0, manager.Snapshot().Count);
    }

    [TestMethod]
    public void AResultItsOwnTypedMemberCannotTakeIsDisposedAndTheCallerIsFailed()
    {
        IAssetLoadManager manager = CreateManager();
        var image = new TestImage();
        var loader = new FakeAssetLoader<int>();

        Task<int> task = manager.LoadAsync(loader, null, CancellationToken.None);

        loader.CompleteWithTheWrongType(image.Data);

        Assert.IsTrue(task.IsFaulted, "a value the typed member cannot take fails the caller rather than leaving it waiting");
        Assert.IsInstanceOfType<InvalidCastException>(task.Exception!.InnerException);
        Assert.IsTrue(image.IsDisposed, "the value that failed the cast has no receiver either, so it is released too");
        Assert.AreEqual(0, manager.Snapshot().Count);
    }
}
