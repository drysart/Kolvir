namespace XPlatUI.Svg;

/// <summary>
/// The SVG feature's opt-in for callers that do not use a service container.
/// </summary>
/// <remarks>
/// <para>
/// This is the whole of what an application has to do: one call, and <see cref="XPlatImage"/> reads
/// SVG. It is the same call <c>services.AddSvgImages()</c> makes, because an image format is process
/// state rather than a service a window resolves - the bytes on the stream are the only thing that
/// decides which provider runs.
/// </para>
/// <para>
/// The handle is what lets a test or a scope undo the call. Disposing it does not touch a picture
/// that has already been loaded: a picture is self-contained, and removing a provider only changes
/// what future loads can do.
/// </para>
/// </remarks>
/// <example>
/// <code>
/// using var svg = XPlatSvg.AddSvgImages();
/// image.Load("icons/star.svg");
/// </code>
/// </example>
public static class XPlatSvg
{
    /// <summary>Registers the SVG provider. Calling it twice registers it once.</summary>
    /// <returns>A handle that unregisters it when disposed.</returns>
    public static IDisposable AddSvgImages() => XPlatImageFormats.Add(XPlatSvgImageFormatProvider.Shared);

    /// <summary>
    /// Empties the parse cache. Controls that are already showing a document keep drawing it.
    /// </summary>
    /// <remarks>
    /// Nothing calls this in a normal application: the cache is bounded, so it cannot grow without
    /// limit. It exists for a program that knows its icon set changed - a theme editor, say - and for
    /// tests that measure how often a document is parsed.
    /// </remarks>
    public static void ClearCache() => XPlatSvgCache.Clear();
}
