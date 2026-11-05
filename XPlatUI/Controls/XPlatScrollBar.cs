using SkiaSharp;

namespace XPlatUI;

/// <summary>
/// A traditional scrollbar. It occupies layout space (unlike an overlay scrollbar) and mirrors
/// <c>System.Windows.Forms.ScrollBar</c>: arrow buttons at both ends, a track, and a draggable
/// thumb sized from <see cref="LargeChange"/>.
/// </summary>
/// <remarks>
/// The bar is purely a value slider. <see cref="XPlatPanel"/> creates and drives its own bars
/// from <see cref="XPlatPanel.HorizontalScroll"/> and <see cref="XPlatPanel.VerticalScroll"/>;
/// use this control directly only when you need a standalone slider. <see cref="Value"/> is
/// always kept inside <see cref="Minimum"/> and <see cref="Maximum"/>.
/// </remarks>
public class XPlatScrollBar : XPlatControl, IXPlatSelectionBarrier
{
    /// <summary>The thickness a scrollbar is given by default, in device independent pixels.</summary>
    public const float DefaultThickness = 16f;

    private const float ArrowLength = 16f;
    private const float MinimumThumbLength = 24f;
    private const float ThumbInset = 3f;

    /// <summary>One wheel detent moves the bar as far as this many arrow presses would.</summary>
    private const float WheelLinesPerDetent = 3f;

    private Action<object>? _scroll;
    private Action<object>? _valueChanged;

    private XPlatOrientation _orientation = XPlatOrientation.Vertical;
    private float _minimum;
    private float _maximum = 100f;
    private float _value;
    private float _smallChange = 16f;
    private float _largeChange = 64f;

    private Part _hoverPart;
    private Part _pressedPart;
    private bool _draggingThumb;
    private float _dragOrigin;
    private float _dragOriginValue;

    private SKRect _decrementRect;
    private SKRect _trackRect;
    private SKRect _incrementRect;
    private SKRect _thumbRect;

    public XPlatScrollBar()
    {
        TabStop = true;
    }

    /// <summary>Adds a handler that runs when the user scrolls the bar.</summary>
    /// <remarks>
    /// Unlike <see cref="AddValueChangedHandler"/> this only fires for user interaction, so a
    /// container can assign <see cref="Value"/> while laying out without feeding the change back
    /// into its own scroll position.
    /// </remarks>
    public IDisposable AddScrollHandler(Action<object> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        _scroll += handler;
        return new XPlatHandlerToken<Action<object>>(RemoveScrollHandler, handler);
    }

    /// <summary>Removes a handler that <see cref="AddScrollHandler"/> added.</summary>
    public void RemoveScrollHandler(Action<object> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        _scroll -= handler;
    }

    /// <summary>Adds a handler that runs whenever <see cref="Value"/> changes.</summary>
    public IDisposable AddValueChangedHandler(Action<object> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        _valueChanged += handler;
        return new XPlatHandlerToken<Action<object>>(RemoveValueChangedHandler, handler);
    }

    /// <summary>Removes a handler that <see cref="AddValueChangedHandler"/> added.</summary>
    public void RemoveValueChangedHandler(Action<object> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        _valueChanged -= handler;
    }

    /// <summary>Whether the bar scrolls vertically (thumb travels up and down) or horizontally.</summary>
    public XPlatOrientation Orientation
    {
        get => _orientation;
        set
        {
            if (_orientation == value)
            {
                return;
            }

            _orientation = value;
            Refresh();
        }
    }

    /// <summary>The value reported when the thumb is at the start of the track.</summary>
    public float Minimum
    {
        get => _minimum;
        set
        {
            if (_minimum == value)
            {
                return;
            }

            _minimum = value;
            if (_maximum < _minimum)
            {
                _maximum = _minimum;
            }

            ClampValue();
            Refresh();
        }
    }

    /// <summary>The value reported when the thumb is at the end of the track.</summary>
    public float Maximum
    {
        get => _maximum;
        set
        {
            var clamped = MathF.Max(value, _minimum);
            if (_maximum == clamped)
            {
                return;
            }

            _maximum = clamped;
            ClampValue();
            Refresh();
        }
    }

    /// <summary>The current position, always inside <see cref="Minimum"/> and <see cref="Maximum"/>.</summary>
    public float Value
    {
        get => _value;
        set => SetValue(value, userInitiated: false);
    }

    /// <summary>The distance one arrow button or arrow key press scrolls.</summary>
    public float SmallChange
    {
        get => _smallChange;
        set
        {
            var clamped = MathF.Max(0f, value);
            if (_smallChange == clamped)
            {
                return;
            }

            _smallChange = clamped;
            Refresh();
        }
    }

    /// <summary>The distance one page click or page key press scrolls. Also sizes the thumb.</summary>
    public float LargeChange
    {
        get => _largeChange;
        set
        {
            var clamped = MathF.Max(1f, value);
            if (_largeChange == clamped)
            {
                return;
            }

            _largeChange = clamped;
            Refresh();
        }
    }

    /// <summary>True when the whole range fits, so there is nothing to scroll.</summary>
    public bool IsScrollable => Enabled && _maximum > _minimum;

    /// <summary>The rectangle the thumb is painted into, in control coordinates.</summary>
    public SKRect ThumbRect => _thumbRect;

    /// <summary>The rectangle the track is painted into, in control coordinates.</summary>
    public SKRect TrackRect => _trackRect;

    protected override void OnLayout()
    {
        base.OnLayout();
        UpdateGeometry();
    }

    protected override SKSize MeasureContent(SKSize availableSize) => new(DefaultThickness, DefaultThickness);

    protected override void OnPaint(SKCanvas canvas, SKRect rect)
    {
        var surface = Enabled ? XPlatTheme.Secondary : XPlatTheme.DisabledBackground;
        XPlatPainter.Fill(canvas, rect, surface);

        if (_trackRect.Width <= 0f || _trackRect.Height <= 0f)
        {
            return;
        }

        DrawButton(canvas, _decrementRect, Part.Decrement);
        DrawButton(canvas, _incrementRect, Part.Increment);
        DrawSeparators(canvas);
        DrawThumb(canvas);

        if (IsFocused)
        {
            XPlatPainter.DrawFocusRing(canvas, rect, XPlatTheme.FocusRing, 1f);
        }
    }

    protected override void OnMouseDown(XPlatMouseEventArgs e)
    {
        base.OnMouseDown(e);

        if (e.Button != XPlatMouseButtons.Left || !IsScrollable)
        {
            return;
        }

        Focus();
        UpdateGeometry();

        switch (HitPart(e.Location))
        {
            case Part.Decrement:
                _pressedPart = Part.Decrement;
                ScrollBy(-_smallChange);
                CaptureMouse();
                break;

            case Part.Increment:
                _pressedPart = Part.Increment;
                ScrollBy(_smallChange);
                CaptureMouse();
                break;

            case Part.Track:
                _pressedPart = Part.Track;
                var thumbCenter = AxisCoordinate(XPlatGeometry.Center(_thumbRect));
                ScrollBy(AxisCoordinate(e.Location) < thumbCenter ? -_largeChange : _largeChange);
                CaptureMouse();
                break;

            case Part.Thumb:
                _pressedPart = Part.Thumb;
                _draggingThumb = true;
                _dragOrigin = AxisCoordinate(e.Location);
                _dragOriginValue = _value;
                CaptureMouse();
                break;
        }

        Invalidate();
    }

    protected override void OnMouseMove(XPlatMouseEventArgs e)
    {
        base.OnMouseMove(e);

        if (!Enabled)
        {
            return;
        }

        if (_draggingThumb)
        {
            float travel = AxisLength(_trackRect) - AxisLength(_thumbRect);
            if (travel > 0f)
            {
                var range = _maximum - _minimum;
                var delta = (AxisCoordinate(e.Location) - _dragOrigin) * range / travel;
                SetValue(_dragOriginValue + delta, userInitiated: true);
            }

            return;
        }

        var part = IsScrollable ? HitPart(e.Location) : Part.None;
        if (part != _hoverPart)
        {
            _hoverPart = part;
            Invalidate();
        }
    }

    protected override void OnMouseUp(XPlatMouseEventArgs e)
    {
        base.OnMouseUp(e);
        EndInteraction();
    }

    protected override void OnMouseEnter()
    {
        base.OnMouseEnter();
        Invalidate();
    }

    protected override void OnMouseLeave()
    {
        base.OnMouseLeave();

        if (_hoverPart != Part.None)
        {
            _hoverPart = Part.None;
            Invalidate();
        }
    }

    protected override void OnKeyDown(XPlatKeyEventArgs e)
    {
        base.OnKeyDown(e);

        if (e.Handled || !IsScrollable)
        {
            return;
        }

        switch (e.Key)
        {
            case XPlatKey.Home:
                SetValue(_minimum, userInitiated: true);
                break;

            case XPlatKey.End:
                SetValue(_maximum, userInitiated: true);
                break;

            case XPlatKey.Up:
            case XPlatKey.Left:
                ScrollBy(-_smallChange);
                break;

            case XPlatKey.Down:
            case XPlatKey.Right:
                ScrollBy(_smallChange);
                break;

            case XPlatKey.PageUp:
                ScrollBy(-_largeChange);
                break;

            case XPlatKey.PageDown:
                ScrollBy(_largeChange);
                break;

            default:
                return;
        }

        e.Handled = true;
    }

    /// <summary>
    /// A wheel turn moves the bar as far as a few arrow presses would, so a bar can be scrolled
    /// without focusing it first. The bar only claims the turn when the value actually moved,
    /// which leaves a bar at either end free to pass it to the container around it.
    /// </summary>
    protected override void OnMouseWheel(XPlatMouseEventArgs e)
    {
        base.OnMouseWheel(e);

        if (e.Handled || !IsScrollable)
        {
            return;
        }

        var before = _value;

        // A positive delta scrolls toward the start of the content, which is a smaller value.
        var steps = e.Delta / XPlatUiHost.WheelPixelsPerDetent * WheelLinesPerDetent;
        ScrollBy(-steps * _smallChange);
        e.Handled = MathF.Abs(_value - before) > 0.001f;
    }

    protected override void OnLostFocus()
    {
        base.OnLostFocus();
        EndInteraction();
    }

    protected override void OnEnabledChanged()
    {
        if (!Enabled)
        {
            EndInteraction();
        }

        base.OnEnabledChanged();
    }

    private void ScrollBy(float delta) => SetValue(_value + delta, userInitiated: true);

    private void SetValue(float value, bool userInitiated)
    {
        var clamped = Math.Clamp(value, _minimum, _maximum);
        if (MathF.Abs(clamped - _value) < 0.001f)
        {
            return;
        }

        _value = clamped;
        UpdateGeometry();
        _valueChanged?.Invoke(this);

        if (userInitiated)
        {
            _scroll?.Invoke(this);
        }

        Invalidate();
    }

    private void ClampValue()
    {
        var clamped = Math.Clamp(_value, _minimum, _maximum);
        if (MathF.Abs(clamped - _value) >= 0.001f)
        {
            _value = clamped;
            _valueChanged?.Invoke(this);
        }
    }

    private void Refresh()
    {
        UpdateGeometry();
        Invalidate();
    }

    private void EndInteraction()
    {
        if (!_draggingThumb && _pressedPart == Part.None)
        {
            return;
        }

        _draggingThumb = false;
        _pressedPart = Part.None;
        ReleaseMouseCapture();
        Invalidate();
    }

    private void UpdateGeometry()
    {
        _decrementRect = SKRect.Empty;
        _trackRect = SKRect.Empty;
        _incrementRect = SKRect.Empty;
        _thumbRect = SKRect.Empty;

        var rect = ClientRect;
        var vertical = _orientation == XPlatOrientation.Vertical;
        var length = vertical ? rect.Height : rect.Width;
        var thickness = vertical ? rect.Width : rect.Height;
        if (length <= 0f || thickness <= 0f)
        {
            return;
        }

        var arrow = MathF.Min(MathF.Min(ArrowLength, thickness), length / 2f);
        var trackStart = arrow;
        var trackLength = MathF.Max(0f, length - (arrow * 2f));

        _decrementRect = vertical
            ? new SKRect(rect.Left, rect.Top, rect.Right, rect.Top + arrow)
            : new SKRect(rect.Left, rect.Top, rect.Left + arrow, rect.Bottom);

        _incrementRect = vertical
            ? new SKRect(rect.Left, rect.Bottom - arrow, rect.Right, rect.Bottom)
            : new SKRect(rect.Right - arrow, rect.Top, rect.Right, rect.Bottom);

        _trackRect = vertical
            ? new SKRect(rect.Left, rect.Top + trackStart, rect.Right, rect.Top + trackStart + trackLength)
            : new SKRect(rect.Left + trackStart, rect.Top, rect.Left + trackStart + trackLength, rect.Bottom);

        if (trackLength <= 0f)
        {
            return;
        }

        var range = _maximum - _minimum;
        var largeChange = MathF.Max(1f, _largeChange);
        var thumbLength = range <= 0f
            ? trackLength
            : Math.Clamp(
                trackLength * (largeChange / (range + largeChange)),
                MathF.Min(MinimumThumbLength, trackLength),
                trackLength);

        var ratio = range <= 0f ? 0f : Math.Clamp((_value - _minimum) / range, 0f, 1f);
        var offset = (trackLength - thumbLength) * ratio;

        _thumbRect = vertical
            ? new SKRect(rect.Left, _trackRect.Top + offset, rect.Right, _trackRect.Top + offset + thumbLength)
            : new SKRect(_trackRect.Left + offset, rect.Top, _trackRect.Left + offset + thumbLength, rect.Bottom);
    }

    private Part HitPart(SKPoint point)
    {
        if (XPlatGeometry.Contains(_thumbRect, point))
        {
            return Part.Thumb;
        }

        if (XPlatGeometry.Contains(_decrementRect, point))
        {
            return Part.Decrement;
        }

        if (XPlatGeometry.Contains(_incrementRect, point))
        {
            return Part.Increment;
        }

        return XPlatGeometry.Contains(_trackRect, point) ? Part.Track : Part.None;
    }

    private float AxisCoordinate(SKPoint point) => _orientation == XPlatOrientation.Vertical ? point.Y : point.X;

    private float AxisLength(SKRect rect) => _orientation == XPlatOrientation.Vertical ? rect.Height : rect.Width;

    private void DrawButton(SKCanvas canvas, SKRect rect, Part part)
    {
        if (rect.Width <= 0f || rect.Height <= 0f)
        {
            return;
        }

        if (Enabled)
        {
            var color = _pressedPart == part
                ? XPlatTheme.SecondaryPressed
                : _hoverPart == part
                    ? XPlatTheme.SecondaryHover
                    : XPlatTheme.Secondary;

            XPlatPainter.Fill(canvas, rect, color);
        }

        DrawArrow(canvas, rect, part == Part.Decrement);
    }

    private void DrawSeparators(SKCanvas canvas)
    {
        if (!Enabled || _decrementRect.Height <= 0f)
        {
            return;
        }

        using var paint = new SKPaint
        {
            Color = XPlatTheme.Border,
            IsAntialias = false,
            Style = SKPaintStyle.Stroke,
            StrokeWidth = 1f,
        };

        if (_orientation == XPlatOrientation.Vertical)
        {
            canvas.DrawLine(_decrementRect.Left, _decrementRect.Bottom - 0.5f, _decrementRect.Right, _decrementRect.Bottom - 0.5f, paint);
            canvas.DrawLine(_incrementRect.Left, _incrementRect.Top + 0.5f, _incrementRect.Right, _incrementRect.Top + 0.5f, paint);
        }
        else
        {
            canvas.DrawLine(_decrementRect.Right - 0.5f, _decrementRect.Top, _decrementRect.Right - 0.5f, _decrementRect.Bottom, paint);
            canvas.DrawLine(_incrementRect.Left + 0.5f, _incrementRect.Top, _incrementRect.Left + 0.5f, _incrementRect.Bottom, paint);
        }
    }

    private void DrawArrow(SKCanvas canvas, SKRect rect, bool decrement)
    {
        var center = XPlatGeometry.Center(rect);
        var size = MathF.Min(MathF.Min(rect.Width, rect.Height) * 0.3f, 4f);
        if (size <= 0.5f)
        {
            return;
        }

        using var builder = new SKPathBuilder();
        if (_orientation == XPlatOrientation.Vertical)
        {
            var tip = decrement ? center.Y - size : center.Y + size;
            var tail = decrement ? center.Y + size : center.Y - size;
            builder.MoveTo(center.X, tip);
            builder.LineTo(center.X - size, tail);
            builder.LineTo(center.X + size, tail);
        }
        else
        {
            var tip = decrement ? center.X - size : center.X + size;
            var tail = decrement ? center.X + size : center.X - size;
            builder.MoveTo(tip, center.Y);
            builder.LineTo(tail, center.Y - size);
            builder.LineTo(tail, center.Y + size);
        }

        builder.Close();

        using var path = builder.Snapshot();
        using var paint = new SKPaint
        {
            Color = Enabled ? XPlatTheme.MutedForeground : XPlatTheme.DisabledForeground,
            IsAntialias = true,
            Style = SKPaintStyle.Fill,
        };

        canvas.DrawPath(path, paint);
    }

    private void DrawThumb(SKCanvas canvas)
    {
        var thumb = XPlatGeometry.Deflate(_thumbRect, ThumbInset);
        if (thumb.Width <= 0f || thumb.Height <= 0f)
        {
            return;
        }

        var color = !Enabled
            ? XPlatTheme.DisabledForeground.WithAlpha(0x30)
            : _draggingThumb
                ? XPlatTheme.Accent
                : _hoverPart == Part.Thumb
                    ? XPlatTheme.MutedForeground.WithAlpha(0x99)
                    : XPlatTheme.MutedForeground.WithAlpha(0x66);

        XPlatPainter.Fill(canvas, thumb, color, MathF.Min(4f, MathF.Min(thumb.Width, thumb.Height) / 2f));
    }

    private enum Part
    {
        None,
        Decrement,
        Track,
        Thumb,
        Increment,
    }
}
