namespace XPlatUI;

/// <summary>
/// The format-specific spelling of loading an animated GIF, through the same
/// <see cref="IXPlatImageDataFactory"/> every other image goes through.
/// </summary>
/// <remarks>
/// <para>
/// There is deliberately no GIF loader of its own. What is specific to GIF is
/// <see cref="XPlatGifOptions"/>, and these overloads are how that type is discoverable:
/// <c>factory.LoadFromPath(path, new XPlatGifOptions { DecodedSizeLimit = ... })</c>.
/// </para>
/// <para>
/// They are thin on purpose: they hand the options to the factory and do nothing else, so there is
/// one implementation of the rules rather than two that can disagree.
/// </para>
/// <para>
/// C# prefers an applicable instance method to an extension method, so the plain
/// <c>factory.LoadFromPath(path, options)</c> spelling binds to the factory's own overload and does
/// exactly what these do. That is the intended shape: one pipeline, two spellings.
/// </para>
/// </remarks>
public static class XPlatGifImageDataFactoryExtensions
{
    /// <summary>Reads an animated GIF from a file with the given options.</summary>
    /// <param name="factory">The factory to load through.</param>
    /// <param name="path">The file to read.</param>
    /// <param name="options">The decoder's settings.</param>
    /// <returns>The data, which owns every decoded frame.</returns>
    /// <exception cref="ArgumentNullException">The factory, the path or the options are null.</exception>
    /// <exception cref="XPlatImageFormatException">
    /// The bytes are not a GIF and no other provider claims them, or the animation is over the
    /// decoded-size limit.
    /// </exception>
    /// <exception cref="IOException">The file could not be read.</exception>
    public static XPlatImageData LoadFromPath(this IXPlatImageDataFactory factory, string path, XPlatGifOptions options)
    {
        ArgumentNullException.ThrowIfNull(factory);
        ArgumentNullException.ThrowIfNull(options);

        return factory.LoadFromPath(path, (IXPlatImageFormatOptions)options);
    }

    /// <summary>Reads an animated GIF from a stream with the given options.</summary>
    /// <param name="factory">The factory to load through.</param>
    /// <param name="stream">The GIF to read; it is read to the end and not retained.</param>
    /// <param name="options">The decoder's settings.</param>
    /// <returns>The data, which owns every decoded frame.</returns>
    /// <exception cref="ArgumentNullException">The factory, the stream or the options are null.</exception>
    public static XPlatImageData LoadFromStream(this IXPlatImageDataFactory factory, Stream stream, XPlatGifOptions options)
    {
        ArgumentNullException.ThrowIfNull(factory);
        ArgumentNullException.ThrowIfNull(options);

        return factory.LoadFromStream(stream, (IXPlatImageFormatOptions)options);
    }

    /// <summary>Reads an animated GIF from bytes with the given options.</summary>
    /// <param name="factory">The factory to load through.</param>
    /// <param name="bytes">The GIF.</param>
    /// <param name="options">The decoder's settings.</param>
    /// <returns>The data, which owns every decoded frame.</returns>
    /// <exception cref="ArgumentNullException">The factory or the options are null.</exception>
    public static XPlatImageData LoadFromBytes(this IXPlatImageDataFactory factory, ReadOnlySpan<byte> bytes, XPlatGifOptions options)
    {
        ArgumentNullException.ThrowIfNull(factory);
        ArgumentNullException.ThrowIfNull(options);

        return factory.LoadFromBytes(bytes, (IXPlatImageFormatOptions)options);
    }
}
