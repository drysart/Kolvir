using SkiaSharp;

namespace Kolvir;

/// <summary>
/// A lightweight, immutable font description backed by an <see cref="SKFont"/>: a size plus a face.
/// </summary>
/// <remarks>
/// <para>
/// The face is either resolved by family name from the ones the operating system has, or loaded from
/// font data with <see cref="FromFile"/>, <see cref="FromStream"/> or <see cref="FromBytes"/>. A
/// loaded face is one physical face on every platform, so the glyph advances, line height and
/// baseline are the same on Windows, macOS and Linux - which family-name lookup cannot promise,
/// because each platform has its own idea of what a family name resolves to.
/// </para>
/// <para>
/// A loaded font is one face. <see cref="WithSize"/> keeps that face at another size, but a weight
/// or an italic the data does not contain cannot be selected: there is no synthetic bold, so the
/// result is the same face, and <see cref="Weight"/>/<see cref="Italic"/> describe the face actually
/// used rather than what was asked for. Load a bold file separately when an emphasis needs one.
/// </para>
/// <para>
/// Instances are owned by the caller; controls never dispose fonts they did not create. A font made
/// with <see cref="WithSize"/> holds its own reference to the same face, so it stays usable after the
/// font it came from is disposed, and disposing any of them twice does nothing.
/// </para>
/// </remarks>
public sealed class Font : IDisposable
{
    /// <summary>The size <see cref="FromFile"/>, <see cref="FromStream"/> and <see cref="FromBytes"/> default to.</summary>
    private const float LoadedDefaultSize = 13f;

    private readonly SKTypeface? _typeface;
    private readonly SKFont _font;
    private readonly float _lineHeight;
    private readonly float _baselineOffset;
    private readonly bool _fixedFace;
    private bool _disposed;

    public Font(float size, bool bold = false, bool italic = false)
        : this(null, size, bold, italic)
    {
    }

    public Font(string? familyName, float size, bool bold = false, bool italic = false)
        : this(familyName, size, bold ? 700 : 400, italic)
    {
    }

    public Font(float size, int weight, bool italic = false)
        : this(null, size, weight, italic)
    {
    }

    public Font(string? familyName, float size, int weight, bool italic = false)
        : this(ResolveFamilyFace(familyName, weight, italic), size, weight, italic, fixedFace: false)
    {
    }

    /// <summary>Creates a font from a font file, such as a <c>.ttf</c> or <c>.otf</c> shipped with an application.</summary>
    /// <param name="path">The file to read. It is read completely, so the font does not depend on it afterwards.</param>
    /// <param name="size">The size to draw at, in logical pixels. Defaults to 13, the theme's default size.</param>
    /// <returns>A font of exactly the face in the file, on every platform.</returns>
    /// <exception cref="FileNotFoundException">The file does not exist.</exception>
    /// <exception cref="FontFormatException">The file does not contain a font this build can read.</exception>
    public static Font FromFile(string path, float size = LoadedDefaultSize)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        return Load(File.ReadAllBytes(path), size, nameof(path));
    }

    /// <summary>Creates a font from a stream of font data, which is the shape an embedded resource arrives in.</summary>
    /// <param name="stream">The stream to read to its end. It is not disposed and is not used again.</param>
    /// <param name="size">The size to draw at, in logical pixels. Defaults to 13, the theme's default size.</param>
    /// <returns>A font of exactly the face in the data, on every platform.</returns>
    /// <exception cref="FontFormatException">The stream does not contain a font this build can read.</exception>
    public static Font FromStream(Stream stream, float size = LoadedDefaultSize)
    {
        ArgumentNullException.ThrowIfNull(stream);

        // The whole face is copied rather than streamed, because Skia keeps reading a stream-backed
        // typeface lazily and the caller is free to close its stream as soon as this returns.
        using var buffer = new MemoryStream();
        stream.CopyTo(buffer);
        return Load(buffer.ToArray(), size, nameof(stream));
    }

    /// <summary>Creates a font from font data already in memory, such as a resource read into a byte array.</summary>
    /// <param name="data">The bytes of a <c>.ttf</c> or <c>.otf</c>.</param>
    /// <param name="size">The size to draw at, in logical pixels. Defaults to 13, the theme's default size.</param>
    /// <returns>A font of exactly the face in the data, on every platform.</returns>
    /// <exception cref="FontFormatException">The data is empty or does not contain a font this build can read.</exception>
    public static Font FromBytes(ReadOnlySpan<byte> data, float size = LoadedDefaultSize) =>
        Load(data, size, nameof(data));

    private static Font Load(ReadOnlySpan<byte> data, float size, string paramName)
    {
        if (data.IsEmpty)
        {
            throw new FontFormatException(null, paramName);
        }

        SKTypeface? typeface;
        try
        {
            using var skData = SKData.CreateCopy(data);
            typeface = SKTypeface.FromData(skData);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            // Skia owns the parsing, and a stream that is not a font is a caller mistake rather than
            // a crash: report it the way an unusable argument is reported.
            throw new FontFormatException(null, paramName, exception);
        }

        if (typeface is null || typeface == SKTypeface.Empty || string.IsNullOrEmpty(typeface.FamilyName))
        {
            typeface?.Dispose();
            throw new FontFormatException(null, paramName);
        }

        // One face is all the data contains: every size and every derived font keeps exactly this
        // typeface, and no weight or slant can be selected from it.
        var style = typeface.FontStyle;
        return new Font(
            (typeface, typeface),
            size,
            style.Weight,
            style.Slant != SKFontStyleSlant.Upright,
            fixedFace: true);
    }

    private static (SKTypeface? Owned, SKTypeface Typeface) ResolveFamilyFace(string? familyName, int weight, bool italic)
    {
        var style = new SKFontStyle(weight, (int)SKFontStyleWidth.Normal, italic ? SKFontStyleSlant.Italic : SKFontStyleSlant.Upright);
        var typeface = SKTypeface.FromFamilyName(familyName, style);
        if (typeface is null || typeface == SKTypeface.Empty)
        {
            // The default face belongs to SkiaSharp, so it is neither wrapped nor disposed here.
            return (null, SKTypeface.Default);
        }

        return (typeface, typeface);
    }

    /// <summary>
    /// The one constructor every public one funnels into. <c>Owned</c> is a typeface this font has to
    /// dispose, or null when the typeface belongs to somebody else - SkiaSharp's default face, or the
    /// cached one inside the <see cref="SKFont"/> a derived font was made from.
    /// </summary>
    private Font((SKTypeface? Owned, SKTypeface Typeface) source, float size, int weight, bool italic, bool fixedFace)
    {
        Size = size;
        Weight = weight;
        Italic = italic;
        _fixedFace = fixedFace;
        _typeface = source.Owned;

        _font = new SKFont(source.Typeface, size)
        {
            Subpixel = true,
            Edging = SKFontEdging.Antialias,
        };

        if (fixedFace)
        {
            // Layout metrics are the point of a pinned face, so its advances and the font's own
            // ascent/descent are taken unhinted and unrounded. Without this the FreeType backend
            // rounds advances to whole pixels while CoreText and DirectWrite do not, and the same
            // file measures differently on Linux for no reason the caller can see. A font resolved
            // by family name keeps exactly the settings it always had.
            _font.LinearMetrics = true;
            _font.Hinting = SKFontHinting.None;
            _font.BaselineSnap = false;
        }

        var metrics = _font.Metrics;
        _baselineOffset = -metrics.Ascent;
        _lineHeight = -metrics.Ascent + metrics.Descent + metrics.Leading;
    }

    public float Size { get; }

    public int Weight { get; }

    public bool Italic { get; }

    public string FamilyName => _font.Typeface?.FamilyName ?? string.Empty;

    /// <summary>Distance between two baselines, in logical pixels.</summary>
    public float LineHeight => _lineHeight;

    /// <summary>Distance from the top of a line box to the baseline.</summary>
    public float BaselineOffset => _baselineOffset;

    internal SKFont SkFont => _font;

    public float MeasureWidth(string? text) =>
        string.IsNullOrEmpty(text) ? 0f : _font.MeasureText(text);

    public Font WithSize(float size) => Derive(size, Weight, Italic);

    /// <summary>
    /// A font with the same face at <paramref name="size"/> in <paramref name="weight"/> and
    /// <paramref name="italic"/> style. A face loaded from data has only one style, so it is kept
    /// as it is; a face resolved by family name is looked up again, which is what it has always done.
    /// </summary>
    internal Font Derive(float size, int weight, bool italic)
    {
        if (_fixedFace && _font.Typeface is { } face)
        {
            // The SKFont keeps its typeface alive natively and hands back the wrapper it owns, so the
            // derived font must not dispose it: its own SKFont takes a reference of its own, which is
            // what lets it outlive this font.
            return new Font((null, face), size, Weight, Italic, fixedFace: true);
        }

        return new Font(FamilyName, size, weight, italic);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _font.Dispose();
        _typeface?.Dispose();
    }
}
