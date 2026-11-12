using Microsoft.Extensions.DependencyInjection;

namespace Kolvir;

/// <summary>Registration helpers for the hook seam.</summary>
public static partial class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers a hook that runs a single callback at one application phase.
    /// </summary>
    /// <param name="services">The collection being built.</param>
    /// <param name="phase">The phase <paramref name="hookFunc"/> runs at.</param>
    /// <param name="hookFunc">The work to do. Receives the application.</param>
    /// <remarks>
    /// Registering twice registers two hooks, which run in registration order. For teardown that
    /// has to happen for every application, including one that never runs a loop, use
    /// <see cref="ApplicationPhase.Disposed"/> rather than
    /// <see cref="ApplicationPhase.Stopping"/>.
    /// </remarks>
    public static IServiceCollection AddApplicationHook(
        this IServiceCollection services,
        ApplicationPhase phase,
        Action<Application> hookFunc)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(hookFunc);

        return services.AddSingleton<IApplicationHook>(
            _ => new ActionBasedAppHook(phase, hookFunc));
    }

    /// <summary>Shorthand for a hook at <see cref="ApplicationPhase.Creating"/>.</summary>
    /// <param name="services">The collection being built.</param>
    /// <param name="startupHookFunc">The work to do before the first window exists.</param>
    public static IServiceCollection AddApplicationStartupHook(
        this IServiceCollection services,
        Action<Application> startupHookFunc) =>
        services.AddApplicationHook(ApplicationPhase.Creating, startupHookFunc);

    /// <summary>
    /// Registers a window hook type, creating one instance per window.
    /// </summary>
    /// <typeparam name="THook">The hook to register.</typeparam>
    /// <param name="services">The collection being built.</param>
    /// <remarks>
    /// This is what a feature uses to install itself: the hook type is named only by the feature's
    /// own registration method, so an application that never calls that method never names the type
    /// and the trimmer drops it along with everything only it reaches. A hook that needs services
    /// can register itself with <see cref="AddWindowScoped{TService}"/> instead, which passes the
    /// window and the scope's provider to a factory.
    /// </remarks>
    public static IServiceCollection AddWindowHook<THook>(this IServiceCollection services)
        where THook : class, IWindowHook, new()
    {
        ArgumentNullException.ThrowIfNull(services);

        return services.AddWindowScoped<IWindowHook>((_, _) => new THook());
    }

    /// <summary>
    /// Registers a window hook that runs one callback per phase, skipping the phases left null.
    /// </summary>
    /// <param name="services">The collection being built.</param>
    /// <param name="onCreated">Runs at <see cref="WindowPhase.Created"/>.</param>
    /// <param name="onShowing">Runs at <see cref="WindowPhase.Showing"/>.</param>
    /// <param name="onShown">Runs at <see cref="WindowPhase.Shown"/>.</param>
    /// <param name="onHiding">Runs at <see cref="WindowPhase.Hiding"/>.</param>
    /// <param name="onHidden">Runs at <see cref="WindowPhase.Hidden"/>.</param>
    /// <param name="onClosing">Runs at <see cref="WindowPhase.Closing"/>.</param>
    /// <param name="onClosed">Runs at <see cref="WindowPhase.Closed"/>.</param>
    /// <param name="onDisposed">Runs at <see cref="WindowPhase.Disposed"/>.</param>
    /// <remarks>
    /// The hook is window-scoped, so each window gets its own instance and its own copy of anything
    /// a callback closes over per window. A callback that needs state shared across phases should
    /// hold it in the captured closure, not in a table keyed by window.
    /// </remarks>
    public static IServiceCollection AddWindowHook(
        this IServiceCollection services,
        Action<Window>? onCreated = null,
        Action<Window>? onShowing = null,
        Action<Window>? onShown = null,
        Action<Window>? onHiding = null,
        Action<Window>? onHidden = null,
        Action<Window>? onClosing = null,
        Action<Window>? onClosed = null,
        Action<Window>? onDisposed = null)
    {
        ArgumentNullException.ThrowIfNull(services);

        return services.AddWindowScoped<IWindowHook>((_, _) => new ActionBasedWindowHook(
            onCreated,
            onShowing,
            onShown,
            onHiding,
            onHidden,
            onClosing,
            onClosed,
            onDisposed));
    }
}
