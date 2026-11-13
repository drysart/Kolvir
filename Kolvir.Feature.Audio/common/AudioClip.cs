namespace Kolvir.Audio;

/// <summary>
/// One decoded sound: a duration, a channel count and a sample rate, and the ability to be played.
/// </summary>
/// <remarks>
/// <para>
/// The audio counterpart of <see cref="ImageData"/>, and it keeps the same two rules. A clip is
/// immutable once it is loaded, so one clip can play on several voices at once without copying or
/// locking. And a clip belongs to the application that loaded it: disposing one while a voice is
/// still playing it is the application's mistake, in the same way that disposing an image under a
/// control that is drawing it is.
/// </para>
/// <para>
/// <b>Everything is 16-bit signed PCM, interleaved.</b> The WAV reader converts 8-, 24- and 32-bit
/// integer samples and 32-bit float samples to that one format as it loads, so the platform halves
/// have one format to open a stream with and no conversion to agree on. It is also what a UI sound
/// needs: a beep does not benefit from more resolution, and 16-bit is the one format every platform
/// accepts without a resampler in the path.
/// </para>
/// <para>
/// Playing is fire and forget - <see cref="Play"/> keeps no handle - while <see cref="Start"/>
/// returns the <see cref="Voice"/> that keeps the position, the volume and the finish notification.
/// </para>
/// </remarks>
public sealed class AudioClip
{
    private readonly AudioEngine _engine;
    private readonly byte[] _samples;

    internal AudioClip(AudioEngine engine, byte[] samples, int sampleRate, int channels)
    {
        _engine = engine;
        _samples = samples;
        SampleRate = sampleRate;
        Channels = channels;
    }

    /// <summary>How long the clip lasts when played once.</summary>
    public TimeSpan Duration =>
        SampleRate <= 0
            ? TimeSpan.Zero
            : TimeSpan.FromSeconds((double)Frames / SampleRate);

    /// <summary>The sample rate the clip was loaded at, in Hz.</summary>
    public int SampleRate { get; }

    /// <summary>How many channels the clip has: 1 for mono, 2 for stereo, more for a surround file.</summary>
    public int Channels { get; }

    /// <summary>
    /// The decoded audio: 16-bit signed samples, interleaved by channel, little-endian.
    /// </summary>
    internal ReadOnlyMemory<byte> Samples => _samples;

    /// <summary>How many sample frames the clip holds, which is one sample per channel.</summary>
    internal int Frames => Channels <= 0 ? 0 : _samples.Length / (Channels * 2);

    /// <summary>
    /// Plays the clip once and keeps no handle on it, which is what a UI sound usually wants.
    /// </summary>
    /// <param name="device">The device to play on, or null for <see cref="IAudioEngine.DefaultDevice"/>.</param>
    /// <param name="volume">The volume, between 0 and 1. Values outside that range are clamped.</param>
    /// <remarks>
    /// The voice the engine creates is not referenced again, so the clip plays to its end and is
    /// cleaned up. An application that wants to stop it, read its position or hear when it finished
    /// uses <see cref="Start"/>.
    /// </remarks>
    public void Play(AudioDeviceInfo? device = null, float volume = 1f) =>
        Start(device, loop: false, volume).Detach();

    /// <summary>
    /// Starts the clip and returns the voice playing it, which is also the first sound of that clip
    /// on that device - a clip may be started on several voices at once.
    /// </summary>
    /// <param name="device">The device to play on, or null for <see cref="IAudioEngine.DefaultDevice"/>.</param>
    /// <param name="loop">Whether to repeat the clip until the voice is stopped.</param>
    /// <param name="volume">The volume, between 0 and 1. Values outside that range are clamped.</param>
    /// <returns>
    /// The voice, which the caller disposes to release the stream. Disposing a voice that is still
    /// playing stops it.
    /// </returns>
    /// <remarks>
    /// A clip of no length is a voice that finishes immediately rather than a free loop: a truncated
    /// or empty file is a mistake worth seeing in the timing.
    /// </remarks>
    public Voice Start(AudioDeviceInfo? device = null, bool loop = false, float volume = 1f) =>
        _engine.Start(this, device, loop, volume);

    /// <inheritdoc/>
    public override string ToString() =>
        $"{Duration.TotalSeconds:0.###}s, {Channels} channel{(Channels == 1 ? string.Empty : "s")}, {SampleRate} Hz";
}
