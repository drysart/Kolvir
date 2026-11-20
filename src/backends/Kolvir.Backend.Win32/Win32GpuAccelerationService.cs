using static Kolvir.Backend.Win32.D3D12;

namespace Kolvir.Backend.Win32;

/// <summary>
/// The Win32 answer to <see cref="IGpuAccelerationService"/>: Direct3D 12, Vulkan and OpenGL, each with
/// the GPUs that can actually serve it, and the CPU path underneath.
/// </summary>
/// <remarks>
/// <para>
/// A family is listed only when something under it works. Direct3D 12 is asked for its hardware
/// adapters and each one is tried with a device and a command queue before it is offered; Vulkan is
/// asked for its physical devices and one without a graphics queue family is dropped; OpenGL is
/// offered when <c>opengl32.dll</c> is present, because a WGL context cannot be made without a window
/// and an application may ask this before it has one. <c>KOLVIR_NO_D3D12</c> and <c>KOLVIR_NO_VULKAN</c>
/// remove their family too: a process told never to use a tier cannot use it.
/// </para>
/// <para>
/// The lists are the families' own enumeration order, and a device's index in its family is what the
/// device suffix on a value names: <c>d3d:10de-2504-1616</c> is the adapter with that PCI identity.
/// </para>
/// </remarks>
internal sealed class Win32GpuAccelerationService : GpuAccelerationService
{
    /// <summary>The name a settings list shows for the Direct3D 12 family.</summary>
    private const string Direct3D12Name = "Direct3D 12";

    /// <summary>The name a settings list shows for the Vulkan family.</summary>
    private const string VulkanName = "Vulkan";

    /// <summary>The name a settings list shows for the OpenGL family.</summary>
    private const string OpenGlName = "OpenGL";

    /// <inheritdoc />
    protected override IReadOnlyList<GpuAccelerationType> Enumerate()
    {
        List<GpuAccelerationType> types = [];

        if (!Win32Gfx.Direct3D12Disabled)
        {
            var adapters = EnumerateAdapters();

            if (adapters.Count > 0)
            {
                types.Add(new GpuAccelerationType(GpuAccelerationKind.Direct3D12, Direct3D12Name, adapters));
            }
        }

        if (!Win32Gfx.VulkanDisabled)
        {
            var devices = Win32VulkanDevice.EnumerateDevices();

            if (devices.Count > 0)
            {
                types.Add(new GpuAccelerationType(GpuAccelerationKind.Vulkan, VulkanName, devices));
            }
        }

        if (OpenGlIsPresent())
        {
            types.Add(new GpuAccelerationType(GpuAccelerationKind.OpenGl, OpenGlName));
        }

        return types;
    }

    /// <summary>
    /// Records what a window came up with, which is what <see cref="IGpuAccelerationService.Current"/>
    /// reports. Null - the window is on its DIB - is the CPU path.
    /// </summary>
    /// <param name="surface">The tier the window built, or null for the CPU path.</param>
    internal void Report(IWin32GpuSurface? surface)
    {
        if (surface is null)
        {
            ReportCurrent(GpuAccelerationSyntax.CpuValue);
            return;
        }

        var kind = KindOf(surface.Tier);
        var value = GpuAccelerationSyntax.Format(kind, surface.DeviceKey);

        // The tier may have fallen back to a device the enumeration did not list - an adapter that
        // could not make a device when it was asked, or one that arrived since. Reporting the family's
        // first device keeps Current pointing at something a settings list can select.
        ReportCurrent(TryFindOption(value, out _) ? value : GpuAccelerationSyntax.Format(kind));    }

    /// <summary>Which family a tier's diagnostic name belongs to.</summary>
    /// <param name="tier">The tier's name: <c>d3d12</c>, <c>vulkan</c> or <c>gl</c>.</param>
    internal static GpuAccelerationKind KindOf(string tier) => tier switch
    {
        "d3d12" => GpuAccelerationKind.Direct3D12,
        "vulkan" => GpuAccelerationKind.Vulkan,
        "gl" => GpuAccelerationKind.OpenGl,
        _ => GpuAccelerationKind.Cpu,
    };

    /// <summary>
    /// The machine's hardware adapters, each one tried with a device and a command queue so that an
    /// adapter that cannot be drawn on is not offered.
    /// </summary>
    private static List<GpuAccelerationDevice> EnumerateAdapters()
    {
        List<GpuAccelerationDevice> adapters = [];

        if (!IsAvailable)
        {
            return adapters;
        }

        IntPtr factory = CreateFactory1(out _);

        if (factory == IntPtr.Zero)
        {
            return adapters;
        }

        try
        {
            for (uint index = 0; index < 16; index++)
            {
                IntPtr adapter = EnumAdapters1(factory, index);

                if (adapter == IntPtr.Zero)
                {
                    break;
                }

                try
                {
                    if (!GetAdapterDesc1(adapter, out var description) ||
                        IsSoftwareAdapter(description.Flags, Describe(description)) ||
                        !CanDrawOn(adapter))
                    {
                        continue;
                    }

                    adapters.Add(new GpuAccelerationDevice(AdapterKey(description), Describe(description)));
                }
                finally
                {
                    Release(ref adapter);
                }
            }
        }
        finally
        {
            Release(ref factory);
        }

        return adapters;
    }

    /// <summary>True when a device and a direct queue can be made on this adapter.</summary>
    /// <param name="adapter">The <c>IDXGIAdapter1</c> to try.</param>
    private static bool CanDrawOn(IntPtr adapter)
    {
        IntPtr device = CreateDevice(adapter, out _);

        if (device == IntPtr.Zero)
        {
            return false;
        }

        IntPtr queue = IntPtr.Zero;

        try
        {
            queue = CreateCommandQueue(device, out _);
            return queue != IntPtr.Zero;
        }
        finally
        {
            Release(ref queue);
            Release(ref device);
        }
    }

    /// <summary>
    /// True when this machine has an OpenGL driver to make a context from. The context itself needs a
    /// window, so this is the most a listing can honestly say before one exists.
    /// </summary>
    private static bool OpenGlIsPresent()
    {
        if (!System.Runtime.InteropServices.NativeLibrary.TryLoad("opengl32.dll", out var handle))
        {
            return false;
        }

        System.Runtime.InteropServices.NativeLibrary.Free(handle);
        return true;
    }
}
