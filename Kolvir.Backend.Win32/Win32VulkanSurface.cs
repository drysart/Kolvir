using SkiaSharp;
using static Kolvir.Backend.Win32.Vulkan;

namespace Kolvir.Backend.Win32;

/// <summary>Creates the Vulkan device stack for a window, or says why it cannot.</summary>
internal interface IWin32VulkanProbe
{
    /// <summary>Builds the device stack, or returns null with the step that failed.</summary>
    Win32VulkanDevice? TryCreate(IntPtr hwnd, out string failure);
}

/// <summary>The production probe: the real loader, instance, surface and device.</summary>
internal sealed class Win32VulkanProbe : IWin32VulkanProbe
{
    private Win32VulkanProbe()
    {
    }

    internal static Win32VulkanProbe Instance { get; } = new();

    public Win32VulkanDevice? TryCreate(IntPtr hwnd, out string failure) =>
        Win32VulkanDevice.TryCreate(hwnd, out failure);
}

/// <summary>
/// The Vulkan objects that live as long as the window does: loader, instance, the window's surface,
/// the device, its queue and a fence for acquire. The swap chain does not live here - it is rebuilt
/// on every resize - so this type stays out of the drawing path.
/// </summary>
internal sealed unsafe class Win32VulkanDevice : IDisposable
{
    private Win32VulkanDevice(
        VulkanLoader loader,
        IntPtr instance,
        IntPtr physical,
        IntPtr device,
        IntPtr queue,
        IntPtr surface,
        IntPtr fence,
        IntPtr renderSemaphore,
        uint family,
        string renderer)
    {
        _loader = loader;
        Instance = instance;
        Physical = physical;
        Device = device;
        Queue = queue;
        Surface = surface;
        Fence = fence;
        RenderSemaphore = renderSemaphore;
        Family = family;
        Renderer = renderer;
    }

    private readonly VulkanLoader _loader;

    internal IntPtr Instance { get; }

    internal IntPtr Physical { get; }

    internal IntPtr Device { get; }

    internal IntPtr Queue { get; }

    internal IntPtr Surface { get; }

    internal IntPtr Fence { get; }

    /// <summary>
    /// Signalled by the barrier submit and waited on by the present, so the image is in
    /// <c>PRESENT_SRC</c> before the display reads it without the CPU waiting to find out.
    /// </summary>
    internal IntPtr RenderSemaphore { get; }

    internal uint Family { get; }

    /// <summary>The adapter's name, so a screenshot says which GPU produced its numbers.</summary>
    internal string Renderer { get; }

    internal IntPtr InstanceProc(string name) => _loader.InstanceProc(Instance, name);

    internal IntPtr DeviceProc(string name) => _loader.DeviceProc(Device, Instance, name);

    internal static Win32VulkanDevice? TryCreate(IntPtr hwnd, out string failure)
    {
        var loader = VulkanLoader.TryLoad();

        if (loader is null)
        {
            failure = "no vulkan-1.dll";
            return null;
        }

        var getInstanceProc = loader.InstanceProc(IntPtr.Zero, "vkCreateInstance");

        if (getInstanceProc == IntPtr.Zero)
        {
            failure = "no vkCreateInstance";
            return null;
        }

        // The surface extensions are the only ones needed to describe a window to Vulkan.
        var applicationName = System.Runtime.InteropServices.Marshal.StringToCoTaskMemUTF8("kolvir");
        var engineName = System.Runtime.InteropServices.Marshal.StringToCoTaskMemUTF8("kolvir");
        var surfaceExtension = System.Runtime.InteropServices.Marshal.StringToCoTaskMemUTF8("VK_KHR_surface");
        var win32Extension = System.Runtime.InteropServices.Marshal.StringToCoTaskMemUTF8("VK_KHR_win32_surface");
        nint[] extensions = [surfaceExtension, win32Extension];
        IntPtr instance = IntPtr.Zero;

        fixed (nint* extensionPtrs = extensions)
        {
            var application = new VkApplicationInfo
            {
                sType = StructureTypeApplicationInfo,
                pApplicationName = applicationName,
                applicationVersion = 1,
                pEngineName = engineName,
                engineVersion = 1,
                apiVersion = (1u << 22) | (1u << 12),
            };

            var createInfo = new VkInstanceCreateInfo
            {
                sType = StructureTypeInstanceCreateInfo,
                pApplicationInfo = (IntPtr)(&application),
                enabledExtensionCount = 2,
                ppEnabledExtensionNames = (IntPtr)extensionPtrs,
            };

            var create = (delegate* unmanaged<VkInstanceCreateInfo*, IntPtr, IntPtr*, int>)getInstanceProc;
            int result = create(&createInfo, IntPtr.Zero, &instance);

            if (result == ErrorIncompatibleDriver)
            {
                failure = $"vulkan: no driver installed ({result})";
                return null;
            }

            if (result != 0)
            {
                failure = $"vulkan: vkCreateInstance {result}";
                return null;
            }
        }

        var createSurface = (delegate* unmanaged<IntPtr, VkWin32SurfaceCreateInfoKhr*, IntPtr, IntPtr*, int>)loader.InstanceProc(instance, "vkCreateWin32SurfaceKHR");

        if (createSurface == null)
        {
            failure = "vulkan: no vkCreateWin32SurfaceKHR";
            DestroyInstance(loader, instance);
            return null;
        }

        var surfaceInfo = new VkWin32SurfaceCreateInfoKhr
        {
            sType = StructureTypeWin32SurfaceCreateInfoKhr,
            hinstance = NativeMethods.GetModuleHandleW(null),
            hwnd = hwnd,
        };

        IntPtr surface = IntPtr.Zero;

        if (createSurface(instance, &surfaceInfo, IntPtr.Zero, &surface) != 0)
        {
            failure = "vulkan: no window surface";
            DestroyInstance(loader, instance);
            return null;
        }

        if (!SelectPhysicalDevice(loader, instance, surface, out var physical, out var family, out var renderer, out failure))
        {
            DestroyInstance(loader, instance);
            return null;
        }

        var createDevice = (delegate* unmanaged<IntPtr, VkDeviceCreateInfo*, IntPtr, IntPtr*, int>)loader.InstanceProc(instance, "vkCreateDevice");
        var swapChainExtension = System.Runtime.InteropServices.Marshal.StringToCoTaskMemUTF8("VK_KHR_swapchain");
        nint[] deviceExtensions = [swapChainExtension];
        float priority = 1f;
        IntPtr device = IntPtr.Zero;

        var queueInfo = new VkDeviceQueueCreateInfo
        {
            sType = StructureTypeDeviceQueueCreateInfo,
            queueFamilyIndex = family,
            queueCount = 1,
            pQueuePriorities = (IntPtr)(&priority),
        };

        fixed (nint* extensionPtrs = deviceExtensions)
        {
            var deviceInfo = new VkDeviceCreateInfo
            {
                sType = StructureTypeDeviceCreateInfo,
                queueCreateInfoCount = 1,
                pQueueCreateInfos = (IntPtr)(&queueInfo),
                enabledExtensionCount = 1,
                ppEnabledExtensionNames = (IntPtr)extensionPtrs,
            };

            if (createDevice(physical, &deviceInfo, IntPtr.Zero, &device) != 0)
            {
                failure = "vulkan: no device";
                DestroyInstance(loader, instance);
                return null;
            }
        }

        var getQueue = (delegate* unmanaged<IntPtr, uint, uint, IntPtr*, void>)loader.DeviceProc(device, instance, "vkGetDeviceQueue");
        IntPtr queue = IntPtr.Zero;
        getQueue(device, family, 0, &queue);

        var createFence = (delegate* unmanaged<IntPtr, VkFenceCreateInfo*, IntPtr, IntPtr*, int>)loader.DeviceProc(device, instance, "vkCreateFence");
        var fenceInfo = new VkFenceCreateInfo { sType = StructureTypeFenceCreateInfo };
        IntPtr fence = IntPtr.Zero;

        if (createFence(device, &fenceInfo, IntPtr.Zero, &fence) != 0)
        {
            failure = "vulkan: no fence";
            DestroyDevice(loader, device, instance);
            DestroyInstance(loader, instance);
            return null;
        }

        var createSemaphore = (delegate* unmanaged<IntPtr, VkSemaphoreCreateInfo*, IntPtr, IntPtr*, int>)loader.DeviceProc(device, instance, "vkCreateSemaphore");
        var semaphoreInfo = new VkSemaphoreCreateInfo { sType = StructureTypeSemaphoreCreateInfo };
        IntPtr renderSemaphore = IntPtr.Zero;

        if (createSemaphore == null || createSemaphore(device, &semaphoreInfo, IntPtr.Zero, &renderSemaphore) != 0)
        {
            failure = "vulkan: no render semaphore";
            DestroyDevice(loader, device, instance);
            DestroyInstance(loader, instance);
            return null;
        }

        failure = string.Empty;
        return new Win32VulkanDevice(loader, instance, physical, device, queue, surface, fence, renderSemaphore, family, renderer);
    }

    private static bool SelectPhysicalDevice(
        VulkanLoader loader,
        IntPtr instance,
        IntPtr surface,
        out IntPtr physical,
        out uint family,
        out string renderer,
        out string failure)
    {
        physical = IntPtr.Zero;
        family = 0;
        renderer = string.Empty;

        var enumerate = (delegate* unmanaged<IntPtr, uint*, IntPtr*, int>)loader.InstanceProc(instance, "vkEnumeratePhysicalDevices");
        uint count = 0;

        if (enumerate == null || enumerate(instance, &count, null) != 0 || count == 0)
        {
            failure = "vulkan: no physical devices";
            return false;
        }

        var devices = new IntPtr[count];

        fixed (IntPtr* buffer = devices)
        {
            enumerate(instance, &count, buffer);
        }

        var getProperties = (delegate* unmanaged<IntPtr, byte*, void>)loader.InstanceProc(instance, "vkGetPhysicalDeviceProperties");
        var getFamilies = (delegate* unmanaged<IntPtr, uint*, VkQueueFamilyProperties*, void>)loader.InstanceProc(instance, "vkGetPhysicalDeviceQueueFamilyProperties");
        var getSupport = (delegate* unmanaged<IntPtr, uint, IntPtr, uint*, int>)loader.InstanceProc(instance, "vkGetPhysicalDeviceSurfaceSupportKHR");

        // The full properties struct is around 824 bytes; only the name is wanted, so read it out of
        // a buffer big enough for the callee's write. It is allocated once outside the loop because
        // a stackalloc inside a loop is not recovered until the frame returns.
        byte* properties = stackalloc byte[1024];

        foreach (var device in devices)
        {
            getProperties(device, properties);

            uint familyCount = 0;
            getFamilies(device, &familyCount, null);

            if (familyCount == 0)
            {
                continue;
            }

            var families = new VkQueueFamilyProperties[familyCount];

            fixed (VkQueueFamilyProperties* familyPtrs = families)
            {
                getFamilies(device, &familyCount, familyPtrs);

                for (uint i = 0; i < familyCount; i++)
                {
                    if ((familyPtrs[i].queueFlags & QueueFlagGraphics) == 0)
                    {
                        continue;
                    }

                    uint supported = 0;

                    if (getSupport(device, i, surface, &supported) != 0 || supported == 0)
                    {
                        continue;
                    }

                    physical = device;
                    family = i;
                    renderer = new string((sbyte*)(properties + 20));
                    failure = string.Empty;
                    return true;
                }
            }
        }

        failure = "vulkan: no device can present to this window";
        return false;
    }

    private static void DestroyDevice(VulkanLoader loader, IntPtr device, IntPtr instance)
    {
        var destroy = (delegate* unmanaged<IntPtr, IntPtr, void>)loader.DeviceProc(device, instance, "vkDestroyDevice");

        if (destroy != null && device != IntPtr.Zero)
        {
            destroy(device, IntPtr.Zero);
        }
    }

    private static void DestroyInstance(VulkanLoader loader, IntPtr instance)
    {
        var destroy = (delegate* unmanaged<IntPtr, IntPtr, void>)loader.InstanceProc(instance, "vkDestroyInstance");

        if (destroy != null && instance != IntPtr.Zero)
        {
            destroy(instance, IntPtr.Zero);
        }
    }

    public void Dispose()
    {
        var waitIdle = (delegate* unmanaged<IntPtr, int>)DeviceProc("vkDeviceWaitIdle");

        if (waitIdle != null)
        {
            waitIdle(Device);
        }

        if (RenderSemaphore != IntPtr.Zero)
        {
            var destroySemaphore = (delegate* unmanaged<IntPtr, IntPtr, IntPtr, void>)DeviceProc("vkDestroySemaphore");

            if (destroySemaphore != null)
            {
                destroySemaphore(Device, RenderSemaphore, IntPtr.Zero);
            }
        }

        DestroyDevice(_loader, Device, Instance);
        DestroyInstance(_loader, Instance);
    }
}

/// <summary>
/// The Vulkan tier's drawing seam: a Skia context on the window's device, a retained GPU surface the
/// host paints damage into, and a present that draws that surface into the image the swap chain hands
/// out.
/// </summary>
/// <remarks>
/// There is no default framebuffer to wrap the way the GL tier does, so the frame the host paints is
/// a texture this type owns, and presenting is one blit into the acquired image. The swap chain is
/// destroyed and rebuilt on every size change, which is the whole point of this tier: the per-size
/// drawables that made a WGL window grow are a driver cache with no release path, and recreating the
/// swap chain is an explicit release.
/// </remarks>
internal sealed unsafe class Win32VulkanSurface : IWin32GpuSurface
{
    private Win32VulkanSurface(Win32VulkanDevice device, IntPtr swapChain, int format, uint usage, int width, int height)
    {
        _device = device;
        _swapChain = swapChain;
        _format = format;
        _usage = usage;
        _width = width;
        _height = height;
    }

    private readonly Win32VulkanDevice _device;
    private IntPtr _swapChain;
    private int _format;
    private uint _usage;
    private int _width;
    private int _height;
    private GRContext? _skia;
    private SKSurface? _retained;
    private IntPtr[] _barrierPools = [];
    private IntPtr[] _barrierBuffers = [];
    private IntPtr[] _swapChainImages = [];

    // Present timings, reported once a second when the trace is on. A frame that is late can be the
    // UI thread waiting for the display, the drawing, or the present call itself, and the three look
    // identical from outside - so they are measured separately rather than guessed at.
    private long _statsStarted = -1;
    private int _statsPresents;
    private long _statsAcquire;
    private long _statsWorstAcquire;
    private long _statsDraw;
    private long _statsWorstDraw;
    private long _statsPresent;
    private long _statsWorstPresent;
    private int _statsImages;
    private bool _needsFull = true;

    public SKSurface? Surface => _retained;

    public bool HasRetainedBuffer => _retained is not null;

    public bool NeedsFullRepaint => _needsFull;

    public string Tier => "vulkan";

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

    private SKColorType ColorType => _format is FormatRgba8Unorm or FormatRgba8Srgb ? SKColorType.Rgba8888 : SKColorType.Bgra8888;

    /// <summary>Builds the whole stack or nothing: swap chain, Skia context and retained surface.</summary>
    internal static Win32VulkanSurface? TryCreate(
        IntPtr hwnd,
        IWin32VulkanProbe probe,
        int width,
        int height,
        out string failure)
    {
        var device = probe.TryCreate(hwnd, out failure);

        if (device is null)
        {
            return null;
        }

        if (!CreateSwapChain(device, IntPtr.Zero, width, height, out var swapChain, out var format, out var usage, out var extent, out failure))
        {
            device.Dispose();
            return null;
        }

        var surface = new Win32VulkanSurface(device, swapChain, format, usage, extent.width, extent.height);

        surface._skia = GRContext.CreateVulkan(new GRVkBackendContext
        {
            VkInstance = device.Instance,
            VkPhysicalDevice = device.Physical,
            VkDevice = device.Device,
            VkQueue = device.Queue,
            GraphicsQueueIndex = device.Family,
            MaxAPIVersion = (1u << 22) | (1u << 12),
            GetProcedureAddress = surface.ResolveProcedure,
        });

        if (surface._skia is null)
        {
            failure = "vulkan: no skia context";
            surface.Dispose();
            return null;
        }

        if (!surface.RefreshImages() || !surface.CreateRetained())
        {
            failure = "vulkan: no swap chain images";
            surface.Dispose();
            return null;
        }

        failure = string.Empty;
        return surface;
    }

    /// <summary>
    /// The entry points Skia asks for, resolved the way the rest of this type does: device first,
    /// because every command it will call is a device command, and the instance only for what a
    /// device cannot answer.
    /// </summary>
    private IntPtr ResolveProcedure(string name, IntPtr instance, IntPtr device)
    {
        if (device != IntPtr.Zero)
        {
            var fromDevice = _device.DeviceProc(name);

            if (fromDevice != IntPtr.Zero)
            {
                return fromDevice;
            }
        }

        return _device.InstanceProc(name);
    }

    private bool CreateRetained()
    {
        _retained?.Dispose();
        _retained = null;

        if (_skia is null || _width <= 0 || _height <= 0)
        {
            return false;
        }

        // Unbudgeted on purpose: this texture is the frame, so it must not be discarded under memory
        // pressure the way a cache entry may be.
        _retained = SKSurface.Create(_skia, false, new SKImageInfo(_width, _height, ColorType, SKAlphaType.Premul), 1, GRSurfaceOrigin.TopLeft);
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
            return true;
        }

        // The old swap chain is handed back as the old one so the driver can retire it in place, and
        // destroyed afterwards; the images it owned are released with it. Nothing may still be in
        // flight while that happens, including presents this window queued: a swap chain is only
        // destroyable once its images are nobody's.
        if (_swapChain != IntPtr.Zero)
        {
            var waitIdle = (delegate* unmanaged<IntPtr, int>)_device.DeviceProc("vkDeviceWaitIdle");

            if (waitIdle != null)
            {
                waitIdle(_device.Device);
            }
        }

        if (!CreateSwapChain(_device, _swapChain, width, height, out var swapChain, out var format, out var usage, out var extent, out var failure))
        {
            Win32GlTrace.Write($"vulkan resize failed: {failure}");
            return false;
        }

        var previous = _swapChain;
        _swapChain = swapChain;

        if (previous != IntPtr.Zero)
        {
            ((delegate* unmanaged<IntPtr, IntPtr, IntPtr, void>)_device.DeviceProc("vkDestroySwapchainKHR"))(_device.Device, previous, IntPtr.Zero);
        }

        _format = format;
        _width = extent.width;
        _height = extent.height;
        _usage = usage;

        return RefreshImages() && CreateRetained();
    }

    private static bool CreateSwapChain(
        Win32VulkanDevice device,
        IntPtr oldChain,
        int width,
        int height,
        out IntPtr swapChain,
        out int format,
        out uint usage,
        out (int width, int height) extent,
        out string failure)
    {
        swapChain = IntPtr.Zero;
        format = FormatBgra8Unorm;
        usage = ImageUsageColorAttachment;
        extent = (width, height);

        var getCapabilities = (delegate* unmanaged<IntPtr, IntPtr, VkSurfaceCapabilitiesKhr*, int>)device.InstanceProc("vkGetPhysicalDeviceSurfaceCapabilitiesKHR");
        var capabilities = new VkSurfaceCapabilitiesKhr();

        if (getCapabilities == null || getCapabilities(device.Physical, device.Surface, &capabilities) != 0)
        {
            failure = "vulkan: no surface capabilities";
            return false;
        }

        // A defined current extent wins: it is the size the surface actually has, and a swap chain
        // built for anything else is immediately out of date.
        if (capabilities.currentExtent.width != uint.MaxValue && capabilities.currentExtent.width > 0 && capabilities.currentExtent.height > 0)
        {
            width = (int)capabilities.currentExtent.width;
            height = (int)capabilities.currentExtent.height;
        }

        if (width <= 0 || height <= 0)
        {
            failure = "vulkan: window has no client area";
            return false;
        }

        format = ChooseFormat(device);

        // COLOR_ATTACHMENT is what presenting needs, and Skia also inspects the usage flags of a
        // texture it wraps, so ask for the rest too and stay inside what this surface allows.
        var wanted = ImageUsageColorAttachment | ImageUsageTransferSrc | ImageUsageTransferDst | ImageUsageSampled;
        usage = wanted & capabilities.supportedUsageFlags;

        if ((usage & ImageUsageColorAttachment) == 0)
        {
            usage = ImageUsageColorAttachment;
        }

        var createInfo = new VkSwapchainCreateInfoKhr
        {
            sType = StructureTypeSwapchainCreateInfoKhr,
            surface = device.Surface,
            minImageCount = capabilities.maxImageCount > 0 && capabilities.minImageCount + 1 > capabilities.maxImageCount
                ? capabilities.maxImageCount
                : capabilities.minImageCount + 1,
            imageFormat = format,
            imageColorSpace = ColorSpaceSrgbNonLinear,
            imageExtent = new VkExtent2D { width = (uint)width, height = (uint)height },
            imageArrayLayers = 1,
            imageUsage = usage,
            imageSharingMode = SharingModeExclusive,
            preTransform = capabilities.currentTransform,
            compositeAlpha = CompositeAlphaOpaque,
            presentMode = PresentModeFifo,
            clipped = 1,
            oldSwapchain = oldChain,
        };

        var create = (delegate* unmanaged<IntPtr, VkSwapchainCreateInfoKhr*, IntPtr, IntPtr*, int>)device.DeviceProc("vkCreateSwapchainKHR");
        IntPtr chain = IntPtr.Zero;

        if (create == null || create(device.Device, &createInfo, IntPtr.Zero, &chain) != 0)
        {
            failure = "vulkan: no swap chain";
            return false;
        }

        swapChain = chain;
        extent = (width, height);
        failure = string.Empty;
        return true;
    }

    private static int ChooseFormat(Win32VulkanDevice device)
    {
        var getFormats = (delegate* unmanaged<IntPtr, IntPtr, uint*, VkSurfaceFormatKhr*, int>)device.InstanceProc("vkGetPhysicalDeviceSurfaceFormatsKHR");
        uint count = 0;

        if (getFormats == null || getFormats(device.Physical, device.Surface, &count, null) != 0 || count == 0)
        {
            return FormatBgra8Unorm;
        }

        var formats = new VkSurfaceFormatKhr[count];

        fixed (VkSurfaceFormatKhr* buffer = formats)
        {
            getFormats(device.Physical, device.Surface, &count, buffer);
        }

        foreach (var candidate in formats)
        {
            if ((candidate.format == FormatBgra8Unorm || candidate.format == FormatBgra8Srgb) &&
                candidate.colorSpace == ColorSpaceSrgbNonLinear)
            {
                return candidate.format;
            }
        }

        return formats[0].format;
    }

    public Win32PresentResult Present()
    {
        if (_skia is null || _retained is null)
        {
            return Win32PresentResult.RetryDirect;
        }

        var acquire = (delegate* unmanaged<IntPtr, IntPtr, ulong, IntPtr, IntPtr, uint*, int>)_device.DeviceProc("vkAcquireNextImageKHR");
        var waitFences = (delegate* unmanaged<IntPtr, uint, IntPtr*, uint, ulong, int>)_device.DeviceProc("vkWaitForFences");
        var resetFences = (delegate* unmanaged<IntPtr, uint, IntPtr*, int>)_device.DeviceProc("vkResetFences");
        var present = (delegate* unmanaged<IntPtr, VkPresentInfoKhr*, int>)_device.DeviceProc("vkQueuePresentKHR");

        // A device that does not answer for these cannot present, and asking it anyway is a null call.
        // The present is in this list because the barrier below signals a semaphore that only the
        // present waits on: submitting it with nothing to hand the frame to would leave the
        // semaphore signalled and the image in a layout nobody presented.
        if (acquire == null || waitFences == null || resetFences == null || present == null)
        {
            return Win32PresentResult.Lost;
        }

        uint index = 0;
        long started = System.Diagnostics.Stopwatch.GetTimestamp();
        int acquired = acquire(_device.Device, _swapChain, TimeoutInfinite, IntPtr.Zero, _device.Fence, &index);

        if (acquired == ErrorOutOfDateKhr)
        {
            return Resize(_width, _height) ? Win32PresentResult.RetryDirect : Win32PresentResult.Lost;
        }

        if (acquired < 0)
        {
            return Win32PresentResult.Lost;
        }

        IntPtr fence = _device.Fence;
        waitFences(_device.Device, 1, &fence, 1, TimeoutInfinite);
        resetFences(_device.Device, 1, &fence);
        long acquiredAt = System.Diagnostics.Stopwatch.GetTimestamp();

        uint count = (uint)_swapChainImages.Length;

        if (count == 0 && !RefreshImages())
        {
            return Win32PresentResult.Lost;
        }

        var images = _swapChainImages;

        if (index >= (uint)images.Length)
        {
            return Win32PresentResult.Lost;
        }

        // UNDEFINED, not PRESENT_SRC: the acquire handed the image to us and the retained frame covers
        // every pixel, so its previous contents do not matter. Skia refuses to wrap PRESENT_SRC.
        var backendTexture = new GRBackendTexture(_width, _height, new GRVkImageInfo
        {
            Image = (ulong)images[index],
            Alloc = new GRVkAlloc(),
            ImageTiling = ImageTilingOptimal,
            ImageLayout = ImageLayoutUndefined,
            Format = (uint)_format,
            ImageUsageFlags = _usage,
            SampleCount = 1,
            LevelCount = 1,
            CurrentQueueFamily = QueueFamilyIgnored,
            SharingMode = SharingModeExclusive,
        });

        using (var frame = SKSurface.Create(_skia, backendTexture, GRSurfaceOrigin.TopLeft, 1, ColorType))
        {
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

            // Submitted, not waited on: the barrier and the present are ordered behind this work on
            // the same queue, and the present's semaphore carries that ordering to the display.
            // Every wait the CPU does here is a frame the animation cannot spend drawing the next
            // one, and two of them per frame is what shows up as skipped animation frames.
            _skia!.Flush();
            _skia.Submit(synchronous: false);
        }

        long drawnAt = System.Diagnostics.Stopwatch.GetTimestamp();

        if (!EnsureBarrierRing(images.Length) || !MoveToPresentState(images[index], index))
        {
            return Win32PresentResult.Lost;
        }

        uint imageIndex = index;

        IntPtr chain = _swapChain;
        IntPtr renderSemaphore = _device.RenderSemaphore;
        var presentInfo = new VkPresentInfoKhr
        {
            sType = StructureTypePresentInfoKhr,
            waitSemaphoreCount = 1,
            pWaitSemaphores = (IntPtr)(&renderSemaphore),
            swapchainCount = 1,
            pSwapchains = (IntPtr)(&chain),
            pImageIndices = (IntPtr)(&imageIndex),
        };

        int presented = present(_device.Queue, &presentInfo);
        long presentedAt = System.Diagnostics.Stopwatch.GetTimestamp();

        ReportTimings(acquiredAt - started, drawnAt - acquiredAt, presentedAt - drawnAt, images.Length);

        if (presented == ErrorOutOfDateKhr)
        {
            return Resize(_width, _height) ? Win32PresentResult.RetryDirect : Win32PresentResult.Lost;
        }

        return presented == 0 || presented == SuboptimalKhr ? Win32PresentResult.Presented : Win32PresentResult.Lost;
    }

    /// <summary>
    /// Accumulates one present's timings and prints a line a second, so a late frame can be blamed on
    /// the display, the drawing or the present call rather than on whichever is easiest to suspect.
    /// </summary>
    private void ReportTimings(long acquireTicks, long drawTicks, long presentTicks, int imageCount)
    {
        if (!Win32GlTrace.IsEnabled)
        {
            return;
        }

        _statsPresents++;
        _statsAcquire += acquireTicks;
        _statsDraw += drawTicks;
        _statsPresent += presentTicks;
        _statsWorstAcquire = Math.Max(_statsWorstAcquire, acquireTicks);
        _statsWorstDraw = Math.Max(_statsWorstDraw, drawTicks);
        _statsWorstPresent = Math.Max(_statsWorstPresent, presentTicks);
        _statsImages = imageCount;

        long now = System.Diagnostics.Stopwatch.GetTimestamp();

        if (_statsStarted < 0)
        {
            _statsStarted = now;
            return;
        }

        double window = Milliseconds(now - _statsStarted);

        if (window < 1000.0)
        {
            return;
        }

        double presents = _statsPresents * 1000.0 / window;
        double count = Math.Max(1, _statsPresents);

        Win32GlTrace.Write(
            string.Create(
                System.Globalization.CultureInfo.InvariantCulture,
                $"vulkan {presents:F1} presents/s  acquire {Milliseconds(_statsAcquire) / count:F2}/{Milliseconds(_statsWorstAcquire):F2}ms  draw {Milliseconds(_statsDraw) / count:F2}/{Milliseconds(_statsWorstDraw):F2}ms  present {Milliseconds(_statsPresent) / count:F2}/{Milliseconds(_statsWorstPresent):F2}ms  images {_statsImages}"));

        _statsStarted = now;
        _statsPresents = 0;
        _statsAcquire = 0;
        _statsDraw = 0;
        _statsPresent = 0;
        _statsWorstAcquire = 0;
        _statsWorstDraw = 0;
        _statsWorstPresent = 0;
    }

    private static double Milliseconds(long ticks) =>
        ticks * 1000.0 / System.Diagnostics.Stopwatch.Frequency;

    /// <summary>
    /// Barriers the image into the layout present requires. Skia leaves it as a colour attachment,
    /// which a driver may present anyway, but the specification says PRESENT_SRC and this is the one
    /// place a frame's ordering is the app's to get right.
    /// </summary>
    /// <remarks>
    /// Nothing here waits. The barrier is submitted after Skia's own work on the same queue, so it
    /// runs after the drawing without the CPU having to find out when that finished, and the submit
    /// signals a semaphore the present waits on. A command pool per swap chain image is what makes
    /// the reset safe: an image can only be acquired again once its present has completed, which
    /// means the submit that preceded it is finished and its command buffer is not pending. One
    /// shared pool would need a wait to prove the same thing.
    /// </remarks>
    private bool MoveToPresentState(IntPtr image, uint imageIndex)
    {
        IntPtr pool = _barrierPools[imageIndex];
        IntPtr buffer = _barrierBuffers[imageIndex];

        var resetPool = (delegate* unmanaged<IntPtr, IntPtr, uint, int>)_device.DeviceProc("vkResetCommandPool");
        var begin = (delegate* unmanaged<IntPtr, VkCommandBufferBeginInfo*, int>)_device.DeviceProc("vkBeginCommandBuffer");
        var end = (delegate* unmanaged<IntPtr, int>)_device.DeviceProc("vkEndCommandBuffer");
        var barrierFunc = (delegate* unmanaged<IntPtr, uint, uint, uint, uint, IntPtr, uint, IntPtr, uint, VkImageMemoryBarrier*, void>)_device.DeviceProc("vkCmdPipelineBarrier");
        var submitFunc = (delegate* unmanaged<IntPtr, uint, VkSubmitInfo*, IntPtr, int>)_device.DeviceProc("vkQueueSubmit");

        if (resetPool == null || begin == null || end == null || barrierFunc == null || submitFunc == null)
        {
            return false;
        }

        if (resetPool(_device.Device, pool, 0) != 0)
        {
            return false;
        }

        var beginInfo = new VkCommandBufferBeginInfo { sType = StructureTypeCommandBufferBeginInfo, flags = 0x1 };

        if (begin(buffer, &beginInfo) != 0)
        {
            return false;
        }

        var barrier = new VkImageMemoryBarrier
        {
            sType = StructureTypeImageMemoryBarrier,
            srcAccessMask = 0x100,          // COLOR_ATTACHMENT_WRITE
            dstAccessMask = 0,              // presentation reads nothing the app owns
            oldLayout = ImageLayoutColorAttachment,
            newLayout = ImageLayoutPresentSrc,
            srcQueueFamilyIndex = QueueFamilyIgnored,
            dstQueueFamilyIndex = QueueFamilyIgnored,
            image = image,
            subresourceRange = new VkImageSubresourceRange { aspectMask = 0x1, levelCount = 1, layerCount = 1 },
        };

        barrierFunc(buffer, 0x400, 0x2000, 0, 0, IntPtr.Zero, 0, IntPtr.Zero, 1, &barrier);

        if (end(buffer) != 0)
        {
            return false;
        }

        IntPtr command = buffer;
        IntPtr semaphore = _device.RenderSemaphore;
        var submitInfo = new VkSubmitInfo
        {
            sType = StructureTypeSubmitInfo,
            commandBufferCount = 1,
            pCommandBuffers = (IntPtr)(&command),
            signalSemaphoreCount = 1,
            pSignalSemaphores = (IntPtr)(&semaphore),
        };

        return submitFunc(_device.Queue, 1, &submitInfo, IntPtr.Zero) == 0;
    }

    /// <summary>
    /// Builds one command pool and command buffer per swap chain image, so no frame ever has to
    /// wait for an earlier frame's command buffer to be free before it can record its own.
    /// </summary>
    private bool EnsureBarrierRing(int imageCount)
    {
        if (imageCount <= 0)
        {
            return false;
        }

        if (_barrierPools.Length == imageCount)
        {
            return true;
        }

        ReleaseBarrierRing();

        var createPool = (delegate* unmanaged<IntPtr, VkCommandPoolCreateInfo*, IntPtr, IntPtr*, int>)_device.DeviceProc("vkCreateCommandPool");
        var allocate = (delegate* unmanaged<IntPtr, VkCommandBufferAllocateInfo*, IntPtr*, int>)_device.DeviceProc("vkAllocateCommandBuffers");

        if (createPool == null || allocate == null)
        {
            return false;
        }

        var pools = new IntPtr[imageCount];
        var buffers = new IntPtr[imageCount];

        for (int i = 0; i < imageCount; i++)
        {
            var poolInfo = new VkCommandPoolCreateInfo { sType = StructureTypeCommandPoolCreateInfo, flags = 0x2, queueFamilyIndex = _device.Family };
            IntPtr pool = IntPtr.Zero;

            if (createPool(_device.Device, &poolInfo, IntPtr.Zero, &pool) != 0 || pool == IntPtr.Zero)
            {
                break;
            }

            pools[i] = pool;

            var allocateInfo = new VkCommandBufferAllocateInfo { sType = StructureTypeCommandBufferAllocateInfo, commandPool = pool, level = 0, commandBufferCount = 1 };
            IntPtr buffer = IntPtr.Zero;

            if (allocate(_device.Device, &allocateInfo, &buffer) != 0 || buffer == IntPtr.Zero)
            {
                break;
            }

            buffers[i] = buffer;
        }

        for (int i = 0; i < imageCount; i++)
        {
            if (pools[i] == IntPtr.Zero || buffers[i] == IntPtr.Zero)
            {
                // Half a ring presents nothing: try again cleanly on the next frame.
                _barrierPools = pools;
                _barrierBuffers = buffers;
                ReleaseBarrierRing();
                return false;
            }
        }

        _barrierPools = pools;
        _barrierBuffers = buffers;
        return true;
    }

    /// <summary>
    /// Reads the swap chain's images once. They cannot change while the chain lives, and asking the
    /// driver for the list on every frame is a call and an allocation at the worst possible moment.
    /// </summary>
    private bool RefreshImages()
    {
        var getImages = (delegate* unmanaged<IntPtr, IntPtr, uint*, IntPtr*, int>)_device.DeviceProc("vkGetSwapchainImagesKHR");
        uint count = 0;

        if (getImages == null || getImages(_device.Device, _swapChain, &count, null) != 0 || count == 0)
        {
            _swapChainImages = [];
            return false;
        }

        var images = new IntPtr[count];

        fixed (IntPtr* imagePtrs = images)
        {
            getImages(_device.Device, _swapChain, &count, imagePtrs);
        }

        _swapChainImages = images;
        return true;
    }

    private void ReleaseBarrierRing()
    {
        var destroyPool = (delegate* unmanaged<IntPtr, IntPtr, IntPtr, void>)_device.DeviceProc("vkDestroyCommandPool");

        if (destroyPool == null)
        {
            return;
        }

        foreach (var pool in _barrierPools)
        {
            if (pool != IntPtr.Zero)
            {
                destroyPool(_device.Device, pool, IntPtr.Zero);
            }
        }

        _barrierPools = [];
        _barrierBuffers = [];
    }
    public void Dispose()
    {
        _retained?.Dispose();
        _retained = null;
        _skia?.Dispose();
        _skia = null;

        ReleaseBarrierRing();

        if (_swapChain != IntPtr.Zero)
        {
            var destroy = (delegate* unmanaged<IntPtr, IntPtr, IntPtr, void>)_device.DeviceProc("vkDestroySwapchainKHR");

            if (destroy != null)
            {
                destroy(_device.Device, _swapChain, IntPtr.Zero);
            }

            _swapChain = IntPtr.Zero;
        }

        _device.Dispose();
    }
}
