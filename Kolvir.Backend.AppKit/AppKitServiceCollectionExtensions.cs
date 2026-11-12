using Microsoft.Extensions.DependencyInjection;

namespace Kolvir.Backend.AppKit;

/// <summary>Registration helpers for window hooks that need the AppKit backend itself.</summary>
public static class AppKitServiceCollectionExtensions
{
    /// <summary>
    /// Registers an AppKit-only window hook, created once per window.
    /// </summary>
    /// <typeparam name="THook">The hook to register.</typeparam>
    /// <param name="services">The collection being built.</param>
    /// <remarks>
    /// The hook is the native half of the platform hook seam: it is handed the concrete
    /// <see cref="AppKitWindowBackend"/>, which is where anything that needs the native window has
    /// to live. A feature registers its hook from its own <c>Add</c> method, so an application that
    /// never opts in never names the hook and the trimmer drops it. A hook that needs services can
    /// register itself with <see cref="ServiceCollectionExtensions.AddWindowScoped{TService}"/>
    /// instead, which passes a factory the window and the scope's provider.
    /// </remarks>
    public static IServiceCollection AddAppKitWindowHook<THook>(this IServiceCollection services)
        where THook : class, IAppKitWindowHook, new()
    {
        ArgumentNullException.ThrowIfNull(services);

        return services.AddWindowScoped<IAppKitWindowHook>((_, _) => new THook());
    }
}
