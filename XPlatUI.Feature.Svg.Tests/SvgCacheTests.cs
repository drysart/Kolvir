namespace XPlatUI.Svg.Tests;

/// <summary>
/// Parsing is the expensive half of loading an icon, and every control that shows the same icon
/// would re-parse it without a cache. These tests count parses rather than time, because a
/// measurement of "once" is the only kind that cannot pass by accident.
/// </summary>
[TestClass]
[TestCategory("Feature")]
[TestCategory("Svg")]
public sealed class SvgCacheTests : SvgTestBase
{
    [TestMethod]
    public void OneDocumentLoadedTwiceIsParsedOnce()
    {
        using var registered = RegisterSvg();
        XPlatSvg.ClearCache();
        int parsed = XPlatSvgCache.ParseCount;

        for (int load = 0; load < 5; load++)
        {
            using var image = new XPlatImage();
            Assert.IsTrue(image.Load(SvgFixtures.Stream(SvgFixtures.Square)), $"load {load} should succeed");
        }

        Assert.AreEqual(parsed + 1, XPlatSvgCache.ParseCount, "the document is parsed once and recorded five times");
        Assert.AreEqual(1, XPlatSvgCache.Count, "and the cache holds one entry for it");
    }

    [TestMethod]
    public void TwoDifferentDocumentsAreTwoEntries()
    {
        using var registered = RegisterSvg();
        XPlatSvg.ClearCache();
        int parsed = XPlatSvgCache.ParseCount;

        using (var first = new XPlatImage())
        {
            Assert.IsTrue(first.Load(SvgFixtures.Stream(SvgFixtures.Square)), "the square loads");
        }

        using (var second = new XPlatImage())
        {
            Assert.IsTrue(second.Load(SvgFixtures.Stream(SvgFixtures.WideRectangle)), "the rectangle loads");
        }

        Assert.AreEqual(parsed + 2, XPlatSvgCache.ParseCount, "different bytes are a different parse");
        Assert.AreEqual(2, XPlatSvgCache.Count, "and two entries");
    }

    [TestMethod]
    public void EveryLoadGetsAPictureItCanDispose()
    {
        // Two controls showing one icon must not share one picture: a loaded picture belongs to the
        // control, so the second load disposing would take the first control's picture with it.
        using var registered = RegisterSvg();
        XPlatSvg.ClearCache();

        var first = new XPlatImage();
        var second = new XPlatImage();

        Assert.IsTrue(first.Load(SvgFixtures.Stream(SvgFixtures.Square)), "the first control loads");
        Assert.IsTrue(second.Load(SvgFixtures.Stream(SvgFixtures.Square)), "the second loads the same document");

        Assert.AreNotSame(first.Picture, second.Picture, "each control gets its own recording");

        second.Dispose();

        using var scene = SvgScene.Create(first, 40f, 40f);
        Assert.AreEqual(SvgFixtures.Blue, scene.PixelAt(20, 20), "and the first still draws after the second is gone");
    }

    [TestMethod]
    public void TheCacheIsBounded()
    {
        using var registered = RegisterSvg();
        XPlatSvg.ClearCache();

        // More distinct documents than the cache holds: the oldest go, and nothing throws while it
        // happens. A cache that only grew would be a leak with a hash function in front of it.
        for (int index = 0; index < XPlatSvgCache.Capacity + 8; index++)
        {
            using var image = new XPlatImage();
            Assert.IsTrue(
                image.Load(SvgFixtures.Stream(
                    $"""<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 10 10"><rect width="{index + 1}" height="10"/></svg>""")),
                $"document {index} should load");
        }

        Assert.IsLessThanOrEqualTo(XPlatSvgCache.Capacity, XPlatSvgCache.Count, "the cache stops at its capacity");
    }

    [TestMethod]
    public void ClearingTheCacheDoesNotDisturbAPictureAlreadyDrawn()
    {
        using var registered = RegisterSvg();
        var image = new XPlatImage { Dock = DockStyle.Fill, SizeMode = XPlatSizeMode.StretchImage };
        Assert.IsTrue(image.Load(SvgFixtures.Stream(SvgFixtures.Square)), "loads");

        XPlatSvg.ClearCache();

        using var scene = SvgScene.Create(image, 40f, 40f);
        Assert.AreEqual(SvgFixtures.Blue, scene.PixelAt(20, 20), "the control's recording outlives the parsed document");
    }
}
