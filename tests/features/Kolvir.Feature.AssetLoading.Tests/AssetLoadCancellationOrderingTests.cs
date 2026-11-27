namespace Kolvir.Feature.AssetLoading.Tests;

/// <summary>
/// The caller's answer depends on the request's own cancellation, not on the order two
/// <c>CancellationToken.Register</c> callbacks happen to run in (dev-docs/ASSET-LOADING.md section
/// 3.9).
/// </summary>
/// <remarks>
/// <para>
/// <c>CancellationToken.Register</c> invokes callbacks last-registered-first. The manager registers
/// when the request is created; a loader that registers on the same token inside its own
/// <c>LoadAsync</c> therefore registers later and runs earlier. A loader that finishes its task from
/// inside that callback - a loader treating the token as "stop, and hand back what you have" - has
/// handed the manager a completion while the manager's cancellation is on its way and not yet
/// applied.
/// </para>
/// <para>
/// Nothing here races: the loader completes inside the callback, on the thread that called
/// <c>Cancel()</c>, so the ordering is deterministic and every assertion is about what the manager
/// answered. A loader that ignores the token entirely is the other half of the same contract and is
/// covered in <see cref="AssetLoadCancellationTests"/>.
/// </para>
/// </remarks>
[TestClass]
[TestCategory("Feature")]
[TestCategory("AssetLoading")]
public sealed class AssetLoadCancellationOrderingTests : AssetLoadingTestBase
{
    [TestMethod]
    public void ALoaderThatCompletesInsideTheTokenCallbackIsStillAnsweredCancelled()
    {
        IAssetLoadManager manager = CreateManager();
        using var cancellation = new CancellationTokenSource();
        var image = new TestImage();
        var loader = new FakeAssetLoader<ImageData>
        {
            CompletesWhenCancelled = true,
            OnCancelResult = image.Data,
        };

        Task<ImageData> task = manager.LoadAsync(loader, null, cancellation.Token);

        cancellation.Cancel();

        Assert.IsTrue(
            task.IsCanceled,
            "the caller asked to stop, so the answer is the cancellation - not the result the loader " +
            "produced while the manager's own callback was still waiting its turn");
        Assert.IsFalse(task.IsFaulted);
        Assert.IsTrue(image.IsDisposed, "a result nobody may receive is released, the same way any other dropped one is");
        Assert.AreEqual(0, loader.CanLoadNowHandlerCount, "the load has ended, so nothing is left watching its slot");
        Assert.AreEqual(0, manager.Snapshot().Count);
    }

    [TestMethod]
    public void ALoaderThatFailsInsideTheTokenCallbackIsStillAnsweredCancelled()
    {
        IAssetLoadManager manager = CreateManager();
        using var cancellation = new CancellationTokenSource();
        var loader = new FakeAssetLoader<string>
        {
            CompletesWhenCancelled = true,
            OnCancelError = new InvalidOperationException("the loader gave up on the fetch the caller abandoned"),
        };

        Task<string> task = manager.LoadAsync(loader, null, cancellation.Token);

        cancellation.Cancel();

        Assert.IsTrue(
            task.IsCanceled,
            "a cancelled caller is answered cancelled rather than faulted, whatever the load did next - " +
            "and the failure is the case a caller would actually see");
        Assert.IsFalse(task.IsFaulted);
        Assert.AreEqual(0, manager.Snapshot().Count);
    }

    [TestMethod]
    public void TheCompletionThatRevealsTheCancellationStillFreesTheSlot()
    {
        IAssetLoadManager manager = CreateManager();
        using var cancellation = new CancellationTokenSource();
        var running = new FakeAssetLoader<string> { CompletesWhenCancelled = true, OnCancelResult = "late" };
        var waiting = new FakeAssetLoader<string>(canLoadNow: false);

        Task<string> task = manager.LoadAsync(running, null, cancellation.Token);
        manager.LoadAsync(waiting, null, CancellationToken.None);

        // Opened without the notification, so the walk that starts it can only be one this manager
        // runs itself - the same pin as the cancelled-then-completes test, for this path.
        waiting.CanLoadNow = true;

        cancellation.Cancel();

        Assert.IsTrue(task.IsCanceled);
        Assert.AreEqual(1, waiting.LoadCount, "the underlying task ended, so the slot it held really did come free");
        Assert.AreEqual(0, running.CanLoadNowHandlerCount);
        Assert.AreEqual(1, manager.Snapshot().Count);
        Assert.AreSame(waiting, manager.Snapshot()[0].Loader);
    }

    [TestMethod]
    public void AnotherRequestsCancellationDoesNotWithholdThisOnesResult()
    {
        IAssetLoadManager manager = CreateManager();
        using var cancelled = new CancellationTokenSource();
        using var delivered = new CancellationTokenSource();

        var abandoned = new FakeAssetLoader<string>(canLoadNow: false);
        Task<string> abandonedTask = manager.LoadAsync(abandoned, null, cancelled.Token);

        var wanted = new FakeAssetLoader<string>();
        Task<string> wantedTask = manager.LoadAsync(wanted, null, delivered.Token);

        cancelled.Cancel();
        wanted.Complete("wanted");

        Assert.IsTrue(abandonedTask.IsCanceled);
        Assert.AreEqual("wanted", wantedTask.Result, "the check is the request's own token, not a manager-wide mood");
    }
}
