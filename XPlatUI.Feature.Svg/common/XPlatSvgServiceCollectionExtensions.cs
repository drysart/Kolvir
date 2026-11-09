using Microsoft.Extensions.DependencyInjection;

namespace XPlatUI.Svg;

/// <summary>
/// The opt-in for applications that wire their features through
/// <see cref="XPlatApplicationOptions.ConfigureServices"/>.
/// </summary>
/// <remarks>
/// <para>
/// This is the registration. The provider goes into the container, and the
/// <see cref="IXPlatImageDataFactory"/> the application resolves is composed from the providers the
/// container holds - so every control that is handed the factory's output reads SVG, and the call
/// site does not change.
/// </para>
/// <para>
/// There is nothing to undo: registration happens once, while services are declared, and the
/// container owns the provider's lifetime. An application that builds a second container gets its own
/// answer about which formats it has, which is the point of doing it this way rather than through a
/// process-wide registry.
/// </para>
/// <para>
/// A test that wants only some formats composes its factory directly instead:
/// <c>new XPlatImageDataFactory([XPlatSvgImageFormatProvider.Shared])</c>.
/// </para>
/// </remarks>
/// <example>
/// <code>
/// using var application = new XPlatApplication(backend, new XPlatApplicationOptions
/// {
///     ConfigureServices = services => services.AddAnimations().AddSvgImages(),
/// });
/// </code>
/// </example>
public static class XPlatSvgServiceCollectionExtensions
{
    /// <summary>Registers the SVG image format provider.</summary>
    /// <param name="services">The collection being built.</param>
    /// <returns>The same collection, so calls can be chained.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="services"/> is null.</exception>
    public static IServiceCollection AddSvgImages(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddSingleton<IXPlatImageFormatProvider>(XPlatSvgImageFormatProvider.Shared);
        return services;
    }
}
