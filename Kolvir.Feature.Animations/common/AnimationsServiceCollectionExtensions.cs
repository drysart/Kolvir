using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Kolvir.Animations;

/// <summary>
/// The opt-in for animations: registers the application's single frame clock.
/// </summary>
/// <remarks>
/// <para>
/// This is the whole of what an application has to do. Nothing in the feature runs without it - no
/// scheduler exists, nothing resolves one, and an application that never calls this gets none of the
/// code in its published executable, which is what makes animations a rung-3 feature rather than
/// something every application pays for.
/// </para>
/// <para>
/// The scheduler is a singleton because the clock is per application, not per control: one timer
/// advances every animation the application runs, and no timer runs at all while nothing is
/// animating.
/// </para>
/// </remarks>
/// <example>
/// <code>
/// using var application = new Application(backend, new ApplicationOptions
/// {
///     ConfigureServices = services => services.AddAnimations(),
/// });
/// </code>
/// </example>
public static class AnimationsServiceCollectionExtensions
{
    /// <summary>Registers the animation scheduler for the application.</summary>
    /// <param name="services">The collection being built.</param>
    /// <returns>The same collection, so calls can be chained.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="services"/> is null.</exception>
    /// <remarks>
    /// A <see cref="TimeProvider"/> is registered only when the application has not supplied one, so
    /// an application or a test that registers its own clock before calling this gets it used
    /// instead of the system clock. Both registrations are <c>TryAdd</c>, which is what makes the
    /// call idempotent and what keeps a caller's choice.
    /// </remarks>
    public static IServiceCollection AddAnimations(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.TryAddSingleton(TimeProvider.System);
        services.TryAddSingleton(CreateScheduler);

        return services;
    }

    private static AnimationScheduler CreateScheduler(IServiceProvider provider) =>
        new(provider.GetRequiredService<TimeProvider>(), new AnimationTimerFrameSource());
}
