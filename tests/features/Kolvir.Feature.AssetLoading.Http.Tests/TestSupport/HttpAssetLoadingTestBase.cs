using Microsoft.Extensions.DependencyInjection;

namespace Kolvir.Feature.AssetLoading.Http.Tests;

/// <summary>
/// The base every test class derives from: it builds the container the way an application does,
/// through <see cref="HttpAssetLoadingServiceCollectionExtensions.AddHttpAssetLoading"/>, and releases
/// what it made afterwards even when a test fails.
/// </summary>
/// <remarks>
/// <para>
/// It registers <see cref="IImageDataFactory"/> the way <see cref="Application"/> does, because a
/// container that is not an application's does not get one for free. Everything else is the public
/// registration under test, so the suite exercises the call an application makes rather than
/// constructing the factory by hand.
/// </para>
/// <para>
/// There is no message loop here. A test awaits the task that a real application would await, and a
/// request that must start is observed through the transport's own signal rather than through a
/// timer, so nothing in the suite sleeps or races a clock. Tests in this assembly are not
/// parallelised: the manager's queue is UI-thread state and the suite assumes one thread, the same
/// way the library does.
/// </para>
/// </remarks>
public abstract class HttpAssetLoadingTestBase
{
    private readonly List<ServiceProvider> _providers = [];

    /// <summary>Disposes the containers a test built, so no state outlives it.</summary>
    [TestCleanup]
    public void DisposeProviders()
    {
        foreach (ServiceProvider provider in _providers)
        {
            provider.Dispose();
        }

        _providers.Clear();
    }

    /// <summary>Builds the container an application would have, with the test's transport.</summary>
    /// <param name="options">The settings under test, which stay live.</param>
    /// <param name="handler">The fake transport, or null for the platform's default handler.</param>
    /// <returns>The container, for resolving the manager and the provider.</returns>
    protected ServiceProvider CreateProvider(HttpAssetLoaderOptions options, HttpMessageHandler? handler = null)
    {
        if (handler is not null)
        {
            options.MessageHandler = handler;
        }

        var services = new ServiceCollection();
        services.AddSingleton<IImageDataFactory>(static _ => new ImageDataFactory());
        services.AddHttpAssetLoading(options);

        ServiceProvider provider = services.BuildServiceProvider();
        _providers.Add(provider);

        return provider;
    }

    /// <summary>The manager an application resolves, which is what schedules a handle.</summary>
    /// <param name="provider">The container.</param>
    /// <returns>The application's single manager.</returns>
    protected static IAssetLoadManager Manager(IServiceProvider provider) =>
        provider.GetRequiredService<IAssetLoadManager>();

    /// <summary>The provider that turns a URI into the loader family's handle.</summary>
    /// <param name="provider">The container.</param>
    /// <returns>The application's single loader provider.</returns>
    protected static IAssetLoaderProvider Loaders(IServiceProvider provider) =>
        provider.GetRequiredService<IAssetLoaderProvider>();

    /// <summary>The concrete factory, so a test can read the in-flight count the suite proves with.</summary>
    /// <param name="provider">The container.</param>
    /// <returns>The registered HTTP factory.</returns>
    internal static HttpAssetLoaderFactory Factory(IServiceProvider provider) =>
        provider.GetServices<IAssetLoaderFactory>().OfType<HttpAssetLoaderFactory>().Single();

    /// <summary>Asks the provider for a handle and submits it to the manager, as an application does.</summary>
    /// <param name="provider">The container.</param>
    /// <param name="uri">The URI to load.</param>
    /// <param name="cancellationToken">The caller's token.</param>
    /// <returns>The caller's task.</returns>
    protected static Task<ImageData> Load(
        IServiceProvider provider,
        string uri,
        CancellationToken cancellationToken = default)
    {
        Assert.IsTrue(
            Loaders(provider).TryCreateAssetLoader(uri, out IAssetLoader<ImageData>? loader),
            $"the provider should make a loader for {uri}");

        return Manager(provider).LoadAsync(loader, null, cancellationToken);
    }
}
