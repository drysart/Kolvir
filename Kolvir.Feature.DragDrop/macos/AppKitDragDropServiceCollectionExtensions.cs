using Microsoft.Extensions.DependencyInjection;
using Kolvir.Backend.AppKit;

namespace Kolvir.DragDrop;

/// <summary>Opts an AppKit application into drag and drop.</summary>
public static class AppKitDragDropServiceCollectionExtensions
{
    /// <summary>
    /// Registers everything drag and drop needs: the portable router, plus the AppKit hook that
    /// registers each window's view for the pasteboard types a drag can carry.
    /// </summary>
    /// <param name="services">The collection being built.</param>
    /// <returns>The same collection, so calls can be chained.</returns>
    /// <remarks>
    /// Nothing in this assembly runs unless this method is called, and a view that is never
    /// registered never receives a drag.
    /// </remarks>
    public static IServiceCollection AddDragDrop(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddWindowHook<DragDropWindowHook>();
        services.AddAppKitWindowHook<AppKitDragDropWindowHook>();

        return services;
    }
}
