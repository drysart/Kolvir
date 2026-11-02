using Microsoft.Extensions.DependencyInjection;
using XPlatUI.Backend.Win32;

namespace XPlatUI.DragDrop;

/// <summary>Opts a Win32 application into drag and drop.</summary>
public static class Win32DragDropServiceCollectionExtensions
{
    /// <summary>
    /// Registers everything drag and drop needs: the portable router, plus the Win32 hook that
    /// registers the OLE drop target with each window's native handle.
    /// </summary>
    /// <param name="services">The collection being built.</param>
    /// <returns>The same collection, so calls can be chained.</returns>
    /// <remarks>
    /// Nothing in this assembly runs unless this method is called. Both halves are named only here,
    /// so an application that leaves it out never reaches the OLE target and the trimmer removes it,
    /// which is what makes the feature cost nothing when it is not opted into.
    /// </remarks>
    public static IServiceCollection AddDragDrop(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddXPlatWindowHook<XPlatDragDropWindowHook>();
        services.AddXPlatWin32WindowHook<Win32DragDropWindowHook>();

        return services;
    }
}
