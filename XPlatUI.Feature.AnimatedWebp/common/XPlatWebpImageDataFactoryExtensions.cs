namespace XPlatUI;

/// <summary>
/// The format-specific spelling of loading an animated WebP, through the same
/// <see cref="IXPlatImageDataFactory"/> every other image goes through.
/// </summary>
/// <remarks>
/// <para>
/// There is deliberately no WebP loader of its own. What is specific to WebP is
/// <see cref="XPlatWebpOptions"/>, and these overloads are how that type is discoverable:
/// <c>factory.LoadFromPath(path, new XPlatWebpOptions { DecodedSizeLimit = ... })</c>.
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
public static class XPlatWebpImageDataFactoryExtensions
{
    /// <summary>Reads an animated WebP from a file with the given options.</summary>
    /// <param name="factory">The factory to load through.</param>
    /// <param name="path">The file to read.</param>
    /// <param name="options">The decoder's settings.</param>
    /// <returns>The data, which owns every decoded frame.</returns>
    /// <exception cref="ArgumentNullException">The factory, the path or the options are null.</exception>
    /// <exception cref="XPlatImageFormatException">
    /// The bytes are not a WebP and no other provider claims them, or the animation is over the
    /// decoded-size limit.
    /// </exception>
    /// <exception cref="IOException">The file could not be read.</exception>
    public static XPlatImageData LoadFromPath(this IXPlatImageDataFactory factory, string path, XPlatWebpOptions options)
    {
        ArgumentNullException.ThrowIfNull(factory);
        ArgumentNullException.ThrowIfNull(options);

        return factory.LoadFromPath(path, (IXPlatImageFormatOptions)options);
    }

    /// <summary>Reads an animated WebP from a stream with the given options.</summary>
    /// <param name="factory">The factory to load through.</param>
    /// <param name="stream">The WebP to read; it is read to the end and not retained.</param>
    /// <param name="options">The decoder's settings.</param>
    /// <returns>The data, which owns every decoded frame.</returns>
    /// <exception cref="ArgumentNullException">The factory, the stream or the options are null.</exception>
    public static XPlatImageData LoadFromStream(this IXPlatImageDataFactory factory, Stream stream, XPlatWebpOptions options)
    {
        ArgumentNullException.ThrowIfNull(factory);
        ArgumentNullException.ThrowIfNull(stream);
        ArgumentNullException.ThrowIfNull(options);

        return factory.LoadFromStream(stream, (IXPlatImageFormatOptions)options);
    }

    /// <summary>Reads an animated WebP from bytes with the given options.</summary>
    /// <param name="factory">The factory to load through.</param>
    /// <param name="bytes">The WebP.</param>
    /// <param name="options">The decoder's settings.</param>
    /// <returns>The data, which owns every decoded frame.</returns>
    /// <exception cref="ArgumentNullException">The factory or the options are null.</exception>
    public static XPlatImageData LoadFromBytes(this IXPlatImageDataFactory factory, ReadOnlySpan<byte> bytes, XPlatWebpOptions options)
    {
        ArgumentNullException.ThrowIfNull(factory);
        ArgumentNullException.ThrowIfNull(options);

        return factory.LoadFromBytes(bytes, (IXPlatImageFormatOptions)options);
    }
}
