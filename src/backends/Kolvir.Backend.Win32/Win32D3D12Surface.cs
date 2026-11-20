using SkiaSharp;
using static Kolvir.Backend.Win32.D3D12;

namespace Kolvir.Backend.Win32;

/// <summary>Creates the Direct3D 12 device stack for a window, or says why it cannot.</summary>
internal interface IWin32D3D12Probe
{
    /// <summary>Builds the device stack, or returns null with the step that failed.</summary>
    Win32D3D12Device? TryCreate(IntPtr hwnd, out string failure);
}

/// <summary>The production probe: the real DXGI factory, adapter, device and command queue.</summary>
internal sealed class Win32D3D12Probe : IWin32D3D12Probe
{
    private Win32D3D12Probe()
    {
    }

    internal static Win32D3D12Probe Instance { get; } = new();

    public Win32D3D12Device? TryCreate(IntPtr hwnd, out string failure) =>
        Win32D3D12Device.TryCreate(hwnd, out failure);
}

/// <summary>
/// The Direct3D objects that live as long as the window does: the device and the direct command queue
/// Skia submits through. The swap chain does not live here - it belongs to the surface, because it is
/// the thing that changes size - so this type stays out of the drawing path.
/// </summary>
internal sealed unsafe class Win32D3D12Device : IDisposable
{
    private Win32D3D12Device(IntPtr adapter, IntPtr device, IntPtr queue, string renderer)
    {
        _adapter = adapter;
        _device = device;
        _queue = queue;
        Renderer = renderer;
    }

    private IntPtr _adapter;
    private IntPtr _device;
    private IntPtr _queue;

    /// <summary>
    /// The <c>IDXGIAdapter1</c> the device was made on. Skia reads it for the adapter's LUID, so it
    /// has to outlive the context rather than being released once the device exists.
    /// </summary>
    internal IntPtr Adapter => _adapter;

    /// <summary>The <c>ID3D12Device</c>, which Skia wraps but does not own.</summary>
    internal IntPtr Device => _device;

    /// <summary>The <c>ID3D12CommandQueue</c> Skia submits and the swap chain presents from.</summary>
    internal IntPtr Queue => _queue;

    /// <summary>The adapter's description, for the window's diagnostic line.</summary>
    internal string Renderer { get; }

    /// <summary>
    /// Picks an adapter, creates a device for it and one direct queue.
    /// </summary>
    /// <remarks>
    /// The adapter is the one that drives the display the window is on, not simply the first one
    /// enumerated. A flip-model swap chain cannot cross adapters - DXGI refuses it with
    /// <c>DXGI_ERROR_INVALID_CALL</c>, which is what a hybrid machine produces when the window is on
    /// the integrated GPU's output and the device was made on the discrete card. Software adapters are
    /// refused as well: WARP answers every call successfully and then draws on the CPU, which is
    /// slower than the DIB path this tier exists to beat.
    /// </remarks>
    internal static Win32D3D12Device? TryCreate(IntPtr hwnd, out string failure)
    {
        if (!IsAvailable)
        {
            failure = "d3d12: no d3d12/dxgi";
            return null;
        }

        IntPtr factory = CreateFactory1(out var factoryResult);

        if (factory == IntPtr.Zero)
        {
            failure = $"d3d12: no dxgi factory (0x{factoryResult:X8})";
            return null;
        }

        try
        {
            if (!TryPickAdapter(factory, hwnd, out var adapter, out var description, out failure))
            {
                return null;
            }

            try
            {
                IntPtr device = CreateDevice(adapter, out var deviceResult);

                if (device == IntPtr.Zero)
                {
                    failure = $"d3d12: no device (0x{deviceResult:X8})";
                    return null;
                }

                IntPtr queue = CreateCommandQueue(device, out var queueResult);

                if (queue == IntPtr.Zero)
                {
                    Release(ref device);
                    failure = $"d3d12: no command queue (0x{queueResult:X8})";
                    return null;
                }

                failure = string.Empty;
                return new Win32D3D12Device(adapter, device, queue, Describe(description));
            }
            catch
            {
                Release(ref adapter);
                throw;
            }
        }
        finally
        {
            Release(ref factory);
        }
    }

    private static bool TryPickAdapter(
        IntPtr factory,
        IntPtr hwnd,
        out IntPtr adapter,
        out DxgiAdapterDesc1 description,
        out string failure)
    {
        adapter = IntPtr.Zero;
        description = default;
        failure = "d3d12: no hardware adapter";

        List<(IntPtr Adapter, DxgiAdapterDesc1 Description)> candidates = [];

        try
        {
            for (uint index = 0; index < 16; index++)
            {
                IntPtr candidate = EnumAdapters1(factory, index);

                if (candidate == IntPtr.Zero)
                {
                    break;
                }

                if (!GetAdapterDesc1(candidate, out var candidateDescription) ||
                    IsSoftwareAdapter(candidateDescription.Flags, Describe(candidateDescription)))
                {
                    Release(ref candidate);
                    continue;
                }

                candidates.Add((candidate, candidateDescription));
            }

            if (candidates.Count == 0)
            {
                return false;
            }

            int chosen = ChooseAdapterIndex(candidates.Select(candidate => DrivesWindow(candidate.Adapter, hwnd)).ToArray());
            (adapter, description) = candidates[chosen];

            if (Win32GlTrace.IsEnabled)
            {
                for (var index = 0; index < candidates.Count; index++)
                {
                    Win32GlTrace.Write($"kolvir-d3d12: adapter {index}: {Describe(candidates[index].Description)}"
                        + (index == chosen ? " (chosen)" : string.Empty));
                }
            }

            candidates.RemoveAt(chosen);
            return true;
        }
        finally
        {
            // Everything not handed back is released here: the chosen adapter is in `adapter` by now.
            foreach (var (candidate, _) in candidates)
            {
                IntPtr released = candidate;
                Release(ref released);
            }
        }
    }

    /// <summary>
    /// Which candidate to use: the first one that drives the display the window is on, and otherwise
    /// the first one at all - a window on a display no enumerated adapter claims is a machine whose
    /// adapter list this code cannot reason about, and trying the first is what it did before.
    /// </summary>
    /// <param name="drivesWindow">One flag per hardware adapter, in enumeration order.</param>
    /// <remarks>
    /// The choice is a pure function of the flags so that a machine with the window on a different
    /// adapter than the first one can be tested without such a machine.
    /// </remarks>
    internal static int ChooseAdapterIndex(IReadOnlyList<bool> drivesWindow)
    {
        for (var index = 0; index < drivesWindow.Count; index++)
        {
            if (drivesWindow[index])
            {
                return index;
            }
        }

        return 0;
    }

    private static bool DrivesWindow(IntPtr adapter, IntPtr hwnd)
    {
        if (!WindowCentre(hwnd, out var centre))
        {
            return false;
        }

        return AdapterDrivesPoint(adapter, centre.X, centre.Y);
    }

    private static bool AdapterDrivesPoint(IntPtr adapter, int x, int y)
    {
        for (uint index = 0; index < 8; index++)
        {
            IntPtr output = EnumOutputs(adapter, index);

            if (output == IntPtr.Zero)
            {
                return false;
            }

            bool drives = GetOutputDesc(output, out var description) && Contains(description, x, y);
            IntPtr released = output;
            Release(ref released);

            if (drives)
            {
                return true;
            }
        }

        return false;
    }

    private static bool WindowCentre(IntPtr hwnd, out (int X, int Y) centre)
    {
        centre = default;

        if (!TryGetWindowRect(hwnd, out var rect) || rect.Right <= rect.Left || rect.Bottom <= rect.Top)
        {
            return false;
        }

        centre = ((rect.Left + rect.Right) / 2, (rect.Top + rect.Bottom) / 2);
        return true;
    }

    public void Dispose()
    {
        // The queue goes first: it holds the device alive while any of its work is outstanding.
        Release(ref _queue);
        Release(ref _device);
        Release(ref _adapter);
    }
}

/// <summary>
/// The Direct3D 12 tier: a flip-model swap chain per window and a Skia context built on the device and
/// queue that outlive it.
/// </summary>
/// <remarks>
/// <para>
/// It answers the interface the way the Vulkan tier does, and for the same reason: Direct3D 12 has no
/// default framebuffer to wrap either, so the window paints into a retained GPU texture that this tier
/// draws into whichever swap chain buffer DXGI hands out. The host cannot tell which of the two it is
/// in front of.
/// </para>
/// <para>
/// Resizing is <c>ResizeBuffers</c> on the same swap chain, which is why this tier leads with the
/// Vulkan one over GL: the buffers are released and reallocated rather than a drawable being kept per
/// size the window has ever had.
/// </para>
/// </remarks>
internal sealed unsafe class Win32D3D12Surface : IWin32GpuSurface
{
    private Win32D3D12Surface(Win32D3D12Device device, IntPtr factory, IntPtr swapChain, uint bufferCount, int width, int height)
    {
        _device = device;
        _factory = factory;
        _swapChain = swapChain;
        _bufferCount = bufferCount;
        _width = width;
        _height = height;
    }

    /// <summary><c>DXGI_MEMORY_SEGMENT_GROUP_LOCAL</c>: the memory the adapter owns.</summary>
    private const int MemorySegmentLocal = 0;

    private readonly Win32D3D12Device _device;
    private readonly uint _bufferCount;
    private IntPtr _factory;
    private IntPtr _swapChain;
    private int _width;
    private int _height;
    private GRContext? _skia;
    private SKSurface? _retained;
    private bool _needsFull = true;
    private bool _memoryTraced;

    public SKSurface? Surface => _retained;

    public bool HasRetainedBuffer => _retained is not null;

    public bool NeedsFullRepaint => _needsFull;

    public string Tier => "d3d12";

    public string Renderer => _device.Renderer;

    public bool MakeCurrent() => true;

    public bool TakeNeedsFull()
    {
        bool needed = _needsFull;
        _needsFull = false;
        return needed;
    }

    public void DropRetainedBuffer()
    {
        _retained?.Dispose();
        _retained = null;
        _needsFull = true;
    }

    /// <summary>Builds the whole stack or nothing: swap chain, Skia context and retained surface.</summary>
    internal static Win32D3D12Surface? TryCreate(
        IntPtr hwnd,
        IWin32D3D12Probe probe,
        int width,
        int height,
        out string failure)
    {
        var device = probe.TryCreate(hwnd, out failure);

        if (device is null)
        {
            return null;
        }

        if (!CreateSwapChain(device, hwnd, width, height, out var factory, out var swapChain, out var bufferCount, out failure))
        {
            device.Dispose();
            return null;
        }

        var surface = new Win32D3D12Surface(device, factory, swapChain, bufferCount, width, height);

        surface._skia = GRContext.CreateDirect3D(new GRD3DBackendContext
        {
            Adapter = device.Adapter,
            Device = device.Device,
            Queue = device.Queue,
        });

        if (surface._skia is null)
        {
            failure = "d3d12: no skia context";
            surface.Dispose();
            return null;
        }

        // Skia hands a Direct3D context a 256 MiB resource cache by default, and that cache is where
        // the frame's upload buffers and the wrappers for the back buffers live, so it decides how
        // much dedicated memory the tier costs. A window composites one image a frame: three
        // full-size surfaces, with a 64 MiB floor for the smaller ones, is what it needs.
        long surfaceBytes = (long)width * height * 4;
        surface._skia.SetResourceCacheLimit(Math.Max(64L * 1024 * 1024, surfaceBytes * 3));

        if (!surface.CreateRetained())
        {
            failure = "d3d12: no retained surface";
            surface.Dispose();
            return null;
        }

        if (Win32GlTrace.IsEnabled)
        {
            // What the context is holding, and what it is allowed to hold: a Direct3D backend keeps
            // upload buffers and the wrappers it made for the back buffers, so this is the number to
            // read when a window's dedicated memory looks larger than its surfaces explain.
            surface._skia.GetResourceCacheUsage(out int resources, out long cached);
            Win32GlTrace.Write($"kolvir-d3d12: skia cache {resources} resources, {cached / (1024 * 1024)} MiB of {surface._skia.GetResourceCacheLimit() / (1024 * 1024)} MiB");
            surface.TraceVideoMemory("with the surface built");
        }

        failure = string.Empty;
        return surface;
    }

    private static bool CreateSwapChain(
        Win32D3D12Device device,
        IntPtr hwnd,
        int width,
        int height,
        out IntPtr factory,
        out IntPtr swapChain,
        out uint bufferCount,
        out string failure)
    {
        swapChain = IntPtr.Zero;
        bufferCount = 0;
        factory = CreateFactory1(out var factoryResult);

        if (factory == IntPtr.Zero)
        {
            failure = $"d3d12: no dxgi factory (0x{factoryResult:X8})";
            return false;
        }

        if (width <= 0 || height <= 0)
        {
            failure = "d3d12: window has no client area";
            Release(ref factory);
            return false;
        }

        // Both rungs are flip models: Direct3D 12 will not present through a bitblt swap chain, which
        // DXGI refuses outright. The order is newest first, because the discard model is the one
        // Windows composites with and the sequential model is only there for a driver that refuses it.
        (int Effect, uint Buffers, string Name)[] ladder =
        [
            (SwapEffectFlipDiscard, 2, "flip-discard"),
            (SwapEffectFlipSequential, 2, "flip-sequential"),
        ];

        List<string> refusals = [];

        foreach (var (effect, buffers, name) in ladder)
        {
            int result = 0;
            swapChain = CreateSwapChainForHwnd(factory, device.Queue, hwnd, width, height, effect, buffers, out result);

            if (swapChain != IntPtr.Zero)
            {
                bufferCount = buffers;
                if (refusals.Count > 0 && Win32GlTrace.IsEnabled)
                {
                    Win32GlTrace.Write($"kolvir-d3d12: swap chain: {name} (after {string.Join(", ", refusals)})");
                }

                failure = string.Empty;
                return true;
            }

            refusals.Add($"{name} 0x{result:X8}");
        }

        // Naming the adapter matters: a flip-model swap chain cannot cross adapters, and the name says
        // which one the device was made on when every effect was refused on it.
        failure = $"d3d12: no swap chain on {device.Renderer} ({string.Join(", ", refusals)})";
        Release(ref factory);
        return false;
    }

    /// <summary>
    /// Reports this process's own use of the adapter's local memory. It is printed once the surface
    /// exists and again after the first frame, because the difference between the two is what Skia's
    /// first submission costs, and because the figure task manager shows has the driver's and the
    /// compositor's overhead added to it.
    /// </summary>
    /// <param name="where">The moment the figure was taken.</param>
    private void TraceVideoMemory(string where)
    {
        if (!Win32GlTrace.IsEnabled)
        {
            return;
        }

        if (D3D12.GetVideoMemoryInfo(_device.Adapter, MemorySegmentLocal, out var info) < 0 || info.Budget == 0)
        {
            return;
        }

        Win32GlTrace.Write($"kolvir-d3d12: video memory {info.CurrentUsage / (1024 * 1024)} MiB local in use of {info.Budget / (1024 * 1024)} MiB, {where}");
    }

    private bool CreateRetained()
    {
        _retained?.Dispose();
        _retained = null;

        if (_skia is null || _width <= 0 || _height <= 0)
        {
            return false;
        }

        // Unbudgeted on purpose, for the same reason the Vulkan tier keeps its retained texture that
        // way: this texture is the frame, so it must not be discarded under memory pressure.
        _retained = SKSurface.Create(_skia, false, new SKImageInfo(_width, _height, SKColorType.Bgra8888, SKAlphaType.Premul), 1, GRSurfaceOrigin.TopLeft);
        _needsFull = true;
        return _retained is not null;
    }

    public bool Resize(int width, int height)
    {
        if (width <= 0 || height <= 0)
        {
            return false;
        }

        if (_skia is not null && _retained is not null && _width == width && _height == height)
        {
            _needsFull = true;
            return true;
        }

        // Every reference to a back buffer has to be gone before DXGI will resize, and the queue has to
        // have finished with them, so the work is drained here rather than left running behind a
        // resize that would fail with DXGI_ERROR_INVALID_CALL. The cache is emptied after the drain for
        // the same reason: a wrapper Skia kept from a frame counts as a live reference on its buffer.
        DropRetainedBuffer();
        _skia?.Flush();
        _skia?.Submit(synchronous: true);
        _skia?.PurgeResources();

        int result = ResizeBuffers(_swapChain, _bufferCount, width, height);

        if (result < 0)
        {
            Win32GlTrace.Write($"kolvir-d3d12: resize to {width}x{height} refused (0x{result:X8})");
            return false;
        }

        _width = width;
        _height = height;
        return CreateRetained();
    }

    public Win32PresentResult Present()
    {
        if (_skia is null || _retained is null || _swapChain == IntPtr.Zero)
        {
            return Win32PresentResult.RetryDirect;
        }

        int index = GetCurrentBackBufferIndex(_swapChain);
        IntPtr resource = GetBuffer(_swapChain, index);

        if (resource == IntPtr.Zero)
        {
            return Win32PresentResult.Lost;
        }

        try
        {
            // PRESENT, not RENDER_TARGET: DXGI hands the buffer back in the state it was presented in,
            // and Skia transitions it itself for the duration of the drawing it is asked to do. The
            // wrapper is disposed as soon as the surface has been made from it, because it holds a
            // reference on the buffer and DXGI will not reallocate a chain that still has one.
            using var texture = new GRBackendTexture(_width, _height, new GRD3DTextureResourceInfo
            {
                Resource = resource,
                ResourceState = ResourceStatePresent,
                Format = (uint)FormatBgra8Unorm,
                SampleCount = 1,
                LevelCount = 1,
            });

            using var frame = SKSurface.Create(_skia, texture, GRSurfaceOrigin.TopLeft, 1, SKColorType.Bgra8888);

            if (frame is null)
            {
                return Win32PresentResult.RetryDirect;
            }

            using (var snapshot = _retained.Snapshot())
            {
                if (snapshot is not null)
                {
                    frame.Canvas.DrawImage(snapshot, 0f, 0f, new SKSamplingOptions(SKFilterMode.Nearest), null);
                }
            }

            // Submitted rather than waited on: the CPU's job is to record the next frame, and the
            // present call is where DXGI applies the pacing.
            _skia.Flush();
            _skia.Submit(synchronous: false);
        }
        finally
        {
            Release(ref resource);
        }

        int result = D3D12.Present(_swapChain);
        // DXGI_STATUS_OCCLUDED is a success code for a hidden window, so it is checked before the
        // general success case rather than being left to the catch-all.
        if (result == StatusOccluded || result >= 0)
        {
            if (!_memoryTraced)
            {
                _memoryTraced = true;
                TraceVideoMemory("after the first frame");
            }

            return Win32PresentResult.Presented;
        }

        return Win32PresentResult.Lost;
    }

    public void Dispose()
    {
        // Same order as a resize, and for the same reason: the buffers cannot go while a frame is
        // still referencing them, and Skia's context holds the ones it drew into.
        DropRetainedBuffer();
        _skia?.Flush();
        _skia?.Submit(synchronous: true);
        _skia?.Dispose();
        _skia = null;
        Release(ref _swapChain);
        Release(ref _factory);
        _device.Dispose();
    }
}
