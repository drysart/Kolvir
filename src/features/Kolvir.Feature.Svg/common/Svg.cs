namespace Kolvir.Svg;

/// <summary>
/// The SVG feature's cache controls.
/// </summary>
/// <remarks>
/// <para>
/// There is no registration call here: an application turns SVG on with
/// <see cref="SvgServiceCollectionExtensions.AddSvgImages"/>, while it declares its services, and
/// the factory it resolves consults this package's provider. That is the whole opt-in. There is
/// deliberately no SVG-specific loader either - <c>factory.LoadFromStream(stream)</c> reads an SVG
/// once the provider is in the container and a PNG whether it is or not, which is the point of
/// "load this image" not depending on which formats happen to be enabled.
/// </para>
/// <para>
/// What is left here is the parse cache, which is a property of this package rather than of loading
/// an image: a theme editor that knows its icon set changed, and a test that counts parses, are the
/// two things that need it.
/// </para>
/// </remarks>
/// <example>
/// <code>
/// // in an application's ConfigureServices:
/// services.AddSvgImages();
///
/// // anywhere afterwards, an SVG is an image like any other:
/// image.Source = factory.LoadFromPath("icons/star.svg");
/// </code>
/// </example>
public static class Svg
{
    /// <summary>
    /// Empties the parse cache. Documents that are already rendered keep drawing.
    /// </summary>
    /// <remarks>
    /// Nothing calls this in a normal application: the cache is bounded, so it cannot grow without
    /// limit. It exists for a program that knows its icon set changed - a theme editor, say - and for
    /// tests that measure how often a document is parsed.
    /// </remarks>
    public static void ClearCache() => SvgCache.Clear();
}
