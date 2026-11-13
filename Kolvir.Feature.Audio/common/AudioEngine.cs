namespace Kolvir.Audio;

/// <summary>
/// The application's audio engine: the devices it can play on, the diagnostic for what it bound to,
/// and the loader for clips.
/// </summary>
/// <remarks>
/// <para>
/// Reached through the application - <c>app.Services.GetRequiredService&lt;IAudioEngine&gt;()</c> -
/// after the feature's <c>AddAudio()</c>. One per application, because a device and the stream it
/// carries belong to a process rather than to a window.
/// </para>
/// <para>
/// <b><see cref="Devices"/> is never empty and <see cref="DefaultDevice"/> is never null.</b> Both are
/// kept by the null device described in <see cref="AudioDeviceInfo"/>, so an application that plays a
/// sound on a machine with no sound card has no special case to write and none to forget.
/// </para>
/// </remarks>
public interface IAudioEngine
{
    /// <summary>
    /// Every output the application can be heard on, with the null device last.
    /// </summary>
    /// <remarks>
    /// A snapshot taken when the engine was created, or when <see cref="Refresh"/> last ran: devices
    /// are not watched for changes in this version, because all three platforms report hotplug
    /// through a different mechanism and none of them can be tested on a build machine.
    /// </remarks>
    IReadOnlyList<AudioDeviceInfo> Devices { get; }

    /// <summary>
    /// The device a sound goes to when no device is named: the platform's default output, or the null
    /// device when there is no real one.
    /// </summary>
    AudioDeviceInfo DefaultDevice { get; }

    /// <summary>
    /// What the feature bound to, as one line: "<c>XAudio2: Speakers (Realtek)</c>",
    /// "<c>CoreAudio: USB Audio Device</c>", or the reason it is silent.
    /// </summary>
    /// <remarks>
    /// The audio counterpart of the graphics path a GPU tier reports, and for the same reason: when a
    /// machine makes no noise, the first question is what the feature thinks it is talking to.
    /// </remarks>
    string Path { get; }

    /// <summary>
    /// Re-reads the device list, in case the user plugged something in or changed the system default.
    /// </summary>
    /// <remarks>
    /// Voices already playing keep the device they started on, because in this version a voice does
    /// not migrate: the device it was started on is part of what it is.
    /// </remarks>
    void Refresh();

    /// <summary>
    /// Decodes a WAV stream into a clip the application can play as often as it likes.
    /// </summary>
    /// <param name="stream">The stream to read, which is not disposed.</param>
    /// <returns>The decoded clip.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="stream"/> is null.</exception>
    /// <exception cref="InvalidDataException">The stream is not a WAV file, or is truncated.</exception>
    /// <exception cref="NotSupportedException">The WAV file holds a format this version cannot decode.</exception>
    /// <remarks>
    /// <b>Decoding is validation.</b> Loading throws on a stream it cannot read, on every machine,
    /// whether or not there is anything to play it through: a machine with no audio must not be a
    /// machine where bad files go unnoticed.
    /// </remarks>
    AudioClip Load(Stream stream);
}

/// <summary>
/// The engine's own surface, which the package keeps internal: an application sees
/// <see cref="IAudioEngine"/>, and only a clip and the null device's timeline see the rest.
/// </summary>
internal sealed class AudioEngine : IAudioEngine, IDisposable
{
    private readonly IPlatformAudio? _platform;
    private readonly NullTimeline _timeline;
    private readonly List<AudioDeviceInfo> _devices = [];

    private AudioDeviceInfo _nullDevice;
    private AudioDeviceInfo? _platformDefault;

    /// <summary>
    /// Creates an engine over <paramref name="platform"/>, or over nothing when the feature was opted
    /// into on a platform whose half is missing, in which case every device is the null device.
    /// </summary>
    internal AudioEngine(IPlatformAudio? platform)
    {
        _platform = platform;
        _timeline = new NullTimeline();
        _nullDevice = AudioDeviceInfo.Null(string.Empty);
        Refresh();
    }

    /// <inheritdoc/>
    public IReadOnlyList<AudioDeviceInfo> Devices => _devices;

    /// <inheritdoc/>
    public AudioDeviceInfo DefaultDevice => _platformDefault ?? _nullDevice;

    /// <inheritdoc/>
    public string Path => _platform?.Path ?? "audio (no platform)";

    /// <summary>The silent device, which every engine has exactly one of.</summary>
    internal AudioDeviceInfo NullDevice => _nullDevice;

    /// <inheritdoc/>
    public void Refresh()
    {
        var platformDevices = _platform?.Enumerate() ?? [];
        var reason = _platform?.EmptyReason ?? "no audio platform";

        _devices.Clear();
        _platformDefault = null;

        foreach (var device in platformDevices)
        {
            var info = new AudioDeviceInfo(device.Id, device.Name, device.IsDefault, isNull: false, reason: null);
            _devices.Add(info);

            if (device.IsDefault)
            {
                _platformDefault = info;
            }
        }

        // The null device is last so a device picker built straight from this list shows the real
        // outputs first and the silence last, which is also the order they are worth reading in.
        _nullDevice = AudioDeviceInfo.Null(_devices.Count == 0 ? reason : "not selected");
        _devices.Add(_nullDevice);
    }

    /// <inheritdoc/>
    public AudioClip Load(Stream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);

        return WavAudioClipReader.Read(this, stream);
    }

    /// <summary>
    /// Starts <paramref name="clip"/> on <paramref name="device"/>, resolving the default when none is
    /// named and building a voice over the platform half or over the null timeline.
    /// </summary>
    internal Voice Start(AudioClip clip, AudioDeviceInfo? device, bool loop, float volume)
    {
        var target = device ?? DefaultDevice;

        // A device that was not in the list the engine handed out is not one this engine can play on:
        // an application holding a stale AudioDeviceInfo from before a Refresh gets the default
        // rather than a stream on a device that may no longer exist.
        if (!_devices.Contains(target))
        {
            target = DefaultDevice;
        }

        var platform = target.IsNull ? null : _platform;

        if (platform is null)
        {
            return new Voice(clip, target, platformVoice: null, timeline: _timeline, loop, volume);
        }

        var platformDevice = new PlatformDevice(target.Id, target.Name, target == _platformDefault);
        var platformVoice = platform.Open(platformDevice, clip, volume, loop);

        return new Voice(clip, target, platformVoice, timeline: null, loop, volume);
    }

    /// <summary>Releases the timeline, which is the only thing an engine owns.</summary>
    public void Dispose() => _timeline.Dispose();
}
