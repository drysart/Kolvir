using SkiaSharp;
using XPlatUI;
using XPlatUI.Backend.Headless;

namespace XPlatUI.Tests;

/// <summary>
/// The two values an application reads instead of assuming: the capability report and the metrics
/// snapshot.
/// </summary>
/// <remarks>
/// The seam's contract is "ask, do not guess", so both of these are values rather than live views -
/// which is what makes it safe to read one, look at it twice, or keep it. The assertions here pin
/// that shape and the fact that a snapshot is re-read rather than cached inside the model; the
/// per-backend numbers are asserted in the backends' own suites.
/// </remarks>
[TestClass]
[TestCategory("Headless")]
public sealed class ChromeCapabilityTests : HeadlessTestBase
{
    [TestMethod]
    public void CapabilitiesAreAValue()
    {
        Assert.AreEqual(XPlatChromeCapabilities.None, new XPlatChromeCapabilities(),
            "adopting nothing is the neutral value, which is also the default a backend starts from");

        var all = new XPlatChromeCapabilities
        {
            Borderless = true,
            CaptionButtons = true,
            WindowColors = true,
        };

        Assert.AreEqual(all, new XPlatChromeCapabilities
        {
            Borderless = true,
            CaptionButtons = true,
            WindowColors = true,
        }, "the same flags are the same capabilities");

        // Each flag answers for itself: a backend that can go borderless does not thereby promise
        // buttons or colours, and the report has to be able to say so.
        Assert.AreNotEqual(all, all with { Borderless = false });
        Assert.AreNotEqual(all, all with { CaptionButtons = false });
        Assert.AreNotEqual(all, all with { WindowColors = false });

        Assert.IsFalse(XPlatChromeCapabilities.None.Borderless);
        Assert.IsFalse(XPlatChromeCapabilities.None.CaptionButtons);
        Assert.IsFalse(XPlatChromeCapabilities.None.WindowColors);
    }

    [TestMethod]
    public void TheMetricsAreASnapshotThatIsReadAgain()
    {
        using var app = new XPlatApplication(
            new HeadlessApplicationBackend(),
            new XPlatApplicationOptions { ApplicationName = "tests" });

        var window = new XPlatWindow("snapshot", new XPlatPanel()) { ClientSize = new SKSize(400f, 300f) };

        var first = window.ChromeMetrics;
        var second = window.ChromeMetrics;

        // A value, not a live view: two reads of an unchanged window are equal, and keeping the first
        // one is not a way to watch the window change.
        Assert.AreEqual(first, second, "an unchanged window reports an equal snapshot");
        Assert.AreEqual(first.SurfaceInset, second.SurfaceInset);
        Assert.AreEqual(first.ResizeInset, second.ResizeInset);
        Assert.AreEqual(first.CornerRadius, second.CornerRadius);
        Assert.AreEqual(first.CaptionButtonBounds.Count, second.CaptionButtonBounds.Count);

        // Showing the window is what a backend would use to report a real size, so the snapshot is
        // re-read afterwards rather than assumed to be the same value.
        window.Show();
        var shown = window.ChromeMetrics;

        Assert.IsNotNull(shown.CaptionButtonBounds, "the list is never null, whatever the backend");
        Assert.AreEqual(XPlatCaptionButtons.None, shown.CaptionButtons,
            "the headless backend draws no buttons, and says so rather than echoing a request");
    }

    [TestMethod]
    public void WhatWasAskedForIsNotWhatIsReported()
    {
        using var app = new XPlatApplication(
            new HeadlessApplicationBackend(),
            new XPlatApplicationOptions { ApplicationName = "tests" });

        var window = new XPlatWindow("request", new XPlatPanel());

        // The request is refused at the call site, and the report never becomes "Platform" on its
        // own: the two are separate answers, which is why the metrics are the ones an application
        // lays out against.
        Assert.ThrowsExactly<NotSupportedException>(() => window.CaptionButtons = XPlatCaptionButtons.Platform);
        Assert.AreEqual(XPlatCaptionButtons.None, window.ChromeMetrics.CaptionButtons);
        Assert.AreEqual(XPlatThickness.Empty, window.ChromeMetrics.ResizeInset,
            "nothing is claimed, so an application may use its whole surface");
    }
}
