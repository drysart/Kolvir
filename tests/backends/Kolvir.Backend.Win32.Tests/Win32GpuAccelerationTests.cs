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
            Assert.AreEqual(option.Value, GpuAccelerationSyntax.Format(option.Kind, option.Device?.Index ?? 0));
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
            Assert.AreEqual(index, d3d.Devices[index].Index, "devices are numbered in enumeration order");
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

        using var surface = new FakeSurface("gl", deviceIndex: 0);
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

    /// <summary>A tier that reports a name and a device index and draws nothing.</summary>
    private sealed class FakeSurface(string tier, int deviceIndex) : IWin32GpuSurface
    {
        public SKSurface? Surface => null;

        public bool HasRetainedBuffer => false;

        public bool NeedsFullRepaint => true;

        public string Tier { get; } = tier;

        public string Renderer => "fake";

        public int DeviceIndex { get; } = deviceIndex;

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
