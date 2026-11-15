using SkiaSharp;

namespace Kolvir;

/// <summary>
/// The recording the control has always done for a raster image: decode the pixels, then record one
/// draw of them into a picture at identity.
/// </summary>
/// <remarks>
/// <para>
/// It lives here rather than in the provider because two entry points need exactly this recording -
/// the built-in <see cref="IImageFormatProvider"/> and the <see cref="ImageData"/>
/// factories - and a second copy of the sampling options is how a "still" image starts to look
/// different depending on which door it came through.
/// </para>
/// <para>
/// A picture recorded from a decoded bitmap shares that bitmap's pixels, and the picture's own
/// native reference is what keeps them alive once the wrapper is gone - measured, not assumed:
/// replaying the picture after its source was disposed drew 0 differing pixels. That is why nothing
/// has to hold the bitmap: the picture is the whole of what the caller has to keep.
/// </para>
/// </remarks>
internal static class RasterPicture
{
    // The sampling a bitmap has always been drawn with. The same options are recorded into the
    // picture, so replaying it under the destination's scale samples exactly as a direct draw does.
    private static readonly SKSamplingOptions Sampling = new(SKFilterMode.Linear, SKMipmapMode.Linear);

    /// <summary>Decodes encoded image bytes into a picture, or null when they are not an image.</summary>
    internal static SKPicture? Decode(Stream stream)
    {
        using var bitmap = SKBitmap.Decode(stream);
        return bitmap is null ? null : Record(bitmap);
    }

    /// <summary>Decodes encoded image bytes into a picture, or null when they are not an image.</summary>
    internal static SKPicture? Decode(ReadOnlySpan<byte> bytes)
    {
        using var bitmap = SKBitmap.Decode(bytes);
        return bitmap is null ? null : Record(bitmap);
    }

    private static SKPicture Record(SKBitmap bitmap)
    {
        using var image = SKImage.FromBitmap(bitmap);
        using var recorder = new SKPictureRecorder();
        var bounds = new SKRect(0f, 0f, image.Width, image.Height);
        var canvas = recorder.BeginRecording(bounds);

        using var paint = new SKPaint
        {
            IsAntialias = true,
        };

        canvas.DrawImage(image, 0f, 0f, Sampling, paint);
        return recorder.EndRecording();
    }
}
