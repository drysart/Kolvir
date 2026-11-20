namespace Kolvir;

/// <summary>
/// One family of GPU acceleration a backend can offer a window, or the CPU path, which every
/// backend always offers.
/// </summary>
/// <remarks>
/// A family only appears in <see cref="IGpuAccelerationService.Types"/> when the machine can
/// actually draw with it, so an application can present the list as it stands rather than filtering
/// it itself. Windows has <see cref="Direct3D12"/>, <see cref="Vulkan"/> and <see cref="OpenGl"/>;
/// macOS has <see cref="Metal"/>; the GTK4 backend on Linux has <see cref="OpenGl"/>.
/// </remarks>
public enum GpuAccelerationKind
{
    /// <summary>No GPU acceleration: the window paints on the CPU into a plain buffer.</summary>
    Cpu,

    /// <summary>Direct3D 12, presenting through a flip-model swap chain.</summary>
    Direct3D12,

    /// <summary>Vulkan, presenting through a <c>VkSwapchainKHR</c>.</summary>
    Vulkan,

    /// <summary>OpenGL: WGL on Windows, the GTK4 drawing context's GL on Linux.</summary>
    OpenGl,

    /// <summary>Metal, presenting through a <c>CAMetalLayer</c>.</summary>
    Metal,
}
