using SkiaSharp;

namespace XPlatUI.Svg.Tests;

/// <summary>
/// <c>currentColor</c> is the one part of an SVG a palette can change, so it is the one thing the
/// feature configures: an icon drawn with it follows the colour a load resolves it to, and an icon
/// that names its own colours does not.
/// </summary>
/// <remarks>
/// <para>
/// The colour is resolved while the document is parsed, which is why it is an option passed to the
/// factory and why the cache is keyed on it. The tests below are as much about that key as about the
/// colour: a recolour that re-parsed on every paint would still pass a pixel assertion.
/// </para>
/// <para>
/// The typed spelling is <see cref="XPlatSvgImageDataFactoryExtensions"/>, which forwards to the same
/// factory overload these tests use - one pipeline, two spellings.
/// </para>
/// </remarks>
[TestClass]
[TestCategory("Feature")]
[TestCategory("Svg")]
public sealed class SvgRecolourTests : SvgTestBase
{
    [TestMethod]
    public void CurrentColorDefaultsToBlack()
    {
        var image = Show(SvgFixtures.Stream(SvgFixtures.CurrentColorIcon));
        image.Dock = DockStyle.Fill;
        image.SizeMode = XPlatSizeMode.StretchImage;

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

        var image = new XPlatImage
        {
            Dock = DockStyle.Fill,
            SizeMode = XPlatSizeMode.StretchImage,
            Image = LoadColoured(SvgFixtures.Stream(SvgFixtures.CurrentColorIcon), green),
        };

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

        using (LoadColoured(SvgFixtures.Stream(SvgFixtures.CurrentColorIcon), green))
        {
        }

        Assert.AreEqual(parsed + 1, XPlatSvgCache.ParseCount, "a colour is parsed once");

        using (LoadColoured(SvgFixtures.Stream(SvgFixtures.CurrentColorIcon), green))
        {
        }

        Assert.AreEqual(parsed + 1, XPlatSvgCache.ParseCount, "and a second load at the same colour is a cache hit");

        using (LoadColoured(SvgFixtures.Stream(SvgFixtures.CurrentColorIcon), purple))
        {
        }

        Assert.AreEqual(parsed + 2, XPlatSvgCache.ParseCount, "a second colour is a second parse, not a thrash per load");

        using (LoadColoured(SvgFixtures.Stream(SvgFixtures.CurrentColorIcon), green))
        {
        }

        Assert.AreEqual(parsed + 2, XPlatSvgCache.ParseCount, "and going back to the first colour parses nothing");
    }

    [TestMethod]
    public void AFixedColourIsNotRecoloured()
    {
        var image = new XPlatImage
        {
            Dock = DockStyle.Fill,
            SizeMode = XPlatSizeMode.StretchImage,
            Image = LoadColoured(SvgFixtures.Stream(SvgFixtures.FixedColourIcon), SKColors.Lime),
        };

        using var scene = SvgScene.Create(image, 40f, 40f);
        var pixel = scene.PixelAt(20, 20);

        Assert.AreEqual(0xCC, pixel.Red, "a fill that names a colour keeps it");
        Assert.AreEqual(0, pixel.Green, "even when the caller asked for a different currentColor");
    }

    [TestMethod]
    public void TheTypedExtensionsHandTheOptionsToTheFactory()
    {
        // The discoverable spelling: an XPlatSvgOptions value reaches the provider, which is what
        // makes the recolour happen. This calls the extension explicitly, because C# binds the
        // factory's own overload for the same call syntax.
        var green = new SKColor(0x33, 0x99, 0x33);

        using var data = XPlatSvgImageDataFactoryExtensions.LoadFromStream(
            Factory,
            SvgFixtures.Stream(SvgFixtures.CurrentColorIcon),
            new XPlatSvgOptions { CurrentColor = green });

        Assert.AreEqual(24f, data.NaturalSize.Width, 0.01f, "the document loaded at its viewBox's size");
    }
}
