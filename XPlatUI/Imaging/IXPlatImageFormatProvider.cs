using SkiaSharp;

namespace XPlatUI;

/// <summary>
/// Turns encoded image bytes into <see cref="XPlatImageData"/>: the seam a format package plugs into
/// <see cref="XPlatImageDataFactory"/> with.
/// </summary>
/// <remarks>
/// <para>
/// A provider is asked two questions in order. <see cref="CanDecode"/> is a cheap look at the start
/// of the bytes - "is this mine?" - and <see cref="Create"/> is the decode, called only for the one
/// provider that won. That split is what keeps a format in a package of its own, and an application
/// that never registers one free of it.
/// </para>
/// <para>
/// <b>Claiming is a commitment.</b> Once <see cref="CanDecode"/> says yes, a failure in
/// <see cref="Create"/> is that provider's failure and the load ends there: it does not fall through
/// to another parser, because handing a document one parser refused to a different one is how a
/// refused file gets read anyway. Declining is free - the next provider is asked.
/// </para>
/// <para>
/// <see cref="CanDecode"/> must not throw its way out of the search. A provider that throws there has
/// answered "not mine": the exception is swallowed and the search continues, because identifying a
/// stream must not be able to fail a load. A bug there costs that provider the format, not the
/// application.
/// </para>
/// <para>
/// Registration belongs to an <see cref="IXPlatImageDataFactory"/> rather than to the process: a
/// provider is declared when the application's services are declared, and the factory that is
/// composed from them consults the fixed list for its lifetime. Two factories in one process - and
/// two tests in one process - never see each other's providers. <see cref="XPlatImageDataFactory"/>
/// owns the ordering and the failure rules; see its remarks for all of them.
/// </para>
/// </remarks>
public interface IXPlatImageFormatProvider
{
    /// <summary>
    /// A short name for the format, used in diagnostics and in the failure a load reports.
    /// </summary>
    string Name { get; }

    /// <summary>
    /// How strongly this provider claims bytes that several providers recognise. Higher wins.
    /// </summary>
    /// <remarks>
    /// Higher wins. The built-in raster decoder is fallback behaviour rather than an entry in this list,
    /// so any registered provider outranks it for bytes they both recognise; <see cref="Priority"/> only
    /// arbitrates between two providers. Two at the same priority are disambiguated by registration
    /// order, the later one winning, so an application can replace a provider by registering another at
    /// the same priority.
    /// </remarks>
    int Priority { get; }

    /// <summary>
    /// Answers whether this provider recognises the start of a stream.
    /// </summary>
    /// <param name="header">
    /// The first bytes of the stream, up to 512 of them, from the beginning. It is a prefix of the
    /// stream the provider is given back if it is asked to decode, never more, and it is short enough
    /// to read from a file without a pass over the whole thing. A provider whose format needs a
    /// longer look should recognise the container here and finish deciding inside
    /// <see cref="Decode"/>.
    /// </param>
    /// <returns>True when the bytes are this format's.</returns>
    bool CanDecode(ReadOnlySpan<byte> header);

    /// <summary>
    /// Decodes bytes this provider claimed into the data a control will play.
    /// </summary>
    /// <param name="stream">
    /// The stream, positioned at the beginning and guaranteed seekable. It is read during the call
    /// and not retained: the data has to own fully decoded pixels by the time this returns.
    /// </param>
    /// <param name="extensionHint">
    /// The file extension of the path the load came from, without the dot and in lower case, or null
    /// when there was no path. It is a hint only: a provider must still work from the bytes, because
    /// a stream has no name.
    /// </param>
    /// <param name="options">
    /// What the caller configured, or null for the defaults. Only the provider that claimed the
    /// bytes sees them; one that declares a format and then receives options of another format must
    /// fail rather than ignore them, because an ignored setting is indistinguishable from a broken
    /// one.
    /// </param>
    /// <returns>
    /// The data, whose frames belong to it and are released when it is disposed. Returning null is a
    /// failure, exactly as throwing is.
    /// </returns>
    /// <exception cref="ArgumentException">
    /// <paramref name="options"/> is not a type this provider understands.
    /// </exception>
    XPlatImageData Create(Stream stream, string? extensionHint, IXPlatImageFormatOptions? options);
}
