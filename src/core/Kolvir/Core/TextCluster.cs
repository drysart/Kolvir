using System.Globalization;

namespace Kolvir;

/// <summary>
/// Text element boundaries: where a caret may rest, how much a Backspace removes, and which prefixes
/// may be measured.
/// </summary>
/// <remarks>
/// <para>
/// A caret index is a UTF-16 index, but a character a person sees can be several code units: an emoji
/// outside the BMP is a surrogate pair, an accented letter may be a base and a combining mark, and a
/// family emoji is joined code points. A caret that rests inside one of those splits it - Backspace
/// then leaves half a character behind - and a prefix that ends inside one measures half a glyph.
/// </para>
/// <para>
/// These are the boundaries the field uses for every index it exposes: movement, selection, deletion,
/// the caret rectangle and the click mapping all go through them, so geometry and movement cannot
/// disagree. The rules are the parts of the Unicode text segmentation algorithm this library can hit -
/// surrogate pairs, combining marks, variation selectors, emoji modifiers, zero-width-joiner
/// sequences, tag characters, keycaps, regional-indicator pairs and CR LF - and nothing is allocated:
/// each call walks one element.
/// </para>
/// </remarks>
internal static class TextCluster
{
    private const char ZeroWidthJoiner = '\u200D';
    private const int FirstEmojiModifier = 0x1F3FB;
    private const int LastEmojiModifier = 0x1F3FF;
    private const int FirstRegionalIndicator = 0x1F1E6;
    private const int LastRegionalIndicator = 0x1F1FF;

    /// <summary>The boundary at or before <paramref name="index"/>, which is where a caret may rest.</summary>
    /// <param name="text">The text the index belongs to.</param>
    /// <param name="index">Any UTF-16 index in the text; out-of-range values are clamped first.</param>
    internal static int Snap(string text, int index)
    {
        int i = Math.Clamp(index, 0, text.Length);
        if (IsBoundary(text, i))
        {
            return i;
        }

        return Previous(text, i);
    }

    /// <summary>True when <paramref name="index"/> falls between two elements rather than inside one.</summary>
    /// <param name="text">The text the index belongs to.</param>
    /// <param name="index">The index to test; 0 and the length are always boundaries.</param>
    internal static bool IsBoundary(string text, int index)
    {
        if (index <= 0 || index >= text.Length)
        {
            return true;
        }

        // An index is a boundary exactly when the element that starts before it ends there.
        return Next(text, Previous(text, index)) == index;
    }

    /// <summary>The boundary after <paramref name="index"/>, or the end of the text.</summary>
    /// <param name="text">The text to walk.</param>
    /// <param name="index">A boundary to walk from.</param>
    internal static int Next(string text, int index)
    {
        int i = Math.Clamp(index, 0, text.Length);
        if (i >= text.Length)
        {
            return text.Length;
        }

        int first = CodePointAt(text, i);
        i = StepForward(text, i);

        // A carriage return and the line feed after it are one break, not two.
        if (first == '\r' && i < text.Length && text[i] == '\n')
        {
            i++;
        }

        // A pair of regional indicators is one flag: neither half stands alone.
        if (IsRegionalIndicator(first) && i < text.Length && IsRegionalIndicator(CodePointAt(text, i)))
        {
            i = StepForward(text, i);
        }

        while (i < text.Length)
        {
            int codePoint = CodePointAt(text, i);

            if (codePoint == ZeroWidthJoiner && i + 1 < text.Length)
            {
                // The joiner and whatever it joins belong to the same element as the base before them.
                i = StepForward(text, StepForward(text, i));
                continue;
            }

            if (!IsExtension(codePoint))
            {
                break;
            }

            i = StepForward(text, i);
        }

        return i;
    }

    /// <summary>The boundary before <paramref name="index"/>, or 0.</summary>
    /// <param name="text">The text to walk.</param>
    /// <param name="index">A boundary to walk back from.</param>
    internal static int Previous(string text, int index)
    {
        int i = Math.Clamp(index, 0, text.Length);
        if (i <= 0)
        {
            return 0;
        }

        int p = StepBack(text, i);

        while (p > 0)
        {
            int codePoint = CodePointAt(text, p);
            int before = CodePointAt(text, StepBack(text, p));

            if (IsExtension(codePoint)
                || before == ZeroWidthJoiner
                || (IsRegionalIndicator(codePoint) && IsRegionalIndicator(before)))
            {
                p = StepBack(text, p);
                continue;
            }

            if (before == '\r' && codePoint == '\n')
            {
                p = StepBack(text, p);
            }

            break;
        }

        return p;
    }

    /// <summary>True when <paramref name="codePoint"/> continues the element it follows.</summary>
    private static bool IsExtension(int codePoint)
    {
        if (codePoint is >= FirstEmojiModifier and <= LastEmojiModifier)
        {
            return true;
        }

        return CharUnicodeInfo.GetUnicodeCategory(codePoint) switch
        {
            // Format covers zero-width joiners, variation selectors that are not marks, and the tag
            // characters a flag emoji is spelled with.
            UnicodeCategory.NonSpacingMark or UnicodeCategory.SpacingCombiningMark
                or UnicodeCategory.EnclosingMark or UnicodeCategory.Format => true,
            _ => false,
        };
    }

    private static bool IsRegionalIndicator(int codePoint) =>
        codePoint is >= FirstRegionalIndicator and <= LastRegionalIndicator;

    private static int CodePointAt(string text, int index)
    {
        char first = text[index];
        if (char.IsHighSurrogate(first) && index + 1 < text.Length && char.IsLowSurrogate(text[index + 1]))
        {
            return char.ConvertToUtf32(first, text[index + 1]);
        }

        return first;
    }

    private static int StepForward(string text, int index) =>
        char.IsHighSurrogate(text[index]) && index + 1 < text.Length && char.IsLowSurrogate(text[index + 1])
            ? index + 2
            : index + 1;

    private static int StepBack(string text, int index) =>
        index >= 2 && char.IsLowSurrogate(text[index - 1]) && char.IsHighSurrogate(text[index - 2])
            ? index - 2
            : index - 1;
}
