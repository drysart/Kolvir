using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Kolvir.Audio;

/// <summary>Opts a macOS application into audio.</summary>
/// <remarks>
/// <para>
/// Same name, same signature, on every platform the feature supports, so an application writes one
/// line and never names the platform:
/// <code>
/// options.ConfigureServices = services => services.AddAudio();
/// </code>
/// </para>
/// <para>
/// Nothing in this assembly runs unless this method is called, and the platform half is named only
/// here, so an application that leaves it out never reaches CoreAudio through the feature and the
/// trimmer removes it, which is what keeps the feature at rung 3 of `FEATURE-COST-POLICY.md`.
/// </para>
/// </remarks>
public static class AudioServiceCollectionExtensions
{
    /// <summary>Registers the audio engine over CoreAudio.</summary>
    /// <param name="services">The collection being built.</param>
    /// <returns>The same collection, so calls can be chained.</returns>
    public static IServiceCollection AddAudio(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.TryAddSingleton<IPlatformAudio, CoreAudioPlatform>();
        services.AddAudioEngine();

        return services;
    }
}
