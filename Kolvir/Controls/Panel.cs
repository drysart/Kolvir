using SkiaSharp;

namespace Kolvir;

/// <summary>
/// A container control that hosts child <see cref="Control"/> instances.
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
/// Only children whose <see cref="Control.Dock"/> is <see cref="DockStyle.None"/> contribute
/// to <see cref="ScrollExtent"/>; docked children stay pinned to the viewport and never scroll.
/// Content is measured from the top-left of the viewport, so children positioned to the left of
/// or above it are clipped rather than scrolled to.
/// </para>
/// </remarks>
public class Panel : Control
{
    private const float ScrollEpsilon = 0.5f;

    private readonly ScrollBars _scroll;
    private readonly List<DockedChild> _docked = [];
    private bool _layingOut;
    private float _scaleFactor = 1f;

    /// <summary>A docked child, and where it sits in the declaration order.</summary>
    /// <remarks>
    /// The order is what a container needs to merge the docked children with an ordered run of
    /// other children without disturbing the order they paint in.
    /// </remarks>
    protected readonly struct DockedChild
    {
        public DockedChild(Control control, int order)
        {
            Control = control;
            Order = order;
        }

        public Control Control { get; }

        public int Order { get; }
    }

    public Panel()
    {
        _scroll = new ScrollBars(this);
        BorderWidth = 1f;
        CornerRadius = 8f;
        Padding = new Thickness(8f);
    }

    /// <summary>A framed panel is a raised surface; a plain grouping container is transparent.</summary>
    protected override Background DefaultBackground =>
        ShowFrame ? Background.FromThemeColor(ThemeColor.Surface) : Background.None;

    /// <summary>When true the panel paints its background and border. Set false for a plain grouping container.</summary>
    public bool ShowFrame { get; set; } = true;

    /// <summary>When true the panel paints itself like a card: filled surface plus a 1px border.</summary>
    public bool IsCard { get; set; } = true;

    /// <summary>
    /// The multiplier applied to the effective scale factor of the subtree under this panel.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This is a <b>multiplier on the effective scale of the subtree</b>, not an absolute device
    /// scale: it is not the logical-to-physical pixel ratio that <see cref="Window.ScaleFactor"/>
    /// and <see cref="IWindowBackend.ScaleFactor"/> report. Multipliers compose down the tree,
    /// so a panel at 2.0 inside a panel at 0.5 behaves exactly as an unzoomed one.
    /// </para>
    /// <para>
    /// The default, 1.0, leaves the panel exactly as it was. At 2.0 the logical space the children
    /// are measured and arranged in is halved, and they are painted at twice the size; a docked
    /// <see cref="DockStyle.Fill"/> child still fills the panel, while the fixed metrics inside it
    /// - fonts, paddings, a 20 px button - double on screen. At 0.5 the space doubles and the
    /// children are painted at half size.
    /// </para>
    /// <para>
    /// The panel's own chrome does not zoom: its border, padding, corner radius and scrollbars keep
    /// their size, and only the subtree is affected. The value must be finite and greater than zero.
    /// </para>
    /// </remarks>
    /// <exception cref="ArgumentOutOfRangeException">
    /// The value is not finite, or is zero or negative.
    /// </exception>
    public float ScaleFactor
    {
        get => _scaleFactor;
        set
        {
            if (float.IsNaN(value) || float.IsInfinity(value) || value <= 0f)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(value),
                    value,
                    "The scale factor must be a finite, positive number.");
            }

            if (_scaleFactor == value)
            {
                return;
            }

            _scaleFactor = value;
            PerformLayout();
            Invalidate();
        }
    }

    /// <summary>How the horizontal axis scrolls. Defaults to <see cref="ScrollStyle.None"/>.</summary>
    public ScrollStyle HorizontalScroll
    {
        get => _scroll.Horizontal;
        set => _scroll.Horizontal = value;
    }

    /// <summary>How the vertical axis scrolls. Defaults to <see cref="ScrollStyle.None"/>.</summary>
    public ScrollStyle VerticalScroll
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
    public ScrollBar? HorizontalScrollBar => _scroll.HorizontalBar;

    /// <summary>The vertical scrollbar, or null until the panel first needs one.</summary>
    public ScrollBar? VerticalScrollBar => _scroll.VerticalBar;

    /// <summary>
    /// The current horizontal scroll offset, in the content space the children are laid out in
    /// (the panel's scrolled space divided by <see cref="ScaleFactor"/>). Zero when not scrolled.
    /// </summary>
    public float HorizontalOffset => ToContentSpace(_scroll.HorizontalOffset);

    /// <summary>
    /// The current vertical scroll offset, in the content space the children are laid out in
    /// (the panel's scrolled space divided by <see cref="ScaleFactor"/>). Zero when not scrolled.
    /// </summary>
    public float VerticalOffset => ToContentSpace(_scroll.VerticalOffset);

    /// <summary>
    /// The total size of the scrollable content, in the same content space the children are laid
    /// out in. Never smaller than the content area.
    /// </summary>
    public SKSize ScrollExtent
    {
        get
        {
            var extent = _scroll.Extent;
            return _scaleFactor == 1f
                ? extent
                : new SKSize(extent.Width / _scaleFactor, extent.Height / _scaleFactor);
        }
    }

    /// <summary>The client area left over once the visible scrollbars are accounted for.</summary>
    public SKRect ViewportRect => _scroll.Viewport;

    /// <summary>True when there is more content below the viewport.</summary>
    public bool CanScrollVertically => _scroll.CanScrollVertically;

    /// <summary>True when there is more content to the right of the viewport.</summary>
    public bool CanScrollHorizontally => _scroll.CanScrollHorizontally;

    private SKRect ContentArea => _scroll.ContentArea;

    private float MaxHorizontalOffset => ToContentSpace(_scroll.MaxHorizontalOffset);

    private float MaxVerticalOffset => ToContentSpace(_scroll.MaxVerticalOffset);

    /// <summary>The zoom this panel applies to its children. Not the device pixel ratio.</summary>
    protected internal override float ChildScaleMultiplier => _scaleFactor;

    protected override SKSize MeasureContent(SKSize availableSize)
    {
        var scale = _scaleFactor;

        // The children are measured in their own, zoomed space, so the room they are offered is
        // the panel's content space divided by the multiplier, and what they ask for is turned
        // back into panel space on the way out. Without both halves an autosized zoomed panel
        // collapses to a fraction of its content.
        var childSpace = scale == 1f
            ? availableSize
            : new SKSize(availableSize.Width / scale, availableSize.Height / scale);

        float width = 0f;
        float height = 0f;

        foreach (var child in Controls)
        {
            if (!child.Visible || child.Dock == DockStyle.Fill)
            {
                continue;
            }

            var childSize = child.Measure(childSpace);
            width = MathF.Max(width, childSize.Width + child.Margin.Horizontal);
            height += childSize.Height + child.Margin.Vertical;
        }

        if (scale != 1f)
        {
            width *= scale;
            height *= scale;
        }

        // The bars belong to the panel, not the subtree, so their thickness is never zoomed.
        if (VerticalScroll == ScrollStyle.Always)
        {
            width += ScrollBarThickness;
        }

        if (HorizontalScroll == ScrollStyle.Always)
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
    public void ScrollControlIntoView(Control control)
    {
        ArgumentNullException.ThrowIfNull(control);

        if (control.Dock != DockStyle.None)
        {
            return;
        }

        // Child bounds and margins are in the content space; the scroll helper works in the panel
        // space the bars live in, so the rectangle is zoomed on the way in.
        _scroll.ScrollRectIntoView(ToPanelSpace(Geometry.Inflate(control.Bounds, control.Margin)));
    }

    /// <summary>Scrolls to the given offsets, given in the content space the children use.</summary>
    public void ScrollTo(float horizontalOffset, float verticalOffset) =>
        _scroll.ScrollTo(ToPanelSpace(horizontalOffset), ToPanelSpace(verticalOffset));

    /// <summary>Scrolls by a relative amount, given in the content space the children use.</summary>
    public void ScrollBy(float horizontalDelta, float verticalDelta) =>
        _scroll.ScrollBy(ToPanelSpace(horizontalDelta), ToPanelSpace(verticalDelta));

    /// <summary>
    /// The scroll translation applied to a child, in the child's own (content) space. The
    /// scrollbars sit above the content and are never part of the scrolled area, so they get none.
    /// </summary>
    protected internal override SKPoint GetChildOffset(Control child) =>
        _scroll.IsBar(child) ? SKPoint.Empty : ContentOffset;

    /// <summary>
    /// The zoom applied to a child's coordinates. The scrollbars are panel-space chrome, exactly
    /// like the border and the padding: they are positioned, painted and hit tested without the
    /// multiplier, so a click that lands on a bar must arrive as the panel-space point the bar laid
    /// itself out in. Every other child is in the zoomed content space.
    /// </summary>
    protected internal override float GetChildScaleFactor(Control child) =>
        _scroll.IsBar(child) ? 1f : _scaleFactor;

    /// <summary>The area scrolled content is clipped to, in this panel's client coordinates.</summary>
    protected SKRect ContentViewport => _scroll.EffectiveViewport(ContentBounds);

    /// <summary>
    /// The same viewport, expressed in the content space the children are laid out in. A zoomed
    /// panel's viewport covers less content than it does panel space.
    /// </summary>
    protected SKRect ContentViewportInContentSpace
    {
        get
        {
            var viewport = ContentViewport;
            return _scaleFactor == 1f
                ? viewport
                : new SKRect(
                    viewport.Left / _scaleFactor,
                    viewport.Top / _scaleFactor,
                    viewport.Right / _scaleFactor,
                    viewport.Bottom / _scaleFactor);
        }
    }

    /// <summary>
    /// The part of the client area the content is laid out, clipped and hit-tested inside.
    /// </summary>
    /// <remarks>
    /// A container that reserves some of its client area overrides this rather than its padding -
    /// an <see cref="GroupBox"/> keeping the band its caption sits in - so the viewport, the
    /// scrollbars and the clipping all move with the content instead of only the layout.
    /// </remarks>
    protected virtual SKRect ContentBounds => ClientRect;

    /// <summary>The translation scrolled content is painted at, in the children's own space.</summary>
    protected SKPoint ContentOffset
    {
        get
        {
            var offset = _scroll.ChildOffset;
            return _scaleFactor == 1f
                ? offset
                : new SKPoint(offset.X / _scaleFactor, offset.Y / _scaleFactor);
        }
    }

    /// <summary>The docked children the last layout pass placed, in declaration order.</summary>
    protected IReadOnlyList<DockedChild> DockedChildren => _docked;

    /// <summary>Clips a child's invalidation to the viewport the child is painted into.</summary>
    /// <remarks>
    /// <para>
    /// Scrolled content is clipped to the viewport while painting, so an area outside it cannot
    /// have changed anything on screen. The scrollbars are the exception: they live in the gutter
    /// the viewport excludes and are painted over the content, so their areas are left alone.
    /// </para>
    /// <para>
    /// The region arrives in the panel's own space but was built from the child's coordinates and
    /// the content offset, which are both content-space values, so a zoomed panel multiplies it by
    /// <see cref="ScaleFactor"/> and rounds each rectangle outwards. A fractional zoom would
    /// otherwise leave a stale edge one pixel wide.
    /// </para>
    /// </remarks>
    protected internal override void ChildInvalidate(Control child, SKRegion region)
    {
        if (_scaleFactor == 1f || _scroll.IsBar(child))
        {
            _scroll.ClipInvalidate(child, region, ContentBounds);
            base.ChildInvalidate(child, region);
            return;
        }

        using var scaled = ScaleRegion(region, _scaleFactor);
        _scroll.ClipInvalidate(child, scaled, ContentBounds);
        base.ChildInvalidate(child, scaled);
    }

    /// <summary>Maps a rectangle from the content space into the panel's own space.</summary>
    private SKRect ToPanelSpace(SKRect rect) =>
        _scaleFactor == 1f
            ? rect
            : new SKRect(
                rect.Left * _scaleFactor,
                rect.Top * _scaleFactor,
                rect.Right * _scaleFactor,
                rect.Bottom * _scaleFactor);

    private float ToContentSpace(float value) => _scaleFactor == 1f ? value : value / _scaleFactor;

    private float ToPanelSpace(float value) => _scaleFactor == 1f ? value : value * _scaleFactor;

    /// <summary>Scales a region, rounding every rectangle outwards so no edge is left behind.</summary>
    private static SKRegion ScaleRegion(SKRegion region, float scale)
    {
        var scaled = new SKRegion();
        using var rects = region.CreateRectIterator();

        while (rects.Next(out var rect))
        {
            scaled.Op(
                new SKRectI(
                    (int)MathF.Floor(rect.Left * scale),
                    (int)MathF.Floor(rect.Top * scale),
                    (int)MathF.Ceiling(rect.Right * scale),
                    (int)MathF.Ceiling(rect.Bottom * scale)),
                SKRegionOperation.Union);
        }

        return scaled;
    }

    public override Control? HitTest(SKPoint pointInParent)
    {
        if (!Visible)
        {
            return null;
        }

        var local = new SKPoint(pointInParent.X - Left, pointInParent.Y - Top);
        if (!Geometry.Contains(ClientRect, local))
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
        if (!Geometry.Contains(ContentViewport, local))
        {
            return Enabled ? this : null;
        }

        // Everything above is panel space, including both bars. The children live in the zoomed
        // content space, so the point crosses that boundary last, after the viewport test.
        var contentPoint = _scaleFactor == 1f
            ? local
            : new SKPoint(local.X / _scaleFactor, local.Y / _scaleFactor);

        return HitTestChildren(contentPoint) ?? (Enabled ? this : null);
    }

    /// <summary>
    /// Scrolls the axis the wheel is asking for: vertical by default, horizontal when Shift is
    /// held, and whichever axis can still move when the preferred one cannot. The panel claims the
    /// turn only when it actually moved, so a panel already at its end hands the rest of the turn
    /// to the container around it.
    /// </summary>
    protected override void OnMouseWheel(MouseEventArgs e)
    {
        base.OnMouseWheel(e);

        if (e.Handled || _scroll.Viewport.Width <= 0f || _scroll.Viewport.Height <= 0f)
        {
            return;
        }

        var shift = (e.Modifiers & Modifiers.Shift) != 0;
        var canScrollVertically = MaxVerticalOffset > ScrollEpsilon;
        var canScrollHorizontally = MaxHorizontalOffset > ScrollEpsilon;

        if (canScrollVertically && !(shift && canScrollHorizontally))
        {
            var before = VerticalOffset;
            ScrollBy(0f, ToContentSpace(-e.Delta));
            e.Handled = MathF.Abs(VerticalOffset - before) > ScrollEpsilon;
            return;
        }

        if (canScrollHorizontally)
        {
            var before = HorizontalOffset;
            ScrollBy(ToContentSpace(-e.Delta), 0f);
            e.Handled = MathF.Abs(HorizontalOffset - before) > ScrollEpsilon;
        }
    }

    /// <summary>
    /// Clips the scrolled children to the viewport, zooms into the children's content space, then
    /// paints the scrollbars on top in panel space.
    /// </summary>
    /// <remarks>
    /// The clip comes first and in panel space, so the viewport and the bars are never zoomed. The
    /// scale is applied before the content translation, which is what makes the translation carry
    /// the children's own units: a child at content point p is drawn at
    /// <c>ScaleFactor * (p + offset)</c>.
    /// </remarks>
    protected override void RenderChildren(SKCanvas canvas)
    {
        canvas.Save();
        canvas.ClipRect(ContentViewport);

        if (_scaleFactor != 1f)
        {
            canvas.Scale(_scaleFactor);
        }

        var offset = ContentOffset;
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

    private void LayoutCore() => _scroll.Layout(ContentBounds, Padding, ArrangeInPanelSpace);

    /// <summary>
    /// Converts the panel-space area the scrollbars hand over into the content space the children
    /// are arranged in, and the extent back into panel space.
    /// </summary>
    /// <remarks>
    /// Keeping the scroll helper entirely in panel space and converting only here is deliberate:
    /// the same helper also serves an unscaled list, and its visibility pass compares the extent
    /// against a panel-space content area. Mixing the two spaces inside it is exactly where an
    /// off-by-scale bug would hide.
    /// </remarks>
    private SKSize ArrangeInPanelSpace(SKRect area)
    {
        if (_scaleFactor == 1f)
        {
            return ArrangeChildren(area);
        }

        var content = new SKRect(
            area.Left / _scaleFactor,
            area.Top / _scaleFactor,
            area.Right / _scaleFactor,
            area.Bottom / _scaleFactor);

        var extent = ArrangeChildren(content);
        return new SKSize(extent.Width * _scaleFactor, extent.Height * _scaleFactor);
    }

    /// <summary>
    /// Positions the children and returns the size of the content the panel scrolls. The scrollbars
    /// call this once per visibility pass, so it has to be idempotent.
    /// </summary>
    /// <param name="area">
    /// The content area, with the padding and any visible scrollbar removed, in the content space
    /// the children are laid out in. A zoomed panel divides the panel space by
    /// <see cref="ScaleFactor"/> before calling this, so the value is already the children's space.
    /// </param>
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
                child.Bounds = Geometry.Deflate(remaining, child.Margin);
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

                var bounds = Geometry.Inflate(child.Bounds, child.Margin);
                right = MathF.Max(right, bounds.Right);
                bottom = MathF.Max(bottom, bounds.Bottom);
            }
        }

        return new SKSize(MathF.Max(0f, right - area.Left), MathF.Max(0f, bottom - area.Top));
    }

    private static SKRect DockChild(Control child, SKRect remaining)
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
            Painter.Fill(canvas, rect, backColor, CornerRadius);
            return;
        }

        base.OnPaintBackground(canvas, rect);
    }

    protected override void OnPaintBorder(SKCanvas canvas, SKRect rect)
    {
        if (ShowFrame && IsCard)
        {
            Painter.DrawBorder(canvas, rect, BorderColor, BorderWidth <= 0f ? 1f : BorderWidth, CornerRadius);
            return;
        }

        base.OnPaintBorder(canvas, rect);
    }
}
