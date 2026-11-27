using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Kolvir.Feature.AssetLoading;

/// <summary>
/// The opt-in for asset loading: registers the manager and the provider, and registers the
/// application's factories.
/// </summary>
/// <remarks>
/// <para>
/// Nothing in the feature exists without <see cref="AddAssetLoading"/>: no manager, no provider, and
/// no line of this package in a published executable for an application that never calls it, which
/// is what makes asset loading a rung-3 feature rather than something every application pays for
/// (dev-docs/ASSET-LOADING.md section 7).
/// </para>
/// <para>
/// It registers the infrastructure and nothing else. It registers no loader and no loader family -
/// https, embedded resource, file and the rest each get their own registration method when they are
/// built, in the shape <c>AddAnimations()</c> and <c>AddSvgImages()</c> already use - because a
/// bundle would drag in loaders an application did not ask for (section 2.2).
/// </para>
/// </remarks>
/// <example>
/// <code>
/// using var application = new Application(backend, new ApplicationOptions
/// {
///     ConfigureServices = services => services
///         .AddAssetLoading()
///         .AddAssetLoaderFactory(new EmbeddedResourceLoaderFactory()),
/// });
/// </code>
/// </example>
public static class AssetLoadingServiceCollectionExtensions
{
    /// <summary>Registers the asset load manager and the loader provider for the application.</summary>
    /// <param name="services">The collection being built.</param>
    /// <returns>The same collection, so calls can be chained.</returns>
    /// <remarks>
    /// <para>
    /// Both registrations are <c>TryAddSingleton</c>, so calling this twice - or from two call sites -
    /// does nothing the second time, and a caller's own registration of either service is kept. That
    /// is what stops the infrastructure call from being mandatory and ordering-sensitive.
    /// </para>
    /// <para>
    /// The manager is a singleton because the queue spans windows: a window that closes disposes its
    /// control tree, and its loads cancel through that rather than through a manager per window. The
    /// provider is a singleton because it holds nothing but the factories, which are singletons
    /// themselves.
    /// </para>
    /// <para>
    /// Calling this alone is a legitimate state: the manager and the provider resolve, no factory is
    /// registered, and the provider answers false for every URI. An application that brings its own
    /// factories registers them with <see cref="AddAssetLoaderFactory"/> and needs nothing else.
    /// </para>
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="services"/> is null.</exception>
    public static IServiceCollection AddAssetLoading(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.TryAddSingleton<IAssetLoadManager>(static _ => new AssetLoadManager());
        services.TryAddSingleton<IAssetLoaderProvider>(
            static provider => new AssetLoaderProvider(provider.GetServices<IAssetLoaderFactory>()));

        return services;
    }

    /// <summary>
    /// Registers one factory, in call order, for the loader provider to ask.
    /// </summary>
    /// <param name="services">The collection being built.</param>
    /// <param name="factory">The factory. A factory, not a loader: a factory is what answers "can I
    /// make a loader for this URI" for a whole scheme or format, while a loader is one request.</param>
    /// <returns>The same collection, so calls can be chained.</returns>
    /// <remarks>
    /// <para>
    /// The provider asks the registered factories in registration order and the first match wins, so
    /// this order is the rule: a factory registered earlier claims a URI before one registered later
    /// gets the chance (dev-docs/ASSET-LOADING.md section 2.3). Each call appends its factory, which
    /// is how the ordering stays visible and free.
    /// </para>
    /// <para>
    /// The instance is registered as a singleton because a factory holds the counting state its
    /// loaders coordinate through - the fetch refcount, the per-domain limits - and that is
    /// process-wide by definition. A scoped registration would be a captive dependency: the manager
    /// resolves the provider for the life of the application, and the container only catches a
    /// captive scope with <c>ValidateScopes</c>, which is a <c>Debug</c> tripwire rather than a
    /// guarantee.
    /// </para>
    /// <para>
    /// This does not call <see cref="AddAssetLoading"/>: the infrastructure call is the
    /// application's opt-in, and whether a loader family's own registration makes it on the caller's
    /// behalf is a rule section 2.2 leaves to the first family that ships.
    /// </para>
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="services"/> or
    /// <paramref name="factory"/> is null.</exception>
    public static IServiceCollection AddAssetLoaderFactory(this IServiceCollection services, IAssetLoaderFactory factory)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(factory);

        services.AddSingleton(factory);

        return services;
    }
}
