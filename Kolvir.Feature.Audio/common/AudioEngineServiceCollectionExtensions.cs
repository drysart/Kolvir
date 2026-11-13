using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Kolvir.Audio;

/// <summary>
/// The one registration every platform half shares: the engine itself, over whichever platform half
/// that half registered.
/// </summary>
/// <remarks>
/// <para>
/// The platform halves each expose a public <c>AddAudio()</c> - same name, same signature, on every
/// platform - which registers its own <see cref="IPlatformAudio"/> and then calls this. Splitting it
/// this way keeps the engine in <c>common/</c>, where it belongs, without a second public entry point
/// an application could call by mistake.
/// </para>
/// <para>
/// The platform half is optional on purpose. A machine whose library is missing, a test that wants no
/// audio at all, and a platform whose half does not exist yet all end up with an engine over no
/// platform, which reports the null device and plays silence on a real timeline. Nothing throws, and
/// nothing has to be special-cased at the call site.
/// </para>
/// </remarks>
internal static class AudioEngineServiceCollectionExtensions
{
    /// <summary>Registers <see cref="IAudioEngine"/> over the registered platform half, if any.</summary>
    /// <param name="services">The collection being built.</param>
    /// <returns>The same collection, so calls can be chained.</returns>
    internal static IServiceCollection AddAudioEngine(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        // One engine per application, because a device and the stream it carries belong to a process
        // rather than to a window, and because the null device's timeline is one timer.
        services.TryAddSingleton(static provider =>
            new AudioEngine(provider.GetService<IPlatformAudio>()) as IAudioEngine);

        return services;
    }
}
