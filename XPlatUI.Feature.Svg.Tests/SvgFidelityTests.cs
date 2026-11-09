using SkiaSharp;

namespace XPlatUI.Svg.Tests;

/// <summary>
/// The part of the supported subset that is a question of paint rather than of parsing: a
/// gradient interpolates, a pattern tiles, text reaches a real font, a filter softens an edge, and
/// a transform and a clip path put things where the document says.
/// </summary>
/// <remarks>
/// The README's "what actually renders" table is a claim about pixels, so the cases that fail
/// silently - a gradient that comes out flat, text that parses and draws nothing because nothing
/// shaped it, a filter that is parsed and then ignored - get an assertion here rather than a line
/// in a table. Every document is 100 by 100 inside a 100 by 100 host, so a document coordinate and
/// a pixel coordinate are the same number and an assertion can name one.
/// </remarks>
[TestClass]
[TestCategory("Feature")]
[TestCategory("Svg")]
public sealed class SvgFidelityTests : SvgTestBase
{
    private const float HostSize = 100f;

    private const string Gradient = """
        <svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 100 100">
          <defs>
            <linearGradient id="across" x1="0" y1="0" x2="1" y2="0">
              <stop offset="0" stop-color="#ff0000"/>
              <stop offset="1" stop-color="#0000ff"/>
            </linearGradient>
          </defs>
          <rect width="100" height="100" fill="url(#across)"/>
        </svg>
        """;

    private const string RadialGradient = """
        <svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 100 100">
          <defs>
            <radialGradient id="out" cx="0.5" cy="0.5" r="0.5">
              <stop offset="0" stop-color="#00ff00"/>
              <stop offset="1" stop-color="#000000"/>
            </radialGradient>
          </defs>
          <rect width="100" height="100" fill="url(#out)"/>
        </svg>
        """;

    private const string Pattern = """
        <svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 100 100">
          <defs>
            <pattern id="tiles" width="20" height="20" patternUnits="userSpaceOnUse">
              <rect width="20" height="20" fill="#0000ff"/>
              <rect width="10" height="20" fill="#ff0000"/>
            </pattern>
          </defs>
          <rect width="100" height="100" fill="url(#tiles)"/>
        </svg>
        """;

    private const string Text = """
        <svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 100 100">
          <text x="6" y="60" font-size="40" fill="#000000">Hg</text>
        </svg>
        """;

    private const string SharpAndBlurred = """
        <svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 100 100">
          <defs>
            <filter id="soften" x="-50%" y="-50%" width="200%" height="200%">
              <feGaussianBlur stdDeviation="6"/>
            </filter>
          </defs>
          <rect x="12" y="10" width="28" height="80" fill="#0000ff"/>
          <rect x="60" y="10" width="28" height="80" fill="#0000ff" filter="url(#soften)"/>
        </svg>
        """;

    private const string TranslatedAndClipped = """
        <svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 100 100">
          <defs>
            <clipPath id="disc"><circle cx="50" cy="50" r="25"/></clipPath>
          </defs>
          <g transform="translate(50 0)">
            <rect x="0" y="0" width="40" height="40" fill="#0000ff"/>
          </g>
          <rect width="100" height="100" fill="#ff0000" clip-path="url(#disc)"/>
        </svg>
        """;

    [TestMethod]
    public void ALinearGradientRunsBetweenItsStops()
    {
        using var image = Load(Gradient);
        using var scene = SvgScene.Create(image, HostSize, HostSize);

        var left = scene.PixelAt(2, 50);
        var middle = scene.PixelAt(50, 50);
        var right = scene.PixelAt(97, 50);

        Assert.IsTrue(left.Red > 200 && left.Blue < 60, $"the start of the gradient is its first stop, got {left}");
        Assert.IsTrue(right.Blue > 200 && right.Red < 60, $"the end of it is the second stop, got {right}");
        Assert.IsTrue(
            middle.Red is > 60 and < 200 && middle.Blue is > 60 and < 200,
            $"and the middle is interpolated rather than clamped to a stop, got {middle}");
    }

    [TestMethod]
    public void ARadialGradientIsBrightestAtItsCentre()
    {
        using var image = Load(RadialGradient);
        using var scene = SvgScene.Create(image, HostSize, HostSize);

        var centre = scene.PixelAt(50, 50);
        var corner = scene.PixelAt(2, 2);

        Assert.IsTrue(centre.Green > 150, $"the centre is the first stop, got {centre}");
        Assert.IsTrue(corner.Green < 80, $"a corner is the last one, got {corner}");
    }

    [TestMethod]
    public void APatternTilesItsContent()
    {
        using var image = Load(Pattern);
        using var scene = SvgScene.Create(image, HostSize, HostSize);

        // The tile is ten units of red and ten of blue, so one row crosses both five times.
        using var bitmap = scene.Render();
        int red = 0;
        int blue = 0;
        for (int x = 0; x < 100; x++)
        {
            var pixel = bitmap.GetPixel(x, 50);
            if (IsRed(pixel))
            {
                red++;
            }
            else if (IsBlue(pixel))
            {
                blue++;
            }
        }

        Assert.IsTrue(red > 30, $"the red half of the tile is drawn, {red} pixels of 100");
        Assert.IsTrue(blue > 30, $"and the blue half, {blue} pixels of 100");
    }

    [TestMethod]
    public void TextIsDrawnWithARealFont()
    {
        using var image = Load(Text);
        using var scene = SvgScene.Create(image, HostSize, HostSize);

        // The point is that something was shaped and painted: a text element that reaches no font
        // draws nothing and would still parse, so counting ink is the assertion that matters. The
        // threshold is loose because the face is whatever the machine has.
        using var bitmap = scene.Render();
        int ink = 0;
        for (int y = 20; y < 70; y++)
        {
            for (int x = 0; x < 100; x++)
            {
                if (bitmap.GetPixel(x, y).Red < 128)
                {
                    ink++;
                }
            }
        }

        Assert.IsTrue(ink > 50, $"the glyphs are painted, not merely parsed; found {ink} dark pixels");
    }

    [TestMethod]
    public void AGaussianBlurSpreadsInkPastTheShape()
    {
        using var image = Load(SharpAndBlurred);
        using var scene = SvgScene.Create(image, HostSize, HostSize);

        // Both rectangles share a fill, a height and a three-unit margin, so the pixels just
        // outside each one differ only by the filter. Ink lowers the red channel of a pale
        // background, which is the difference to look for: the blue channel cannot tell the two
        // apart because a pale background is already high in blue.
        var outsideSharp = scene.PixelAt(9, 50);
        var outsideBlurred = scene.PixelAt(57, 50);

        Assert.IsTrue(
            outsideBlurred.Red < outsideSharp.Red - 20,
            $"the blurred rectangle fades past its edge: red {outsideBlurred.Red} against {outsideSharp.Red}");
    }

    [TestMethod]
    public void ATransformMovesTheShape()
    {
        using var image = Load(TranslatedAndClipped);
        using var scene = SvgScene.Create(image, HostSize, HostSize);

        // The rectangle is authored at the origin and translated fifty units right, so the corner
        // it would have covered and the corner it now covers say whether the transform applied.
        var beforeTranslate = scene.PixelAt(5, 5);
        var afterTranslate = scene.PixelAt(80, 5);

        Assert.IsFalse(IsBlue(beforeTranslate), $"nothing is left where it was authored, got {beforeTranslate}");
        Assert.IsTrue(IsBlue(afterTranslate), $"the rectangle is drawn where the transform put it, got {afterTranslate}");
    }

    [TestMethod]
    public void AClipPathConfinesWhatItCovers()
    {
        using var image = Load(TranslatedAndClipped);
        using var scene = SvgScene.Create(image, HostSize, HostSize);

        var centre = scene.PixelAt(50, 50);
        var corner = scene.PixelAt(5, 95);

        Assert.IsTrue(IsRed(centre), $"the clip keeps the middle of the red document, got {centre}");
        Assert.IsFalse(IsRed(corner), $"and drops the corner outside the circle, got {corner}");
    }

    private static bool IsBlue(SKColor colour) => colour.Blue > 200 && colour.Red < 60;

    private static bool IsRed(SKColor colour) => colour.Red > 200 && colour.Green < 60 && colour.Blue < 60;

    private XPlatImage Load(string svg)
    {
        var image = new XPlatImage { Dock = DockStyle.Fill, SizeMode = XPlatSizeMode.StretchImage };
        image.Image = Factory.LoadFromStream(SvgFixtures.Stream(svg));
        return image;
    }
}


