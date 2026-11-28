using SkiaSharp;

namespace Kolvir;

/// <summary>
/// One physical face read out of font data: its OpenType bytes, the family name recorded inside it,
/// its weight and whether it is slanted.
/// </summary>
/// <remarks>
/// <para>
/// A face is a value, not a font: it has no size, it retains no Skia object, and nothing about it is
/// disposable. That is what lets a <see cref="FontRegistry"/> hold one for the life of the process
/// while the fonts that name it come and go.
/// </para>
/// <para>
/// The style and the internal family name are read once, when the face is created, by parsing the
/// data. The parse is the one cost of registration; drawing with the face parses its bytes again per
/// realization, which is once per description in use rather than once per frame.
/// </para>
/// <para>
/// A face does not carry a size because the file does not contain one. A loader that returned a
/// <see cref="Font"/> therefore had to invent a size, which is why loading produces this type now:
/// the caller picks the size when it builds the description that names the registered family.
/// </para>
/// </remarks>
public sealed class FontFace
{
    /// <summary>Reads a face out of OpenType font data.</summary>
    /// <param name="data">The bytes of a <c>.ttf</c> or <c>.otf</c>, or of the OpenType payload a
    /// format provider unwrapped.</param>
    /// <exception cref="FontFormatException">
    /// <paramref name="data"/> is empty, or is not a font this build can read.
    /// </exception>
    /// <remarks>
    /// The bytes are kept as they were given. Nothing is parsed lazily: the family name and the style
    /// are read here, which is what makes a malformed face a failure at the call that supplied it
    /// rather than one at the first measurement.
    /// </remarks>
    public FontFace(ReadOnlyMemory<byte> data)
    {
        if (data.IsEmpty)
        {
            throw new FontFormatException(null, nameof(data));
        }

        SKTypeface? typeface;
        try
        {
            using var skData = SKData.CreateCopy(data.Span);
            typeface = SKTypeface.FromData(skData);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            // Skia owns the parsing, and data that is not a font is a caller mistake rather than a
            // crash: report it the way an unusable argument is reported.
            throw new FontFormatException(null, nameof(data), exception);
        }

        if (typeface is null || typeface == SKTypeface.Empty || string.IsNullOrEmpty(typeface.FamilyName))
        {
            typeface?.Dispose();
            throw new FontFormatException(null, nameof(data));
        }

        try
        {
            var style = typeface.FontStyle;
            FamilyName = typeface.FamilyName!;
            Weight = style.Weight;
            Italic = style.Slant != SKFontStyleSlant.Upright;
            Data = data;
        }
        finally
        {
            typeface.Dispose();
        }
    }

    /// <summary>The family name recorded inside the data, which is not necessarily the name the face
    /// is registered under.</summary>
    /// <remarks>
    /// Kept for diagnostics and as the default when a caller registers a file without naming a
    /// family. Resolution uses the registered name, so an application that registers a face under
    /// its own name gets that name everywhere, including in <see cref="Font.FamilyName"/>.
    /// </remarks>
    public string FamilyName { get; }

    /// <summary>The weight recorded in the data, on the 1-1000 scale.</summary>
    public int Weight { get; }

    /// <summary>Whether the face recorded in the data is slanted.</summary>
    public bool Italic { get; }

    /// <summary>The data this face was read from.</summary>
    /// <remarks>
    /// A realization builds its own typeface from these bytes and owns it, so unregistering a family
    /// drops the registry's copy without taking a face away from a font that is still drawing.
    /// </remarks>
    public ReadOnlyMemory<byte> Data { get; }
}
