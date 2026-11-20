using System.Runtime.InteropServices;
using SkiaSharp;
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
    [DllImport("user32.dll", EntryPoint = "CreateWindowExW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr CreateWindowExW(uint exStyle, string className, string title, uint style, int x, int y, int width, int height, IntPtr parent, IntPtr menu, IntPtr instance, IntPtr parameter);

    [DllImport("user32.dll", EntryPoint = "DestroyWindow")]
    private static extern int DestroyWindow(IntPtr hwnd);

    [DllImport("kernel32.dll", EntryPoint = "GetModuleHandleW", CharSet = CharSet.Unicode)]
    private static extern IntPtr GetModuleHandle(string? name);

    private sealed class RefusingVulkanProbe : IWin32VulkanProbe
    {
        public Win32VulkanDevice? TryCreate(IntPtr hwnd, string? deviceKey, out string failure)
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

        public Win32D3D12Device? TryCreate(IntPtr hwnd, string? deviceKey, out string failure)
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
        // DIB path it replaced. The diagnostic switch that lets it through for the frame test below
        // is cleared here so this test does not depend on the order the two run in.
        bool allowing = D3D12.AllowSoftwareAdapter;
        D3D12.AllowSoftwareAdapter = false;

        try
        {
            Assert.IsTrue(D3D12.IsSoftwareAdapter(D3D12.AdapterFlagSoftware, "NVIDIA GeForce RTX 4090"));
            Assert.IsTrue(D3D12.IsSoftwareAdapter(0, "Microsoft Basic Render Driver"));
            Assert.IsFalse(D3D12.IsSoftwareAdapter(0, "NVIDIA GeForce RTX 4090"));
            Assert.IsFalse(D3D12.IsSoftwareAdapter(0, "AMD Radeon RX 7900 XTX"));
        }
        finally
        {
            D3D12.AllowSoftwareAdapter = allowing;
        }
    }

    [TestMethod]
    public void TheAdapterThatDrivesTheWindowIsChosen()
    {
        // A flip-model swap chain cannot cross adapters, so a device on the discrete card cannot
        // present to a window on the integrated GPU's output. The adapter is therefore the one whose
        // output holds the window, not simply the first enumerated.
        string[] keys = ["8086-46a6-0", "10de-2504-1616", "10de-2504-1617"];

        Assert.AreEqual(0, Win32D3D12Device.ChooseAdapterIndex([true, true], keys), "the first that drives it");
        Assert.AreEqual(1, Win32D3D12Device.ChooseAdapterIndex([false, true], keys), "a later adapter can be the one");
        Assert.AreEqual(2, Win32D3D12Device.ChooseAdapterIndex([false, false, true], keys));
        Assert.AreEqual(0, Win32D3D12Device.ChooseAdapterIndex([false, false], keys), "nothing claims the window: keep the old behaviour");
        Assert.AreEqual(0, Win32D3D12Device.ChooseAdapterIndex([], []), "and an empty list still has a first");
    }

    [TestMethod]
    public void AChosenAdapterIsPickedByIdentityNotByPosition()
    {
        // The identities are what a saved setting carries, so they are what decides - whatever order the
        // machine happens to enumerate its adapters in today.
        string[] keys = ["8086-46a6-0", "10de-2504-1616"];

        Assert.AreEqual(1, Win32D3D12Device.ChooseAdapterIndex([true, true], keys, preferredKey: "10de-2504-1616"));
        Assert.AreEqual(0, Win32D3D12Device.ChooseAdapterIndex([true, false], keys, preferredKey: "10de-2504-1616"), "it does not drive the window, so the one that does");
        Assert.AreEqual(1, Win32D3D12Device.ChooseAdapterIndex([false, true], keys, preferredKey: "10de-2504-1616"));
        Assert.AreEqual(0, Win32D3D12Device.ChooseAdapterIndex([true, true], keys, preferredKey: "not-here"), "a card this machine does not have");
        Assert.AreEqual(1, Win32D3D12Device.ChooseAdapterIndex([false, false], keys, preferredKey: "10de-2504-1616"), "nothing drives it, so the preference is still tried");
    }

    [TestMethod]
    public void ASwapChainIsAcceptedOnTheDefaultAdapter()
    {
        // The create call has no diagnostics of its own: DXGI answers DXGI_ERROR_INVALID_CALL for a
        // description it will not take and names none of the fields it objected to. Every Windows
        // machine has a default adapter, so this runs everywhere and fails if the description drifts -
        // a transposed DXGI_ALPHA_MODE (STRAIGHT beside IGNORE) did exactly that.
        Assert.IsTrue(D3D12.IsAvailable, "dxgi.dll and d3d12.dll are present");

        IntPtr hwnd = CreateWindowExW(0, "STATIC", "kolvir d3d12", 0x00CF0000, 0, 0, 64, 64, IntPtr.Zero, IntPtr.Zero, GetModuleHandle(null), IntPtr.Zero);
        Assert.AreNotEqual(IntPtr.Zero, hwnd, "the test window was created");

        try
        {
            IntPtr factory = D3D12.CreateFactory1(out int factoryResult);
            Assert.AreNotEqual(IntPtr.Zero, factory, $"the factory was created (0x{factoryResult:X8})");

            IntPtr device = D3D12.CreateDevice(IntPtr.Zero, out int deviceResult);
            IntPtr queue = device == IntPtr.Zero ? IntPtr.Zero : D3D12.CreateCommandQueue(device, out _);

            if (queue == IntPtr.Zero)
            {
                // A machine with no Direct3D 12 runtime at all has nothing to say about the
                // description; the probe test above is where that is reported.
                Assert.Inconclusive($"no default device (0x{deviceResult:X8})");
                return;
            }

            IntPtr swapChain = D3D12.CreateSwapChainForHwnd(factory, queue, hwnd, 64, 64, D3D12.SwapEffectFlipDiscard, 2, 0, out int chainResult);
            Assert.AreNotEqual(IntPtr.Zero, swapChain, $"the swap chain was created (0x{chainResult:X8})");

            D3D12.Release(ref swapChain);
            D3D12.Release(ref queue);
            D3D12.Release(ref device);
            D3D12.Release(ref factory);
        }
        finally
        {
            DestroyWindow(hwnd);
        }
    }

    [TestMethod]
    public void FramesAndResizesOnTheDefaultAdapter()
    {
        // The whole surface on a software adapter: a real device, a real flip swap chain and a real
        // Skia context, driven for a few frames and then resized. Two faults that only a live chain
        // shows up in are pinned here - GetCurrentBackBufferIndex at the wrong vtable slot (an access
        // violation on the first present) and the per-frame texture wrapper holding a reference on the
        // back buffer, which DXGI answers a resize with DXGI_ERROR_INVALID_CALL.
        Assert.IsTrue(D3D12.IsAvailable, "dxgi.dll and d3d12.dll are present");

        IntPtr hwnd = CreateWindowExW(0, "STATIC", "kolvir d3d12 frames", 0x00CF0000, 0, 0, 320, 200, IntPtr.Zero, IntPtr.Zero, GetModuleHandle(null), IntPtr.Zero);
        Assert.AreNotEqual(IntPtr.Zero, hwnd, "the test window was created");

        bool allowing = D3D12.AllowSoftwareAdapter;
        D3D12.AllowSoftwareAdapter = true;

        try
        {
            var surface = Win32D3D12Surface.TryCreate(hwnd, Win32D3D12Probe.Instance, 320, 200, out var failure);

            if (surface is null)
            {
                Assert.Inconclusive(failure);
                return;
            }

            try
            {
                Assert.AreEqual("d3d12", surface.Tier);

                for (int frame = 0; frame < 5; frame++)
                {
                    surface.Surface!.Canvas.Clear(new SKColor((byte)(frame * 20), 40, 90));
                    Assert.AreEqual(Win32PresentResult.Presented, surface.Present(), $"frame {frame} was presented");
                }

                Assert.IsTrue(surface.Resize(360, 220), "the chain resizes after frames have been presented");
                Assert.AreEqual(Win32PresentResult.Presented, surface.Present(), "and presents again at the new size");
            }
            finally
            {
                surface.Dispose();
            }
        }
        finally
        {
            D3D12.AllowSoftwareAdapter = allowing;
            DestroyWindow(hwnd);
        }
    }

    [TestMethod]
    public void TheProductionProbeAnswersOrSaysWhy()
    {
        // The interop's entry points are loaded and the adapter list walked for real, so this is what
        // catches a wrong export name or a mis-sized struct without needing a GPU in the machine.
        var device = Win32D3D12Probe.Instance.TryCreate(new IntPtr(1), adapterKey: null, out var failure);

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
