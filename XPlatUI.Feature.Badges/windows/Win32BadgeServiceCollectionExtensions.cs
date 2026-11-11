using Microsoft.Extensions.DependencyInjection;
using XPlatUI.Backend.Win32;

namespace XPlatUI.Badges;

/// <summary>Opts a Win32 application into app badging.</summary>
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
/// here, so an application that leaves it out never reaches the taskbar's COM interface through the
/// feature and the trimmer removes it, which is what keeps the feature at rung 3 of
/// `FEATURE-COST-POLICY.md`.
/// </para>
/// </remarks>
public static class Win32BadgeServiceCollectionExtensions
{
    /// <summary>Registers the taskbar overlay icon.</summary>
    /// <param name="services">The collection being built.</param>
    /// <returns>The same collection, so calls can be chained.</returns>
    public static IServiceCollection AddAppBadges(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        // The badge belongs to the application rather than to a window, and the windows take their
        // overlay from it, so it is the one instance both registrations point at.
        services.AddSingleton<Win32AppBadgePresenter>();
        services.AddSingleton<IXPlatAppBadgePresenter>(static provider =>
            provider.GetRequiredService<Win32AppBadgePresenter>());

        services.AddWindowScoped<IXPlatWin32WindowHook>(static (_, provider) =>
            new Win32BadgeWindowHook(provider.GetRequiredService<Win32AppBadgePresenter>()));

        return services;
    }
}
