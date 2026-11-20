using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Kolvir.Backend.Win32.Tests;

/// <summary>
/// <c>KOLVIR_GL_OPTIONS</c>: the tiers a window may use, in the order it may use them, or the CPU path.
/// </summary>
/// <remarks>
/// The parser is exercised directly, which is what makes the awkward cases - a stray word, a repeated
/// tier, <c>cpu</c> beside a tier - answerable without a driver. The window's use of the result is
/// pinned separately, because the order it asks in is the whole point of the variable.
/// </remarks>
[TestClass]
[TestCategory("Platform.Win32")]
public sealed class Win32GfxOptionsTests
{
    private sealed class RefusingVulkanProbe : IWin32VulkanProbe
    {
        private readonly List<string> _order;

        public RefusingVulkanProbe(List<string> order) => _order = order;

        public int Calls { get; private set; }

        public Win32VulkanDevice? TryCreate(IntPtr hwnd, string? deviceKey, out string failure)
        {
            Calls++;
            _order.Add("vulkan");
            failure = "no physical devices";
            return null;
        }
    }

    private sealed class RefusingD3D12Probe : IWin32D3D12Probe
    {
        private readonly List<string> _order;

        public RefusingD3D12Probe(List<string> order) => _order = order;

        public int Calls { get; private set; }

        public Win32D3D12Device? TryCreate(IntPtr hwnd, string? deviceKey, out string failure)
        {
            Calls++;
            _order.Add("d3d12");
            failure = "d3d12: no hardware adapter";
            return null;
        }
    }

    private sealed class RefusingGlProbe : IWin32GlProbe
    {
        private readonly List<string> _order;

        public RefusingGlProbe(List<string> order) => _order = order;

        public int SelectCalls { get; private set; }

        public Win32GlFlavor? SelectFlavor(out string failure)
        {
            SelectCalls++;
            _order.Add("gl");
            failure = "no gl interface";
            return null;
        }

        public Win32GlContext? TryCreate(IntPtr hwnd, Win32GlFlavor flavor, out string failure)
        {
            failure = "no gl interface";
            return null;
        }
    }

    private static string Names(Win32GfxChoice choice) =>
        string.Join(",", choice.Order.Select(tier => tier switch
        {
            Win32GfxTier.Direct3D12 => "d3d",
            Win32GfxTier.Vulkan => "vulkan",
            _ => "ogl",
        }));

    [TestMethod]
    public void AnUnsetValueIsEveryTierWithDirect3DFirst()
    {
        Assert.AreEqual("d3d,vulkan,ogl", Names(Win32GfxOptions.Parse(null)));
        Assert.AreEqual("d3d,vulkan,ogl", Names(Win32GfxOptions.Parse(string.Empty)));
        Assert.AreEqual("d3d,vulkan,ogl", Names(Win32GfxOptions.Parse("   ")));
    }

    [TestMethod]
    public void TheOrderIsTheOrderWritten()
    {
        Assert.AreEqual("vulkan,ogl,d3d", Names(Win32GfxOptions.Parse("vulkan,ogl,d3d")));
        Assert.AreEqual("ogl,vulkan", Names(Win32GfxOptions.Parse("ogl,vulkan")));
    }

    [TestMethod]
    public void ATierLeftOutIsNotTried()
    {
        var choice = Win32GfxOptions.Parse("ogl");

        Assert.AreEqual("ogl", Names(choice));
        Assert.IsFalse(choice.CpuOnly);
    }

    [TestMethod]
    public void CpuAloneAsksForTheCpuPath()
    {
        var choice = Win32GfxOptions.Parse("cpu");

        Assert.IsTrue(choice.CpuOnly);
        Assert.AreEqual(0, choice.Order.Count, "no tier is left to try");
    }

    [TestMethod]
    public void CpuBesideATierIsIgnored()
    {
        var choice = Win32GfxOptions.Parse("cpu,d3d");

        Assert.IsFalse(choice.CpuOnly, "a list that names a tier is asking for acceleration");
        Assert.AreEqual("d3d", Names(choice));
    }

    [TestMethod]
    public void AnInvalidEntryIsIgnored()
    {
        Assert.AreEqual("d3d,vulkan", Names(Win32GfxOptions.Parse("d3d,bogus,vulkan")));
        Assert.AreEqual("d3d,ogl", Names(Win32GfxOptions.Parse("d3d,,ogl")), "an empty entry is ignored too");
        Assert.AreEqual("d3d,vulkan,ogl", Names(Win32GfxOptions.Parse("bogus")), "nothing recognized leaves the default");
        Assert.IsTrue(Win32GfxOptions.Parse("cpu,bogus").CpuOnly, "cpu alone among the recognized words stands");
    }

    [TestMethod]
    public void ARepeatedTierKeepsItsFirstPlace()
    {
        Assert.AreEqual("vulkan,d3d", Names(Win32GfxOptions.Parse("vulkan,d3d,vulkan")));
    }

    [TestMethod]
    public void TheWordsIgnoreCaseAndSpacing()
    {
        Assert.AreEqual("d3d,vulkan", Names(Win32GfxOptions.Parse(" D3D , Vulkan ")));
        Assert.IsTrue(Win32GfxOptions.Parse(" CPU ").CpuOnly);
    }

    [TestMethod]
    public void TheWindowAsksInTheOrderTheChoiceGives()
    {
        List<string> order = [];
        var gl = new RefusingGlProbe(order);
        var vulkan = new RefusingVulkanProbe(order);
        var d3d12 = new RefusingD3D12Probe(order);

        var surface = Win32Gfx.TryCreateAccelerated(
            Win32GfxChoice.FromOrder(Win32GfxTier.OpenGl, Win32GfxTier.Direct3D12),
            hardwareAcceleration: true,
            new IntPtr(1),
            gl,
            vulkan,
            d3d12,
            width: 800,
            height: 600,
            out var reason);

        Assert.IsNull(surface);
        CollectionAssert.AreEqual(new[] { "gl", "d3d12" }, order, "the listed tiers, in the listed order");
        Assert.AreEqual(0, vulkan.Calls, "a tier the list leaves out is not asked at all");
        Assert.AreEqual("cpu (no gl interface; d3d12: no hardware adapter)", reason);
    }

    [TestMethod]
    public void TheCpuPathTouchesNoTier()
    {
        List<string> order = [];
        var gl = new RefusingGlProbe(order);
        var vulkan = new RefusingVulkanProbe(order);
        var d3d12 = new RefusingD3D12Probe(order);

        var surface = Win32Gfx.TryCreateAccelerated(
            Win32GfxChoice.Cpu,
            hardwareAcceleration: true,
            new IntPtr(1),
            gl,
            vulkan,
            d3d12,
            width: 800,
            height: 600,
            out var reason);

        Assert.IsNull(surface);
        Assert.AreEqual(0, order.Count, "no probe is consulted");
        Assert.AreEqual("cpu (KOLVIR_GL_OPTIONS=cpu)", reason);
    }

    [TestMethod]
    public void NoSavedChoiceLeavesTheBackendToPick()
    {
        // The saved setting is optional: without it the variable and the default order decide, exactly
        // as they did before the setting existed.
        Assert.IsNull(Win32GfxOptions.FromSelected(null));
        Assert.IsNull(Win32GfxOptions.FromSelected(string.Empty));
        Assert.IsNull(Win32GfxOptions.FromSelected("   "));
    }

    [TestMethod]
    public void ASavedChoiceNamesOneFamilyOnly()
    {
        var choice = Win32GfxOptions.FromSelected("vulkan");

        Assert.IsTrue(choice.HasValue);
        Assert.AreEqual("vulkan", Names(choice.Value));
        Assert.IsNull(choice.Value.DeviceKey, "no device was named, so the window picks one");
    }

    [TestMethod]
    public void ASavedChoiceCarriesItsDevice()
    {
        var choice = Win32GfxOptions.FromSelected("d3d:10de-2504-1616");

        Assert.IsTrue(choice.HasValue);
        Assert.AreEqual("d3d", Names(choice.Value));
        Assert.AreEqual("10de-2504-1616", choice.Value.DeviceKey);
    }

    [TestMethod]
    public void ABareFamilyWordNamesNoDevice()
    {
        // "d3d" means whichever adapter the window would have picked, not "the first one": the tier then
        // prefers the adapter that drives the display, as it always did.
        var choice = Win32GfxOptions.FromSelected("d3d");

        Assert.IsTrue(choice.HasValue);
        Assert.IsNull(choice.Value.DeviceKey);
    }

    [TestMethod]
    public void ASavedCpuChoiceIsTheCpuPath()
    {
        Assert.IsTrue(Win32GfxOptions.FromSelected("cpu")?.CpuOnly);
    }

    [TestMethod]
    public void ASavedChoiceThisMachineCannotHonorIsTheCpuPath()
    {
        // Metal is a Mac's setting and a hand-edited word is nobody's: both are "not available here",
        // and the answer to a request that cannot be honored is to stop accelerating.
        Assert.IsTrue(Win32GfxOptions.FromSelected("metal")?.CpuOnly);
        Assert.IsTrue(Win32GfxOptions.FromSelected("software")?.CpuOnly);
    }

    [TestMethod]
    public void AChosenFamilyThatCannotBeBuiltDoesNotFallThrough()
    {
        List<string> order = [];
        var gl = new RefusingGlProbe(order);
        var vulkan = new RefusingVulkanProbe(order);
        var d3d12 = new RefusingD3D12Probe(order);

        var surface = Win32Gfx.TryCreateAccelerated(
            Win32GfxOptions.FromSelected("vulkan")!.Value,
            hardwareAcceleration: true,
            new IntPtr(1),
            gl,
            vulkan,
            d3d12,
            width: 800,
            height: 600,
            out var reason);

        Assert.IsNull(surface);
        CollectionAssert.AreEqual(new[] { "vulkan" }, order, "a chosen family is not replaced by another");
        StringAssert.Contains(reason, "no physical devices", "the reason names what the chosen family said");
    }

    [TestMethod]
    public void AVulkanPreferenceIsTriedFirstAndThenTheRest()
    {
        string[] keys = ["10de-2504-1616", "8086-46a6-0", "10de-2504-1617"];

        CollectionAssert.AreEqual(new[] { 1, 0, 2 }, Win32VulkanDevice.PreferenceOrder(3, keys, "8086-46a6-0").ToArray());
        CollectionAssert.AreEqual(new[] { 0, 1, 2 }, Win32VulkanDevice.PreferenceOrder(3, keys, preferredKey: null).ToArray());
        CollectionAssert.AreEqual(new[] { 0, 1, 2 }, Win32VulkanDevice.PreferenceOrder(3, keys, "not-here").ToArray(), "a device this machine does not have");
    }
}
