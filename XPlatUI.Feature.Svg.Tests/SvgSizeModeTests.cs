using SkiaSharp;

namespace XPlatUI.Svg.Tests;

/// <summary>
/// A vector picture scales by the same four rules a bitmap does, and the rules are what
/// <see cref="XPlatSizeMode"/> promises: a shape is drawn at the destination, not at the size the
/// file happened to be authored at.
/// </summary>
/// <remarks>
/// Each case uses a document that is twice as wide as it is tall inside a square host, because that
/// is the only arrangement in which all four modes disagree: a square document fills the host in
/// every mode and would pass whatever the control did.
/// </remarks>
[TestClass]
[TestCategory("Feature")]
[TestCategory("Svg")]
public sealed class SvgSizeModeTests : SvgTestBase
{
    private const float HostSize = 100f;

    [TestMethod]
    public void StretchImageFillsTheBoxAndNormalDoesNot()
    {
        using var registered = RegisterSvg();
        var image = Load(sizeMode: XPlatSizeMode.StretchImage);

        using (var scene = SvgScene.Create(image, HostSize, HostSize))
        {
            Assert.AreEqual(SvgFixtures.Blue, scene.PixelAt(2, 2), "a stretched document reaches the corner");
        }

        image.SizeMode = XPlatSizeMode.Normal;
        using var normal = SvgScene.Create(image, HostSize, HostSize);
        Assert.AreNotEqual(SvgFixtures.Blue, normal.PixelAt(2, 2), "an eight-by-four document drawn at natural size cannot");
        Assert.AreEqual(SvgFixtures.Blue, normal.PixelAt(50, 50), "but it is painted in the middle of the box");
    }

    [TestMethod]
    public void ZoomKeepsTheAspectRatioAndLeavesBands()
    {
        using var registered = RegisterSvg();
        var image = Load(sizeMode: XPlatSizeMode.Zoom);
        using var scene = SvgScene.Create(image, HostSize, HostSize);

        // 8x4 zoomed into 100x100 becomes 100x50, so the middle is the document and the top is not.
        Assert.AreEqual(SvgFixtures.Blue, scene.PixelAt(50, 50), "the zoomed document covers the middle");
        Assert.AreNotEqual(SvgFixtures.Blue, scene.PixelAt(50, 2), "and leaves the bands above and below");
    }

    [TestMethod]
    public void CenterImageDrawsTheDocumentAtItsOwnSize()
    {
        using var registered = RegisterSvg();
        var image = Load(sizeMode: XPlatSizeMode.CenterImage);
        using var scene = SvgScene.Create(image, HostSize, HostSize);

        // The document is 8x4, centred in 100x100: x from 46 to 54, y from 48 to 52.
        Assert.AreEqual(SvgFixtures.Blue, scene.PixelAt(50, 50), "a centred document is in the middle");
        Assert.AreNotEqual(SvgFixtures.Blue, scene.PixelAt(20, 50), "and is not stretched to the box");
    }

    [TestMethod]
    public void EveryModePlacesTheDocumentWhereTheBitmapsTestAlreadySaysItDoes()
    {
        using var registered = RegisterSvg();
        var image = Load(sizeMode: XPlatSizeMode.Normal);

        // The same expectations as the core suite's bitmap test, driven through a vector document:
        // the alignment is the control's, and a picture that was never a bitmap uses it unchanged.
        foreach (var mode in Enum.GetValues<XPlatSizeMode>())
        {
            image.SizeMode = mode;
            using var scene = SvgScene.Create(image, HostSize, HostSize);

            var painted = scene.PixelAt(52, 50);
            var corner = scene.PixelAt(2, 2);

            if (mode is XPlatSizeMode.StretchImage)
            {
                Assert.AreEqual(SvgFixtures.Blue, corner, $"{mode} reaches the corner");
            }
            else if (mode is XPlatSizeMode.Zoom or XPlatSizeMode.CenterImage or XPlatSizeMode.Normal)
            {
                Assert.AreEqual(SvgFixtures.Blue, painted, $"{mode} paints somewhere near the middle");
                Assert.AreNotEqual(SvgFixtures.Blue, corner, $"{mode} does not reach the corner");
            }
        }
    }

    private static XPlatImage Load(XPlatSizeMode sizeMode)
    {
        var image = new XPlatImage
        {
            Dock = DockStyle.Fill,
            SizeMode = sizeMode,
            CornerRadius = 0f,
            ImageAlign = XPlatContentAlignment.MiddleCenter,
        };

        Assert.IsTrue(image.Load(SvgFixtures.Stream(SvgFixtures.WideRectangle)), "the fixture document should load");
        return image;
    }
}
