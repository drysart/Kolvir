using SkiaSharp;

namespace Kolvir;

/// <summary>
/// The <see cref="IImageFormatProvider"/> that turns an animated WebP into
/// <see cref="ImageData"/>.
/// </summary>
/// <remarks>
/// <para>
/// It claims every <c>RIFF....WEBP</c> stream, animated or not, so registering it -
/// <c>services.AddAnimatedWebpImages()</c> - is all it takes to take WebP away from the built-in
/// decoder. A file that is not animated is decoded as a still picture through the same recording
/// path the built-in decoder uses, so enabling the feature never makes a static WebP worse.
/// </para>
/// <para>
/// Every frame is decoded and composited up front, because seeking to any position is part of what
/// the timeline promises. The cost is bounded by <see cref="WebpOptions.DecodedSizeLimit"/>.
/// </para>
/// </remarks>
public sealed class WebpImageFormatProvider : IImageFormatProvider
{
    /// <summary>The single instance, so registering it twice is registering it once.</summary>
    public static WebpImageFormatProvider Shared { get; } = new();

    /// <inheritdoc/>
    public string Name => "webp";

    /// <summary>
    /// Above anything else registered for WebP, and only because it names a format: two providers
    /// that both claim a WebP are separated by priority, then by the later registration winning.
    /// </summary>
    public int Priority => 50;

    /// <inheritdoc/>
    public bool CanDecode(ReadOnlySpan<byte> header) =>
        header.Length >= 12
        && header[..4].SequenceEqual("RIFF"u8)
        && header[8..12].SequenceEqual("WEBP"u8);

    /// <summary>Decodes every frame, composites them, and returns the animation or the still.</summary>
    /// <param name="stream">The WebP, positioned at the beginning and seekable.</param>
    /// <param name="extensionHint">Unused: the magic bytes already decided.</param>
    /// <param name="options">
    /// An <see cref="WebpOptions"/>, or null for the defaults. Any other type is a failure rather
    /// than a setting this provider silently ignores.
    /// </param>
    /// <exception cref="ArgumentException"><paramref name="options"/> is not a <see cref="WebpOptions"/>.</exception>
    public ImageData Create(Stream stream, string? extensionHint, IImageFormatOptions? options)
    {
        ArgumentNullException.ThrowIfNull(stream);

        var webp = options switch
        {
            null => new WebpOptions(),
            WebpOptions typed => typed,
            _ => throw new ArgumentException(
                $"The WebP provider does not understand {options.GetType().Name}.",
                nameof(options)),
        };

        return new ImageData(WebpFrameSource.Decode(stream, webp));
    }
}
