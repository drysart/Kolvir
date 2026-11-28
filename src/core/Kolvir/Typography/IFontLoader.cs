namespace Kolvir;

/// <summary>
/// The one-stop shop for turning a path, a stream or bytes into a <see cref="FontFace"/>: whichever
/// registered <see cref="IFontFormatProvider"/> claims the data makes them.
/// </summary>
/// <remarks>
/// <para>
/// <b>The developer story is one call, always.</b> An application does not decide which loader to use
/// by format: it enables the formats it wants - <c>WoffFonts.AddWoffFonts()</c>, for instance - and
/// then hands the bytes to the same three methods whatever they are. A <c>.ttf</c>, a <c>.woff</c> and
/// a <c>.woff2</c> all come back from <see cref="LoadFromBytes"/> with no branching at the call site,
/// which is why no feature package offers a creation API of its own.
/// </para>
/// <para>
/// <b>A load produces a face, not a font.</b> A file holds one physical face and no size, so what
/// comes back is a <see cref="FontFace"/>: the OpenType bytes, the family name recorded inside them
/// and the style they carry. The caller gives it a name with <see cref="IFontRegistry"/> and builds a
/// <see cref="Font"/> at whatever size it needs, which is the same path a font the operating system
/// provides takes.
/// </para>
/// <para>
/// <b>The built-in loader is whatever Skia reads</b>, which today is TrueType and OpenType: a
/// <c>.ttf</c> or a <c>.otf</c>. Anything else needs a provider, and the failure says so, so "my
/// <c>.woff</c> does not load" has one answer - register the format - rather than a mystery.
/// </para>
/// <para>
/// An application without a container composes one by hand: <c>new FontLoader(providers)</c>, or the
/// parameterless form for the built-in loader alone. There is no ambient instance, because a static
/// that a container also writes into is the mutable registry this design removed. The
/// <see cref="Application"/> registers the one its services describe, which is what an application
/// resolves.
/// </para>
/// <para>
/// <b>Failure is an exception</b>, because the return type is not nullable and because a load with a
/// caller is the one place where an exception can be handled properly: the developer called the
/// loader, so the developer catches it and decides what the UI shows. Nothing in a paint path calls a
/// loader. Environmental failures (a missing file, a denied path) are the file system's own
/// exceptions, because they are not format failures.
/// </para>
/// </remarks>
public interface IFontLoader
{
    /// <summary>Reads a font from a file, using its extension only as a hint.</summary>
    /// <param name="path">The file to read.</param>
    /// <returns>The face the file holds, with its OpenType bytes and the style recorded in them.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="path"/> is null.</exception>
    /// <exception cref="FontFormatException">
    /// No provider recognised the data and the built-in loader could not read it, or the provider that
    /// claimed it could not decode it.
    /// </exception>
    /// <exception cref="System.IO.IOException">The file could not be read.</exception>
    FontFace LoadFromPath(string path);

    /// <summary>Reads a font from a stream.</summary>
    /// <param name="stream">
    /// The stream to read. It is read to its end and not retained, so the face outlives it.
    /// </param>
    /// <returns>The face the data holds, with its OpenType bytes and the style recorded in them.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="stream"/> is null.</exception>
    /// <exception cref="FontFormatException">
    /// No provider recognised the data and the built-in loader could not read it, or the provider that
    /// claimed it could not decode it.
    /// </exception>
    FontFace LoadFromStream(Stream stream);

    /// <summary>Reads a font from bytes already in memory, such as an embedded resource.</summary>
    /// <param name="data">The encoded font.</param>
    /// <returns>The face the data holds, with its OpenType bytes and the style recorded in them.</returns>
    /// <exception cref="FontFormatException">
    /// No provider recognised the data and the built-in loader could not read it, or the provider that
    /// claimed it could not decode it.
    /// </exception>
    FontFace LoadFromBytes(ReadOnlySpan<byte> data);
}
