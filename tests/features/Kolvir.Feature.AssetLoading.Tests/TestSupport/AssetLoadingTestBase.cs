using Microsoft.Extensions.DependencyInjection;

namespace Kolvir.Feature.AssetLoading.Tests;

/// <summary>
/// The base every test class derives from: it builds the manager the way an application does, through
/// <see cref="AssetLoadingServiceCollectionExtensions.AddAssetLoading"/>, and releases the containers
/// it made afterwards even when a test fails.
/// </summary>
/// <remarks>
/// <para>
/// Going through a container rather than constructing the manager keeps the tests to the public
/// surface, which is what an application has, and it means the registration is exercised by every
/// test that needs a manager rather than by one test that could rot unnoticed.
/// </para>
/// <para>
/// Tests in this assembly are not parallelised: the queue is UI-thread state and the suite assumes one
/// thread, the same way the library does. It also means the one test that builds an
/// <see cref="Application"/> never races another test for the process-wide instance.
/// </para>
/// </remarks>
public abstract class AssetLoadingTestBase
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

    /// <summary>Builds a container with the given registrations and keeps it for cleanup.</summary>
    /// <param name="configure">The registrations to make.</param>
    /// <returns>The container, for resolving services from.</returns>
    protected ServiceProvider CreateProvider(Action<IServiceCollection> configure)
    {
        var services = new ServiceCollection();
        configure(services);

        ServiceProvider provider = services.BuildServiceProvider();
        _providers.Add(provider);

        return provider;
    }

    /// <summary>The manager an application would resolve, with nothing registered in it.</summary>
    /// <returns>The application's single manager.</returns>
    protected IAssetLoadManager CreateManager() =>
        CreateProvider(services => services.AddAssetLoading()).GetRequiredService<IAssetLoadManager>();
}
