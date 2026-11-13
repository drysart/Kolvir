namespace Kolvir.Audio;

/// <summary>
/// One way an application can be heard: an output device, or the silent device that stands in when
/// there is nothing to play through.
/// </summary>
/// <remarks>
/// <para>
/// These are values, not handles. A device is identified by <see cref="Id"/> - the platform's own
/// identifier, which is what <see cref="AudioClip.Start"/> remembers when a voice is started on it -
/// and the object itself carries nothing an application has to dispose.
/// </para>
/// <para>
/// <b><see cref="IsNull"/> is the interesting one.</b> Every engine has one null device, always in
/// <see cref="IAudioEngine.Devices"/> and the default when no real device exists, so no call site
/// needs a null check. It is a device rather than an absence: a voice started on it advances a
/// timeline and reports when it would have ended, and it never produces a sample.
/// <see cref="Reason"/> says why it is in use, because silence that cannot be explained is the
/// failure mode this pattern risks.
/// </para>
/// </remarks>
public sealed class AudioDeviceInfo
{
    internal AudioDeviceInfo(string id, string name, bool isDefault, bool isNull, string? reason)
    {
        Id = id;
        Name = name;
        IsDefault = isDefault;
        IsNull = isNull;
        Reason = reason;
    }

    /// <summary>
    /// The platform's identifier for this device, which is what a voice is started on.
    /// </summary>
    /// <remarks>
    /// Opaque, and different on every platform: an MMDevice id string on Windows, an audio device
    /// UID on macOS, a PulseAudio sink name on Linux. The null device's id names nothing, which is
    /// why <see cref="IsNull"/> exists rather than applications comparing ids.
    /// </remarks>
    public string Id { get; }

    /// <summary>
    /// What to show a user: the device's name as the platform reports it.
    /// </summary>
    /// <remarks>
    /// "<c>No audio device</c>" for the null device. Applications that show a device picker should
    /// show this and, for the null device, <see cref="Reason"/> beside it.
    /// </remarks>
    public string Name { get; }

    /// <summary>
    /// Whether this is the device a voice started without one goes to, which is the platform's
    /// default output - or the null device when there is no real one.
    /// </summary>
    /// <remarks>
    /// Resolved when the engine read its device list, so it is a snapshot rather than a live query:
    /// when the user changes the system default, <see cref="IAudioEngine.Refresh"/> is what notices.
    /// </remarks>
    public bool IsDefault { get; }

    /// <summary>
    /// Whether this is the silent device that stands in when nothing can be played through.
    /// </summary>
    public bool IsNull { get; }

    /// <summary>
    /// Why the null device is in use - "<c>no output device</c>", "<c>no sound server</c>",
    /// "<c>libpulse is not installed</c>" - or null for a real device.
    /// </summary>
    /// <remarks>
    /// Diagnostic text for an application to show or log, not something to branch on, and not
    /// localised for that reason. <see cref="IAudioEngine.Path"/> is the other half of the same
    /// story: this says why there is no device, that says what the feature bound to.
    /// </remarks>
    public string? Reason { get; }

    /// <inheritdoc/>
    public override string ToString() => Name;

    /// <summary>
    /// The silent device, created once per engine with the reason it is in use.
    /// </summary>
    /// <param name="reason">Why there is no real device to play on.</param>
    internal static AudioDeviceInfo Null(string reason) =>
        new(id: string.Empty, name: "No audio device", isDefault: false, isNull: true, reason);
}
