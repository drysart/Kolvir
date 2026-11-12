using Microsoft.VisualStudio.TestTools.UnitTesting;
using SkiaSharp;
using Kolvir;

namespace Kolvir.Backend.Win32.Tests;

/// <summary>
/// The glyphs the emulated caption buttons paint, checked as pixels rather than as code.
/// </summary>
/// <remarks>
/// The shapes were measured off a system-drawn caption at 200%: every glyph fills a ten logical
/// pixel box, so a minimise stroke is ten pixels long, and the restore glyph is two eight pixel
/// squares offset by two. The interesting half of the restore glyph is what must <em>not</em> be
/// drawn: its back square shows only its top and right edges, because the platform's opaque front
/// square hides the rest of it. This backend paints over the application's own content and cannot
/// fill anything, so those edges are left out - which is invisible in a test of the geometry and
/// very visible on screen, hence the pixel assertions.
/// </remarks>
[TestClass]
[TestCategory("Platform.Win32")]
public sealed class Win32CaptionGlyphTests
{
    private const float ButtonWidth = 46f;
    private const float StripHeight = 22.5f;

    /// <summary>The button centre, which is where the glyphs are centred.</summary>
    private const int CenterX = 23;
    private const int CenterY = 11;

    /// <summary>Half the glyph box, so a glyph covers <c>CenterX ± 5</c>.</summary>
    private const int Half = 5;

    /// <summary>Paints one button's glyph and returns the pixels that were lit.</summary>
    private static SKBitmap Paint(CaptionButtonKind kind)
    {
        var bitmap = new SKBitmap((int)ButtonWidth, (int)MathF.Ceiling(StripHeight));
        using var canvas = new SKCanvas(bitmap);
        canvas.Clear(SKColors.Black);

        var button = new CaptionButton(kind, new SKRect(0f, 0f, ButtonWidth, StripHeight));
        Win32CaptionButtons.Draw(canvas, [button], hot: -1, pressed: -1, dark: true);

        return bitmap;
    }

    private static bool IsLit(SKBitmap bitmap, int x, int y)
    {
        var pixel = bitmap.GetPixel(x, y);
        return pixel.Red > 128 && pixel.Green > 128 && pixel.Blue > 128;
    }

    /// <summary>Paints one button's glyph at 200% and returns the pixels that were lit.</summary>
    private static SKBitmap PaintAt200(CaptionButtonKind kind)
    {
        var bitmap = new SKBitmap((int)(ButtonWidth * 2f), (int)MathF.Ceiling(StripHeight * 2f));
        using var canvas = new SKCanvas(bitmap);
        canvas.Clear(SKColors.Black);
        canvas.Scale(2f);

        var button = new CaptionButton(kind, new SKRect(0f, 0f, ButtonWidth, StripHeight));
        Win32CaptionButtons.Draw(canvas, [button], hot: -1, pressed: -1, dark: true);
        canvas.Flush();

        return bitmap;
    }

    private static SKRectI LitBounds(SKBitmap bitmap)
    {
        var left = bitmap.Width;
        var top = bitmap.Height;
        var right = -1;
        var bottom = -1;

        for (var y = 0; y < bitmap.Height; y++)
        {
            for (var x = 0; x < bitmap.Width; x++)
            {
                if (!IsLit(bitmap, x, y))
                {
                    continue;
                }

                left = Math.Min(left, x);
                top = Math.Min(top, y);
                right = Math.Max(right, x);
                bottom = Math.Max(bottom, y);
            }
        }

        return new SKRectI(left, top, right + 1, bottom + 1);
    }

    [TestMethod]
    [DataRow(CaptionButtonKind.Minimize, 10, 1)]
    [DataRow(CaptionButtonKind.Maximize, 10, 10)]
    [DataRow(CaptionButtonKind.Restore, 10, 10)]
    [DataRow(CaptionButtonKind.Close, 10, 10)]
    public void TheGlyphFillsThePlatformsTenPixelBox(CaptionButtonKind kind, int width, int height)
    {
        using var bitmap = Paint(kind);
        var bounds = LitBounds(bitmap);

        Assert.AreEqual(width, bounds.Width, $"{kind} glyph width");
        Assert.AreEqual(height, bounds.Height, $"{kind} glyph height");
        Assert.AreEqual(CenterX - Half, bounds.Left, $"{kind} glyph left");
        Assert.AreEqual(CenterX + Half, bounds.Right, $"{kind} glyph right");
    }

    [TestMethod]
    public void TheRestoreGlyphDrawsTheBackSquareAboveAndRightOfTheFront()
    {
        using var bitmap = Paint(CaptionButtonKind.Restore);

        // The back square's top edge is above the front square's, and its right edge is to the
        // right of it. Both stop where they would meet the front square's own edges.
        Assert.IsTrue(IsLit(bitmap, CenterX, CenterY - Half), "the back square's top edge is drawn");
        Assert.IsTrue(IsLit(bitmap, CenterX + Half - 1, CenterY), "the back square's right edge is drawn");
        Assert.IsFalse(IsLit(bitmap, CenterX + Half - 1, CenterY + Half - 1),
            "the back square's right edge stops above the front square's bottom edge");
    }

    [TestMethod]
    public void TheRestoreGlyphDrawsNothingBelowItsFrontSquare()
    {
        using var bitmap = Paint(CaptionButtonKind.Restore);

        // The back square is a square: without the front one covering it, its bottom half has to be
        // left out. Drawing the whole square and clipping it against the front one - which is what
        // this glyph did first - hangs a line of pixels below the front square: 7 of them here.
        var frontBottom = CenterY + Half;
        for (var y = frontBottom + 1; y < bitmap.Height; y++)
        {
            for (var x = 0; x < bitmap.Width; x++)
            {
                Assert.IsFalse(IsLit(bitmap, x, y), $"nothing is drawn below the front square, at {x},{y}");
            }
        }
    }

    [TestMethod]
    public void TheRestoreGlyphsFrontSquareIsHollowAndGapped()
    {
        using var bitmap = Paint(CaptionButtonKind.Restore);

        // The front square is the one the platform's window manager fills in; here it is drawn as an
        // outline, so its interior has to stay clear for the application's own paint.
        var interiorLit = 0;
        for (var y = CenterY - Half + 3; y < CenterY + Half - 1; y++)
        {
            for (var x = CenterX - Half + 2; x < CenterX + 1; x++)
            {
                if (IsLit(bitmap, x, y))
                {
                    interiorLit++;
                }
            }
        }

        Assert.AreEqual(0, interiorLit, "the front square's interior stays clear");

        // The two squares do not touch: the row under the back square's top edge is clear between
        // the back square's left end and its right edge.
        Assert.IsFalse(IsLit(bitmap, CenterX, CenterY - Half + 1), "there is a gap under the back square's top edge");
    }

    [TestMethod]
    public void TheRestoreGlyphsBackSquareIsARoundedRectBehindTheFrontOne()
    {
        using var bitmap = PaintAt200(CaptionButtonKind.Restore);

        // Both squares are rounded rects, the back one drawn first and covered by the front one where
        // they overlap - at 200% the back square's left edge is device pixel 40. It reaches that
        // column on the top edge's lower row but not on the row above, which is its top-left corner
        // curving; a square-ended edge, which this glyph had at first, fills that row too. It carries
        // on down past the corner, because it is the side of a rect, and stops where the front square
        // starts - inside it, at row 19, nothing of the back square is drawn.
        Assert.IsTrue(IsLit(bitmap, 40, 14), "the top edge reaches the back square's left edge");
        Assert.IsFalse(IsLit(bitmap, 40, 13), "the row above is inset, so the corner curves");
        Assert.IsTrue(IsLit(bitmap, 40, 16), "the corner continues down the back square's side");
        Assert.IsFalse(IsLit(bitmap, 40, 19), "which stops at the front square, that covers the rest");
    }

    [TestMethod]
    public void TheMinimizeGlyphIsASingleLine()
    {
        using var bitmap = Paint(CaptionButtonKind.Minimize);

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
        using var bitmap = Paint(CaptionButtonKind.Close);

        Assert.IsTrue(
            IsLit(bitmap, CenterX, CenterY) || IsLit(bitmap, CenterX - 1, CenterY) || IsLit(bitmap, CenterX + 1, CenterY),
            "the two strokes of the close glyph meet at the centre");
    }
}
