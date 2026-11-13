namespace Kolvir;

/// <summary>
/// Turns font data in some other container into the bytes <see cref="Font"/> reads: the seam a format
/// package plugs into <see cref="FontLoader"/> with.
/// </summary>
/// <remarks>
/// <para>
/// A provider is asked two questions in order. <see cref="CanLoad"/> is a cheap look at the start of
/// the data - "is this mine?" - and <see cref="Decode"/> is the conversion, called only for the one
/// provider that won. That split is what keeps a format in a package of its own, and an application
/// that never registers one free of it.
/// </para>
/// <para>
/// <b>A provider unwraps a container; it does not draw and it does not parse outlines.</b> Skia is the
/// only thing that reads glyphs in this library, and it reads OpenType - a <c>.ttf</c> or <c>.otf</c>.
/// So the whole job of a provider here is "these bytes are the same font in a wrapper Skia does not
/// know, and here is the font without the wrapper". A format whose payload is not OpenType cannot be
/// supported through this seam, which is deliberate: the alternative is a second font engine.
/// </para>
/// <para>
/// <b>Claiming is a commitment.</b> Once <see cref="CanLoad"/> says yes, a failure in
/// <see cref="Decode"/> is that provider's failure and the load ends there: it does not fall through
/// to another provider or to the built-in loader. Declining is free - the next provider is asked.
/// </para>
/// <para>
/// <see cref="CanLoad"/> must not throw its way out of the search. A provider that throws there has
/// answered "not mine": the exception is swallowed and the search continues, because identifying a
/// stream must not be able to fail a load.
/// </para>
/// <para>
/// Registration belongs to a <see cref="FontLoader"/> rather than to the process, exactly as
/// <see cref="IImageFormatProvider"/> registration belongs to an <see cref="IImageDataFactory"/>: a
/// provider is declared while the application's services are declared, and the loader composed from
/// them consults the fixed list for its lifetime. Two loaders in one process - and two tests in one
/// process - never see each other's providers.
/// </para>
/// </remarks>
public interface IFontFormatProvider
{
    /// <summary>
    /// A short name for the format, used in diagnostics and in the failure a load reports.
    /// </summary>
    string Name { get; }

    /// <summary>
    /// How strongly this provider claims data that several providers recognise. Higher wins.
    /// </summary>
    /// <remarks>
    /// Higher wins. The built-in loader is fallback behaviour rather than an entry in this list, so
    /// any registered provider outranks it for data they both recognise; <see cref="Priority"/> only
    /// arbitrates between two providers. Two at the same priority are disambiguated by registration
    /// order, the later one winning, so an application can replace a provider by registering another
    /// at the same priority.
    /// </remarks>
    int Priority { get; }

    /// <summary>
    /// Answers whether this provider recognises the start of the data.
    /// </summary>
    /// <param name="header">
    /// The first bytes of the data, up to 512 of them, from the beginning. It is a prefix of the data
    /// the provider is given back if it is asked to decode, never more. A provider whose container
    /// needs a longer look should recognise the magic number here and finish deciding inside
    /// <see cref="Decode"/>.
    /// </param>
    /// <returns>True when the data is this format's.</returns>
    bool CanLoad(ReadOnlySpan<byte> header);

    /// <summary>
    /// Converts data this provider claimed into the OpenType bytes the built-in loader reads.
    /// </summary>
    /// <param name="data">
    /// The whole input, as it was handed to the loader. It is not retained: the result has to stand on
    /// its own.
    /// </param>
    /// <returns>
    /// A complete OpenType font - a <c>.ttf</c> or <c>.otf</c> - which the loader then opens the usual
    /// way. Returning null or an empty buffer is a failure, exactly as throwing is. A buffer that is
    /// not a font is also a failure, and is reported against this provider rather than as "no font
    /// here".
    /// </returns>
    /// <exception cref="System.IO.InvalidDataException">
    /// The data is damaged: truncated, a table that does not decompress, or a structure whose
    /// declared lengths do not add up. This is the exception to throw for a file that is the right
    /// format but unusable, and the loader passes its message on.
    /// </exception>
    ReadOnlyMemory<byte> Decode(ReadOnlyMemory<byte> data);
}
