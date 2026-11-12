using Microsoft.Extensions.DependencyInjection;

namespace XPlatUI.Badges;

/// <summary>Opts a macOS application into app badging.</summary>
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
/// here, so an application that leaves it out never reaches AppKit through the feature and the
/// trimmer removes it, which is what keeps the feature at rung 3 of `FEATURE-COST-POLICY.md`.
/// </para>
/// </remarks>
/// <param name="services">The collection being built.</param>
/// <returns>The same collection, so calls can be chained.</returns>
public static class AppKitBadgeServiceCollectionExtensions
{
    /// <summary>Registers the Dock tile badge.</summary>
    /// <param name="services">The collection being built.</param>
    /// <returns>The same collection, so calls can be chained.</returns>
    public static IServiceCollection AddAppBadges(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddSingleton<IXPlatAppBadgePresenter, AppKitAppBadgePresenter>();
        return services;
    }
}
