using SkiaSharp;
using Kolvir;
using Kolvir.Backend.Headless;

namespace Kolvir.Tests;

/// <summary>
/// The chrome half of <see cref="IWindowBackend"/> as an application sees it: what a backend
/// that has not adopted the feature reports, and what it does when asked for it anyway.
/// </summary>
/// <remarks>
/// <para>
/// Step 2 of the chromeless window work adds the seam members and nothing else. The headless backend
/// implements none of them, so every assertion here is against the interface's default
/// implementation - which is exactly the state a backend starts in before its own step lands.
/// </para>
/// <para>
/// The defaults refuse rather than no-op: a borderless request against a backend that cannot deliver
/// one throws at the call site, because a window that silently stays decorated is the failure mode
/// that costs an afternoon. Reporting, dragging and resizing degrade instead of throwing, since the
/// application can act sensibly on "no".
/// </para>
/// </remarks>
[TestClass]
[TestCategory("Headless")]
public sealed class ChromeSeamTests : HeadlessTestBase
{
    private static void WithWindow(Action<Window> body)
    {
        var app = new Application(
            new HeadlessApplicationBackend(),
            new ApplicationOptions
            {
                ApplicationName = "tests",
                ShutdownMode = ShutdownMode.OnLastWindowClose,
            });

        try
        {
            body(new Window("chrome", new Panel()));
        }
        finally
        {
            app.Dispose();
        }
    }

    [TestMethod]
    public void ABackendWithoutChromeSupportReportsSystemAndNoButtons()
    {
        WithWindow(window =>
        {
            Assert.AreEqual(WindowChrome.System, window.Chrome, "the ordinary window is the default");

            var metrics = window.ChromeMetrics;
            Assert.AreEqual(CaptionButtons.None, metrics.CaptionButtons, "no platform buttons are drawn");
            Assert.AreEqual(Thickness.Empty, metrics.SurfaceInset, "the platform paints nothing");
            Assert.AreEqual(Thickness.Empty, metrics.ResizeInset, "the platform claims nothing");
            Assert.AreEqual(0f, metrics.CornerRadius);
            Assert.AreEqual(0f, metrics.CornerResizeExtent);
            Assert.AreEqual(SKRect.Empty, metrics.CaptionButtonStrip);
            Assert.AreEqual(0, metrics.CaptionButtonBounds.Count, "no buttons, and never a null list");

            Assert.IsFalse(window.TryBeginDrag(), "no drag was started");
            Assert.IsFalse(window.TryBeginResize(ResizeEdge.Left));
            Assert.IsFalse(window.TryBeginResize(ResizeEdge.Left | ResizeEdge.Top),
                "a corner is not special here either");
        });
    }

    [TestMethod]
    public void AskingAnUnimplementedBackendForChromeThrowsAtTheCallSite()
    {
        WithWindow(window =>
        {
            Assert.ThrowsExactly<NotSupportedException>(() => window.Chrome = WindowChrome.Borderless);
            Assert.ThrowsExactly<NotSupportedException>(() => window.CaptionButtons = CaptionButtons.Platform);
            Assert.ThrowsExactly<NotSupportedException>(() => window.CaptionColor = SKColors.Red);
            Assert.ThrowsExactly<NotSupportedException>(() => window.BorderColor = SKColors.Red);

            Assert.AreEqual(WindowChrome.System, window.Chrome, "a refused request leaves the window as it was");
            Assert.AreEqual(CaptionButtons.None, window.CaptionButtons);
            Assert.IsNull(window.CaptionColor);
            Assert.IsNull(window.BorderColor);
        });
    }

    [TestMethod]
    public void CapabilitiesAnswerWithoutThrowingAndMatchWhatTheBackendDoes()
    {
        WithWindow(window =>
        {
            var capabilities = window.ChromeCapabilities;

            Assert.AreEqual(ChromeCapabilities.None, capabilities, "a backend that has adopted nothing says so");
            Assert.IsFalse(capabilities.Borderless);
            Assert.IsFalse(capabilities.CaptionButtons);
            Assert.IsFalse(capabilities.WindowColors);

            // The whole point of the report is that the answer matches the behaviour: an application
            // reads the capability, decides to stay decorated, and never reaches the throw.
            Assert.ThrowsExactly<NotSupportedException>(() => window.Chrome = WindowChrome.Borderless);
        });
    }

    [TestMethod]
    public void TheMetricsHandlerCanBeSubscribedToAndDisposed()
    {
        WithWindow(window =>
        {
            var calls = 0;
            using (window.AddChromeMetricsChangedHandler(_ => calls++))
            {
                // A backend whose snapshot never moves never fires, which is what "nothing to
                // report" has to mean; the subscription itself must still be well behaved.
            }

            Assert.AreEqual(0, calls, "a fixed snapshot never reports a change");

            window.RemoveChromeMetricsChangedHandler(_ => { });
            Assert.ThrowsExactly<ArgumentNullException>(() => window.AddChromeMetricsChangedHandler(null!));
        });
    }
}
