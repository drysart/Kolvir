using System.Text;
using System.Runtime.InteropServices;

namespace Kolvir.Audio;

/// <summary>
/// The CoreAudio and AudioToolbox entry points this feature uses.
/// </summary>
/// <remarks>
/// <para>
/// Two frameworks, two jobs. The HAL answers "what can make a sound", and it identifies a device with
/// an <c>AudioDeviceID</c> that means nothing outside the process. AudioQueue plays, and it takes a
/// device <i>UID</i> - a string whose form differs per driver - so the two identifiers are both kept
/// and never confused.
/// </para>
/// <para>
/// <b>Nothing here is Objective-C.</b> CoreAudio and AudioToolbox are plain C, so this half is callable
/// from a <c>net10.0-macos</c> assembly with no bindings and no runtime interop, which is why the macOS
/// half matches the Linux one in shape rather than looking like the Windows one.
/// </para>
/// </remarks>
internal static class CoreAudioNative
{
    /// <summary><c>kAudioObjectSystemObject</c>, the object properties about the system are read from.</summary>
    internal const uint SystemObject = 1;

    /// <summary><c>kAudioObjectPropertyScopeGlobal</c>.</summary>
    internal const uint ScopeGlobal = 0x676C6F62;

    /// <summary><c>kAudioObjectPropertyScopeOutput</c>, where a device's output streams live.</summary>
    internal const uint ScopeOutput = 0x6F757470;

    /// <summary><c>kAudioObjectPropertyElementMain</c>.</summary>
    internal const uint ElementMain = 0;

    /// <summary><c>kAudioHardwarePropertyDevices</c> ('dev#').</summary>
    internal const uint PropertyDevices = 0x64657623;

    /// <summary><c>kAudioHardwarePropertyDefaultOutputDevice</c> ('dOut').</summary>
    internal const uint PropertyDefaultOutput = 0x644F7574;

    /// <summary><c>kAudioDevicePropertyStreamConfiguration</c> ('slay'): what a device's streams are.</summary>
    internal const uint PropertyStreamConfiguration = 0x736C6179;

    /// <summary><c>kAudioObjectPropertyName</c> ('lnam').</summary>
    internal const uint PropertyName = 0x6C6E616D;

    /// <summary><c>kAudioDevicePropertyDeviceUID</c> ('uid '), which is what AudioQueue takes.</summary>
    internal const uint PropertyDeviceUid = 0x75696420;

    /// <summary><c>kAudioFormatLinearPCM</c>.</summary>
    internal const uint FormatLinearPcm = 0x6C70636D;

    /// <summary><c>kAudioFormatFlagIsSignedInteger | kAudioFormatFlagIsPacked</c>: 16-bit signed, packed.</summary>
    internal const uint FormatSignedIntegerPacked = 4 | 8;

    /// <summary><c>kAudioQueueProperty_CurrentDevice</c> ('aqcd'), which takes a device UID.</summary>
    internal const uint QueuePropertyCurrentDevice = 0x61716364;

    /// <summary><c>kAudioQueueParam_Volume</c>: the queue's own gain, 0 to 1.</summary>
    internal const uint QueueParamVolume = 1;

    /// <summary><c>kCFStringEncodingUTF8</c>.</summary>
    internal const uint StringEncodingUtf8 = 0x08000100;

    /// <summary>
    /// Reads a property address, which every HAL call needs and none of them owns.
    /// </summary>
    internal static PropertyAddress Address(uint selector, uint scope) =>
        new() { selector = selector, scope = scope, element = ElementMain };

    /// <summary>
    /// Copies a <c>CFString</c> the platform handed back into a managed string and releases it.
    /// </summary>
    /// <remarks>
    /// A CFString from a property read is owned by the caller, so not releasing it is a leak per
    /// device per enumeration - a small leak that a device picker reopened on every change turns into
    /// a real one.
    /// </remarks>
    internal static string TakeString(IntPtr cfString)
    {
        if (cfString == IntPtr.Zero)
        {
            return string.Empty;
        }

        try
        {
            var buffer = new byte[512];

            return CoreFoundation.CFStringGetCString(cfString, buffer, buffer.Length, StringEncodingUtf8)
                ? Encoding.UTF8.GetString(buffer).TrimEnd('\0')
                : string.Empty;
        }
        finally
        {
            CoreFoundation.CFRelease(cfString);
        }
    }

    /// <summary>Creates a <c>CFString</c> the caller must release.</summary>
    internal static IntPtr CreateString(string value)
    {
        var bytes = Encoding.UTF8.GetBytes(value + "\0");
        var handle = GCHandle.Alloc(bytes, GCHandleType.Pinned);

        try
        {
            return CoreFoundation.CFStringCreateWithCString(IntPtr.Zero, handle.AddrOfPinnedObject(), StringEncodingUtf8);
        }
        finally
        {
            handle.Free();
        }
    }

    /// <summary><c>AudioObjectPropertyAddress</c>: selector, scope and element.</summary>
    [StructLayout(LayoutKind.Sequential)]
    internal struct PropertyAddress
    {
        public uint selector;
        public uint scope;
        public uint element;
    }

    /// <summary><c>AudioStreamBasicDescription</c>, which describes an audio format to AudioQueue.</summary>
    [StructLayout(LayoutKind.Sequential)]
    internal struct StreamFormat
    {
        public double sampleRate;
        public uint formatId;
        public uint formatFlags;
        public uint bytesPerPacket;
        public uint framesPerPacket;
        public uint bytesPerFrame;
        public uint channelsPerFrame;
        public uint bitsPerChannel;
        public uint reserved;
    }

    /// <summary>
    /// The head of <c>AudioQueueBuffer</c>: capacity, payload pointer and how much of it is used.
    /// </summary>
    /// <remarks>
    /// The record continues with user data and packet descriptions that this feature does not use; only
    /// the fields up to the byte count are touched, and the rest is left as the queue wrote it.
    /// </remarks>
    [StructLayout(LayoutKind.Sequential)]
    internal struct QueueBuffer
    {
        public uint audioDataBytesCapacity;
        public IntPtr audioData;
        public uint audioDataByteSize;
        public IntPtr userData;
        public uint packetDescriptionCapacity;
        public IntPtr packetDescriptions;
        public uint packetDescriptionCount;
    }

    /// <summary>The callback an <c>AudioQueue</c> calls when a buffer has been consumed.</summary>
    internal delegate void QueueCallback(IntPtr userData, IntPtr queue, IntPtr buffer);

    internal static class Hal
    {
        [DllImport("/System/Library/Frameworks/CoreAudio.framework/CoreAudio")]
        internal static extern int AudioObjectGetPropertyDataSize(
            uint objectId, ref PropertyAddress address, uint qualifierSize, IntPtr qualifier, out uint dataSize);

        [DllImport("/System/Library/Frameworks/CoreAudio.framework/CoreAudio")]
        internal static extern int AudioObjectGetPropertyData(
            uint objectId, ref PropertyAddress address, uint qualifierSize, IntPtr qualifier, ref uint dataSize, IntPtr data);
    }

    internal static class Queue
    {
        [DllImport("/System/Library/Frameworks/AudioToolbox.framework/AudioToolbox")]
        internal static extern int AudioQueueNewOutput(
            ref StreamFormat format,
            QueueCallback callback,
            IntPtr userData,
            IntPtr runLoop,
            IntPtr runLoopMode,
            uint flags,
            out IntPtr queue);

        [DllImport("/System/Library/Frameworks/AudioToolbox.framework/AudioToolbox")]
        internal static extern int AudioQueueSetProperty(IntPtr queue, uint propertyId, IntPtr data, int dataSize);

        [DllImport("/System/Library/Frameworks/AudioToolbox.framework/AudioToolbox")]
        internal static extern int AudioQueueSetParameter(IntPtr queue, uint parameterId, float value);

        [DllImport("/System/Library/Frameworks/AudioToolbox.framework/AudioToolbox")]
        internal static extern int AudioQueueAllocateBuffer(IntPtr queue, uint byteSize, out IntPtr buffer);

        [DllImport("/System/Library/Frameworks/AudioToolbox.framework/AudioToolbox")]
        internal static extern int AudioQueueEnqueueBuffer(
            IntPtr queue, IntPtr buffer, uint packetDescriptionCount, IntPtr packetDescriptions);

        [DllImport("/System/Library/Frameworks/AudioToolbox.framework/AudioToolbox")]
        internal static extern int AudioQueueStart(IntPtr queue, IntPtr startTime);

        [DllImport("/System/Library/Frameworks/AudioToolbox.framework/AudioToolbox")]
        internal static extern int AudioQueuePause(IntPtr queue);

        [DllImport("/System/Library/Frameworks/AudioToolbox.framework/AudioToolbox")]
        internal static extern int AudioQueueStop(IntPtr queue, bool immediate);

        [DllImport("/System/Library/Frameworks/AudioToolbox.framework/AudioToolbox")]
        internal static extern int AudioQueueDispose(IntPtr queue, bool immediate);
    }

    internal static class CoreFoundation
    {
        [DllImport("/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation")]
        internal static extern IntPtr CFStringCreateWithCString(IntPtr allocator, IntPtr cString, uint encoding);

        [DllImport("/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation")]
        internal static extern bool CFStringGetCString(IntPtr theString, byte[] buffer, int bufferSize, uint encoding);

        [DllImport("/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation")]
        internal static extern void CFRelease(IntPtr cf);
    }
}
