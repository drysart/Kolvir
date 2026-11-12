using SkiaSharp;

namespace Kolvir;

/// <summary>
/// The <see cref="IImageFormatProvider"/> that turns an animated GIF into
/// <see cref="ImageData"/>.
/// </summary>
/// <remarks>
/// <para>
/// It claims GIF87a and GIF89a bytes, so registering it - <c>services.AddAnimatedGifImages()</c> -
/// is all it takes to take GIF away from the built-in decoder, which would otherwise read the still
/// first frame. Without it, a GIF is still an image: the built-in decoder reads frame one and the
/// control shows a picture, which is the documented fallback rather than an error.
/// </para>
/// <para>
/// Every frame is decoded and composited up front, because seeking to any position is part of what
/// the timeline promises. The cost is bounded by <see cref="GifOptions.DecodedSizeLimit"/>.
/// </para>
/// </remarks>
public sealed class GifImageFormatProvider : IImageFormatProvider
{
    /// <summary>The single instance, so registering it twice is registering it once.</summary>
    public static GifImageFormatProvider Shared { get; } = new();

    /// <inheritdoc/>
    public string Name => "gif";

    /// <summary>
    /// Above anything else registered for GIF, and only because it names a format: two providers
    /// that both claim a GIF are separated by priority, then by the later registration winning.
    /// </summary>
    public int Priority => 50;

    /// <inheritdoc/>
    public bool CanDecode(ReadOnlySpan<byte> header) =>
        header.Length >= 6
        && (header[..6].SequenceEqual("GIF89a"u8) || header[..6].SequenceEqual("GIF87a"u8));

    /// <summary>Decodes every frame, composites them, and returns the animation.</summary>
    /// <param name="stream">The GIF, positioned at the beginning and seekable.</param>
    /// <param name="extensionHint">Unused: the magic bytes already decided.</param>
    /// <param name="options">
    /// An <see cref="GifOptions"/>, or null for the defaults. Any other type is a failure rather
    /// than a setting this provider silently ignores.
    /// </param>
    /// <exception cref="ArgumentException"><paramref name="options"/> is not a <see cref="GifOptions"/>.</exception>
    public ImageData Create(Stream stream, string? extensionHint, IImageFormatOptions? options)
    {
        ArgumentNullException.ThrowIfNull(stream);

        var gif = options switch
        {
            null => new GifOptions(),
            GifOptions typed => typed,
            _ => throw new ArgumentException(
                $"The GIF provider does not understand {options.GetType().Name}.",
                nameof(options)),
        };

        return new ImageData(GifFrameSource.Decode(stream, gif));
    }
}
