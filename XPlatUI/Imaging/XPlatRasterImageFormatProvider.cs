using SkiaSharp;

namespace XPlatUI;

/// <summary>
/// The decoder <see cref="XPlatImage"/> has always had, expressed as the lowest-priority provider so
/// that a feature can outrank it without the control naming the feature.
/// </summary>
/// <remarks>
/// <para>
/// It claims the raster formats by their magic bytes rather than by trying to decode, so an unknown
/// stream stays an unknown stream and a load of one still returns false without raising. That is the
/// behaviour this class replaces, kept deliberate rather than inherited.
/// </para>
/// <para>
/// The recording it does is the one <see cref="XPlatImage"/> used to do itself:
/// <see cref="SKBitmap.Decode"/> for the pixels, one draw of the decoded image into a picture at
/// identity with the sampling the control has always drawn with, so replaying the picture under a
/// destination scale paints exactly what a direct image draw painted.
/// </para>
/// </remarks>
internal sealed class XPlatRasterImageFormatProvider : IXPlatImageFormatProvider
{
    // The sampling a bitmap has always been drawn with. The same options are recorded into the
    // picture, so replaying it under the destination's scale samples exactly as a direct draw does.
    private static readonly SKSamplingOptions Sampling = new(SKFilterMode.Linear, SKMipmapMode.Linear);

    internal static XPlatRasterImageFormatProvider Shared { get; } = new();

    private XPlatRasterImageFormatProvider()
    {
    }

    public string Name => "raster";

    // Never removed, and outranked by everything else for the bytes both recognise.
    public int Priority => 0;

    public bool CanDecode(ReadOnlySpan<byte> header) =>
        StartsWith(header, [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A])                                     // PNG
        || StartsWith(header, [0xFF, 0xD8, 0xFF])                                                                   // JPEG
        || StartsWith(header, "GIF87a"u8) || StartsWith(header, "GIF89a"u8)                                         // GIF
        || StartsWith(header, "BM"u8)                                                                               // BMP
        || (StartsWith(header, "RIFF"u8) && header.Length >= 12 && StartsWith(header[8..], "WEBP"u8))                // WebP
        || StartsWith(header, [0x00, 0x00, 0x01, 0x00]);                                                            // ICO

    public SKPicture? Decode(Stream stream, string? extensionHint)
    {
        using var bitmap = SKBitmap.Decode(stream);
        if (bitmap is null)
        {
            return null;
        }

        // A picture recorded from a decoded bitmap shares that bitmap's pixels, and the picture's own
        // native reference is what keeps them alive once this wrapper is gone - measured, not
        // assumed: replaying the picture after its source was disposed drew 0 differing pixels.
        using var image = SKImage.FromBitmap(bitmap);
        return Record(image);
    }

    private static SKPicture Record(SKImage source)
    {
        using var recorder = new SKPictureRecorder();
        var bounds = new SKRect(0f, 0f, source.Width, source.Height);
        var canvas = recorder.BeginRecording(bounds);

        using var paint = new SKPaint
        {
            IsAntialias = true,
        };

        canvas.DrawImage(source, 0f, 0f, Sampling, paint);
        return recorder.EndRecording();
    }

    private static bool StartsWith(ReadOnlySpan<byte> header, ReadOnlySpan<byte> magic) =>
        header.Length >= magic.Length && header[..magic.Length].SequenceEqual(magic);
}
