using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Kolvir.Feature.AssetLoading.Http;

/// <summary>
/// The opt-in for applications that want <c>http</c> and <c>https</c> URIs loaded into
/// <see cref="ImageData"/>: one call registers the options, the loader factory and the manager the
/// factory is scheduled by.
/// </summary>
/// <remarks>
/// <para>
/// <b>It stands on its own.</b> It calls <see cref="AssetLoadingServiceCollectionExtensions.AddAssetLoading"/>
/// itself, and does so with <c>TryAddSingleton</c> registrations, so an application that already
/// called it - or that calls both in any order - gets one manager rather than two. That is the rule
/// dev-docs/ASSET-LOADING.md section 2.2 left for the first loader family to settle: the
/// infrastructure call is not something a reader has to remember, because the loader registration
/// that needs it makes it.
/// </para>
/// <para>
/// <b>Nothing happens until this is called.</b> An application that never calls it has none of this
/// assembly in its published output - the package is the opt-in
/// (FEATURE-COST-POLICY.md section 4) - and the demo application does not, because it has no network.
/// </para>
/// <para>
/// <b>Runtime access to the options.</b> Keep the instance passed to the call. Raising a limit is
/// then one assignment with no service lookup and no knowledge of the container:
/// </para>
/// <code>
/// var http = new HttpAssetLoaderOptions { MaxConcurrentRequestsPerHost = 4 };
/// services.AddHttpAssetLoading(http);
///
/// http.MaxConcurrentRequestsPerHost = 12;   // the queue starts moving on the next walk
/// </code>
/// <para>
/// The instance is the same one the container holds, so an application that keeps it and one that
/// resolves it are looking at the same values. Resolving is supported for code that does not keep
/// the reference, but it is not the route this API is shaped around.
/// </para>
/// </remarks>
/// <example>
/// <code>
/// using var application = new Application(backend, new ApplicationOptions
/// {
///     ConfigureServices = services => services
///         .AddAssetLoading()
///         .AddHttpAssetLoading(),
/// });
///
/// var image = new Image { SourceUri = "https://example.test/logo.png" };
/// </code>
/// </example>
public static class HttpAssetLoadingServiceCollectionExtensions
{
    /// <summary>
    /// Registers HTTP asset loading with the default settings.
    /// </summary>
    /// <param name="services">The collection being built.</param>
    /// <returns>The same collection, so calls can be chained.</returns>
    /// <remarks>
    /// The options instance is the default one, so an application that omitted this overload in
    /// favour of <see cref="AddHttpAssetLoading(IServiceCollection, HttpAssetLoaderOptions)"/> and
    /// wants the defaults can construct a new <see cref="HttpAssetLoaderOptions"/> instead. There is
    /// one options object per application, registered as a singleton because the factory reads it
    /// live for the life of the process.
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="services"/> is null.</exception>
    public static IServiceCollection AddHttpAssetLoading(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddAssetLoading();
        services.TryAddSingleton(static _ => new HttpAssetLoaderOptions());
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IAssetLoaderFactory, HttpAssetLoaderFactory>());

        return services;
    }

    /// <summary>
    /// Registers HTTP asset loading with the given options, which stay live for the application's
    /// life.
    /// </summary>
    /// <param name="services">The collection being built.</param>
    /// <param name="options">
    /// The settings. It is registered as an instance, so the caller keeps a reference to the same
    /// object the loader reads; assigning to it later changes the running loader.
    /// </param>
    /// <returns>The same collection, so calls can be chained.</returns>
    /// <remarks>
    /// <para>
    /// The first options registration wins, in the same way every other registration in this feature
    /// is a <c>TryAdd</c>: registering an instance yourself before this call is supported, and this
    /// call then leaves yours in place. Call it once - a second call adds nothing, because
    /// <c>TryAddEnumerable</c> recognises the factory's implementation type.
    /// </para>
    /// <para>
    /// The factory depends on <see cref="IImageDataFactory"/>, which
    /// <see cref="Application"/> registers after an application's own registrations run - so it is
    /// present whether this call is made through <c>ConfigureServices</c> or from an application
    /// without a window. A container built without an application has to register one itself, which
    /// is what the suite does.
    /// </para>
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="services"/> or
    /// <paramref name="options"/> is null.</exception>
    public static IServiceCollection AddHttpAssetLoading(
        this IServiceCollection services,
        HttpAssetLoaderOptions options)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(options);

        services.AddAssetLoading();
        services.TryAddSingleton(options);
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IAssetLoaderFactory, HttpAssetLoaderFactory>());

        return services;
    }
}
