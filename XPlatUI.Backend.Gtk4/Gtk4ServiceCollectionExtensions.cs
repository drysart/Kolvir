using Microsoft.Extensions.DependencyInjection;

namespace XPlatUI.Backend.Gtk4;

/// <summary>Registration helpers for window hooks that need the GTK4 backend itself.</summary>
public static class Gtk4ServiceCollectionExtensions
{
    /// <summary>
    /// Registers a GTK4-only window hook, created once per window.
    /// </summary>
    /// <typeparam name="THook">The hook to register.</typeparam>
    /// <param name="services">The collection being built.</param>
    /// <remarks>
    /// The hook is the native half of the platform hook seam: it is handed the concrete
    /// <see cref="Gtk4WindowBackend"/>, which is where anything that needs the native window has to
    /// live. A feature registers its hook from its own <c>Add</c> method, so an application that
    /// never opts in never names the hook and the trimmer drops it. A hook that needs services can
    /// register itself with <see cref="XPlatServiceCollectionExtensions.AddWindowScoped{TService}"/>
    /// instead, which passes a factory the window and the scope's provider.
    /// </remarks>
    public static IServiceCollection AddXPlatGtk4WindowHook<THook>(this IServiceCollection services)
        where THook : class, IXPlatGtk4WindowHook, new()
    {
        ArgumentNullException.ThrowIfNull(services);

        return services.AddWindowScoped<IXPlatGtk4WindowHook>((_, _) => new THook());
    }
}
