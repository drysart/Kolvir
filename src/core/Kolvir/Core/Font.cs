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

    /// <summary>
    /// Creates a font at <paramref name="size"/> in the family the platform treats as its default.
    /// </summary>
    /// <param name="size">The size to draw at, in logical pixels.</param>
    /// <param name="bold">True to ask for weight 700 instead of 400.</param>
    /// <param name="italic">True to ask for an italic face.</param>
    /// <remarks>
    /// The family is left to the platform, so the same call can draw with a different face on each
    /// desktop. Pass a family name, or load a file with <see cref="FromFile"/>, when text has to
    /// look the same everywhere.
    /// </remarks>
    public Font(float size, bool bold = false, bool italic = false)
        : this(null, size, bold, italic)
    {
    }

    /// <summary>
    /// Creates a font in a family the operating system provides, at <paramref name="size"/>.
    /// </summary>
    /// <param name="familyName">
    /// The family to resolve. Null, or a family the platform does not have, falls back to the
    /// platform's default face rather than failing.
    /// </param>
    /// <param name="size">The size to draw at, in logical pixels.</param>
    /// <param name="bold">True to ask for weight 700 instead of 400.</param>
    /// <param name="italic">True to ask for an italic face.</param>
    /// <remarks>
    /// Skia selects the nearest face the family has to the requested weight and slant, and it does
    /// not synthesise one the family lacks: asking for bold in a family that only has a regular face
    /// draws the regular face. <see cref="Weight"/> and <see cref="Italic"/> therefore report what
    /// was asked for, not what the platform chose.
    /// </remarks>
    public Font(string? familyName, float size, bool bold = false, bool italic = false)
        : this(familyName, size, bold ? 700 : 400, italic)
    {
    }

    /// <summary>
    /// Creates a font at <paramref name="size"/> in the platform's default family, at a numeric
    /// weight.
    /// </summary>
    /// <param name="size">The size to draw at, in logical pixels.</param>
    /// <param name="weight">
    /// The weight to ask for, on the 1-1000 scale where 400 is regular and 700 is bold.
    /// </param>
    /// <param name="italic">True to ask for an italic face.</param>
    /// <remarks>
    /// As with the overload that takes a <c>bold</c> flag, the family is whatever the platform treats
    /// as its default, so the face is not the same on every desktop.
    /// </remarks>
    public Font(float size, int weight, bool italic = false)
        : this(null, size, weight, italic)
    {
    }

    /// <summary>
    /// Creates a font in a family the operating system provides, at <paramref name="size"/> and a
    /// numeric weight.
    /// </summary>
    /// <param name="familyName">
    /// The family to resolve, or null to use the platform's default family. A family the platform
    /// does not have also falls back to the default face.
    /// </param>
    /// <param name="size">The size to draw at, in logical pixels.</param>
    /// <param name="weight">
    /// The weight to ask for, on the 1-1000 scale where 400 is regular and 700 is bold. Skia uses the
    /// nearest weight the family has.
    /// </param>
    /// <param name="italic">True to ask for an italic face.</param>
    /// <remarks>
    /// Unlike a font loaded from data, a family can supply a face close to the requested weight or
    /// slant; the choice is the platform's, because each platform resolves a family name to its own
    /// files.
    /// </remarks>
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
        _style = new SKFontStyle(
            weight,
            (int)SKFontStyleWidth.Normal,
            italic ? SKFontStyleSlant.Italic : SKFontStyleSlant.Upright);

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
        _coversAscii = CoversAscii(_font);
    }

    /// <summary>The size this font draws at, in logical pixels.</summary>
    public float Size { get; }

    /// <summary>
    /// The weight of the face, on the 1-1000 scale where 400 is regular and 700 is bold.
    /// </summary>
    /// <remarks>
    /// For a face loaded from data this is the weight recorded in the file, whatever was asked for,
    /// because one file holds one face. For a face resolved by family name it is the weight that was
    /// requested, which the platform may have had to substitute with the nearest face it has.
    /// </remarks>
    public int Weight { get; }

    /// <summary>Whether the face is italic.</summary>
    /// <remarks>
    /// For a face loaded from data this says whether the file is slanted, since no slant is
    /// synthesised; for a face resolved by family name it is the slant that was requested.
    /// </remarks>
    public bool Italic { get; }

    /// <summary>
    /// The family name of the face in use, or the empty string when the face reports none.
    /// </summary>
    /// <remarks>
    /// A font created without a family still reports the family the platform selected, so this is a
    /// report of the face rather than an echo of the constructor.
    /// </remarks>
    public string FamilyName => _font.Typeface?.FamilyName ?? string.Empty;

    /// <summary>Distance between two baselines, in logical pixels.</summary>
    public float LineHeight => _lineHeight;

    /// <summary>Distance from the top of a line box to the baseline.</summary>
    public float BaselineOffset => _baselineOffset;

    internal SKFont SkFont => _font;

    /// <summary>The advance width of <paramref name="text"/> in this font, in logical pixels.</summary>
    /// <param name="text">The text to measure. Null or an empty string measures zero.</param>
    /// <returns>The width the text occupies when drawn at this font's size.</returns>
    /// <remarks>
    /// The width comes from the face's own advances, so a face loaded from data measures the same on
    /// every platform; a face resolved by family name measures however that platform's file lays the
    /// text out.
    /// </remarks>
    public float MeasureWidth(string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return 0f;
        }

        // Laying text out asks for the width of a word, and asks again on every pass: a window that
        // is being resized measures the same words thousands of times, and measuring them is the whole
        // cost of the pass. A word's width cannot change while the text and this font are the same, so
        // it is worth remembering - a resize then spends its time building lines rather than shaping
        // glyphs it has already shaped.
        if (_widths.TryGetValue(text, out float known))
        {
            return known;
        }

        float width = MeasureByFace(text);

        // Text that is mostly unique - a log, a chat that never repeats a word - would grow this
        // without bound, and a miss only costs a measurement, so it starts over instead.
        if (_widths.Count >= WidthCacheLimit)
        {
            _widths.Clear();
        }

        _widths[text] = width;
        return width;
    }

    /// <summary>
    /// Measures <paramref name="text"/> by adding up the widths of the runs its characters fall into,
    /// so a character the primary face does not cover is measured with the face that draws it.
    /// </summary>
    private float MeasureByFace(string text)
    {
        // Text the primary face plainly covers - printable ASCII in a font that has it - is measured
        // in one call and never becomes a run at all, so the common string pays nothing for a feature
        // it does not use. Everything else goes through the split, whose result is cached.
        if (_coversAscii && IsAscii(text))
        {
            return _font.MeasureText(text);
        }

        var runs = TextRuns(text);

        // A single run drawn by a fallback face is not measured with the primary font: that would
        // report the width of a row of boxes instead of the width of what is drawn.
        if (runs.Length == 1 && ReferenceEquals(runs[0].Font, _font))
        {
            return _font.MeasureText(text);
        }

        float width = 0f;
        foreach (var run in runs)
        {
            width += run.Font.MeasureText(run.Text);
        }

        return width;
    }

    /// <summary>The text split into runs that one face can draw, in order.</summary>
    /// <param name="text">The text to split. An empty string splits into nothing.</param>
    /// <remarks>
    /// This is the seam that keeps drawing and measuring in agreement: a fallback face with different
    /// advances would otherwise be drawn in one place and measured in another, which puts carets,
    /// selection and line breaks at the wrong character. The split is cached by string, so a frame
    /// that redraws the same text pays a dictionary lookup rather than a scan.
    /// </remarks>
    internal TextRun[] TextRuns(string text)
    {
        if (text.Length == 0)
        {
            return Array.Empty<TextRun>();
        }

        if (_runs.TryGetValue(text, out var known))
        {
            return known;
        }

        var runs = BuildTextRuns(text);

        if (_runs.Count >= RunCacheLimit)
        {
            _runs.Clear();
        }

        _runs[text] = runs;
        return runs;
    }

    /// <summary>How many faces <paramref name="text"/> needs, which is what a test of the split asserts.</summary>
    internal int TextRunCount(string text) => TextRuns(text).Length;

    private TextRun[] BuildTextRuns(string text)
    {
        // The common case is text the primary face covers, and it has to cost one pass and no
        // allocation, because this runs on the measure path.
        if (!NeedsFallback(text))
        {
            return new[] { new TextRun(0, text.Length, text, _font) };
        }

        var runs = new List<TextRun>(2);
        int runStart = 0;
        var runFont = _font;

        for (int i = 0; i < text.Length;)
        {
            int codePoint = char.IsHighSurrogate(text[i]) && i + 1 < text.Length && char.IsLowSurrogate(text[i + 1])
                ? char.ConvertToUtf32(text[i], text[i + 1])
                : text[i];
            int width = codePoint > char.MaxValue ? 2 : 1;

            var font = FontForCodePoint(codePoint);
            if (!ReferenceEquals(font, runFont))
            {
                if (i > runStart)
                {
                    runs.Add(new TextRun(runStart, i - runStart, text.Substring(runStart, i - runStart), runFont));
                }

                runStart = i;
                runFont = font;
            }

            i += width;
        }

        runs.Add(new TextRun(runStart, text.Length - runStart, text.Substring(runStart, text.Length - runStart), runFont));
        return runs.ToArray();
    }

    /// <summary>
    /// True when some character of <paramref name="text"/> has no glyph in the primary face, which is
    /// the only case that needs a split.
    /// </summary>
    /// <remarks>
    /// The answer comes from one native call for the whole string rather than one per character: a
    /// long line measured for the first time is exactly where a per-character call shows up, and this
    /// runs on the measure path. A zero glyph anywhere - control characters include - sends the string
    /// to the careful check, which is only reached by text that actually needs a second face.
    /// </remarks>
    private bool NeedsFallback(string text)
    {
        // Every face this library is asked to draw with covers printable ASCII; a face that does not
        // (a CJK-only file, say) is checked at construction and stays on the general path. This is the
        // single most common string in any UI, so it costs one managed scan and no native call.
        if (_coversAscii && IsAscii(text))
        {
            return false;
        }

        var glyphs = _font.GetGlyphs(text);
        for (int i = 0; i < glyphs.Length; i++)
        {
            if (glyphs[i] == 0)
            {
                return NeedsFallbackPerCharacter(text);
            }
        }

        return false;
    }

    private static bool IsAscii(string text)
    {
        for (int i = 0; i < text.Length; i++)
        {
            if (text[i] >= 0x80)
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>The printable ASCII a font is expected to cover, checked once when the font is made.</summary>
    private const string AsciiProbe =
        " !\"#$%&'()*+,-./0123456789:;<=>?@ABCDEFGHIJKLMNOPQRSTUVWXYZ[\\]^_`abcdefghijklmnopqrstuvwxyz{|}~";

    private static bool CoversAscii(SKFont font)
    {
        var glyphs = font.GetGlyphs(AsciiProbe);
        for (int i = 0; i < glyphs.Length; i++)
        {
            if (glyphs[i] == 0)
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// The precise check: control characters are never drawn and need no face, and a surrogate pair is
    /// one code point rather than two missing glyphs.
    /// </summary>
    private bool NeedsFallbackPerCharacter(string text)
    {
        for (int i = 0; i < text.Length; i++)
        {
            char character = text[i];
            if (character < ' ')
            {
                continue;
            }

            if (char.IsSurrogate(character) || _font.GetGlyph(character) == 0)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>The font that draws <paramref name="codePoint"/>: this one, or the face that covers it.</summary>
    private SKFont FontForCodePoint(int codePoint)
    {
        if (_font.GetGlyph(codePoint) != 0)
        {
            return _font;
        }

        return FallbackFontFor(codePoint) ?? _font;
    }

    private SKFont? FallbackFontFor(int codePoint)
    {
        if (_coverage.TryGetValue(codePoint, out var known))
        {
            return known;
        }

        var font = ResolveCoveringFont(codePoint);

        // Past the limit a code point is resolved again on each measure rather than pinned: the cache
        // exists for the characters a document actually uses, and text with more than that is not a
        // hot path worth an unbounded table.
        if (_coverage.Count < FallbackCacheLimit)
        {
            _coverage[codePoint] = font;
        }

        return font;
    }

    /// <summary>
    /// The font for the face the platform offers for <paramref name="codePoint"/>, reusing the one
    /// already made for that family and style, or null when nothing covers the character.
    /// </summary>
    private SKFont? ResolveCoveringFont(int codePoint)
    {
        var face = SKFontManager.Default.MatchCharacter(
            FamilyForFallback(),
            _style,
            null,
            codePoint);

        if (face is null || face == SKTypeface.Empty)
        {
            face?.Dispose();
            return null;
        }

        // The platform returns a new wrapper for the same physical face on every query, so the
        // identity that matters for a run is the family and style, not the wrapper.
        var key = (face.FamilyName ?? string.Empty, face.FontStyle.Weight, face.FontStyle.Slant);
        if (_faceFonts.TryGetValue(key, out var existing))
        {
            face.Dispose();
            return existing.GetGlyph(codePoint) != 0 ? existing : null;
        }

        var font = CreateRunFont(face);
        if (font.GetGlyph(codePoint) == 0)
        {
            // A platform can hand back a face that still has no glyph for the character. Drawing with
            // it would be no better than the box it replaces, so it is refused and the primary face
            // keeps the character, exactly as it did before there was any fallback.
            font.Dispose();
            face.Dispose();
            return null;
        }

        _faceFonts[key] = font;
        _ownedFaces.Add(face);
        return font;
    }

    /// <summary>
    /// The family to search from: this font's own, so the platform prefers its fallbacks, or null for
    /// the system default family when the face reports none.
    /// </summary>
    private string? FamilyForFallback()
    {
        var family = _font.Typeface?.FamilyName;
        return string.IsNullOrEmpty(family) ? null : family;
    }

    /// <summary>A font at this font's size and settings, drawing with a fallback face.</summary>
    private SKFont CreateRunFont(SKTypeface face)
    {
        var font = new SKFont(face, Size)
        {
            Subpixel = true,
            Edging = SKFontEdging.Antialias,
        };

        // A pinned face has its layout settings mirrored so the fallback does not silently measure
        // differently from the face it is standing in for on this platform alone.
        if (_fixedFace)
        {
            font.LinearMetrics = true;
            font.Hinting = SKFontHinting.None;
            font.BaselineSnap = false;
        }

        return font;
    }

    /// <summary>
    /// Draws <paramref name="text"/> with its baseline at <paramref name="baseline"/>, switching face
    /// where the primary face has no glyph, so text from another script is legible instead of a row
    /// of missing-glyph boxes.
    /// </summary>
    /// <param name="canvas">The canvas to draw into.</param>
    /// <param name="text">The text to draw. Nothing is drawn for an empty string.</param>
    /// <param name="x">The left edge, or the alignment point, of the drawn text.</param>
    /// <param name="baseline">The y coordinate of the baseline.</param>
    /// <param name="paint">The paint, which owns the colour.</param>
    /// <param name="alignment">How <paramref name="x"/> is interpreted, which is the text align Skia takes.</param>
    /// <remarks>
    /// The advances come from the same run split <see cref="MeasureWidth"/> uses, so a caret placed at
    /// a measured width lands where the glyphs were drawn.
    /// </remarks>
    internal void DrawText(
        SKCanvas canvas,
        string text,
        float x,
        float baseline,
        SKPaint paint,
        SKTextAlign alignment = SKTextAlign.Left)
    {
        if (text.Length == 0)
        {
            return;
        }

        if (_coversAscii && IsAscii(text) && alignment == SKTextAlign.Left)
        {
            canvas.DrawText(text, x, baseline, SKTextAlign.Left, _font, paint);
            return;
        }

        var runs = TextRuns(text);
        if (runs.Length == 1 && ReferenceEquals(runs[0].Font, _font) && alignment == SKTextAlign.Left)
        {
            canvas.DrawText(text, x, baseline, SKTextAlign.Left, _font, paint);
            return;
        }

        if (alignment != SKTextAlign.Left)
        {
            float width = MeasureWidth(text);
            x -= alignment switch
            {
                SKTextAlign.Center => width / 2f,
                SKTextAlign.Right => width,
                _ => 0f,
            };
        }

        foreach (var run in runs)
        {
            canvas.DrawText(run.Text, x, baseline, SKTextAlign.Left, run.Font, paint);
            x += run.Font.MeasureText(run.Text);
        }
    }

    /// <summary>
    /// One stretch of a string that a single face draws: where it starts in the source, how long it
    /// is, and the font that draws it.
    /// </summary>
    internal readonly struct TextRun
    {
        internal TextRun(int start, int length, string text, SKFont font)
        {
            Start = start;
            Length = length;
            Text = text;
            Font = font;
        }

        /// <summary>Offset of the run's first character in the string it came from.</summary>
        internal int Start { get; }

        /// <summary>Number of characters in the run.</summary>
        internal int Length { get; }

        /// <summary>This run's slice of the source, which is the whole string when there is one run.</summary>
        internal string Text { get; }

        /// <summary>The font that draws this run.</summary>
        internal SKFont Font { get; }
    }

    /// <summary>How many word widths are remembered before the cache starts over.</summary>
    private const int WidthCacheLimit = 8192;

    /// <summary>How many strings remember how they split into faces before that cache starts over.</summary>
    private const int RunCacheLimit = 4096;

    /// <summary>How many code points a font resolves a fallback face for before it stops caching them.</summary>
    private const int FallbackCacheLimit = 512;

    private readonly Dictionary<string, float> _widths = new(StringComparer.Ordinal);

    /// <summary>
    /// Strings already split into single-face runs. Drawing asks for this on every frame, so a
    /// string that has been drawn once costs one dictionary lookup afterwards.
    /// </summary>
    private readonly Dictionary<string, TextRun[]> _runs = new(StringComparer.Ordinal);

    /// <summary>Code point to the font that covers it, or null when no installed face does.</summary>
    private readonly Dictionary<int, SKFont?> _coverage = new();

    /// <summary>
    /// Family and style to the font that draws with it at this font's size. Keyed by family rather
    /// than by typeface because the platform hands back a new wrapper for the same face on every
    /// query, and a run is a family, not a wrapper.
    /// </summary>
    private readonly Dictionary<(string Family, int Weight, SKFontStyleSlant Slant), SKFont> _faceFonts = new();

    /// <summary>The fallback faces this font owns, which <see cref="Dispose"/> releases.</summary>
    private readonly List<SKTypeface> _ownedFaces = new();

    /// <summary>
    /// The style asked of a fallback face, so a bold field gets the bold face of the family that
    /// covers the character rather than the regular one.
    /// </summary>
    private readonly SKFontStyle _style;

    /// <summary>
    /// Whether the primary face has a glyph for every printable ASCII character, which lets the common
    /// string skip the coverage check entirely.
    /// </summary>
    private readonly bool _coversAscii;

    /// <summary>A font with the same face at another size.</summary>
    /// <param name="size">The size for the new font, in logical pixels.</param>
    /// <returns>
    /// A new font; this one is unchanged, and both stay usable after either is disposed.
    /// </returns>
    /// <remarks>
    /// The face, weight and italic are carried over, and for a face loaded from data the size is all
    /// that can change: one file holds one face, so no weight or slant is selected from it. A
    /// family-name font is looked up again at the new size.
    /// </remarks>
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

    /// <summary>
    /// Releases the Skia objects behind the font, including the typeface when this font is the one
    /// that owns it.
    /// </summary>
    /// <remarks>
    /// Disposing twice does nothing, and fonts made from this one with <see cref="WithSize"/> stay
    /// usable because each holds its own reference to the face. Controls never dispose a font they
    /// did not create, so a font handed to a control is the application's to release.
    /// </remarks>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        foreach (var font in _faceFonts.Values)
        {
            font.Dispose();
        }

        foreach (var face in _ownedFaces)
        {
            face.Dispose();
        }

        _faceFonts.Clear();
        _ownedFaces.Clear();
        _coverage.Clear();
        _runs.Clear();
        _widths.Clear();

        _font.Dispose();
        _typeface?.Dispose();
    }
}
