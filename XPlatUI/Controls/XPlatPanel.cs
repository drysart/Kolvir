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

    private readonly XPlatScrollBars _scroll;
    private readonly List<DockedChild> _docked = [];
    private bool _layingOut;

    /// <summary>A docked child, and where it sits in the declaration order.</summary>
    /// <remarks>
    /// The order is what a container needs to merge the docked children with an ordered run of
    /// other children without disturbing the order they paint in.
    /// </remarks>
    protected readonly struct DockedChild
    {
        public DockedChild(XPlatControl control, int order)
        {
            Control = control;
            Order = order;
        }

        public XPlatControl Control { get; }

        public int Order { get; }
    }

    public XPlatPanel()
    {
        _scroll = new XPlatScrollBars(this);
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
        get => _scroll.Horizontal;
        set => _scroll.Horizontal = value;
    }

    /// <summary>How the vertical axis scrolls. Defaults to <see cref="XPlatScrollStyle.None"/>.</summary>
    public XPlatScrollStyle VerticalScroll
    {
        get => _scroll.Vertical;
        set => _scroll.Vertical = value;
    }

    /// <summary>The thickness reserved for a visible scrollbar, in device independent pixels.</summary>
    public float ScrollBarThickness
    {
        get => _scroll.Thickness;
        set => _scroll.Thickness = value;
    }

    /// <summary>The horizontal scrollbar, or null until the panel first needs one.</summary>
    public XPlatScrollBar? HorizontalScrollBar => _scroll.HorizontalBar;

    /// <summary>The vertical scrollbar, or null until the panel first needs one.</summary>
    public XPlatScrollBar? VerticalScrollBar => _scroll.VerticalBar;

    /// <summary>The current horizontal scroll offset, in pixels. Zero when not scrolled.</summary>
    public float HorizontalOffset => _scroll.HorizontalOffset;

    /// <summary>The current vertical scroll offset, in pixels. Zero when not scrolled.</summary>
    public float VerticalOffset => _scroll.VerticalOffset;

    /// <summary>
    /// The total size of the scrollable content, measured from the top-left of
    /// <see cref="ContentArea"/>. Never smaller than the content area.
    /// </summary>
    public SKSize ScrollExtent => _scroll.Extent;

    /// <summary>The client area left over once the visible scrollbars are accounted for.</summary>
    public SKRect ViewportRect => _scroll.Viewport;

    /// <summary>True when there is more content below the viewport.</summary>
    public bool CanScrollVertically => _scroll.CanScrollVertically;

    /// <summary>True when there is more content to the right of the viewport.</summary>
    public bool CanScrollHorizontally => _scroll.CanScrollHorizontally;

    private SKRect ContentArea => _scroll.ContentArea;

    private float MaxHorizontalOffset => _scroll.MaxHorizontalOffset;

    private float MaxVerticalOffset => _scroll.MaxVerticalOffset;

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

        if (VerticalScroll == XPlatScrollStyle.Always)
        {
            width += ScrollBarThickness;
        }

        if (HorizontalScroll == XPlatScrollStyle.Always)
        {
            height += ScrollBarThickness;
        }

        return new SKSize(width, height);
    }

    /// <summary>Arranges the docked children and then the scrollable content.</summary>
    protected override void OnLayout()
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

        _scroll.ScrollRectIntoView(XPlatGeometry.Inflate(control.Bounds, control.Margin));
    }

    /// <summary>Scrolls to the given offsets, clamped to the scrollable range.</summary>
    public void ScrollTo(float horizontalOffset, float verticalOffset) =>
        _scroll.ScrollTo(horizontalOffset, verticalOffset);

    /// <summary>Scrolls by a relative amount, clamped to the scrollable range.</summary>
    public void ScrollBy(float horizontalDelta, float verticalDelta) =>
        _scroll.ScrollBy(horizontalDelta, verticalDelta);

    /// <summary>The scrollbars sit above the content and are never part of the scrolled area.</summary>
    protected internal override SKPoint GetChildOffset(XPlatControl child) =>
        _scroll.IsBar(child) ? SKPoint.Empty : _scroll.ChildOffset;

    /// <summary>The area scrolled content is clipped to, in this panel's client coordinates.</summary>
    protected SKRect ContentViewport => _scroll.EffectiveViewport(ClientRect);

    /// <summary>The translation scrolled content is painted at, which is the negated offset.</summary>
    protected SKPoint ContentOffset => _scroll.ChildOffset;

    /// <summary>The docked children the last layout pass placed, in declaration order.</summary>
    protected IReadOnlyList<DockedChild> DockedChildren => _docked;

    /// <summary>Clips a child's invalidation to the viewport the child is painted into.</summary>
    /// <remarks>
    /// Scrolled content is clipped to the viewport while painting, so an area outside it cannot
    /// have changed anything on screen. The scrollbars are the exception: they live in the gutter
    /// the viewport excludes and are painted over the content, so their areas are left alone.
    /// </remarks>
    protected internal override void ChildInvalidate(XPlatControl child, SKRegion region)
    {
        _scroll.ClipInvalidate(child, region, ClientRect);
        base.ChildInvalidate(child, region);
    }

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

        if (_scroll.VerticalBar?.HitTest(local) is { } verticalHit)
        {
            return verticalHit;
        }

        if (_scroll.HorizontalBar?.HitTest(local) is { } horizontalHit)
        {
            return horizontalHit;
        }

        // Children are clipped to the viewport, so a point in the bar gutter (or the dead square
        // where the two bars meet) must not reach them even though it is inside the client area.
        if (!XPlatGeometry.Contains(_scroll.EffectiveViewport(ClientRect), local))
        {
            return Enabled ? this : null;
        }

        return HitTestChildren(local) ?? (Enabled ? this : null);
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

        if (e.Handled || _scroll.Viewport.Width <= 0f || _scroll.Viewport.Height <= 0f)
        {
            return;
        }

        var shift = (e.Modifiers & XPlatModifiers.Shift) != 0;
        var canScrollVertically = MaxVerticalOffset > ScrollEpsilon;
        var canScrollHorizontally = MaxHorizontalOffset > ScrollEpsilon;

        if (canScrollVertically && !(shift && canScrollHorizontally))
        {
            var before = VerticalOffset;
            ScrollBy(0f, -e.Delta);
            e.Handled = MathF.Abs(VerticalOffset - before) > ScrollEpsilon;
            return;
        }

        if (canScrollHorizontally)
        {
            var before = HorizontalOffset;
            ScrollBy(-e.Delta, 0f);
            e.Handled = MathF.Abs(HorizontalOffset - before) > ScrollEpsilon;
        }
    }

    /// <summary>Clips the scrolled children to the viewport, then paints the scrollbars on top.</summary>
    protected override void RenderChildren(SKCanvas canvas)
    {
        canvas.Save();
        canvas.ClipRect(ContentViewport);

        var offset = _scroll.ChildOffset;
        if (offset.X != 0f || offset.Y != 0f)
        {
            canvas.Translate(offset.X, offset.Y);
        }

        RenderScrolledChildren(canvas);
        canvas.Restore();

        _scroll.RenderBars(canvas);
    }

    /// <summary>Paints the children into the clipped, translated canvas.</summary>
    /// <remarks>
    /// The canvas arrives already clipped to the viewport and translated by the content offset, so
    /// children are painted in content coordinates. A container whose children are laid out in a
    /// known order can override this to paint only the ones that can be seen.
    /// </remarks>
    protected virtual void RenderScrolledChildren(SKCanvas canvas) => base.RenderChildren(canvas);

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _scroll.Dispose();
        }

        base.Dispose(disposing);
    }

    private void LayoutCore() => _scroll.Layout(ClientRect, Padding, ArrangeChildren);

    /// <summary>
    /// Positions the children and returns the size of the content the panel scrolls. The scrollbars
    /// call this once per visibility pass, so it has to be idempotent.
    /// </summary>
    /// <param name="area">The content area, with the padding and any visible scrollbar removed.</param>
    protected virtual SKSize ArrangeChildren(SKRect area)
    {
        LayoutDockedChildren(area);
        SizeFreeChildren(area);
        return MeasureExtent(area);
    }

    /// <summary>Docks the docked children into <paramref name="area"/>, in declaration order.</summary>
    /// <returns>What is left of <paramref name="area"/> once the docked children have taken their edges.</returns>
    protected SKRect LayoutDockedChildren(SKRect area)
    {
        _docked.Clear();

        if (!HasChildren)
        {
            return area;
        }

        var remaining = area;

        // Docking is applied in declaration order: the first docked child gets the edge,
        // subsequent ones fill the space that is left over. Fill children are applied last
        // so their position in the list does not matter.
        List<int>? fillChildren = null;
        for (int i = 0; i < Controls.Count; i++)
        {
            var child = Controls[i];
            if (!child.Visible || child.Dock == DockStyle.None)
            {
                continue;
            }

            _docked.Add(new DockedChild(child, i));

            if (child.Dock == DockStyle.Fill)
            {
                (fillChildren ??= []).Add(i);
                continue;
            }

            remaining = DockChild(child, remaining);
        }

        if (fillChildren is not null)
        {
            foreach (var index in fillChildren)
            {
                var child = Controls[index];
                child.Bounds = XPlatGeometry.Deflate(remaining, child.Margin);
            }
        }

        return remaining;
    }

    /// <summary>Sizes the freely positioned children that ask to be sized to their content.</summary>
    private void SizeFreeChildren(SKRect area)
    {
        if (!HasChildren)
        {
            return;
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

    /// <summary>
    /// Measures the scrollable content as the union of the freely positioned children, from the
    /// top-left of <paramref name="area"/>. Docked children stay pinned to the viewport, so they are
    /// not part of it.
    /// </summary>
    protected SKSize MeasureExtent(SKRect area)
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
