using SkiaSharp;
using Kolvir;

namespace Kolvir.Tests;

/// <summary>
/// <see cref="WindowChromeMetrics"/>: the snapshot of what the platform still owns in a
/// borderless window.
/// </summary>
/// <remarks>
/// These are model tests, not backend tests. They pin the shape of the snapshot: a default window
/// reports that it owns nothing, every field survives a round trip, the button list is ordered and
/// never null, and the two insets stay independent of each other because they mean different things.
/// </remarks>
[TestClass]
[TestCategory("Headless")]
public sealed class ChromeMetricsTests
{
    [TestMethod]
    public void ADefaultSnapshotReportsThatThePlatformOwnsNothing()
    {
        var metrics = new WindowChromeMetrics();

        Assert.AreEqual(Thickness.Empty, metrics.SurfaceInset, "nothing is painted by the platform");
        Assert.AreEqual(Thickness.Empty, metrics.ResizeInset, "no pixel is claimed for resizing");
        Assert.AreEqual(0f, metrics.CornerResizeExtent);
        Assert.AreEqual(0f, metrics.CornerRadius, "a square window clips nothing");
        Assert.AreEqual(CaptionButtons.None, metrics.CaptionButtons);
        Assert.AreEqual(SKRect.Empty, metrics.CaptionButtonStrip);
        Assert.IsNotNull(metrics.CaptionButtonBounds, "the list is empty, never null");
        Assert.AreEqual(0, metrics.CaptionButtonBounds.Count);
    }

    [TestMethod]
    public void EveryFieldSurvivesARoundTrip()
    {
        var close = new CaptionButton(CaptionButtonKind.Close, new SKRect(946f, 0f, 1000f, 43f));
        var restore = new CaptionButton(CaptionButtonKind.Restore, new SKRect(903f, 0f, 946f, 43f));
        var minimize = new CaptionButton(CaptionButtonKind.Minimize, new SKRect(860f, 0f, 903f, 43f));

        var metrics = new WindowChromeMetrics
        {
            SurfaceInset = new Thickness(13f, 0f, 13f, 13f),
            ResizeInset = new Thickness(0f, 13f, 0f, 0f),
            CornerResizeExtent = 13f,
            CornerRadius = 8f,
            CaptionButtons = CaptionButtons.Platform,
            CaptionButtonSide = CaptionButtonSide.Right,
            CaptionButtonStrip = new SKRect(860f, 0f, 1000f, 43f),
            CaptionButtonBounds = [minimize, restore, close],
        };

        Assert.AreEqual(new Thickness(13f, 0f, 13f, 13f), metrics.SurfaceInset);
        Assert.AreEqual(new Thickness(0f, 13f, 0f, 0f), metrics.ResizeInset);
        Assert.AreEqual(13f, metrics.CornerResizeExtent);
        Assert.AreEqual(8f, metrics.CornerRadius);
        Assert.AreEqual(CaptionButtons.Platform, metrics.CaptionButtons);
        Assert.AreEqual(CaptionButtonSide.Right, metrics.CaptionButtonSide);
        Assert.AreEqual(new SKRect(860f, 0f, 1000f, 43f), metrics.CaptionButtonStrip);

        Assert.AreEqual(3, metrics.CaptionButtonBounds.Count);
        Assert.AreEqual(CaptionButtonKind.Minimize, metrics.CaptionButtonBounds[0].Kind, "drawing order is preserved");
        Assert.AreEqual(CaptionButtonKind.Restore, metrics.CaptionButtonBounds[1].Kind);
        Assert.AreEqual(CaptionButtonKind.Close, metrics.CaptionButtonBounds[2].Kind);
        Assert.AreEqual(new SKRect(946f, 0f, 1000f, 43f), metrics.CaptionButtonBounds[2].Bounds);
    }

    [TestMethod]
    public void TheTwoInsetsAreIndependent()
    {
        // GTK: the resize band is 12 px wide but lives outside the application's surface, so it costs
        // the application no pixels at all.
        var gtk = new WindowChromeMetrics
        {
            SurfaceInset = Thickness.Empty,
            ResizeInset = Thickness.Empty,
            CornerRadius = 12f,
        };

        // macOS: the application paints the whole window, and the platform still claims 3 pt of it.
        var mac = new WindowChromeMetrics
        {
            SurfaceInset = Thickness.Empty,
            ResizeInset = new Thickness(3f),
            CornerResizeExtent = 13f,
            CornerRadius = 11f,
        };

        // Win32: the platform paints a frame, so those pixels never reach the application, and the
        // band it still claims inside the client is along the top edge only.
        var win32 = new WindowChromeMetrics
        {
            SurfaceInset = new Thickness(13f, 0f, 13f, 13f),
            ResizeInset = new Thickness(0f, 13f, 0f, 0f),
            CornerResizeExtent = 13f,
        };

        Assert.IsTrue(gtk.SurfaceInset.IsEmpty && gtk.ResizeInset.IsEmpty,
            "GTK: the application owns every pixel and loses none to the band");
        Assert.IsTrue(mac.SurfaceInset.IsEmpty && mac.ResizeInset == new Thickness(3f),
            "macOS: the application paints everywhere but the platform still claims 3 pt");
        Assert.AreNotEqual(win32.SurfaceInset, win32.ResizeInset,
            "Win32: what is painted by the platform is not what it claims inside the client");
        Assert.AreEqual(0f, win32.SurfaceInset.Top, "Win32 has no top surface inset - the client covers the window top");
        Assert.AreEqual(13f, win32.ResizeInset.Top, "but the top band is still claimed inside the client");
    }

    [TestMethod]
    public void CaptionButtonIsAValueWithAKindAndARect()
    {
        var a = new CaptionButton(CaptionButtonKind.Close, new SKRect(0f, 0f, 10f, 10f));
        var b = new CaptionButton(CaptionButtonKind.Close, new SKRect(0f, 0f, 10f, 10f));
        var different = new CaptionButton(CaptionButtonKind.Close, new SKRect(0f, 0f, 10f, 11f));

        Assert.AreEqual(a, b, "the same kind and rect are the same button");
        Assert.AreNotEqual(a, different);
    }

    [TestMethod]
    public void ResizeEdgesCombineIntoCorners()
    {
        Assert.AreEqual(0, (int)ResizeEdge.None);
        Assert.AreEqual(ResizeEdge.Left | ResizeEdge.Top, ResizeEdge.Left | ResizeEdge.Top);

        var corner = ResizeEdge.Left | ResizeEdge.Top;
        Assert.IsTrue(corner.HasFlag(ResizeEdge.Left), "a corner names both of its edges");
        Assert.IsTrue(corner.HasFlag(ResizeEdge.Top));
        Assert.IsFalse(corner.HasFlag(ResizeEdge.Right));
        Assert.IsFalse(corner.HasFlag(ResizeEdge.Bottom));

        Assert.AreEqual(ResizeEdge.Right | ResizeEdge.Top, ResizeEdge.Top | ResizeEdge.Right,
            "order does not matter");
    }
}
