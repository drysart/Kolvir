using SkiaSharp;

namespace XPlatUI.Spans;

/// <summary>
/// The whole seam between <see cref="XPlatTextBox"/> and annotated spans.
/// </summary>
/// <remarks>
/// <para>
/// A text box holds one nullable reference to this interface and nothing else. Drawing the
/// squiggles and resolving a right-click to the span under it are the only two things the control
/// has to be able to do, so both are reached through here and the implementation - and every type
/// it needs, this one included - is named only by the extension property that turns the feature on.
/// </para>
/// <para>
/// That is what keeps FEATURE-COST-POLICY.md §4's first rung reachable for a feature whose paint
/// path cannot live outside the control: an application that never writes
/// <c>using XPlatUI.Spans;</c> and never assigns <c>Spans</c> publishes without any of this. What
/// such an application does pay for is the null reference every text box carries - eight bytes and
/// a null check on the paint path - which is the same accepted cost the multi-line feature pays,
/// and the reason both use one seam rather than a member per concern.
/// </para>
/// </remarks>
internal interface IXPlatTextBoxSpanFeature
{
    /// <summary>The list the caller handed over. It is kept by reference and never copied.</summary>
    IReadOnlyList<XPlatTextBoxSpan> Spans { get; set; }

    /// <summary>Draws one squiggle per span, in the control's client coordinates.</summary>
    void Paint(XPlatTextBox textBox, SKCanvas canvas);

    /// <summary>
    /// The span whose characters contain <paramref name="pointInLocal"/>, or null when the point is
    /// outside every one of them.
    /// </summary>
    XPlatTextBoxSpan? HitTest(XPlatTextBox textBox, SKPoint pointInLocal);

    /// <summary>Releases what the feature owns. The control keeps no spans afterwards.</summary>
    void Dispose();
}

/// <summary>
/// The span state of one text box: the caller's list, and the two questions the control asks of it.
/// </summary>
/// <remarks>
/// <para>
/// One instance exists per text box that has spans, is created by the <c>Spans</c> property, and is
/// installed into the control as <c>XPlatTextBox.SpanFeature</c>. Nothing else in the library names
/// this type, so it is trimmed along with the property that creates it.
/// </para>
/// <para>
/// The horizontal geometry is not computed here. A span's characters are already the range a
/// selection covers, so the squiggle is drawn inside exactly the rectangles
/// <see cref="XPlatTextBox.GetSelectionRects"/> reports: one per visual line, clipped to the text
/// area, scrolled, and soft-wrapped in the same way as the highlight. That is what makes a span
/// that crosses a soft wrap draw on both visual lines with no second line model to keep in step.
/// </para>
/// <para>
/// The vertical position is derived from the font, never from a pixel constant, so it follows the
/// size and the display's scale factor: the wave starts one gap below the baseline and ends inside
/// the descent, which is the room the font reserves under the letters. A glyph without a descender
/// therefore never touches it, and a glyph with one crosses it - and because the control paints the
/// squiggles before the text, the descender is drawn over the wave rather than the other way round.
/// </para>
/// </remarks>
internal sealed class XPlatTextBoxSpanFeature : IXPlatTextBoxSpanFeature
{
    private readonly XPlatTextBox _textBox;
    private readonly List<SKRect> _rects = [];
    private IReadOnlyList<XPlatTextBoxSpan> _spans;

    private XPlatTextBoxSpanFeature(XPlatTextBox textBox, IReadOnlyList<XPlatTextBoxSpan> spans)
    {
        _textBox = textBox;
        _spans = spans;
    }

    /// <summary>The list the caller handed over. It is kept by reference and never copied.</summary>
    public IReadOnlyList<XPlatTextBoxSpan> Spans
    {
        get => _spans;
        set => _spans = value;
    }

    /// <summary>Turns span painting on for <paramref name="textBox"/>, which must not have it yet.</summary>
    public static XPlatTextBoxSpanFeature Attach(XPlatTextBox textBox, IReadOnlyList<XPlatTextBoxSpan> spans)
    {
        var feature = new XPlatTextBoxSpanFeature(textBox, spans);
        textBox.SpanFeature = feature;
        textBox.Invalidate();
        return feature;
    }

    /// <inheritdoc />
    public void Paint(XPlatTextBox textBox, SKCanvas canvas)
    {
        var spans = _spans;
        if (spans.Count == 0)
        {
            return;
        }

        var font = textBox.Font;
        var area = textBox.Viewport.Text;
        if (font.LineHeight <= 0f || area.Width <= 0f || area.Height <= 0f)
        {
            return;
        }

        using var paint = new SKPaint
        {
            IsAntialias = true,
            Style = SKPaintStyle.Stroke,
            StrokeCap = SKStrokeCap.Butt,
            StrokeJoin = SKStrokeJoin.Miter,
        };

        // Earlier spans are drawn first, so on an overlapping pair the later one is on top - and it
        // is the one a right-click resolves to, because HitTest walks the same list backwards.
        for (int index = 0; index < spans.Count; index++)
        {
            if (spans[index] is not { } span || span.Length == 0)
            {
                continue;
            }

            _rects.Clear();
            textBox.GetSelectionRects(span.Start, span.Length, _rects);
            if (_rects.Count == 0)
            {
                continue;
            }

            paint.Color = span.ResolveColor();
            paint.StrokeWidth = span.Thickness;

            foreach (var rect in _rects)
            {
                DrawSegment(canvas, rect, area, span, paint);
            }
        }
    }

    /// <inheritdoc />
    public XPlatTextBoxSpan? HitTest(XPlatTextBox textBox, SKPoint pointInLocal)
    {
        var spans = _spans;

        for (int index = spans.Count - 1; index >= 0; index--)
        {
            if (spans[index] is not { } span || span.Length == 0)
            {
                continue;
            }

            _rects.Clear();
            textBox.GetSelectionRects(span.Start, span.Length, _rects);

            foreach (var rect in _rects)
            {
                // The whole line height counts as the span's row, so a right-click level with the
                // top of a tall glyph still finds the word; the horizontal range is the glyphs
                // themselves, so a right-click past the end of the word finds nothing.
                if (pointInLocal.X >= rect.Left && pointInLocal.X < rect.Right &&
                    pointInLocal.Y >= rect.Top && pointInLocal.Y < rect.Bottom)
                {
                    return span;
                }
            }
        }

        return null;
    }

    /// <inheritdoc />
    public void Dispose() => _spans = [];

    /// <summary>
    /// Draws the wave for one visual-line segment of a span. The segment rectangle is what the
    /// selection would have filled, so the squiggle can never reach outside the text area: it is
    /// clipped to the rectangle, and the rectangle is already clipped and scrolled.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Vertically the wave is anchored to the baseline <em>of the line this segment belongs to</em>,
    /// and its band runs from one gap below that baseline to no further than the font's descent. So
    /// the marker sits in the space the font reserves under the letters: it clears every glyph that
    /// has no descender - the ink of which stops at the baseline, give or take a round glyph's
    /// overshoot - and a descender crosses it, which is intended. Both the gap and the descent are
    /// fractions of the font, which is already scaled for the display, so the marker follows the
    /// size and the DPI instead of drifting away from the text at other scale factors.
    /// </para>
    /// <para>
    /// A segment is a whole line box unless the viewport clipped it. Its top edge is therefore the
    /// line's top, except when the view has scrolled into the line, in which case the bottom edge is
    /// still the line's own and the top is recovered from it. Only a line box taller than the whole
    /// text area is clipped on both edges, and then the baseline is not recoverable at all: nothing
    /// is drawn rather than a marker in the wrong place.
    /// </para>
    /// </remarks>
    private void DrawSegment(
        SKCanvas canvas,
        SKRect segment,
        SKRect area,
        XPlatTextBoxSpan span,
        SKPaint paint)
    {
        if (segment.Width <= 0f || segment.Height <= 0f)
        {
            return;
        }

        var font = _textBox.Font;
        float lineHeight = font.LineHeight;

        bool topClipped = segment.Top <= area.Top + 0.5f;
        bool bottomClipped = segment.Bottom >= area.Bottom - 0.5f;
        if (topClipped && bottomClipped && segment.Height < lineHeight - 0.5f)
        {
            return;
        }

        float lineTop = topClipped ? segment.Bottom - lineHeight : segment.Top;
        float baseline = lineTop + font.BaselineOffset;

        // Clear of the baseline: a hair of the em for the slight dip of a round glyph, and never
        // less than a pixel so the marker does not fuse with the ink at small sizes.
        float gap = MathF.Max(1f, font.Size * 0.06f);

        // The descent is the room the font itself reserves under the baseline. The wave has to fit
        // in what is left of it after the gap, so a large marker is shrunk instead of pushed back
        // up into the letters.
        float halfWave = span.Amplitude;
        float descentRoom = MathF.Abs(font.SkFont.Metrics.Descent) - gap;
        if (halfWave > 0f && halfWave * 2f > descentRoom)
        {
            halfWave = MathF.Max(0.25f, descentRoom * 0.5f);
        }

        // And it has to fit in the part of the line the viewport is showing, so a line clipped by
        // the edge of the view still shows a marker at that edge rather than drawing past it.
        float visible = segment.Bottom - baseline - gap;
        if (visible <= 0.5f)
        {
            return;
        }

        if (halfWave > 0f && halfWave * 2f > visible)
        {
            halfWave = MathF.Max(0.25f, visible * 0.5f);
        }

        float top = baseline + gap;
        float centre = top + halfWave;
        float low = centre + halfWave;
        float high = centre - halfWave;

        float step = MathF.Max(1f, span.Wavelength * 0.5f);
        float left = segment.Left;
        float right = segment.Right;

        using var builder = new SKPathBuilder();
        builder.MoveTo(left, low);

        bool peak = true;
        float x = left;
        while (x + step < right)
        {
            x += step;
            builder.LineTo(x, peak ? high : low);
            peak = !peak;
        }

        builder.LineTo(right, peak ? high : low);

        using var path = builder.Snapshot();

        canvas.Save();
        canvas.ClipRect(segment);
        canvas.DrawPath(path, paint);
        canvas.Restore();
    }
}
