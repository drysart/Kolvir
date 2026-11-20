using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Kolvir.Tests;

/// <summary>
/// The acceleration contract an application builds a settings list from: the values it saves, the
/// two-level list it shows, and the choice it reads back as the live one.
/// </summary>
/// <remarks>
/// The values are asserted the way a saved setting uses them: the device part is a hardware identity, so
/// what matters is that it round-trips and that a position is never mistaken for one - not that it looks
/// like anything in particular.
/// </remarks>
[TestClass]
[TestCategory("Core")]
public sealed class GpuAccelerationTests
{
    private const string FirstCard = "10de-2504-1616";
    private const string SecondCard = "10de-2504-1617";

    [TestMethod]
    public void FormatWritesTheFamilyAndTheDeviceIdentity()
    {
        Assert.AreEqual("d3d", GpuAccelerationSyntax.Format(GpuAccelerationKind.Direct3D12));
        Assert.AreEqual("d3d:10de-2504-1616", GpuAccelerationSyntax.Format(GpuAccelerationKind.Direct3D12, FirstCard));
        Assert.AreEqual("cpu", GpuAccelerationSyntax.Format(GpuAccelerationKind.Cpu));
    }

    [TestMethod]
    public void FormatLowercasesTheIdentitySoASavedValueIsCanonical()
    {
        Assert.AreEqual("d3d:10de-2504-1616", GpuAccelerationSyntax.Format(GpuAccelerationKind.Direct3D12, "10DE-2504-1616"));
    }

    [TestMethod]
    public void FormatRefusesADeviceOnTheCpuPath()
    {
        Assert.ThrowsExactly<ArgumentException>(() => GpuAccelerationSyntax.Format(GpuAccelerationKind.Cpu, FirstCard));
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
            var bare = GpuAccelerationSyntax.Format(kind);
            Assert.IsTrue(GpuAccelerationSyntax.TryParse(bare, out var parsedKind, out var noKey), bare);
            Assert.AreEqual(kind, parsedKind, bare);
            Assert.IsNull(noKey, bare);

            if (kind == GpuAccelerationKind.Cpu)
            {
                continue;
            }

            var withDevice = GpuAccelerationSyntax.Format(kind, FirstCard);
            Assert.IsTrue(GpuAccelerationSyntax.TryParse(withDevice, out parsedKind, out var key), withDevice);
            Assert.AreEqual(kind, parsedKind, withDevice);
            Assert.AreEqual(FirstCard, key, withDevice);
        }
    }

    [TestMethod]
    public void ParseAcceptsTheSpellingsAUserMightWrite()
    {
        // The family words are the ones KOLVIR_GL_OPTIONS already uses; the rest are what a hand-edited
        // setting or an older document is likely to contain.
        AssertParses("D3D", GpuAccelerationKind.Direct3D12, null);
        AssertParses(" d3d:10DE-2504-1616 ", GpuAccelerationKind.Direct3D12, FirstCard);
        AssertParses("d3d12", GpuAccelerationKind.Direct3D12, null);
        AssertParses("Direct3D", GpuAccelerationKind.Direct3D12, null);
        AssertParses("dx12", GpuAccelerationKind.Direct3D12, null);
        AssertParses("vulkan:8f4a37c2", GpuAccelerationKind.Vulkan, "8f4a37c2");
        AssertParses("gl", GpuAccelerationKind.OpenGl, null);
        AssertParses("OpenGL", GpuAccelerationKind.OpenGl, null);
        AssertParses("mtl", GpuAccelerationKind.Metal, null);
        AssertParses("CPU", GpuAccelerationKind.Cpu, null);
    }

    [TestMethod]
    public void ParseRejectsWhatNamesNoFamilyOrNoIdentity()
    {
        // A suffix has to be an identity. An empty one, a sign, a space, a repeated dash or a character
        // that would need quoting in a config file all name nothing, rather than quietly meaning
        // "device zero".
        foreach (var value in new string?[] { null, "", "   ", "d3d:", "d3d:-1", "d3d:--1", "d3d:1x", "cpu:1", "d3d: 1", "software", "9" })
        {
            Assert.IsFalse(GpuAccelerationSyntax.TryParse(value, out _, out _), value ?? "<null>");
        }
    }

    [TestMethod]
    public void ADeviceIdentityIsATokenNotANumber()
    {
        Assert.IsTrue(GpuAccelerationSyntax.IsDeviceKey(FirstCard));
        Assert.IsTrue(GpuAccelerationSyntax.IsDeviceKey("8f4a37c2c5d14b1e9f3e0a2b7c6d5e4f"));
        Assert.IsTrue(GpuAccelerationSyntax.IsDeviceKey("10de-2504"));

        Assert.IsFalse(GpuAccelerationSyntax.IsDeviceKey(null));
        Assert.IsFalse(GpuAccelerationSyntax.IsDeviceKey(string.Empty));
        Assert.IsFalse(GpuAccelerationSyntax.IsDeviceKey("-"), "a dash alone separates nothing");
        Assert.IsFalse(GpuAccelerationSyntax.IsDeviceKey("10de--2504"), "two dashes are a typo, not a separator");
        Assert.IsFalse(GpuAccelerationSyntax.IsDeviceKey("10de-"), "a trailing dash is a typo");
        Assert.IsFalse(GpuAccelerationSyntax.IsDeviceKey("10de 2504"), "an identity holds no whitespace");
        Assert.IsFalse(GpuAccelerationSyntax.IsDeviceKey("10de:2504"), "a colon would be read as the family's separator");
        Assert.IsFalse(GpuAccelerationSyntax.IsDeviceKey(new string('a', 41)), "an identity is a short token");
    }

    [TestMethod]
    public void AFamilyWithDevicesOffersOneOptionPerDevice()
    {
        var type = new GpuAccelerationType(
            GpuAccelerationKind.Direct3D12,
            "Direct3D 12",
            [new GpuAccelerationDevice(FirstCard, "GeForce RTX 5090"), new GpuAccelerationDevice(SecondCard, "GeForce RTX 5090 (2)")]);

        CollectionAssert.AreEqual(
            new[] { $"d3d:{FirstCard}", $"d3d:{SecondCard}" },
            type.Options.Select(option => option.Value).ToArray());

        CollectionAssert.AreEqual(
            new[] { "Direct3D 12: GeForce RTX 5090", "Direct3D 12: GeForce RTX 5090 (2)" },
            type.Options.Select(option => option.Name).ToArray());

        Assert.AreEqual(SecondCard, type.Options[1].Device?.Key);
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
    public void ATypeFindsItsOwnDevicesOnly()
    {
        var type = new GpuAccelerationType(
            GpuAccelerationKind.Vulkan,
            "Vulkan",
            [new GpuAccelerationDevice(FirstCard, "GPU 0")]);

        Assert.IsTrue(type.TryFindOption($"vulkan:{FirstCard}", out var option));
        Assert.AreEqual($"vulkan:{FirstCard}", option?.Value);

        Assert.IsFalse(type.TryFindOption($"vulkan:{SecondCard}", out _), "a device the family does not have");
        Assert.IsFalse(type.TryFindOption($"d3d:{FirstCard}", out _), "another family's value");
    }

    [TestMethod]
    public void ABareFamilyWordNamesTheFirstDevice()
    {
        var type = new GpuAccelerationType(
            GpuAccelerationKind.Direct3D12,
            "Direct3D 12",
            [new GpuAccelerationDevice(FirstCard, "GPU 0"), new GpuAccelerationDevice(SecondCard, "GPU 1")]);

        Assert.IsTrue(type.TryFindOption("d3d", out var option));
        Assert.AreEqual(FirstCard, option?.Device?.Key, "a hand-written value still means something");
    }

    [TestMethod]
    public void TheCpuPathIsAlwaysListedAndStartsCurrent()
    {
        var service = new TestAccelerationService(
            new GpuAccelerationType(GpuAccelerationKind.Direct3D12, "Direct3D 12", [new GpuAccelerationDevice(FirstCard, "GPU")]));

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
            new GpuAccelerationType(GpuAccelerationKind.Vulkan, "Vulkan", [new GpuAccelerationDevice(FirstCard, "GPU")]),
            GpuAccelerationType.Cpu);

        Assert.AreEqual(1, service.Types.Count(type => type.Kind == GpuAccelerationKind.Cpu));
        Assert.AreEqual(GpuAccelerationKind.Cpu, service.Types[^1].Kind, "the CPU path stays last");
    }

    [TestMethod]
    public void ReportingAChoiceMovesCurrentAndRaisesOnce()
    {
        var service = new TestAccelerationService(
            new GpuAccelerationType(
                GpuAccelerationKind.Direct3D12,
                "Direct3D 12",
                [new GpuAccelerationDevice(FirstCard, "GPU 0"), new GpuAccelerationDevice(SecondCard, "GPU 1")]));

        var raised = 0;
        service.CurrentChanged += (_, _) => raised++;

        Assert.IsTrue(service.Report($"d3d:{SecondCard}"));
        Assert.AreEqual($"d3d:{SecondCard}", service.Current.Value);
        Assert.AreEqual("GPU 1", service.Current.Device?.Name);
        Assert.AreEqual(1, raised);

        Assert.IsFalse(service.Report($"d3d:{SecondCard}"), "nothing changed, so nothing is raised");
        Assert.AreEqual(1, raised);

        Assert.IsTrue(service.Report("cpu"));
        Assert.AreEqual("cpu", service.Current.Value);
        Assert.AreEqual(2, raised);
    }

    [TestMethod]
    public void ReportingSomethingTheMachineDoesNotListIsIgnored()
    {
        // A window that fell back to a tier nobody enumerated must not leave Current pointing outside
        // Options: a settings list would have nothing to select.
        var service = new TestAccelerationService(
            new GpuAccelerationType(GpuAccelerationKind.Vulkan, "Vulkan", [new GpuAccelerationDevice(FirstCard, "GPU")]));

        service.Report($"vulkan:{FirstCard}");
        Assert.AreEqual($"vulkan:{FirstCard}", service.Current.Value);

        Assert.IsFalse(service.Report("metal"), "a family this machine does not have");
        Assert.AreEqual($"vulkan:{FirstCard}", service.Current.Value);

        Assert.IsFalse(service.Report($"vulkan:{SecondCard}"), "a device this machine does not have");
        Assert.AreEqual($"vulkan:{FirstCard}", service.Current.Value);
    }

    [TestMethod]
    public void AnEmptyReportMeansTheCpuPath()
    {
        var service = new TestAccelerationService(
            new GpuAccelerationType(GpuAccelerationKind.Vulkan, "Vulkan", [new GpuAccelerationDevice(FirstCard, "GPU")]));

        service.Report($"vulkan:{FirstCard}");
        Assert.IsTrue(service.Report(null));
        Assert.AreEqual("cpu", service.Current.Value);
    }

    [TestMethod]
    public void FindingAnOptionNeedsAMatchingDevice()
    {
        var service = new TestAccelerationService(
            new GpuAccelerationType(GpuAccelerationKind.Direct3D12, "Direct3D 12", [new GpuAccelerationDevice(FirstCard, "GPU")]));

        Assert.IsTrue(service.TryFindOption($"d3d:{FirstCard}", out var found));
        Assert.AreEqual($"d3d:{FirstCard}", found?.Value);

        Assert.IsFalse(service.TryFindOption($"d3d:{SecondCard}", out _), "a GPU the machine does not list");
        Assert.IsFalse(service.TryFindOption(null, out _));
    }

    [TestMethod]
    public void TheEnumerationRunsOnce()
    {
        var service = new TestAccelerationService(
            new GpuAccelerationType(GpuAccelerationKind.Vulkan, "Vulkan", [new GpuAccelerationDevice(FirstCard, "GPU")]));

        _ = service.Types;
        _ = service.Options;
        _ = service.Current;
        _ = service.Types;

        Assert.AreEqual(1, service.EnumerationCount);
    }

    private static void AssertParses(string value, GpuAccelerationKind kind, string? deviceKey)
    {
        Assert.IsTrue(GpuAccelerationSyntax.TryParse(value, out var parsed, out var key), value);
        Assert.AreEqual(kind, parsed, value);
        Assert.AreEqual(deviceKey, key, value);
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
