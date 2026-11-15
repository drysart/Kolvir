namespace Kolvir.Audio.Tests;

/// <summary>
/// A platform half that records what it was asked to do: the device list it was given, and every
/// voice that was opened on it.
/// </summary>
/// <remarks>
/// What <see cref="AudioEngine"/> does above the seam is the whole of `common/`, so the interesting
/// assertions are about the calls it makes - which device, what volume, whether it looped - rather
/// than about sound. This fake is the seam's other side, and the platform halves are written to the
/// same contract.
/// </remarks>
internal sealed class RecordingPlatform : IPlatformAudio
{
    /// <summary>The devices this platform claims to have.</summary>
    public List<PlatformDevice> Devices { get; } = [];

    /// <summary>Every voice opened on this platform, in the order it was opened.</summary>
    public List<RecordingVoice> Voices { get; } = [];

    /// <inheritdoc/>
    public string Path => "test audio";

    /// <inheritdoc/>
    public string EmptyReason { get; set; } = "no output device";

    /// <inheritdoc/>
    public IReadOnlyList<PlatformDevice> Enumerate() => Devices;

    /// <inheritdoc/>
    public IPlatformVoice Open(PlatformDevice device, AudioClip clip, float volume, bool loop)
    {
        var voice = new RecordingVoice(device, clip, volume, loop);
        Voices.Add(voice);
        return voice;
    }
}

/// <summary>One voice as the platform half saw it: started, paused, resumed, stopped, or ended.</summary>
internal sealed class RecordingVoice : IPlatformVoice
{
    private int _disposed;

    public RecordingVoice(PlatformDevice device, AudioClip clip, float volume, bool loop)
    {
        Device = device;
        Clip = clip;
        Volume = volume;
        Loops = loop;
    }

    /// <inheritdoc/>
    public event Action<bool>? Ended;

    /// <summary>The device this voice was opened on.</summary>
    public PlatformDevice Device { get; }

    /// <summary>The clip it was opened with.</summary>
    public AudioClip Clip { get; }

    /// <summary>The volume the engine set before starting, and every later change.</summary>
    public float Volume { get; private set; }

    /// <summary>Whether the engine asked for native looping.</summary>
    public bool Loops { get; }

    /// <summary>How many times each lifecycle call arrived.</summary>
    public int Starts { get; private set; }

    public int Pauses { get; private set; }

    public int Resumes { get; private set; }

    public int Stops { get; private set; }

    /// <summary>True once the engine disposed it, which is when the stream is released.</summary>
    public bool IsDisposed => Volatile.Read(ref _disposed) != 0;

    /// <inheritdoc/>
    public void Start() => Starts++;

    /// <inheritdoc/>
    public void Pause() => Pauses++;

    /// <inheritdoc/>
    public void Resume() => Resumes++;

    /// <inheritdoc/>
    public void Stop() => Stops++;

    /// <inheritdoc/>
    public void SetVolume(float volume) => Volume = volume;

    /// <summary>Reports the clip reaching its end, as the platform half does from its own thread.</summary>
    public void ReportEnded(bool lost = false) => Ended?.Invoke(lost);

    /// <inheritdoc/>
    public void Dispose() => Interlocked.Exchange(ref _disposed, 1);
}
