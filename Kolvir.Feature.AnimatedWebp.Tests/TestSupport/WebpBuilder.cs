using System.Buffers.Binary;
using System.Text;
using SkiaSharp;

namespace Kolvir.AnimatedWebp.Tests;

/// <summary>
/// Builds animated WebP files frame by frame, so a test can state exactly what a file contains.
/// </summary>
/// <remarks>
/// <para>
/// The fixtures are built here rather than checked in as binaries because the claims that matter are
/// about pixels, durations, blend bits and disposal bits: a test that asserts "after a dispose-to-
/// background frame, the pixels outside the next frame's rectangle are transparent" needs to know
/// every one of those values, and a checked-in file makes them something to re-derive with another
/// tool whenever the file changes.
/// </para>
/// <para>
/// Frames are encoded losslessly by default with <see cref="SKWebpEncoder"/>, so the colours a test
/// declares are the colours the decoder returns; a frame can be encoded lossily when a test needs a
/// separate <c>ALPH</c> chunk. The RIFF container is assembled here: a
/// <c>VP8X</c> chunk carrying the canvas and the animation flag, an <c>ANIM</c> chunk carrying the
/// loop count, and one <c>ANMF</c> per frame. That is the same layout the feature parses, which is
/// the point - the fixture is a WebP a browser would read, not a private format.
/// </para>
/// </remarks>
internal sealed class WebpBuilder(int width, int height, int? loopCount = 0)
{
    private static readonly SKWebpEncoderOptions Lossless = new(SKWebpEncoderCompression.Lossless, 100);
    private static readonly SKWebpEncoderOptions Lossy = new(SKWebpEncoderCompression.Lossy, 90);

    private readonly List<byte[]> _encoded = [];
    private readonly List<FrameSettings> _settings = [];

    /// <summary>Adds a frame whose rectangle is the whole canvas.</summary>
    internal WebpBuilder AddFrame(
        SKColor[] pixels,
        int durationMilliseconds = 100,
        bool blend = true,
        bool disposeToBackground = false,
        bool lossy = false)
    {
        return AddFrame(0, 0, width, height, pixels, durationMilliseconds, blend, disposeToBackground, lossy);
    }

    /// <summary>Adds a frame covering <paramref name="frameWidth"/> x <paramref name="frameHeight"/> at an offset.</summary>
    /// <remarks>A WebP frame offset is stored in two-pixel units, so both offsets must be even.</remarks>
    internal WebpBuilder AddFrame(
        int left,
        int top,
        int frameWidth,
        int frameHeight,
        SKColor[] pixels,
        int durationMilliseconds = 100,
        bool blend = true,
        bool disposeToBackground = false,
        bool lossy = false)
    {
        if (pixels.Length != frameWidth * frameHeight)
        {
            throw new ArgumentException("The frame's pixel count does not match its rectangle.", nameof(pixels));
        }

        if ((left & 1) != 0 || (top & 1) != 0)
        {
            throw new ArgumentException("A WebP frame offset is stored in two-pixel units, so it must be even.", nameof(left));
        }

        _encoded.Add(EncodeFrame(pixels, frameWidth, frameHeight, lossy));
        _settings.Add(new FrameSettings(
            left,
            top,
            frameWidth,
            frameHeight,
            durationMilliseconds,
            blend,
            disposeToBackground,
            pixels.Any(pixel => pixel.Alpha != 255)));

        return this;
    }

    /// <summary>Writes the file.</summary>
    internal byte[] ToArray()
    {
        var body = new List<byte>();

        // VP8X: animation, plus alpha when at least one frame declares a pixel that is not opaque.
        byte flags = 0x02;
        if (_settings.Any(frame => frame.HasAlpha))
        {
            flags |= 0x10;
        }

        var vp8x = new List<byte> { flags, 0, 0, 0 };
        vp8x.AddRange(U24(width - 1));
        vp8x.AddRange(U24(height - 1));
        body.AddRange(Chunk("VP8X", vp8x.ToArray()));

        if (loopCount is int count)
        {
            // Transparent background, then the 16-bit loop count (0 means forever).
            body.AddRange(Chunk("ANIM", [0, 0, 0, 0, (byte)(count & 0xFF), (byte)(count >> 8)]));
        }

        for (int index = 0; index < _encoded.Count; index++)
        {
            var frame = _settings[index];
            var payload = new List<byte>();

            payload.AddRange(U24(frame.Left / 2));
            payload.AddRange(U24(frame.Top / 2));
            payload.AddRange(U24(frame.Width - 1));
            payload.AddRange(U24(frame.Height - 1));
            payload.AddRange(U24(frame.DurationMilliseconds));

            // Bit 1 is "do not blend" and bit 0 is "dispose to background".
            payload.Add((byte)((frame.Blend ? 0 : 0x02) | (frame.DisposeToBackground ? 0x01 : 0)));
            payload.AddRange(_encoded[index]);

            body.AddRange(Chunk("ANMF", payload.ToArray()));
        }

        var file = new List<byte>();
        file.AddRange("RIFF"u8.ToArray());
        file.AddRange(BitConverter.GetBytes(4 + body.Count));
        file.AddRange("WEBP"u8.ToArray());
        file.AddRange(body);
        return file.ToArray();
    }

    /// <summary>Encodes one still WebP the way a browser would hand one over: RIFF, no animation.</summary>
    internal static byte[] EncodeStill(int width, int height, Func<int, int, SKColor> pixel)
    {
        using var bitmap = CreateBitmap(width, height, pixel);
        using var stream = new MemoryStream();

        if (!SKWebpEncoder.Encode(stream, bitmap.PeekPixels(), Lossless))
        {
            throw new InvalidOperationException("SkiaSharp could not encode the still fixture.");
        }

        return stream.ToArray();
    }

    /// <summary>The pieces of one frame that the container has to carry.</summary>
    private static byte[] EncodeFrame(SKColor[] pixels, int width, int height, bool lossy)
    {
        using var bitmap = new SKBitmap(new SKImageInfo(width, height, SKColorType.Rgba8888, SKAlphaType.Premul));

        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                bitmap.SetPixel(x, y, pixels[y * width + x]);
            }
        }

        using var stream = new MemoryStream();
        var options = lossy ? Lossy : Lossless;
        if (!SKWebpEncoder.Encode(stream, bitmap.PeekPixels(), options))
        {
            throw new InvalidOperationException("SkiaSharp could not encode the frame fixture.");
        }

        // A still file may carry a VP8X describing its own canvas; an ANMF frame carries only the
        // image chunks, because the ANMF header already says where and how big it is.
        var file = stream.ToArray();
        var image = new List<byte>();

        int position = 12;
        while (position + 8 <= file.Length)
        {
            var fourCC = Encoding.ASCII.GetString(file, position, 4);
            int size = BinaryPrimitives.ReadInt32LittleEndian(file.AsSpan(position + 4, 4));

            if (fourCC is "ALPH" or "VP8 " or "VP8L")
            {
                image.AddRange(file.AsSpan(position, 8 + size + (size & 1)).ToArray());
            }

            position += 8 + size + (size & 1);
        }

        return image.ToArray();
    }

    private static SKBitmap CreateBitmap(int width, int height, Func<int, int, SKColor> pixel)
    {
        var bitmap = new SKBitmap(new SKImageInfo(width, height, SKColorType.Rgba8888, SKAlphaType.Premul));

        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                bitmap.SetPixel(x, y, pixel(x, y));
            }
        }

        return bitmap;
    }

    private static byte[] U24(int value) => [(byte)value, (byte)(value >> 8), (byte)(value >> 16)];

    private static byte[] Chunk(string fourCC, byte[] payload)
    {
        var chunk = new List<byte>();
        chunk.AddRange(Encoding.ASCII.GetBytes(fourCC));
        chunk.AddRange(BitConverter.GetBytes(payload.Length));
        chunk.AddRange(payload);

        if ((payload.Length & 1) != 0)
        {
            chunk.Add(0);
        }

        return chunk.ToArray();
    }

    private readonly record struct FrameSettings(
        int Left,
        int Top,
        int Width,
        int Height,
        int DurationMilliseconds,
        bool Blend,
        bool DisposeToBackground,
        bool HasAlpha);
}
