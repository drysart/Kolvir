using Microsoft.Extensions.DependencyInjection;

namespace Kolvir.Feature.AssetLoading.Http.Tests;

/// <summary>
/// The two limits: what they stop, what a change at runtime admits, and what lowering one does not
/// do (dev-docs/ASSET-LOADING.md sections 3.1 and 4.2).
/// </summary>
[TestClass]
[TestCategory("Feature")]
[TestCategory("AssetLoading.Http")]
public sealed class HttpAssetLoadLimitTests : HttpAssetLoadingTestBase
{
    [TestMethod]
    public async Task ThePerHostLimitStopsASecondRequestToThatHostAndNotOneToAnother()
    {
        var options = new HttpAssetLoaderOptions { MaxConcurrentRequests = 10, MaxConcurrentRequestsPerHost = 1 };
        var handler = new FakeHttpMessageHandler { RespondAutomatically = false };
        using ServiceProvider provider = CreateProvider(options, handler);

        Task<ImageData> first = Load(provider, "https://a.test/1.png");
        Task<ImageData> second = Load(provider, "https://a.test/2.png");
        Task<ImageData> other = Load(provider, "https://b.test/1.png");

        Assert.AreEqual(2, handler.Count, "one request to a.test and one to b.test; the second to a.test is queued");
        Assert.AreEqual("a.test", handler.Requests[0].Uri!.Host);
        Assert.AreEqual("b.test", handler.Requests[1].Uri!.Host);
        Assert.IsFalse(second.IsCompleted, "the host is at its cap");

        handler.Respond(0);
        await handler.WaitForCountAsync(3);

        Assert.AreEqual("a.test", handler.Requests[2].Uri!.Host, "a.test's freed slot starts the queued request to a.test");

        handler.RespondAutomatically = true;
        handler.Respond(1);
        handler.Respond(2);

        await first;
        await second;
        await other;
    }

    [TestMethod]
    public async Task TheTotalLimitStopsARequestToAnotherHost()
    {
        var options = new HttpAssetLoaderOptions { MaxConcurrentRequests = 1, MaxConcurrentRequestsPerHost = 10 };
        var handler = new FakeHttpMessageHandler { RespondAutomatically = false };
        using ServiceProvider provider = CreateProvider(options, handler);

        Task<ImageData> first = Load(provider, "https://a.test/1.png");
        Task<ImageData> second = Load(provider, "https://b.test/1.png");

        Assert.AreEqual(1, handler.Count, "one total slot, and a.test has it");
        Assert.IsFalse(second.IsCompleted, "b.test has room of its own, but the total is full");

        handler.Respond(0);
        await handler.WaitForCountAsync(2);

        Assert.AreEqual("b.test", handler.Requests[1].Uri!.Host, "the total slot freeing starts b.test's request");

        handler.RespondAutomatically = true;
        handler.Respond(1);

        await first;
        await second;
    }

    [TestMethod]
    public async Task RaisingTheTotalLimitAtRuntimeAdmitsQueuedWork()
    {
        var options = new HttpAssetLoaderOptions { MaxConcurrentRequests = 1, MaxConcurrentRequestsPerHost = 10 };
        var handler = new FakeHttpMessageHandler { RespondAutomatically = false };
        using ServiceProvider provider = CreateProvider(options, handler);

        _ = Load(provider, "https://a.test/1.png");
        Task<ImageData> queued = Load(provider, "https://b.test/1.png");

        Assert.AreEqual(1, handler.Count);

        options.MaxConcurrentRequests = 2;

        Assert.AreEqual(2, handler.Count, "the raised cap notifies the waiting handles and the manager re-walks");
        Assert.AreEqual("b.test", handler.Requests[1].Uri!.Host);
        Assert.IsFalse(queued.IsCompleted, "it is in flight, not finished");

        handler.RespondAutomatically = true;
        handler.RespondToEverything();

        await queued;
    }

    [TestMethod]
    public async Task RaisingThePerHostLimitAdmitsThatHostsQueuedWorkAndLeavesTheOtherHostAlone()
    {
        var options = new HttpAssetLoaderOptions { MaxConcurrentRequests = 3, MaxConcurrentRequestsPerHost = 1 };
        var handler = new FakeHttpMessageHandler { RespondAutomatically = false };
        using ServiceProvider provider = CreateProvider(options, handler);

        Task<ImageData> aFirst = Load(provider, "https://a.test/1.png");
        Task<ImageData> aSecond = Load(provider, "https://a.test/2.png");
        Task<ImageData> bFirst = Load(provider, "https://b.test/1.png");
        Task<ImageData> bSecond = Load(provider, "https://b.test/2.png");

        Assert.AreEqual(2, handler.Count, "both hosts are at one request each");

        options.MaxConcurrentRequestsPerHost = 2;

        Assert.AreEqual(3, handler.Count, "one of the queued requests can now fit, and it is a.test's first");
        Assert.AreEqual("a.test", handler.Requests[2].Uri!.Host);
        Assert.IsFalse(aSecond.IsCompleted);
        Assert.IsFalse(bSecond.IsCompleted, "the total is full now, so b.test's queued request still waits");
        Assert.IsFalse(bFirst.IsCompleted, "the other host's running request was not disturbed");
        Assert.IsFalse(handler.Requests[1].Token.IsCancellationRequested, "nor was its request aborted");

        handler.RespondAutomatically = true;
        handler.RespondToEverything();

        await aFirst;
        await aSecond;
        await bFirst;
        await bSecond;
    }

    [TestMethod]
    public async Task TheGateReservesNothingAndAnswersTheSameQuestionTheSameWay()
    {
        var options = new HttpAssetLoaderOptions { MaxConcurrentRequests = 1, MaxConcurrentRequestsPerHost = 1 };
        var handler = new FakeHttpMessageHandler { RespondAutomatically = false };
        using ServiceProvider provider = CreateProvider(options, handler);
        IAssetLoaderProvider loaders = Loaders(provider);

        Assert.IsTrue(loaders.TryCreateAssetLoader<ImageData>("https://a.test/1.png", out IAssetLoader<ImageData>? first));
        Assert.IsNotNull(first);
        Assert.IsTrue(first.CanLoadNow);
        Assert.IsTrue(first.CanLoadNow, "asking twice with no load in between gives the same answer");
        Assert.AreEqual(0, handler.Count, "asking reserves nothing and starts nothing");

        Task<ImageData> running = Manager(provider).LoadAsync(first, null, CancellationToken.None);

        Assert.IsTrue(loaders.TryCreateAssetLoader<ImageData>("https://b.test/1.png", out IAssetLoader<ImageData>? second));
        Assert.IsNotNull(second);
        Assert.IsFalse(second.CanLoadNow, "the slot moved at submission, inside LoadAsync and before the task was returned");
        Assert.IsFalse(second.CanLoadNow, "and a second ask does not release it either");
        Assert.AreEqual(1, handler.Count, "asking still consumes nothing");

        handler.RespondAutomatically = true;
        handler.Respond(0);

        await running;
    }

    [TestMethod]
    public async Task LoweringALimitAdmitsNothingNewAndAbortsNothingInFlight()
    {
        var options = new HttpAssetLoaderOptions { MaxConcurrentRequests = 2, MaxConcurrentRequestsPerHost = 2 };
        var handler = new FakeHttpMessageHandler { RespondAutomatically = false };
        using ServiceProvider provider = CreateProvider(options, handler);

        Task<ImageData> first = Load(provider, "https://a.test/1.png");
        Task<ImageData> second = Load(provider, "https://a.test/2.png");

        Assert.AreEqual(2, handler.Count);

        options.MaxConcurrentRequests = 1;
        options.MaxConcurrentRequestsPerHost = 1;

        Assert.AreEqual(2, handler.Count, "lowering a cap starts nothing");
        Assert.IsFalse(first.IsCompleted, "and aborts nothing");
        Assert.IsFalse(second.IsCompleted);
        Assert.IsFalse(handler.Requests[0].Token.IsCancellationRequested);
        Assert.IsFalse(handler.Requests[1].Token.IsCancellationRequested);

        Task<ImageData> queued = Load(provider, "https://b.test/1.png");

        Assert.AreEqual(2, handler.Count, "the count is above the new cap, so the new request waits without asking the transport");
        Assert.IsFalse(queued.IsCompleted);

        handler.RespondAutomatically = true;
        handler.Respond(0);
        handler.Respond(1);
        await handler.WaitForCountAsync(3);

        await first;
        await second;
        await queued;
    }
}

