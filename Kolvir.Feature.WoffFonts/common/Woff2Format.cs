using System.Buffers.Binary;
using System.IO.Compression;

namespace Kolvir.WoffFonts;

/// <summary>
/// Unwraps a WOFF 2 file into the OpenType font it carries.
/// </summary>
/// <remarks>
/// <para>
/// WOFF 2 is a different problem from WOFF 1.0. The container is smaller (a variable-length directory
/// and one Brotli stream) but the payload is not the font's tables: <c>glyf</c> arrives re-encoded as
/// separate streams of contours, points, flags and coordinate triplets, and <c>loca</c> is not stored
/// at all. So this file reads the container and hands the outline work to
/// <see cref="Woff2Glyf"/> - which is where the effort in this feature actually is.
/// </para>
/// <para>
/// Brotli is in the BCL, so the compression half costs nothing: <see cref="BrotliStream"/> is the
/// whole dependency.
/// </para>
/// </remarks>
internal static class Woff2Format
{
    /// <summary>The four bytes every WOFF 2 file starts with.</summary>
    public static ReadOnlySpan<byte> Signature => "wOF2"u8;

    /// <summary>The flavor of a font collection, which both decoders refuse by name.</summary>
    internal const uint CollectionFlavor = 0x74746366;

    private const int HeaderSize = 48;

    /// <summary>
    /// The 63 tags the directory can name with six bits instead of four characters, in the order the
    /// format numbers them.
    /// </summary>
    private static readonly string[] KnownTags =
    [
        "cmap", "head", "hhea", "hmtx", "maxp", "name", "OS/2", "post", "cvt ", "fpgm", "glyf", "loca",
        "prep", "CFF ", "VORG", "EBDT", "EBLC", "gasp", "hdmx", "kern", "LTSH", "PCLT", "VDMX", "vhea",
        "vmtx", "BASE", "GDEF", "GPOS", "GSUB", "EBSC", "JSTF", "MATH", "CBDT", "CBLC", "COLR", "CPAL",
        "SVG ", "sbix", "acnt", "avar", "bdat", "bloc", "bsln", "cvar", "fdsc", "feat", "fmtx", "fvar",
        "gvar", "hsty", "just", "lcar", "mort", "morx", "opbd", "prop", "trak", "Zapf", "Silf", "Glat",
        "Gloc", "Feat", "Sill",
    ];

    private readonly record struct DirectoryEntry(
        uint Tag,
        int TransformVersion,
        int OriginalLength,
        int StreamLength,
        bool Transformed);

    /// <summary>Unwraps WOFF 2 data.</summary>
    /// <param name="woff2">The whole file.</param>
    /// <returns>A complete OpenType font.</returns>
    /// <exception cref="InvalidDataException">The file is damaged, or holds a font collection.</exception>
    public static byte[] Unwrap(ReadOnlySpan<byte> woff2)
    {
        if (woff2.Length < HeaderSize)
        {
            throw new InvalidDataException("The WOFF 2 header is truncated.");
        }

        var flavor = BinaryPrimitives.ReadUInt32BigEndian(woff2[4..]);
        var length = BinaryPrimitives.ReadUInt32BigEndian(woff2[8..]);
        var tableCount = BinaryPrimitives.ReadUInt16BigEndian(woff2[12..]);
        var compressedLength = BinaryPrimitives.ReadUInt32BigEndian(woff2[20..]);

        if (length > woff2.Length)
        {
            throw new InvalidDataException("The WOFF 2 file is truncated.");
        }

        if (flavor == CollectionFlavor)
        {
            throw new InvalidDataException("Font collections are not supported.");
        }

        if (tableCount == 0)
        {
            throw new InvalidDataException("The WOFF 2 file has no tables.");
        }

        var directory = ReadDirectory(woff2, tableCount, out var dataStart);

        var block = Decompress(woff2, dataStart, compressedLength);

        // The format requires the decompressed stream to be exactly the tables, in directory order,
        // with nothing between them - which also makes a truncated Brotli stream detectable here
        // rather than as a table that decodes to rubbish.
        var expected = directory.Sum(entry => entry.StreamLength);
        if (block.Length != expected)
        {
            throw new InvalidDataException(
                $"The WOFF 2 data block is {block.Length} bytes, but the table directory describes {expected}.");
        }

        var streams = new ReadOnlyMemory<byte>[directory.Length];
        var at = 0;
        for (var i = 0; i < directory.Length; i++)
        {
            streams[i] = block.AsMemory(at, directory[i].StreamLength);
            at += directory[i].StreamLength;
        }

        return Assemble(flavor, directory, streams);
    }

    private static DirectoryEntry[] ReadDirectory(ReadOnlySpan<byte> woff2, int tableCount, out int dataStart)
    {
        var entries = new DirectoryEntry[tableCount];
        var offset = HeaderSize;

        for (var i = 0; i < tableCount; i++)
        {
            if (offset >= woff2.Length)
            {
                throw new InvalidDataException("The WOFF 2 table directory is truncated.");
            }

            var flags = woff2[offset++];
            var tagIndex = flags & 0x3F;

            string tag;
            if (tagIndex == 0x3F)
            {
                if (offset + 4 > woff2.Length)
                {
                    throw new InvalidDataException("The WOFF 2 table directory is truncated.");
                }

                tag = System.Text.Encoding.ASCII.GetString(woff2.Slice(offset, 4));
                offset += 4;
            }
            else
            {
                tag = KnownTags[tagIndex];
            }

            if (tag.Length != 4)
            {
                throw new InvalidDataException($"The WOFF 2 table directory names an unknown tag '{tag}'.");
            }

            var transformVersion = (flags >> 6) & 0x3;

            // The meaning of the transform bits is inverted for glyf and loca: zero means transformed.
            var transformed = tag is "glyf" or "loca" ? transformVersion == 0 : transformVersion != 0;

            var originalLength = ReadBase128(woff2, ref offset);
            var streamLength = transformed ? ReadBase128(woff2, ref offset) : originalLength;

            entries[i] = new DirectoryEntry(SfntBuilder.Tag(tag), transformVersion, originalLength, streamLength, transformed);
        }

        dataStart = offset;
        return entries;
    }

    private static byte[] Decompress(ReadOnlySpan<byte> woff2, int at, uint compressedLength)
    {
        if (at + (long)compressedLength > woff2.Length)
        {
            throw new InvalidDataException("The WOFF 2 compressed data block is out of bounds.");
        }

        try
        {
            using var input = new MemoryStream(woff2[at..(at + (int)compressedLength)].ToArray(), writable: false);
            using var brotli = new BrotliStream(input, CompressionMode.Decompress);
            using var output = new MemoryStream();
            brotli.CopyTo(output);
            return output.ToArray();
        }
        catch (InvalidDataException)
        {
            throw;
        }
        catch (Exception exception)
        {
            throw new InvalidDataException(
                $"The WOFF 2 data block could not be decompressed ({at}+{compressedLength}): {exception.Message}",
                exception);
        }
    }

    /// <summary>
    /// Turns the table streams into a font, reconstructing the three tables WOFF 2 can transform.
    /// </summary>
    private static byte[] Assemble(uint flavor, DirectoryEntry[] directory, ReadOnlyMemory<byte>[] streams)
    {
        var glyfIndex = IndexOf(directory, SfntBuilder.Tag("glyf"));
        var locaIndex = IndexOf(directory, SfntBuilder.Tag("loca"));
        var headIndex = IndexOf(directory, SfntBuilder.Tag("head"));
        var maxpIndex = IndexOf(directory, SfntBuilder.Tag("maxp"));
        var hheaIndex = IndexOf(directory, SfntBuilder.Tag("hhea"));
        var hmtxIndex = IndexOf(directory, SfntBuilder.Tag("hmtx"));

        var tables = new List<SfntBuilder.Table>(directory.Length);
        short[]? xMins = null;
        byte[]? glyf = null;
        byte[]? loca = null;

        if (glyfIndex >= 0 && directory[glyfIndex].Transformed)
        {
            var head = Require(headIndex, "head", streams);
            var maxp = Require(maxpIndex, "maxp", streams);
            var numGlyphs = SfntBuilder.ReadUInt16(maxp, 4);
            var indexFormat = SfntBuilder.ReadInt16(head, 50);

            if (locaIndex < 0 || !directory[locaIndex].Transformed)
            {
                throw new InvalidDataException("A transformed glyf table must be accompanied by a transformed loca table.");
            }

            if (directory[locaIndex].StreamLength != 0)
            {
                throw new InvalidDataException("A transformed loca table must be empty.");
            }

            var expectedLoca = (numGlyphs + 1) * (indexFormat == 0 ? 2 : 4);
            if (directory[locaIndex].OriginalLength != expectedLoca)
            {
                throw new InvalidDataException(
                    $"The loca table declares {directory[locaIndex].OriginalLength} bytes; {expectedLoca} fit {numGlyphs} glyphs.");
            }

            (glyf, loca, xMins) = Woff2Glyf.Reconstruct(streams[glyfIndex].Span, numGlyphs, indexFormat);
        }

        for (var i = 0; i < directory.Length; i++)
        {
            var entry = directory[i];
            byte[] data;

            if (i == glyfIndex && glyf is not null)
            {
                data = glyf;
            }
            else if (i == locaIndex && loca is not null)
            {
                data = loca;
            }
            else if (i == hmtxIndex && entry.Transformed)
            {
                var hhea = Require(hheaIndex, "hhea", streams);
                var maxp = Require(maxpIndex, "maxp", streams);
                data = Woff2Hmtx.Reconstruct(
                    streams[i],
                    SfntBuilder.ReadUInt16(hhea, 34),
                    SfntBuilder.ReadUInt16(maxp, 4),
                    xMins);
            }
            else if (i == headIndex && !entry.Transformed)
            {
                // Bit 11 of head.flags marks a font that came through a modifying transform, which is
                // what a WOFF 2 glyph reconstruction is. A reader that cares uses it to avoid trusting
                // the glyph checksums, which no longer match the original.
                data = streams[i].ToArray();
                if (data.Length > 17)
                {
                    SfntBuilder.WriteUInt16(data, 16, (ushort)(SfntBuilder.ReadUInt16(data, 16) | (1 << 11)));
                }
            }
            else
            {
                data = streams[i].ToArray();
            }

            tables.Add(new SfntBuilder.Table(entry.Tag, data));
        }

        return SfntBuilder.Build(flavor, tables);
    }

    private static int IndexOf(DirectoryEntry[] directory, uint tag) =>
        Array.FindIndex(directory, entry => entry.Tag == tag);

    private static ReadOnlySpan<byte> Require(int index, string tag, ReadOnlyMemory<byte>[] streams) =>
        index >= 0
            ? streams[index].Span
            : throw new InvalidDataException($"The WOFF 2 file has no '{tag}' table, which a transform needs.");

    /// <summary>
    /// The directory's own variable-length integer: seven bits per byte, most significant first, with
    /// the high bit marking another byte to come.
    /// </summary>
    /// <remarks>
    /// This is <em>not</em> the <see cref="Read255UInt16"/> used inside the transformed tables. The
    /// two encodings differ in a way that looks harmless and is not: reading a base-128 length as a
    /// 255UInt16 silently consumes the wrong number of bytes and leaves the directory three bytes past
    /// where it should be, per table.
    /// </remarks>
    internal static int ReadBase128(ReadOnlySpan<byte> data, ref int offset)
    {
        var value = 0;

        for (var i = 0; i < 5; i++)
        {
            if (offset >= data.Length)
            {
                throw new InvalidDataException("The WOFF 2 stream is truncated.");
            }

            var next = data[offset++];
            if (i == 0 && next == 0x80)
            {
                throw new InvalidDataException("A WOFF 2 length starts with a redundant zero byte.");
            }

            value = (value << 7) | (next & 0x7F);

            if ((next & 0x80) == 0)
            {
                return value;
            }
        }

        throw new InvalidDataException("A WOFF 2 length runs past five bytes.");
    }

    /// <summary>The format's variable-length unsigned integer, which packs most values into one byte.</summary>
    internal static int Read255UInt16(ReadOnlySpan<byte> data, ref int offset)
    {
        if (offset >= data.Length)
        {
            throw new InvalidDataException("The WOFF 2 stream is truncated.");
        }

        var code = data[offset++];

        switch (code)
        {
            case 253:
                if (offset + 2 > data.Length)
                {
                    throw new InvalidDataException("The WOFF 2 stream is truncated.");
                }

                var value = (data[offset] << 8) | data[offset + 1];
                offset += 2;
                return value;

            case 254:
                if (offset >= data.Length)
                {
                    throw new InvalidDataException("The WOFF 2 stream is truncated.");
                }

                return data[offset++] + 506;

            case 255:
                if (offset >= data.Length)
                {
                    throw new InvalidDataException("The WOFF 2 stream is truncated.");
                }

                return data[offset++] + 253;

            default:
                return code;
        }
    }
}
