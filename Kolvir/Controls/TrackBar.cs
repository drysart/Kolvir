using SkiaSharp;

namespace Kolvir;

/// <summary>
/// A track bar: a value picked by dragging a knob along a track, between <see cref="Minimum"/> and
/// <see cref="Maximum"/>, horizontally or vertically.
/// </summary>
/// <remarks>
/// <para>
/// The track is filled up to the knob, so the value can be read without finding the knob first, and
/// <see cref="TickFrequency"/> puts marks on the track where the round values are. Pressing the knob
/// starts a drag; pressing the track pages by <see cref="LargeChange"/> toward the press, which is
/// what a track bar has always done and what makes a coarse jump possible without dragging.
/// </para>
/// <para>
/// A drag keeps the grab point, so grabbing the knob off-centre does not make it jump under the
/// pointer. The value follows the pointer rather than a delta, which is what stops a fast drag from
/// falling behind the mouse.
/// </para>
/// </remarks>
public class TrackBar : Control
{
    private const float TrackThickness = 4f;
    private const float KnobWidth = 10f;
    private const float KnobMaxHeight = 18f;
    private const float RingWidth = 2f;
    private const float TickLength = 8f;

    /// <summary>A tick closer than this to the one before it is not drawn: it would read as a block.</summary>
    private const float MinimumTickSpacing = 4f;

    private Action<object>? _valueChanged;

    private Orientation _orientation = Orientation.Horizontal;
    private float _minimum;
    private float _maximum = 10f;
    private float _value;
    private float _smallChange = 1f;
    private float _largeChange = 5f;
    private float _tickFrequency = 1f;

    private bool _dragging;
    private float _grabOffset;

    private SKRect _trackRect;
    private SKRect _knobRect;

    /// <summary>
    /// Creates a horizontal bar at 0 with a range of 0 to 10, one-unit steps, five-unit page steps
    /// and a tick every unit, in the tab order.
    /// </summary>
    public TrackBar()
    {
        TabStop = true;
        Padding = new Thickness(0f);
        MinimumSize = new SKSize(16f, 16f);
    }

    /// <summary>Adds a handler that runs after <see cref="Value"/> has changed.</summary>
    public IDisposable AddValueChangedHandler(Action<object> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        _valueChanged += handler;
        return new HandlerToken<Action<object>>(RemoveValueChangedHandler, handler);
    }

    /// <summary>Removes a handler that <see cref="AddValueChangedHandler"/> added.</summary>
    public void RemoveValueChangedHandler(Action<object> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        _valueChanged -= handler;
    }

    /// <summary>Whether the knob travels left and right or up and down. Defaults to horizontal.</summary>
    /// <remarks>A vertical bar puts <see cref="Minimum"/> at the bottom, so dragging up raises the value.</remarks>
    public Orientation Orientation
    {
        get => _orientation;
        set
        {
            if (_orientation == value)
            {
                return;
            }

            _orientation = value;
            UpdateGeometry();
            Invalidate();
        }
    }

    /// <summary>The value with the knob at the start of the track. Defaults to 0.</summary>
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
            SetValue(_value);
        }
    }

    /// <summary>The value with the knob at the end of the track. Defaults to 10.</summary>
    public float Maximum
    {
        get => _maximum;
        set
        {
            if (_maximum == value)
            {
                return;
            }

            _maximum = value;
            SetValue(_value);
        }
    }

    /// <summary>The current value, kept inside <see cref="Minimum"/> and <see cref="Maximum"/>.</summary>
    public float Value
    {
        get => _value;
        set => SetValue(value);
    }

    /// <summary>How far one arrow key, or one wheel detent, moves the value. Defaults to 1.</summary>
    public float SmallChange
    {
        get => _smallChange;
        set => _smallChange = MathF.Max(0f, value);
    }

    /// <summary>How far a press on the track, Page Up or Page Down moves the value. Defaults to 5.</summary>
    public float LargeChange
    {
        get => _largeChange;
        set => _largeChange = MathF.Max(0f, value);
    }

    /// <summary>
    /// How far apart the marks on the track are, in value units. Defaults to 1; zero, or negative,
    /// draws no marks at all.
    /// </summary>
    /// <remarks>
    /// Marks are dropped when they would land closer together than a few pixels, so a fine
    /// frequency over a wide range never turns the track into a solid bar.
    /// </remarks>
    public float TickFrequency
    {
        get => _tickFrequency;
        set
        {
            if (_tickFrequency == value)
            {
                return;
            }

            _tickFrequency = value;
            Invalidate();
        }
    }

    /// <summary>Moves the value one <see cref="SmallChange"/> toward the end of the track.</summary>
    public void PerformSmallChange() => SetValue(_value + _smallChange);

    /// <summary>Moves the value one <see cref="LargeChange"/> toward the end of the track.</summary>
    public void PerformLargeChange() => SetValue(_value + _largeChange);

    /// <inheritdoc />
    /// <remarks>160 units along the track and 28 across it, the long axis being the one
    /// <see cref="TrackBar.Orientation"/> names.</remarks>
    protected override SKSize MeasureContent(SKSize availableSize) =>
        _orientation == Orientation.Vertical ? new SKSize(28f, 160f) : new SKSize(160f, 28f);

    /// <summary>The knob travels along the pointer, so the cursor names the axis of the drag.</summary>
    public override Cursor GetCursorAt(SKPoint pointInLocal) =>
        _orientation == Orientation.Vertical ? Cursor.SizeNS : Cursor.SizeWE;

    /// <summary>
    /// Paints the track, its tick marks, the part of the track filled up to the knob and the knob
    /// itself, plus a focus ring while the bar has the focus.
    /// </summary>
    protected override void OnPaint(SKCanvas canvas, SKRect rect)
    {
        UpdateGeometry();
        if (_trackRect.Width <= 0f || _trackRect.Height <= 0f)
        {
            return;
        }

        var track = Enabled ? Theme.Secondary : Theme.DisabledBackground;
        var fill = Enabled ? Theme.Accent : Theme.DisabledForeground;

        Painter.Fill(canvas, _trackRect, track, TrackThickness / 2f);
        PaintTicks(canvas);
        PaintFill(canvas, fill);
        PaintKnob(canvas);

        if (IsFocused)
        {
            Painter.DrawFocusRing(canvas, rect, Theme.FocusRing, 1f, Border.CornerRadius);
        }
    }

    /// <summary>
    /// Starts a drag when the knob is pressed, remembering where inside the knob it was grabbed so
    /// the knob does not jump to the pointer. A press on the track pages by
    /// <see cref="LargeChange"/> toward the press instead of jumping there.
    /// </summary>
    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);

        if (e.Button != MouseButtons.Left || !Enabled)
        {
            return;
        }

        Focus();
        UpdateGeometry();

        float pointer = AxisCoordinate(e.Location);
        if (Geometry.Contains(_knobRect, e.Location))
        {
            // Remembering where inside the knob it was grabbed keeps it from jumping to the pointer.
            _dragging = true;
            _grabOffset = AxisCoordinate(Geometry.Center(_knobRect)) - pointer;
            CaptureMouse();
            Invalidate();
            return;
        }

        // A press anywhere on the track pages toward it rather than jumping to it, so a bar with a
        // wide range can still be moved in controlled steps.
        if (Geometry.Contains(_trackRect, e.Location))
        {
            SetValue(_value + (pointer < AxisCoordinate(Geometry.Center(_knobRect)) ? -_largeChange : _largeChange));
        }
    }

    /// <summary>
    /// Drags the knob to the value under the pointer, corrected by the grab offset taken when the
    /// drag started, while a drag is running.
    /// </summary>
    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);

        if (!_dragging)
        {
            return;
        }

        UpdateGeometry();
        SetValue(ValueAtAxis(AxisCoordinate(e.Location) + _grabOffset));
    }

    /// <summary>Ends a drag when the button is released, so the knob stops following the pointer.</summary>
    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);

        if (_dragging)
        {
            _dragging = false;
            ReleaseMouseCapture();
            Invalidate();
        }
    }

    /// <summary>
    /// Moves the value by <see cref="SmallChange"/> per wheel detent, and claims the turn only when
    /// the value actually moved - a bar already at its end leaves the wheel to the panel above it.
    /// </summary>
    protected override void OnMouseWheel(MouseEventArgs e)
    {
        base.OnMouseWheel(e);

        if (e.Handled || !Enabled || e.Delta == 0f)
        {
            return;
        }

        var before = _value;
        SetValue(_value + (e.Delta / UiHost.WheelPixelsPerDetent * _smallChange));
        e.Handled = MathF.Abs(_value - before) > 0.0001f;
    }

    /// <summary>
    /// Home and End jump to the ends of the range, the arrow keys step by
    /// <see cref="SmallChange"/> and Page Up and Page Down by <see cref="LargeChange"/>.
    /// </summary>
    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Handled || !Enabled)
        {
            return;
        }

        switch (e.Key)
        {
            case Key.Home:
                SetValue(_minimum);
                break;

            case Key.End:
                SetValue(_maximum);
                break;

            case Key.Left:
            case Key.Down:
                SetValue(_value - _smallChange);
                break;

            case Key.Right:
            case Key.Up:
                SetValue(_value + _smallChange);
                break;

            case Key.PageDown:
                SetValue(_value - _largeChange);
                break;

            case Key.PageUp:
                SetValue(_value + _largeChange);
                break;

            default:
                return;
        }

        e.Handled = true;
    }

    /// <summary>Ends a drag in progress when the bar loses the focus.</summary>
    protected override void OnLostFocus()
    {
        base.OnLostFocus();
        EndDrag();
    }

    /// <summary>Ends a drag in progress when the bar is disabled.</summary>
    protected override void OnEnabledChanged()
    {
        if (!Enabled)
        {
            EndDrag();
        }

        base.OnEnabledChanged();
    }

    private void EndDrag()
    {
        if (!_dragging)
        {
            return;
        }

        _dragging = false;
        ReleaseMouseCapture();
        Invalidate();
    }

    private bool IsVertical => _orientation == Orientation.Vertical;

    private float AxisCoordinate(SKPoint point) => IsVertical ? point.Y : point.X;

    /// <summary>
    /// The value whose knob centre sits at <paramref name="coordinate"/>, read along the axis. A
    /// vertical bar counts from the bottom, so up is a larger value.
    /// </summary>
    private float ValueAtAxis(float coordinate)
    {
        float travel = IsVertical ? _trackRect.Height : _trackRect.Width;
        if (travel <= 0f)
        {
            return _value;
        }

        float ratio = IsVertical
            ? (_trackRect.Bottom - coordinate) / travel
            : (coordinate - _trackRect.Left) / travel;

        ratio = Math.Clamp(ratio, 0f, 1f);
        return _minimum + ((_maximum - _minimum) * ratio);
    }

    private void UpdateGeometry()
    {
        _trackRect = SKRect.Empty;
        _knobRect = SKRect.Empty;

        var rect = Geometry.Deflate(ClientRect, Padding);
        if (rect.Width <= 0f || rect.Height <= 0f)
        {
            return;
        }

        if (IsVertical)
        {
            float knobWidth = MathF.Min(KnobMaxHeight, MathF.Max(KnobWidth, rect.Width - (RingWidth * 2f)));
            float knobHeight = MathF.Min(KnobWidth, rect.Height);
            float centerX = rect.MidX;

            _trackRect = new SKRect(
                centerX - (TrackThickness / 2f),
                rect.Top + RingWidth + (knobHeight / 2f),
                centerX + (TrackThickness / 2f),
                rect.Bottom - RingWidth - (knobHeight / 2f));

            float centerY = KnobCenter();
            _knobRect = new SKRect(
                centerX - (knobWidth / 2f),
                centerY - (knobHeight / 2f),
                centerX + (knobWidth / 2f),
                centerY + (knobHeight / 2f));
        }
        else
        {
            float knobHeight = MathF.Min(KnobMaxHeight, MathF.Max(KnobWidth, rect.Height - (RingWidth * 2f)));
            float knobWidth = MathF.Min(KnobWidth, rect.Width);
            float centerY = rect.MidY;

            _trackRect = new SKRect(
                rect.Left + RingWidth + (knobWidth / 2f),
                centerY - (TrackThickness / 2f),
                rect.Right - RingWidth - (knobWidth / 2f),
                centerY + (TrackThickness / 2f));

            float centerX = KnobCenter();
            _knobRect = new SKRect(
                centerX - (knobWidth / 2f),
                centerY - (knobHeight / 2f),
                centerX + (knobWidth / 2f),
                centerY + (knobHeight / 2f));
        }
    }

    /// <summary>Where the middle of the knob sits along the track, which is where the value is read.</summary>
    private float KnobCenter()
    {
        float range = _maximum - _minimum;
        float ratio = range <= 0f ? 0f : Math.Clamp((_value - _minimum) / range, 0f, 1f);

        if (IsVertical)
        {
            return _trackRect.Bottom - (_trackRect.Height * ratio);
        }

        return _trackRect.Left + (_trackRect.Width * ratio);
    }

    private void SetValue(float value)
    {
        float low = MathF.Min(_minimum, _maximum);
        float high = MathF.Max(_minimum, _maximum);
        var clamped = Math.Clamp(value, low, high);

        if (MathF.Abs(clamped - _value) < 0.0001f)
        {
            return;
        }

        _value = clamped;
        UpdateGeometry();
        _valueChanged?.Invoke(this);
        Invalidate();
    }

    private void PaintFill(SKCanvas canvas, SKColor color)
    {
        var center = KnobCenter();
        var fill = IsVertical
            ? new SKRect(_trackRect.Left, center, _trackRect.Right, _trackRect.Bottom)
            : new SKRect(_trackRect.Left, _trackRect.Top, center, _trackRect.Bottom);

        if (fill.Width < 1f || fill.Height < 1f)
        {
            return;
        }

        Painter.Fill(canvas, fill, color, TrackThickness / 2f);
    }

    private void PaintTicks(SKCanvas canvas)
    {
        float range = _maximum - _minimum;
        float travel = IsVertical ? _trackRect.Height : _trackRect.Width;
        if (_tickFrequency <= 0f || range <= 0f || travel <= 0f)
        {
            return;
        }

        if (travel * _tickFrequency / range < MinimumTickSpacing)
        {
            return;
        }

        using var paint = new SKPaint
        {
            Color = (Enabled ? Theme.MutedForeground : Theme.DisabledForeground).WithAlpha(0x60),
            IsAntialias = false,
            Style = SKPaintStyle.Stroke,
            StrokeWidth = 1f,
        };

        for (float value = _minimum; value <= _maximum + (_tickFrequency / 1000f); value += _tickFrequency)
        {
            float center = IsVertical
                ? _trackRect.Bottom - (travel * Math.Clamp((value - _minimum) / range, 0f, 1f))
                : _trackRect.Left + (travel * Math.Clamp((value - _minimum) / range, 0f, 1f));

            // A tick under the knob would be drawn over it, so the knob wins.
            if (IsVertical ? center >= _knobRect.Top && center <= _knobRect.Bottom : center >= _knobRect.Left && center <= _knobRect.Right)
            {
                continue;
            }

            if (IsVertical)
            {
                canvas.DrawLine(
                    _trackRect.MidX - (TickLength / 2f),
                    MathF.Round(center) + 0.5f,
                    _trackRect.MidX + (TickLength / 2f),
                    MathF.Round(center) + 0.5f,
                    paint);
            }
            else
            {
                canvas.DrawLine(
                    MathF.Round(center) + 0.5f,
                    _trackRect.MidY - (TickLength / 2f),
                    MathF.Round(center) + 0.5f,
                    _trackRect.MidY + (TickLength / 2f),
                    paint);
            }
        }
    }

    private void PaintKnob(SKCanvas canvas)
    {
        if (_knobRect.Width <= 0f || _knobRect.Height <= 0f)
        {
            return;
        }

        var color = !Enabled
            ? Theme.DisabledForeground
            : _dragging
                ? Theme.AccentPressed
                : IsMouseOver
                    ? Theme.AccentHover
                    : Theme.Accent;

        // The gap is the control's own surface colour, so the knob stays separate from the fill it
        // sits at the end of without a hard outline.
        var gap = Enabled ? Theme.Surface : Theme.DisabledBackground;
        float radius = MathF.Min(_knobRect.Width, _knobRect.Height) / 2f;

        Painter.Fill(canvas, Geometry.Inflate(_knobRect, RingWidth), gap, radius + RingWidth);
        Painter.Fill(canvas, _knobRect, color, radius);
    }
}
