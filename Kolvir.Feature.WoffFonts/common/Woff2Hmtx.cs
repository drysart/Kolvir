namespace Kolvir.WoffFonts;

/// <summary>
/// Rebuilds the <c>hmtx</c> table when WOFF 2 stored it transformed.
/// </summary>
/// <remarks>
/// <para>
/// The transform exists because a TrueType glyph's left side bearing is almost always its outline's
/// <c>xMin</c>, so the array of bearings can be dropped and recomputed. Two independent bit flags
/// say which of the two arrays went missing - the proportional run and the monospaced tail - and the
/// file is invalid if neither did.
/// </para>
/// <para>
/// It follows that reconstructing needs the glyph boxes, so this transform can only appear alongside a
/// transformed <c>glyf</c> table. Encoders rarely emit it at all - neither Google's nor fontTools' do -
/// which is why it is the one part of this feature with no real file in the test corpus and a unit
/// test instead.
/// </para>
/// </remarks>
internal static class Woff2Hmtx
{
    /// <summary>The <c>hmtx</c> transform's first byte: the proportional run's bearings are missing.</summary>
    private const byte LsbMissing = 0x01;

    /// <summary>The <c>hmtx</c> transform's first byte: the monospaced run's bearings are missing.</summary>
    private const byte LeftSideBearingMissing = 0x02;

    /// <summary>Rebuilds the table.</summary>
    /// <param name="transformed">The transformed stream.</param>
    /// <param name="numberOfHMetrics">The proportional run's length, from <c>hhea</c>.</param>
    /// <param name="numGlyphs">The glyph count, from <c>maxp</c>.</param>
    /// <param name="xMins">Each glyph's <c>xMin</c>, from the reconstructed <c>glyf</c> table.</param>
    /// <returns>The table's bytes.</returns>
    /// <exception cref="InvalidDataException">The stream is damaged, or the bearings cannot be recovered.</exception>
    public static byte[] Reconstruct(
        ReadOnlyMemory<byte> transformed,
        int numberOfHMetrics,
        int numGlyphs,
        short[]? xMins)
    {
        var data = transformed.Span;

        if (data.IsEmpty)
        {
            throw new InvalidDataException("The transformed hmtx table is empty.");
        }

        var flags = data[0];
        if ((flags & 0xFC) != 0)
        {
            throw new InvalidDataException($"The transformed hmtx table has reserved flag bits set (0x{flags:X2}).");
        }

        if ((flags & (LsbMissing | LeftSideBearingMissing)) == 0)
        {
            throw new InvalidDataException("The transformed hmtx table drops neither bearing array.");
        }

        if (numberOfHMetrics <= 0 || numberOfHMetrics > numGlyphs)
        {
            throw new InvalidDataException(
                $"hhea declares {numberOfHMetrics} horizontal metrics for {numGlyphs} glyphs.");
        }

        var monospaced = numGlyphs - numberOfHMetrics;
        var at = 1;

        if (at + (numberOfHMetrics * 2) > data.Length)
        {
            throw new InvalidDataException("The transformed hmtx advance widths are truncated.");
        }

        var advances = new ushort[numberOfHMetrics];
        for (var i = 0; i < numberOfHMetrics; i++)
        {
            advances[i] = SfntBuilder.ReadUInt16(data, at);
            at += 2;
        }

        var bearings = new short[numberOfHMetrics];
        if ((flags & LsbMissing) != 0)
        {
            NeedBoxes(xMins, numGlyphs);
            Array.Copy(xMins!, bearings, numberOfHMetrics);
        }
        else
        {
            if (at + (numberOfHMetrics * 2) > data.Length)
            {
                throw new InvalidDataException("The transformed hmtx bearings are truncated.");
            }

            for (var i = 0; i < numberOfHMetrics; i++)
            {
                bearings[i] = SfntBuilder.ReadInt16(data, at);
                at += 2;
            }
        }

        var tail = new short[monospaced];
        if ((flags & LeftSideBearingMissing) != 0)
        {
            NeedBoxes(xMins, numGlyphs);
            Array.Copy(xMins!, numberOfHMetrics, tail, 0, monospaced);
        }
        else
        {
            if (at + (monospaced * 2) > data.Length)
            {
                throw new InvalidDataException("The transformed hmtx monospaced bearings are truncated.");
            }

            for (var i = 0; i < monospaced; i++)
            {
                tail[i] = SfntBuilder.ReadInt16(data, at);
                at += 2;
            }
        }

        var hmtx = new byte[(numberOfHMetrics * 4) + (monospaced * 2)];
        var write = 0;

        for (var i = 0; i < numberOfHMetrics; i++)
        {
            SfntBuilder.WriteUInt16(hmtx, write, advances[i]);
            SfntBuilder.WriteUInt16(hmtx, write + 2, (ushort)bearings[i]);
            write += 4;
        }

        for (var i = 0; i < monospaced; i++)
        {
            SfntBuilder.WriteUInt16(hmtx, write, (ushort)tail[i]);
            write += 2;
        }

        return hmtx;
    }

    private static void NeedBoxes(short[]? xMins, int numGlyphs)
    {
        if (xMins is null || xMins.Length < numGlyphs)
        {
            throw new InvalidDataException(
                "The transformed hmtx table needs glyph boxes, which a null-transformed glyf table cannot supply.");
        }
    }
}
