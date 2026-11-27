using Microsoft.Extensions.DependencyInjection;

namespace Kolvir.Feature.AssetLoading.Http.Tests;

/// <summary>
/// The registration: one call, what it puts in the container, what the factory claims, and the
/// transport options an application supplies (dev-docs/ASSET-LOADING.md section 2.2).
/// </summary>
[TestClass]
[TestCategory("Feature")]
[TestCategory("AssetLoading.Http")]
public sealed class HttpAssetLoaderRegistrationTests : HttpAssetLoadingTestBase
{
    [TestMethod]
    public void OneCallRegistersTheManagerTheOptionsAndTheFactory()
    {
        var options = new HttpAssetLoaderOptions();
        using ServiceProvider provider = CreateProvider(options, new FakeHttpMessageHandler());

        Assert.AreSame(options, provider.GetRequiredService<HttpAssetLoaderOptions>(), "the caller's object is the one in the container");
        Assert.IsNotNull(provider.GetRequiredService<IAssetLoadManager>(), "the manager is registered by the one call");
        Assert.AreEqual(1, provider.GetServices<IAssetLoaderFactory>().Count(), "one factory, not one per registration");
        Assert.IsInstanceOfType<HttpAssetLoaderFactory>(provider.GetServices<IAssetLoaderFactory>().Single());
    }

    [TestMethod]
    public void CallingTheRegistrationTwiceLeavesOneFactory()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IImageDataFactory>(static _ => new ImageDataFactory());
        services.AddHttpAssetLoading();
        services.AddHttpAssetLoading();

        using ServiceProvider provider = services.BuildServiceProvider();

        Assert.AreEqual(
            1,
            provider.GetServices<IAssetLoaderFactory>().Count(),
            "TryAddEnumerable recognises the factory's implementation type, so the second call adds nothing");
        Assert.IsNotNull(provider.GetRequiredService<IAssetLoadManager>(), "and AddAssetLoading() stays idempotent too");
    }

    [TestMethod]
    public void TheFactoryClaimsAbsoluteHttpAndHttpsUrisOnly()
    {
        using ServiceProvider provider = CreateProvider(new HttpAssetLoaderOptions(), new FakeHttpMessageHandler());
        IAssetLoaderProvider loaders = Loaders(provider);

        Assert.IsTrue(loaders.TryCreateAssetLoader<ImageData>("https://example.test/a.png", out _), "https is claimed");
        Assert.IsTrue(loaders.TryCreateAssetLoader<ImageData>("http://example.test/a.png", out _), "http is claimed");
        Assert.IsTrue(loaders.TryCreateAssetLoader<ImageData>("HTTPS://EXAMPLE.TEST/a.png", out _), "the scheme is case-insensitive");

        Assert.IsFalse(loaders.TryCreateAssetLoader<ImageData>("ftp://example.test/a.png", out _), "another scheme is somebody else's");
        Assert.IsFalse(loaders.TryCreateAssetLoader<ImageData>("file:///c:/images/a.png", out _));
        Assert.IsFalse(loaders.TryCreateAssetLoader<ImageData>("/images/a.png", out _), "a relative path is not a URL");
        Assert.IsFalse(loaders.TryCreateAssetLoader<ImageData>("not a uri", out _));
        Assert.IsFalse(loaders.TryCreateAssetLoader<string>("https://example.test/a.png", out _), "the result type has to match exactly");
    }

    [TestMethod]
    public async Task AClaimedUriIsLoadedThroughTheManagerAsImageData()
    {
        var handler = new FakeHttpMessageHandler();
        using ServiceProvider provider = CreateProvider(new HttpAssetLoaderOptions(), handler);

        ImageData image = await Load(provider, "https://example.test/logo.png");

        Assert.AreEqual(2f, image.NaturalSize.Width);
        Assert.AreEqual(3f, image.NaturalSize.Height);
        Assert.AreEqual(1, handler.Count);
    }

    [TestMethod]
    public async Task TheLoaderSendsAProductUserAgentByDefault()
    {
        var handler = new FakeHttpMessageHandler();
        using ServiceProvider provider = CreateProvider(new HttpAssetLoaderOptions(), handler);

        await Load(provider, "https://example.test/a.png");

        Assert.IsNotNull(handler.Requests[0].UserAgent, "a request with no User-Agent is refused by many CDNs");
        StringAssert.StartsWith(handler.Requests[0].UserAgent!, "Kolvir.Feature.AssetLoading.Http/");
    }

    [TestMethod]
    public async Task ACustomUserAgentReplacesTheDefault()
    {
        var handler = new FakeHttpMessageHandler();
        using ServiceProvider provider = CreateProvider(new HttpAssetLoaderOptions { UserAgent = "MyApp/2.1" }, handler);

        await Load(provider, "https://example.test/a.png");

        Assert.AreEqual("MyApp/2.1", handler.Requests[0].UserAgent);
    }

    [TestMethod]
    public async Task AnApplicationSuppliedClientIsUsedAsItIs()
    {
        var handler = new FakeHttpMessageHandler();
        using var client = new HttpClient(handler);
        var options = new HttpAssetLoaderOptions { HttpClient = client };

        using ServiceProvider provider = CreateProvider(options);

        await Load(provider, "https://example.test/a.png");

        Assert.AreEqual(1, handler.Count);
        Assert.IsNull(
            handler.Requests[0].UserAgent,
            "the client's own default headers are the application's decision, so the loader adds none");
    }

    [TestMethod]
    public async Task SettingBothAClientAndAHandlerFailsTheLoadRatherThanPickingOne()
    {
        var handler = new FakeHttpMessageHandler();
        using var client = new HttpClient(handler);
        var options = new HttpAssetLoaderOptions { HttpClient = client, MessageHandler = handler };

        using ServiceProvider provider = CreateProvider(options);

        Task<ImageData> task = Load(provider, "https://example.test/a.png");

        await Assert.ThrowsExactlyAsync<InvalidOperationException>(async () => await task);
        Assert.AreEqual(0, handler.Count, "nothing was sent, so neither transport was chosen");
    }
}
