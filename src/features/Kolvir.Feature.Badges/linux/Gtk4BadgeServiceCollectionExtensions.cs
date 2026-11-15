using Microsoft.Extensions.DependencyInjection;

namespace Kolvir.Badges;

/// <summary>Opts a Linux (GTK 4) application into app badging.</summary>
/// <remarks>
/// <para>
/// Same name, same signature, on every platform the feature supports, so an application writes one
/// line and never names the platform:
/// <code>
/// options.ConfigureServices = services => services.AddAppBadges();
/// </code>
/// </para>
/// <para>
/// Nothing in this assembly runs unless this method is called, and the platform half is named only
/// here, so an application that leaves it out never reaches GTK through the feature and the trimmer
/// removes it, which is what keeps the feature at rung 3 of `FEATURE-COST-POLICY.md`.
/// </para>
/// </remarks>
public static class Gtk4BadgeServiceCollectionExtensions
{
    /// <summary>Registers the Unity LauncherEntry badge.</summary>
    /// <param name="services">The collection being built.</param>
    /// <returns>The same collection, so calls can be chained.</returns>
    public static IServiceCollection AddAppBadges(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        // The presenter is built over the backend, which registers itself, because the desktop id a
        // badge has to name is the one the backend derived from the application's instance key.
        services.AddSingleton<IAppBadgePresenter, Gtk4AppBadgePresenter>();
        return services;
    }
}
