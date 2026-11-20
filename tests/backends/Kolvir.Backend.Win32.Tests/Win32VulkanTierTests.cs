using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Kolvir.Backend.Win32.Tests;

/// <summary>
/// The order the window asks for its tiers in. Vulkan leads because a rebuilt swap chain is what
/// keeps a resized window from accumulating a drawable per size; Direct3D 12 follows it with the same
/// property and no Vulkan dependency of its own; GL follows because it is what the backend had before,
/// and the DIB follows that. Only the refusals are reachable without a driver, which is enough to pin
/// the order and the diagnostic line the log gets.
/// </summary>
[TestClass]
[TestCategory("Platform.Win32")]
public sealed class Win32VulkanTierTests
{
    private sealed class RecordingVulkanProbe : IWin32VulkanProbe
    {
        private readonly List<string> _order;

        public RecordingVulkanProbe(List<string> order) => _order = order;

        public string Failure { get; set; } = "vkCreateInstance failed (-9)";

        public int Calls { get; private set; }

        public Win32VulkanDevice? TryCreate(IntPtr hwnd, out string failure)
        {
            Calls++;
            _order.Add("vulkan");
            failure = Failure;
            return null;
        }
    }

    private sealed class RecordingGlProbe : IWin32GlProbe
    {
        private readonly List<string> _order;

        public RecordingGlProbe(List<string> order) => _order = order;

        public string FlavorFailure { get; set; } = "no wgl context (error 0)";

        public int SelectCalls { get; private set; }

        public Win32GlFlavor? SelectFlavor(out string failure)
        {
            SelectCalls++;
            _order.Add("gl");
            failure = FlavorFailure;
            return null;
        }

        public Win32GlContext? TryCreate(IntPtr hwnd, Win32GlFlavor flavor, out string failure)
        {
            failure = FlavorFailure;
            return null;
        }
    }

    private sealed class RecordingD3D12Probe : IWin32D3D12Probe
    {
        private readonly List<string> _order;

        public RecordingD3D12Probe(List<string> order) => _order = order;

        public string Failure { get; set; } = "d3d12: no hardware adapter";

        public int Calls { get; private set; }

        public Win32D3D12Device? TryCreate(IntPtr hwnd, out string failure)
        {
            Calls++;
            _order.Add("d3d12");
            failure = Failure;
            return null;
        }
    }
    [TestMethod]
    public void TheTiersAreTriedInOrder()
    {
        List<string> order = [];
        var vulkan = new RecordingVulkanProbe(order);
        var gl = new RecordingGlProbe(order);

        var surface = Win32Gfx.TryCreateAccelerated(
            hardwareAcceleration: true,
            new IntPtr(1),
            gl,
            vulkan,
            new RecordingD3D12Probe(order),
            width: 800,
            height: 600,
            out var reason);

        Assert.IsNull(surface, "neither tier produces a surface without a driver");
        CollectionAssert.AreEqual(
            new[] { "vulkan", "d3d12", "gl" },
            order,
            "GL is only asked once the two swap chain tiers have refused");
        Assert.AreEqual("cpu (vkCreateInstance failed (-9); d3d12: no hardware adapter; no wgl context (error 0))", reason);
    }

    [TestMethod]
    public void ADisabledOptionConsultsNeitherTier()
    {
        List<string> order = [];
        var vulkan = new RecordingVulkanProbe(order);
        var gl = new RecordingGlProbe(order);

        var surface = Win32Gfx.TryCreateAccelerated(
            hardwareAcceleration: false,
            new IntPtr(1),
            gl,
            vulkan,
            new RecordingD3D12Probe(order),
            width: 800,
            height: 600,
            out var reason);

        Assert.IsNull(surface);
        Assert.AreEqual(0, vulkan.Calls);
        Assert.AreEqual(0, gl.SelectCalls);
        Assert.AreEqual("cpu (disabled)", reason);
    }

    [TestMethod]
    public void AWindowLessSurfaceConsultsNeitherTier()
    {
        List<string> order = [];
        var vulkan = new RecordingVulkanProbe(order);
        var gl = new RecordingGlProbe(order);

        var surface = Win32Gfx.TryCreateAccelerated(
            hardwareAcceleration: true,
            IntPtr.Zero,
            gl,
            vulkan,
            new RecordingD3D12Probe(order),
            width: 800,
            height: 600,
            out var reason);

        Assert.IsNull(surface);
        Assert.AreEqual(0, vulkan.Calls);
        Assert.AreEqual(0, gl.SelectCalls);
        Assert.AreEqual("cpu (no window)", reason);
    }

    [TestMethod]
    public void AnEmptySurfaceConsultsNeitherTier()
    {
        List<string> order = [];
        var vulkan = new RecordingVulkanProbe(order);
        var gl = new RecordingGlProbe(order);

        var surface = Win32Gfx.TryCreateAccelerated(
            hardwareAcceleration: true,
            new IntPtr(1),
            gl,
            vulkan,
            new RecordingD3D12Probe(order),
            width: 0,
            height: 600,
            out var reason);

        Assert.IsNull(surface);
        Assert.AreEqual(0, vulkan.Calls);
        Assert.AreEqual(0, gl.SelectCalls);
        Assert.AreEqual("cpu (empty surface)", reason);
    }

    [TestMethod]
    public void TheReasonKeepsTheGlStepWhenOnlyTheVulkanSideIsTerse()
    {
        List<string> order = [];
        var vulkan = new RecordingVulkanProbe(order) { Failure = "no vulkan loader" };
        var gl = new RecordingGlProbe(order) { FlavorFailure = "gdi: no wgl context (error 0)" };

        var surface = Win32Gfx.TryCreateAccelerated(
            hardwareAcceleration: true,
            new IntPtr(1),
            gl,
            vulkan,
            new RecordingD3D12Probe(order),
            width: 800,
            height: 600,
            out var reason);

        Assert.IsNull(surface);
        Assert.AreEqual("cpu (no vulkan loader; d3d12: no hardware adapter; gdi: no wgl context (error 0))", reason);
    }
}
