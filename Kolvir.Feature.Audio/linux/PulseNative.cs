using System.Runtime.InteropServices;

namespace Kolvir.Audio;

/// <summary>
/// The libpulse entry points and structures this feature uses, declared by hand.
/// </summary>
/// <remarks>
/// <para>
/// Two libraries, two jobs. Enumeration needs the asynchronous <c>pa_context</c> API and a main loop to
/// drive it, because the simple API has no way to list sinks at all. Playback uses the blocking
/// <c>pa_simple</c> API, which can still be aimed at a sink by name - so the awkward half of Pulse is
/// confined to reading a list, and a voice is a thread that writes blocks.
/// </para>
/// <para>
/// <b>The structures are the first members only.</b> <c>pa_sink_info</c> is a long record of volume,
/// channel map, latency and statistics that this feature has no use for, and every field of it is a
/// chance to get an offset wrong on a 64-bit machine. Name, index, description and sample spec are
/// the first four, so they are the four that are declared.
/// </para>
/// <para>
/// <b>Everything here is optional.</b> <see cref="IsAvailable"/> is checked before any of it is used,
/// because a machine without a sound server, a container without the library and a CI runner are all
/// the normal case: the feature has to report silence with a reason, not throw out of a type
/// initialiser.
/// </para>
/// </remarks>
internal static class PulseNative
{
    /// <summary><c>PA_STREAM_PLAYBACK</c>: a stream that carries audio to a sink.</summary>
    internal const int StreamPlayback = 1;

    /// <summary><c>PA_SAMPLE_S16LE</c>, which is the format every clip is decoded into.</summary>
    internal const int SampleS16Le = 3;

    /// <summary><c>PA_CONTEXT_READY</c>: connected, and able to answer questions.</summary>
    internal const int ContextReady = 4;

    /// <summary><c>PA_OPERATION_RUNNING</c>: not finished, so the main loop has to keep running.</summary>
    internal const int OperationRunning = 0;

    /// <summary>
    /// Whether both libraries this half needs are present, which is the one check that decides
    /// between a real device list and a reason.
    /// </summary>
    internal static bool IsAvailable { get; } =
        NativeLibrary.TryLoad("libpulse.so.0", out _) &&
        NativeLibrary.TryLoad("libpulse-simple.so.0", out _);

    /// <summary>
    /// Reads a C string out of a PulseAudio structure, tolerating the null the server may leave in any
    /// of its descriptive fields - <c>Marshal.PtrToStringUTF8</c> throws on null.
    /// </summary>
    internal static string String(IntPtr pointer) =>
        pointer == IntPtr.Zero ? string.Empty : Marshal.PtrToStringUTF8(pointer) ?? string.Empty;

    /// <summary>The sample spec PulseAudio describes a sink with.</summary>
    [StructLayout(LayoutKind.Sequential)]
    internal struct SampleSpec
    {
        public int format;
        public uint rate;
        public byte channels;
        public byte padding1;
        public byte padding2;
        public byte padding3;
    }

    /// <summary>The first four members of <c>pa_sink_info</c>: name, index, description, sample spec.</summary>
    [StructLayout(LayoutKind.Sequential)]
    internal struct SinkInfo
    {
        public IntPtr name;
        public uint index;
        public IntPtr description;
        public SampleSpec spec;
    }

    /// <summary>What a sink list operation calls back with: one sink, or the end of the list.</summary>
    internal delegate void SinkInfoCallback(IntPtr context, IntPtr info, int eol, IntPtr userData);

    internal static class Context
    {
        [DllImport("libpulse.so.0")]
        internal static extern IntPtr pa_mainloop_new();

        [DllImport("libpulse.so.0")]
        internal static extern IntPtr pa_mainloop_get_api(IntPtr mainloop);

        [DllImport("libpulse.so.0")]
        internal static extern int pa_mainloop_iterate(IntPtr mainloop, int block, out int retval);

        [DllImport("libpulse.so.0")]
        internal static extern void pa_mainloop_free(IntPtr mainloop);

        [DllImport("libpulse.so.0")]
        internal static extern IntPtr pa_context_new(IntPtr api, string name);

        [DllImport("libpulse.so.0")]
        internal static extern int pa_context_connect(IntPtr context, string? server, int flags, IntPtr spawnApi);

        [DllImport("libpulse.so.0")]
        internal static extern int pa_context_disconnect(IntPtr context);

        [DllImport("libpulse.so.0")]
        internal static extern void pa_context_unref(IntPtr context);

        [DllImport("libpulse.so.0")]
        internal static extern int pa_context_get_state(IntPtr context);

        [DllImport("libpulse.so.0")]
        internal static extern IntPtr pa_context_get_sink_info_list(
            IntPtr context, SinkInfoCallback callback, IntPtr userData);

        [DllImport("libpulse.so.0")]
        internal static extern IntPtr pa_context_get_sink_info_by_name(
            IntPtr context, string name, SinkInfoCallback callback, IntPtr userData);

        [DllImport("libpulse.so.0")]
        internal static extern int pa_operation_get_state(IntPtr operation);

        [DllImport("libpulse.so.0")]
        internal static extern void pa_operation_unref(IntPtr operation);
    }

    internal static class Simple
    {
        [DllImport("libpulse-simple.so.0")]
        internal static extern IntPtr pa_simple_new(
            string? server,
            string name,
            int direction,
            string? device,
            string streamName,
            ref SampleSpec spec,
            IntPtr channelMap,
            IntPtr bufferAttr,
            out int error);

        [DllImport("libpulse-simple.so.0")]
        internal static extern int pa_simple_write(IntPtr simple, byte[] data, nuint bytes, out int error);

        [DllImport("libpulse-simple.so.0")]
        internal static extern int pa_simple_flush(IntPtr simple, out int error);

        [DllImport("libpulse-simple.so.0")]
        internal static extern void pa_simple_free(IntPtr simple);
    }
}
