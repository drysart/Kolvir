using SkiaSharp;

namespace XPlatUI.Svg;

/// <summary>
/// The <see cref="IXPlatImageFormatProvider"/> that teaches <see cref="XPlatImageDataFactory"/> to
/// read SVG.
/// </summary>
/// <remarks>
/// <para>
/// Register it once - <c>services.AddSvgImages()</c> - and every subsequent load through the
/// application's <see cref="IXPlatImageDataFactory"/> reads SVG with no change to the call site: the
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
public sealed class XPlatSvgImageFormatProvider : IXPlatImageFormatProvider
{
    /// <summary>The single instance, so registering twice is registering once.</summary>
    public static XPlatSvgImageFormatProvider Shared { get; } = new();

    /// <inheritdoc/>
    public string Name => "svg";

    /// <inheritdoc/>
    public int Priority => 100;

    /// <inheritdoc/>
    public bool CanDecode(ReadOnlySpan<byte> header) => XPlatSvgDocument.LooksLikeSvg(header);

    /// <summary>
    /// Parses the document, or serves it from the cache, and returns data that owns its recording.
    /// </summary>
    /// <remarks>
    /// <c>currentColor</c> comes from <see cref="XPlatSvgOptions"/> when the caller passed one and is
    /// SVG's own default - black - otherwise, because a provider has no other way to be told a
    /// colour. <see cref="XPlatSvgImageDataFactoryExtensions"/> is the discoverable spelling of that
    /// call.
    /// </remarks>
    public XPlatImageData Create(Stream stream, string? extensionHint, IXPlatImageFormatOptions? options)
    {
        ArgumentNullException.ThrowIfNull(stream);

        var svg = options switch
        {
            null => null,
            XPlatSvgOptions typed => typed,
            _ => throw new ArgumentException(
                $"The SVG provider does not understand {options.GetType().Name}.",
                nameof(options)),
        };

        using var buffer = new MemoryStream();
        stream.CopyTo(buffer);

        var picture = XPlatSvgCache.Get(buffer.GetBuffer().AsSpan(0, (int)buffer.Length), svg?.CurrentColor).Record();
        return XPlatImageData.FromPicture(picture, disposePictureWithData: true);
    }
}
