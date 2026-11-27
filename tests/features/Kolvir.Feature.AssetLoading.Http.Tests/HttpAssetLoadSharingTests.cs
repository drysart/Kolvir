using Microsoft.Extensions.DependencyInjection;

namespace Kolvir.Feature.AssetLoading.Http.Tests;

/// <summary>
/// One request per URI, shared by the handles over it: what they share, what they do not, and what
/// cancellation means for each of them (dev-docs/ASSET-LOADING.md section 4.1).
/// </summary>
[TestClass]
[TestCategory("Feature")]
[TestCategory("AssetLoading.Http")]
public sealed class HttpAssetLoadSharingTests : HttpAssetLoadingTestBase
{
    [TestMethod]
    public async Task TwoHandlesForOneUriShareOneRequestAndEachGetTheirOwnImage()
    {
        var options = new HttpAssetLoaderOptions { MaxConcurrentRequests = 1, MaxConcurrentRequestsPerHost = 1 };
        var handler = new FakeHttpMessageHandler { RespondAutomatically = false };
        using ServiceProvider provider = CreateProvider(options, handler);

        Task<ImageData> first = Load(provider, "https://example.test/logo.png");
        Task<ImageData> second = Load(provider, "https://example.test/logo.png");

        Assert.AreEqual(1, handler.Count, "the second handle shares the request rather than asking again");
        Assert.IsFalse(first.IsCompleted);
        Assert.IsFalse(second.IsCompleted);

        handler.Respond(0);

        ImageData firstImage = await first;
        ImageData secondImage = await second;

        Assert.AreEqual(2f, firstImage.NaturalSize.Width);
        Assert.AreEqual(3f, firstImage.NaturalSize.Height);
        Assert.AreNotSame(
            firstImage,
            secondImage,
            "the bytes are shared; the decoded image belongs to one caller, which is what lets the manager dispose one " +
            "without disturbing the other");
    }

    [TestMethod]
    public async Task CancellingOneHandleLeavesTheSharedRequestRunning()
    {
        var options = new HttpAssetLoaderOptions { MaxConcurrentRequests = 1, MaxConcurrentRequestsPerHost = 1 };
        var handler = new FakeHttpMessageHandler { RespondAutomatically = false };
        using ServiceProvider provider = CreateProvider(options, handler);
        using var firstCancellation = new CancellationTokenSource();
        using var secondCancellation = new CancellationTokenSource();

        Task<ImageData> first = Load(provider, "https://example.test/logo.png", firstCancellation.Token);
        Task<ImageData> second = Load(provider, "https://example.test/logo.png", secondCancellation.Token);

        Assert.AreEqual(1, handler.Count);

        firstCancellation.Cancel();

        Assert.IsTrue(first.IsCanceled, "the caller that cancelled is answered at cancellation time");
        Assert.IsFalse(
            handler.Requests[0].Token.IsCancellationRequested,
            "an earlier handle leaving does not abort a request another handle is still waiting on");
        Assert.IsFalse(second.IsCompleted);

        handler.Respond(0);

        Assert.IsTrue((await second).NaturalSize.Width > 0f, "the handle that stayed gets its image");
    }

    [TestMethod]
    public async Task TheLastCancellationAbortsTheUnderlyingRequest()
    {
        var options = new HttpAssetLoaderOptions { MaxConcurrentRequests = 1, MaxConcurrentRequestsPerHost = 1 };
        var handler = new FakeHttpMessageHandler { RespondAutomatically = false };
        using ServiceProvider provider = CreateProvider(options, handler);
        using var firstCancellation = new CancellationTokenSource();
        using var secondCancellation = new CancellationTokenSource();
        HttpAssetLoaderFactory factory = Factory(provider);

        Task<ImageData> first = Load(provider, "https://example.test/logo.png", firstCancellation.Token);
        Task<ImageData> second = Load(provider, "https://example.test/logo.png", secondCancellation.Token);
        HttpAssetFetch? fetch = factory.PeekFetch("https://example.test/logo.png");

        Assert.IsNotNull(fetch);

        firstCancellation.Cancel();

        Assert.IsFalse(handler.Requests[0].Token.IsCancellationRequested);

        secondCancellation.Cancel();

        Assert.IsTrue(first.IsCanceled);
        Assert.IsTrue(second.IsCanceled);
        Assert.IsTrue(
            handler.Requests[0].Token.IsCancellationRequested,
            "the last handle going is the only thing that means nobody wants the bytes");

        await fetch.Completion;

        Assert.AreEqual(0, factory.InFlightRequestCount, "and the aborted request has left the map");
    }
}
