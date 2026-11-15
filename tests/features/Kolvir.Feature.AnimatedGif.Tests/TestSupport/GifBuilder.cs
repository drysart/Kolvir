namespace Kolvir.AnimatedGif.Tests;

/// <summary>
/// Builds GIF89a files frame by frame, so a test can state exactly what a file contains.
/// </summary>
/// <remarks>
/// <para>
/// The fixtures are built here rather than checked in as binaries because the claims that matter are
/// about pixels, delays and disposal methods: a test that asserts "after a <c>RestoreBackground</c>
/// frame, the pixels outside the next frame's rectangle are transparent" needs to know every one of
/// those values, and a checked-in file makes them something to re-derive with another tool whenever
/// the file changes.
/// </para>
/// <para>
/// The image data is LZW-compressed with a clear code every 250 pixels instead of a real dictionary
/// encoder. That is valid GIF - a decoder resets its dictionary on the clear code - and it keeps the
/// encoder to a few lines whose correctness can be read.
/// </para>
/// <para>
/// Palette index 0 is fully transparent, 1 is red, 2 is green, 3 is blue and 4 is white. Everything
/// else is unused.
/// </para>
/// </remarks>
internal sealed class GifBuilder(int width, int height, int? loopCount = null)
{
    internal const byte Transparent = 0;
    internal const byte Red = 1;
    internal const byte Green = 2;
    internal const byte Blue = 3;
    internal const byte White = 4;

    private readonly List<byte[]> _frames = [];
    private readonly List<FrameSettings> _settings = [];

    /// <summary>Adds a frame whose rectangle is the whole canvas.</summary>
    internal GifBuilder AddFrame(byte[] indexes, int delayCentiseconds = 10, int disposal = 1)
    {
        return AddFrame(0, 0, width, height, indexes, delayCentiseconds, disposal, transparent: false);
    }

    /// <summary>Adds a frame covering <paramref name="frameWidth"/> x <paramref name="frameHeight"/> at an offset.</summary>
    internal GifBuilder AddFrame(
        int left,
        int top,
        int frameWidth,
        int frameHeight,
        byte[] indexes,
        int delayCentiseconds = 10,
        int disposal = 1,
        bool transparent = false)
    {
        if (indexes.Length != frameWidth * frameHeight)
        {
            throw new ArgumentException("The frame's pixel count does not match its rectangle.", nameof(indexes));
        }

        _frames.Add(indexes);
        _settings.Add(new FrameSettings(left, top, frameWidth, frameHeight, delayCentiseconds, disposal, transparent));
        return this;
    }

    /// <summary>Writes the file.</summary>
    internal byte[] ToArray()
    {
        var bytes = new List<byte>();
        bytes.AddRange("GIF89a"u8.ToArray());

        // Logical screen descriptor: a global colour table of 256 entries, which is what makes the
        // one-byte indices below addressable.
        bytes.AddRange([(byte)(width & 0xFF), (byte)(width >> 8), (byte)(height & 0xFF), (byte)(height >> 8)]);
        bytes.Add(0xF7);
        bytes.Add(0x00);
        bytes.Add(0x00);

        for (int index = 0; index < 256; index++)
        {
            (byte red, byte green, byte blue) = index switch
            {
                Red => ((byte)0xFF, (byte)0x00, (byte)0x00),
                Green => ((byte)0x00, (byte)0x80, (byte)0x00),
                Blue => ((byte)0x00, (byte)0x00, (byte)0xFF),
                White => ((byte)0xFF, (byte)0xFF, (byte)0xFF),
                _ => ((byte)0x00, (byte)0x00, (byte)0x00),
            };

            bytes.Add(red);
            bytes.Add(green);
            bytes.Add(blue);
        }

        if (loopCount is int count)
        {
            bytes.AddRange([0x21, 0xFF, 0x0B]);
            bytes.AddRange("NETSCAPE2.0"u8.ToArray());
            bytes.AddRange([0x03, 0x01, (byte)(count & 0xFF), (byte)(count >> 8), 0x00]);
        }

        for (int index = 0; index < _frames.Count; index++)
        {
            var frame = _settings[index];
            int packed = (frame.Disposal << 2) | (frame.Transparent ? 1 : 0);

            bytes.AddRange(
            [
                0x21, 0xF9, 0x04, (byte)packed,
                (byte)(frame.DelayCentiseconds & 0xFF), (byte)(frame.DelayCentiseconds >> 8),
                0x00, 0x00,
            ]);

            bytes.Add(0x2C);
            bytes.AddRange([(byte)(frame.Left & 0xFF), (byte)(frame.Left >> 8)]);
            bytes.AddRange([(byte)(frame.Top & 0xFF), (byte)(frame.Top >> 8)]);
            bytes.AddRange([(byte)(frame.Width & 0xFF), (byte)(frame.Width >> 8)]);
            bytes.AddRange([(byte)(frame.Height & 0xFF), (byte)(frame.Height >> 8)]);
            bytes.Add(0x00);

            bytes.Add(8);
            bytes.AddRange(Encode(_frames[index]));
            bytes.Add(0x00);
        }

        bytes.Add(0x3B);
        return bytes.ToArray();
    }

    private static byte[] Encode(byte[] pixels)
    {
        const int clear = 256;
        const int end = 257;

        var output = new List<byte>();
        int bits = 0;
        int bitCount = 0;

        void Emit(int code)
        {
            bits |= code << bitCount;
            bitCount += 9;

            while (bitCount >= 8)
            {
                output.Add((byte)(bits & 0xFF));
                bits >>= 8;
                bitCount -= 8;
            }
        }

        Emit(clear);

        int sinceClear = 0;
        foreach (byte pixel in pixels)
        {
            Emit(pixel);

            // 250 literals keeps the decoder's next free code below 512, so the code size never has
            // to grow and a clear code resets everything before it could.
            if (++sinceClear == 250)
            {
                Emit(clear);
                sinceClear = 0;
            }
        }

        Emit(end);

        if (bitCount > 0)
        {
            output.Add((byte)(bits & 0xFF));
        }

        var blocks = new List<byte>();
        for (int index = 0; index < output.Count; index += 255)
        {
            int length = Math.Min(255, output.Count - index);
            blocks.Add((byte)length);
            blocks.AddRange(output.GetRange(index, length));
        }

        return blocks.ToArray();
    }

    private readonly record struct FrameSettings(
        int Left,
        int Top,
        int Width,
        int Height,
        int DelayCentiseconds,
        int Disposal,
        bool Transparent);
}
