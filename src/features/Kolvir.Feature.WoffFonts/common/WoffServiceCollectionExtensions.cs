using Microsoft.Extensions.DependencyInjection;

namespace Kolvir.WoffFonts;

/// <summary>
/// The opt-in for applications that wire their features through
/// <see cref="ApplicationOptions.ConfigureServices"/>.
/// </summary>
/// <remarks>
/// <para>
/// This is the registration. The provider goes into the container, and the <see cref="IFontLoader"/>
/// the application resolves is composed from the providers the container holds - so every load reads
/// WOFF and WOFF 2 as well as the plain files the library already reads, and the call site does not
/// change.
/// </para>
/// <para>
/// There is nothing to undo: registration happens once, while services are declared, and the
/// container owns the provider's lifetime. An application that builds a second container gets its own
/// answer about which formats it has, which is the point of doing it this way rather than through a
/// process-wide registry.
/// </para>
/// <para>
/// A test that wants only this format composes its loader directly instead:
/// <c>new FontLoader([WoffFontFormatProvider.Shared])</c>.
/// </para>
/// </remarks>
/// <example>
/// <code>
/// using var application = new Application(backend, new ApplicationOptions
/// {
///     ConfigureServices = services => services.AddWoffFonts(),
/// });
///
/// var fonts = application.Services.GetRequiredService&lt;IFontLoader&gt;();
/// label.Font = fonts.LoadFromPath("Inter-Regular.woff2", 15f);
/// </code>
/// </example>
public static class WoffServiceCollectionExtensions
{
    /// <summary>Registers the WOFF and WOFF 2 font format provider.</summary>
    /// <param name="services">The collection being built.</param>
    /// <returns>The same collection, so calls can be chained.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="services"/> is null.</exception>
    public static IServiceCollection AddWoffFonts(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddSingleton<IFontFormatProvider>(WoffFontFormatProvider.Shared);
        return services;
    }
}
