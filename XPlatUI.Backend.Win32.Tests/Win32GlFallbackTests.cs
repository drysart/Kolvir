using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace XPlatUI.Backend.Win32.Tests;

/// <summary>
/// The Win32 surface's fallback and flavour decisions, driven through injected probes. The
/// accelerated path itself needs a window, a driver and an OpenGL implementation; the decisions
/// that keep a machine without one alive - and that stop a failed probe from spending the real
/// window's one-shot pixel format - do not.
/// </summary>
[TestClass]
[TestCategory("Platform.Win32")]
public sealed class Win32GlFallbackTests
{
    private sealed class FakeContext : IDisposable
    {
        public bool Disposed { get; private set; }

        public void Dispose() => Disposed = true;
    }

    private sealed class FakeWindowFactory : IWin32ProbeWindowFactory
    {
        private readonly Queue<IntPtr> _windows;

        public FakeWindowFactory(params IntPtr[] windows) => _windows = new Queue<IntPtr>(windows);

        public List<IntPtr> Created { get; } = [];

        public List<IntPtr> Destroyed { get; } = [];

        public IntPtr Create()
        {
            var hwnd = _windows.Count > 0 ? _windows.Dequeue() : IntPtr.Zero;

            if (hwnd != IntPtr.Zero)
            {
                Created.Add(hwnd);
            }

            return hwnd;
        }

        public void Destroy(IntPtr hwnd) => Destroyed.Add(hwnd);
    }

    private sealed class ScriptedTester : IWin32GlFlavorTester
    {
        private readonly Dictionary<Win32GlFlavor, (bool Ok, string Failure)> _results;
        private readonly bool _throwOnGdi;

        public ScriptedTester(params (Win32GlFlavor Flavor, bool Ok, string Failure)[] results)
            : this(throwOnGdi: false, results)
        {
        }

        public ScriptedTester(bool throwOnGdi, params (Win32GlFlavor Flavor, bool Ok, string Failure)[] results)
        {
            _throwOnGdi = throwOnGdi;
            _results = results.ToDictionary(result => result.Flavor, result => (result.Ok, result.Failure));
        }

        public List<(Win32GlFlavor Flavor, IntPtr Hwnd)> Calls { get; } = [];

        public List<FakeContext> Contexts { get; } = [];

        public IDisposable? TryCreate(IntPtr hwnd, Win32GlFlavor flavor, out string failure)
        {
            Calls.Add((flavor, hwnd));

            if (_throwOnGdi && flavor == Win32GlFlavor.Gdi32)
            {
                throw new InvalidOperationException("driver reset");
            }

            var result = _results[flavor];
            failure = result.Failure;

            if (!result.Ok)
            {
                return null;
            }

            var context = new FakeContext();
            Contexts.Add(context);
            return context;
        }
    }

    private sealed class ScriptedProbe : IWin32GlProbe
    {
        public Win32GlFlavor? Flavor { get; set; }

        public string FlavorFailure { get; set; } = "no wgl context (error 0); gdi: no wgl context (error 0)";

        public string ContextFailure { get; set; } = "gdi: set pixel format failed (error 2000)";

        public bool ThrowOnSelect { get; set; }

        public int SelectCalls { get; private set; }

        public int CreateCalls { get; private set; }

        public IntPtr LastHwnd { get; private set; }

        public Win32GlFlavor? SelectFlavor(out string failure)
        {
            SelectCalls++;

            if (ThrowOnSelect)
            {
                throw new InvalidOperationException("driver reset");
            }

            failure = FlavorFailure;
            return Flavor;
        }

        public Win32GlContext? TryCreate(IntPtr hwnd, Win32GlFlavor flavor, out string failure)
        {
            CreateCalls++;
            LastHwnd = hwnd;
            failure = ContextFailure;
            return null;
        }
    }

    [TestMethod]
    public void Gdi32IsPreferredWhenItProducesAContext()
    {
        var tester = new ScriptedTester(
            (Win32GlFlavor.Gdi32, true, string.Empty),
            (Win32GlFlavor.Opengl32, true, string.Empty));
        var windows = new FakeWindowFactory(new IntPtr(11), new IntPtr(22));

        var flavor = Win32GlFlavorSelection.Select(tester, windows, out var failure);

        Assert.AreEqual(Win32GlFlavor.Gdi32, flavor);
        Assert.AreEqual(string.Empty, failure);
        Assert.AreEqual(1, tester.Calls.Count, "the second flavour is not tried when the first works");
        Assert.AreEqual(1, tester.Contexts.Count, "the winning probe context is disposed after use");
        Assert.IsTrue(tester.Contexts[0].Disposed);
        Assert.AreEqual(1, windows.Created.Count);
        Assert.AreEqual(1, windows.Destroyed.Count);
    }

    [TestMethod]
    public void Opengl32RescuesWhenGdi32SetAFormatButCouldNotCreateAContext()
    {
        // This is the Mesa shape: gdi32's SetPixelFormat succeeds at a system format number, then
        // Mesa's wglCreateContext rejects it. The throwaway window is spent; a fresh one is used
        // for the opengl32 flavour.
        var tester = new ScriptedTester(
            (Win32GlFlavor.Gdi32, false, "gdi: no wgl context (error 0)"),
            (Win32GlFlavor.Opengl32, true, string.Empty));
        var windows = new FakeWindowFactory(new IntPtr(11), new IntPtr(22));

        var flavor = Win32GlFlavorSelection.Select(tester, windows, out var failure);

        Assert.AreEqual(Win32GlFlavor.Opengl32, flavor);
        Assert.AreEqual(string.Empty, failure);
        Assert.AreEqual(2, tester.Calls.Count);
        Assert.AreEqual(Win32GlFlavor.Gdi32, tester.Calls[0].Flavor);
        Assert.AreEqual(Win32GlFlavor.Opengl32, tester.Calls[1].Flavor);
        Assert.AreNotEqual(tester.Calls[0].Hwnd, tester.Calls[1].Hwnd, "the poisoned window is not reused");
        Assert.AreEqual(2, windows.Created.Count);
        Assert.AreEqual(2, windows.Destroyed.Count);
        Assert.IsTrue(tester.Contexts[0].Disposed);
    }

    [TestMethod]
    public void BothFlavoursFailingGivesOneDiagnosticWithBothReasons()
    {
        var tester = new ScriptedTester(
            (Win32GlFlavor.Gdi32, false, "gdi: no pixel format (error 0)"),
            (Win32GlFlavor.Opengl32, false, "wgl: no wgl context (error 0)"));
        var windows = new FakeWindowFactory(new IntPtr(11), new IntPtr(22));

        var flavor = Win32GlFlavorSelection.Select(tester, windows, out var failure);

        Assert.IsNull(flavor);
        Assert.AreEqual("no wgl context (error 0); gdi: no pixel format (error 0)", failure);
        Assert.AreEqual(2, windows.Destroyed.Count);
    }

    [TestMethod]
    public void AThrowingFlavourDoesNotStopTheOther()
    {
        var tester = new ScriptedTester(
            throwOnGdi: true,
            (Win32GlFlavor.Gdi32, false, string.Empty),
            (Win32GlFlavor.Opengl32, true, string.Empty));
        var windows = new FakeWindowFactory(new IntPtr(11), new IntPtr(22));

        var flavor = Win32GlFlavorSelection.Select(tester, windows, out _);

        Assert.AreEqual(Win32GlFlavor.Opengl32, flavor);
        Assert.AreEqual(2, windows.Destroyed.Count);
    }

    [TestMethod]
    public void AProbeWindowThatCannotBeCreatedIsSkipped()
    {
        var tester = new ScriptedTester(
            (Win32GlFlavor.Gdi32, true, string.Empty),
            (Win32GlFlavor.Opengl32, true, string.Empty));
        var windows = new FakeWindowFactory(IntPtr.Zero, new IntPtr(22));

        var flavor = Win32GlFlavorSelection.Select(tester, windows, out _);

        Assert.AreEqual(Win32GlFlavor.Opengl32, flavor);
        Assert.AreEqual(1, tester.Calls.Count);
        Assert.AreEqual(new IntPtr(22), tester.Calls[0].Hwnd);
        CollectionAssert.AreEqual(new[] { new IntPtr(22) }, windows.Created.ToArray());
        CollectionAssert.AreEqual(new[] { new IntPtr(22) }, windows.Destroyed.ToArray());
    }

    [TestMethod]
    public void TheRealWindowIsNeverPassedToTheFlavourProbe()
    {
        var realWindow = new IntPtr(999);
        var tester = new ScriptedTester(
            (Win32GlFlavor.Gdi32, false, "gdi: no wgl context (error 0)"),
            (Win32GlFlavor.Opengl32, true, string.Empty));
        var windows = new FakeWindowFactory(new IntPtr(11), new IntPtr(22));

        Win32GlFlavorSelection.Select(tester, windows, out _);

        Assert.IsTrue(tester.Calls.All(call => call.Hwnd != realWindow));
        Assert.IsTrue(windows.Created.All(hwnd => hwnd != realWindow));
        Assert.AreEqual(0, windows.Created.Count(hwnd => hwnd == realWindow));
    }

    [TestMethod]
    public void ADisabledOptionStaysOnTheCpuPathWithoutProbing()
    {
        var probe = new ScriptedProbe();

        var surface = Win32Gfx.TryCreate(
            hardwareAcceleration: false,
            new IntPtr(1),
            probe,
            width: 800,
            height: 600,
            out var reason);

        Assert.IsNull(surface, "the disabled option must not produce a GL surface");
        Assert.AreEqual(0, probe.SelectCalls, "a disabled option must not even choose a flavour");
        Assert.AreEqual(0, probe.CreateCalls);
        Assert.AreEqual("cpu (disabled)", reason);
    }

    [TestMethod]
    public void AFailingProbeFallsBackWithTheStepItFailedAt()
    {
        var probe = new ScriptedProbe();

        var surface = Win32Gfx.TryCreate(
            hardwareAcceleration: true,
            new IntPtr(1),
            probe,
            width: 800,
            height: 600,
            out var reason);

        Assert.IsNull(surface, "a machine with no GL must not get a surface");
        Assert.AreEqual(1, probe.SelectCalls);
        Assert.AreEqual(0, probe.CreateCalls, "no context is attempted when no flavour works");
        StringAssert.StartsWith(reason, "cpu (no wgl context (error 0)");
        StringAssert.Contains(reason, "gdi: no wgl context (error 0)");
    }

    [TestMethod]
    public void ARealWindowContextFailureKeepsItsStepAndErrorCode()
    {
        var probe = new ScriptedProbe
        {
            Flavor = Win32GlFlavor.Gdi32,
            ContextFailure = "gdi: set pixel format failed (error 2000)",
        };

        var surface = Win32Gfx.TryCreate(
            hardwareAcceleration: true,
            new IntPtr(1),
            probe,
            width: 800,
            height: 600,
            out var reason);

        Assert.IsNull(surface);
        Assert.AreEqual(1, probe.SelectCalls);
        Assert.AreEqual(1, probe.CreateCalls);
        Assert.AreEqual("cpu (gdi: set pixel format failed (error 2000))", reason);
    }

    [TestMethod]
    public void AWindowLessSurfaceFallsBackWithoutProbing()
    {
        var probe = new ScriptedProbe();

        var surface = Win32Gfx.TryCreate(
            hardwareAcceleration: true,
            IntPtr.Zero,
            probe,
            width: 800,
            height: 600,
            out var reason);

        Assert.IsNull(surface);
        Assert.AreEqual(0, probe.SelectCalls);
        Assert.AreEqual("cpu (no window)", reason);
    }

    [TestMethod]
    public void AnEmptySurfaceFallsBackWithoutProbing()
    {
        var probe = new ScriptedProbe();

        var surface = Win32Gfx.TryCreate(
            hardwareAcceleration: true,
            new IntPtr(1),
            probe,
            width: 0,
            height: 0,
            out var reason);

        Assert.IsNull(surface);
        Assert.AreEqual(0, probe.SelectCalls);
        Assert.AreEqual("cpu (empty surface)", reason);
    }

    [TestMethod]
    public void AProbeThatThrowsIsTreatedAsNoContext()
    {
        var probe = new ScriptedProbe { ThrowOnSelect = true };

        var surface = Win32Gfx.TryCreate(
            hardwareAcceleration: true,
            new IntPtr(1),
            probe,
            width: 800,
            height: 600,
            out var reason);

        Assert.IsNull(surface);
        Assert.AreEqual("cpu (gl probe threw InvalidOperationException)", reason);
    }
}
