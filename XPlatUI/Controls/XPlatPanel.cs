using SkiaSharp;

namespace XPlatUI;

/// <summary>
/// A container control that hosts child <see cref="XPlatControl"/> instances.
/// Children can be docked or positioned absolutely; docked children are arranged
/// first (in reverse z-order, matching WinForms), then remaining space is left for
/// absolutely positioned children.
/// </summary>
/// <remarks>
/// <para>
/// Each axis can scroll independently through <see cref="HorizontalScroll"/> and
/// <see cref="VerticalScroll"/>. A visible scrollbar takes a gutter out of the client area
/// (classic, non-overlay style) and the remaining <see cref="ViewportRect"/> clips the children.
/// </para>
/// <para>
/// Only children whose <see cref="XPlatControl.Dock"/> is <see cref="DockStyle.None"/> contribute
/// to <see cref="ScrollExtent"/>; docked children stay pinned to the viewport and never scroll.
/// Content is measured from the top-left of the viewport, so children positioned to the left of
/// or above it are clipped rather than scrolled to.
/// </para>
/// </remarks>
public class XPlatPanel : XPlatControl
{
    private const float ScrollEpsilon = 0.5f;

    private XPlatScrollStyle _horizontalScroll = XPlatScrollStyle.None;
    private XPlatScrollStyle _verticalScroll = XPlatScrollStyle.None;
    private float _scrollBarThickness = XPlatScrollBar.DefaultThickness;

    private XPlatScrollBar? _horizontalBar;
    private XPlatScrollBar? _verticalBar;

    private float _horizontalOffset;
    private float _verticalOffset;
    private SKPoint _childOffset;
    private SKRect _viewport;
    private SKSize _scrollExtent;
    private bool _layingOut;

    public XPlatPanel()
    {
        BorderWidth = 1f;
        CornerRadius = 8f;
        Padding = new XPlatThickness(8f);
    }

    /// <summary>A framed panel is a raised surface; a plain grouping container is transparent.</summary>
    protected override SKColor? DefaultBackColor => ShowFrame ? XPlatTheme.Surface : null;

    /// <summary>When true the panel paints its background and border. Set false for a plain grouping container.</summary>
    public bool ShowFrame { get; set; } = true;

    /// <summary>When true the panel paints itself like a card: filled surface plus a 1px border.</summary>
    public bool IsCard { get; set; } = true;

    /// <summary>How the horizontal axis scrolls. Defaults to <see cref="XPlatScrollStyle.None"/>.</summary>
    public XPlatScrollStyle HorizontalScroll
    {
        get => _horizontalScroll;
        set
        {
            if (_horizontalScroll == value)
            {
                return;
            }

            _horizontalScroll = value;
            PerformLayout();
            Invalidate();
        }
    }

    /// <summary>How the vertical axis scrolls. Defaults to <see cref="XPlatScrollStyle.None"/>.</summary>
    public XPlatScrollStyle VerticalScroll
    {
        get => _verticalScroll;
        set
        {
            if (_verticalScroll == value)
            {
                return;
            }

            _verticalScroll = value;
            PerformLayout();
            Invalidate();
        }
    }

    /// <summary>The thickness reserved for a visible scrollbar, in device independent pixels.</summary>
    public float ScrollBarThickness
    {
        get => _scrollBarThickness;
        set
        {
            var clamped = MathF.Max(0f, value);
            if (_scrollBarThickness == clamped)
            {
                return;
            }

            _scrollBarThickness = clamped;
            PerformLayout();
            Invalidate();
        }
    }

    /// <summary>The horizontal scrollbar, or null until the panel first needs one.</summary>
    public XPlatScrollBar? HorizontalScrollBar => _horizontalBar;

    /// <summary>The vertical scrollbar, or null until the panel first needs one.</summary>
    public XPlatScrollBar? VerticalScrollBar => _verticalBar;

    /// <summary>The current horizontal scroll offset, in pixels. Zero when not scrolled.</summary>
    public float HorizontalOffset => _horizontalOffset;

    /// <summary>The current vertical scroll offset, in pixels. Zero when not scrolled.</summary>
    public float VerticalOffset => _verticalOffset;

    /// <summary>
    /// The total size of the scrollable content, measured from the top-left of
    /// <see cref="ContentArea"/>. Never smaller than the content area.
    /// </summary>
    public SKSize ScrollExtent => _scrollExtent;

    /// <summary>The client area left over once the visible scrollbars are accounted for.</summary>
    public SKRect ViewportRect => _viewport;

    /// <summary>True when there is more content below the viewport.</summary>
    public bool CanScrollVertically => _verticalOffset < MaxVerticalOffset - ScrollEpsilon;

    /// <summary>True when there is more content to the right of the viewport.</summary>
    public bool CanScrollHorizontally => _horizontalOffset < MaxHorizontalOffset - ScrollEpsilon;

    private SKRect ContentArea => XPlatGeometry.Deflate(_viewport, Padding);

    // A None axis never scrolls, so it has no reachable offset even if the content overflows.
    private float MaxHorizontalOffset => HorizontalScroll == XPlatScrollStyle.None
        ? 0f
        : MathF.Max(0f, _scrollExtent.Width - ContentArea.Width);

    private float MaxVerticalOffset => VerticalScroll == XPlatScrollStyle.None
        ? 0f
        : MathF.Max(0f, _scrollExtent.Height - ContentArea.Height);

    protected override SKSize MeasureContent(SKSize availableSize)
    {
        float width = 0f;
        float height = 0f;

        foreach (var child in Controls)
        {
            if (!child.Visible || child.Dock == DockStyle.Fill)
            {
                continue;
            }

            var childSize = child.Measure(availableSize);
            width = MathF.Max(width, childSize.Width + child.Margin.Horizontal);
            height += childSize.Height + child.Margin.Vertical;
        }

        if (_verticalScroll == XPlatScrollStyle.Always)
        {
            width += _scrollBarThickness;
        }

        if (_horizontalScroll == XPlatScrollStyle.Always)
        {
            height += _scrollBarThickness;
        }

        return new SKSize(width, height);
    }

    /// <summary>Arranges the docked children and then the scrollable content.</summary>
    public override void PerformLayout()
    {
        if (_layingOut)
        {
            return;
        }

        _layingOut = true;
        try
        {
            LayoutCore();
        }
        finally
        {
            _layingOut = false;
        }
    }

    /// <summary>
    /// Scrolls so that <paramref name="control"/> is inside the viewport. Offsets are clamped to
    /// the scrollable range. Docked children are pinned and are ignored.
    /// </summary>
    public void ScrollControlIntoView(XPlatControl control)
    {
        ArgumentNullException.ThrowIfNull(control);

        if (control.Dock != DockStyle.None)
        {
            return;
        }

        var area = ContentArea;
        var bounds = XPlatGeometry.Inflate(control.Bounds, control.Margin);

        float horizontal = _horizontalOffset;
        float vertical = _verticalOffset;

        float left = bounds.Left - area.Left;
        float right = bounds.Right - area.Left;
        if (left < horizontal)
        {
            horizontal = left;
        }
        else if (right > horizontal + area.Width)
        {
            horizontal = right - area.Width;
        }

        float top = bounds.Top - area.Top;
        float bottom = bounds.Bottom - area.Top;
        if (top < vertical)
        {
            vertical = top;
        }
        else if (bottom > vertical + area.Height)
        {
            vertical = bottom - area.Height;
        }

        ScrollTo(horizontal, vertical);
    }

    /// <summary>Scrolls to the given offsets, clamped to the scrollable range.</summary>
    public void ScrollTo(float horizontalOffset, float verticalOffset)
    {
        var horizontal = MathF.Max(0f, horizontalOffset);
        var vertical = MathF.Max(0f, verticalOffset);

        if (MathF.Abs(horizontal - _horizontalOffset) < 0.01f && MathF.Abs(vertical - _verticalOffset) < 0.01f)
        {
            return;
        }

        _horizontalOffset = horizontal;
        _verticalOffset = vertical;
        PerformLayout();
        Invalidate();
    }

    /// <summary>Scrolls by a relative amount, clamped to the scrollable range.</summary>
    public void ScrollBy(float horizontalDelta, float verticalDelta) =>
        ScrollTo(_horizontalOffset + horizontalDelta, _verticalOffset + verticalDelta);

    /// <summary>The scrollbars sit above the content and are never part of the scrolled area.</summary>
    protected internal override SKPoint GetChildOffset(XPlatControl child) =>
        ReferenceEquals(child, _verticalBar) || ReferenceEquals(child, _horizontalBar)
            ? SKPoint.Empty
            : _childOffset;

    public override XPlatControl? HitTest(SKPoint pointInParent)
    {
        if (!Visible)
        {
            return null;
        }

        var local = new SKPoint(pointInParent.X - Left, pointInParent.Y - Top);
        if (!XPlatGeometry.Contains(ClientRect, local))
        {
            return null;
        }

        if (_verticalBar?.HitTest(local) is { } verticalHit)
        {
            return verticalHit;
        }

        if (_horizontalBar?.HitTest(local) is { } horizontalHit)
        {
            return horizontalHit;
        }

        // Children are clipped to the viewport, so a point in the bar gutter (or the dead square
        // where the two bars meet) must not reach them even though it is inside the client area.
        var viewport = _viewport.Width > 0f && _viewport.Height > 0f ? _viewport : ClientRect;
        if (!XPlatGeometry.Contains(viewport, local))
        {
            return Enabled ? this : null;
        }

        return base.HitTest(pointInParent);
    }

    /// <summary>
    /// Scrolls the axis the wheel is asking for: vertical by default, horizontal when Shift is
    /// held, and whichever axis can still move when the preferred one cannot. The panel claims the
    /// turn only when it actually moved, so a panel already at its end hands the rest of the turn
    /// to the container around it.
    /// </summary>
    protected override void OnMouseWheel(XPlatMouseEventArgs e)
    {
        base.OnMouseWheel(e);

        if (e.Handled || _viewport.Width <= 0f || _viewport.Height <= 0f)
        {
            return;
        }

        var shift = (e.Modifiers & XPlatModifiers.Shift) != 0;
        var canScrollVertically = MaxVerticalOffset > ScrollEpsilon;
        var canScrollHorizontally = MaxHorizontalOffset > ScrollEpsilon;

        if (canScrollVertically && !(shift && canScrollHorizontally))
        {
            var before = _verticalOffset;
            ScrollBy(0f, -e.Delta);
            e.Handled = MathF.Abs(_verticalOffset - before) > ScrollEpsilon;
            return;
        }

        if (canScrollHorizontally)
        {
            var before = _horizontalOffset;
            ScrollBy(-e.Delta, 0f);
            e.Handled = MathF.Abs(_horizontalOffset - before) > ScrollEpsilon;
        }
    }

    /// <summary>Clips the scrolled children to the viewport, then paints the scrollbars on top.</summary>
    protected override void RenderChildren(SKCanvas canvas)
    {
        var viewport = _viewport.Width > 0f && _viewport.Height > 0f ? _viewport : ClientRect;

        canvas.Save();
        canvas.ClipRect(viewport);

        if (_childOffset.X != 0f || _childOffset.Y != 0f)
        {
            canvas.Translate(_childOffset.X, _childOffset.Y);
        }

        base.RenderChildren(canvas);
        canvas.Restore();

        _verticalBar?.Render(canvas);
        _horizontalBar?.Render(canvas);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _horizontalBar = null;
            _verticalBar = null;
        }

        base.Dispose(disposing);
    }

    private void LayoutCore()
    {
        var client = ClientRect;

        // Bar visibility depends on the extent, the extent depends on the viewport and the
        // viewport depends on visibility, so repeat until all three agree.
        var showVertical = _verticalScroll == XPlatScrollStyle.Always;
        var showHorizontal = _horizontalScroll == XPlatScrollStyle.Always;
        var viewport = ViewportOf(client, showHorizontal, showVertical);
        var area = XPlatGeometry.Deflate(viewport, Padding);
        LayoutDockedChildren(area);
        var extent = MeasureExtent(area);

        for (int pass = 0; pass < 3; pass++)
        {
            var nextVertical = _verticalScroll switch
            {
                XPlatScrollStyle.Always => true,
                XPlatScrollStyle.Automatic => extent.Height > area.Height + ScrollEpsilon,
                _ => false,
            };

            var nextHorizontal = _horizontalScroll switch
            {
                XPlatScrollStyle.Always => true,
                XPlatScrollStyle.Automatic => extent.Width > area.Width + ScrollEpsilon,
                _ => false,
            };

            if (nextVertical == showVertical && nextHorizontal == showHorizontal)
            {
                break;
            }

            showVertical = nextVertical;
            showHorizontal = nextHorizontal;
            viewport = ViewportOf(client, showHorizontal, showVertical);
            area = XPlatGeometry.Deflate(viewport, Padding);
            LayoutDockedChildren(area);
            extent = MeasureExtent(area);
        }

        _viewport = viewport;
        _scrollExtent = extent;

        _horizontalOffset = Math.Clamp(_horizontalOffset, 0f, MaxHorizontalOffset);
        _verticalOffset = Math.Clamp(_verticalOffset, 0f, MaxVerticalOffset);
        _childOffset = new SKPoint(-_horizontalOffset, -_verticalOffset);

        PlaceScrollBar(
            ref _verticalBar,
            showVertical,
            XPlatOrientation.Vertical,
            new SKRect(viewport.Right, client.Top, client.Right, viewport.Bottom),
            area.Height,
            extent.Height,
            _verticalOffset);

        PlaceScrollBar(
            ref _horizontalBar,
            showHorizontal,
            XPlatOrientation.Horizontal,
            new SKRect(client.Left, viewport.Bottom, viewport.Right, client.Bottom),
            area.Width,
            extent.Width,
            _horizontalOffset);
    }

    private SKRect ViewportOf(SKRect client, bool showHorizontal, bool showVertical)
    {
        var thickness = MathF.Max(0f, _scrollBarThickness);
        var width = MathF.Max(0f, client.Width - (showVertical ? thickness : 0f));
        var height = MathF.Max(0f, client.Height - (showHorizontal ? thickness : 0f));

        return new SKRect(client.Left, client.Top, client.Left + width, client.Top + height);
    }

    private void LayoutDockedChildren(SKRect area)
    {
        if (!HasChildren)
        {
            return;
        }

        var remaining = area;

        // Docking is applied in declaration order: the first docked child gets the edge,
        // subsequent ones fill the space that is left over. Fill children are applied last
        // so their position in the list does not matter.
        List<XPlatControl>? fillChildren = null;
        foreach (var child in Controls)
        {
            if (!child.Visible || child.Dock == DockStyle.None)
            {
                continue;
            }

            if (child.Dock == DockStyle.Fill)
            {
                (fillChildren ??= new List<XPlatControl>()).Add(child);
                continue;
            }

            remaining = DockChild(child, remaining);
        }

        if (fillChildren is not null)
        {
            foreach (var child in fillChildren)
            {
                child.Bounds = XPlatGeometry.Deflate(remaining, child.Margin);
            }
        }

        // Freely positioned children keep their own position; only their size is resolved here.
        foreach (var child in Controls)
        {
            if (!child.Visible || child.Dock != DockStyle.None || !child.AutoSize)
            {
                continue;
            }

            var desired = child.Measure(new SKSize(area.Width, area.Height));
            child.Size = new SKSize(desired.Width, desired.Height);
        }
    }

    private SKSize MeasureExtent(SKRect area)
    {
        var right = area.Right;
        var bottom = area.Bottom;

        if (HasChildren)
        {
            foreach (var child in Controls)
            {
                if (!child.Visible || child.Dock != DockStyle.None)
                {
                    continue;
                }

                var bounds = XPlatGeometry.Inflate(child.Bounds, child.Margin);
                right = MathF.Max(right, bounds.Right);
                bottom = MathF.Max(bottom, bounds.Bottom);
            }
        }

        return new SKSize(MathF.Max(0f, right - area.Left), MathF.Max(0f, bottom - area.Top));
    }

    private void PlaceScrollBar(
        ref XPlatScrollBar? bar,
        bool visible,
        XPlatOrientation orientation,
        SKRect bounds,
        float viewportLength,
        float extentLength,
        float offset)
    {
        if (!visible)
        {
            if (bar is not null)
            {
                bar.Visible = false;
            }

            return;
        }

        if (bar is null)
        {
            bar = new XPlatScrollBar
            {
                Name = orientation == XPlatOrientation.Vertical ? "VerticalScrollBar" : "HorizontalScrollBar",
                Orientation = orientation,

                // The bar belongs to the panel, so the focus belongs to whatever the user is
                // working in. It still takes clicks and wheel turns, just never the focus.
                Focusable = false,
                Parent = this,
            };

            bar.AddScrollHandler(OnScrollBarScrolled);
            Own(bar);
        }

        var maximum = MathF.Max(0f, extentLength - viewportLength);

        bar.Visible = true;
        bar.Bounds = bounds;
        bar.Minimum = 0f;
        bar.Maximum = maximum;
        bar.LargeChange = MathF.Max(1f, viewportLength);
        bar.SmallChange = MathF.Max(1f, MathF.Min(XPlatScrollBar.DefaultThickness, maximum));
        bar.Value = MathF.Min(offset, maximum);
        bar.Enabled = maximum > 0f;
    }

    private void OnScrollBarScrolled(object sender)
    {
        if (_layingOut)
        {
            return;
        }

        if (ReferenceEquals(sender, _verticalBar))
        {
            ScrollTo(_horizontalOffset, _verticalBar!.Value);
        }
        else if (ReferenceEquals(sender, _horizontalBar))
        {
            ScrollTo(_horizontalBar!.Value, _verticalOffset);
        }
    }

    private static SKRect DockChild(XPlatControl child, SKRect remaining)
    {
        var margin = child.Margin;
        var available = new SKSize(
            MathF.Max(0f, remaining.Width - margin.Horizontal),
            MathF.Max(0f, remaining.Height - margin.Vertical));

        var desired = child.AutoSize ? child.Measure(available) : child.Size;

        switch (child.Dock)
        {
            case DockStyle.Top:
            {
                float height = child.AutoSize ? desired.Height : MathF.Max(desired.Height, 0f);
                var bounds = new SKRect(
                    remaining.Left + margin.Left,
                    remaining.Top + margin.Top,
                    remaining.Right - margin.Right,
                    remaining.Top + margin.Top + height);
                child.Bounds = bounds;
                return new SKRect(remaining.Left, bounds.Bottom + margin.Bottom, remaining.Right, remaining.Bottom);
            }

            case DockStyle.Bottom:
            {
                float height = child.AutoSize ? desired.Height : MathF.Max(desired.Height, 0f);
                var bounds = new SKRect(
                    remaining.Left + margin.Left,
                    remaining.Bottom - margin.Bottom - height,
                    remaining.Right - margin.Right,
                    remaining.Bottom - margin.Bottom);
                child.Bounds = bounds;
                return new SKRect(remaining.Left, remaining.Top, remaining.Right, bounds.Top - margin.Top);
            }

            case DockStyle.Left:
            {
                float width = child.AutoSize ? desired.Width : MathF.Max(desired.Width, 0f);
                var bounds = new SKRect(
                    remaining.Left + margin.Left,
                    remaining.Top + margin.Top,
                    remaining.Left + margin.Left + width,
                    remaining.Bottom - margin.Bottom);
                child.Bounds = bounds;
                return new SKRect(bounds.Right + margin.Right, remaining.Top, remaining.Right, remaining.Bottom);
            }

            case DockStyle.Right:
            {
                float width = child.AutoSize ? desired.Width : MathF.Max(desired.Width, 0f);
                var bounds = new SKRect(
                    remaining.Right - margin.Right - width,
                    remaining.Top + margin.Top,
                    remaining.Right - margin.Right,
                    remaining.Bottom - margin.Bottom);
                child.Bounds = bounds;
                return new SKRect(remaining.Left, remaining.Top, bounds.Left - margin.Left, remaining.Bottom);
            }

            case DockStyle.Fill:
                child.Bounds = new SKRect(
                    remaining.Left + margin.Left,
                    remaining.Top + margin.Top,
                    remaining.Right - margin.Right,
                    remaining.Bottom - margin.Bottom);
                return SKRect.Empty;

            default:
                return remaining;
        }
    }

    protected override void OnPaintBackground(SKCanvas canvas, SKRect rect)
    {
        if (ShowFrame && BackColor is { } backColor)
        {
            XPlatPainter.Fill(canvas, rect, backColor, CornerRadius);
            return;
        }

        base.OnPaintBackground(canvas, rect);
    }

    protected override void OnPaintBorder(SKCanvas canvas, SKRect rect)
    {
        if (ShowFrame && IsCard)
        {
            XPlatPainter.DrawBorder(canvas, rect, BorderColor, BorderWidth <= 0f ? 1f : BorderWidth, CornerRadius);
            return;
        }

        base.OnPaintBorder(canvas, rect);
    }
}
