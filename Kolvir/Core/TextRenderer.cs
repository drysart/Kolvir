using SkiaSharp;

namespace Kolvir;

/// <summary>
/// Minimal text layout engine: measurement, greedy word wrapping, alignment and drawing.
/// Kept intentionally small (and allocation-light) so the library stays AOT friendly.
/// </summary>
public static class TextRenderer
{
    public static SKSize Measure(string? text, Font font, float maxWidth, bool wrap)
    {
        if (string.IsNullOrEmpty(text))
        {
            return SKSize.Empty;
        }

        var lines = BuildTextLines(text, font, maxWidth, wrap);
        float width = 0f;
        foreach (var line in lines)
        {
            width = MathF.Max(width, font.MeasureWidth(line.Text));
        }

        return new SKSize(width, lines.Count * font.LineHeight);
    }

    public static void Draw(
        SKCanvas canvas,
        string? text,
        Font font,
        SKColor color,
        SKRect bounds,
        ContentAlignment alignment = ContentAlignment.MiddleLeft,
        bool wrap = false)
    {
        if (string.IsNullOrEmpty(text) || bounds.Width <= 0f || bounds.Height <= 0f)
        {
            return;
        }

        var lines = BuildTextLines(text, font, bounds.Width, wrap);
        if (lines.Count == 0)
        {
            return;
        }

        float lineHeight = font.LineHeight;
        float top = GetBlockTop(bounds, font, lines.Count, alignment);

        using var paint = new SKPaint
        {
            Color = color,
            IsAntialias = true,
            Style = SKPaintStyle.Fill,
        };

        for (int i = 0; i < lines.Count; i++)
        {
            var line = lines[i];
            float x = GetLineLeft(bounds, font, line.Text, alignment);
            float baseline = top + (i * lineHeight) + font.BaselineOffset;
            canvas.DrawText(line.Text, x, baseline, SKTextAlign.Left, font.SkFont, paint);
        }
    }

    /// <summary>Top of the text block inside <paramref name="bounds" /> for the given alignment.</summary>
    public static float GetBlockTop(SKRect bounds, Font font, int lineCount, ContentAlignment alignment)
    {
        float blockHeight = lineCount * font.LineHeight;
        return alignment switch
        {
            ContentAlignment.MiddleLeft or ContentAlignment.MiddleCenter or ContentAlignment.MiddleRight
                => bounds.Top + ((bounds.Height - blockHeight) / 2f),
            ContentAlignment.BottomLeft or ContentAlignment.BottomCenter or ContentAlignment.BottomRight
                => bounds.Bottom - blockHeight,
            _ => bounds.Top,
        };
    }

    /// <summary>Left edge of a single line inside <paramref name="bounds" /> for the given alignment.</summary>
    public static float GetLineLeft(SKRect bounds, Font font, string line, ContentAlignment alignment)
    {
        float lineWidth = font.MeasureWidth(line);
        return alignment switch
        {
            ContentAlignment.TopCenter or ContentAlignment.MiddleCenter or ContentAlignment.BottomCenter
                => bounds.Left + ((bounds.Width - lineWidth) / 2f),
            ContentAlignment.TopRight or ContentAlignment.MiddleRight or ContentAlignment.BottomRight
                => bounds.Right - lineWidth,
            _ => bounds.Left,
        };
    }

    /// <summary>Returns the text of the individual lines that <paramref name="text" /> occupies.</summary>
    public static List<string> BuildLines(string? text, Font font, float maxWidth, bool wrap)
    {
        var lines = BuildTextLines(text, font, maxWidth, wrap);
        var result = new List<string>(lines.Count);
        foreach (var line in lines)
        {
            result.Add(line.Text);
        }

        return result;
    }

    /// <summary>
    /// Lays <paramref name="text" /> out into lines, recording where each line came from so that
    /// callers (such as selection) can map between characters and geometry. Line text is always a
    /// slice of the source, so indices stay meaningful for the caller's own string.
    /// </summary>
    public static List<TextLine> BuildTextLines(string? text, Font font, float maxWidth, bool wrap)
    {
        var lines = new List<TextLine>();
        if (string.IsNullOrEmpty(text))
        {
            return lines;
        }

        int index = 0;
        while (index <= text.Length)
        {
            int breakIndex = -1;
            for (int i = index; i < text.Length; i++)
            {
                if (text[i] == '\n' || text[i] == '\r')
                {
                    breakIndex = i;
                    break;
                }
            }

            int lineEnd = breakIndex < 0 ? text.Length : breakIndex;
            int lineLength = lineEnd - index;
            if (!wrap || maxWidth <= 0f || font.MeasureWidth(text.Substring(index, lineLength)) <= maxWidth)
            {
                lines.Add(new TextLine(index, lineLength, text.Substring(index, lineLength)));
            }
            else
            {
                WrapLine(text, index, lineEnd, font, maxWidth, lines);
            }

            if (breakIndex < 0)
            {
                break;
            }

            index = breakIndex + (text[breakIndex] == '\r' && breakIndex + 1 < text.Length && text[breakIndex + 1] == '\n' ? 2 : 1);
        }

        return lines;
    }

    private static void WrapLine(string source, int lineStart, int lineEnd, Font font, float maxWidth, List<TextLine> output)
    {
        int currentStart = -1;
        int currentEnd = -1;
        int wordStart = -1;

        for (int i = lineStart; i <= lineEnd; i++)
        {
            if (i < lineEnd && source[i] != ' ')
            {
                if (wordStart < 0)
                {
                    wordStart = i;
                }

                continue;
            }

            if (wordStart < 0)
            {
                continue;
            }

            int start = wordStart;
            int end = i;
            wordStart = -1;

            if (currentStart < 0)
            {
                currentStart = start;
                currentEnd = end;
            }
            else if (font.MeasureWidth(source[currentStart..end]) <= maxWidth)
            {
                currentEnd = end;
            }
            else
            {
                Flush();
                currentStart = start;
                currentEnd = end;
            }

            // A single word that is wider than the available space gets hard-broken.
            while (font.MeasureWidth(source[currentStart..currentEnd]) > maxWidth)
            {
                int take = HardBreakIndex(source, currentStart, currentEnd, font, maxWidth);
                if (take <= 0)
                {
                    break;
                }

                output.Add(new TextLine(currentStart, take, source.Substring(currentStart, take)));
                currentStart += take;
            }
        }

        Flush();

        void Flush()
        {
            if (currentStart >= 0 && currentEnd > currentStart)
            {
                output.Add(new TextLine(currentStart, currentEnd - currentStart, source.Substring(currentStart, currentEnd - currentStart)));
            }

            currentStart = -1;
            currentEnd = -1;
        }
    }

    private static int HardBreakIndex(string source, int start, int end, Font font, float maxWidth)
    {
        int best = 0;
        for (int i = 1; start + i <= end; i++)
        {
            if (font.MeasureWidth(source[start..(start + i)]) > maxWidth)
            {
                break;
            }

            best = i;
        }

        return best;
    }
}

/// <summary>
/// One laid-out line of text together with the slice of the source string it was taken from, so
/// callers can map between characters and geometry.
/// </summary>
public readonly struct TextLine
{
    public TextLine(int start, int length, string text)
    {
        Start = start;
        Length = length;
        Text = text;
    }

    /// <summary>Offset of the first character of this line in the source string.</summary>
    public int Start { get; }

    /// <summary>Number of source characters in the line, excluding the line break.</summary>
    public int Length { get; }

    /// <summary>Offset just past the last character of this line.</summary>
    public int End => Start + Length;

    /// <summary>The text drawn for this line.</summary>
    public string Text { get; }
}
