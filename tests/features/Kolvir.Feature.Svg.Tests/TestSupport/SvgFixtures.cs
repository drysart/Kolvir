using SkiaSharp;

namespace Kolvir.Svg.Tests;

/// <summary>
/// The documents and bitmaps the suite loads, kept in one place so a test reads as an assertion
/// rather than as a wall of markup.
/// </summary>
internal static class SvgFixtures
{
    /// <summary>A square document whose whole area is one colour.</summary>
    internal const string Square = """
        <svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 10 10">
          <rect width="10" height="10" fill="#3366cc"/>
        </svg>
        """;

    /// <summary>A document twice as wide as it is tall, for the SizeMode tests.</summary>
    internal const string WideRectangle = """
        <svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 8 4">
          <rect width="8" height="4" fill="#3366cc"/>
        </svg>
        """;

    /// <summary>The colour those two documents paint.</summary>
    internal static readonly SKColor Blue = new(0x33, 0x66, 0xCC);

    /// <summary>A document that draws with <c>currentColor</c>.</summary>
    internal const string CurrentColorIcon = """
        <svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 24 24">
          <path d="M12 2 L22 22 L2 22 Z" fill="currentColor"/>
        </svg>
        """;

    /// <summary>A document with a named colour, which a tint must not change.</summary>
    internal const string FixedColourIcon = """
        <svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 10 10">
          <rect width="10" height="10" fill="#cc0000"/>
        </svg>
        """;

    /// <summary>Bytes of a document, which is what every load takes.</summary>
    internal static byte[] Bytes(string svg) => System.Text.Encoding.UTF8.GetBytes(svg);

    internal static MemoryStream Stream(string svg) => new(Bytes(svg));

    /// <summary>A PNG of one colour, 4 by 2, for the raster regression tests.</summary>
    internal static byte[] Png(SKColor color, int width = 4, int height = 2) =>
        Encode(SKEncodedImageFormat.Png, color, width, height);

    internal static byte[] Jpeg(SKColor color, int width = 4, int height = 2) =>
        Encode(SKEncodedImageFormat.Jpeg, color, width, height);

    internal static byte[] Webp(SKColor color, int width = 4, int height = 2) =>
        Encode(SKEncodedImageFormat.Webp, color, width, height);

    /// <summary>
    /// A 1x1 GIF, which SkiaSharp will not encode but every toolkit is expected to read.
    /// </summary>
    internal static byte[] Gif() => Convert.FromBase64String(
        "R0lGODlhAQABAIAAAAAAAP///yH5BAEAAAAALAAAAAABAAEAAAIBRAA7");

    /// <summary>A 2x2 uncompressed 24-bit BMP, built here because Skia does not encode one.</summary>
    internal static byte[] Bmp()
    {
        const int width = 2;
        const int height = 2;
        int rowBytes = width * 3;
        int padding = (4 - (rowBytes % 4)) % 4;
        int stride = rowBytes + padding;
        int pixelBytes = stride * height;
        int fileSize = 14 + 40 + pixelBytes;

        var bytes = new byte[fileSize];

        // BITMAPFILEHEADER: "BM", file size, two reserved words, offset to the pixels.
        bytes[0] = (byte)'B';
        bytes[1] = (byte)'M';
        BitConverter.TryWriteBytes(bytes.AsSpan(2), fileSize);
        BitConverter.TryWriteBytes(bytes.AsSpan(10), 14 + 40);

        // BITMAPINFOHEADER: 40 bytes, then dimensions, planes and 24 bits per pixel.
        BitConverter.TryWriteBytes(bytes.AsSpan(14), 40);
        BitConverter.TryWriteBytes(bytes.AsSpan(18), width);
        BitConverter.TryWriteBytes(bytes.AsSpan(22), height);
        BitConverter.TryWriteBytes(bytes.AsSpan(26), (short)1);
        BitConverter.TryWriteBytes(bytes.AsSpan(28), (short)24);
        BitConverter.TryWriteBytes(bytes.AsSpan(34), pixelBytes);

        // Rows are stored bottom-up and in BGR order.
        for (int row = 0; row < height; row++)
        {
            int start = 54 + (row * stride);
            for (int column = 0; column < width; column++)
            {
                int offset = start + (column * 3);
                bytes[offset] = 0x33;
                bytes[offset + 1] = 0x66;
                bytes[offset + 2] = 0xCC;
            }
        }

        return bytes;
    }

    private static byte[] Encode(SKEncodedImageFormat format, SKColor color, int width, int height)
    {
        using var bitmap = new SKBitmap(width, height);
        bitmap.Erase(color);
        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(format, 100);
        return data.ToArray();
    }
}
