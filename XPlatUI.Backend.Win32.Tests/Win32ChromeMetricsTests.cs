using Microsoft.VisualStudio.TestTools.UnitTesting;
using XPlatUI;

namespace XPlatUI.Backend.Win32.Tests;

/// <summary>
/// The mapping from the frame the window actually got to the snapshot the seam reports.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="Win32WindowBackend.ChromeMetricsFor"/> is the part of the borderless work that can be
/// checked without a window: it takes the measured frame in device pixels and the display scale,
/// and produces the logical insets an application lays out against.
/// </para>
/// <para>
/// The numbers asserted here are the ones measured against a live window at 192 DPI: window insets
/// of 13 device pixels on the left, right and bottom and 0 at the top, with a 13 pixel band claimed
/// inside the client along the top edge only. Everything else follows from converting those to
/// logical pixels.
/// </para>
/// </remarks>
[TestClass]
[TestCategory("Platform.Win32")]
public sealed class Win32ChromeMetricsTests
{
    [TestMethod]
    public void ADecoratedWindowReportsThatThePlatformOwnsNothing()
    {
        var metrics = Win32WindowBackend.ChromeMetricsFor(
            borderless: false, frameWidth: 13, frameHeight: 13, scale: 2f, maximized: false);

        Assert.AreEqual(XPlatThickness.Empty, metrics.SurfaceInset);
        Assert.AreEqual(XPlatThickness.Empty, metrics.ResizeInset);
        Assert.AreEqual(0f, metrics.CornerResizeExtent);
        Assert.AreEqual(0f, metrics.CornerRadius);
        Assert.AreEqual(XPlatCaptionButtons.None, metrics.CaptionButtons);
        Assert.AreEqual(0, metrics.CaptionButtonBounds.Count);
    }

    [TestMethod]
    public void ABorderlessWindowAt192DpiSplitsItsInsetsAsMeasured()
    {
        // 13 device pixels at scale 2 is 6.5 logical, which is what the window really has.
        var metrics = Win32WindowBackend.ChromeMetricsFor(
            borderless: true, frameWidth: 13, frameHeight: 13, scale: 2f, maximized: false);

        Assert.AreEqual(new XPlatThickness(6.5f, 0f, 6.5f, 6.5f), metrics.SurfaceInset,
            "the system paints the frame on three edges and the client owns the top");
        Assert.AreEqual(new XPlatThickness(0f, 6.5f, 0f, 0f), metrics.ResizeInset,
            "the band takes application pixels along the top edge only");
        Assert.AreEqual(6.5f, metrics.CornerResizeExtent);
        Assert.AreEqual(0f, metrics.CornerRadius, "Windows exposes no radius, so nothing may clip the app");
        Assert.AreEqual(XPlatCaptionButtons.None, metrics.CaptionButtons, "step 5 adds emulated buttons");
    }

    [TestMethod]
    public void At96DpiTheInsetsAreTheDeviceNumbers()
    {
        var metrics = Win32WindowBackend.ChromeMetricsFor(
            borderless: true, frameWidth: 13, frameHeight: 13, scale: 1f, maximized: false);

        Assert.AreEqual(new XPlatThickness(13f, 0f, 13f, 13f), metrics.SurfaceInset);
        Assert.AreEqual(new XPlatThickness(0f, 13f, 0f, 0f), metrics.ResizeInset);
    }

    [TestMethod]
    public void AMaximizedWindowClaimsNoResizeBand()
    {
        var metrics = Win32WindowBackend.ChromeMetricsFor(
            borderless: true, frameWidth: 13, frameHeight: 13, scale: 2f, maximized: true);

        Assert.AreEqual(XPlatThickness.Empty, metrics.ResizeInset,
            "the screen edges are not resizable, so nothing is claimed there");
        Assert.AreEqual(0f, metrics.CornerResizeExtent);
        Assert.AreEqual(new XPlatThickness(6.5f, 0f, 6.5f, 6.5f), metrics.SurfaceInset,
            "the frame is still painted on three edges when maximized");
    }

    [TestMethod]
    public void AnUnmeasuredFrameReportsZerosRatherThanNegativeNumbers()
    {
        // Before the first WM_NCCALCSIZE with NCCALCSIZE_PARAMS the frame is unknown.
        var metrics = Win32WindowBackend.ChromeMetricsFor(
            borderless: true, frameWidth: 0, frameHeight: 0, scale: 2f, maximized: false);

        Assert.AreEqual(XPlatThickness.Empty, metrics.SurfaceInset);
        Assert.AreEqual(XPlatThickness.Empty, metrics.ResizeInset);
        Assert.AreEqual(0f, metrics.CornerResizeExtent);
    }

    [TestMethod]
    public void AZeroScaleDoesNotProduceInfinities()
    {
        var metrics = Win32WindowBackend.ChromeMetricsFor(
            borderless: true, frameWidth: 13, frameHeight: 13, scale: 0f, maximized: false);

        Assert.AreEqual(new XPlatThickness(13f, 0f, 13f, 13f), metrics.SurfaceInset,
            "a display that has not reported a scale yet is treated as 96 DPI");
    }
}
