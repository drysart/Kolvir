namespace Kolvir.WoffFonts;

/// <summary>
/// Assembles tables into an OpenType (sfnt) file: the format Skia reads, and the only thing either
/// decoder produces.
/// </summary>
/// <remarks>
/// <para>
/// Two containers, one output. A WOFF file already holds sfnt tables and a WOFF 2 file holds them
/// after reconstruction, so both end here rather than each growing its own writer - which is also why
/// the checksum rules are written once.
/// </para>
/// <para>
/// <b>The checksums are recomputed rather than carried over.</b> WOFF 1.0 stores each table's original
/// checksum, but the reconstructed file has different offsets and, for WOFF 2, different table
/// contents, so the stored values are only useful as a cross-check. What a strict reader wants is a
/// per-table checksum over the bytes actually written and a <c>head.checkSumAdjustment</c> that makes
/// the whole file sum to <c>0xB1B0AFBA</c>. Skia checks neither; a font that is written out to disk and
/// opened by something that does check would be a font that fails to load for no visible reason.
/// </para>
/// </remarks>
internal static class SfntBuilder
{
    /// <summary>The value every correctly checksummed OpenType file sums to.</summary>
    private const uint MagicChecksum = 0xB1B0AFBA;

    /// <summary>The table whose checksum is computed with its own adjustment field zeroed.</summary>
    private const uint HeadTag = 0x68656164;

    /// <summary>Where <c>checkSumAdjustment</c> sits inside the head table.</summary>
    private const int HeadAdjustmentOffset = 8;

    /// <summary>One decoded table: its four character tag and the bytes it is made of.</summary>
    internal readonly record struct Table(uint Tag, byte[] Data);

    /// <summary>Packs tables into a complete font file.</summary>
    /// <param name="flavor">The sfnt version: <c>0x00010000</c> for TrueType, <c>OTTO</c> for CFF.</param>
    /// <param name="tables">The tables. Their order does not matter; the sfnt directory is sorted.</param>
    /// <returns>The font's bytes.</returns>
    /// <remarks>
    /// The table records are sorted by tag because the format requires it, and a reader is entitled
    /// to binary-search them. Each table starts on a four byte boundary, and its checksum covers its
    /// own bytes padded with zeros to that boundary.
    /// </remarks>
    public static byte[] Build(uint flavor, IReadOnlyList<Table> tables)
    {
        var ordered = tables.OrderBy(table => table.Tag).ToArray();
        var directorySize = 12 + (ordered.Length * 16);

        var offset = directorySize;
        var offsets = new int[ordered.Length];
        for (var i = 0; i < ordered.Length; i++)
        {
            offsets[i] = offset;
            offset += Align4(ordered[i].Data.Length);
        }

        var sfnt = new byte[offset];
        var span = sfnt.AsSpan();

        WriteUInt32(span, 0, flavor);
        WriteUInt16(span, 4, (ushort)ordered.Length);
        WriteSearchHints(span, ordered.Length);

        for (var i = 0; i < ordered.Length; i++)
        {
            var at = 12 + (i * 16);
            var data = ordered[i].Data.AsSpan();

            WriteUInt32(span, at, ordered[i].Tag);
            WriteUInt32(span, at + 4, Checksum(data, zeroHeadAdjustment: ordered[i].Tag == HeadTag));
            WriteUInt32(span, at + 8, (uint)offsets[i]);
            WriteUInt32(span, at + 12, (uint)ordered[i].Data.Length);
            ordered[i].Data.CopyTo(span[offsets[i]..]);
        }

        // The adjustment is the difference between the file's checksum and the magic value, computed
        // with the field itself at zero - which it is now, because the head table was copied verbatim.
        var head = Array.FindIndex(ordered, table => table.Tag == HeadTag);
        if (head >= 0 && ordered[head].Data.Length >= HeadAdjustmentOffset + 4)
        {
            var adjustment = MagicChecksum - Checksum(sfnt);
            WriteUInt32(sfnt.AsSpan(), offsets[head] + HeadAdjustmentOffset, adjustment);
        }

        return sfnt;
    }

    private static void WriteSearchHints(Span<byte> span, int tableCount)
    {
        var entrySelector = 0;
        while ((1 << (entrySelector + 1)) <= tableCount)
        {
            entrySelector++;
        }

        var searchRange = 16 * (1 << entrySelector);
        WriteUInt16(span, 6, (ushort)searchRange);
        WriteUInt16(span, 8, (ushort)entrySelector);
        WriteUInt16(span, 10, (ushort)((tableCount * 16) - searchRange));
    }

    /// <summary>The sum of a table's bytes read as big-endian 32-bit words, padded with zeros.</summary>
    private static uint Checksum(ReadOnlySpan<byte> data, bool zeroHeadAdjustment = false)
    {
        uint sum = 0;
        var words = (data.Length + 3) / 4;

        for (var word = 0; word < words; word++)
        {
            var at = word * 4;
            uint value = 0;

            for (var byteIndex = 0; byteIndex < 4; byteIndex++)
            {
                var index = at + byteIndex;
                var isAdjustment = zeroHeadAdjustment
                    && index >= HeadAdjustmentOffset
                    && index < HeadAdjustmentOffset + 4;

                if (index < data.Length && !isAdjustment)
                {
                    value = (value << 8) | data[index];
                }
                else
                {
                    value <<= 8;
                }
            }

            sum += value;
        }

        return sum;
    }

    /// <summary>The four byte alignment every table starts on.</summary>
    internal static int Align4(int length) => (length + 3) & ~3;

    /// <summary>A tag as a name, for a message a caller can act on.</summary>
    internal static string TagName(uint tag) =>
        new([
            (char)((tag >> 24) & 0xFF),
            (char)((tag >> 16) & 0xFF),
            (char)((tag >> 8) & 0xFF),
            (char)(tag & 0xFF),
        ]);

    /// <summary>The tag a name spells, such as <c>glyf</c>.</summary>
    internal static uint Tag(string name) =>
        ((uint)name[0] << 24) | ((uint)name[1] << 16) | ((uint)name[2] << 8) | name[3];

    internal static ushort ReadUInt16(ReadOnlySpan<byte> data, int at) =>
        (ushort)((data[at] << 8) | data[at + 1]);

    internal static short ReadInt16(ReadOnlySpan<byte> data, int at) =>
        (short)ReadUInt16(data, at);

    internal static uint ReadUInt32(ReadOnlySpan<byte> data, int at) =>
        ((uint)data[at] << 24) | ((uint)data[at + 1] << 16) | ((uint)data[at + 2] << 8) | data[at + 3];

    internal static void WriteUInt16(Span<byte> data, int at, ushort value)
    {
        data[at] = (byte)(value >> 8);
        data[at + 1] = (byte)value;
    }

    internal static void WriteUInt32(Span<byte> data, int at, uint value)
    {
        data[at] = (byte)(value >> 24);
        data[at + 1] = (byte)(value >> 16);
        data[at + 2] = (byte)(value >> 8);
        data[at + 3] = (byte)value;
    }
}
