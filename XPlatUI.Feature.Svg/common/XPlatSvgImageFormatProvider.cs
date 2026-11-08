using SkiaSharp;

namespace XPlatUI.Svg;

/// <summary>
/// The <see cref="IXPlatImageFormatProvider"/> that teaches <see cref="XPlatImage"/> to read SVG.
/// </summary>
/// <remarks>
/// <para>
/// Register it once and every subsequent <see cref="XPlatImage.Load(string)"/> and
/// <see cref="XPlatImage.Load(Stream)"/> reads SVG with no change to the call site: the bytes
/// decide, and the picture is handed to the control exactly as a decoded bitmap is. The provider is
/// opt-in because it is a package: an application that does not reference this assembly has none of
/// it, and one that references it but never registers it has the assembly and none of its work.
/// </para>
/// <para>
/// Its priority is above the built-in raster decoder's, and it claims only documents that are
/// SVG, so the two never contend over the same bytes in practice. A bitmap is never routed here and
/// an SVG is never handed to the raster decoder.
/// </para>
/// </remarks>
public sealed class XPlatSvgImageFormatProvider : IXPlatImageFormatProvider
{
    /// <summary>The single instance, so registering twice is registering once.</summary>
    internal static XPlatSvgImageFormatProvider Shared { get; } = new();

    /// <inheritdoc/>
    public string Name => "svg";

    /// <inheritdoc/>
    public int Priority => 100;

    /// <inheritdoc/>
    public bool CanDecode(ReadOnlySpan<byte> header) => XPlatSvgDocument.LooksLikeSvg(header);

    /// <summary>
    /// Parses the document, or serves it from the cache, and returns a recording the control owns.
    /// </summary>
    /// <remarks>
    /// <c>currentColor</c> is left at SVG's own default - black - because a provider has no way to
    /// be told a colour. An application that wants an icon to follow its palette loads it through
    /// <see cref="XPlatImageSvgExtensions.LoadSvg(XPlatImage, string, SKColor?)"/> instead, which
    /// keys the same cache on the colour.
    /// </remarks>
    public SKPicture? Decode(Stream stream, string? extensionHint)
    {
        ArgumentNullException.ThrowIfNull(stream);

        using var buffer = new MemoryStream();
        stream.CopyTo(buffer);

        return XPlatSvgCache.Get(buffer.GetBuffer().AsSpan(0, (int)buffer.Length), currentColor: null).Record();
    }
}
