using SkiaSharp;

namespace Kolvir;

/// <summary>
/// The scrollbar plumbing shared by the scrolling containers: it owns the two
/// <see cref="ScrollBar"/> children, works out how much of the client area they take, and
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
internal sealed class ScrollBars
{
    private const float Epsilon = 0.5f;

    private readonly Control _owner;

    private ScrollStyle _horizontal = ScrollStyle.None;
    private ScrollStyle _vertical = ScrollStyle.None;
    private float _thickness = ScrollBar.DefaultThickness;

    private ScrollBar? _horizontalBar;
    private ScrollBar? _verticalBar;

    private float _horizontalOffset;
    private float _verticalOffset;
    private SKPoint _childOffset;
    private SKRect _client;
    private SKRect _viewport;
    private SKRect _contentArea;
    private SKSize _extent;

    // The visibility the last pass settled on. Starting the next one from it is what keeps a
    // steady state at one arrange: an Automatic bar is only discovered by arranging without it, so
    // guessing "hidden" every time would arrange the whole content twice on every single pass.
    private bool _showHorizontal;
    private bool _showVertical;

    private bool _hasLayout;
    private bool _layingOut;

    public ScrollBars(Control owner)
    {
        ArgumentNullException.ThrowIfNull(owner);
        _owner = owner;
    }

    /// <summary>The epsilon used when deciding whether an axis can still move.</summary>
    public static float ScrollEpsilon => Epsilon;

    public ScrollStyle Horizontal
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

    public ScrollStyle Vertical
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

    public ScrollBar? HorizontalBar => _horizontalBar;

    public ScrollBar? VerticalBar => _verticalBar;

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
    public float MaxHorizontalOffset => _horizontal == ScrollStyle.None
        ? 0f
        : MathF.Max(0f, _extent.Width - _contentArea.Width);

    public float MaxVerticalOffset => _vertical == ScrollStyle.None
        ? 0f
        : MathF.Max(0f, _extent.Height - _contentArea.Height);

    /// <summary>
    /// The area the content is clipped to. Falls back to the whole client area before the first
    /// layout has run, so a control can be hit tested or painted without one.
    /// </summary>
    public SKRect EffectiveViewport(SKRect client) =>
        _viewport.Width > 0f && _viewport.Height > 0f ? _viewport : client;

    /// <summary>True when <paramref name="child"/> is one of the bars this helper owns.</summary>
    public bool IsBar(Control child) =>
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
    public void Layout(SKRect client, Thickness padding, Func<SKRect, SKSize> arrange)
    {
        ArgumentNullException.ThrowIfNull(arrange);

        if (_layingOut)
        {
            return;
        }

        _layingOut = true;
        try
        {
            // A control that has not been given a size yet has no viewport to scroll in, and
            // deflating an empty client still leaves a small non-empty content area behind.
            // Scrolling against that would record an offset that is wrong the moment the control
            // is really laid out, so an unsized client arranges nothing visible.
            if (client.Width <= 0f || client.Height <= 0f)
            {
                _client = client;
                _viewport = SKRect.Empty;
                _contentArea = SKRect.Empty;
                _extent = arrange(SKRect.Empty);
                _showVertical = _vertical == ScrollStyle.Always;
                _showHorizontal = _horizontal == ScrollStyle.Always;
                _hasLayout = true;
                _childOffset = new SKPoint(-_horizontalOffset, -_verticalOffset);
                PlaceBars();
                return;
            }

            var showVertical = _vertical switch
            {
                ScrollStyle.Always => true,
                ScrollStyle.Automatic => _showVertical,
                _ => false,
            };

            var showHorizontal = _horizontal switch
            {
                ScrollStyle.Always => true,
                ScrollStyle.Automatic => _showHorizontal,
                _ => false,
            };

            var viewport = ViewportOf(client, showHorizontal, showVertical);
            var area = Geometry.Deflate(viewport, padding);
            var extent = arrange(area);

            for (int pass = 0; pass < 3; pass++)
            {
                var nextVertical = _vertical switch
                {
                    ScrollStyle.Always => true,
                    ScrollStyle.Automatic => extent.Height > area.Height + Epsilon,
                    _ => false,
                };

                var nextHorizontal = _horizontal switch
                {
                    ScrollStyle.Always => true,
                    ScrollStyle.Automatic => extent.Width > area.Width + Epsilon,
                    _ => false,
                };

                if (nextVertical == showVertical && nextHorizontal == showHorizontal)
                {
                    break;
                }

                showVertical = nextVertical;
                showHorizontal = nextHorizontal;
                viewport = ViewportOf(client, showHorizontal, showVertical);
                area = Geometry.Deflate(viewport, padding);
                extent = arrange(area);
            }

            _client = client;
            _viewport = viewport;
            _contentArea = area;
            _extent = extent;
            _showVertical = showVertical;
            _showHorizontal = showHorizontal;
            _hasLayout = true;

            _horizontalOffset = Math.Clamp(_horizontalOffset, 0f, MaxHorizontalOffset);
            _verticalOffset = Math.Clamp(_verticalOffset, 0f, MaxVerticalOffset);
            _childOffset = new SKPoint(-_horizontalOffset, -_verticalOffset);

            PlaceBars();
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

        // The range comes from the extent and the viewport, so it only exists once something has
        // been arranged. Before that the offsets are kept as asked and the layout below clamps them.
        if (_hasLayout)
        {
            horizontal = MathF.Min(horizontal, MaxHorizontalOffset);
            vertical = MathF.Min(vertical, MaxVerticalOffset);
        }

        if (MathF.Abs(horizontal - _horizontalOffset) < 0.01f && MathF.Abs(vertical - _verticalOffset) < 0.01f)
        {
            return;
        }

        _horizontalOffset = horizontal;
        _verticalOffset = vertical;

        // Scrolling slides the content under a viewport that does not move, so it changes nothing
        // the arrange callback computes: whatever the container scrolls is positioned in content
        // coordinates and painted through the offset. Re-arranging on a scroll would walk every
        // child of a large content extent to end up exactly where the last pass left it, so only
        // the bars and the frame are touched. The first scroll before any layout still needs one,
        // because there is no viewport or extent to place the bars against yet.
        if (!_hasLayout)
        {
            _owner.PerformLayout();
            _owner.Invalidate();
            return;
        }

        _childOffset = new SKPoint(-horizontal, -vertical);
        PlaceBars();
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

    /// <summary>
    /// Scrolls vertically so that a band of the content, given as a top and a bottom in content
    /// coordinates, is inside the viewport. The horizontal offset is left where it is, which is
    /// what a caller moving a selection one row at a time wants.
    /// </summary>
    public void ScrollVerticalIntoView(float top, float bottom)
    {
        float vertical = _verticalOffset;

        if (top < vertical)
        {
            vertical = top;
        }
        else if (bottom > vertical + _contentArea.Height)
        {
            vertical = bottom - _contentArea.Height;
        }

        ScrollTo(_horizontalOffset, vertical);
    }

    /// <summary>Clips an invalidation coming from scrolled content to the viewport.</summary>
    /// <remarks>
    /// Scrolled content is clipped to the viewport while painting, so an area outside it cannot
    /// have changed anything on screen. The bars are the exception: they live in the gutter the
    /// viewport excludes and are painted over the content, so their areas are left alone.
    /// </remarks>
    public void ClipInvalidate(Control child, SKRegion region, SKRect client)
    {
        if (IsBar(child))
        {
            return;
        }

        region.Op(Control.ToRectI(EffectiveViewport(client)), SKRegionOperation.Intersect);
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

    /// <summary>Positions the two bars for the geometry the last layout pass resolved.</summary>
    private void PlaceBars()
    {
        PlaceBar(
            ref _verticalBar,
            _showVertical,
            Orientation.Vertical,
            new SKRect(_viewport.Right, _client.Top, _client.Right, _viewport.Bottom),
            _contentArea.Height,
            _extent.Height,
            _verticalOffset);

        PlaceBar(
            ref _horizontalBar,
            _showHorizontal,
            Orientation.Horizontal,
            new SKRect(_client.Left, _viewport.Bottom, _viewport.Right, _client.Bottom),
            _contentArea.Width,
            _extent.Width,
            _horizontalOffset);
    }

    private void PlaceBar(
        ref ScrollBar? bar,
        bool visible,
        Orientation orientation,
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
            bar = new ScrollBar
            {
                Name = orientation == Orientation.Vertical ? "VerticalScrollBar" : "HorizontalScrollBar",
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
        bar.SmallChange = MathF.Max(1f, MathF.Min(ScrollBar.DefaultThickness, maximum));
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
