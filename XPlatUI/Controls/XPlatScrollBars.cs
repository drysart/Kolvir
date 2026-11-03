using SkiaSharp;

namespace XPlatUI;

/// <summary>
/// The scrollbar plumbing shared by the scrolling containers: it owns the two
/// <see cref="XPlatScrollBar"/> children, works out how much of the client area they take, and
/// keeps the offsets clamped to the range the content actually has.
/// </summary>
/// <remarks>
/// <para>
/// A visible scrollbar takes a gutter out of the client area (classic, non-overlay style) and the
/// remaining <see cref="Viewport"/> clips the content. Bar visibility depends on the extent, the
/// extent depends on the viewport and the viewport depends on visibility, so <see cref="Layout"/>
/// repeats until all three agree.
/// </para>
/// <para>
/// The container decides what the extent is by returning it from the arrange callback, which is
/// also where it positions whatever it scrolls. That is the only thing the two containers disagree
/// about: a panel measures its children, a list box measures its rows.
/// </para>
/// </remarks>
internal sealed class XPlatScrollBars
{
    private const float Epsilon = 0.5f;

    private readonly XPlatControl _owner;

    private XPlatScrollStyle _horizontal = XPlatScrollStyle.None;
    private XPlatScrollStyle _vertical = XPlatScrollStyle.None;
    private float _thickness = XPlatScrollBar.DefaultThickness;

    private XPlatScrollBar? _horizontalBar;
    private XPlatScrollBar? _verticalBar;

    private float _horizontalOffset;
    private float _verticalOffset;
    private SKPoint _childOffset;
    private SKRect _viewport;
    private SKRect _contentArea;
    private SKSize _extent;
    private bool _layingOut;

    public XPlatScrollBars(XPlatControl owner)
    {
        ArgumentNullException.ThrowIfNull(owner);
        _owner = owner;
    }

    /// <summary>The epsilon used when deciding whether an axis can still move.</summary>
    public static float ScrollEpsilon => Epsilon;

    public XPlatScrollStyle Horizontal
    {
        get => _horizontal;
        set
        {
            if (_horizontal == value)
            {
                return;
            }

            _horizontal = value;
            _owner.PerformLayout();
            _owner.Invalidate();
        }
    }

    public XPlatScrollStyle Vertical
    {
        get => _vertical;
        set
        {
            if (_vertical == value)
            {
                return;
            }

            _vertical = value;
            _owner.PerformLayout();
            _owner.Invalidate();
        }
    }

    public float Thickness
    {
        get => _thickness;
        set
        {
            var clamped = MathF.Max(0f, value);
            if (_thickness == clamped)
            {
                return;
            }

            _thickness = clamped;
            _owner.PerformLayout();
            _owner.Invalidate();
        }
    }

    public XPlatScrollBar? HorizontalBar => _horizontalBar;

    public XPlatScrollBar? VerticalBar => _verticalBar;

    public float HorizontalOffset => _horizontalOffset;

    public float VerticalOffset => _verticalOffset;

    public SKSize Extent => _extent;

    public SKRect Viewport => _viewport;

    public SKRect ContentArea => _contentArea;

    /// <summary>The translation scrolled content is painted at, which is the negated offset.</summary>
    public SKPoint ChildOffset => _childOffset;

    public bool CanScrollVertically => _verticalOffset < MaxVerticalOffset - Epsilon;

    public bool CanScrollHorizontally => _horizontalOffset < MaxHorizontalOffset - Epsilon;

    // A None axis never scrolls, so it has no reachable offset even if the content overflows.
    public float MaxHorizontalOffset => _horizontal == XPlatScrollStyle.None
        ? 0f
        : MathF.Max(0f, _extent.Width - _contentArea.Width);

    public float MaxVerticalOffset => _vertical == XPlatScrollStyle.None
        ? 0f
        : MathF.Max(0f, _extent.Height - _contentArea.Height);

    /// <summary>
    /// The area the content is clipped to. Falls back to the whole client area before the first
    /// layout has run, so a control can be hit tested or painted without one.
    /// </summary>
    public SKRect EffectiveViewport(SKRect client) =>
        _viewport.Width > 0f && _viewport.Height > 0f ? _viewport : client;

    /// <summary>True when <paramref name="child"/> is one of the bars this helper owns.</summary>
    public bool IsBar(XPlatControl child) =>
        ReferenceEquals(child, _verticalBar) || ReferenceEquals(child, _horizontalBar);

    /// <summary>
    /// Positions the bars and the content for the current client area.
    /// </summary>
    /// <param name="client">The area the bars may occupy.</param>
    /// <param name="padding">The container's padding, applied inside the viewport.</param>
    /// <param name="arrange">
    /// Called with the content area; positions whatever the container scrolls and returns the
    /// total size of that content. Called once per visibility pass, so it must be idempotent.
    /// </param>
    public void Layout(SKRect client, XPlatThickness padding, Func<SKRect, SKSize> arrange)
    {
        ArgumentNullException.ThrowIfNull(arrange);

        if (_layingOut)
        {
            return;
        }

        _layingOut = true;
        try
        {
            var showVertical = _vertical == XPlatScrollStyle.Always;
            var showHorizontal = _horizontal == XPlatScrollStyle.Always;
            var viewport = ViewportOf(client, showHorizontal, showVertical);
            var area = XPlatGeometry.Deflate(viewport, padding);
            var extent = arrange(area);

            for (int pass = 0; pass < 3; pass++)
            {
                var nextVertical = _vertical switch
                {
                    XPlatScrollStyle.Always => true,
                    XPlatScrollStyle.Automatic => extent.Height > area.Height + Epsilon,
                    _ => false,
                };

                var nextHorizontal = _horizontal switch
                {
                    XPlatScrollStyle.Always => true,
                    XPlatScrollStyle.Automatic => extent.Width > area.Width + Epsilon,
                    _ => false,
                };

                if (nextVertical == showVertical && nextHorizontal == showHorizontal)
                {
                    break;
                }

                showVertical = nextVertical;
                showHorizontal = nextHorizontal;
                viewport = ViewportOf(client, showHorizontal, showVertical);
                area = XPlatGeometry.Deflate(viewport, padding);
                extent = arrange(area);
            }

            _viewport = viewport;
            _contentArea = area;
            _extent = extent;

            _horizontalOffset = Math.Clamp(_horizontalOffset, 0f, MaxHorizontalOffset);
            _verticalOffset = Math.Clamp(_verticalOffset, 0f, MaxVerticalOffset);
            _childOffset = new SKPoint(-_horizontalOffset, -_verticalOffset);

            PlaceBar(
                ref _verticalBar,
                showVertical,
                XPlatOrientation.Vertical,
                new SKRect(viewport.Right, client.Top, client.Right, viewport.Bottom),
                area.Height,
                extent.Height,
                _verticalOffset);

            PlaceBar(
                ref _horizontalBar,
                showHorizontal,
                XPlatOrientation.Horizontal,
                new SKRect(client.Left, viewport.Bottom, viewport.Right, client.Bottom),
                area.Width,
                extent.Width,
                _horizontalOffset);
        }
        finally
        {
            _layingOut = false;
        }
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
        _owner.PerformLayout();
        _owner.Invalidate();
    }

    /// <summary>Scrolls by a relative amount, clamped to the scrollable range.</summary>
    public void ScrollBy(float horizontalDelta, float verticalDelta) =>
        ScrollTo(_horizontalOffset + horizontalDelta, _verticalOffset + verticalDelta);

    /// <summary>
    /// Scrolls so that a rectangle, expressed in content coordinates, is inside the viewport.
    /// </summary>
    public void ScrollRectIntoView(SKRect bounds)
    {
        var area = _contentArea;
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

    /// <summary>Clips an invalidation coming from scrolled content to the viewport.</summary>
    /// <remarks>
    /// Scrolled content is clipped to the viewport while painting, so an area outside it cannot
    /// have changed anything on screen. The bars are the exception: they live in the gutter the
    /// viewport excludes and are painted over the content, so their areas are left alone.
    /// </remarks>
    public void ClipInvalidate(XPlatControl child, SKRegion region, SKRect client)
    {
        if (IsBar(child))
        {
            return;
        }

        region.Op(XPlatControl.ToRectI(EffectiveViewport(client)), SKRegionOperation.Intersect);
    }

    /// <summary>Paints the bars over whatever the container drew.</summary>
    public void RenderBars(SKCanvas canvas)
    {
        _verticalBar?.Render(canvas);
        _horizontalBar?.Render(canvas);
    }

    public void Dispose()
    {
        _horizontalBar = null;
        _verticalBar = null;
    }

    private SKRect ViewportOf(SKRect client, bool showHorizontal, bool showVertical)
    {
        var thickness = MathF.Max(0f, _thickness);
        var width = MathF.Max(0f, client.Width - (showVertical ? thickness : 0f));
        var height = MathF.Max(0f, client.Height - (showHorizontal ? thickness : 0f));

        return new SKRect(client.Left, client.Top, client.Left + width, client.Top + height);
    }

    private void PlaceBar(
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

                // The bar belongs to the container, so the focus belongs to whatever the user is
                // working in. It still takes clicks and wheel turns, just never the focus.
                Focusable = false,
                Parent = _owner,
            };

            bar.AddScrollHandler(OnBarScrolled);
            _owner.Own(bar);
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

    private void OnBarScrolled(object sender)
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
}
