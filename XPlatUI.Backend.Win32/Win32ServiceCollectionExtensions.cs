using Microsoft.Extensions.DependencyInjection;

namespace XPlatUI.Backend.Win32;

/// <summary>Registration helpers for window hooks that need the Win32 backend itself.</summary>
public static class Win32ServiceCollectionExtensions
{
    /// <summary>
    /// Registers a Win32-only window hook, created once per window.
    /// </summary>
    /// <typeparam name="THook">The hook to register.</typeparam>
    /// <param name="services">The collection being built.</param>
    /// <remarks>
    /// The hook is the native half of the platform hook seam: it is handed the concrete
    /// <see cref="Win32WindowBackend"/>, which is where anything that needs a window handle has to
    /// live. A feature registers its hook from its own <c>Add</c> method, so an application that
    /// never opts in never names the hook and the trimmer drops it. A hook that needs services can
    /// register itself with <see cref="XPlatServiceCollectionExtensions.AddWindowScoped{TService}"/>
    /// instead, which passes a factory the window and the scope's provider.
    /// </remarks>
    public static IServiceCollection AddXPlatWin32WindowHook<THook>(this IServiceCollection services)
        where THook : class, IXPlatWin32WindowHook, new()
    {
        ArgumentNullException.ThrowIfNull(services);

        return services.AddWindowScoped<IXPlatWin32WindowHook>((_, _) => new THook());
    }
}
