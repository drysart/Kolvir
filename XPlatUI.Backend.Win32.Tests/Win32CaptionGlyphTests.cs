using Microsoft.VisualStudio.TestTools.UnitTesting;
using SkiaSharp;
using XPlatUI;

namespace XPlatUI.Backend.Win32.Tests;

/// <summary>
/// The glyphs the emulated caption buttons paint, checked as pixels rather than as code.
/// </summary>
/// <remarks>
/// The restore glyph is two offset squares, and the interesting half is what must <em>not</em> be
/// drawn: the back square is occluded by the front one, not drawn through it. The platform can fill
/// the front square with the caption colour because it owns that strip; this backend paints over the
/// application's own content, where a fill would cover whatever the application drew there - so the
/// back square is clipped away instead. That is invisible in a unit test of the geometry and very
/// visible on screen, which is why these assertions read pixels.
/// </remarks>
[TestClass]
[TestCategory("Platform.Win32")]
public sealed class Win32CaptionGlyphTests
{
    private const float ButtonWidth = 46f;
    private const float StripHeight = 22.5f;

    /// <summary>Paints one button's glyph and returns the pixels that were lit.</summary>
    private static SKBitmap Paint(XPlatCaptionButtonKind kind)
    {
        var bitmap = new SKBitmap((int)ButtonWidth, (int)MathF.Ceiling(StripHeight));
        using var canvas = new SKCanvas(bitmap);
        canvas.Clear(SKColors.Black);

        var button = new XPlatCaptionButton(kind, new SKRect(0f, 0f, ButtonWidth, StripHeight));
        Win32CaptionButtons.Draw(canvas, [button], hot: -1, pressed: -1, dark: true);

        return bitmap;
    }

    private static bool IsLit(SKBitmap bitmap, int x, int y)
    {
        var pixel = bitmap.GetPixel(x, y);
        return pixel.Red > 128 && pixel.Green > 128 && pixel.Blue > 128;
    }

    [TestMethod]
    public void TheRestoreGlyphDrawsTheBackSquareBeforeTheFrontOne()
    {
        using var bitmap = Paint(XPlatCaptionButtonKind.Restore);

        var cx = (int)MathF.Round(ButtonWidth / 2f);
        var cy = (int)MathF.Round(StripHeight / 2f);
        const int half = 5;

        // The back square's right edge is its own, so it is drawn.
        Assert.IsTrue(IsLit(bitmap, cx + half, cy), "the back square's right edge is visible");

        // The corner of the back square that the front one covers is not drawn at all: no pixel
        // between the two strokes, where the old glyph left a line running through the front square.
        // The window is inside the front square's own stroke by a pixel, because that stroke is
        // drawn along the edge and a pixel of it lands on the inside.
        var interiorLit = 0;
        for (var y = cy - half + 3; y < cy + half; y++)
        {
            for (var x = cx - half + 1; x < cx + half - 2; x++)
            {
                if (IsLit(bitmap, x, y))
                {
                    interiorLit++;
                }
            }
        }

        Assert.AreEqual(0, interiorLit, "the front square's interior stays clear");
    }

    [TestMethod]
    public void TheFrontSquareIsDrawnWhole()
    {
        using var bitmap = Paint(XPlatCaptionButtonKind.Restore);

        var cx = (int)MathF.Round(ButtonWidth / 2f);
        var cy = (int)MathF.Round(StripHeight / 2f);
        const int half = 5;
        var frontBottom = cy + half;

        var drawn = 0;
        for (var x = cx - half; x <= cx + half - 2; x++)
        {
            if (IsLit(bitmap, x, frontBottom) || IsLit(bitmap, x, frontBottom - 1))
            {
                drawn++;
            }
        }

        Assert.IsTrue(drawn > 0, "the front square's bottom edge is drawn");
    }

    [TestMethod]
    public void TheMinimizeGlyphIsASingleLine()
    {
        using var bitmap = Paint(XPlatCaptionButtonKind.Minimize);

        var rows = 0;
        for (var y = 0; y < bitmap.Height; y++)
        {
            for (var x = 0; x < bitmap.Width; x++)
            {
                if (IsLit(bitmap, x, y))
                {
                    rows++;
                    break;
                }
            }
        }

        Assert.IsTrue(rows <= 2, $"a minimise glyph is one stroke, not {rows} rows of them");
    }

    [TestMethod]
    public void TheCloseGlyphCrossesTheButtonCentre()
    {
        using var bitmap = Paint(XPlatCaptionButtonKind.Close);

        var cx = (int)MathF.Round(ButtonWidth / 2f);
        var cy = (int)MathF.Round(StripHeight / 2f);

        Assert.IsTrue(IsLit(bitmap, cx, cy) || IsLit(bitmap, cx - 1, cy) || IsLit(bitmap, cx + 1, cy),
            "the two strokes of the close glyph meet at the centre");
    }
}
