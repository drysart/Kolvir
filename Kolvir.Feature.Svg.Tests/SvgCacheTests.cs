namespace Kolvir.Svg.Tests;

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
        Svg.ClearCache();
        int parsed = SvgCache.ParseCount;

        for (int load = 0; load < 5; load++)
        {
            using var data = Factory.LoadFromStream(SvgFixtures.Stream(SvgFixtures.Square));
        }

        Assert.AreEqual(parsed + 1, SvgCache.ParseCount, "the document is parsed once and recorded five times");
        Assert.AreEqual(1, SvgCache.Count, "and the cache holds one entry for it");
    }

    [TestMethod]
    public void TwoDifferentDocumentsAreTwoEntries()
    {
        Svg.ClearCache();
        int parsed = SvgCache.ParseCount;

        using (Factory.LoadFromStream(SvgFixtures.Stream(SvgFixtures.Square)))
        {
        }

        using (Factory.LoadFromStream(SvgFixtures.Stream(SvgFixtures.WideRectangle)))
        {
        }

        Assert.AreEqual(parsed + 2, SvgCache.ParseCount, "different bytes are a different parse");
        Assert.AreEqual(2, SvgCache.Count, "and two entries");
    }

    [TestMethod]
    public void EveryLoadGetsItsOwnRecording()
    {
        // Two controls showing one icon must not share one picture: the data owns what it was given,
        // so two loads of the same document are two recordings of one parse.
        Svg.ClearCache();

        using var first = Factory.LoadFromStream(SvgFixtures.Stream(SvgFixtures.Square));
        using var second = Factory.LoadFromStream(SvgFixtures.Stream(SvgFixtures.Square));

        Assert.AreNotSame(first.CurrentFrame, second.CurrentFrame, "each load gets its own recording");

        var image = new Image { Source = first, Dock = DockStyle.Fill, SizeMode = SizeMode.StretchImage };
        second.Dispose();

        using var scene = SvgScene.Create(image, 40f, 40f);
        Assert.AreEqual(SvgFixtures.Blue, scene.PixelAt(20, 20), "and the first still draws after the second is gone");
    }

    [TestMethod]
    public void TheCacheIsBounded()
    {
        Svg.ClearCache();

        // More distinct documents than the cache holds: the oldest go, and nothing throws while it
        // happens. A cache that only grew would be a leak with a hash function in front of it.
        for (int index = 0; index < SvgCache.Capacity + 8; index++)
        {
            using var data = Factory.LoadFromStream(SvgFixtures.Stream(
                $"""<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 10 10"><rect width="{index + 1}" height="10"/></svg>"""));
        }

        Assert.IsLessThanOrEqualTo(SvgCache.Capacity, SvgCache.Count, "the cache stops at its capacity");
    }

    [TestMethod]
    public void ClearingTheCacheDoesNotDisturbAPictureAlreadyDrawn()
    {
        var image = Show(SvgFixtures.Stream(SvgFixtures.Square));
        image.Dock = DockStyle.Fill;
        image.SizeMode = SizeMode.StretchImage;

        Svg.ClearCache();

        using var scene = SvgScene.Create(image, 40f, 40f);
        Assert.AreEqual(SvgFixtures.Blue, scene.PixelAt(20, 20), "the recording outlives the parsed document");
    }
}
