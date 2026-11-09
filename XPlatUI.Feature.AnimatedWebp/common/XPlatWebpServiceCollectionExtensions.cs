using Microsoft.Extensions.DependencyInjection;

namespace XPlatUI;

/// <summary>
/// The opt-in for applications that wire their features through
/// <see cref="XPlatApplicationOptions.ConfigureServices"/>.
/// </summary>
/// <remarks>
/// <para>
/// This is the feature's whole contribution to loading: the provider goes into the container, and the
/// <see cref="IXPlatImageDataFactory"/> the application resolves is composed from the providers the
/// container holds. A call that loaded a PNG before this runs loads an animated WebP after it, with no
/// change at the call site - and a static WebP keeps loading, as the same still picture, through the
/// feature's still path.
/// </para>
/// <para>
/// There is nothing to undo, and no WebP-specific loader: registration happens once, while services
/// are declared, and what is specific to WebP is <see cref="XPlatWebpOptions"/>, reached through
/// <see cref="XPlatWebpImageDataFactoryExtensions"/>.
/// </para>
/// </remarks>
/// <example>
/// <code>
/// using var application = new XPlatApplication(backend, new XPlatApplicationOptions
/// {
///     ConfigureServices = services => services.AddAnimations().AddSvgImages().AddAnimatedWebpImages(),
/// });
/// </code>
/// </example>
public static class XPlatWebpServiceCollectionExtensions
{
    /// <summary>Registers the animated WebP image format provider.</summary>
    /// <param name="services">The collection being built.</param>
    /// <returns>The same collection, so calls can be chained.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="services"/> is null.</exception>
    public static IServiceCollection AddAnimatedWebpImages(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddSingleton<IXPlatImageFormatProvider>(XPlatWebpImageFormatProvider.Shared);
        return services;
    }
}
