using Microsoft.Extensions.DependencyInjection;

namespace XPlatUI.Svg;

/// <summary>
/// The opt-in for applications that wire their features through
/// <see cref="XPlatApplicationOptions.ConfigureServices"/>.
/// </summary>
/// <remarks>
/// <para>
/// This exists so that turning SVG on happens in the same place as turning every other feature on,
/// not because there is a service to resolve. An image format is process state - which provider
/// claims a stream's bytes is not a question a window can answer differently from another window -
/// so the call registers the provider immediately and returns the collection for chaining.
/// </para>
/// <para>
/// One consequence is worth stating rather than discovering: the registration belongs to the
/// process, so it is not undone when the container is disposed. An application that builds and
/// disposes a second container in the same process keeps SVG support, which is what an image format
/// wants; a test that needs the previous state back calls <see cref="XPlatSvg.AddSvgImages"/> and
/// disposes the handle it returns.
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

        XPlatSvg.AddSvgImages();
        return services;
    }
}
