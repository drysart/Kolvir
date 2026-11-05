using SkiaSharp;

namespace XPlatUI;

/// <summary>
/// Displays read-only text. Supports single- or multi-line content, alignment,
/// word wrapping and auto-sizing.
/// </summary>
public class XPlatLabel : XPlatControl
{
    public XPlatLabel()
    {
        AutoSize = true;
        SelectionStyle = XPlatSelectionStyle.Partial;
    }

    public XPlatLabel(string text)
        : this()
    {
        Text = text;
    }

    public XPlatContentAlignment TextAlign { get; set; } = XPlatContentAlignment.MiddleLeft;

    /// <summary>When true the text wraps at the control's width.</summary>
    public bool WrapText { get; set; }

    protected override SKSize MeasureContent(SKSize availableSize)
    {
        float maxWidth = WrapText
            ? MathF.Max(0f, availableSize.Width)
            : float.PositiveInfinity;

        var measured = XPlatTextRenderer.Measure(Text, Font, maxWidth, WrapText && !float.IsInfinity(maxWidth));
        return new SKSize(MathF.Ceiling(measured.Width), MathF.Ceiling(measured.Height));
    }

    protected override void OnPaint(SKCanvas canvas, SKRect rect)
    {
        if (string.IsNullOrEmpty(Text))
        {
            return;
        }

        var content = XPlatGeometry.Deflate(rect, Padding);
        XPlatTextRenderer.Draw(canvas, Text, Font, ForeColor, content, TextAlign, WrapText);
    }

    /// <summary>
    /// Snaps the point to the nearest line and then to the nearest character boundary on it, so a
    /// drag reads like ordinary text selection over the label's own string.
    /// </summary>
    public override float GetSelectionOffset(SKPoint pointInLocal)
    {
        var content = ContentRect;
        var lines = BuildLines(content);
        if (lines.Count == 0)
        {
            return 0f;
        }

        float lineHeight = Font.LineHeight;
        if (lineHeight <= 0f)
        {
            return 0f;
        }

        float top = XPlatTextRenderer.GetBlockTop(content, Font, lines.Count, TextAlign);
        int index = Math.Clamp((int)MathF.Floor((pointInLocal.Y - top) / lineHeight), 0, lines.Count - 1);

        var line = lines[index];
        float left = XPlatTextRenderer.GetLineLeft(content, Font, line.Text, TextAlign);
        return line.Start + OffsetInLine(line.Text, pointInLocal.X - left);
    }

    /// <summary>Adds one rectangle per line the selection touches, clipped to the text on it.</summary>
    public override void GetSelectionRects(int start, int length, List<SKRect> output)
    {
        ArgumentNullException.ThrowIfNull(output);

        if (length <= 0)
        {
            return;
        }

        var content = ContentRect;
        var lines = BuildLines(content);
        if (lines.Count == 0)
        {
            return;
        }

        int end = start + length;
        float lineHeight = Font.LineHeight;
        float top = XPlatTextRenderer.GetBlockTop(content, Font, lines.Count, TextAlign);

        for (int i = 0; i < lines.Count; i++)
        {
            var line = lines[i];
            int from = Math.Max(start, line.Start);
            int to = Math.Min(end, line.End);
            if (to <= from)
            {
                continue;
            }

            float left = XPlatTextRenderer.GetLineLeft(content, Font, line.Text, TextAlign);
            float x1 = left + Font.MeasureWidth(line.Text[..(from - line.Start)]);
            float x2 = left + Font.MeasureWidth(line.Text[..(to - line.Start)]);
            float lineTop = top + (i * lineHeight);
            output.Add(new SKRect(x1, lineTop, MathF.Max(x1, x2), lineTop + lineHeight));
        }
    }

    private List<XPlatTextLine> BuildLines(SKRect content) =>
        XPlatTextRenderer.BuildTextLines(Text, Font, content.Width, WrapText);

    /// <summary>Index of the character boundary in <paramref name="line"/> nearest to <paramref name="x"/>.</summary>
    private int OffsetInLine(string line, float x)
    {
        int best = 0;
        float bestDistance = MathF.Abs(x);
        for (int i = 1; i <= line.Length; i++)
        {
            float distance = MathF.Abs(Font.MeasureWidth(line[..i]) - x);
            if (distance < bestDistance)
            {
                bestDistance = distance;
                best = i;
            }
        }

        return best;
    }
}
