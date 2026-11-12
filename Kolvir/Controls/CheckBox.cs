using SkiaSharp;

namespace Kolvir;

/// <summary>
/// A check box: a square glyph followed by a label, checked and unchecked by a click or the space
/// bar. Set <see cref="ToggleControl.ThreeState"/> for the indeterminate state as well.
/// </summary>
public class CheckBox : ToggleControl
{
    public CheckBox()
    {
    }

    public CheckBox(string text)
        : this()
    {
        Text = text;
    }

    protected override void PaintGlyph(SKCanvas canvas, SKRect glyph)
    {
        const float Radius = 3f;

        bool marked = CheckState != CheckState.Unchecked;

        if (marked)
        {
            Painter.Fill(canvas, glyph, GlyphFillColor, Radius);
        }
        else if (IsMouseOver && Enabled)
        {
            Painter.Fill(canvas, glyph, Theme.Secondary, Radius);
        }

        Painter.DrawBorder(canvas, glyph, GlyphBorderColor, 1.5f, Radius);

        if (!marked)
        {
            return;
        }

        if (CheckState == CheckState.Indeterminate)
        {
            var bar = new SKRect(glyph.Left + 4f, glyph.MidY - 1f, glyph.Right - 4f, glyph.MidY + 1f);
            Painter.Fill(canvas, bar, GlyphMarkColor, 1f);
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
