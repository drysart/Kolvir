using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Kolvir.Backend.Win32.Tests;

/// <summary>
/// The Direct3D 12 tier's place in the order, and the one thing that can be checked without a driver:
/// the production probe either builds a device or names the step that refused.
/// </summary>
/// <remarks>
/// The tier itself cannot be built here - a swap chain, a Skia context on it and a retained surface
/// need a real device - so what is pinned is the order the window asks in and the diagnostic line the
/// log gets when a tier declines.
/// </remarks>
[TestClass]
[TestCategory("Platform.Win32")]
public sealed class Win32D3D12TierTests
{
    private sealed class RefusingVulkanProbe : IWin32VulkanProbe
    {
        public Win32VulkanDevice? TryCreate(IntPtr hwnd, out string failure)
        {
            failure = "no vulkan loader";
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

    private sealed class RecordingGlProbe : IWin32GlProbe
    {
        private readonly List<string> _order;

        public RecordingGlProbe(List<string> order) => _order = order;

        public int SelectCalls { get; private set; }

        public Win32GlFlavor? SelectFlavor(out string failure)
        {
            SelectCalls++;
            _order.Add("gl");
            failure = "no wgl context (error 0)";
            return null;
        }

        public Win32GlContext? TryCreate(IntPtr hwnd, Win32GlFlavor flavor, out string failure)
        {
            failure = "no wgl context (error 0)";
            return null;
        }
    }

    [TestMethod]
    public void D3d12IsAskedFirstByDefault()
    {
        List<string> order = [];
        var gl = new RecordingGlProbe(order);

        var surface = Win32Gfx.TryCreateAccelerated(
            Win32GfxChoice.Default,
            hardwareAcceleration: true,
            new IntPtr(1),
            gl,
            new RefusingVulkanProbe(),
            new RecordingD3D12Probe(order),
            width: 800,
            height: 600,
            out var reason);

        Assert.IsNull(surface, "no tier produces a surface without a driver");
        CollectionAssert.AreEqual(
            new[] { "d3d12", "gl" },
            order,
            "Direct3D 12 leads the default order, and GL is asked once the swap chain tiers have refused");
        Assert.AreEqual("cpu (d3d12: no hardware adapter; no vulkan loader; no wgl context (error 0))", reason);
    }

    [TestMethod]
    public void TheD3d12StepKeepsWhatItSaid()
    {
        List<string> order = [];
        var d3d12 = new RecordingD3D12Probe(order) { Failure = "d3d12: no device (0x887A0004)" };

        var surface = Win32Gfx.TryCreateAccelerated(
            Win32GfxChoice.Default,
            hardwareAcceleration: true,
            new IntPtr(1),
            new RecordingGlProbe(order),
            new RefusingVulkanProbe(),
            d3d12,
            width: 800,
            height: 600,
            out var reason);

        Assert.IsNull(surface);
        Assert.AreEqual(1, d3d12.Calls, "the tier is asked once, not once per refusal it reports");
        StringAssert.Contains(reason, "d3d12: no device (0x887A0004)", "the diagnostic line names the tier's own failure");
    }

    [TestMethod]
    public void ASoftwareAdapterIsRefusedWhateverItClaims()
    {
        // The Basic Render Driver is what a machine with no GPU reports, and it does not always set
        // the software flag: a tier that accepted it would rasterize on the CPU and be slower than the
        // DIB path it replaced.
        Assert.IsTrue(D3D12.IsSoftwareAdapter(D3D12.AdapterFlagSoftware, "NVIDIA GeForce RTX 4090"));
        Assert.IsTrue(D3D12.IsSoftwareAdapter(0, "Microsoft Basic Render Driver"));
        Assert.IsFalse(D3D12.IsSoftwareAdapter(0, "NVIDIA GeForce RTX 4090"));
        Assert.IsFalse(D3D12.IsSoftwareAdapter(0, "AMD Radeon RX 7900 XTX"));
    }
    [TestMethod]
    public void TheProductionProbeAnswersOrSaysWhy()
    {        // The interop's entry points are loaded and the adapter list walked for real, so this is what
        // catches a wrong export name or a mis-sized struct without needing a GPU in the machine.
        var device = Win32D3D12Probe.Instance.TryCreate(new IntPtr(1), out var failure);

        if (device is null)
        {
            StringAssert.StartsWith(failure, "d3d12: ", "a refusal names the tier and the step");
            return;
        }

        using (device)
        {
            Assert.IsFalse(string.IsNullOrWhiteSpace(device.Renderer), "the adapter has a description");
            Assert.AreNotEqual(IntPtr.Zero, device.Device, "the device exists");
            Assert.AreNotEqual(IntPtr.Zero, device.Queue, "the queue exists");
        }
    }
}
