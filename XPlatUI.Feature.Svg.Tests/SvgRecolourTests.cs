using SkiaSharp;

namespace XPlatUI.Svg.Tests;

/// <summary>
/// <c>currentColor</c> is the one part of an SVG a palette can change, so it is the one the feature
/// exposes: an icon drawn with it follows the colour a load resolves it to, and an icon that names
/// its own colours does not.
/// </summary>
/// <remarks>
/// The colour is resolved while the document is parsed, which is why the parser is the thing that
/// takes it and why the cache is keyed on it. The tests below are as much about that key as about
/// the colour: a recolour that re-parsed on every paint would still pass a pixel assertion.
/// </remarks>
[TestClass]
[TestCategory("Feature")]
[TestCategory("Svg")]
public sealed class SvgRecolourTests : SvgTestBase
{
    [TestMethod]
    public void CurrentColorDefaultsToBlack()
    {
        using var image = new XPlatImage { Dock = DockStyle.Fill, SizeMode = XPlatSizeMode.StretchImage };
        Assert.IsTrue(image.LoadSvg(SvgFixtures.Stream(SvgFixtures.CurrentColorIcon)), "the icon should load");

        using var scene = SvgScene.Create(image, 40f, 40f);
        var pixel = scene.PixelAt(20, 30);

        Assert.AreEqual(SKColors.Black.Red, pixel.Red, "SVG's own default for currentColor is black");
        Assert.AreEqual(0, pixel.Green, "in every channel");
        Assert.AreEqual(0, pixel.Blue, "including blue");
    }

    [TestMethod]
    public void AResolvedCurrentColorIsWhatTheIconDraws()
    {
        var green = new SKColor(0x33, 0x99, 0x33);
        using var image = new XPlatImage { Dock = DockStyle.Fill, SizeMode = XPlatSizeMode.StretchImage };
        Assert.IsTrue(image.LoadSvg(SvgFixtures.Stream(SvgFixtures.CurrentColorIcon), green), "the icon should load tinted");

        using var scene = SvgScene.Create(image, 40f, 40f);
        var pixel = scene.PixelAt(20, 30);

        Assert.AreEqual(green.Red, pixel.Red, "the tint's red");
        Assert.AreEqual(green.Green, pixel.Green, "its green");
        Assert.AreEqual(green.Blue, pixel.Blue, "and its blue");
    }

    [TestMethod]
    public void TwoTintsOfOneDocumentAreTwoCacheEntriesAndAThirdLoadIsAHit()
    {
        var green = new SKColor(0x33, 0x99, 0x33);
        var purple = new SKColor(0x99, 0x33, 0x99);

        XPlatSvg.ClearCache();
        int parsed = XPlatSvgCache.ParseCount;

        using (var image = new XPlatImage())
        {
            Assert.IsTrue(image.LoadSvg(SvgFixtures.Stream(SvgFixtures.CurrentColorIcon), green), "green loads");
        }

        Assert.AreEqual(parsed + 1, XPlatSvgCache.ParseCount, "a colour is parsed once");

        using (var image = new XPlatImage())
        {
            Assert.IsTrue(image.LoadSvg(SvgFixtures.Stream(SvgFixtures.CurrentColorIcon), green), "green loads again");
        }

        Assert.AreEqual(parsed + 1, XPlatSvgCache.ParseCount, "and a second load at the same colour is a cache hit");

        using (var image = new XPlatImage())
        {
            Assert.IsTrue(image.LoadSvg(SvgFixtures.Stream(SvgFixtures.CurrentColorIcon), purple), "purple loads");
        }

        Assert.AreEqual(parsed + 2, XPlatSvgCache.ParseCount, "a second colour is a second parse, not a thrash per load");

        using (var image = new XPlatImage())
        {
            Assert.IsTrue(image.LoadSvg(SvgFixtures.Stream(SvgFixtures.CurrentColorIcon), green), "green again");
        }

        Assert.AreEqual(parsed + 2, XPlatSvgCache.ParseCount, "and going back to the first colour parses nothing");
    }

    [TestMethod]
    public void AFixedColourIsNotRecoloured()
    {
        using var image = new XPlatImage { Dock = DockStyle.Fill, SizeMode = XPlatSizeMode.StretchImage };
        Assert.IsTrue(image.LoadSvg(SvgFixtures.Stream(SvgFixtures.FixedColourIcon), SKColors.Lime), "the icon loads");

        using var scene = SvgScene.Create(image, 40f, 40f);
        var pixel = scene.PixelAt(20, 20);

        Assert.AreEqual(0xCC, pixel.Red, "a fill that names a colour keeps it");
        Assert.AreEqual(0, pixel.Green, "even when the caller asked for a different currentColor");
    }

    [TestMethod]
    public void LoadSvgTakesOwnershipOfWhatItLoads()
    {
        // The picture is built for this load, so the control has to dispose it; a caller that sets
        // Picture itself afterwards is responsible for turning the rule back off.
        using var image = new XPlatImage();
        Assert.IsTrue(image.LoadSvg(SvgFixtures.Stream(SvgFixtures.CurrentColorIcon)), "loads");
        Assert.IsTrue(image.DisposePictureWithControl, "the control owns the picture it was given");
    }
}
