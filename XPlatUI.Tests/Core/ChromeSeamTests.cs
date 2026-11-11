using SkiaSharp;
using XPlatUI;
using XPlatUI.Backend.Headless;

namespace XPlatUI.Tests;

/// <summary>
/// The chrome half of <see cref="IXPlatWindowBackend"/> as an application sees it: what a backend
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
    private static void WithWindow(Action<XPlatWindow> body)
    {
        var app = new XPlatApplication(
            new HeadlessApplicationBackend(),
            new XPlatApplicationOptions
            {
                ApplicationName = "tests",
                ShutdownMode = XPlatShutdownMode.OnLastWindowClose,
            });

        try
        {
            body(new XPlatWindow("chrome", new XPlatPanel()));
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
            Assert.AreEqual(XPlatWindowChrome.System, window.Chrome, "the ordinary window is the default");

            var metrics = window.ChromeMetrics;
            Assert.AreEqual(XPlatCaptionButtons.None, metrics.CaptionButtons, "no platform buttons are drawn");
            Assert.AreEqual(XPlatThickness.Empty, metrics.SurfaceInset, "the platform paints nothing");
            Assert.AreEqual(XPlatThickness.Empty, metrics.ResizeInset, "the platform claims nothing");
            Assert.AreEqual(0f, metrics.CornerRadius);
            Assert.AreEqual(0f, metrics.CornerResizeExtent);
            Assert.AreEqual(SKRect.Empty, metrics.CaptionButtonStrip);
            Assert.AreEqual(0, metrics.CaptionButtonBounds.Count, "no buttons, and never a null list");

            Assert.IsFalse(window.TryBeginDrag(), "no drag was started");
            Assert.IsFalse(window.TryBeginResize(XPlatResizeEdge.Left));
            Assert.IsFalse(window.TryBeginResize(XPlatResizeEdge.Left | XPlatResizeEdge.Top),
                "a corner is not special here either");
        });
    }

    [TestMethod]
    public void AskingAnUnimplementedBackendForChromeThrowsAtTheCallSite()
    {
        WithWindow(window =>
        {
            Assert.ThrowsExactly<NotSupportedException>(() => window.Chrome = XPlatWindowChrome.Borderless);
            Assert.ThrowsExactly<NotSupportedException>(() => window.CaptionButtons = XPlatCaptionButtons.Platform);
            Assert.ThrowsExactly<NotSupportedException>(() => window.CaptionColor = SKColors.Red);
            Assert.ThrowsExactly<NotSupportedException>(() => window.BorderColor = SKColors.Red);

            Assert.AreEqual(XPlatWindowChrome.System, window.Chrome, "a refused request leaves the window as it was");
            Assert.AreEqual(XPlatCaptionButtons.None, window.CaptionButtons);
            Assert.IsNull(window.CaptionColor);
            Assert.IsNull(window.BorderColor);
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
