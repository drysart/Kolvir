using SkiaSharp;

namespace XPlatUI;

/// <summary>
/// The one-stop shop for turning a path, a stream or bytes into <see cref="XPlatImageData"/>:
/// whichever registered <see cref="IXPlatImageFormatProvider"/> claims the bytes makes them.
/// </summary>
/// <remarks>
/// <para>
/// <b>The developer story is one call, always.</b> An application does not decide which loader to
/// use by format: it enables the formats it wants - <c>XPlatSvg.AddSvgImages()</c>, for instance -
/// and then hands the bytes to the same three methods whatever they are. A PNG, an SVG and an
/// animated GIF all come back from <see cref="LoadFromStream"/> with no branching at the call site,
/// which is why no feature package offers a creation API of its own.
/// </para>
/// <para>
/// An application without a container composes one by hand: <c>new XPlatImageDataFactory(providers)</c>,
/// or the parameterless form for the built-in decoder alone. There is no ambient instance, because a
/// static that a container also writes into is the mutable registry this design removed.
/// </para>
/// <para>
/// <b>Failure is an exception</b>, because the return type is not nullable and because a load with a
/// caller is the one place where an exception can be handled properly: the developer called the
/// factory, so the developer catches it and decides what the UI shows. Nothing in a paint path calls
/// a factory. Environmental failures (a missing file, a denied path) are the file system's own
/// exceptions, because they are not format failures.
/// </para>
/// </remarks>
public interface IXPlatImageDataFactory
{
    /// <summary>Reads an image from a file, using its extension only as a hint.</summary>
    /// <param name="path">The file to read.</param>
    /// <param name="options">
    /// What to configure in the provider that claims the bytes, or null for the defaults.
    /// </param>
    /// <returns>The data, which owns what it decoded.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="path"/> is null.</exception>
    /// <exception cref="XPlatImageFormatException">
    /// No provider recognised the bytes, or the one that claimed them could not decode them.
    /// </exception>
    /// <exception cref="IOException">The file could not be read.</exception>
    XPlatImageData LoadFromPath(string path, IXPlatImageFormatOptions? options = null);

    /// <summary>Reads an image from a stream, which may be unseekable.</summary>
    /// <param name="stream">
    /// The stream to read. It is read to the end and not retained, so the data outlives it. A stream
    /// that cannot seek is buffered first, up to <see cref="XPlatImageDataFactory.BufferLimit"/>.
    /// </param>
    /// <param name="options">
    /// What to configure in the provider that claims the bytes, or null for the defaults.
    /// </param>
    /// <exception cref="ArgumentNullException"><paramref name="stream"/> is null.</exception>
    /// <exception cref="XPlatImageFormatException">
    /// No provider recognised the bytes, the one that claimed them could not decode them, or an
    /// unseekable stream was larger than the buffer limit.
    /// </exception>
    XPlatImageData LoadFromStream(Stream stream, IXPlatImageFormatOptions? options = null);

    /// <summary>Reads an image from bytes already in memory.</summary>
    /// <param name="bytes">The encoded image.</param>
    /// <param name="options">
    /// What to configure in the provider that claims the bytes, or null for the defaults.
    /// </param>
    /// <exception cref="XPlatImageFormatException">
    /// No provider recognised the bytes, or the one that claimed them could not decode them.
    /// </exception>
    XPlatImageData LoadFromBytes(ReadOnlySpan<byte> bytes, IXPlatImageFormatOptions? options = null);
}
