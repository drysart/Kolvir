using SkiaSharp;

namespace XPlatUI;

/// <summary>
/// A check box: a square glyph followed by a label, checked and unchecked by a click or the space
/// bar. Set <see cref="XPlatToggleControl.ThreeState"/> for the indeterminate state as well.
/// </summary>
public class XPlatCheckBox : XPlatToggleControl
{
    public XPlatCheckBox()
    {
    }

    public XPlatCheckBox(string text)
        : this()
    {
        Text = text;
    }

    protected override void PaintGlyph(SKCanvas canvas, SKRect glyph)
    {
        const float Radius = 3f;

        bool marked = CheckState != XPlatCheckState.Unchecked;

        if (marked)
        {
            XPlatPainter.Fill(canvas, glyph, GlyphFillColor, Radius);
        }
        else if (IsMouseOver && Enabled)
        {
            XPlatPainter.Fill(canvas, glyph, XPlatTheme.Secondary, Radius);
        }

        XPlatPainter.DrawBorder(canvas, glyph, GlyphBorderColor, 1.5f, Radius);

        if (!marked)
        {
            return;
        }

        if (CheckState == XPlatCheckState.Indeterminate)
        {
            var bar = new SKRect(glyph.Left + 4f, glyph.MidY - 1f, glyph.Right - 4f, glyph.MidY + 1f);
            XPlatPainter.Fill(canvas, bar, GlyphMarkColor, 1f);
            return;
        }

        // The tick is drawn as a stroke rather than filled so it keeps its weight at any glyph
        // size; the join keeps the corner from notching.
        using var builder = new SKPathBuilder();
        builder.MoveTo(glyph.Left + 4f, glyph.MidY);
        builder.LineTo(glyph.Left + 6.8f, glyph.Bottom - 5f);
        builder.LineTo(glyph.Right - 3.8f, glyph.Top + 5f);

        using var path = builder.Snapshot();
        using var paint = new SKPaint
        {
            Color = GlyphMarkColor,
            IsAntialias = true,
            Style = SKPaintStyle.Stroke,
            StrokeWidth = 2f,
            StrokeCap = SKStrokeCap.Round,
            StrokeJoin = SKStrokeJoin.Round,
        };

        canvas.DrawPath(path, paint);
    }
}
