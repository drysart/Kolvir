namespace XPlatUI;

/// <summary>
/// Reads the one piece of a GIF that SkiaSharp does not report: whether the file declares how many
/// times it should play.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="SkiaSharp.SKCodec.RepetitionCount"/> answers -1 for every GIF this library has measured,
/// including files with no loop extension at all, so it cannot be used to tell "loop forever" from
/// "play once". The Netscape extension is a small, strictly structured application block, and walking
/// the block chain to the end of it is cheaper and more reliable than deciding from a decoded frame
/// count that may not exist.
/// </para>
/// <para>
/// The answer is only "does the file declare a loop count", not what the count is: any count present
/// - 0 for forever or N for N times - means the animation loops, and a file with no count plays once.
/// That is a deliberate simplification of the GIF spec, recorded in the README and the pull request:
/// the playback model has a boolean <c>Loop</c>, and a declared count of three is "loops", not
/// "loops three times".
/// </para>
/// </remarks>
internal static class XPlatGifStructure
{
    /// <summary>True when the file carries a Netscape loop sub-block.</summary>
    internal static bool DeclaresLoop(ReadOnlySpan<byte> data)
    {
        // Header (6), logical screen descriptor (7).
        if (data.Length < 13)
        {
            return false;
        }

        byte screen = data[10];
        int position = 13;

        if ((screen & 0x80) != 0)
        {
            position += 3 * (1 << ((screen & 0x07) + 1));
        }

        while (position < data.Length)
        {
            byte block = data[position++];

            switch (block)
            {
                case 0x3B:
                    return false;

                case 0x21:
                    {
                        if (position >= data.Length)
                        {
                            return false;
                        }

                        byte label = data[position++];
                        if (label == 0xFF && SkipApplicationExtension(data, ref position))
                        {
                            return true;
                        }

                        position = SkipSubBlocks(data, position);
                        break;
                    }

                case 0x2C:
                    {
                        // Image descriptor: left, top, width, height, packed.
                        if (position + 9 > data.Length)
                        {
                            return false;
                        }

                        byte image = data[position + 8];
                        position += 9;

                        if ((image & 0x80) != 0)
                        {
                            position += 3 * (1 << ((image & 0x07) + 1));
                        }

                        // The LZW minimum code size, then the data sub-blocks.
                        position++;
                        position = SkipSubBlocks(data, position);
                        break;
                    }

                default:
                    // Not a block this reader understands; the rest of the file cannot be walked.
                    return false;
            }
        }

        return false;
    }

    /// <summary>
    /// Reads an application extension's identifier and, when it is the Netscape one, looks for its
    /// loop sub-block. Returns true when the loop count is there.
    /// </summary>
    private static bool SkipApplicationExtension(ReadOnlySpan<byte> data, ref int position)
    {
        if (position >= data.Length)
        {
            return false;
        }

        int size = data[position++];
        if (position + size > data.Length)
        {
            return false;
        }

        var identifier = data.Slice(position, size);
        position += size;

        bool netscape = identifier.SequenceEqual("NETSCAPE2.0"u8);

        while (position < data.Length)
        {
            int sub = data[position++];
            if (sub == 0)
            {
                return false;
            }

            if (position + sub > data.Length)
            {
                return false;
            }

            // "1" marks the loop-count sub-block: one byte of sub-block id, then the 16-bit count.
            if (netscape && sub >= 3 && data[position] == 0x01)
            {
                return true;
            }

            position += sub;
        }

        return false;
    }

    private static int SkipSubBlocks(ReadOnlySpan<byte> data, int position)
    {
        while (position < data.Length)
        {
            int size = data[position++];
            if (size == 0)
            {
                return position;
            }

            position += size;
        }

        return position;
    }
}
