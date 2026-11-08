using SkiaSharp;

namespace XPlatUI;

/// <summary>
/// Decodes one image format that <see cref="XPlatImage"/> does not know about by itself.
/// </summary>
/// <remarks>
/// <para>
/// The control's built-in decoder handles the raster formats a desktop toolkit is expected to read
/// - PNG, JPEG, WebP, GIF, BMP and ICO - with nothing installed. Everything else, and any format an
/// application wants to handle differently, arrives through this interface: a provider is a small
/// public seam rather than code in the control, so a format lives in a package of its own and an
/// application that never registers one ships none of it.
/// </para>
/// <para>
/// A provider produces an <see cref="SKPicture"/> rather than a bitmap, because that is what the
/// control draws. A raster provider records the decoded image into a picture; a vector provider
/// hands over the picture its renderer produced. Nothing about the picture is re-owned by the
/// control unless <see cref="XPlatImage.DisposePictureWithControl"/> says so, so a provider is free
/// to cache and share pictures, and a cache entry outlives the provider that made it.
/// </para>
/// <para>
/// Registration is global to the process rather than to a control, because the bytes on the stream
/// are the only thing that decides. See <see cref="XPlatImageFormats"/> for the conflict and failure
/// rules.
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
    /// The built-in raster decoder is 0, so any registered provider outranks it for bytes they both
    /// recognise. Two providers at equal priority are disambiguated by registration order, the later
    /// one winning, so an application can replace a provider by registering another at the same
    /// priority.
    /// </remarks>
    int Priority { get; }

    /// <summary>
    /// Answers whether this provider recognises the start of a stream.
    /// </summary>
    /// <param name="header">
    /// The first bytes of the stream, up to 512 of them, from the beginning. It is a prefix of the
    /// stream the provider is given back if it is asked to decode, never more, and it is short
    /// enough to read from a file without a pass over the whole thing. A provider whose format
    /// needs a longer look should recognise the container here and finish deciding inside
    /// <see cref="Decode"/>.
    /// </param>
    /// <returns>True when the bytes are this format's.</returns>
    /// <remarks>
    /// A provider that throws here has not claimed the bytes: the exception is swallowed and the
    /// next provider is asked, because identifying a stream must not be able to fail a load.
    /// </remarks>
    bool CanDecode(ReadOnlySpan<byte> header);

    /// <summary>
    /// Decodes a stream this provider recognised into the picture the control will draw.
    /// </summary>
    /// <param name="stream">
    /// The stream, positioned at the beginning and guaranteed seekable. It is read during the call
    /// and not retained.
    /// </param>
    /// <param name="extensionHint">
    /// The file extension of the path the control was asked to load, without the dot and in lower
    /// case, or null when the control was handed a stream and has no path. It is a hint only: a
    /// provider must still work from the bytes, because a stream has no name.
    /// </param>
    /// <returns>
    /// The picture, which the provider may own or hand over; or null when the bytes were
    /// recognised but could not be decoded.
    /// </returns>
    SKPicture? Decode(Stream stream, string? extensionHint);
}
