using System.Runtime.InteropServices;

namespace Kolvir.Audio;

/// <summary>
/// The Linux half: sinks through PulseAudio, one <c>pa_simple</c> stream per voice.
/// </summary>
/// <remarks>
/// <para>
/// PulseAudio is the lowest common denominator on a Linux desktop: GNOME and KDE both run it, and
/// PipeWire's <c>pipewire-pulse</c> speaks the same protocol, which is why one implementation covers
/// both. When the library or the server is missing - a container, a bare VM, a CI runner - this half
/// says so and the engine falls back to the silent device.
/// </para>
/// <para>
/// Nothing here uses the asynchronous API for playback. Enumeration needs it because the simple API
/// cannot list anything, and a single-threaded pump over a main loop is enough for a question asked
/// once. A voice needs no such thing: <c>pa_simple_write</c> blocks until the server wants more, which
/// is precisely the pacing a stream of audio wants, so each voice is one thread.
/// </para>
/// </remarks>
internal sealed class PulseAudioPlatform : IPlatformAudio
{
    private readonly object _gate = new();

    // Rooted for the life of the process: libpulse keeps this pointer for as long as an operation runs.
    private readonly PulseNative.SinkInfoCallback _sinkInfo;

    private readonly List<(string Name, string Description)> _sinks = [];
    private string? _defaultSink;

    public PulseAudioPlatform() => _sinkInfo = OnSinkInfo;

    /// <inheritdoc/>
    public string Path => PulseNative.IsAvailable ? "PulseAudio" : "audio (libpulse is not installed)";

    /// <inheritdoc/>
    public string EmptyReason => PulseNative.IsAvailable ? "no sound server" : "libpulse is not installed";

    /// <inheritdoc/>
    public IReadOnlyList<PlatformDevice> Enumerate()
    {
        if (!PulseNative.IsAvailable)
        {
            return [];
        }

        lock (_gate)
        {
            _sinks.Clear();
            _defaultSink = null;
        }

        var mainloop = PulseNative.Context.pa_mainloop_new();

        if (mainloop == IntPtr.Zero)
        {
            return [];
        }

        var context = IntPtr.Zero;

        try
        {
            context = PulseNative.Context.pa_context_new(
                PulseNative.Context.pa_mainloop_get_api(mainloop), "kolvir-audio");

            if (context == IntPtr.Zero ||
                PulseNative.Context.pa_context_connect(context, null, 0, IntPtr.Zero) != 0)
            {
                return [];
            }

            if (!PumpUntil(mainloop, () => PulseNative.Context.pa_context_get_state(context) == PulseNative.ContextReady))
            {
                return [];
            }

            Pump(mainloop, PulseNative.Context.pa_context_get_sink_info_list(context, _sinkInfo, IntPtr.Zero));

            // The default sink is asked for by the name the server understands rather than read out of
            // pa_server_info, whose layout differs between PulseAudio and pipewire-pulse in practice.
            Pump(
                mainloop,
                PulseNative.Context.pa_context_get_sink_info_by_name(context, "@DEFAULT_SINK@", _sinkInfo, IntPtr.Zero));

            lock (_gate)
            {
                var devices = new List<PlatformDevice>(_sinks.Count);

                for (int i = 0; i < _sinks.Count; i++)
                {
                    var sink = _sinks[i];
                    devices.Add(new PlatformDevice(
                        sink.Name,
                        string.IsNullOrWhiteSpace(sink.Description) ? sink.Name : sink.Description,
                        IsDefault: sink.Name == _defaultSink));
                }

                // A server that answers with no sink at all leaves every device non-default, which
                // would strand an application with no default; the first sink is the reasonable answer.
                if (devices.Count > 0 && !devices.Exists(device => device.IsDefault))
                {
                    devices[0] = devices[0] with { IsDefault = true };
                }

                return devices;
            }
        }
        finally
        {
            if (context != IntPtr.Zero)
            {
                PulseNative.Context.pa_context_disconnect(context);
                PulseNative.Context.pa_context_unref(context);
            }

            PulseNative.Context.pa_mainloop_free(mainloop);
        }
    }

    /// <inheritdoc/>
    public IPlatformVoice Open(PlatformDevice device, AudioClip clip, float volume, bool loop) =>
        new PulseVoice(device, clip, volume, loop);

    /// <summary>Turns one callback into a sink, or notes the end of the list.</summary>
    private void OnSinkInfo(IntPtr context, IntPtr info, int eol, IntPtr userData)
    {
        if (eol != 0 || info == IntPtr.Zero)
        {
            return;
        }

        var sink = Marshal.PtrToStructure<PulseNative.SinkInfo>(info);
        var name = PulseNative.String(sink.name);

        if (string.IsNullOrEmpty(name))
        {
            return;
        }

        lock (_gate)
        {
            // The by-name query is the one that carries the default; it reports the sink under a real
            // name, so it is added once and remembered rather than added twice.
            if (_sinks.Exists(entry => entry.Name == name))
            {
                _defaultSink = name;
                return;
            }

            _sinks.Add((name, PulseNative.String(sink.description)));
        }
    }

    /// <summary>Runs the main loop until <paramref name="done"/>, which is how a context reaches READY.</summary>
    private static bool PumpUntil(IntPtr mainloop, Func<bool> done)
    {
        for (int i = 0; i < 2000; i++)
        {
            if (done())
            {
                return true;
            }

            if (PulseNative.Context.pa_mainloop_iterate(mainloop, 1, out _) < 0)
            {
                return false;
            }
        }

        return done();
    }

    /// <summary>Runs the main loop until one operation finished, then releases it.</summary>
    private static void Pump(IntPtr mainloop, IntPtr operation)
    {
        if (operation == IntPtr.Zero)
        {
            return;
        }

        try
        {
            for (int i = 0;
                 i < 4000 && PulseNative.Context.pa_operation_get_state(operation) == PulseNative.OperationRunning;
                 i++)
            {
                if (PulseNative.Context.pa_mainloop_iterate(mainloop, 1, out _) < 0)
                {
                    return;
                }
            }
        }
        finally
        {
            PulseNative.Context.pa_operation_unref(operation);
        }
    }
}

/// <summary>
/// One clip playing on one sink: a thread that writes blocks and lets the server pace it.
/// </summary>
/// <remarks>
/// <para>
/// <c>pa_simple_write</c> blocks until the server has room, so the thread needs no timer and the audio
/// is paced by the sink rather than by a sleep - which is what makes a long clip stay in step on a
/// loaded machine. The cost is a thread per voice, which is the trade every platform in this feature
/// makes: a UI sound is short, and there are never many at once.
/// </para>
/// <para>
/// <b>Volume is applied here</b>, because the simple API has none: each block is scaled as it is copied
/// out of the clip, so a volume change is heard at the next block boundary rather than needing the
/// stream to be reopened.
/// </para>
/// </remarks>
internal sealed class PulseVoice : IPlatformVoice
{
    /// <summary>How much audio one write carries: 20 ms, the usual granularity for a UI sound.</summary>
    private static readonly TimeSpan BlockDuration = TimeSpan.FromMilliseconds(20);

    private readonly AudioClip _clip;
    private readonly bool _loops;
    private readonly ManualResetEventSlim _resumed = new(true);
    private readonly Thread _thread;
    private readonly object _gate = new();

    private float _volume;
    private bool _started;
    private bool _stopped;
    private bool _disposed;
    private IntPtr _handle;

    public PulseVoice(PlatformDevice device, AudioClip clip, float volume, bool loop)
    {
        Device = device;
        _clip = clip;
        _volume = volume;
        _loops = loop;

        var spec = new PulseNative.SampleSpec
        {
            format = PulseNative.SampleS16Le,
            rate = (uint)clip.SampleRate,
            channels = (byte)Math.Clamp(clip.Channels, 1, 32),
        };

        // The stream is aimed at the sink by name, which is the one thing the simple API can still do
        // that the asynchronous one would otherwise be needed for.
        _handle = PulseNative.Simple.pa_simple_new(
            server: null,
            name: "kolvir-audio",
            direction: PulseNative.StreamPlayback,
            device: device.Id,
            streamName: "Kolvir audio",
            ref spec,
            channelMap: IntPtr.Zero,
            bufferAttr: IntPtr.Zero,
            out int error);

        if (_handle == IntPtr.Zero)
        {
            // No stream is a voice that is already over, reported as a lost device rather than thrown:
            // a sink that went away between enumeration and playback is not an application's bug.
            Failure = $"pa_simple_new failed ({error})";
        }

        _thread = new Thread(Write) { IsBackground = true, Name = "Kolvir audio" };
    }

    /// <inheritdoc/>
    public event Action<bool>? Ended;

    /// <summary>The device this voice was opened on.</summary>
    public PlatformDevice Device { get; }

    /// <summary>Why no stream could be opened, or null when there is one.</summary>
    public string? Failure { get; }

    /// <inheritdoc/>
    public void Start()
    {
        lock (_gate)
        {
            if (_started || _disposed)
            {
                return;
            }

            _started = true;
        }

        if (_handle == IntPtr.Zero)
        {
            Ended?.Invoke(true);
            return;
        }

        _thread.Start();
    }

    /// <inheritdoc/>
    public void Pause() => _resumed.Reset();

    /// <inheritdoc/>
    public void Resume() => _resumed.Set();

    /// <inheritdoc/>
    public void Stop()
    {
        lock (_gate)
        {
            if (_stopped)
            {
                return;
            }

            _stopped = true;
        }

        // Nothing here touches the stream. `pa_simple` is not thread-safe and the writer is very
        // likely inside a write, so stopping is a flag the writer sees at its next block boundary -
        // and that boundary is only 20 ms away.
        _resumed.Set();
    }

    /// <inheritdoc/>
    public void SetVolume(float volume) => Volatile.Write(ref _volume, volume);

    /// <inheritdoc/>
    public void Dispose()
    {
        bool started;

        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _stopped = true;
            started = _started;
        }

        _resumed.Set();

        if (started)
        {
            // The writer owns the stream: it is the one blocked in a write, so it is the one that frees
            // the handle, on its way out. Waiting for it is what makes the free safe.
            _thread.Join(TimeSpan.FromSeconds(2));
        }
        else
        {
            // Never started, so nothing is writing and there is nobody else to free it.
            ReleaseHandle();
        }
    }

    /// <summary>The writing thread: copy a block, scale it, hand it over, repeat.</summary>
    private void Write()
    {
        try
        {
            var samples = _clip.Samples.Span;
            int block = Math.Max(
                2,
                (int)(_clip.SampleRate * Math.Clamp(_clip.Channels, 1, 32) * 2 * BlockDuration.TotalSeconds) & ~1);

            var buffer = new byte[Math.Min(block, Math.Max(samples.Length, 2))];
            int offset = 0;

            while (true)
            {
                _resumed.Wait();

                lock (_gate)
                {
                    if (_stopped || _disposed)
                    {
                        return;
                    }
                }

                if (samples.Length == 0)
                {
                    Ended?.Invoke(false);
                    return;
                }

                int count = Math.Min(buffer.Length, samples.Length - offset);
                Scale(samples.Slice(offset, count), buffer.AsSpan(0, count), Volatile.Read(ref _volume));

                if (PulseNative.Simple.pa_simple_write(_handle, count == buffer.Length ? buffer : buffer[..count], (nuint)count, out _) != 0)
                {
                    // A failed write is the server or the sink going away, not a clip reaching its end.
                    Ended?.Invoke(true);
                    return;
                }

                offset += count;

                if (offset < samples.Length)
                {
                    continue;
                }

                if (!_loops)
                {
                    Ended?.Invoke(false);
                    return;
                }

                offset = 0;
            }
        }
        finally
        {
            // The writer is the only thread that may touch the stream, so the flush that makes a stop
            // immediate and the free that ends the stream both happen here, once, on the way out.
            var handle = _handle;

            if (handle != IntPtr.Zero)
            {
                // Dropping what is still buffered is what makes a stop audible now rather than after
                // the server's queue drains, and it is harmless when the clip simply ended.
                PulseNative.Simple.pa_simple_flush(handle, out _);
            }

            ReleaseHandle();
        }
    }

    /// <summary>Scales 16-bit samples by <paramref name="volume"/> as they are copied into a block.</summary>
    private static void Scale(ReadOnlySpan<byte> source, Span<byte> destination, float volume)
    {
        if (volume >= 1f)
        {
            source.CopyTo(destination);
            return;
        }

        for (int i = 0; i + 1 < source.Length; i += 2)
        {
            short sample = (short)(source[i] | (source[i + 1] << 8));
            short scaled = (short)Math.Clamp(sample * volume, short.MinValue, short.MaxValue);
            destination[i] = (byte)(scaled & 0xFF);
            destination[i + 1] = (byte)((scaled >> 8) & 0xFF);
        }
    }

    private void ReleaseHandle()
    {
        IntPtr handle;

        lock (_gate)
        {
            handle = _handle;
            _handle = IntPtr.Zero;
        }

        if (handle != IntPtr.Zero)
        {
            PulseNative.Simple.pa_simple_free(handle);
        }
    }
}
