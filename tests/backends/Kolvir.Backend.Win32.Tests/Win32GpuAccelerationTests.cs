using System.Runtime.InteropServices;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SkiaSharp;

namespace Kolvir.Backend.Win32.Tests;

/// <summary>
/// The Win32 acceleration listing: what this machine can draw with, and what it is drawing with now.
/// </summary>
/// <remarks>
/// The enumeration walks real DXGI adapters and, when a Vulkan runtime is installed, real physical
/// devices, so what it finds depends on the machine. What is asserted here is the shape the contract
/// promises - the CPU path is always present and always last, every option is one the service would
/// accept as a setting, and nothing is listed twice - which holds on a GPU machine and on a CI agent
/// with none.
/// </remarks>
[TestClass]
[TestCategory("Platform.Win32")]
public sealed class Win32GpuAccelerationTests
{
    [TestMethod]
    public void TheCpuPathIsAlwaysOfferedAndIsAlwaysLast()
    {
        var service = new Win32GpuAccelerationService();

        Assert.IsTrue(service.Types.Count > 0, "the list is never empty");
        Assert.AreEqual(GpuAccelerationKind.Cpu, service.Types[^1].Kind);
        Assert.AreEqual("cpu", service.Options[^1].Value);
        Assert.AreEqual("No GPU acceleration", service.Options[^1].Name);
    }

    [TestMethod]
    public void CurrentStartsOnTheCpuPath()
    {
        var service = new Win32GpuAccelerationService();

        Assert.AreEqual("cpu", service.Current.Value);
        CollectionAssert.Contains(service.Options.ToArray(), service.Current);
    }

    [TestMethod]
    public void EveryOptionIsAValueTheServiceReadsBack()
    {
        var service = new Win32GpuAccelerationService();

        foreach (var option in service.Options)
        {
            Assert.IsTrue(service.TryFindOption(option.Value, out var found), option.Value);
            Assert.AreEqual(option, found, option.Value);
            Assert.IsFalse(string.IsNullOrWhiteSpace(option.Name), option.Value);

            // A saved setting has to survive the round trip, or the choice a user makes is lost.
            Assert.AreEqual(option.Value, GpuAccelerationSyntax.Format(option.Kind, option.Device?.Key));
        }
    }

    [TestMethod]
    public void ADirect3D12FamilyOffersOneOptionPerHardwareAdapter()
    {
        var service = new Win32GpuAccelerationService();
        var d3d = service.Types.FirstOrDefault(type => type.Kind == GpuAccelerationKind.Direct3D12);

        if (d3d is null)
        {
            // No hardware adapter this process can draw on - a CI agent, or a session with no desktop.
            Assert.IsTrue(
                service.Types.Any(type => type.Kind == GpuAccelerationKind.Cpu),
                "a machine with no Direct3D 12 still offers the CPU path");
            return;
        }

        Assert.IsTrue(d3d.Devices.Count > 0, "a family listed without a device could never be chosen");
        Assert.AreEqual("Direct3D 12", d3d.Name);

        for (var index = 0; index < d3d.Devices.Count; index++)
        {
            Assert.IsTrue(GpuAccelerationSyntax.IsDeviceKey(d3d.Devices[index].Key), "every device carries an identity");
            Assert.IsFalse(string.IsNullOrWhiteSpace(d3d.Devices[index].Name), "an adapter has a description");
        }
    }

    [TestMethod]
    public void ReportingATierMakesItCurrent()
    {
        var service = new Win32GpuAccelerationService();

        if (!service.Types.Any(type => type.Kind == GpuAccelerationKind.OpenGl))
        {
            Assert.Inconclusive("this machine has no OpenGL driver to report");
        }

        var raised = 0;
        service.CurrentChanged += (_, _) => raised++;

        using var surface = new FakeSurface("gl", deviceKey: string.Empty);
        service.Report(surface);

        Assert.AreEqual("ogl", service.Current.Value);
        Assert.AreEqual(1, raised);

        service.Report(surface);
        Assert.AreEqual(1, raised, "reporting the same choice again raises nothing");
    }

    [TestMethod]
    public void ADroppedSurfaceIsTheCpuPath()
    {
        var service = new Win32GpuAccelerationService();

        service.Report(null);

        Assert.AreEqual("cpu", service.Current.Value);
    }

    [TestMethod]
    public void ATierNameMapsToItsFamily()
    {
        Assert.AreEqual(GpuAccelerationKind.Direct3D12, Win32GpuAccelerationService.KindOf("d3d12"));
        Assert.AreEqual(GpuAccelerationKind.Vulkan, Win32GpuAccelerationService.KindOf("vulkan"));
        Assert.AreEqual(GpuAccelerationKind.OpenGl, Win32GpuAccelerationService.KindOf("gl"));
        Assert.AreEqual(GpuAccelerationKind.Cpu, Win32GpuAccelerationService.KindOf("cpu"));
    }

    [TestMethod]
    public void TheVulkanIdentityStructsMatchTheHeaders()
    {
        // A hand-written struct that is a byte off corrupts the driver's write rather than failing, so
        // the two layouts the device identity is read out of are pinned against vulkan_core.h. Offsets
        // follow the header exactly: VkPhysicalDeviceProperties2 is sType, pNext, then the 824-byte
        // VkPhysicalDeviceProperties, whose own order is apiVersion, driverVersion, vendorID, deviceID.
        Assert.AreEqual(16 + 824, Marshal.SizeOf<Vulkan.VkPhysicalDeviceProperties2>());
        Assert.AreEqual(24, Marshal.OffsetOf<Vulkan.VkPhysicalDeviceProperties2>("vendorID").ToInt32());
        Assert.AreEqual(28, Marshal.OffsetOf<Vulkan.VkPhysicalDeviceProperties2>("deviceID").ToInt32());

        Assert.AreEqual(64, Marshal.SizeOf<Vulkan.VkPhysicalDeviceIdProperties>());
        Assert.AreEqual(16, Marshal.OffsetOf<Vulkan.VkPhysicalDeviceIdProperties>("deviceUuidLow").ToInt32());
        Assert.AreEqual(24, Marshal.OffsetOf<Vulkan.VkPhysicalDeviceIdProperties>("deviceUuidHigh").ToInt32());
        Assert.AreEqual(48, Marshal.OffsetOf<Vulkan.VkPhysicalDeviceIdProperties>("deviceLUID").ToInt32());
        Assert.AreEqual(60, Marshal.OffsetOf<Vulkan.VkPhysicalDeviceIdProperties>("deviceLUIDValid").ToInt32());
    }

    [TestMethod]
    public void ALiveSwitchNeedsAWindowAndSaysSoWhenThereIsNone()
    {
        // The service is built outside an application here, which is the case a test reaches: it can list
        // the machine's options but has nothing to rebuild, so it refuses rather than pretending.
        var service = new Win32GpuAccelerationService();

        Assert.IsFalse(service.TryApply("cpu", out var reason));
        StringAssert.Contains(reason, "no windows that can be switched");
    }

    [TestMethod]
    public void ASwitchIsCarriedToTheNextWindowOnlyWhenItTook()
    {
        var target = new RecordingTarget { Result = true };
        var service = new Win32GpuAccelerationService(target);

        Assert.IsNull(service.Requested, "nothing has switched, so the application's option decides");

        if (!service.Types.Any(type => type.Kind == GpuAccelerationKind.OpenGl))
        {
            Assert.Inconclusive("this machine has no OpenGL driver to switch to");
        }

        // A request the target honors becomes what every window created afterwards asks for.
        Assert.IsTrue(service.TryApply("ogl", out _));
        Assert.AreEqual("ogl", service.Requested);
        Assert.AreEqual("ogl", target.Last?.Value);
    }

    [TestMethod]
    public void AFailedSwitchLeavesTheRequestAlone()
    {
        var target = new RecordingTarget { Result = false, Reason = "the tier fell over" };
        var service = new Win32GpuAccelerationService(target);

        if (!service.Types.Any(type => type.Kind == GpuAccelerationKind.OpenGl))
        {
            Assert.Inconclusive("this machine has no OpenGL driver to switch to");
        }

        Assert.IsFalse(service.TryApply("ogl", out var reason));
        Assert.AreEqual("the tier fell over", reason);
        Assert.IsNull(service.Requested, "a window opened later keeps the choice that worked");
    }

    /// <summary>A target that records what it was asked for and answers as the test decides.</summary>
    private sealed class RecordingTarget : IWin32AccelerationTarget
    {
        public bool Result { get; set; }

        public string Reason { get; set; } = string.Empty;

        public GpuAccelerationOption? Last { get; private set; }

        public bool Apply(GpuAccelerationOption option, out string reason)
        {
            Last = option;
            reason = Reason;
            return Result;
        }
    }

    /// <summary>A tier that reports a name and a device index and draws nothing.</summary>
    private sealed class FakeSurface(string tier, string deviceKey) : IWin32GpuSurface
    {
        public SKSurface? Surface => null;

        public bool HasRetainedBuffer => false;

        public bool NeedsFullRepaint => true;

        public string Tier { get; } = tier;

        public string Renderer => "fake";

        public string DeviceKey { get; } = deviceKey;

        public bool TakeNeedsFull() => false;

        public bool MakeCurrent() => true;

        public bool Resize(int width, int height) => true;

        public Win32PresentResult Present() => Win32PresentResult.Presented;

        public void DropRetainedBuffer()
        {
        }

        public void Dispose()
        {
        }
    }
}
