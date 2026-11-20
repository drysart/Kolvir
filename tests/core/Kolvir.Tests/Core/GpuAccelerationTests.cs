using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Kolvir.Tests;

/// <summary>
/// The acceleration contract an application builds a settings list from: the values it saves, the
/// two-level list it shows, and the choice it reads back as the live one.
/// </summary>
[TestClass]
[TestCategory("Core")]
public sealed class GpuAccelerationTests
{
    [TestMethod]
    public void FormatWritesTheBareFamilyForTheFirstDevice()
    {
        // A saved value should read like the family wherever it can, so index 0 is implicit.
        Assert.AreEqual("d3d", GpuAccelerationSyntax.Format(GpuAccelerationKind.Direct3D12));
        Assert.AreEqual("d3d", GpuAccelerationSyntax.Format(GpuAccelerationKind.Direct3D12, 0));
        Assert.AreEqual("d3d:2", GpuAccelerationSyntax.Format(GpuAccelerationKind.Direct3D12, 2));
        Assert.AreEqual("cpu", GpuAccelerationSyntax.Format(GpuAccelerationKind.Cpu));
    }

    [TestMethod]
    public void FormatRefusesADeviceOnTheCpuPath()
    {
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => GpuAccelerationSyntax.Format(GpuAccelerationKind.Cpu, 1));
    }

    [TestMethod]
    public void ParseReadsBackWhatFormatWrote()
    {
        foreach (var kind in new[]
        {
            GpuAccelerationKind.Cpu,
            GpuAccelerationKind.Vulkan,
            GpuAccelerationKind.Direct3D12,
            GpuAccelerationKind.OpenGl,
            GpuAccelerationKind.Metal,
        })
        {
            for (var device = 0; device < 3; device++)
            {
                if (kind == GpuAccelerationKind.Cpu && device > 0)
                {
                    continue;
                }

                var value = GpuAccelerationSyntax.Format(kind, device);

                Assert.IsTrue(GpuAccelerationSyntax.TryParse(value, out var parsed, out var index), value);
                Assert.AreEqual(kind, parsed, value);
                Assert.AreEqual(device, index, value);
            }
        }
    }

    [TestMethod]
    public void ParseAcceptsTheSpellingsAUserMightWrite()
    {
        // The family words are the ones KOLVIR_GL_OPTIONS already uses; the rest are what a hand-edited
        // setting or an older document is likely to contain.
        AssertParses("D3D", GpuAccelerationKind.Direct3D12, 0);
        AssertParses(" d3d:1 ", GpuAccelerationKind.Direct3D12, 1);
        AssertParses("d3d12", GpuAccelerationKind.Direct3D12, 0);
        AssertParses("Direct3D", GpuAccelerationKind.Direct3D12, 0);
        AssertParses("dx12", GpuAccelerationKind.Direct3D12, 0);
        AssertParses("vulkan:3", GpuAccelerationKind.Vulkan, 3);
        AssertParses("gl", GpuAccelerationKind.OpenGl, 0);
        AssertParses("OpenGL", GpuAccelerationKind.OpenGl, 0);
        AssertParses("mtl", GpuAccelerationKind.Metal, 0);
        AssertParses("CPU", GpuAccelerationKind.Cpu, 0);
    }

    [TestMethod]
    public void ParseRejectsWhatNamesNoFamily()
    {
        // "cpu:1" is not a device index, and a suffix with a sign, a space or padding is not an index
        // either: both would otherwise be read as a device nobody asked for.
        foreach (var value in new string?[] { null, "", "   ", "d3d:", "d3d:-1", "d3d:+0", "d3d:1x", "cpu:1", "d3d: 1", "software", "9" })
        {
            Assert.IsFalse(GpuAccelerationSyntax.TryParse(value, out _, out _), value ?? "<null>");
        }
    }

    [TestMethod]
    public void AFamilyWithDevicesOffersOneOptionPerDevice()
    {
        var type = new GpuAccelerationType(
            GpuAccelerationKind.Direct3D12,
            "Direct3D 12",
            [new GpuAccelerationDevice(0, "Intel Arc"), new GpuAccelerationDevice(1, "RTX 5090")]);

        CollectionAssert.AreEqual(new[] { "d3d", "d3d:1" }, type.Options.Select(option => option.Value).ToArray());
        CollectionAssert.AreEqual(
            new[] { "Direct3D 12: Intel Arc", "Direct3D 12: RTX 5090" },
            type.Options.Select(option => option.Name).ToArray());
        Assert.AreEqual("RTX 5090", type.Options[1].Device?.Name);
    }

    [TestMethod]
    public void AFamilyWithoutDevicesOffersItself()
    {
        var type = new GpuAccelerationType(GpuAccelerationKind.OpenGl, "OpenGL");

        Assert.AreEqual(1, type.Options.Count);
        Assert.AreEqual("ogl", type.Options[0].Value);
        Assert.AreEqual("OpenGL", type.Options[0].Name);
        Assert.IsNull(type.Options[0].Device);
        Assert.IsFalse(type.HasDevices);
    }

    [TestMethod]
    public void ATypeFindsItsOwnOptionsOnly()
    {
        var type = new GpuAccelerationType(
            GpuAccelerationKind.Vulkan,
            "Vulkan",
            [new GpuAccelerationDevice(0, "GPU 0")]);

        Assert.IsTrue(type.TryFindOption("vulkan", out var option));
        Assert.AreEqual("vulkan", option?.Value);

        Assert.IsFalse(type.TryFindOption("vulkan:7", out _), "a device the family does not have");
        Assert.IsFalse(type.TryFindOption("d3d", out _), "another family's value");
    }

    [TestMethod]
    public void TheCpuPathIsAlwaysListedAndStartsCurrent()
    {
        var service = new TestAccelerationService(
            new GpuAccelerationType(GpuAccelerationKind.Direct3D12, "Direct3D 12", [new GpuAccelerationDevice(0, "GPU")]));

        CollectionAssert.AreEqual(
            new[] { GpuAccelerationKind.Direct3D12, GpuAccelerationKind.Cpu },
            service.Types.Select(type => type.Kind).ToArray(),
            "the CPU path is added last when the backend left it out");

        Assert.AreEqual("cpu", service.Current.Value, "nothing has drawn yet, so nothing is accelerated");
    }

    [TestMethod]
    public void AListingThatIncludesTheCpuPathDoesNotGetASecond()
    {
        var service = new TestAccelerationService(
            new GpuAccelerationType(GpuAccelerationKind.Vulkan, "Vulkan", [new GpuAccelerationDevice(0, "GPU")]),
            GpuAccelerationType.Cpu);

        Assert.AreEqual(1, service.Types.Count(type => type.Kind == GpuAccelerationKind.Cpu));
        Assert.IsTrue(service.Types[^1].Kind == GpuAccelerationKind.Cpu, "the CPU path stays last");
    }

    [TestMethod]
    public void ReportingAChoiceMovesCurrentAndRaisesOnce()
    {
        var service = new TestAccelerationService(
            new GpuAccelerationType(
                GpuAccelerationKind.Direct3D12,
                "Direct3D 12",
                [new GpuAccelerationDevice(0, "GPU 0"), new GpuAccelerationDevice(1, "GPU 1")]));

        var raised = 0;
        service.CurrentChanged += (_, _) => raised++;

        Assert.IsTrue(service.Report("d3d:1"));
        Assert.AreEqual("d3d:1", service.Current.Value);
        Assert.AreEqual("GPU 1", service.Current.Device?.Name);
        Assert.AreEqual(1, raised);

        Assert.IsFalse(service.Report("d3d:1"), "nothing changed, so nothing is raised");
        Assert.AreEqual(1, raised);

        Assert.IsTrue(service.Report("cpu"));
        Assert.AreEqual("cpu", service.Current.Value);
        Assert.AreEqual(2, raised);
    }

    [TestMethod]
    public void ReportingSomethingMachineDoesNotListIsIgnored()
    {
        // A window that fell back to a tier nobody enumerated must not leave Current pointing outside
        // Options: a settings list would have nothing to select.
        var service = new TestAccelerationService(
            new GpuAccelerationType(GpuAccelerationKind.Vulkan, "Vulkan", [new GpuAccelerationDevice(0, "GPU")]));

        service.Report("vulkan");
        Assert.AreEqual("vulkan", service.Current.Value);

        Assert.IsFalse(service.Report("metal"), "a family this machine does not have");
        Assert.AreEqual("vulkan", service.Current.Value);

        Assert.IsFalse(service.Report("vulkan:4"), "a device this machine does not have");
        Assert.AreEqual("vulkan", service.Current.Value);
    }

    [TestMethod]
    public void AnEmptyReportMeansTheCpuPath()
    {
        var service = new TestAccelerationService(
            new GpuAccelerationType(GpuAccelerationKind.Vulkan, "Vulkan", [new GpuAccelerationDevice(0, "GPU")]));

        service.Report("vulkan");
        Assert.IsTrue(service.Report(null));
        Assert.AreEqual("cpu", service.Current.Value);
    }

    [TestMethod]
    public void FindingAnOptionNeedsAMatchingDevice()
    {
        var service = new TestAccelerationService(
            new GpuAccelerationType(GpuAccelerationKind.Direct3D12, "Direct3D 12", [new GpuAccelerationDevice(0, "GPU")]));

        Assert.IsTrue(service.TryFindOption("d3d", out var found));
        Assert.AreEqual("d3d", found?.Value);

        Assert.IsFalse(service.TryFindOption("d3d:1", out _), "a GPU the machine does not list");
        Assert.IsFalse(service.TryFindOption(null, out _));
    }

    [TestMethod]
    public void TheEnumerationRunsOnce()
    {
        var service = new TestAccelerationService(
            new GpuAccelerationType(GpuAccelerationKind.Vulkan, "Vulkan", [new GpuAccelerationDevice(0, "GPU")]));

        _ = service.Types;
        _ = service.Options;
        _ = service.Current;
        _ = service.Types;

        Assert.AreEqual(1, service.EnumerationCount);
    }

    private static void AssertParses(string value, GpuAccelerationKind kind, int deviceIndex)
    {
        Assert.IsTrue(GpuAccelerationSyntax.TryParse(value, out var parsed, out var index), value);
        Assert.AreEqual(kind, parsed, value);
        Assert.AreEqual(deviceIndex, index, value);
    }

    /// <summary>
    /// A service over a fixed list, with the reporting the backends do exposed for the tests.
    /// </summary>
    private sealed class TestAccelerationService(params GpuAccelerationType[] types) : GpuAccelerationService
    {
        public int EnumerationCount { get; private set; }

        public bool Report(string? value) => ReportCurrent(value);

        protected override IReadOnlyList<GpuAccelerationType> Enumerate()
        {
            EnumerationCount++;
            return types;
        }
    }
}
