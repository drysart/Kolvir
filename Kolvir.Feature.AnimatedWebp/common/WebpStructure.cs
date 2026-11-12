using System.Buffers.Binary;
using System.Text;

namespace Kolvir;

/// <summary>
/// Reads the parts of an animated WebP that SkiaSharp does not expose as a stream of frames: the
/// canvas, the loop declaration, and one <c>ANMF</c> per frame with its rectangle, duration, blend
/// and disposal flags.
/// </summary>
/// <remarks>
/// <para>
/// WebP is a RIFF container: <c>RIFF&lt;size&gt;WEBP</c>, then chunks of a four-character code, a
/// 32-bit little-endian size and a payload padded to an even length. An animation is a <c>VP8X</c>
/// chunk whose animation bit is set, an <c>ANIM</c> chunk carrying the background colour and the
/// loop count, and one <c>ANMF</c> chunk per frame. A still WebP has no <c>VP8X</c> animation bit and
/// no <c>ANMF</c> chunks at all.
/// </para>
/// <para>
/// <b>The loop rule is the GIF feature's, on purpose.</b> <c>ANIM</c> holds 0 for "forever" and N for
/// "N times"; the playback model has a boolean, and a finite count is a total number of passes that
/// nothing else in the timeline tracks. Any declared count therefore means <c>Loop</c> starts true,
/// and a file with no <c>ANIM</c> chunk plays once. The reasoning is written out in the README and in
/// the GIF feature's <c>GifStructure</c>; this class deliberately does not invent a second answer.
/// </para>
/// <para>
/// <b>The background colour is read and deliberately not used for disposal.</b> The spec calls the
/// disposal method "restore to background", but browsers and libwebp clear the rectangle to
/// transparent, and the colour is absent or meaningless in most files. Clearing to transparent is
/// what the GIF compositor does for the same reason, and a control that fills its own background
/// cannot tell the difference.
/// </para>
/// <para>
/// Each frame's image data is wrapped in a stand-alone RIFF container as it is parsed. That is not a
/// second decode: it is the same <c>ALPH</c> and <c>VP8</c>/<c>VP8L</c> bytes the animation carries,
/// placed where <see cref="SkiaSharp.SKBitmap"/> can read one frame without compositing it onto the
/// frames before it.
/// </para>
/// </remarks>
internal static class WebpStructure
{
    private const int RiffHeaderLength = 12;
    private const int AnmfHeaderLength = 16;

    /// <summary>One frame's rectangle, timing and the image data it owns.</summary>
    internal readonly record struct Frame(
        int Left,
        int Top,
        int Width,
        int Height,
        int DurationMilliseconds,
        bool Blends,
        bool DisposesToBackground,
        byte[] Encoded);

    /// <summary>An animation's canvas, loop declaration and frames.</summary>
    internal sealed class Animation(int width, int height, bool loops, uint backgroundColor, Frame[] frames)
    {
        internal int Width { get; } = width;

        internal int Height { get; } = height;

        internal bool Loops { get; } = loops;

        /// <summary>The <c>ANIM</c> background colour, BGRA. Recorded, not used for disposal.</summary>
        internal uint BackgroundColor { get; } = backgroundColor;

        internal Frame[] Frames { get; } = frames;
    }

    /// <summary>
    /// Parses an animated WebP, or returns null when the file is a still picture.
    /// </summary>
    /// <param name="data">The whole file.</param>
    /// <returns>The animation, or null for a still WebP or a stream that is not a WebP.</returns>
    internal static Animation? Parse(ReadOnlySpan<byte> data)
    {
        if (data.Length < RiffHeaderLength
            || !data[..4].SequenceEqual("RIFF"u8)
            || !data[8..12].SequenceEqual("WEBP"u8))
        {
            return null;
        }

        // A RIFF size shorter than the bytes present is a truncated file; one longer is a file with
        // something appended to it. Walk what is there rather than trusting either number.
        int end = (int)Math.Min(data.Length, (long)BinaryPrimitives.ReadUInt32LittleEndian(data[4..8]) + 8);
        if (end < RiffHeaderLength)
        {
            return null;
        }

        bool animated = false;
        bool declaresLoop = false;
        uint backgroundColor = 0;
        int width = 0;
        int height = 0;
        var frames = new List<Frame>();

        int position = RiffHeaderLength;
        while (position + 8 <= end)
        {
            var fourCC = data.Slice(position, 4);
            int size = BinaryPrimitives.ReadInt32LittleEndian(data.Slice(position + 4, 4));
            if (size < 0 || (long)position + 8 + size > end)
            {
                // A length that runs past the file means the rest cannot be walked.
                break;
            }

            var payload = data.Slice(position + 8, size);

            if (fourCC.SequenceEqual("VP8X"u8) && payload.Length >= 10)
            {
                // Bit 1 is the animation flag, bit 4 says at least one frame carries alpha.
                animated = (payload[0] & 0x02) != 0;
                width = (int)U24(payload[4..7]) + 1;
                height = (int)U24(payload[7..10]) + 1;
            }
            else if (fourCC.SequenceEqual("ANIM"u8) && payload.Length >= 6)
            {
                // The colour is BGRA and is recorded so a caller -- a future diagnostic, say -- can
                // see it; disposal still clears to transparent, see the remarks.
                backgroundColor = BinaryPrimitives.ReadUInt32LittleEndian(payload[..4]);
                declaresLoop = true;
            }
            else if (fourCC.SequenceEqual("ANMF"u8) && payload.Length >= AnmfHeaderLength)
            {
                int left = (int)U24(payload[0..3]) * 2;
                int top = (int)U24(payload[3..6]) * 2;
                int frameWidth = (int)U24(payload[6..9]) + 1;
                int frameHeight = (int)U24(payload[9..12]) + 1;
                int durationMilliseconds = (int)U24(payload[12..15]);
                byte flags = payload[15];

                // Bit 1 is "do not blend" and bit 0 is "dispose to background". A set blend bit is
                // the replace case, which is the opposite of GIF's flag in name but not in effect.
                bool blends = (flags & 0x02) == 0;
                bool disposesToBackground = (flags & 0x01) != 0;

                frames.Add(new Frame(
                    left,
                    top,
                    frameWidth,
                    frameHeight,
                    durationMilliseconds,
                    blends,
                    disposesToBackground,
                    WrapFrame(payload[AnmfHeaderLength..], frameWidth, frameHeight)));
            }

            position += 8 + size + (size & 1);
        }

        if (!animated || width <= 0 || height <= 0 || frames.Count == 0)
        {
            return null;
        }

        return new Animation(width, height, declaresLoop, backgroundColor, frames.ToArray());
    }

    /// <summary>
    /// Puts one frame's image data into a stand-alone WebP, so it decodes to the frame alone rather
    /// than to the canvas with every earlier frame composited onto it.
    /// </summary>
    private static byte[] WrapFrame(ReadOnlySpan<byte> chunks, int width, int height)
    {
        var image = new List<(string FourCC, byte[] Payload)>();

        int position = 0;
        while (position + 8 <= chunks.Length)
        {
            var fourCC = Encoding.ASCII.GetString(chunks.Slice(position, 4));
            int size = BinaryPrimitives.ReadInt32LittleEndian(chunks.Slice(position + 4, 4));
            if (size < 0 || (long)position + 8 + size > chunks.Length)
            {
                break;
            }

            if (fourCC is "ALPH" or "VP8 " or "VP8L")
            {
                image.Add((fourCC, chunks.Slice(position + 8, size).ToArray()));
            }

            position += 8 + size + (size & 1);
        }

        if (image.Count == 0)
        {
            throw new InvalidOperationException("A WebP frame carries no image data.");
        }

        // A lossy frame can carry its alpha in a separate ALPH chunk, which only a VP8X container
        // can describe. Lossless frames carry alpha in the VP8L bitstream and need no VP8X.
        bool hasAlphaChunk = image.Exists(entry => entry.FourCC == "ALPH");
        bool isLossless = image.Exists(entry => entry.FourCC == "VP8L");

        var body = new List<byte>();

        if (hasAlphaChunk && !isLossless)
        {
            var header = new byte[10];
            header[0] = 0x10;
            U24(header.AsSpan(4), width - 1);
            U24(header.AsSpan(7), height - 1);
            AddChunk(body, "VP8X", header);
        }

        foreach (var (fourCC, payload) in image)
        {
            AddChunk(body, fourCC, payload);
        }

        var file = new List<byte>(RiffHeaderLength + body.Count);
        file.AddRange("RIFF"u8.ToArray());
        file.AddRange(BitConverter.GetBytes(4 + body.Count));
        file.AddRange("WEBP"u8.ToArray());
        file.AddRange(body);
        return file.ToArray();
    }

    private static void AddChunk(List<byte> destination, string fourCC, byte[] payload)
    {
        destination.AddRange(Encoding.ASCII.GetBytes(fourCC));
        destination.AddRange(BitConverter.GetBytes(payload.Length));
        destination.AddRange(payload);

        if ((payload.Length & 1) != 0)
        {
            destination.Add(0);
        }
    }

    private static uint U24(ReadOnlySpan<byte> bytes) =>
        (uint)(bytes[0] | bytes[1] << 8 | bytes[2] << 16);

    private static void U24(Span<byte> destination, int value)
    {
        destination[0] = (byte)value;
        destination[1] = (byte)(value >> 8);
        destination[2] = (byte)(value >> 16);
    }
}
