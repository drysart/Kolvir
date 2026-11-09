namespace XPlatUI.Svg;

/// <summary>
/// The format-specific spelling of loading an SVG through <see cref="IXPlatImageDataFactory"/>.
/// </summary>
/// <remarks>
/// <para>
/// There is deliberately no SVG loader of its own: an SVG is an image like any other, and it goes
/// through the same factory, with the same provider ordering and the same failure rules. What is
/// specific to SVG is <see cref="XPlatSvgOptions"/>, and these overloads are how that type is
/// discoverable - <c>factory.LoadFromPath(path, new XPlatSvgOptions { CurrentColor = ... })</c>.
/// </para>
/// <para>
/// They are thin on purpose: they pass the options to the factory and do nothing else. Anything that
/// validated or parsed here would be a second implementation of the rules the factory already owns.
/// </para>
/// <para>
/// C# prefers an applicable instance method to an extension method, so the plain
/// <c>factory.LoadFromPath(path, options)</c> spelling binds to the factory's own overload and does
/// exactly what these do. That is the intended shape: one pipeline, two spellings.
/// </para>
/// </remarks>
public static class XPlatSvgImageDataFactoryExtensions
{
    /// <summary>Reads an SVG document from a file with the given options.</summary>
    /// <param name="factory">The factory to load through.</param>
    /// <param name="path">The file to read.</param>
    /// <param name="options">What <c>currentColor</c> resolves to, and anything else SVG adds later.</param>
    /// <returns>The data, which owns what it decoded.</returns>
    /// <exception cref="ArgumentNullException">The factory, the path or the options are null.</exception>
    /// <exception cref="XPlatImageFormatException">
    /// The bytes are not an SVG and no other provider claims them, or the document could not be
    /// parsed.
    /// </exception>
    /// <exception cref="IOException">The file could not be read.</exception>
    public static XPlatImageData LoadFromPath(this IXPlatImageDataFactory factory, string path, XPlatSvgOptions options)
    {
        ArgumentNullException.ThrowIfNull(factory);
        ArgumentNullException.ThrowIfNull(options);

        return factory.LoadFromPath(path, (IXPlatImageFormatOptions)options);
    }

    /// <summary>Reads an SVG document from a stream with the given options.</summary>
    /// <param name="factory">The factory to load through.</param>
    /// <param name="stream">The document to read; it is read to the end and not retained.</param>
    /// <param name="options">What <c>currentColor</c> resolves to.</param>
    /// <returns>The data, which owns what it decoded.</returns>
    /// <exception cref="ArgumentNullException">The factory, the stream or the options are null.</exception>
    public static XPlatImageData LoadFromStream(this IXPlatImageDataFactory factory, Stream stream, XPlatSvgOptions options)
    {
        ArgumentNullException.ThrowIfNull(factory);
        ArgumentNullException.ThrowIfNull(options);

        return factory.LoadFromStream(stream, (IXPlatImageFormatOptions)options);
    }

    /// <summary>Reads an SVG document from bytes with the given options.</summary>
    /// <param name="factory">The factory to load through.</param>
    /// <param name="bytes">The document.</param>
    /// <param name="options">What <c>currentColor</c> resolves to.</param>
    /// <returns>The data, which owns what it decoded.</returns>
    /// <exception cref="ArgumentNullException">The factory or the options are null.</exception>
    public static XPlatImageData LoadFromBytes(this IXPlatImageDataFactory factory, ReadOnlySpan<byte> bytes, XPlatSvgOptions options)
    {
        ArgumentNullException.ThrowIfNull(factory);
        ArgumentNullException.ThrowIfNull(options);

        return factory.LoadFromBytes(bytes, (IXPlatImageFormatOptions)options);
    }
}
