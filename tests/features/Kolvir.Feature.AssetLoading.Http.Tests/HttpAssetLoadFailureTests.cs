using Microsoft.Extensions.DependencyInjection;
using System.Net;

namespace Kolvir.Feature.AssetLoading.Http.Tests;

/// <summary>
/// What a caller hears when the request did not produce an image: a status, a transport failure, and
/// the shared case where one failed request answers every handle over it.
/// </summary>
[TestClass]
[TestCategory("Feature")]
[TestCategory("AssetLoading.Http")]
public sealed class HttpAssetLoadFailureTests : HttpAssetLoadingTestBase
{
    [TestMethod]
    public async Task ANonSuccessStatusFailsTheCallerRatherThanLeavingItWaiting()
    {
        var handler = new FakeHttpMessageHandler { StatusCode = HttpStatusCode.NotFound };
        using ServiceProvider provider = CreateProvider(new HttpAssetLoaderOptions(), handler);

        Task<ImageData> task = Load(provider, "https://example.test/missing.png");

        await Assert.ThrowsExactlyAsync<HttpRequestException>(async () => await task);
        Assert.AreEqual(1, handler.Count);
    }

    [TestMethod]
    public async Task TwoHandlesOverAFailedRequestBothHearTheSameFailure()
    {
        var options = new HttpAssetLoaderOptions { MaxConcurrentRequests = 1, MaxConcurrentRequestsPerHost = 1 };
        var handler = new FakeHttpMessageHandler { RespondAutomatically = false };

        using ServiceProvider provider = CreateProvider(options, handler);

        Task<ImageData> first = Load(provider, "https://example.test/logo.png");
        Task<ImageData> second = Load(provider, "https://example.test/logo.png");

        Assert.AreEqual(1, handler.Count);

        handler.StatusCode = HttpStatusCode.ServiceUnavailable;
        handler.Respond(0);

        await Assert.ThrowsExactlyAsync<HttpRequestException>(async () => await first);
        await Assert.ThrowsExactlyAsync<HttpRequestException>(async () => await second);
    }

    [TestMethod]
    public async Task ATransportFailureFailsTheCallerWithTheReason()
    {
        var handler = new FakeHttpMessageHandler { SendError = new HttpRequestException("the host is unreachable") };
        using ServiceProvider provider = CreateProvider(new HttpAssetLoaderOptions(), handler);

        Task<ImageData> task = Load(provider, "https://offline.test/logo.png");

        await Assert.ThrowsExactlyAsync<HttpRequestException>(async () => await task);
    }

    [TestMethod]
    public async Task ARequestThatOutlivesItsTimeoutFailsTheCallerWithATimeout()
    {
        // One tick rather than a wait: the transport never answers, the deadline fires as soon as the
        // runtime runs its timer, and the test only awaits the task that already has to end. Nothing
        // here sleeps or measures an interval.
        var options = new HttpAssetLoaderOptions { Timeout = TimeSpan.FromTicks(1) };
        var handler = new FakeHttpMessageHandler { RespondAutomatically = false };
        using ServiceProvider provider = CreateProvider(options, handler);

        Task<ImageData> task = Load(provider, "https://slow.test/logo.png");

        await Assert.ThrowsExactlyAsync<TimeoutException>(async () => await task);
        Assert.IsTrue(
            handler.Requests[0].Token.IsCancellationRequested,
            "the deadline cancels the request itself rather than only the caller's wait");
        Assert.AreEqual(
            0,
            Factory(provider).InFlightRequestCount,
            "a request that timed out leaves the map like any other ending");
    }
}
