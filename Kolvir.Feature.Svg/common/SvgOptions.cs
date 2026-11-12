using SkiaSharp;

namespace Kolvir.Svg;

/// <summary>
/// What an SVG load can be told, for the one thing SVG needs that a plain image does not:
/// what <c>currentColor</c> resolves to.
/// </summary>
/// <remarks>
/// <para>
/// <c>currentColor</c> is resolved while the document is parsed, so it is a load-time setting rather
/// than something a loaded document can change afterwards. It also keys the parse cache: the same
/// bytes at a different colour is a different parse, which is why this is an option and not a
/// property on the data.
/// </para>
/// <para>
/// The typed spelling is
/// <see cref="SvgImageDataFactoryExtensions.LoadFromBytes(IImageDataFactory, ReadOnlySpan{byte}, SvgOptions)"/>
/// and its path and stream siblings, so the format-specific setting is discoverable on the same
/// factory every other image goes through.
/// </para>
/// </remarks>
public sealed class SvgOptions : IImageFormatOptions
{
    /// <summary>
    /// What <c>currentColor</c> resolves to, or null (the default) for SVG's own default of black.
    /// </summary>
    /// <remarks>
    /// Elements that name a colour of their own keep it; only <c>currentColor</c> follows this.
    /// </remarks>
    public SKColor? CurrentColor { get; set; }
}
