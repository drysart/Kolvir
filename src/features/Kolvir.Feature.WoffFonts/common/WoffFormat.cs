using System.Buffers.Binary;
using System.IO.Compression;

namespace Kolvir.WoffFonts;

/// <summary>
/// Unwraps a WOFF 1.0 file into the OpenType font it carries.
/// </summary>
/// <remarks>
/// <para>
/// WOFF 1.0 is a container and nothing more: the same sfnt tables, each zlib-compressed, behind a
/// header and a table directory. So unwrapping is decompression and arithmetic - there is no font
/// knowledge in it at all, which is why this file is short and why a table's checksum can simply be
/// recomputed afterwards.
/// </para>
/// <para>
/// A table whose compressed length equals its original length is stored as-is rather than compressed,
/// which the format allows and encoders use when compression did not help. That is the only special
/// case in the loop.
/// </para>
/// </remarks>
internal static class WoffFormat
{
    /// <summary>The four bytes every WOFF file starts with.</summary>
    public static ReadOnlySpan<byte> Signature => "wOFF"u8;

    private const int HeaderSize = 44;
    private const int DirectoryEntrySize = 20;

    /// <summary>Unwraps WOFF 1.0 data.</summary>
    /// <param name="woff">The whole file.</param>
    /// <returns>A complete OpenType font.</returns>
    /// <exception cref="InvalidDataException">The file is damaged, or holds a font collection.</exception>
    public static byte[] Unwrap(ReadOnlySpan<byte> woff)
    {
        if (woff.Length < HeaderSize)
        {
            throw new InvalidDataException("The WOFF header is truncated.");
        }

        var flavor = BinaryPrimitives.ReadUInt32BigEndian(woff[4..]);
        var length = BinaryPrimitives.ReadUInt32BigEndian(woff[8..]);
        var tableCount = BinaryPrimitives.ReadUInt16BigEndian(woff[12..]);

        if (length > woff.Length)
        {
            throw new InvalidDataException("The WOFF file is truncated.");
        }

        // A collection is a different output format - a ttcf header with its own directories - so it
        // is refused by name rather than decoded into something that is not a font.
        if (flavor == Woff2Format.CollectionFlavor)
        {
            throw new InvalidDataException("Font collections are not supported.");
        }

        if (tableCount == 0 || HeaderSize + (tableCount * DirectoryEntrySize) > woff.Length)
        {
            throw new InvalidDataException("The WOFF table directory is truncated.");
        }

        var tables = new List<SfntBuilder.Table>(tableCount);

        for (var i = 0; i < tableCount; i++)
        {
            var at = HeaderSize + (i * DirectoryEntrySize);
            var tag = BinaryPrimitives.ReadUInt32BigEndian(woff[at..]);
            var offset = BinaryPrimitives.ReadUInt32BigEndian(woff[(at + 4)..]);
            var compressedLength = BinaryPrimitives.ReadUInt32BigEndian(woff[(at + 8)..]);
            var originalLength = BinaryPrimitives.ReadUInt32BigEndian(woff[(at + 12)..]);

            if (originalLength == 0 || compressedLength > originalLength || offset + (long)compressedLength > woff.Length)
            {
                throw new InvalidDataException($"The WOFF table '{SfntBuilder.TagName(tag)}' is out of bounds.");
            }

            var source = woff.Slice((int)offset, (int)compressedLength);

            // Equal lengths mean the table is stored literally, not compressed.
            var data = compressedLength == originalLength ? source.ToArray() : Inflate(source, tag, originalLength);

            tables.Add(new SfntBuilder.Table(tag, data));
        }

        return SfntBuilder.Build(flavor, tables);
    }

    private static byte[] Inflate(ReadOnlySpan<byte> source, uint tag, uint originalLength)
    {
        try
        {
            using var input = new MemoryStream(source.ToArray(), writable: false);
            using var zlib = new ZLibStream(input, CompressionMode.Decompress);
            using var output = new MemoryStream((int)originalLength);
            zlib.CopyTo(output);

            return output.Length == originalLength
                ? output.ToArray()
                : throw new InvalidDataException(
                    $"The WOFF table '{SfntBuilder.TagName(tag)}' decompressed to {output.Length} bytes, not {originalLength}.");
        }
        catch (InvalidDataException)
        {
            throw;
        }
        catch (Exception exception)
        {
            throw new InvalidDataException(
                $"The WOFF table '{SfntBuilder.TagName(tag)}' could not be decompressed.", exception);
        }
    }
}
