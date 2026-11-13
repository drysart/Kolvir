using System.Security.Cryptography;
using Kolvir.WoffFonts;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SkiaSharp;

namespace Kolvir.WoffFonts.Tests;

/// <summary>
/// The two containers, against the plain file they were built from.
/// </summary>
/// <remarks>
/// <para>
/// The fixtures are IBM Plex Sans Regular v6.4.1 - the same release, the same face - as the
/// <c>.ttf</c> the sample carries, so every claim here has a right answer that does not depend on
/// this decoder agreeing with itself. Rendering is compared pixel for pixel through Skia, which is
/// the same rasteriser the library uses, so a glyph that came back with the wrong point, the wrong
/// contour end or a missing instruction shows up as a different bitmap rather than as a plausible
/// font.
/// </para>
/// <para>
/// The character set is deliberately wider than a sentence: a sample of twenty letters would pass on
/// a decoder that mishandles fat triplets, empty glyphs or composite glyphs, and those are the three
/// cases the transform exists to make interesting.
/// </para>
/// </remarks>
[TestClass]
[TestCategory("Woff")]
public sealed class WoffFontLoadingTests
{
    /// <summary>Every printable ASCII character, which reaches most of a Latin font's glyphs.</summary>
    private const string GlyphSample =
        "!\"#$%&'()*+,-./0123456789:;<=>?@ABCDEFGHIJKLMNOPQRSTUVWXYZ[\\]^_`" +
        "abcdefghijklmnopqrstuvwxyz{|}~ ";

    private static readonly Lazy<byte[]> TrueType = new(() => Read("IBMPlexSans-Regular.ttf"));
    private static readonly Lazy<byte[]> Woff = new(() => Read("IBMPlexSans-Regular.woff"));
    private static readonly Lazy<byte[]> Woff2 = new(() => Read("IBMPlexSans-Regular.woff2"));

    [TestMethod]
    public void AWoffFileDrawsTheSameGlyphsAsItsTrueTypeOriginal()
    {
        var decoded = WoffFontFormatProvider.Shared.Decode(Woff.Value).ToArray();

        CollectionAssert.AreEqual(
            Render(TrueType.Value),
            Render(decoded),
            "a WOFF 1.0 file is the same tables compressed, so the drawing has to be identical");
    }

    [TestMethod]
    public void AWoff2FileDrawsTheSameGlyphsAsItsTrueTypeOriginal()
    {
        var decoded = WoffFontFormatProvider.Shared.Decode(Woff2.Value).ToArray();

        CollectionAssert.AreEqual(
            Render(TrueType.Value),
            Render(decoded),
            "the reconstructed outlines have to draw what the original font drew");
    }

    [TestMethod]
    public void BothContainersAgreeTableForTable()
    {
        var woff = Tables(WoffFontFormatProvider.Shared.Decode(Woff.Value).ToArray());
        var woff2 = Tables(WoffFontFormatProvider.Shared.Decode(Woff2.Value).ToArray());

        CollectionAssert.AreEquivalent(woff.Keys.ToList(), woff2.Keys.ToList(), "the same tables, by tag");

        foreach (var (tag, bytes) in woff)
        {
            // glyf and loca are rebuilt from the transform, and head carries both the transformed-font
            // flag and the whole-file checksum adjustment, so three tables are allowed to differ.
            if (tag is "glyf" or "loca" or "head")
            {
                continue;
            }

            CollectionAssert.AreEqual(bytes, woff2[tag], $"'{tag}' should be the same table either way");
        }
    }

    [TestMethod]
    public void TheReconstructedLocaAddressesEveryGlyph()
    {
        var ttf = Tables(TrueType.Value);
        var decoded = Tables(WoffFontFormatProvider.Shared.Decode(Woff2.Value).ToArray());

        // loca is not stored in a WOFF 2 file at all, so its length is the only thing tying the rebuilt
        // offsets to the font's own glyph count: one entry per glyph plus the final offset.
        Assert.AreEqual(ttf["loca"].Length, decoded["loca"].Length, "one loca entry per glyph, plus the end");
        Assert.AreEqual(ttf["hmtx"].Length, decoded["hmtx"].Length);

        CollectionAssert.AreEqual(
            ttf["hmtx"], decoded["hmtx"], "hmtx is not transformed in this file, so it must survive untouched");

        // The rebuilt glyf is allowed to be a different size - the flag encoding has several valid
        // spellings - but not by much: more than a few percent means something was written twice.
        var growth = (decoded["glyf"].Length - ttf["glyf"].Length) / (double)ttf["glyf"].Length;
        Assert.IsTrue(
            Math.Abs(growth) < 0.05,
            $"the rebuilt glyf table is {decoded["glyf"].Length} bytes against the original {ttf["glyf"].Length}");
    }

    [TestMethod]
    public void TheMetricsSurviveTheRoundTrip()
    {
        var original = Font.FromBytes(TrueType.Value, 17f);
        var woff = Font.FromBytes(WoffFontFormatProvider.Shared.Decode(Woff.Value).Span, 17f);
        var woff2 = Font.FromBytes(WoffFontFormatProvider.Shared.Decode(Woff2.Value).Span, 17f);

        Assert.AreEqual("IBM Plex Sans", woff2.FamilyName);
        Assert.AreEqual(400, woff2.Weight);
        Assert.AreEqual(original.LineHeight, woff.LineHeight, 0.001f);
        Assert.AreEqual(original.LineHeight, woff2.LineHeight, 0.001f);
        Assert.AreEqual(original.MeasureWidth(GlyphSample), woff2.MeasureWidth(GlyphSample), 0.001f);
    }

    [TestMethod]
    public void TheLoaderReadsEveryFormatWithoutBeingToldWhichItHas()
    {
        var loader = new FontLoader([WoffFontFormatProvider.Shared]);

        var fromTtf = loader.LoadFromBytes(TrueType.Value, 20f);
        var fromWoff = loader.LoadFromBytes(Woff.Value, 20f);
        var fromWoff2 = loader.LoadFromBytes(Woff2.Value, 20f);

        Assert.AreEqual("IBM Plex Sans", fromTtf.FamilyName, "the built-in path still reads a plain file");
        Assert.AreEqual(fromTtf.FamilyName, fromWoff.FamilyName);
        Assert.AreEqual(fromTtf.FamilyName, fromWoff2.FamilyName);
        Assert.AreEqual(fromTtf.MeasureWidth(GlyphSample), fromWoff2.MeasureWidth(GlyphSample), 0.001f);
    }

    [TestMethod]
    public void TheBuiltInLoaderIsThePlatformsAndTheProviderDoesNotDependOnIt()
    {
        // SkiaSharp is built on FreeType on macOS and Linux, and FreeType reads WOFF 1.0, so on those
        // platforms a loader with no provider loads the file by itself. On Windows SkiaSharp is built
        // on DirectWrite, which reads neither container. That difference is Skia's, not Kolvir's, so
        // this test records it rather than asserting one platform's answer on all three: a plain
        // loader either refuses the container or reads the same font, and neither outcome is a bug.
        var plain = new FontLoader();

        try
        {
            using var font = plain.LoadFromBytes(Woff.Value);

            Assert.AreEqual("IBM Plex Sans", font.FamilyName, "and reads it as the same font");
        }
        catch (FontFormatException exception)
        {
            Assert.AreEqual(string.Empty, exception.ProviderName, "no provider claimed it, so none is named");
        }

        // WOFF 2 is not among them: it needs the glyph reconstruction, and no FreeType Skia ships is
        // built against Brotli. So the package is not redundant on any of the three platforms.
        var refused = Assert.ThrowsExactly<FontFormatException>(
            () => plain.LoadFromBytes(Woff2.Value));

        Assert.AreEqual(string.Empty, refused.ProviderName);
    }

    [TestMethod]
    public void TheProviderIsNamedWhenItClaimsDataItCannotDecode()
    {
        var truncated = Woff2.Value.AsSpan(0, Woff2.Value.Length / 2).ToArray();

        var exception = Assert.ThrowsExactly<FontFormatException>(
            () => new FontLoader([WoffFontFormatProvider.Shared]).LoadFromBytes(truncated));

        Assert.AreEqual("WOFF", exception.ProviderName);
        Assert.IsInstanceOfType<InvalidDataException>(exception.InnerException);
    }

    [TestMethod]
    public void EveryTruncationIsRefusedRatherThanCrashing()
    {
        var loader = new FontLoader([WoffFontFormatProvider.Shared]);

        foreach (var whole in new[] { Woff.Value, Woff2.Value })
        {
            // A sampled sweep rather than every length: enough to land inside each structure, cheap
            // enough to keep the suite quick. What matters is that a cut file raises a format failure
            // and never an index or null dereference, which would mean a length was trusted.
            for (var length = 0; length < whole.Length; length += 97)
            {
                try
                {
                    using var font = loader.LoadFromBytes(whole.AsSpan(0, length));
                }
                catch (FontFormatException)
                {
                }
                catch (Exception exception)
                {
                    Assert.Fail(
                        $"truncating to {length} of {whole.Length} bytes raised {exception.GetType().Name}: {exception.Message}");
                }
            }
        }
    }

    [TestMethod]
    public void ACollectionIsRefusedByName()
    {
        // The flavor is the font collection tag, which is a different output format rather than a
        // damaged file: it is refused with a message that says so.
        var woff = Woff.Value.ToArray();
        "ttcf"u8.CopyTo(woff.AsSpan(4));

        var exception = Assert.ThrowsExactly<InvalidDataException>(
            () => WoffFontFormatProvider.Shared.Decode(woff));

        StringAssert.Contains(exception.Message, "collection");
    }

    [TestMethod]
    public void TheProviderOnlyClaimsItsOwnContainers()
    {
        var provider = WoffFontFormatProvider.Shared;

        Assert.IsFalse(provider.CanLoad(TrueType.Value), "a plain font is the built-in loader's");
        Assert.IsTrue(provider.CanLoad(Woff.Value));
        Assert.IsTrue(provider.CanLoad(Woff2.Value));
        Assert.IsFalse(provider.CanLoad([]), "nothing to recognise");
        Assert.IsFalse(provider.CanLoad("wOF"u8), "three bytes of a signature is not a signature");
    }

    /// <summary>
    /// Draws the whole sample in one pass and returns the pixels.
    /// </summary>
    /// <remarks>
    /// Unhinted antialiased drawing at a size large enough that a point moved by one font unit changes
    /// a pixel, so a reconstructed outline that is merely close is caught. The bits are compared as
    /// bytes rather than hashed so a failure can report how many pixels differ.
    /// </remarks>
    private static byte[] Render(byte[] sfnt)
    {
        using var typeface = SKTypeface.FromData(SKData.CreateCopy(sfnt))
            ?? throw new InvalidOperationException("The data is not a font Skia will read.");
        using var font = new SKFont(typeface, 40f);
        using var bitmap = new SKBitmap(1600, 220);

        using (var canvas = new SKCanvas(bitmap))
        {
            canvas.Clear(SKColors.Black);
            using var paint = new SKPaint { Color = SKColors.White, IsAntialias = true };
            canvas.DrawText(GlyphSample, 4f, 60f, SKTextAlign.Left, font, paint);
            canvas.DrawText(GlyphSample, 4f, 120f, SKTextAlign.Left, font, paint);
        }

        return bitmap.Bytes;
    }

    private static Dictionary<string, byte[]> Tables(byte[] sfnt)
    {
        var count = (sfnt[4] << 8) | sfnt[5];
        var tables = new Dictionary<string, byte[]>(count, StringComparer.Ordinal);

        for (var i = 0; i < count; i++)
        {
            var at = 12 + (i * 16);
            var tag = System.Text.Encoding.ASCII.GetString(sfnt, at, 4);
            var offset = (sfnt[at + 8] << 24) | (sfnt[at + 9] << 16) | (sfnt[at + 10] << 8) | sfnt[at + 11];
            var length = (sfnt[at + 12] << 24) | (sfnt[at + 13] << 16) | (sfnt[at + 14] << 8) | sfnt[at + 15];
            tables[tag] = sfnt.AsSpan(offset, length).ToArray();
        }

        return tables;
    }

    private static byte[] Read(string name) =>
        File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Fonts", name));
}
