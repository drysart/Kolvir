using Microsoft.Extensions.DependencyInjection;

namespace Kolvir.Feature.AssetLoading.Http.Tests;

/// <summary>
/// The memory rule: the in-flight map is not a cache, and the encoded bytes outlive neither the
/// decode nor the handles that were waiting for it.
/// </summary>
/// <remarks>
/// The suite asserts what the loader is still holding rather than what the garbage collector has
/// already freed. A <see cref="WeakReference"/> plus a forced collection is the only test that could
/// observe the array itself, and it is exactly the test that goes flaky in a debug build or on a
/// different runtime - which would be worse than none. What is asserted instead is the two structural
/// facts that make the array drop: a completed fetch leaves the map, and the shared result releases
/// its bytes when the last handle has decoded, even while the test still holds the fetch object.
/// </remarks>
[TestClass]
[TestCategory("Feature")]
[TestCategory("AssetLoading.Http")]
public sealed class HttpAssetLoadMemoryTests : HttpAssetLoadingTestBase
{
    [TestMethod]
    public async Task ACompletedFetchLeavesTheInFlightMap()
    {
        var handler = new FakeHttpMessageHandler { RespondAutomatically = false };
        using ServiceProvider provider = CreateProvider(new HttpAssetLoaderOptions(), handler);
        HttpAssetLoaderFactory factory = Factory(provider);

        Task<ImageData> task = Load(provider, "https://example.test/logo.png");

        Assert.AreEqual(1, factory.InFlightRequestCount, "the request is shared while it runs");

        handler.Respond(0);
        await task;

        Assert.AreEqual(0, factory.InFlightRequestCount, "a completed fetch is not left in the map for a later caller");
    }

    [TestMethod]
    public async Task ASecondLoadOfTheSameUriMakesASecondRequest()
    {
        var handler = new FakeHttpMessageHandler();
        using ServiceProvider provider = CreateProvider(new HttpAssetLoaderOptions(), handler);

        await Load(provider, "https://example.test/logo.png");
        await Load(provider, "https://example.test/logo.png");

        Assert.AreEqual(
            2,
            handler.Count,
            "the first load's bytes were dropped with its fetch, so the second load is a request rather than a cache hit");
    }

    [TestMethod]
    public async Task TheSharedResultDropsItsBytesWhenTheLastHandleHasDecoded()
    {
        var options = new HttpAssetLoaderOptions { MaxConcurrentRequests = 1, MaxConcurrentRequestsPerHost = 1 };
        var handler = new FakeHttpMessageHandler { RespondAutomatically = false };
        using ServiceProvider provider = CreateProvider(options, handler);
        HttpAssetLoaderFactory factory = Factory(provider);

        Task<ImageData> first = Load(provider, "https://example.test/logo.png");
        Task<ImageData> second = Load(provider, "https://example.test/logo.png");
        HttpAssetFetch? fetch = factory.PeekFetch("https://example.test/logo.png");

        Assert.IsNotNull(fetch, "the test holds the shared object for longer than the loader does");

        // From a pool thread, which is where a real response arrives, so the decode follows through
        // the same path an application's fetch does rather than inside this call.
        await Task.Run(() => handler.Respond(0));

        await first;
        await second;

        Assert.IsNull(
            fetch.Completion.Result.Bytes,
            "the last decode released the encoded array, so even a leaked reference to the fetch holds no image file");
    }
}
