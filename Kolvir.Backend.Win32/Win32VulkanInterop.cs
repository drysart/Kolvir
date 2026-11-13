using System.Runtime.InteropServices;

namespace Kolvir.Backend.Win32;

/// <summary>
/// The slice of Vulkan a windowed presenter needs: instance, surface, device, swapchain, and the
/// handful of calls that acquire and present an image. Everything else stays in the header files.
/// </summary>
/// <remarks>
/// <para>
/// Entry points are reached through the loader rather than by import, because <c>vulkan-1.dll</c> is
/// the only stable export set and the rest live behind <c>vkGetInstanceProcAddr</c>. The loader
/// itself is nullable: a machine with no Vulkan leaves <see cref="VulkanLoader.TryLoad"/> returning
/// null and the window stays on whatever tier it had.
/// </para>
/// <para>
/// Struct layouts follow the C headers exactly. They are written out by hand because the managed
/// side has no equivalent of the SDK, and a wrong field here is a silent corruption rather than a
/// compile error: VkPhysicalDeviceProperties, for instance, is about 824 bytes and must never be
/// described by a shorter struct.
/// </para>
/// </remarks>
internal static unsafe class Vulkan
{
    internal const int StructureTypeApplicationInfo = 0;
    internal const int StructureTypeInstanceCreateInfo = 1;
    internal const int StructureTypeDeviceQueueCreateInfo = 2;
    internal const int StructureTypeDeviceCreateInfo = 3;
    internal const int StructureTypeFenceCreateInfo = 8;
    internal const int StructureTypeWin32SurfaceCreateInfoKhr = 1000009000;
    internal const int StructureTypeSwapchainCreateInfoKhr = 1000001000;
    internal const int StructureTypePresentInfoKhr = 1000001002;

    internal const int StructureTypeSubmitInfo = 4;
    internal const int StructureTypeCommandPoolCreateInfo = 39;
    internal const int StructureTypeCommandBufferAllocateInfo = 40;
    internal const int StructureTypeCommandBufferBeginInfo = 42;
    internal const int StructureTypeImageMemoryBarrier = 45;
    internal const int FormatBgra8Unorm = 44;
    internal const int FormatBgra8Srgb = 50;
    internal const int FormatRgba8Unorm = 37;
    internal const int FormatRgba8Srgb = 43;

    internal const int ColorSpaceSrgbNonLinear = 0;
    internal const int PresentModeFifo = 2;
    internal const int SharingModeExclusive = 0;
    internal const int ImageTilingOptimal = 0;
    internal const int ImageLayoutUndefined = 0;
    internal const int ImageLayoutColorAttachment = 2;
    internal const int ImageLayoutPresentSrc = 1000001002;

    internal const uint QueueFamilyIgnored = 0xFFFFFFFF;
    internal const uint QueueFlagGraphics = 0x1;
    internal const uint CompositeAlphaOpaque = 0x1;

    // COLOR_ATTACHMENT is what presenting needs. Skia also inspects the usage flags of a texture it
    // wraps, so the swapchain asks for these alongside it, filtered by what the surface allows.
    internal const uint ImageUsageTransferSrc = 0x1;
    internal const uint ImageUsageTransferDst = 0x2;
    internal const uint ImageUsageSampled = 0x4;
    internal const uint ImageUsageColorAttachment = 0x10;

    internal const ulong TimeoutInfinite = 0xFFFFFFFFFFFFFFFF;

    // A swapchain that no longer matches its window: recreate it and present again.
    internal const int ErrorOutOfDateKhr = -1000001004;
    internal const int SuboptimalKhr = 1000001003;
    internal const int ErrorIncompatibleDriver = -9;

    [StructLayout(LayoutKind.Sequential)]
    internal struct VkApplicationInfo
    {
        public int sType;
        public IntPtr pNext;
        public IntPtr pApplicationName;
        public uint applicationVersion;
        public IntPtr pEngineName;
        public uint engineVersion;
        public uint apiVersion;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct VkInstanceCreateInfo
    {
        public int sType;
        public IntPtr pNext;
        public uint flags;
        public IntPtr pApplicationInfo;
        public uint enabledLayerCount;
        public IntPtr ppEnabledLayerNames;
        public uint enabledExtensionCount;
        public IntPtr ppEnabledExtensionNames;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct VkQueueFamilyProperties
    {
        public uint queueFlags;
        public uint queueCount;
        public uint timestampValidBits;
        public uint minImageTransferGranularityWidth;
        public uint minImageTransferGranularityHeight;
        public uint minImageTransferGranularityDepth;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct VkDeviceQueueCreateInfo
    {
        public int sType;
        public IntPtr pNext;
        public uint flags;
        public uint queueFamilyIndex;
        public uint queueCount;
        public IntPtr pQueuePriorities;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct VkDeviceCreateInfo
    {
        public int sType;
        public IntPtr pNext;
        public uint flags;
        public uint queueCreateInfoCount;
        public IntPtr pQueueCreateInfos;
        public uint enabledLayerCount;
        public IntPtr ppEnabledLayerNames;
        public uint enabledExtensionCount;
        public IntPtr ppEnabledExtensionNames;
        public IntPtr pEnabledFeatures;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct VkWin32SurfaceCreateInfoKhr
    {
        public int sType;
        public IntPtr pNext;
        public uint flags;
        public IntPtr hinstance;
        public IntPtr hwnd;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct VkExtent2D
    {
        public uint width;
        public uint height;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct VkSurfaceFormatKhr
    {
        public int format;
        public int colorSpace;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct VkSurfaceCapabilitiesKhr
    {
        public uint minImageCount;
        public uint maxImageCount;
        public VkExtent2D currentExtent;
        public VkExtent2D minImageExtent;
        public VkExtent2D maxImageExtent;
        public uint maxImageArrayLayers;
        public uint supportedTransforms;
        public int currentTransform;
        public uint supportedCompositeAlpha;
        public uint supportedUsageFlags;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct VkSwapchainCreateInfoKhr
    {
        public int sType;
        public IntPtr pNext;
        public uint flags;
        public IntPtr surface;
        public uint minImageCount;
        public int imageFormat;
        public int imageColorSpace;
        public VkExtent2D imageExtent;
        public uint imageArrayLayers;
        public uint imageUsage;
        public int imageSharingMode;
        public uint queueFamilyIndexCount;
        public IntPtr pQueueFamilyIndices;
        public int preTransform;
        public uint compositeAlpha;
        public int presentMode;
        public uint clipped;
        public IntPtr oldSwapchain;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct VkPresentInfoKhr
    {
        public int sType;
        public IntPtr pNext;
        public uint waitSemaphoreCount;
        public IntPtr pWaitSemaphores;
        public uint swapchainCount;
        public IntPtr pSwapchains;
        public IntPtr pImageIndices;
        public IntPtr pResults;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct VkFenceCreateInfo
    {
        public int sType;
        public IntPtr pNext;
        public uint flags;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct VkImageSubresourceRange
    {
        public uint aspectMask;
        public uint baseMipLevel;
        public uint levelCount;
        public uint baseArrayLayer;
        public uint layerCount;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct VkImageMemoryBarrier
    {
        public int sType;
        public IntPtr pNext;
        public uint srcAccessMask;
        public uint dstAccessMask;
        public int oldLayout;
        public int newLayout;
        public uint srcQueueFamilyIndex;
        public uint dstQueueFamilyIndex;
        public IntPtr image;
        public VkImageSubresourceRange subresourceRange;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct VkSubmitInfo
    {
        public int sType;
        public IntPtr pNext;
        public uint waitSemaphoreCount;
        public IntPtr pWaitSemaphores;
        public IntPtr pWaitDstStageMask;
        public uint commandBufferCount;
        public IntPtr pCommandBuffers;
        public uint signalSemaphoreCount;
        public IntPtr pSignalSemaphores;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct VkCommandPoolCreateInfo
    {
        public int sType;
        public IntPtr pNext;
        public uint flags;
        public uint queueFamilyIndex;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct VkCommandBufferAllocateInfo
    {
        public int sType;
        public IntPtr pNext;
        public IntPtr commandPool;
        public int level;
        public uint commandBufferCount;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct VkCommandBufferBeginInfo
    {
        public int sType;
        public IntPtr pNext;
        public uint flags;
        public IntPtr pInheritanceInfo;
    }

    /// <summary>The loader and the two entry points every other call is reached through.</summary>
    internal sealed unsafe class VulkanLoader
    {
        private VulkanLoader(IntPtr module, IntPtr getInstanceProcAddress, IntPtr getDeviceProcAddress)
        {
            Module = module;
            _getInstanceProcAddress = (delegate* unmanaged<IntPtr, byte*, IntPtr>)getInstanceProcAddress;
            _getDeviceProcAddress = (delegate* unmanaged<IntPtr, byte*, IntPtr>)getDeviceProcAddress;
        }

        internal IntPtr Module { get; }

        private readonly delegate* unmanaged<IntPtr, byte*, IntPtr> _getInstanceProcAddress;
        private readonly delegate* unmanaged<IntPtr, byte*, IntPtr> _getDeviceProcAddress;

        /// <summary>Loads <c>vulkan-1.dll</c> once; null means the machine has no Vulkan at all.</summary>
        internal static VulkanLoader? TryLoad()
        {
            if (Instance is not null)
            {
                return Instance;
            }

            if (Attempted)
            {
                return null;
            }

            Attempted = true;

            var module = NativeMethods.LoadLibraryW("vulkan-1.dll");

            if (module == IntPtr.Zero)
            {
                return null;
            }

            var getInstance = NativeMethods.GetProcAddress(module, "vkGetInstanceProcAddr");
            var getDevice = NativeMethods.GetProcAddress(module, "vkGetDeviceProcAddr");

            if (getInstance == IntPtr.Zero)
            {
                return null;
            }

            Instance = new VulkanLoader(module, getInstance, getDevice);
            return Instance;
        }

        private static VulkanLoader? Instance;
        private static bool Attempted;

        /// <summary>Resolves an instance-level entry point, or zero when this loader has none.</summary>
        internal IntPtr InstanceProc(IntPtr instance, string name)
        {
            var bytes = Marshal.StringToCoTaskMemUTF8(name);

            try
            {
                return _getInstanceProcAddress(instance, (byte*)bytes);
            }
            finally
            {
                Marshal.FreeCoTaskMem(bytes);
            }
        }

        /// <summary>Resolves a device-level entry point, falling back to the instance level.</summary>
        internal IntPtr DeviceProc(IntPtr device, IntPtr instance, string name)
        {
            if (device != IntPtr.Zero && _getDeviceProcAddress != null)
            {
                var bytes = Marshal.StringToCoTaskMemUTF8(name);

                try
                {
                    var fromDevice = _getDeviceProcAddress(device, (byte*)bytes);

                    if (fromDevice != IntPtr.Zero)
                    {
                        return fromDevice;
                    }
                }
                finally
                {
                    Marshal.FreeCoTaskMem(bytes);
                }
            }

            return InstanceProc(instance, name);
        }
    }
}
