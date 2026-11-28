using SkiaSharp;

namespace Kolvir;

/// <summary>
/// The heavyweight half of a font: the typeface that resolved, the <c>SKFont</c> built from it, the
/// metrics read once, and the caches every measurement and paint pass uses.
/// </summary>
/// <remarks>
/// <para>
/// One of these exists per description in use, shared by every <see cref="Font"/> that has that
/// description. That sharing is the point: a control that measures a word against an equal font hits
/// the width cache the first font filled, and a Virtual DOM pass that rebuilds a font per node pays
/// nothing for it.
/// </para>
/// <para>
/// <b>Lifetime is the garbage collector's job.</b> There is no reference count and nothing to
/// dispose: the manager holds this object weakly, the fonts that realized it hold it strongly, and
/// when the last of those goes away the collector collects it. Its finalizer does one thing - remove
/// its own table entry, and only if that entry is still this instance's - and touches no Skia object,
/// because every SkiaSharp object here closes its own handle from its own finalizer.
/// </para>
/// <para>
/// Identity is the point, so this type is not value-equal and is never compared with another
/// realization of the same description. Two of those can exist at once after an invalidation: the
/// fonts that had already realized the old face keep it until they look again.
/// </para>
/// </remarks>
internal sealed class RealizedFont
{
    /// <summary>The printable ASCII a font is expected to cover, checked once when the font is made.</summary>
    private const string AsciiProbe =
        " !\"#$%&'()*+,-./0123456789:;<=>?@ABCDEFGHIJKLMNOPQRSTUVWXYZ[\\]^_`abcdefghijklmnopqrstuvwxyz{|}~";

    /// <summary>How many word widths are remembered before the cache starts over.</summary>
    private const int WidthCacheLimit = 8192;

    /// <summary>How many strings remember how they split into faces before that cache starts over.</summary>
    private const int RunCacheLimit = 4096;

    /// <summary>How many code points a font resolves a fallback face for before it stops caching them.</summary>
    private const int FallbackCacheLimit = 512;

    private readonly FontKey _key;
    private readonly SKTypeface? _typeface;
    private readonly SKFont _font;
    private readonly float _lineHeight;
    private readonly float _baselineOffset;
    private readonly bool _fixedFace;

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

    /// <summary>The fallback faces this font owns, which releasing it hands to the collector.</summary>
    private readonly List<SKTypeface> _ownedFaces = new();

    /// <summary>The manager's entry for this realization, so its finalizer can remove exactly it.</summary>
    private WeakReference<RealizedFont>? _reference;

    /// <summary>Resolves one description and builds the objects the rest of the library measures with.</summary>
    /// <param name="key">The description to realize.</param>
    internal RealizedFont(FontKey key)
    {
        _key = key;

        var (owned, typeface, fixedFace) = ResolveFace(key);
        _typeface = owned;
        _fixedFace = fixedFace;
        _style = new SKFontStyle(
            key.Weight,
            (int)SKFontStyleWidth.Normal,
            key.Italic ? SKFontStyleSlant.Italic : SKFontStyleSlant.Upright);

        _font = new SKFont(typeface, key.Size)
        {
            Subpixel = true,
            Edging = SKFontEdging.Antialias,
        };

        if (fixedFace)
        {
            // Layout metrics are the point of a registered face, so its advances and the font's own
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

    /// <summary>Removes this realization's manager entry when the object is collected.</summary>
    /// <remarks>
    /// The manager verifies that the entry is still this object's own weak reference before removing
    /// it, so a realization that was invalidated while it was alive cannot delete its successor's
    /// entry. Nothing is disposed here; see the type's remarks.
    /// </remarks>
    ~RealizedFont()
    {
        if (_reference is { } reference)
        {
            FontManager.Remove(_key, reference);
        }
    }

    /// <summary>Distance between two baselines, in logical pixels.</summary>
    internal float LineHeight => _lineHeight;

    /// <summary>Distance from the top of a line box to the baseline.</summary>
    internal float BaselineOffset => _baselineOffset;

    /// <summary>The Skia font that draws the primary face at this size.</summary>
    internal SKFont SkFont => _font;

    /// <summary>Hands the manager's entry for this realization to the object, for its finalizer.</summary>
    /// <param name="reference">The weak reference the manager stored under this realization's key.</param>
    internal void Attach(WeakReference<RealizedFont> reference) => _reference = reference;

    /// <summary>
    /// Resolves a description to a face: the registry first, then the platform's own families.
    /// </summary>
    /// <param name="key">The description to resolve.</param>
    /// <returns>
    /// The typeface to use, the typeface this object owns (null when the face belongs to somebody
    /// else), and whether the face came from the registry, which decides the layout settings.
    /// </returns>
    /// <remarks>
    /// A registered family wins over a system family of the same name. That is a capability rather
    /// than an accident: an application that registers its own "Inter" - or its own "Segoe UI" -
    /// gets that face everywhere, including in the theme, without naming a different family anywhere
    /// in its code.
    /// </remarks>
    private static (SKTypeface? Owned, SKTypeface Typeface, bool Fixed) ResolveFace(FontKey key)
    {
        if (FontRegistry.Current.TryResolve(key.Family, key.Weight, key.Italic, out var face))
        {
            // The registry keeps bytes, so each realization builds its own typeface from them and
            // owns it. That is what lets unregistering drop the registry's copy without pulling a
            // face out from under a font that is still drawing with it.
            using var data = SKData.CreateCopy(face.Data.Span);
            var registered = SKTypeface.FromData(data);
            if (registered is not null && registered != SKTypeface.Empty)
            {
                return (registered, registered, true);
            }

            registered?.Dispose();
        }

        var style = new SKFontStyle(
            key.Weight,
            (int)SKFontStyleWidth.Normal,
            key.Italic ? SKFontStyleSlant.Italic : SKFontStyleSlant.Upright);

        var typeface = SKTypeface.FromFamilyName(key.Family.Length == 0 ? null : key.Family, style);
        if (typeface is null || typeface == SKTypeface.Empty)
        {
            // The default face belongs to SkiaSharp, so it is neither wrapped nor disposed here.
            typeface?.Dispose();
            return (null, SKTypeface.Default, false);
        }

        return (typeface, typeface, false);
    }

    /// <summary>
    /// The family name the platform treats as its default, or null when it reports none.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Asked for by <see cref="Font"/>'s constructor when no family is given, and cached: the answer
    /// cannot change while the process runs, and asking is one native call. SkiaSharp ignores the
    /// requested weight and slant when a typeface is asked for by a null family name - DirectWrite
    /// answers the default face whatever style is asked for - so a description that names the default
    /// family is the only way it can express "the default family in bold", and naming it before the
    /// key is built is what lets such a description share a realization with the same family named
    /// explicitly.
    /// </para>
    /// <para>
    /// Null is a real answer - a headless platform, or a font manager with nothing installed - and it
    /// is cached like any other, so a description made without a family stays unnamed on such a
    /// platform rather than re-asking per construction.
    /// </para>
    /// </remarks>
    /// <returns>The platform's default family name, or null.</returns>
    internal static string? DefaultFamilyName() => PlatformDefaultFamily.Value;

    /// <summary>The one native lookup behind <see cref="DefaultFamilyName"/>.</summary>
    private static readonly Lazy<string?> PlatformDefaultFamily = new(ComputeDefaultFamilyName);

    /// <summary>Asks the platform for its default face and reports the family it belongs to.</summary>
    /// <returns>The default family name, or null when the platform reports none.</returns>
    private static string? ComputeDefaultFamilyName()
    {
        var typeface = SKTypeface.FromFamilyName(null, SKFontStyle.Normal);

        if (typeface is null || typeface == SKTypeface.Empty)
        {
            typeface?.Dispose();
            return null;
        }

        try
        {
            string? family = typeface.FamilyName;
            return string.IsNullOrEmpty(family) ? null : family;
        }
        finally
        {
            typeface.Dispose();
        }
    }

    /// <summary>The advance width of <paramref name="text"/> in this font, in logical pixels.</summary>
    /// <param name="text">The text to measure. Must not be null or empty.</param>
    /// <returns>The width the text occupies when drawn at this font's size.</returns>
    internal float MeasureWidth(string text)
    {
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
    /// <remarks>
    /// A registered family's own name is unknown to the platform, so the search starts from the
    /// system default - which is what happened when the same face was loaded from a file directly.
    /// </remarks>
    private string? FamilyForFallback()
    {
        var family = _font.Typeface?.FamilyName;
        return string.IsNullOrEmpty(family) ? null : family;
    }

    /// <summary>A font at this font's size and settings, drawing with a fallback face.</summary>
    private SKFont CreateRunFont(SKTypeface face)
    {
        var font = new SKFont(face, _key.Size)
        {
            Subpixel = true,
            Edging = SKFontEdging.Antialias,
        };

        // A registered face has its layout settings mirrored so the fallback does not silently
        // measure differently from the face it is standing in for on this platform alone.
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
}
