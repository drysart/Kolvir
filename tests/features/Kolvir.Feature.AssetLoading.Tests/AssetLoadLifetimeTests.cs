using Kolvir.Backend.Headless;

namespace Kolvir.Feature.AssetLoading.Tests;

/// <summary>
/// What a request releases and what it refuses: the handler pairs on every exit path, one request per
/// live handle, and the UI-thread rule (dev-docs/ASSET-LOADING.md sections 3.10, 3.11 and 3.12).
/// </summary>
[TestClass]
[TestCategory("Feature")]
[TestCategory("AssetLoading")]
public sealed class AssetLoadLifetimeTests : AssetLoadingTestBase
{
    [TestMethod]
    public void BothHandlersAreReleasedWhenALoadCompletes()
    {
        IAssetLoadManager manager = CreateManager();
        var loader = new FakeAssetLoader<string>();

        Task<string> task = manager.LoadAsync(loader, null, CancellationToken.None);

        Assert.AreEqual(1, loader.PriorityHandlerCount);
        Assert.AreEqual(1, loader.CanLoadNowHandlerCount);

        loader.Complete("done");

        Assert.AreEqual(0, loader.PriorityHandlerCount, "completion releases both");
        Assert.AreEqual(0, loader.CanLoadNowHandlerCount);
        Assert.AreEqual("done", task.Result);
        Assert.AreEqual(0, manager.Snapshot().Count);
    }

    [TestMethod]
    public void BothHandlersAreReleasedWhenALoadFails()
    {
        IAssetLoadManager manager = CreateManager();
        var loader = new FakeAssetLoader<string>();
        var failure = new InvalidOperationException("the bytes were not what they claimed");

        Task<string> task = manager.LoadAsync(loader, null, CancellationToken.None);

        loader.Fail(failure);

        Assert.IsTrue(task.IsFaulted);
        Assert.AreSame(failure, task.Exception!.InnerException, "the caller sees the loader's own exception");
        Assert.AreEqual(0, loader.PriorityHandlerCount, "faulting releases both");
        Assert.AreEqual(0, loader.CanLoadNowHandlerCount);
        Assert.AreEqual(0, manager.Snapshot().Count);
    }

    [TestMethod]
    public void BothHandlersAreReleasedWhenTheLoaderThrowsInsteadOfReturningATask()
    {
        IAssetLoadManager manager = CreateManager();
        var failure = new InvalidOperationException("no writer for this format");
        var loader = new FakeAssetLoader<string> { StartError = failure };

        Task<string> task = manager.LoadAsync(loader, null, CancellationToken.None);

        Assert.IsTrue(task.IsFaulted, "a loader that throws fails its caller like any other loader failure");
        Assert.AreSame(failure, task.Exception!.InnerException);
        Assert.AreEqual(0, loader.PriorityHandlerCount);
        Assert.AreEqual(0, loader.CanLoadNowHandlerCount);
        Assert.AreEqual(0, manager.Snapshot().Count, "nothing was started, so nothing is left to watch");
    }

    [TestMethod]
    public void AStartedCallbackThatThrowsFailsTheRequestAndLeavesNoHandlersBehind()
    {
        IAssetLoadManager manager = CreateManager();
        var failure = new InvalidOperationException("the caller's own bookkeeping broke");
        var loader = new FakeAssetLoader<string>();

        Task<string> task = manager.LoadAsync(loader, () => throw failure, CancellationToken.None);

        Assert.IsTrue(task.IsFaulted, "onStarted throwing fails the entry like any other throw from a loader");
        Assert.AreSame(failure, task.Exception!.InnerException);
        Assert.AreEqual(1, loader.LoadCount, "the loader had already been asked, which is the loader's half of it");
        Assert.AreEqual(0, loader.PriorityHandlerCount);
        Assert.AreEqual(0, loader.CanLoadNowHandlerCount);
    }

    [TestMethod]
    public void SubmittingALiveHandleASecondTimeThrows()
    {
        IAssetLoadManager manager = CreateManager();
        var loader = new FakeAssetLoader<string>();

        Task<string> first = manager.LoadAsync(loader, null, CancellationToken.None);

        Action resubmit = () => manager.LoadAsync(loader, null, CancellationToken.None);
        var thrown = Assert.ThrowsExactly<InvalidOperationException>(resubmit);

        StringAssert.Contains(thrown.Message, "outstanding");
        Assert.AreEqual(1, loader.LoadCount, "the rejected submission started nothing");

        loader.Complete("done");

        Assert.AreEqual("done", first.Result);

        // A handle is for one request at a time, not one request ever: reuse after it has finished is
        // what section 4.1's sharing depends on.
        Task<string> again = manager.LoadAsync(loader, null, CancellationToken.None);

        Assert.AreEqual(2, loader.LoadCount);
        Assert.AreEqual("done", again.Result, "the fake's task was already complete, so the second request ends at once");
    }

    [TestMethod]
    public void AQueuedRequestThatStartsLaterIsAnsweredAndReleasesBothHandlers()
    {
        IAssetLoadManager manager = CreateManager();

        var queued = new FakeAssetLoader<string>(canLoadNow: false);
        Task<string> task = manager.LoadAsync(queued, null, CancellationToken.None);

        Assert.AreEqual(0, queued.LoadCount);
        Assert.AreEqual(1, queued.PriorityHandlerCount);
        Assert.AreEqual(1, queued.CanLoadNowHandlerCount);
        Assert.AreEqual(1, manager.Snapshot().Count);

        queued.SetCanLoadNow(true);
        queued.Complete("late but wanted");

        Assert.AreEqual("late but wanted", task.Result);
        Assert.AreEqual(0, queued.PriorityHandlerCount, "the queued-then-started path releases both too");
        Assert.AreEqual(0, queued.CanLoadNowHandlerCount);
        Assert.AreEqual(0, manager.Snapshot().Count);
    }

    [TestMethod]
    public void ALoaderThatHandsBackTheWrongTypeFailsItsCallerRatherThanLeavingItWaiting()
    {
        IAssetLoadManager manager = CreateManager();
        var loader = new FakeAssetLoader<int>();

        Task<int> task = manager.LoadAsync(loader, null, CancellationToken.None);

        loader.CompleteWithTheWrongType("this is not an int");

        Assert.IsTrue(task.IsFaulted, "a loader whose two members disagree completes its caller with the reason");
        Assert.IsInstanceOfType<InvalidCastException>(task.Exception!.InnerException);
        Assert.AreEqual(0, manager.Snapshot().Count);
    }

    [TestMethod]
    public void AValueTypeLoaderThatProducesNothingFailsItsCallerAndTheWalkContinues()
    {
        IAssetLoadManager manager = CreateManager();
        var loader = new FakeAssetLoader<int>();
        var behind = new FakeAssetLoader<string>(canLoadNow: false);

        Task<int> task = manager.LoadAsync(loader, null, CancellationToken.None);
        manager.LoadAsync(behind, null, CancellationToken.None);

        Assert.AreEqual(0, behind.LoadCount, "the failing load holds the only slot");

        // Opened without raising the notification, so the walk the failure has to leave behind is the
        // only thing that can start it. A fix that catches without reaching its Walk() leaves both the
        // caller unanswered and this entry queued, which is what the two assertions below are for.
        behind.CanLoadNow = true;

        loader.CompleteWithTheWrongType(null);

        Assert.IsTrue(task.IsFaulted, "null for a value type answers the caller rather than leaving it waiting");
        Assert.IsInstanceOfType<InvalidOperationException>(task.Exception!.InnerException);
        StringAssert.Contains(
            task.Exception.InnerException!.Message,
            "Int32",
            "and the failure names the type the loader owed");
        IReadOnlyList<AssetLoadSnapshot> live = manager.Snapshot();

        Assert.AreEqual(1, live.Count, "the failed request left the manager; only the one behind it is still live");
        Assert.AreSame(behind, live[0].Loader);
        Assert.AreEqual(1, behind.LoadCount, "the failing request's walk still runs, so the slot it held comes back");
    }

    [TestMethod]
    public void ACallFromAnotherThreadThrows()
    {
        // The check is Application.CheckAccess, so the test is what an application looks like: an
        // instance whose UI thread is this one, and a call made from a pool thread.
        using var application = new Application(new HeadlessApplicationBackend());

        IAssetLoadManager manager = CreateManager();
        var loader = new FakeAssetLoader<string>();

        Action offThread = () => Task.Run(() => manager.LoadAsync(loader, null, CancellationToken.None)).GetAwaiter().GetResult();
        var thrown = Assert.ThrowsExactly<InvalidOperationException>(offThread);

        StringAssert.Contains(thrown.Message, "UI thread");
        Assert.AreEqual(0, loader.LoadCount, "nothing was ordered");
        Assert.AreEqual(0, loader.PriorityHandlerCount, "and nothing was subscribed");
    }

    [TestMethod]
    public void ASnapshotFromAnotherThreadThrows()
    {
        using var application = new Application(new HeadlessApplicationBackend());

        IAssetLoadManager manager = CreateManager();

        Action offThread = () => Task.Run(manager.Snapshot).GetAwaiter().GetResult();

        Assert.ThrowsExactly<InvalidOperationException>(offThread);
    }
}
