namespace Kolvir.WoffFonts;

/// <summary>
/// The font format provider for WOFF and WOFF 2: the two web containers for the same OpenType font.
/// </summary>
/// <remarks>
/// <para>
/// Register it - <c>WoffFonts.AddWoffFonts()</c> - and every load through
/// <see cref="IFontLoader"/> reads both formats as well as the plain files the library already reads.
/// A <c>.ttf</c>, a <c>.woff</c> and a <c>.woff2</c> then arrive at the same call and the application
/// does not branch on which one it has.
/// </para>
/// <para>
/// One provider covers both versions rather than two, because they are the same promise to the
/// caller - "this is a web font, here is the OpenType in it" - and because a file that is one version
/// and looks like the other is a damaged file rather than a different format. The signature decides
/// which decoder runs, and the name in a failure says which it was.
/// </para>
/// <para>
/// WOFF 1.0 is a container only, so unwrapping it needs nothing but zlib. WOFF 2 re-encodes the glyph
/// outlines and rebuilds <c>loca</c> from them - see <see cref="Woff2Glyf"/> - and both end up in
/// <see cref="SfntBuilder"/>, which writes the OpenType file Skia reads.
/// </para>
/// </remarks>
public sealed class WoffFontFormatProvider : IFontFormatProvider
{
    /// <summary>
    /// The instance the registration uses. A provider is stateless - it reads its input and returns
    /// bytes - so one instance serves every loader.
    /// </summary>
    public static WoffFontFormatProvider Shared { get; } = new();

    private const string WoffName = "WOFF";
    private const string Woff2Name = "WOFF 2";

    /// <inheritdoc />
    public string Name => WoffName;

    /// <summary>
    /// Zero: nothing else in the library claims these signatures, so the number only has to lose to a
    /// provider an application registers to override this one.
    /// </summary>
    public int Priority => 0;

    /// <inheritdoc />
    public bool CanLoad(ReadOnlySpan<byte> header) =>
        header.StartsWith(WoffFormat.Signature) || header.StartsWith(Woff2Format.Signature);

    /// <inheritdoc />
    public ReadOnlyMemory<byte> Decode(ReadOnlyMemory<byte> data)
    {
        var span = data.Span;

        if (span.StartsWith(Woff2Format.Signature))
        {
            return Woff2Format.Unwrap(span);
        }

        if (span.StartsWith(WoffFormat.Signature))
        {
            return WoffFormat.Unwrap(span);
        }

        throw new InvalidDataException("The data is neither WOFF nor WOFF 2.");
    }
}
