using SkiaSharp;

namespace XPlatUI.Svg.Tests;

/// <summary>
/// Loading an SVG through the seam, and the size the document says it is.
/// </summary>
/// <remarks>
/// The bounds are what <see cref="XPlatImage.AutoSize"/> measures and what every
/// <see cref="XPlatSizeMode"/> scales from, so a document that carries a <c>viewBox</c> has to come
/// out of the renderer at the size the icon's author asked for rather than at a default.
/// </remarks>
[TestClass]
[TestCategory("Feature")]
[TestCategory("Svg")]
public sealed class SvgLoadingTests : SvgTestBase
{
    [TestMethod]
    public void AViewBoxAloneDecidesTheNaturalSize()
    {
        using var registered = RegisterSvg();
        using var image = new XPlatImage();

        Assert.IsTrue(image.Load(SvgFixtures.Stream("""
            <svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 24 24"><circle cx="12" cy="12" r="10"/></svg>
            """)), "a document with only a viewBox should load");

        var bounds = image.Picture!.CullRect;
        Assert.AreEqual(24f, bounds.Width, 0.01f, "the viewBox's width is the natural width");
        Assert.AreEqual(24f, bounds.Height, 0.01f, "and its height is the natural height");
    }

    [TestMethod]
    public void AWidthAndAHeightOutrankTheViewBoxForTheNaturalSize()
    {
        using var registered = RegisterSvg();
        using var image = new XPlatImage();

        Assert.IsTrue(image.Load(SvgFixtures.Stream("""
            <svg xmlns="http://www.w3.org/2000/svg" width="48" height="24" viewBox="0 0 24 12"><rect width="24" height="12"/></svg>
            """)), "a document with a size and a viewBox should load");

        var bounds = image.Picture!.CullRect;
        Assert.AreEqual(48f, bounds.Width, 0.01f, "the document's own width wins over the viewBox's");
        Assert.AreEqual(24f, bounds.Height, 0.01f, "and its own height too");
    }

    [TestMethod]
    public void PreserveAspectRatioNoneLetsTheWidthAndHeightDisagreeWithTheViewBox()
    {
        using var registered = RegisterSvg();
        using var image = new XPlatImage();

        Assert.IsTrue(image.Load(SvgFixtures.Stream("""
            <svg xmlns="http://www.w3.org/2000/svg" width="60" height="20" viewBox="0 0 30 30" preserveAspectRatio="none">
              <rect width="30" height="30"/>
            </svg>
            """)), "a non-uniform document should load");

        var bounds = image.Picture!.CullRect;
        Assert.AreEqual(60f, bounds.Width, 0.01f, "the width is the document's");
        Assert.AreEqual(20f, bounds.Height, 0.01f, "and the height is not the width's ratio");
    }

    [TestMethod]
    public void AViewBoxWithAMinimumIsRecordedAtTheOrigin()
    {
        // A viewBox of "10 20 30 40" means the content sits at an offset inside it. What the control
        // draws is the picture's own bounds, so the recording has to start where the content does.
        using var registered = RegisterSvg();
        using var image = new XPlatImage();

        Assert.IsTrue(image.Load(SvgFixtures.Stream("""
            <svg xmlns="http://www.w3.org/2000/svg" viewBox="10 20 30 40"><rect x="10" y="20" width="30" height="40"/></svg>
            """)), "an offset viewBox should load");

        var bounds = image.Picture!.CullRect;
        Assert.AreEqual(30f, bounds.Width, 0.01f, "the width is the viewBox's");
        Assert.AreEqual(40f, bounds.Height, 0.01f, "and the height is the viewBox's");
        Assert.AreEqual(0f, bounds.Left, 0.01f, "the recording starts at the content, not at the viewBox's minimum");
        Assert.AreEqual(0f, bounds.Top, 0.01f, "on both axes");
    }

    [TestMethod]
    public void ADocumentWithNoIntrinsicSizeFallsBackToTheRenderersOwnSize()
    {
        using var registered = RegisterSvg();
        using var image = new XPlatImage();
        int failures = 0;
        image.AddLoadFailedHandler((_, _) => failures++);

        // No viewBox and no width or height. The renderer still produces a picture - a degenerate
        // one-pixel one for a document with nothing in it - rather than failing, so the control
        // measures something rather than nothing. A real icon should carry a viewBox; the README
        // says so, and this test pins what happens when one does not.
        Assert.IsTrue(image.Load(SvgFixtures.Stream("<svg xmlns=\"http://www.w3.org/2000/svg\"></svg>")),
            "a document with no size still loads");
        Assert.IsGreaterThan(0f, image.Picture!.CullRect.Width, "with a measurable width");
        Assert.IsGreaterThan(0f, image.Picture.CullRect.Height, "and height");
        Assert.AreEqual(0, failures, "and nothing reported");
    }

    [TestMethod]
    public void MalformedMarkupSurfacesThroughLoadFailed()
    {
        using var registered = RegisterSvg();
        using var image = new XPlatImage();
        Exception? reported = null;
        using var subscription = image.AddLoadFailedHandler((_, e) => reported = e);

        Assert.IsFalse(image.Load(SvgFixtures.Stream("""
            <svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 10 10"><rect width="10"
            """)), "a truncated document does not load");

        Assert.IsInstanceOfType<XPlatImageFormatException>(reported, "the failure is the feature's, not the reader's");
        Assert.IsNotNull(reported!.InnerException, "the parser's own exception is kept");
        Assert.IsNull(image.Picture, "and nothing was replaced");
    }

    [TestMethod]
    public void ADocumentWithAColourOfItsOwnKeepsIt()
    {
        using var registered = RegisterSvg();
        var image = new XPlatImage { Dock = DockStyle.Fill, SizeMode = XPlatSizeMode.StretchImage };
        Assert.IsTrue(image.Load(SvgFixtures.Stream(SvgFixtures.FixedColourIcon)), "the fixture should load");

        using var scene = SvgScene.Create(image, 40f, 40f);
        var pixel = scene.PixelAt(20, 20);

        Assert.AreEqual(0xCC, pixel.Red, "a fill that names a colour is not a currentColor");
        Assert.AreEqual(0x00, pixel.Green, "so it stays red");
        Assert.AreEqual(0x00, pixel.Blue, "whatever the theme is");
    }

    [TestMethod]
    public void AnSvgFileOnDiskLoadsThroughTheSameCallAsABitmap()
    {
        using var registered = RegisterSvg();

        string path = Path.Combine(Path.GetTempPath(), $"xplatui-svg-{Guid.NewGuid():N}.svg");
        File.WriteAllBytes(path, SvgFixtures.Bytes(SvgFixtures.Square));
        try
        {
            using var image = new XPlatImage();
            Assert.IsTrue(image.Load(path), "an .svg path loads through Load(path)");
            Assert.AreEqual(10f, image.Picture!.CullRect.Width, 0.01f, "at the document's size");
        }
        finally
        {
            File.Delete(path);
        }
    }

    [TestMethod]
    public void AnSvgStreamLoadsEvenThoughItHasNoName()
    {
        using var registered = RegisterSvg();
        using var image = new XPlatImage();

        Assert.IsTrue(image.Load(SvgFixtures.Stream(SvgFixtures.Square)), "a stream with no extension loads");
        Assert.AreEqual(SvgFixtures.Blue, SampleCentre(image), "and paints the document's colour");
    }

    private static SKColor SampleCentre(XPlatImage image)
    {
        using var scene = SvgScene.Create(image, 40f, 40f);
        return scene.PixelAt(20, 20);
    }
}
