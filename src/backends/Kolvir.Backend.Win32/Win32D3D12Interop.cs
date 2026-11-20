using System.Runtime.InteropServices;

namespace Kolvir.Backend.Win32;

/// <summary>
/// The slice of DXGI and Direct3D 12 a windowed presenter needs: a factory, an adapter, a device, one
/// direct command queue, and a flip-model swap chain. Everything else stays in the headers.
/// </summary>
/// <remarks>
/// <para>
/// Every call here is a raw vtable call rather than a declared interface, for the reason the Vulkan
/// slice does the same thing: the handles are pointers handed to and from Skia, so a wrapper type
/// would only add a layer between them. The COM entry points are reached by explicit vtable slot,
/// which is what the comment on each call records - the slots are the ABI, and a wrong one is a call
/// into the wrong method rather than a compile error.
/// </para>
/// <para>
/// The loaders are nullable. A machine without <c>d3d12.dll</c> or <c>dxgi.dll</c> - which is only
/// Windows 7, or a stripped image - leaves the export unresolvable and the window on whatever tier it
/// had, exactly as a missing Vulkan loader does.
/// </para>
/// </remarks>
internal static unsafe class D3D12
{
    /// <summary><c>DXGI_ERROR_DEVICE_REMOVED</c>.</summary>
    internal const int ErrorDeviceRemoved = unchecked((int)0x887A0005);

    /// <summary><c>DXGI_ERROR_DEVICE_RESET</c>.</summary>
    internal const int ErrorDeviceReset = unchecked((int)0x887A0007);

    /// <summary><c>DXGI_STATUS_OCCLUDED</c>: the frame was not shown because the window is hidden.</summary>
    internal const int StatusOccluded = 0x087A0001;

    /// <summary><c>DXGI_ADAPTER_FLAG_SOFTWARE</c>: WARP, which is not what "accelerated" means here.</summary>
    internal const uint AdapterFlagSoftware = 2;

    /// <summary><c>D3D_FEATURE_LEVEL_11_0</c>: the floor Skia's Direct3D backend is written against.</summary>
    internal const int FeatureLevel11_0 = 0xB000;

    /// <summary><c>DXGI_FORMAT_B8G8R8A8_UNORM</c>: the swap chain format Windows composites fastest.</summary>
    internal const int FormatBgra8Unorm = 87;

    /// <summary><c>DXGI_USAGE_RENDER_TARGET_OUTPUT</c>.</summary>
    internal const uint UsageRenderTargetOutput = 0x20;

    /// <summary><c>DXGI_SCALING_STRETCH</c>: the window's client area is already the size DXGI is asked for.</summary>
    internal const int ScalingStretch = 0;

    /// <summary><c>DXGI_SWAP_EFFECT_FLIP_DISCARD</c>: what makes the resize path release its buffers.</summary>
    internal const int SwapEffectFlipDiscard = 4;

    /// <summary><c>DXGI_SWAP_EFFECT_FLIP_SEQUENTIAL</c>: the older flip model, for a driver that
    /// refuses the discard one.</summary>
    internal const int SwapEffectFlipSequential = 3;

    /// <summary>
    /// <c>DXGI_ALPHA_MODE_IGNORE</c>. It is 3, not 2: 2 is <c>STRAIGHT</c>, which an HWND swap chain
    /// does not accept, and DXGI answers the create call with <c>DXGI_ERROR_INVALID_CALL</c>.
    /// </summary>
    internal const int AlphaModeIgnore = 3;

    /// <summary><c>D3D12_COMMAND_LIST_TYPE_DIRECT</c>.</summary>
    internal const int CommandListTypeDirect = 0;

    /// <summary><c>D3D12_COMMAND_QUEUE_FLAG_NONE</c>.</summary>
    internal const int CommandQueueFlagNone = 0;

    /// <summary><c>D3D12_RESOURCE_STATE_PRESENT</c>: what a back buffer is in when it is handed out.</summary>
    internal const int ResourceStatePresent = 0;

    /// <summary><c>IID_NULL</c> is not a thing DXGI accepts, so the factory asks for its own interface.</summary>
    internal static readonly Guid Factory1Interface = new("770AAE78-F26F-4DBA-A829-253C83D1B387");

    /// <summary><c>IID_ID3D12Device</c>.</summary>
    internal static readonly Guid DeviceInterface = new("189819F1-1DB6-4B57-BE54-1821339B85F7");

    // Vtable slots. IUnknown is 0-2, IDXGIObject adds 3-6, and every DXGI interface continues from
    // there in header order; a slot is a fact about the ABI and nothing here may be renumbered.
    private const int SlotQueryInterface = 0;
    private const int SlotRelease = 2;
    private const int SlotDeviceCreateCommandQueue = 8;
    private const int SlotAdapterGetDesc1 = 10;
    private const int SlotAdapter3QueryVideoMemoryInfo = 14;
    private const int SlotAdapterEnumOutputs = 7;
    private const int SlotOutputGetDesc = 7;
    private const int SlotFactoryEnumAdapters1 = 12;
    private const int SlotFactory2CreateSwapChainForHwnd = 15;
    private const int SlotSwapChainPresent = 8;
    private const int SlotSwapChainGetBuffer = 9;
    private const int SlotSwapChainResizeBuffers = 13;
    private const int SlotSwapChain3GetCurrentBackBufferIndex = 36;

    private static readonly IntPtr D3D12Library = LoadLibrary("d3d12.dll");
    private static readonly IntPtr DxgiLibrary = LoadLibrary("dxgi.dll");

    private static readonly IntPtr CreateDeviceEntry = GetEntryPoint(D3D12Library, "D3D12CreateDevice");
    private static readonly IntPtr CreateFactoryEntry = GetEntryPoint(DxgiLibrary, "CreateDXGIFactory1");

    [DllImport("kernel32.dll", EntryPoint = "LoadLibraryW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr LoadLibrary(string name);

    [DllImport("kernel32.dll", EntryPoint = "GetProcAddress", CharSet = CharSet.Ansi, SetLastError = true)]
    private static extern IntPtr GetProcAddress(IntPtr module, string name);

    private static IntPtr GetEntryPoint(IntPtr module, string name) =>
        module == IntPtr.Zero ? IntPtr.Zero : GetProcAddress(module, name);

    /// <summary>True when both libraries and both exports are present.</summary>
    internal static bool IsAvailable => CreateDeviceEntry != IntPtr.Zero && CreateFactoryEntry != IntPtr.Zero;

    /// <summary><c>DXGI_ADAPTER_DESC1</c>: only the name and the flags are read.</summary>
    [StructLayout(LayoutKind.Sequential)]
    internal struct DxgiAdapterDesc1
    {
        public fixed char Description[128];
        public uint VendorId;
        public uint DeviceId;
        public uint SubSystemId;
        public uint Revision;
        public nuint DedicatedVideoMemory;
        public nuint DedicatedSystemMemory;
        public nuint SharedSystemMemory;
        public long AdapterLuidLow;
        public uint AdapterLuidHigh;
        public uint Flags;
    }

    /// <summary><c>DXGI_SAMPLE_DESC</c>.</summary>
    [StructLayout(LayoutKind.Sequential)]
    internal struct DxgiSampleDesc
    {
        public uint Count;
        public uint Quality;
    }

    /// <summary><c>DXGI_SWAP_CHAIN_DESC1</c>.</summary>
    [StructLayout(LayoutKind.Sequential)]
    internal struct DxgiSwapChainDesc1
    {
        public uint Width;
        public uint Height;
        public int Format;
        public int Stereo;
        public DxgiSampleDesc SampleDesc;
        public uint BufferUsage;
        public uint BufferCount;
        public int Scaling;
        public int SwapEffect;
        public int AlphaMode;
        public uint Flags;
    }

    /// <summary><c>D3D12_COMMAND_QUEUE_DESC</c>.</summary>
    [StructLayout(LayoutKind.Sequential)]
    internal struct D3D12CommandQueueDesc
    {
        public int Type;
        public int Priority;
        public int Flags;
        public uint NodeMask;
    }

    /// <summary>Asks an interface for another one.</summary>
    /// <returns>The new interface, or zero when it is not supported.</returns>
    internal static IntPtr QueryInterface(IntPtr unknown, Guid interfaceId)
    {
        var query = (delegate* unmanaged[Stdcall]<IntPtr, Guid*, IntPtr*, int>)(*(IntPtr**)unknown)[SlotQueryInterface];
        Guid id = interfaceId;
        IntPtr result = IntPtr.Zero;
        return query(unknown, &id, &result) == 0 ? result : IntPtr.Zero;
    }

    /// <summary>Calls <c>Release</c> through the vtable and leaves the pointer unusable.</summary>
    /// <param name="unknown">The interface pointer to release, or zero.</param>
    internal static void Release(ref IntPtr unknown)
    {
        if (unknown == IntPtr.Zero)
        {
            return;
        }

        ((delegate* unmanaged[Stdcall]<IntPtr, uint>)(*(IntPtr**)unknown)[SlotRelease])(unknown);
        unknown = IntPtr.Zero;
    }

    /// <summary>Creates an <c>IDXGIFactory1</c>.</summary>
    /// <returns>The factory, or zero with the <c>HRESULT</c> that failed.</returns>
    internal static IntPtr CreateFactory1(out int result)
    {
        if (CreateFactoryEntry == IntPtr.Zero)
        {
            result = -1;
            return IntPtr.Zero;
        }

        var create = (delegate* unmanaged[Stdcall]<Guid*, IntPtr*, int>)CreateFactoryEntry;
        Guid interfaceId = Factory1Interface;
        IntPtr factory = IntPtr.Zero;
        result = create(&interfaceId, &factory);
        return result == 0 ? factory : IntPtr.Zero;
    }

    /// <summary>Enumerates one adapter.</summary>
    /// <param name="factory">The factory.</param>
    /// <param name="index">Which adapter, from zero.</param>
    /// <returns>The adapter, or zero when there are no more.</returns>
    internal static IntPtr EnumAdapters1(IntPtr factory, uint index)
    {
        var enumerate = (delegate* unmanaged[Stdcall]<IntPtr, uint, IntPtr*, int>)(*(IntPtr**)factory)[SlotFactoryEnumAdapters1];
        IntPtr adapter = IntPtr.Zero;
        return enumerate(factory, index, &adapter) == 0 ? adapter : IntPtr.Zero;
    }

    /// <summary>Fills in an adapter's description.</summary>
    /// <returns>False when the call failed.</returns>
    internal static bool GetAdapterDesc1(IntPtr adapter, out DxgiAdapterDesc1 description)
    {
        var get = (delegate* unmanaged[Stdcall]<IntPtr, DxgiAdapterDesc1*, int>)(*(IntPtr**)adapter)[SlotAdapterGetDesc1];
        description = default;

        // A stack local rather than the out parameter: a struct that lives on the stack can have its
        // address taken directly, which an out parameter cannot.
        DxgiAdapterDesc1 local;

        if (get(adapter, &local) != 0)
        {
            return false;
        }

        description = local;
        return true;
    }

    /// <summary>Creates an <c>ID3D12Device</c> on the given adapter.</summary>
    /// <returns>The device, or zero with the <c>HRESULT</c> that failed.</returns>
    internal static IntPtr CreateDevice(IntPtr adapter, out int result)
    {
        if (CreateDeviceEntry == IntPtr.Zero)
        {
            result = -1;
            return IntPtr.Zero;
        }

        var create = (delegate* unmanaged[Stdcall]<IntPtr, int, Guid*, IntPtr*, int>)CreateDeviceEntry;
        Guid interfaceId = DeviceInterface;
        IntPtr device = IntPtr.Zero;
        result = create(adapter, FeatureLevel11_0, &interfaceId, &device);
        return result == 0 ? device : IntPtr.Zero;
    }

    /// <summary>Creates the direct command queue Skia submits its work to.</summary>
    /// <returns>The queue, or zero with the <c>HRESULT</c> that failed.</returns>
    internal static IntPtr CreateCommandQueue(IntPtr device, out int result)
    {
        var create = (delegate* unmanaged[Stdcall]<IntPtr, D3D12CommandQueueDesc*, Guid*, IntPtr*, int>)(*(IntPtr**)device)[SlotDeviceCreateCommandQueue];
        var description = new D3D12CommandQueueDesc
        {
            Type = CommandListTypeDirect,
            Priority = 0,
            Flags = CommandQueueFlagNone,
            NodeMask = 0,
        };

        Guid interfaceId = CommandQueueInterface;
        IntPtr queue = IntPtr.Zero;
        result = create(device, &description, &interfaceId, &queue);
        return result == 0 ? queue : IntPtr.Zero;
    }

    /// <summary>Enumerates one of an adapter's outputs.</summary>
    /// <param name="adapter">The adapter.</param>
    /// <param name="index">Which output, from zero.</param>
    /// <returns>The output, or zero when there are no more.</returns>
    internal static IntPtr EnumOutputs(IntPtr adapter, uint index)
    {
        var enumerate = (delegate* unmanaged[Stdcall]<IntPtr, uint, IntPtr*, int>)(*(IntPtr**)adapter)[SlotAdapterEnumOutputs];
        IntPtr output = IntPtr.Zero;
        return enumerate(adapter, index, &output) == 0 ? output : IntPtr.Zero;
    }

    /// <summary>Fills in an output's description.</summary>
    /// <returns>False when the call failed.</returns>
    internal static bool GetOutputDesc(IntPtr output, out DxgiOutputDesc description)
    {
        var get = (delegate* unmanaged[Stdcall]<IntPtr, DxgiOutputDesc*, int>)(*(IntPtr**)output)[SlotOutputGetDesc];
        description = default;
        DxgiOutputDesc local;

        if (get(output, &local) != 0)
        {
            return false;
        }

        description = local;
        return true;
    }

    /// <summary>
    /// True when an output's desktop rectangle contains <paramref name="x"/> and
    /// <paramref name="y"/>.
    /// </summary>
    internal static bool Contains(in DxgiOutputDesc output, int x, int y) =>
        output.AttachedToDesktop != 0
        && x >= output.Left && x < output.Right
        && y >= output.Top && y < output.Bottom;

    /// <summary>The window's rectangle in desktop coordinates.</summary>
    /// <returns>False when the window handle is not a window.</returns>
    internal static bool TryGetWindowRect(IntPtr hwnd, out RECT rect)
    {
        rect = default;
        return GetWindowRectEntry(hwnd, out rect);
    }

    [DllImport("user32.dll", EntryPoint = "GetWindowRect", SetLastError = true)]
    private static extern bool GetWindowRectEntry(IntPtr hwnd, out RECT rect);

    /// <summary><c>RECT</c>.</summary>
    [StructLayout(LayoutKind.Sequential)]
    internal struct RECT
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    /// <summary><c>DXGI_OUTPUT_DESC</c>: the desktop rectangle and whether the output is in use.</summary>
    [StructLayout(LayoutKind.Sequential)]
    internal struct DxgiOutputDesc
    {
        public fixed char DeviceName[32];
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
        public int AttachedToDesktop;
        public int Rotation;
        public IntPtr Monitor;
    }
    /// <summary><c>IID_ID3D12CommandQueue</c>.</summary>
    internal static readonly Guid CommandQueueInterface = new("0EC870A6-5D7E-4C22-8CFC-5BAAE07616ED");

    /// <summary>Creates a flip-model swap chain on a window.</summary>
    /// <param name="factory">The factory.</param>
    /// <param name="queue">The command queue the swap chain will present from.</param>
    /// <param name="hwnd">The window.</param>
    /// <param name="width">The client width in pixels.</param>
    /// <param name="height">The client height in pixels.</param>
    /// <param name="swapEffect">The <c>DXGI_SWAP_EFFECT</c> to ask for.</param>
    /// <param name="bufferCount">How many back buffers to ask for.</param>
    /// <param name="result">The <c>HRESULT</c>.</param>
    /// <returns>The swap chain, or zero.</returns>
    internal static IntPtr CreateSwapChainForHwnd(
        IntPtr factory,
        IntPtr queue,
        IntPtr hwnd,
        int width,
        int height,
        int swapEffect,
        uint bufferCount,
        out int result)
    {
        var create = (delegate* unmanaged[Stdcall]<IntPtr, IntPtr, IntPtr, DxgiSwapChainDesc1*, IntPtr, IntPtr, IntPtr*, int>)(*(IntPtr**)factory)[SlotFactory2CreateSwapChainForHwnd];
        var description = new DxgiSwapChainDesc1
        {
            Width = (uint)width,
            Height = (uint)height,
            Format = FormatBgra8Unorm,
            Stereo = 0,
            SampleDesc = new DxgiSampleDesc { Count = 1, Quality = 0 },
            BufferUsage = UsageRenderTargetOutput,
            BufferCount = bufferCount,
            Scaling = ScalingStretch,
            SwapEffect = swapEffect,
            AlphaMode = AlphaModeIgnore,
            Flags = 0,
        };

        IntPtr created = IntPtr.Zero;
        result = create(factory, queue, hwnd, &description, IntPtr.Zero, IntPtr.Zero, &created);

        if (result != 0 || created == IntPtr.Zero)
        {
            return IntPtr.Zero;
        }

        // CreateSwapChainForHwnd hands back an IDXGISwapChain1; the back buffer index comes from
        // IDXGISwapChain3, so the newer interface is asked for and the older reference let go.
        IntPtr swapChain = QueryInterface(created, SwapChain3Interface);
        Release(ref created);

        if (swapChain == IntPtr.Zero)
        {
            result = -1;
        }

        return swapChain;
    }

    /// <summary><c>IID_IDXGISwapChain3</c>, asked for directly so the index call needs no cast.</summary>
    internal static readonly Guid SwapChain3Interface = new("94D99BDB-F1F8-4AB0-B236-7DA0170EDAB1");

    /// <summary>Resizes a swap chain's buffers.</summary>
    /// <param name="swapChain">The swap chain.</param>
    /// <param name="bufferCount">The buffer count to keep, which a flip-model chain insists on.</param>
    /// <param name="width">The new width.</param>
    /// <param name="height">The new height.</param>
    /// <returns>The <c>HRESULT</c>.</returns>
    internal static int ResizeBuffers(IntPtr swapChain, uint bufferCount, int width, int height)
    {
        var resize = (delegate* unmanaged[Stdcall]<IntPtr, uint, uint, uint, int, uint, int>)(*(IntPtr**)swapChain)[SlotSwapChainResizeBuffers];
        return resize(swapChain, bufferCount, (uint)width, (uint)height, FormatBgra8Unorm, 0);
    }

    /// <summary>Gets the buffer a flip-model swap chain wants drawn into next.</summary>
    internal static int GetCurrentBackBufferIndex(IntPtr swapChain)
    {
        var get = (delegate* unmanaged[Stdcall]<IntPtr, uint>)(*(IntPtr**)swapChain)[SlotSwapChain3GetCurrentBackBufferIndex];
        return (int)get(swapChain);
    }

    /// <summary>Gets one of the swap chain's buffers, which is the texture Skia draws into.</summary>
    /// <returns>The <c>ID3D12Resource</c>, or zero.</returns>
    internal static IntPtr GetBuffer(IntPtr swapChain, int index)
    {
        var get = (delegate* unmanaged[Stdcall]<IntPtr, uint, Guid*, IntPtr*, int>)(*(IntPtr**)swapChain)[SlotSwapChainGetBuffer];
        Guid interfaceId = ResourceInterface;
        IntPtr resource = IntPtr.Zero;
        return get(swapChain, (uint)index, &interfaceId, &resource) == 0 ? resource : IntPtr.Zero;
    }

    /// <summary>Presents the current buffer, waiting for the display.</summary>
    internal static int Present(IntPtr swapChain) =>
        ((delegate* unmanaged[Stdcall]<IntPtr, uint, uint, int>)(*(IntPtr**)swapChain)[SlotSwapChainPresent])(swapChain, 1, 0);

    /// <summary><c>IID_IDXGIAdapter3</c>: the interface the memory query is answered on.</summary>
    internal static readonly Guid Adapter3Interface = new("645967A4-1392-4310-A798-8053CE3E93FD");

    /// <summary><c>IID_ID3D12Resource</c>.</summary>
    internal static readonly Guid ResourceInterface = new("696442BE-A72E-4059-BC79-5B5C98040FAD");

    /// <summary>
    /// True when an adapter draws on the CPU. WARP usually sets the software flag, but the Basic Render
    /// Driver Windows exposes on a machine with no GPU reports no flags at all, so the description is
    /// checked as well: a tier that answers every call and then rasterizes in software is slower than
    /// the DIB path the window would otherwise keep.
    /// </summary>
    /// <param name="flags">The adapter's <c>DXGI_ADAPTER_DESC1.Flags</c>.</param>
    /// <param name="renderer">The adapter's description.</param>
    internal static bool IsSoftwareAdapter(uint flags, string renderer) =>
        !AllowSoftwareAdapter
        && ((flags & AdapterFlagSoftware) != 0
            || renderer.StartsWith("Microsoft Basic Render", StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Lets a software adapter through, which is a diagnostic and not a preference: WARP answers every
    /// call and rasterizes on the CPU, so it is slower than the DIB path the tier exists to beat. It is
    /// how the tier is exercised on a machine with no GPU - the tests set it - and
    /// <c>KOLVIR_D3D12_ALLOW_SOFTWARE=1</c> turns it on for a window that has to prove a frame.
    /// </summary>
    internal static bool AllowSoftwareAdapter { get; set; } =
        Environment.GetEnvironmentVariable("KOLVIR_D3D12_ALLOW_SOFTWARE") == "1";

    /// <summary><c>DXGI_QUERY_VIDEO_MEMORY_INFO</c>: what this process has in the segment.</summary>
    internal struct DxgiQueryVideoMemoryInfo
    {
        public ulong Budget;
        public ulong CurrentUsage;
        public ulong AvailableForReservation;
        public ulong CurrentReservation;
    }

    /// <summary>
    /// Reads this process's usage of one memory segment. <c>CurrentUsage</c> is the process's own, which
    /// is what makes this the number to print when a window's dedicated memory is being questioned:
    /// task manager adds the driver's and the compositor's overhead to it.
    /// </summary>
    /// <param name="adapter">The adapter.</param>
    /// <param name="segment">0 for local, 1 for non-local.</param>
    /// <param name="info">The usage and the budget.</param>
    /// <returns>The <c>HRESULT</c>; a software adapter may have no budget to report.</returns>
    internal static int GetVideoMemoryInfo(IntPtr adapter, int segment, out DxgiQueryVideoMemoryInfo info)
    {
        // NodeIndex comes first: the call is (node, segment, info), and a two-argument call lands on
        // the routine before it in the table, which answers S_OK and writes nothing.
        var query = (delegate* unmanaged[Stdcall]<IntPtr, uint, int, DxgiQueryVideoMemoryInfo*, int>)(*(IntPtr**)adapter)[SlotAdapter3QueryVideoMemoryInfo];
        DxgiQueryVideoMemoryInfo local = default;
        int result = query(adapter, 0, segment, &local);
        info = local;
        return result;
    }

    /// <summary>Reads an <c>DXGI_ADAPTER_DESC1</c>'s description into a managed string.</summary>
    internal static string Describe(DxgiAdapterDesc1 description)
    {
        // The buffer is already fixed - it lives inside a struct that cannot move - so its address is
        // taken directly rather than through a fixed statement, which the compiler refuses here.
        char* text = description.Description;
        var length = 0;

        while (length < 128 && text[length] != '\0')
        {
            length++;
        }

        return length == 0 ? "unknown adapter" : new string(text, 0, length);
    }
}
