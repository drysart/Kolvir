using SkiaSharp;

namespace Kolvir.Svg;

/// <summary>
/// The <see cref="IImageFormatProvider"/> that teaches <see cref="ImageDataFactory"/> to
/// read SVG.
/// </summary>
/// <remarks>
/// <para>
/// Register it once - <c>services.AddSvgImages()</c> - and every subsequent load through the
/// application's <see cref="IImageDataFactory"/> reads SVG with no change to the call site: the
/// bytes decide, and the data reaches the control exactly as a decoded bitmap does. The provider is
/// opt-in because it is a package: an application that does not reference this assembly has none of
/// it, and one that references it but never registers it has the assembly and none of its work.
/// </para>
/// <para>
/// Its priority is above the built-in raster decoder's, and it claims only documents that are
/// SVG, so the two never contend over the same bytes in practice. A bitmap is never routed here and
/// an SVG is never handed to the raster decoder.
/// </para>
/// <para>
/// The document is a still image: the data it produces has a duration of zero, which is what an SVG
/// is to a timeline. An animated SVG would be a source of its own, added here without the control
/// knowing about it.
/// </para>
/// </remarks>
public sealed class SvgImageFormatProvider : IImageFormatProvider
{
    /// <summary>The single instance, so registering twice is registering once.</summary>
    public static SvgImageFormatProvider Shared { get; } = new();

    /// <inheritdoc/>
    public string Name => "svg";

    /// <inheritdoc/>
    public int Priority => 100;

    /// <inheritdoc/>
    public bool CanDecode(ReadOnlySpan<byte> header) => SvgDocument.LooksLikeSvg(header);

    /// <summary>
    /// Parses the document, or serves it from the cache, and returns data that owns its recording.
    /// </summary>
    /// <remarks>
    /// <c>currentColor</c> comes from <see cref="SvgOptions"/> when the caller passed one and is
    /// SVG's own default - black - otherwise, because a provider has no other way to be told a
    /// colour. <see cref="SvgImageDataFactoryExtensions"/> is the discoverable spelling of that
    /// call.
    /// </remarks>
    public ImageData Create(Stream stream, string? extensionHint, IImageFormatOptions? options)
    {
        ArgumentNullException.ThrowIfNull(stream);

        var svg = options switch
        {
            null => null,
            SvgOptions typed => typed,
            _ => throw new ArgumentException(
                $"The SVG provider does not understand {options.GetType().Name}.",
                nameof(options)),
        };

        using var buffer = new MemoryStream();
        stream.CopyTo(buffer);

        var picture = SvgCache.Get(buffer.GetBuffer().AsSpan(0, (int)buffer.Length), svg?.CurrentColor).Record();
        return ImageData.FromPicture(picture, disposePictureWithData: true);
    }
}
