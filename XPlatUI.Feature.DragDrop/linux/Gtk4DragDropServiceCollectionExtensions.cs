using Microsoft.Extensions.DependencyInjection;
using XPlatUI.Backend.Gtk4;

namespace XPlatUI.DragDrop;

/// <summary>Opts a GTK4 application into drag and drop.</summary>
public static class Gtk4DragDropServiceCollectionExtensions
{
    /// <summary>
    /// Registers everything drag and drop needs: the portable router, plus the GTK4 hook that
    /// installs each window view's drop targets.
    /// </summary>
    /// <param name="services">The collection being built.</param>
    /// <returns>The same collection, so calls can be chained.</returns>
    /// <remarks>
    /// Nothing in this assembly runs unless this method is called, and a view that installs no drop
    /// targets never receives a drag.
    /// </remarks>
    public static IServiceCollection AddDragDrop(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddXPlatWindowHook<XPlatDragDropWindowHook>();
        services.AddXPlatGtk4WindowHook<Gtk4DragDropWindowHook>();

        return services;
    }
}
