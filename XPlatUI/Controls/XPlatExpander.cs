using SkiaSharp;

namespace XPlatUI;

/// <summary>
/// A collapsible section: a header with a chevron and a caption, and the content that folds away
/// under it. It is the shape a settings page is made of, where <see cref="XPlatGroupBox"/> is the
/// static version of the same idea.
/// </summary>
/// <remarks>
/// <para>
/// It is an <see cref="XPlatPanel"/>, so the content keeps the docking, scrolling and child
/// management of one. The header band is taken out of the content area by overriding
/// <see cref="XPlatPanel.ContentBounds"/> rather than by adding to the padding, which is what keeps
/// a scrolled child from ever being arranged or painted under the caption.
/// </para>
/// <para>
/// Collapsing folds the control itself rather than only hiding the content: it remembers the height
/// it had and shrinks to its header, and expanding puts that height back. An
/// <see cref="XPlatControl.AutoSize"/> expander needs none of that - it measures as its header when
/// collapsed - so the two agree without the control having to guess which one it is.
/// </para>
/// <para>
/// The expand and collapse is a jump, deliberately. Animating it is
/// <c>XPlatUI.Feature.Animations</c> work, and the control does not reference that package: an
/// application that wants the fade or slide opts into it the way it opts into any other feature.
/// </para>
/// </remarks>
public class XPlatExpander : XPlatPanel
{
    private const float ChevronWidth = 18f;

    private Action<object>? _expandedChanged;
    private bool _expanded = true;
    private bool _headerHot;
    private bool _headerPressed;
    private float _expandedHeight;

    public XPlatExpander()
    {
        ShowFrame = true;
        IsCard = true;
        BorderWidth = 1f;
        CornerRadius = 8f;
        Padding = new XPlatThickness(12f, 0f, 12f, 12f);
        TabStop = true;
        Cursor = XPlatCursor.Hand;
    }

    /// <summary>Creates an expander whose header reads <paramref name="header"/>.</summary>
    public XPlatExpander(string header)
        : this()
    {
        Text = header;
    }

    /// <summary>Raised after <see cref="IsExpanded"/> changed.</summary>
    public IDisposable AddExpandedChangedHandler(Action<object> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        _expandedChanged += handler;
        return new XPlatHandlerToken<Action<object>>(RemoveExpandedChangedHandler, handler);
    }

    /// <summary>Removes a handler that <see cref="AddExpandedChangedHandler"/> added.</summary>
    public void RemoveExpandedChangedHandler(Action<object> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        _expandedChanged -= handler;
    }

    /// <summary>
    /// Whether the content is showing. Collapsing shrinks the control to its header and remembers
    /// the height it had; expanding restores it.
    /// </summary>
    public bool IsExpanded
    {
        get => _expanded;
        set
        {
            if (_expanded == value)
            {
                return;
            }

            _expanded = value;

            if (value)
            {
                if (_expandedHeight > HeaderHeight)
                {
                    Height = _expandedHeight;
                }
            }
            else if (Height > HeaderHeight)
            {
                _expandedHeight = Height;
                Height = HeaderHeight;
            }

            PerformLayout();
            Invalidate();
            _expandedChanged?.Invoke(this);
        }
    }

    /// <summary>The height the content is showing at, which collapsing remembers and expanding restores.</summary>
    public float ExpandedHeight => _expandedHeight > HeaderHeight ? _expandedHeight : Height;

    /// <summary>The height of the header band: the caption's line plus its vertical padding.</summary>
    protected float HeaderHeight => MathF.Ceiling(Font.LineHeight) + 18f;

    /// <summary>The area the content is laid out in, which starts below the header.</summary>
    protected override SKRect ContentBounds =>
        new(ClientRect.Left, ClientRect.Top + HeaderHeight, ClientRect.Right, ClientRect.Bottom);

    /// <summary>An expander's own header is emphasized until the theme or an ancestor says otherwise.</summary>
    protected override XPlatFont DefaultFont => XPlatTheme.EmphasizedFont;

    protected override SKSize MeasureContent(SKSize availableSize)
    {
        float header = HeaderHeight;
        float headerWidth = ChevronWidth + 2f +
                            XPlatTextRenderer.Measure(Text, Font, float.PositiveInfinity, wrap: false).Width;

        if (!_expanded)
        {
            return new SKSize(headerWidth, header);
        }

        var content = base.MeasureContent(new SKSize(availableSize.Width, MathF.Max(0f, availableSize.Height - header)));
        return new SKSize(MathF.Max(headerWidth, content.Width), content.Height + header);
    }

    protected override SKSize ArrangeChildren(SKRect area)
    {
        if (_expanded)
        {
            return base.ArrangeChildren(area);
        }

        // Folded, the content still exists - so it shows again on expand - but it occupies nothing:
        // zeroing the rectangles is what stops a child reporting the position it had the last time
        // the section was open. Painting and hit testing skip it separately, so nothing is walked.
        foreach (var child in Controls)
        {
            child.Bounds = new SKRect(area.Left, area.Top, area.Right, area.Top);
        }

        return SKSize.Empty;
    }

    /// <summary>A folded expander paints nothing of its content, so the walk is skipped entirely.</summary>
    protected override void RenderScrolledChildren(SKCanvas canvas)
    {
        if (_expanded)
        {
            base.RenderScrolledChildren(canvas);
        }
    }

    /// <summary>While folded the only thing under the pointer is the header, which is this control.</summary>
    protected override XPlatControl? HitTestChildren(SKPoint local) =>
        _expanded ? base.HitTestChildren(local) : null;

    protected override void OnPaintBackground(SKCanvas canvas, SKRect rect)
    {
        base.OnPaintBackground(canvas, rect);

        if (!_headerHot && !_headerPressed)
        {
            return;
        }

        var header = new SKRect(rect.Left, rect.Top, rect.Right, rect.Top + HeaderHeight);
        var fill = _headerPressed ? XPlatTheme.SecondaryPressed : XPlatTheme.SecondaryHover;
        XPlatPainter.Fill(canvas, header, fill);
    }

    protected override void OnPaint(SKCanvas canvas, SKRect rect)
    {
        var color = Enabled ? ForeColor : XPlatTheme.DisabledForeground;
        float headerBottom = rect.Top + HeaderHeight;
        var chevron = new SKRect(rect.Left + Padding.Left, rect.Top, rect.Left + Padding.Left + ChevronWidth, headerBottom);

        PaintChevron(canvas, chevron, color);

        var caption = new SKRect(
            chevron.Right + 2f,
            rect.Top,
            rect.Right - Padding.Right,
            headerBottom);

        if (!string.IsNullOrEmpty(Text) && caption.Width > 0f)
        {
            XPlatTextRenderer.Draw(canvas, Text, Font, color, caption, XPlatContentAlignment.MiddleLeft);
        }

        if (_expanded)
        {
            // The hairline separates the header from the content; folded, the panel's own border is
            // already the bottom edge and a second line would read as a gap.
            XPlatPainter.Fill(
                canvas,
                new SKRect(rect.Left + 1f, headerBottom - 0.5f, rect.Right - 1f, headerBottom + 0.5f),
                XPlatTheme.Border);
        }
    }

    protected override void OnMouseDown(XPlatMouseEventArgs e)
    {
        base.OnMouseDown(e);

        if (e.Button != XPlatMouseButtons.Left || !IsInHeader(e.Location))
        {
            return;
        }

        _headerPressed = true;
        CaptureMouse();
        Invalidate();
    }

    protected override void OnMouseUp(XPlatMouseEventArgs e)
    {
        base.OnMouseUp(e);

        if (e.Button != XPlatMouseButtons.Left || !_headerPressed)
        {
            return;
        }

        _headerPressed = false;
        ReleaseMouseCapture();
        Invalidate();

        if (IsInHeader(e.Location))
        {
            IsExpanded = !IsExpanded;
        }
    }

    protected override void OnMouseMove(XPlatMouseEventArgs e)
    {
        bool hot = IsInHeader(e.Location);
        if (hot != _headerHot)
        {
            _headerHot = hot;
            Invalidate();
        }

        // Dragging off the header cancels the press, the way it does on a button.
        if (_headerPressed && !hot)
        {
            _headerPressed = false;
            ReleaseMouseCapture();
            Invalidate();
        }

        base.OnMouseMove(e);
    }

    protected override void OnMouseLeave()
    {
        _headerHot = false;

        if (_headerPressed)
        {
            _headerPressed = false;
            ReleaseMouseCapture();
        }

        base.OnMouseLeave();
    }

    protected override void OnKeyDown(XPlatKeyEventArgs e)
    {
        base.OnKeyDown(e);

        if (e.Handled || !Enabled)
        {
            return;
        }

        switch (e.Key)
        {
            case XPlatKey.Enter:
            case XPlatKey.Space:
                IsExpanded = !IsExpanded;
                e.Handled = true;
                break;

            case XPlatKey.Right when !IsExpanded:
                IsExpanded = true;
                e.Handled = true;
                break;

            case XPlatKey.Left when IsExpanded:
                IsExpanded = false;
                e.Handled = true;
                break;
        }
    }

    protected override void OnGotFocus()
    {
        base.OnGotFocus();
        Invalidate();
    }

    protected override void OnLostFocus()
    {
        base.OnLostFocus();

        if (_headerPressed)
        {
            _headerPressed = false;
            ReleaseMouseCapture();
        }

        Invalidate();
    }

    protected override void OnEnabledChanged()
    {
        if (!Enabled)
        {
            _headerHot = false;
            _headerPressed = false;
            ReleaseMouseCapture();
        }

        base.OnEnabledChanged();
    }

    protected override void OnPaintBorder(SKCanvas canvas, SKRect rect)
    {
        base.OnPaintBorder(canvas, rect);

        if (IsFocused && Enabled)
        {
            var header = new SKRect(rect.Left, rect.Top, rect.Right, rect.Top + HeaderHeight);
            XPlatPainter.DrawFocusRing(canvas, header, XPlatTheme.FocusRing, 2f, CornerRadius);
        }
    }

    private bool IsInHeader(SKPoint point) => point.Y < HeaderHeight;

    private void PaintChevron(SKCanvas canvas, SKRect cell, SKColor color)
    {
        float centerX = cell.MidX;
        float centerY = cell.MidY;
        float half = 3.5f;

        using var builder = new SKPathBuilder();
        if (_expanded)
        {
            builder.MoveTo(centerX - half, centerY - (half * 0.5f));
            builder.LineTo(centerX + half, centerY - (half * 0.5f));
            builder.LineTo(centerX, centerY + (half * 0.7f));
        }
        else
        {
            builder.MoveTo(centerX - (half * 0.5f), centerY - half);
            builder.LineTo(centerX + (half * 0.7f), centerY);
            builder.LineTo(centerX - (half * 0.5f), centerY + half);
        }

        using var path = builder.Snapshot();
        using var paint = new SKPaint
        {
            Color = color,
            IsAntialias = true,
            Style = SKPaintStyle.Stroke,
            StrokeWidth = 1.6f,
            StrokeCap = SKStrokeCap.Round,
            StrokeJoin = SKStrokeJoin.Round,
        };

        canvas.DrawPath(path, paint);
    }
}
