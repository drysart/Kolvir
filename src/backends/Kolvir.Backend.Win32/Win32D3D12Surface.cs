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
        Win32D3D12Device.TryCreate(out failure);
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
    /// Picks a hardware adapter, creates a device for it and one direct queue. A software adapter is
    /// refused: WARP would answer every call successfully and then draw on the CPU, which is slower
    /// than the DIB path this tier exists to beat.
    /// </summary>
    internal static Win32D3D12Device? TryCreate(out string failure)
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
            if (!TryPickAdapter(factory, out var adapter, out var description, out failure))
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
        out IntPtr adapter,
        out DxgiAdapterDesc1 description,
        out string failure)
    {
        adapter = IntPtr.Zero;
        description = default;
        failure = "d3d12: no hardware adapter";

        // The first adapter that is not software: cards are enumerated ahead of WARP, and a machine
        // whose only adapter is software has nothing this tier can use.
        for (uint index = 0; index < 8; index++)
        {
            IntPtr candidate = EnumAdapters1(factory, index);

            if (candidate == IntPtr.Zero)
            {
                return false;
            }

            if (!GetAdapterDesc1(candidate, out var candidateDescription))
            {
                Release(ref candidate);
                continue;
            }

            if (IsSoftwareAdapter(candidateDescription.Flags, Describe(candidateDescription)))
            {
                Release(ref candidate);
                continue;
            }

            adapter = candidate;
            description = candidateDescription;
            failure = string.Empty;
            return true;
        }

        return false;
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
    private Win32D3D12Surface(Win32D3D12Device device, IntPtr factory, IntPtr swapChain, int width, int height)
    {
        _device = device;
        _factory = factory;
        _swapChain = swapChain;
        _width = width;
        _height = height;
    }

    private readonly Win32D3D12Device _device;
    private IntPtr _factory;
    private IntPtr _swapChain;
    private int _width;
    private int _height;
    private GRContext? _skia;
    private SKSurface? _retained;
    private bool _needsFull = true;

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

        if (!CreateSwapChain(device, hwnd, width, height, out var factory, out var swapChain, out failure))
        {
            device.Dispose();
            return null;
        }

        var surface = new Win32D3D12Surface(device, factory, swapChain, width, height);

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

        if (!surface.CreateRetained())
        {
            failure = "d3d12: no retained surface";
            surface.Dispose();
            return null;
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
        out string failure)
    {
        swapChain = IntPtr.Zero;
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

        swapChain = CreateSwapChainForHwnd(factory, device.Queue, hwnd, width, height, out var result);

        if (swapChain == IntPtr.Zero)
        {
            failure = $"d3d12: no swap chain (0x{result:X8})";
            Release(ref factory);
            return false;
        }

        failure = string.Empty;
        return true;
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
        // resize that would fail with DXGI_ERROR_INVALID_CALL.
        DropRetainedBuffer();
        _skia?.Flush();
        _skia?.Submit(synchronous: true);

        int result = ResizeBuffers(_swapChain, width, height);

        if (result < 0)
        {
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
            // and Skia transitions it itself for the duration of the drawing it is asked to do.
            var texture = new GRBackendTexture(_width, _height, new GRD3DTextureResourceInfo
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
