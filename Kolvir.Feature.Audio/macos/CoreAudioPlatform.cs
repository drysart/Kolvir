using System.Runtime.InteropServices;

namespace Kolvir.Audio;

/// <summary>
/// The macOS half: output devices from the HAL, sound from <c>AudioQueue</c>.
/// </summary>
/// <remarks>
/// <para>
/// <b>AudioQueue rather than <c>AVAudioEngine</c>.</b> The engine picks its own output - the system
/// default, always - so a feature that lets an application choose a device cannot be built on it.
/// <c>AudioQueue</c> takes a device through <c>kAudioQueueProperty_CurrentDevice</c>, and it needs no
/// run loop to run, so it is a queue this half starts and stops rather than an object graph to host.
/// </para>
/// <para>
/// The cost is that the two identifiers are different things: the HAL names a device with an
/// <c>AudioDeviceID</c>, and AudioQueue wants the device's UID string. Both are read here, and only the
/// UID is ever handed to a queue.
/// </para>
/// </remarks>
internal sealed class CoreAudioPlatform : IPlatformAudio
{
    /// <inheritdoc/>
    public string Path => "CoreAudio";

    /// <inheritdoc/>
    public string EmptyReason => "no output device";

    /// <inheritdoc/>
    public IReadOnlyList<PlatformDevice> Enumerate()
    {
        var defaultId = DefaultOutput();
        var devices = new List<PlatformDevice>();

        foreach (var id in DeviceIds())
        {
            // A device with no output streams - a microphone, an aggregate with nothing downstream -
            // is not somewhere this feature can play.
            if (!HasOutputStream(id))
            {
                continue;
            }

            var uid = StringProperty(id, CoreAudioNative.PropertyDeviceUid, CoreAudioNative.ScopeGlobal);

            if (string.IsNullOrEmpty(uid))
            {
                continue;
            }

            var name = StringProperty(id, CoreAudioNative.PropertyName, CoreAudioNative.ScopeGlobal);

            devices.Add(new PlatformDevice(
                uid,
                string.IsNullOrEmpty(name) ? uid : name,
                IsDefault: id == defaultId));
        }

        // A machine with no console default - a headless Mac, a session mid-switch - still has devices,
        // and the first is a better answer than none.
        if (devices.Count > 0 && !devices.Exists(device => device.IsDefault))
        {
            devices[0] = devices[0] with { IsDefault = true };
        }

        return devices;
    }

    /// <inheritdoc/>
    public IPlatformVoice Open(PlatformDevice device, AudioClip clip, float volume, bool loop) =>
        new AudioQueueVoice(device, clip, volume, loop);

    /// <summary>Every object the HAL knows about, including the ones that are not output devices.</summary>
    private static uint[] DeviceIds()
    {
        if (!TryRead(CoreAudioNative.SystemObject, CoreAudioNative.PropertyDevices, CoreAudioNative.ScopeGlobal, out byte[] data))
        {
            return [];
        }

        var ids = new uint[data.Length / sizeof(uint)];
        Buffer.BlockCopy(data, 0, ids, 0, ids.Length * sizeof(uint));

        return ids;
    }

    /// <summary>The console default output's <c>AudioDeviceID</c>, or 0 when there is none.</summary>
    private static uint DefaultOutput()
    {
        var address = CoreAudioNative.Address(CoreAudioNative.PropertyDefaultOutput, CoreAudioNative.ScopeGlobal);
        uint size = sizeof(uint);
        var pointer = Marshal.AllocHGlobal(sizeof(uint));

        try
        {
            return CoreAudioNative.Hal.AudioObjectGetPropertyData(
                CoreAudioNative.SystemObject, ref address, 0, IntPtr.Zero, ref size, pointer) == 0
                ? (uint)Marshal.ReadInt32(pointer)
                : 0;
        }
        finally
        {
            Marshal.FreeHGlobal(pointer);
        }
    }

    /// <summary>Whether a device has at least one output stream, which is what "speaker" means here.</summary>
    private static bool HasOutputStream(uint device)
    {
        if (!TryRead(device, CoreAudioNative.PropertyStreamConfiguration, CoreAudioNative.ScopeOutput, out byte[] data))
        {
            return false;
        }

        // An AudioBufferList starts with how many buffers follow it.
        return data.Length >= sizeof(uint) && BitConverter.ToUInt32(data, 0) > 0;
    }

    /// <summary>Reads a property whose value is a <c>CFString</c>, and releases what it was given.</summary>
    private static string StringProperty(uint device, uint selector, uint scope)
    {
        var address = CoreAudioNative.Address(selector, scope);
        uint size = (uint)IntPtr.Size;
        var pointer = Marshal.AllocHGlobal(IntPtr.Size);

        try
        {
            if (CoreAudioNative.Hal.AudioObjectGetPropertyData(
                    device, ref address, 0, IntPtr.Zero, ref size, pointer) != 0)
            {
                return string.Empty;
            }

            return CoreAudioNative.TakeString(Marshal.ReadIntPtr(pointer));
        }
        finally
        {
            Marshal.FreeHGlobal(pointer);
        }
    }

    /// <summary>Reads a property into a byte array, which is how the HAL returns everything.</summary>
    private static bool TryRead(uint objectId, uint selector, uint scope, out byte[] data)
    {
        data = [];
        var address = CoreAudioNative.Address(selector, scope);

        if (CoreAudioNative.Hal.AudioObjectGetPropertyDataSize(
                objectId, ref address, 0, IntPtr.Zero, out uint size) != 0 || size == 0)
        {
            return false;
        }

        var buffer = new byte[size];
        var handle = GCHandle.Alloc(buffer, GCHandleType.Pinned);

        try
        {
            if (CoreAudioNative.Hal.AudioObjectGetPropertyData(
                    objectId, ref address, 0, IntPtr.Zero, ref size, handle.AddrOfPinnedObject()) != 0)
            {
                return false;
            }

            data = buffer;
            return true;
        }
        finally
        {
            handle.Free();
        }
    }
}

/// <summary>
/// One clip playing on one macOS device: an <c>AudioQueue</c> with the whole clip in one buffer.
/// </summary>
/// <remarks>
/// <para>
/// A queue carries its own converter, so the clip is handed over in its own format and the device's
/// rate is CoreAudio's problem. Looping is a re-enqueue: the buffer callback fires when the audio has
/// been consumed, and for a looping voice the same buffer goes back on the queue.
/// </para>
/// <para>
/// <b>The buffer is allocated by the queue and filled by us.</b> That is the whole interop hazard of
/// this half: <c>AudioQueueBuffer</c> starts with the capacity, the payload pointer and the byte count,
/// and the payload is written through the pointer rather than through the structure.
/// </para>
/// </remarks>
internal sealed class AudioQueueVoice : IPlatformVoice
{
    private readonly AudioClip _clip;
    private readonly bool _loops;
    private readonly object _gate = new();

    // Rooted for the life of the voice: the queue calls this from its own thread, and a collected
    // delegate here is a crash inside CoreAudio.
    private readonly CoreAudioNative.QueueCallback _bufferDone;

    private IntPtr _queue;
    private float _volume;
    private bool _stopped;
    private bool _disposed;

    public AudioQueueVoice(PlatformDevice device, AudioClip clip, float volume, bool loop)
    {
        Device = device;
        _clip = clip;
        _volume = volume;
        _loops = loop;
        _bufferDone = OnBufferDone;
    }

    /// <inheritdoc/>
    public event Action<bool>? Ended;

    /// <summary>The device this voice plays on.</summary>
    public PlatformDevice Device { get; }

    /// <summary>Why no queue could be opened, or null when there is one.</summary>
    public string? Failure { get; private set; }

    /// <inheritdoc/>
    public void Start()
    {
        lock (_gate)
        {
            if (_queue != IntPtr.Zero || _stopped)
            {
                return;
            }

            try
            {
                Open();
            }
            catch (Exception error)
            {
                Failure = error.Message;
            }
        }

        if (Failure is not null)
        {
            Ended?.Invoke(true);
            return;
        }

        CoreAudioNative.Queue.AudioQueueStart(_queue, IntPtr.Zero);
    }

    /// <inheritdoc/>
    public void Pause()
    {
        lock (_gate)
        {
            if (_queue != IntPtr.Zero)
            {
                CoreAudioNative.Queue.AudioQueuePause(_queue);
            }
        }
    }

    /// <inheritdoc/>
    public void Resume()
    {
        lock (_gate)
        {
            if (_queue != IntPtr.Zero)
            {
                CoreAudioNative.Queue.AudioQueueStart(_queue, IntPtr.Zero);
            }
        }
    }

    /// <inheritdoc/>
    public void Stop()
    {
        IntPtr queue;

        lock (_gate)
        {
            if (_stopped)
            {
                return;
            }

            _stopped = true;
            queue = _queue;
        }

        if (queue != IntPtr.Zero)
        {
            // Immediate, because stopping a sound should be heard now rather than after the buffer
            // that is already on the queue has played out.
            CoreAudioNative.Queue.AudioQueueStop(queue, true);
        }
    }

    /// <inheritdoc/>
    public void SetVolume(float volume)
    {
        lock (_gate)
        {
            _volume = volume;

            if (_queue != IntPtr.Zero)
            {
                CoreAudioNative.Queue.AudioQueueSetParameter(_queue, CoreAudioNative.QueueParamVolume, volume);
            }
        }
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        IntPtr queue;

        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _stopped = true;
            queue = _queue;
            _queue = IntPtr.Zero;
        }

        if (queue != IntPtr.Zero)
        {
            // Disposing the queue releases the buffers it allocated, which is why they are not freed
            // here: the queue owns them until it is gone.
            CoreAudioNative.Queue.AudioQueueDispose(queue, true);
        }
    }

    /// <summary>Builds the queue, aims it at the device and puts the clip on it.</summary>
    private void Open()
    {
        var format = new CoreAudioNative.StreamFormat
        {
            sampleRate = _clip.SampleRate,
            formatId = CoreAudioNative.FormatLinearPcm,
            formatFlags = CoreAudioNative.FormatSignedIntegerPacked,
            bytesPerPacket = (uint)(_clip.Channels * 2),
            framesPerPacket = 1,
            bytesPerFrame = (uint)(_clip.Channels * 2),
            channelsPerFrame = (uint)_clip.Channels,
            bitsPerChannel = 16,
        };

        int status = CoreAudioNative.Queue.AudioQueueNewOutput(
            ref format, _bufferDone, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, 0, out _queue);

        if (status != 0)
        {
            throw new InvalidOperationException($"AudioQueueNewOutput failed ({status}).");
        }

        Aim();

        CoreAudioNative.Queue.AudioQueueSetParameter(_queue, CoreAudioNative.QueueParamVolume, _volume);

        var payload = _clip.Samples.ToArray();
        status = CoreAudioNative.Queue.AudioQueueAllocateBuffer(_queue, (uint)Math.Max(payload.Length, 2), out IntPtr buffer);

        if (status != 0 || buffer == IntPtr.Zero)
        {
            throw new InvalidOperationException($"AudioQueueAllocateBuffer failed ({status}).");
        }

        var header = Marshal.PtrToStructure<CoreAudioNative.QueueBuffer>(buffer);
        Marshal.Copy(payload, 0, header.audioData, payload.Length);
        header.audioDataByteSize = (uint)payload.Length;
        Marshal.StructureToPtr(header, buffer, false);

        if (CoreAudioNative.Queue.AudioQueueEnqueueBuffer(_queue, buffer, 0, IntPtr.Zero) != 0)
        {
            throw new InvalidOperationException("AudioQueueEnqueueBuffer failed.");
        }
    }

    /// <summary>
    /// The clip has been played out. A looping voice puts the same buffer back on the queue, which is
    /// what looping is here; a voice that does not loop has finished.
    /// </summary>
    /// <remarks>
    /// Called on AudioToolbox's thread, which is why it reads the two flags and does not touch anything
    /// else the application could be using at the same time.
    /// </remarks>
    private void OnBufferDone(IntPtr userData, IntPtr queue, IntPtr buffer)
    {
        lock (_gate)
        {
            if (_stopped || _disposed)
            {
                return;
            }
        }

        if (_loops)
        {
            CoreAudioNative.Queue.AudioQueueEnqueueBuffer(queue, buffer, 0, IntPtr.Zero);
            return;
        }

        Ended?.Invoke(false);
    }

    /// <summary>
    /// Points the queue at the device the voice was started on.
    /// </summary>
    /// <remarks>
    /// <c>kAudioQueueProperty_CurrentDevice</c> takes a pointer <i>to</i> a <c>CFStringRef</c>, not the
    /// reference itself - handing it the reference is a segfault inside AudioToolbox rather than an
    /// error code, which is worth remembering before changing this.
    /// </remarks>
    private void Aim()
    {
        var uid = CoreAudioNative.CreateString(Device.Id);
        var holder = Marshal.AllocHGlobal(IntPtr.Size);

        try
        {
            Marshal.WriteIntPtr(holder, uid);

            if (CoreAudioNative.Queue.AudioQueueSetProperty(
                    _queue, CoreAudioNative.QueuePropertyCurrentDevice, holder, IntPtr.Size) != 0)
            {
                // Not fatal: a queue that could not be aimed plays on the system default, which is
                // worse than silence only when the user chose a device, and better than no sound.
            }
        }
        finally
        {
            Marshal.FreeHGlobal(holder);
            CoreAudioNative.CoreFoundation.CFRelease(uid);
        }
    }
}
