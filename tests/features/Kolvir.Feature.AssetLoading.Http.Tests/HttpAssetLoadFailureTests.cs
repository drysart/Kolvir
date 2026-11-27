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
}
