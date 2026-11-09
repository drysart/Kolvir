using SkiaSharp;

namespace XPlatUI.Svg.Tests;

/// <summary>
/// Loading an SVG through the factory, and the size the document says it is.
/// </summary>
/// <remarks>
/// The natural size is what <see cref="XPlatImage.AutoSize"/> measures and what every
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
        using var data = Factory.LoadFromStream(SvgFixtures.Stream("""
            <svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 24 24"><circle cx="12" cy="12" r="10"/></svg>
            """));

        Assert.AreEqual(24f, data.NaturalSize.Width, 0.01f, "the viewBox's width is the natural width");
        Assert.AreEqual(24f, data.NaturalSize.Height, 0.01f, "and its height is the natural height");
    }

    [TestMethod]
    public void AWidthAndAHeightOutrankTheViewBoxForTheNaturalSize()
    {
        using var data = Factory.LoadFromStream(SvgFixtures.Stream("""
            <svg xmlns="http://www.w3.org/2000/svg" width="48" height="24" viewBox="0 0 24 12"><rect width="24" height="12"/></svg>
            """));

        Assert.AreEqual(48f, data.NaturalSize.Width, 0.01f, "the document's own width wins over the viewBox's");
        Assert.AreEqual(24f, data.NaturalSize.Height, 0.01f, "and its own height too");
    }

    [TestMethod]
    public void PreserveAspectRatioNoneLetsTheWidthAndHeightDisagreeWithTheViewBox()
    {
        using var data = Factory.LoadFromStream(SvgFixtures.Stream("""
            <svg xmlns="http://www.w3.org/2000/svg" width="60" height="20" viewBox="0 0 30 30" preserveAspectRatio="none">
              <rect width="30" height="30"/>
            </svg>
            """));

        Assert.AreEqual(60f, data.NaturalSize.Width, 0.01f, "the width is the document's");
        Assert.AreEqual(20f, data.NaturalSize.Height, 0.01f, "and the height is not the width's ratio");
    }

    [TestMethod]
    public void AViewBoxWithAMinimumIsRecordedAtTheOrigin()
    {
        // A viewBox of "10 20 30 40" means the content sits at an offset inside it. What the control
        // draws is the picture's own bounds, so the recording has to start where the content does.
        using var data = Factory.LoadFromStream(SvgFixtures.Stream("""
            <svg xmlns="http://www.w3.org/2000/svg" viewBox="10 20 30 40"><rect x="10" y="20" width="30" height="40"/></svg>
            """));

        var bounds = data.CurrentFrame!.CullRect;
        Assert.AreEqual(30f, bounds.Width, 0.01f, "the width is the viewBox's");
        Assert.AreEqual(40f, bounds.Height, 0.01f, "and the height is the viewBox's");
        Assert.AreEqual(0f, bounds.Left, 0.01f, "the recording starts at the content, not at the viewBox's minimum");
        Assert.AreEqual(0f, bounds.Top, 0.01f, "on both axes");
    }

    [TestMethod]
    public void ADocumentWithNoIntrinsicSizeFallsBackToTheRenderersOwnSize()
    {
        // No viewBox and no width or height. The renderer still produces a picture - a degenerate
        // one-pixel one for a document with nothing in it - rather than failing, so the control
        // measures something rather than nothing.
        using var data = Factory.LoadFromStream(
            SvgFixtures.Stream("<svg xmlns=\"http://www.w3.org/2000/svg\"></svg>"));

        Assert.IsGreaterThan(0f, data.NaturalSize.Width, "with a measurable width");
        Assert.IsGreaterThan(0f, data.NaturalSize.Height, "and height");
    }

    [TestMethod]
    public void MalformedMarkupIsAFailureThatKeepsTheParsersException()
    {
        var failure = Assert.ThrowsExactly<XPlatImageFormatException>(() => Factory.LoadFromStream(SvgFixtures.Stream("""
            <svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 10 10"><rect width="10"
            """)));

        Assert.AreEqual("svg", failure.ProviderName, "the failure is the feature's");
        Assert.IsNotNull(failure.InnerException, "and the parser's own exception is kept");
    }

    [TestMethod]
    public void ADocumentWithAColourOfItsOwnKeepsIt()
    {
        var image = Show(SvgFixtures.Stream(SvgFixtures.FixedColourIcon));
        image.Dock = DockStyle.Fill;
        image.SizeMode = XPlatSizeMode.StretchImage;

        using var scene = SvgScene.Create(image, 40f, 40f);
        var pixel = scene.PixelAt(20, 20);

        Assert.AreEqual(0xCC, pixel.Red, "a fill that names a colour is not a currentColor");
        Assert.AreEqual(0x00, pixel.Green, "so it stays red");
        Assert.AreEqual(0x00, pixel.Blue, "whatever the theme is");
    }

    [TestMethod]
    public void AnSvgFileOnDiskLoadsThroughTheSameCallAsABitmap()
    {
        string path = Path.Combine(Path.GetTempPath(), $"xplatui-svg-{Guid.NewGuid():N}.svg");
        File.WriteAllBytes(path, SvgFixtures.Bytes(SvgFixtures.Square));

        try
        {
            using var data = Factory.LoadFromPath(path);
            Assert.AreEqual(10f, data.NaturalSize.Width, 0.01f, "at the document's size");
        }
        finally
        {
            File.Delete(path);
        }
    }

    [TestMethod]
    public void AnSvgStreamLoadsEvenThoughItHasNoName()
    {
        var image = Show(SvgFixtures.Stream(SvgFixtures.Square));
        image.Dock = DockStyle.Fill;
        image.SizeMode = XPlatSizeMode.StretchImage;

        using var scene = SvgScene.Create(image, 40f, 40f);
        Assert.AreEqual(SvgFixtures.Blue, scene.PixelAt(20, 20), "and paints the document's colour");
    }
}
