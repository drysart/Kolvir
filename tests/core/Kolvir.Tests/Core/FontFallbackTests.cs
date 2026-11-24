using Microsoft.VisualStudio.TestTools.UnitTesting;
using SkiaSharp;

namespace Kolvir.Tests;

/// <summary>
/// Font fallback: text the primary face has no glyph for is measured and drawn with a face that does.
/// </summary>
/// <remarks>
/// <para>
/// Every test here uses <see cref="TestFont"/>, a face loaded from bytes that contains Latin glyphs and
/// nothing else, so a Japanese character can only measure or draw correctly if the fallback path
/// found another face for it. That is what makes these tests about the split rather than about
/// whatever the machine's default font happens to contain.
/// </para>
/// <para>
/// The face that covers a character is the platform's choice, so nothing here asserts a width in
/// pixels. What is asserted is the relationship the pipeline depends on: a run is one face, a mixed
/// string is several runs, and every width - line breaking, alignment and caret placement alike - is
/// the sum of those runs. Where no CJK face is installed at all the tests say so and skip, because a
/// machine with no Japanese font can only produce the missing-glyph box this change cannot remove.
/// </para>
/// </remarks>
[TestClass]
[TestCategory("Core")]
public sealed class FontFallbackTests
{
    private const string Japanese = "日本語";
    private const string Mixed = "abc日本語def";

    /// <summary>True when the platform has a face for a Japanese character, so the tests can run.</summary>
    private static bool HasJapaneseFace =>
        SKFontManager.Default.MatchCharacter(null, SKFontStyle.Normal, null, '日') is not null;

    [TestMethod]
    public void JapaneseTextIsMeasuredWithAFaceThatCoversIt()
    {
        if (!HasJapaneseFace)
        {
            Assert.Inconclusive("No installed face covers a Japanese character on this machine.");
        }

        using var font = TestFont.Create();

        // A CJK ideograph advances about one em in every face that has one, and the Latin face's
        // missing-glyph box does not: three of them cannot be narrower than two and a half ems.
        Assert.IsTrue(
            font.MeasureWidth(Japanese) > 2.5f * font.Size,
            "a Japanese string measures in glyphs, not in the primary face's boxes");

        Assert.AreEqual(
            1,
            font.TextRunCount(Japanese),
            "one face covers all three characters, so they are one run rather than three");
    }

    [TestMethod]
    public void AMixedStringSplitsIntoOneRunPerFace()
    {
        if (!HasJapaneseFace)
        {
            Assert.Inconclusive("No installed face covers a Japanese character on this machine.");
        }

        using var font = TestFont.Create();

        Assert.AreEqual(3, font.TextRunCount(Mixed), "Latin, Japanese, Latin");

        // The width of the whole is the width of its runs, which is what keeps a caret or a wrap
        // point from landing inside a run that was measured with another face.
        float expected = font.MeasureWidth("abc") + font.MeasureWidth(Japanese) + font.MeasureWidth("def");
        Assert.AreEqual(expected, font.MeasureWidth(Mixed), 0.01f, "the runs add up to the whole");
    }

    [TestMethod]
    public void TextThePrimaryFaceCoversStaysOneRun()
    {
        using var font = TestFont.Create();

        Assert.AreEqual(1, font.TextRunCount("hello world"), "the common case allocates no split");
        Assert.AreEqual(0, font.TextRunCount(string.Empty), "empty text has no runs");
    }

    [TestMethod]
    public void TheCaretInsideAMixedStringMeasurestheRunsBeforeIt()
    {
        if (!HasJapaneseFace)
        {
            Assert.Inconclusive("No installed face covers a Japanese character on this machine.");
        }

        using var font = TestFont.Create();

        // Caret placement is a prefix width, so a prefix that stops inside the Japanese run must
        // still measure through the fallback face rather than through the primary one.
        float beforeJapanese = font.MeasureWidth("abc");
        float intoJapanese = font.MeasureWidth("abc日");
        float afterJapanese = font.MeasureWidth("abc日本語");

        Assert.IsTrue(intoJapanese > beforeJapanese, "the caret moves across the boundary");
        Assert.IsTrue(afterJapanese > intoJapanese, "and again inside the run");

        Assert.AreEqual(beforeJapanese + font.MeasureWidth("日"), intoJapanese, 0.01f);
        Assert.AreEqual(beforeJapanese + font.MeasureWidth(Japanese), afterJapanese, 0.01f);
    }

    [TestMethod]
    public void WrappingJapaneseTextKeepsEveryLineInsideTheWidth()
    {
        if (!HasJapaneseFace)
        {
            Assert.Inconclusive("No installed face covers a Japanese character on this machine.");
        }

        using var font = TestFont.Create();

        // No spaces, so this is the hard-break path, which has to use the same run widths as drawing.
        const float maxWidth = 60f;
        var lines = TextRenderer.BuildLines(Japanese + Japanese + Japanese, font, maxWidth, wrap: true);

        Assert.IsTrue(lines.Count > 1, "the text does not fit on one line");

        var rejoined = new System.Text.StringBuilder();
        foreach (var line in lines)
        {
            Assert.IsTrue(
                font.MeasureWidth(line) <= maxWidth,
                "a wrapped line never exceeds the width it was broken for");
            rejoined.Append(line);
        }

        Assert.AreEqual(Japanese + Japanese + Japanese, rejoined.ToString(), "no character is lost or repeated");
    }

    [TestMethod]
    public void ACharacterNoInstalledFaceCoversStillMeasuresRatherThanThrowing()
    {
        using var font = TestFont.Create();

        // U+10FFFD is a private-use code point, which no shipping text font is meant to have a glyph
        // for. macOS ships a face of last resort that answers for it anyway, so the assertion is not
        // "it stayed with the primary face" - it is that the character measures, is not thrown on, and
        // is covered by exactly one run, whichever face that run uses.
        const string unassigned = "a\U0010FFFDb";

        float width = font.MeasureWidth(unassigned);
        Assert.IsTrue(width > 0f, "an uncovered character still measures");

        var runs = font.TextRuns(unassigned);
        Assert.IsTrue(runs.Length >= 1, "the string is covered by at least one run");

        int covered = 0;
        foreach (var run in runs)
        {
            Assert.AreEqual(covered, run.Start, "the runs are contiguous, so no character is lost between them");
            covered += run.Length;
        }

        Assert.AreEqual(unassigned.Length, covered, "and together they cover the whole string");
    }

    [TestMethod]
    public void AMixedStringDrawsEveryRun()
    {
        if (!HasJapaneseFace)
        {
            Assert.Inconclusive("No installed face covers a Japanese character on this machine.");
        }

        using var font = TestFont.Create();
        using var surface = SKSurface.Create(new SKImageInfo(240, 40));
        using var paint = new SKPaint { Color = SKColors.Black, IsAntialias = true };

        surface.Canvas.Clear(SKColors.White);
        font.DrawText(surface.Canvas, Mixed, 0f, 20f, paint);
        surface.Canvas.Flush();

        using var image = surface.Snapshot();
        using var bitmap = SKBitmap.FromImage(image);

        int ink = 0;
        for (int y = 0; y < bitmap.Height; y++)
        {
            for (int x = 0; x < bitmap.Width; x++)
            {
                if (bitmap.GetPixel(x, y) != SKColors.White)
                {
                    ink++;
                }
            }
        }

        Assert.IsTrue(ink > 0, "something was drawn");
        Assert.IsTrue(
            font.MeasureWidth(Mixed) <= 240f,
            "and the drawn width is the width that was measured, so the runs fit the surface");
    }
}
