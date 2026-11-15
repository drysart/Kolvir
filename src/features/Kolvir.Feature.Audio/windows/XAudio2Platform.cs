using System.Runtime.InteropServices;
using Vortice.Multimedia;
using Vortice.XAudio2;

namespace Kolvir.Audio;

/// <summary>
/// The Windows half: endpoints from MMDevice, voices from XAudio2.
/// </summary>
/// <remarks>
/// <para>
/// XAudio2 does the work this feature would otherwise have to write - it mixes the voices, resamples a
/// clip to the device's rate and loops a buffer natively - so a voice here is a source voice and
/// nothing else. That is what keeps the package a thin shim on this platform, and it is why the
/// alternative of talking to WASAPI directly was dropped: WASAPI has no voice concept, no loop and no
/// volume.
/// </para>
/// <para>
/// <b>XAudio2 is probed before it is used.</b> It ships with Windows, so in practice this is a check
/// that passes on every machine that has a version of Windows this feature supports - but the wrapper
/// throws from a static constructor when the DLL is missing, and a type initialiser exploding is not
/// the failure mode a machine without audio should produce. The probe is the same one the Linux half
/// makes of <c>libpulse</c>.
/// </para>
/// </remarks>
internal sealed class XAudio2Platform : IPlatformAudio
{
    private static readonly bool Available = NativeLibrary.TryLoad("xaudio2_9.dll", out _);

    /// <inheritdoc/>
    public string Path => Available ? "XAudio2" : "audio (XAudio2 is not installed)";

    /// <inheritdoc/>
    public string EmptyReason => Available ? "no output device" : "XAudio2 is not installed";

    /// <inheritdoc/>
    public IReadOnlyList<PlatformDevice> Enumerate() => Available ? MmDevice.Enumerate() : [];

    /// <inheritdoc/>
    public IPlatformVoice Open(PlatformDevice device, AudioClip clip, float volume, bool loop) =>
        new XAudio2Voice(device, clip, volume, loop);
}

/// <summary>
/// One clip playing on one Windows endpoint: an XAudio2 engine, a mastering voice for the device and a
/// source voice carrying the clip.
/// </summary>
/// <remarks>
/// <para>
/// <b>One engine per voice, on purpose.</b> XAudio2 sends a source voice to the mastering voice that
/// was created first unless it is told otherwise, so several devices in one engine means a send list
/// per voice and a mastering voice per device and sample rate. An engine per voice is a few kilobytes
/// and one audio thread, a UI sound is one of a handful playing at once, and the routing question
/// disappears: this voice's mastering voice is on this voice's device.
/// </para>
/// <para>
/// The clip is submitted as one buffer with <c>EndOfStream</c> set and <c>LoopCount</c> when the voice
/// loops, so looping and the end of the sound are XAudio2's to report rather than ours to count. The
/// buffer is owned by the voice until it is disposed, because XAudio2 reads from it on its own thread.
/// </para>
/// </remarks>
internal sealed class XAudio2Voice : IPlatformVoice
{
    /// <summary><c>XAUDIO2_LOOP_INFINITE</c>: loop until the voice is stopped.</summary>
    private const uint LoopInfinite = 255;

    private readonly AudioClip _clip;
    private readonly bool _loops;
    private readonly object _gate = new();

    private IXAudio2? _engine;
    private IXAudio2MasteringVoice? _mastering;
    private IXAudio2SourceVoice? _voice;
    private AudioBuffer? _buffer;
    private float _volume;
    private bool _stopped;

    public XAudio2Voice(PlatformDevice device, AudioClip clip, float volume, bool loop)
    {
        Device = device;
        _clip = clip;
        _volume = volume;
        _loops = loop;
    }

    /// <inheritdoc/>
    public event Action<bool>? Ended;

    /// <summary>The device this voice plays on.</summary>
    public PlatformDevice Device { get; }

    /// <summary>Why no voice could be opened, or null when there is one.</summary>
    public string? Failure { get; private set; }

    /// <inheritdoc/>
    public void Start()
    {
        lock (_gate)
        {
            if (_voice is not null || _stopped)
            {
                return;
            }

            try
            {
                Open();
            }
            catch (Exception error)
            {
                // A device that vanished between enumeration and playback is not an application's bug:
                // it is reported as a lost device, which is what it is.
                Failure = error.Message;
                Ended?.Invoke(true);
                return;
            }
        }

        // Started outside the lock, because XAudio2 calls back on its own thread and a callback that
        // wanted the lock while it was held would deadlock on the first block.
        _voice?.Start();
    }

    /// <inheritdoc/>
    public void Pause()
    {
        lock (_gate)
        {
            _voice?.Stop();
        }
    }

    /// <inheritdoc/>
    public void Resume()
    {
        lock (_gate)
        {
            // Stopping a source voice keeps its queued buffer, so starting it again continues from
            // where it stopped rather than from the beginning of the clip.
            _voice?.Start();
        }
    }

    /// <inheritdoc/>
    public void Stop()
    {
        IXAudio2SourceVoice? voice;

        lock (_gate)
        {
            if (_stopped)
            {
                return;
            }

            _stopped = true;
            voice = _voice;
        }

        voice?.Stop();
    }

    /// <inheritdoc/>
    public void SetVolume(float volume)
    {
        lock (_gate)
        {
            _volume = volume;

            if (_voice is not null)
            {
                _voice.Volume = volume;
            }
        }
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        IXAudio2SourceVoice? voice;
        IXAudio2MasteringVoice? mastering;
        IXAudio2? engine;
        AudioBuffer? buffer;

        lock (_gate)
        {
            _stopped = true;
            voice = _voice;
            _voice = null;
            mastering = _mastering;
            _mastering = null;
            engine = _engine;
            _engine = null;
            buffer = _buffer;
            _buffer = null;
        }

        if (voice is not null)
        {
            voice.BufferEnd -= OnBufferEnd;
            voice.StreamEnd -= OnStreamEnd;
            voice.VoiceError -= OnVoiceError;
            voice.Stop();
            voice.Dispose();
        }

        // The buffer outlives the voice's last read, and only then is it safe to free.
        buffer?.Dispose();
        mastering?.Dispose();
        engine?.Dispose();
    }

    /// <summary>Builds the engine, the mastering voice for this device and the source voice for this clip.</summary>
    private void Open()
    {
        _engine = XAudio2.XAudio2Create(registerCallback: false);

        // The mastering voice is created at the clip's own format, so the source voice and its output
        // agree on a rate and XAudio2 only has to resample once, to the device.
        _mastering = _engine.CreateMasteringVoice(
            (uint)_clip.Channels,
            (uint)_clip.SampleRate,
            deviceId: Device.Id);

        _engine.StartEngine();

        var format = new WaveFormat(_clip.SampleRate, 16, _clip.Channels);
        _voice = _engine.CreateSourceVoice(format, enableCallbackEvents: true);

        _voice.Volume = _volume;
        _voice.BufferEnd += OnBufferEnd;
        _voice.StreamEnd += OnStreamEnd;
        _voice.VoiceError += OnVoiceError;

        // One buffer for the whole clip, owned until this voice is disposed: XAudio2 reads it from its
        // own thread, and handing it a managed array it might later collect is how a sound becomes a
        // click or a crash.
        _buffer = new AudioBuffer(_clip.Samples.ToArray())
        {
            LoopCount = _loops ? LoopInfinite : 0,
        };

        _voice.SubmitSourceBuffer(_buffer);
    }

    /// <summary>Every submitted buffer has played, which is the end of a clip that does not loop.</summary>
    private void OnBufferEnd(IntPtr context) => Ended?.Invoke(false);

    /// <summary>XAudio2 reports a whole stream, which arrives after the buffer that ended it.</summary>
    private void OnStreamEnd() => Ended?.Invoke(false);

    /// <summary>The device went away, or the engine failed: either way the sound has stopped.</summary>
    private void OnVoiceError(IXAudio2SourceVoice.VoiceErrorArgs args) => Ended?.Invoke(true);
}
