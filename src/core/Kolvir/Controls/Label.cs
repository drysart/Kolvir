using SkiaSharp;

namespace Kolvir;

/// <summary>
/// Displays read-only text. Supports single- or multi-line content, alignment,
/// word wrapping and auto-sizing.
/// </summary>
public class Label : Control
{
    /// <summary>Creates a label with no text. It auto-sizes and uses partial selection.</summary>
    public Label()
    {
        AutoSize = true;
        SelectionStyle = SelectionStyle.Partial;
    }

    /// <summary>Creates a label showing <paramref name="text"/>.</summary>
    /// <param name="text">The text to display.</param>
    public Label(string text)
        : this()
    {
        Text = text;
    }

    /// <summary>
    /// Where the text sits inside the content area. Defaults to
    /// <see cref="ContentAlignment.MiddleLeft"/>.
    /// </summary>
    public ContentAlignment TextAlign { get; set; } = ContentAlignment.MiddleLeft;

    /// <summary>When true the text wraps at the control's width.</summary>
    public bool WrapText { get; set; }

    /// <inheritdoc />
    protected override SKSize MeasureContent(SKSize availableSize)
    {
        float maxWidth = WrapText
            ? MathF.Max(0f, availableSize.Width)
            : float.PositiveInfinity;

        var measured = TextRenderer.Measure(Text, Font, maxWidth, WrapText && !float.IsInfinity(maxWidth));
        return new SKSize(MathF.Ceiling(measured.Width), MathF.Ceiling(measured.Height));
    }

    /// <summary>
    /// Draws the text inside the content area, aligned by <see cref="TextAlign"/> and wrapped at the
    /// content width when <see cref="WrapText"/> is set.
    /// </summary>
    protected override void OnPaint(SKCanvas canvas, SKRect rect)
    {
        if (string.IsNullOrEmpty(Text))
        {
            return;
        }

        var content = Geometry.Deflate(rect, Padding);
        TextRenderer.Draw(canvas, Text, Font, ForeColor, content, TextAlign, WrapText);
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

        float top = TextRenderer.GetBlockTop(content, Font, lines.Count, TextAlign);
        int index = Math.Clamp((int)MathF.Floor((pointInLocal.Y - top) / lineHeight), 0, lines.Count - 1);

        var line = lines[index];
        float left = TextRenderer.GetLineLeft(content, Font, line.Text, TextAlign);
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
        float top = TextRenderer.GetBlockTop(content, Font, lines.Count, TextAlign);

        for (int i = 0; i < lines.Count; i++)
        {
            var line = lines[i];
            int from = Math.Max(start, line.Start);
            int to = Math.Min(end, line.End);
            if (to <= from)
            {
                continue;
            }

            float left = TextRenderer.GetLineLeft(content, Font, line.Text, TextAlign);
            float x1 = left + Font.MeasureWidth(line.Text[..(from - line.Start)]);
            float x2 = left + Font.MeasureWidth(line.Text[..(to - line.Start)]);
            float lineTop = top + (i * lineHeight);
            output.Add(new SKRect(x1, lineTop, MathF.Max(x1, x2), lineTop + lineHeight));
        }
    }

    private List<TextLine> BuildLines(SKRect content) =>
        TextRenderer.BuildTextLines(Text, Font, content.Width, WrapText);

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
