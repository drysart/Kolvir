using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace XPlatUI.Backend.Win32.Tests;

/// <summary>
/// The Win32 surface's fallback decision, driven through an injected probe. The accelerated path
/// itself needs a window, a driver and an OpenGL implementation; the decision that keeps a machine
/// without one alive does not, which is why it is the part worth a unit test on every build.
/// </summary>
[TestClass]
[TestCategory("Platform.Win32")]
public sealed class Win32GlFallbackTests
{
    private sealed class FailingProbe : IWin32GlProbe
    {
        public int Calls { get; private set; }

        public Win32GlContext? TryCreate(IntPtr hwnd)
        {
            Calls++;
            return null;
        }
    }

    [TestMethod]
    public void ADisabledOptionStaysOnTheCpuPathWithoutProbing()
    {
        var probe = new FailingProbe();

        var surface = Win32Gfx.TryCreate(
            hardwareAcceleration: false,
            new IntPtr(1),
            probe,
            width: 800,
            height: 600,
            out var reason);

        Assert.IsNull(surface, "the disabled option must not produce a GL surface");
        Assert.AreEqual(0, probe.Calls, "a disabled option must not even probe for a context");
        Assert.AreEqual("cpu (disabled)", reason);
    }

    [TestMethod]
    public void AFailingProbeFallsBackToTheCpuPath()
    {
        var probe = new FailingProbe();

        var surface = Win32Gfx.TryCreate(
            hardwareAcceleration: true,
            new IntPtr(1),
            probe,
            width: 800,
            height: 600,
            out var reason);

        Assert.IsNull(surface, "a machine with no GL must not get a surface");
        Assert.AreEqual(1, probe.Calls, "the enabled option asks the probe exactly once");
        Assert.AreEqual("cpu (no gl context)", reason);
        StringAssert.StartsWith(reason, "cpu", "the reason drives the demo's one-line diagnostic");
    }

    [TestMethod]
    public void AWindowLessSurfaceFallsBackWithoutProbing()
    {
        var probe = new FailingProbe();

        var surface = Win32Gfx.TryCreate(
            hardwareAcceleration: true,
            IntPtr.Zero,
            probe,
            width: 800,
            height: 600,
            out var reason);

        Assert.IsNull(surface);
        Assert.AreEqual(0, probe.Calls);
        Assert.AreEqual("cpu (no window)", reason);
    }

    [TestMethod]
    public void AnEmptySurfaceFallsBackWithoutProbing()
    {
        var probe = new FailingProbe();

        var surface = Win32Gfx.TryCreate(
            hardwareAcceleration: true,
            new IntPtr(1),
            probe,
            width: 0,
            height: 0,
            out var reason);

        Assert.IsNull(surface);
        Assert.AreEqual(0, probe.Calls);
        Assert.AreEqual("cpu (empty surface)", reason);
    }

    [TestMethod]
    public void AProbeThatThrowsIsTreatedAsNoContext()
    {
        var surface = Win32Gfx.TryCreate(
            hardwareAcceleration: true,
            new IntPtr(1),
            new ThrowingProbe(),
            width: 800,
            height: 600,
            out var reason);

        Assert.IsNull(surface);
        Assert.AreEqual("cpu (no gl context)", reason);
    }

    private sealed class ThrowingProbe : IWin32GlProbe
    {
        public Win32GlContext? TryCreate(IntPtr hwnd) => throw new InvalidOperationException("driver reset");
    }
}
