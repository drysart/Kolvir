using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Kolvir.WoffFonts.Tests;

/// <summary>
/// The <c>hmtx</c> transform, driven directly.
/// </summary>
/// <remarks>
/// This is the one part of WOFF 2 that no file in the corpus exercises: neither Google's encoder nor
/// fontTools' applies the transform, because a font's bearings usually match its boxes already. So it
/// is tested from synthetic streams - the arrays the specification describes - rather than through a
/// font, and the cases are the two flags and the two refusals.
/// </remarks>
[TestClass]
[TestCategory("Woff")]
public sealed class Woff2HmtxTests
{
    /// <summary>Both arrays stored: the transform only dropped nothing, so it must give it all back.</summary>
    [TestMethod]
    public void TheTransformKeepsArraysThatWereNotDropped()
    {
        // flags 0 is refused (nothing was transformed), so this uses a stream where only the
        // monospaced tail was dropped - bit 1 - which leaves the proportional bearings in the stream.
        var stream = new byte[]
        {
            0x02,                           // flags: the monospaced bearings are missing
            0x01, 0xF4,                     // advance 500
            0x02, 0x58,                     // advance 600
            0x00, 0x0A,                     // bearing 10
            0x00, 0x14,                     // bearing 20
        };

        var hmtx = Woff2Hmtx.Reconstruct(stream, numberOfHMetrics: 2, numGlyphs: 4, xMins: [10, 20, 30, -5]);

        Assert.AreEqual(12, hmtx.Length, "two proportional entries at four bytes, two bearings at two");
        AssertProportional(hmtx, index: 0, advance: 500, bearing: 10);
        AssertProportional(hmtx, index: 1, advance: 600, bearing: 20);
        Assert.AreEqual((ushort)30, Word(hmtx, 8), "the tail comes from the glyph boxes");
        Assert.AreEqual(unchecked((ushort)-5), Word(hmtx, 10));
    }

    /// <summary>Both arrays dropped: every bearing must come from the reconstructed boxes.</summary>
    [TestMethod]
    public void TheTransformRebuildsBothArraysFromGlyphBoxes()
    {
        var stream = new byte[]
        {
            0x03,                           // flags: both arrays missing
            0x01, 0xF4,                     // advance 500
            0x02, 0x58,                     // advance 600
            0x02, 0xBC,                     // advance 700
        };

        var hmtx = Woff2Hmtx.Reconstruct(stream, numberOfHMetrics: 2, numGlyphs: 4, xMins: [10, 20, 30, -5]);

        AssertProportional(hmtx, index: 0, advance: 500, bearing: 10);
        AssertProportional(hmtx, index: 1, advance: 600, bearing: 20);
        Assert.AreEqual((ushort)30, Word(hmtx, 8));
        Assert.AreEqual(unchecked((ushort)-5), Word(hmtx, 10));
    }

    [TestMethod]
    public void TheTransformRefusesReservedFlags()
    {
        Assert.ThrowsExactly<InvalidDataException>(
            () => Woff2Hmtx.Reconstruct(new byte[] { 0x0C, 0, 0 }, 1, 1, [0]));
    }

    [TestMethod]
    public void TheTransformRefusesAStreamThatDroppedNothing()
    {
        Assert.ThrowsExactly<InvalidDataException>(
            () => Woff2Hmtx.Reconstruct(new byte[] { 0x00, 0, 0 }, 1, 1, [0]));
    }

    [TestMethod]
    public void TheTransformRefusesToGuessBearings()
    {
        // Boxes are only known when glyf was transformed too, so a font whose hmtx transform needs
        // them and has no boxes is contradictory rather than merely odd.
        var stream = new byte[] { 0x03, 0x01, 0xF4 };

        var exception = Assert.ThrowsExactly<InvalidDataException>(
            () => Woff2Hmtx.Reconstruct(stream, 1, 1, xMins: null));

        StringAssert.Contains(exception.Message, "glyph boxes");
    }

    [TestMethod]
    public void TheTransformRefusesMoreMetricsThanGlyphs()
    {
        Assert.ThrowsExactly<InvalidDataException>(
            () => Woff2Hmtx.Reconstruct(new byte[] { 0x03, 0x01, 0xF4 }, numberOfHMetrics: 4, numGlyphs: 2, xMins: [0, 0]));
    }

    private static void AssertProportional(byte[] hmtx, int index, ushort advance, short bearing)
    {
        Assert.AreEqual(advance, Word(hmtx, index * 4), $"advance of {index}");
        Assert.AreEqual(unchecked((ushort)bearing), Word(hmtx, (index * 4) + 2), $"bearing of {index}");
    }

    private static ushort Word(byte[] data, int at) => (ushort)((data[at] << 8) | data[at + 1]);
}
