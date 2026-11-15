namespace Kolvir.Audio;

/// <summary>
/// One output device as the platform half sees it, before the engine decides what is the default and
/// adds the null device.
/// </summary>
/// <param name="Id">The platform's identifier, which is what <see cref="IPlatformAudio.Open"/> takes.</param>
/// <param name="Name">The name the platform reports.</param>
/// <param name="IsDefault">Whether the platform reports this as its default output.</param>
internal sealed record PlatformDevice(string Id, string Name, bool IsDefault);

/// <summary>
/// The platform half of the feature: enumerate the outputs, and play one clip on one of them.
/// </summary>
/// <remarks>
/// <para>
/// Written once and implemented three times, which is the whole reason the layout is
/// `common/` plus three folders. Everything above it - the devices list, the null device, the voice
/// timeline, looping, volume, WAV decoding - is ordinary code in <c>common/</c> and knows nothing
/// about a platform.
/// </para>
/// <para>
/// <b>One voice is one stream.</b> All three platforms mix streams and resample them for us - XAudio2
/// on Windows, the device itself on macOS, the server on Linux - so a voice opens its own stream and
/// the mixing is not this package's problem. That is also why a platform can implement looping
/// natively: none of them has to share a stream with another voice.
/// </para>
/// <para>
/// <b>Nothing here throws for the absence of audio.</b> A platform that cannot play reports it through
/// <see cref="EmptyReason"/> and an empty list, and the engine turns that into the null device. An
/// exception out of this interface is a bug, not a machine without speakers.
/// </para>
/// </remarks>
internal interface IPlatformAudio
{
    /// <summary>
    /// The one-line diagnostic for <see cref="IAudioEngine.Path"/>: what this half bound to.
    /// </summary>
    /// <remarks>
    /// "<c>XAudio2</c>", "<c>CoreAudio</c>", "<c>PulseAudio</c>", or the reason there is nothing for
    /// the same shape as <c>GraphicsPath</c>.
    /// </remarks>
    string Path { get; }

    /// <summary>
    /// Why <see cref="Enumerate"/> returned nothing, or null when it returned something.
    /// </summary>
    /// <remarks>
    /// The string the null device carries as <see cref="AudioDeviceInfo.Reason"/>. It has to be
    /// specific - the missing library, the stopped server and the unplugged device are three
    /// different situations and an application can only report what it is told.
    /// </remarks>
    string EmptyReason { get; }

    /// <summary>
    /// The output devices the platform has right now, in the platform's own order.
    /// </summary>
    IReadOnlyList<PlatformDevice> Enumerate();

    /// <summary>
    /// Opens a stream for one clip on one device. The voice is not started until
    /// <see cref="IPlatformVoice.Start"/>.
    /// </summary>
    /// <param name="device">The device the voice was started on.</param>
    /// <param name="clip">The decoded audio: 16-bit signed PCM, interleaved, at the clip's rate.</param>
    /// <param name="volume">The initial volume, between 0 and 1.</param>
    /// <param name="loop">Whether the platform should loop the clip natively.</param>
    IPlatformVoice Open(PlatformDevice device, AudioClip clip, float volume, bool loop);
}

/// <summary>
/// One clip playing on one device: what the engine's <see cref="Voice"/> drives.
/// </summary>
/// <remarks>
/// The mirror of <see cref="IPlatformAudio"/> at the other end of a voice's life. Implementations
/// receive their samples once, at construction, and are expected to copy what they need rather than
/// hold a reference to the clip: a clip may be disposed while its voices are still running, which is
/// the application's mistake and not something the platform half has to survive.
/// </remarks>
internal interface IPlatformVoice : IDisposable
{
    /// <summary>
    /// Raised once, on a platform thread, when the voice stops making sound by itself, or when the
    /// device it was on disappeared.
    /// </summary>
    /// <remarks>
    /// The argument is true when the device went away rather than the clip reaching its end. It is
    /// raised from the audio thread, so the engine posts it to the UI thread before any application
    /// code sees it, which is the rule <see cref="Timer"/> follows.
    /// </remarks>
    event Action<bool>? Ended;

    /// <summary>Starts consuming the clip. Called once, after the volume is set.</summary>
    void Start();

    /// <summary>Stops consuming the clip without losing the position in it.</summary>
    void Pause();

    /// <summary>Resumes a paused voice from where <see cref="Pause"/> stopped it.</summary>
    void Resume();

    /// <summary>Stops consuming the clip for good. Called before <see cref="IDisposable.Dispose"/>.</summary>
    void Stop();

    /// <summary>Changes the volume of a running voice, between 0 and 1.</summary>
    void SetVolume(float volume);
}
