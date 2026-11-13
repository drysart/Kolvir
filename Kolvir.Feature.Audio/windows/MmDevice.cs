using System.Runtime.InteropServices;
using System.Text;

namespace Kolvir.Audio;

/// <summary>
/// The MMDevice render endpoints: what Windows calls an output device, and what XAudio2 takes a
/// device id from.
/// </summary>
/// <remarks>
/// <para>
/// XAudio2 2.9 removed device enumeration, so the list comes from MMDevice and the playing comes from
/// XAudio2, tied together by the id string this class reads. That is two COM surfaces for one
/// feature, and it is the API's arrangement rather than a choice.
/// </para>
/// <para>
/// <b>The vtables are called rather than declared.</b> Four interfaces with a dozen methods between
/// them would be four declarations to keep in step with a header nothing in the build reads, while the
/// slot numbers are the ABI and are fixed. Each call is one line and its slot is named where it is
/// used, so a wrong one is visible instead of hidden behind a signature.
/// </para>
/// <para>
/// <b>A machine with no endpoint is returned as an empty list.</b> A remote session, a VM and a
/// stripped-down image all reach here and all get the same answer, which the engine turns into the
/// silent device.
/// </para>
/// </remarks>
internal static class MmDevice
{
    private const uint StgmRead = 0;

    /// <summary><c>EDataFlow.eRender</c>: the endpoints that make a sound.</summary>
    private const int Render = 0;

    /// <summary><c>ERole.eConsole</c>: the device sounds for games and applications go to.</summary>
    private const int Console = 0;

    /// <summary><c>DEVICE_STATE_ACTIVE</c>, so a disabled or unplugged endpoint is not offered.</summary>
    private const uint Active = 1;

    /// <summary><c>VT_LPWSTR</c>: the type of a string in a <c>PROPVARIANT</c>.</summary>
    private const ushort VariantString = 31;

    private static readonly Guid EnumeratorClass = new("BCDE0395-E52F-467C-8E3D-C4579291692E");
    private static readonly Guid EnumeratorInterface = new("A95664D2-9614-4F35-A746-DE8DB63617E6");

    /// <summary>
    /// The active render endpoints, with the console-default flagged, or an empty list when there is
    /// nothing to play through.
    /// </summary>
    internal static IReadOnlyList<PlatformDevice> Enumerate()
    {
        if (Com.Create(out var enumerator, EnumeratorClass, EnumeratorInterface) is not 0)
        {
            return [];
        }

        try
        {
            var devices = new List<PlatformDevice>();
            var defaultId = DefaultId(enumerator);

            if (Com.EnumAudioEndpoints(enumerator, Render, Active, out var collection) is not 0)
            {
                return devices;
            }

            try
            {
                if (Com.Count(collection, out uint count) is not 0)
                {
                    return devices;
                }

                for (uint i = 0; i < count; i++)
                {
                    if (Com.Item(collection, i, out IntPtr device) is not 0)
                    {
                        continue;
                    }

                    try
                    {
                        var id = DeviceId(device);

                        if (string.IsNullOrEmpty(id))
                        {
                            continue;
                        }

                        devices.Add(new PlatformDevice(id, FriendlyName(device), IsDefault: id == defaultId));
                    }
                    finally
                    {
                        Com.Release(device);
                    }
                }
            }
            finally
            {
                Com.Release(collection);
            }

            // A session that has no console default - a service, an RDP session mid-reconnect - still
            // has endpoints, and the first of them is a better answer than none.
            if (devices.Count > 0 && !devices.Exists(device => device.IsDefault))
            {
                devices[0] = devices[0] with { IsDefault = true };
            }

            return devices;
        }
        finally
        {
            Com.Release(enumerator);
        }
    }

    /// <summary>The console default endpoint's id, or null when the session has no default.</summary>
    private static string? DefaultId(IntPtr enumerator)
    {
        // GetDefaultAudioEndpoint(EDataFlow, ERole): absent is a normal answer, not a failure.
        if (Com.DefaultEndpoint(enumerator, Render, Console, out IntPtr device) is not 0)
        {
            return null;
        }

        try
        {
            return DeviceId(device);
        }
        finally
        {
            Com.Release(device);
        }
    }

    /// <summary>Reads a device's id, which is a COM-allocated string and has to be freed.</summary>
    private static string? DeviceId(IntPtr device)
    {
        if (Com.GetId(device, out IntPtr pointer) is not 0)
        {
            return null;
        }

        try
        {
            return Marshal.PtrToStringUni(pointer);
        }
        finally
        {
            if (pointer != IntPtr.Zero)
            {
                Marshal.FreeCoTaskMem(pointer);
            }
        }
    }

    /// <summary>Reads <c>PKEY_Device_FriendlyName</c>, which is what a user recognises the device by.</summary>
    private static string FriendlyName(IntPtr device)
    {
        // OpenPropertyStore wants an STGM access flag; the store is read-only here.
        if (Com.OpenStore(device, StgmRead, out IntPtr store) is not 0)
        {
            return "Audio device";
        }

        try
        {
            var key = new PropertyKey
            {
                format = new Guid("a45c254e-df1c-4efd-8020-67d146a850e0"),
                id = 14,
            };

            if (Com.GetValue(store, ref key, out var value) is not 0)
            {
                return "Audio device";
            }

            try
            {
                // PROPVARIANT: vt identifies the type, and the payload follows the four ushorts.
                return value.type == VariantString
                    ? Marshal.PtrToStringUni(value.pointer) ?? "Audio device"
                    : "Audio device";
            }
            finally
            {
                PropVariantClear(ref value);
            }
        }
        finally
        {
            Com.Release(store);
        }
    }

    [DllImport("ole32.dll")]
    private static extern int PropVariantClear(ref PropVariant value);

    [StructLayout(LayoutKind.Sequential)]
    private struct PropertyKey
    {
        public Guid format;
        public uint id;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct PropVariant
    {
        public ushort type;
        public ushort reserved1;
        public ushort reserved2;
        public ushort reserved3;
        public IntPtr pointer;
        public IntPtr pointer2;
    }

    /// <summary>Raw vtable calls, one method each, with the slot named where it is used.</summary>
    private static class Com
    {
        private const int ReleaseSlot = 2;

        public static int Create(out IntPtr instance, Guid clsid, Guid iid) =>
            CoCreateInstance(ref clsid, IntPtr.Zero, 1, ref iid, out instance);

        public static void Release(IntPtr instance)
        {
            if (instance != IntPtr.Zero)
            {
                Function<ReleaseFn>(instance, ReleaseSlot)(instance);
            }
        }

        // IMMDeviceEnumerator: 3 EnumAudioEndpoints, 4 GetDefaultAudioEndpoint, 5 GetDevice.
        public static int EnumAudioEndpoints(IntPtr enumerator, int flow, uint stateMask, out IntPtr collection) =>
            Function<EnumEndpointsFn>(enumerator, 3)(enumerator, flow, stateMask, out collection);

        public static int DefaultEndpoint(IntPtr enumerator, int flow, int role, out IntPtr device) =>
            Function<DefaultEndpointFn>(enumerator, 4)(enumerator, flow, role, out device);

        // IMMDeviceCollection: 3 GetCount, 4 Item.
        public static int Count(IntPtr collection, out uint count) =>
            Function<CountFn>(collection, 3)(collection, out count);

        public static int Item(IntPtr collection, uint index, out IntPtr device) =>
            Function<ItemFn>(collection, 4)(collection, index, out device);

        // IMMDevice: 3 Activate, 4 OpenPropertyStore, 5 GetId.
        public static int OpenStore(IntPtr device, uint access, out IntPtr store) =>
            Function<OpenStoreFn>(device, 4)(device, access, out store);

        public static int GetId(IntPtr device, out IntPtr id) =>
            Function<StringOutFn>(device, 5)(device, out id);

        // IPropertyStore: 3 GetCount, 4 GetAt, 5 GetValue.
        public static int GetValue(IntPtr store, ref PropertyKey key, out PropVariant value) =>
            Function<GetValueFn>(store, 5)(store, ref key, out value);

        private static T Function<T>(IntPtr instance, int slot) where T : Delegate =>
            Marshal.GetDelegateForFunctionPointer<T>(Marshal.ReadIntPtr(Marshal.ReadIntPtr(instance), slot * IntPtr.Size));

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate int ReleaseFn(IntPtr self);

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate int EnumEndpointsFn(IntPtr self, int flow, uint stateMask, out IntPtr collection);

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate int DefaultEndpointFn(IntPtr self, int flow, int role, out IntPtr device);

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate int CountFn(IntPtr self, out uint count);

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate int ItemFn(IntPtr self, uint index, out IntPtr device);

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate int OpenStoreFn(IntPtr self, uint access, out IntPtr store);

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate int StringOutFn(IntPtr self, out IntPtr text);

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate int GetValueFn(IntPtr self, ref PropertyKey key, out PropVariant value);

        [DllImport("ole32.dll")]
        private static extern int CoCreateInstance(
            ref Guid clsid, IntPtr outer, uint context, ref Guid iid, out IntPtr instance);
    }
}
