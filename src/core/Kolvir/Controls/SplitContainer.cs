using SkiaSharp;

namespace Kolvir;

/// <summary>
/// Splits its client area into two <see cref="Panel"/> instances separated by a
/// draggable splitter. Naming follows <c>System.Windows.Forms.SplitContainer</c>:
/// with <see cref="Orientation.Vertical"/> the splitter runs vertically, so
/// <see cref="Panel1"/> is on the left, <see cref="Panel2"/> on the right and
/// <see cref="SplitterDistance"/> is a width; with <see cref="Orientation.Horizontal"/>
/// the splitter runs horizontally, <see cref="Panel1"/> is on top, <see cref="Panel2"/> below
/// and <see cref="SplitterDistance"/> is a height.
/// </summary>
/// <remarks>
/// <see cref="Panel1"/> is the panel whose size is specified; <see cref="Panel2"/> always
/// fills the remaining space. Dragging the splitter (or pressing the arrow keys while the
/// container has focus) adjusts the distance within <see cref="Panel1MinSize"/> and the
/// smaller of <see cref="Panel1MaxSize"/> and the space left over by
/// <see cref="Panel2MinSize"/>.
/// </remarks>
public class SplitContainer : Control
{
    private Action<object>? _splitterMoving;
    private Action<object>? _splitterMoved;
    private Action<object>? _splitterDistanceChanged;
    private const float KeyboardStep = 8f;

    private Orientation _orientation = Orientation.Vertical;
    private float _splitterDistance = 200f;
    private float _splitterWidth = 6f;
    private float _panel1MinSize = 25f;
    private float _panel2MinSize = 25f;
    private float _panel1MaxSize = float.PositiveInfinity;
    private bool _isSplitterFixed;
    private bool _isDragging;
    private float _dragOrigin;
    private float _dragOriginDistance;
    private SKRect _splitterRect;

    private FixedPanel _fixedPanel = FixedPanel.Panel1;

    // The last layout this container ran, kept so a resize can tell which panel should keep the
    // difference: a drag, a re-orientation or a new splitter width leaves the available length
    // alone, only a resize changes it.
    private float _lastAvailable;
    private float _lastSplitterWidth;
    private float _lastDistance;

    /// <summary>The size the fixed panel keeps; NaN until one is asked for or measured.</summary>
    private float _fixedSize = float.NaN;

    /// <summary>
    /// Creates a vertical split with an empty <see cref="Panel1"/> and <see cref="Panel2"/> and the
    /// splitter 200 units from the left.
    /// </summary>
    public SplitContainer()
    {
        Panel1 = new Panel { Name = "Panel1" };
        Panel2 = new Panel { Name = "Panel2" };
        Controls.Add(Panel1);
        Controls.Add(Panel2);
        TabStop = true;
        UpdateCursor();
    }

    /// <summary>Adds a handler that runs while the splitter is being dragged.</summary>
    public IDisposable AddSplitterMovingHandler(Action<object> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        _splitterMoving += handler;
        return new HandlerToken<Action<object>>(RemoveSplitterMovingHandler, handler);
    }

    /// <summary>Removes a handler that <see cref="AddSplitterMovingHandler"/> added.</summary>
    public void RemoveSplitterMovingHandler(Action<object> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        _splitterMoving -= handler;
    }

    /// <summary>Adds a handler that runs when the user finishes dragging the splitter.</summary>
    public IDisposable AddSplitterMovedHandler(Action<object> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        _splitterMoved += handler;
        return new HandlerToken<Action<object>>(RemoveSplitterMovedHandler, handler);
    }

    /// <summary>Removes a handler that <see cref="AddSplitterMovedHandler"/> added.</summary>
    public void RemoveSplitterMovedHandler(Action<object> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        _splitterMoved -= handler;
    }

    /// <summary>Adds a handler that runs whenever <see cref="SplitterDistance"/> changes programmatically or through the splitter.</summary>
    public IDisposable AddSplitterDistanceChangedHandler(Action<object> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        _splitterDistanceChanged += handler;
        return new HandlerToken<Action<object>>(RemoveSplitterDistanceChangedHandler, handler);
    }

    /// <summary>Removes a handler that <see cref="AddSplitterDistanceChangedHandler"/> added.</summary>
    public void RemoveSplitterDistanceChangedHandler(Action<object> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        _splitterDistanceChanged -= handler;
    }

    /// <summary>The panel with the specified size. It sits left (vertical) or top (horizontal).</summary>
    public Panel Panel1 { get; }

    /// <summary>The panel that fills the remaining space. It sits right (vertical) or bottom (horizontal).</summary>
    public Panel Panel2 { get; }

    /// <summary>
    /// Which way the splitter runs. Changing it turns <see cref="SplitterDistance"/> from a width
    /// into a height or back, moves the panels to match and picks the resize cursor for the new
    /// axis.
    /// </summary>
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
            // Widths and heights are not comparable, so the length recorded for the old axis is
            // forgotten rather than carried into the new one: re-orienting keeps the distance it
            // was given.
            _lastAvailable = 0f;
            _fixedSize = float.NaN;
            UpdateCursor();
            PerformLayout();
            Invalidate();
        }
    }

    /// <summary>
    /// Which panel keeps its size when the container is resized, and so which one takes the
    /// difference. Defaults to <see cref="FixedPanel.Panel1"/>.
    /// </summary>
    /// <remarks>
    /// A resize is the only thing this answers: dragging the splitter, changing
    /// <see cref="Orientation"/> or changing <see cref="SplitterWidth"/> sets the split itself and
    /// leaves both panels where they were put. Sizes are still held to the minimums, so a container
    /// with no room for both panels takes the space from whichever one it can.
    /// </remarks>
    public FixedPanel FixedPanel
    {
        get => _fixedPanel;
        set
        {
            if (_fixedPanel == value)
            {
                return;
            }

            _fixedPanel = value;
            _fixedSize = float.NaN;
        }
    }

    /// <summary>
    /// The size of the fixed panel along the split axis - a width for
    /// <see cref="Orientation.Vertical"/>, a height for <see cref="Orientation.Horizontal"/> - and
    /// setting it moves the splitter so that panel measures the value given.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Which panel this is about follows <see cref="FixedPanel"/>, so switching the fixed panel
    /// re-points the property at the other panel rather than changing the split. With
    /// <see cref="FixedPanel.None"/> nothing is held fixed, and Panel 1 is the one this reads and
    /// moves, the way it is when there is no choice at all.
    /// </para>
    /// <para>
    /// It is the same measurement as <see cref="SplitterDistance"/> for Panel 1, and the room left
    /// over for Panel 2. Setting either one is clamped to the panel minimums and raises the
    /// splitter-distance handlers, because both describe the same splitter.
    /// </para>
    /// </remarks>
    public float FixedPanelSize
    {
        get
        {
            var available = AxisLength(ContentRect);
            var splitter = MathF.Min(MathF.Max(0f, _splitterWidth), available);
            var distance = Math.Clamp(_splitterDistance, 0f, MathF.Max(0f, available - splitter));

            return _fixedPanel == FixedPanel.Panel2
                ? MathF.Max(0f, available - splitter - distance)
                : distance;
        }
        set
        {
            _fixedSize = MathF.Max(0f, value);
            float previous = _splitterDistance;
            PerformLayout();
            Invalidate();

            // It is still one splitter, so moving it this way raises the same event a drag does.
            if (MathF.Abs(_splitterDistance - previous) >= 0.01f)
            {
                OnSplitterDistanceChanged();
            }
        }
    }

    /// <summary>
    /// Size of <see cref="Panel1"/> along the split axis: a width for
    /// <see cref="Orientation.Vertical"/>, a height for <see cref="Orientation.Horizontal"/>.
    /// The value is clamped to the allowed range when it is set.
    /// </summary>
    public float SplitterDistance
    {
        get => _splitterDistance;
        set
        {
            var clamped = ClampDistance(value, AxisLength(ContentRect));
            if (MathF.Abs(clamped - _splitterDistance) < 0.01f)
            {
                return;
            }

            _splitterDistance = clamped;
            _fixedSize = float.NaN;
            RequestArrange();
            OnSplitterDistanceChanged();
        }
    }

    /// <summary>Thickness of the draggable band between the panels.</summary>
    public float SplitterWidth
    {
        get => _splitterWidth;
        set
        {
            var normalized = MathF.Max(0f, value);
            if (MathF.Abs(normalized - _splitterWidth) < 0.01f)
            {
                return;
            }

            _splitterWidth = normalized;
            PerformLayout();
            Invalidate();
        }
    }

    /// <summary>Smallest size <see cref="Panel1"/> may be shrunk to.</summary>
    public float Panel1MinSize
    {
        get => _panel1MinSize;
        set => SetRange(ref _panel1MinSize, value);
    }

    /// <summary>Largest size <see cref="Panel1"/> may be grown to. Defaults to unlimited.</summary>
    public float Panel1MaxSize
    {
        get => _panel1MaxSize;
        set => SetRange(ref _panel1MaxSize, value <= 0f ? float.PositiveInfinity : value);
    }

    /// <summary>Smallest size <see cref="Panel2"/> may be shrunk to; it lowers the effective maximum.</summary>
    public float Panel2MinSize
    {
        get => _panel2MinSize;
        set => SetRange(ref _panel2MinSize, value);
    }

    /// <summary>When true the splitter cannot be dragged or moved with the keyboard.</summary>
    public bool IsSplitterFixed
    {
        get => _isSplitterFixed;
        set
        {
            if (_isSplitterFixed == value)
            {
                return;
            }

            _isSplitterFixed = value;
            if (value)
            {
                EndDrag();
            }

            UpdateCursor();
            Invalidate();
        }
    }

    /// <summary>True while the user is dragging the splitter.</summary>
    public bool IsDragging => _isDragging;

    /// <summary>
    /// Fill colour of the splitter assigned here: a literal <see cref="SKColor"/>, a palette
    /// <see cref="ThemeColor"/> role, or <see cref="InheritedValue.Instance"/> for
    /// <see cref="ThemeColor.Splitter"/>.
    /// </summary>
    /// <remarks>
    /// The property reports what was assigned, not what is painted. Read
    /// <see cref="EffectiveSplitterColor"/> for the colour the band is drawn in. A role is resolved
    /// when it is read, so a palette swap recolours a live splitter. The colour is only used while
    /// the splitter is enabled, not hovered and not being dragged; those states keep their own
    /// theme colours.
    /// </remarks>
    public Union<SKColor, ThemeColor, InheritedValue> SplitterColor { get; set; } = InheritedValue.Instance;

    /// <summary>
    /// The splitter band's fill as it is painted: the assignment, else <see cref="ThemeColor.Splitter"/>.
    /// </summary>
    /// <remarks>
    /// A literal colour is returned as it is and a role is resolved through
    /// <see cref="Theme.ColorOf"/> on every read, so a palette swap is a repaint rather than a
    /// rebuild.
    /// </remarks>
    /// <exception cref="InvalidOperationException">
    /// <see cref="SplitterColor"/> was assigned <c>default</c>, so it holds no case.
    /// </exception>
    public SKColor EffectiveSplitterColor
    {
        get
        {
            SplitterColor.ThrowIfUnspecified();

            if (SplitterColor.TryGetValue<SKColor>(out var literal))
            {
                return literal;
            }

            return SplitterColor.TryGetValue<ThemeColor>(out var role)
                ? Theme.ColorOf(role)
                : Theme.ColorOf(ThemeColor.Splitter);
        }
    }

    /// <summary>Draws a small grip in the middle of the splitter.</summary>
    public bool ShowSplitterGrip { get; set; } = true;

    /// <summary>The splitter band in the control's own coordinates.</summary>
    public SKRect SplitterRect => _splitterRect;

    /// <inheritdoc />
    protected override SKSize MeasureContent(SKSize availableSize)
    {
        var first = Panel1.Measure(availableSize);
        var second = Panel2.Measure(availableSize);

        return _orientation == Orientation.Vertical
            ? new SKSize(_splitterDistance + _splitterWidth + second.Width, MathF.Max(first.Height, second.Height))
            : new SKSize(MathF.Max(first.Width, second.Width), _splitterDistance + _splitterWidth + second.Height);
    }

    /// <inheritdoc />
    /// <remarks>
    /// The distance is only clamped here when the available space could actually hold the whole
    /// range, so a transient layout at a tiny size does not collapse a distance the caller asked
    /// for.
    /// </remarks>
    protected override void OnLayout()
    {
        var content = ContentRect;
        var available = AxisLength(content);
        var splitterWidth = MathF.Min(MathF.Max(0f, _splitterWidth), available);
        var track = MathF.Max(0f, available - splitterWidth);
        bool room = available >= splitterWidth + _panel1MinSize + _panel2MinSize;

        // The fixed panel keeps the size it was given, and failing that the size the last split gave
        // it. A request made while the container was too small to honour it waits for a frame that
        // has the room, instead of being clamped into place and then kept at the clamped size.
        float fixedSize = float.IsNaN(_fixedSize)
            ? MathF.Max(0f, _fixedPanel == FixedPanel.Panel2 ? track - _splitterDistance : _splitterDistance)
            : _fixedSize;

        if (room && _fixedPanel == FixedPanel.None && !float.IsNaN(_fixedSize))
        {
            // Nothing is held fixed, so the request places the splitter once and the split's share of
            // the track is what survives the next resize.
            _splitterDistance = Math.Clamp(
                _fixedSize,
                _panel1MinSize,
                MathF.Max(_panel1MinSize, track - _panel2MinSize));
            _fixedSize = float.NaN;
        }
        else if (room && _fixedPanel == FixedPanel.Panel1)
        {
            fixedSize = Math.Clamp(fixedSize, _panel1MinSize, MathF.Max(_panel1MinSize, track - _panel2MinSize));
            _splitterDistance = fixedSize;
            _fixedSize = fixedSize;
        }
        else if (room && _fixedPanel == FixedPanel.Panel2)
        {
            fixedSize = Math.Clamp(fixedSize, _panel2MinSize, MathF.Max(_panel2MinSize, track - _panel1MinSize));
            _splitterDistance = track - fixedSize;
            _fixedSize = fixedSize;
        }
        else if (room)
        {
            // Neither panel is fixed, so the split keeps its share of the track and both panels take
            // part in the difference.
            var previousTrack = MathF.Max(0f, _lastAvailable - _lastSplitterWidth);
            if (previousTrack > 0f && MathF.Abs(available - _lastAvailable) > 0.5f)
            {
                _splitterDistance = ClampDistance(_lastDistance / previousTrack * track, available);
            }
        }
        else
        {
            // Not enough room for both: fit the split to what there is without touching the request,
            // so the panels go back to the size that was asked for when the room comes back.
            _splitterDistance = ClampDistance(_splitterDistance, available);
        }

        var distance = Math.Clamp(_splitterDistance, 0f, track);

        if (_orientation == Orientation.Vertical)
        {
            _splitterRect = new SKRect(content.Left + distance, content.Top, content.Left + distance + splitterWidth, content.Bottom);
            Panel1.SetArrangedBounds(new SKRect(content.Left, content.Top, _splitterRect.Left, content.Bottom));
            Panel2.SetArrangedBounds(new SKRect(_splitterRect.Right, content.Top, content.Right, content.Bottom));
        }
        else
        {
            _splitterRect = new SKRect(content.Left, content.Top + distance, content.Right, content.Top + distance + splitterWidth);
            Panel1.SetArrangedBounds(new SKRect(content.Left, content.Top, content.Right, _splitterRect.Top));
            Panel2.SetArrangedBounds(new SKRect(content.Left, _splitterRect.Bottom, content.Right, content.Bottom));
        }

        // What this frame settled on, for the next resize to measure the difference against.
        _lastAvailable = available;
        _lastSplitterWidth = splitterWidth;
        _lastDistance = distance;
    }

    /// <summary>
    /// Paints the splitter band, and the grip in the middle of it when
    /// <see cref="ShowSplitterGrip"/> is set. The panels paint themselves.
    /// </summary>
    protected override void OnPaint(SKCanvas canvas, SKRect rect)
    {
        if (_splitterRect.Width <= 0f || _splitterRect.Height <= 0f)
        {
            return;
        }

        var highlight = EffectiveEnabled && !_isSplitterFixed && (IsMouseOver || _isDragging || IsFocused);
        var color = !EffectiveEnabled
            ? Theme.DisabledBackground
            : _isDragging
                ? Theme.Accent
                : highlight
                    ? Theme.SplitterHover
                    : EffectiveSplitterColor;

        Painter.Fill(canvas, _splitterRect, color);

        if (ShowSplitterGrip && EffectiveEnabled)
        {
            DrawGrip(canvas, _isDragging || highlight);
        }
    }

    /// <summary>
    /// Starts a drag when the left button goes down inside the splitter band, capturing the mouse
    /// so the drag can leave the band. A fixed splitter, or a disabled container, ignores the press.
    /// </summary>
    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);

        if (e.Button != MouseButtons.Left || !EffectiveEnabled || _isSplitterFixed)
        {
            return;
        }

        if (!Geometry.Contains(_splitterRect, e.Location))
        {
            return;
        }

        _isDragging = true;
        _dragOrigin = AxisCoordinate(e.Location);
        _dragOriginDistance = _splitterDistance;
        CaptureMouse();
        InvalidateSplitter();
    }

    /// <summary>
    /// Moves the splitter with the pointer while a drag is running, raising the splitter-moving
    /// handlers after each move.
    /// </summary>
    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);

        if (!_isDragging)
        {
            return;
        }

        SplitterDistance = _dragOriginDistance + (AxisCoordinate(e.Location) - _dragOrigin);
        _splitterMoving?.Invoke(this);
    }

    /// <summary>
    /// Ends a drag and raises the splitter-moved handlers, which run once per drag rather than once
    /// per move.
    /// </summary>
    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);

        if (!_isDragging)
        {
            return;
        }

        EndDrag();
        _splitterMoved?.Invoke(this);
    }

    /// <summary>
    /// Moves the splitter by a fixed step with the arrow keys that match its orientation, and marks
    /// the key handled.
    /// </summary>
    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);

        if (e.Handled || !EffectiveEnabled || _isSplitterFixed)
        {
            return;
        }

        float delta = e.Key switch
        {
            Key.Left when _orientation == Orientation.Vertical => -KeyboardStep,
            Key.Right when _orientation == Orientation.Vertical => KeyboardStep,
            Key.Up when _orientation == Orientation.Horizontal => -KeyboardStep,
            Key.Down when _orientation == Orientation.Horizontal => KeyboardStep,
            _ => 0f,
        };

        if (delta == 0f)
        {
            return;
        }

        SplitterDistance += delta;
        e.Handled = true;
    }

    /// <summary>Repaints the splitter band, whose colour follows whether the pointer is over it.</summary>
    protected override void OnMouseEnter()
    {
        base.OnMouseEnter();
        InvalidateSplitter();
    }

    /// <summary>Repaints the splitter band, whose colour follows whether the pointer is over it.</summary>
    protected override void OnMouseLeave()
    {
        base.OnMouseLeave();
        InvalidateSplitter();
    }

    /// <summary>Ends a drag in progress, so a splitter cannot be left following a lost capture.</summary>
    protected override void OnLostFocus()
    {
        base.OnLostFocus();
        EndDrag();
    }

    /// <summary>Ends a drag in progress when the container is disabled.</summary>
    protected override void OnEnabledChanged()
    {
        if (!EffectiveEnabled)
        {
            EndDrag();
        }

        base.OnEnabledChanged();
    }

    /// <summary>
    /// Raises <see cref="AddSplitterDistanceChangedHandler"/>. Only reached when
    /// <see cref="SplitterDistance"/> really moved, from the property or from a drag or key step.
    /// </summary>
    protected virtual void OnSplitterDistanceChanged() => _splitterDistanceChanged?.Invoke(this);

    private void EndDrag()
    {
        if (!_isDragging)
        {
            return;
        }

        _isDragging = false;
        ReleaseMouseCapture();
        InvalidateSplitter();
    }

    /// <summary>
    /// Repaints the splitter band alone. Hovering it, dragging it and focusing the container change
    /// nothing but the band's colour and grip, so repainting the container - and with it both
    /// panels, which can hold whole trees of their own - would be far more than the change needs.
    /// </summary>
    private void InvalidateSplitter()
    {
        if (_splitterRect.Width > 0f && _splitterRect.Height > 0f)
        {
            Invalidate(_splitterRect, false);
        }
    }

    private void DrawGrip(SKCanvas canvas, bool highlight)
    {
        const float radius = 1.5f;
        const float spacing = 5f;

        var center = Geometry.Center(_splitterRect);
        var color = highlight ? Theme.AccentForeground : Theme.MutedForeground.WithAlpha(0x60);
        for (int i = -1; i <= 1; i++)
        {
            float offset = i * spacing;
            float x = _orientation == Orientation.Vertical ? center.X : center.X + offset;
            float y = _orientation == Orientation.Vertical ? center.Y + offset : center.Y;
            Painter.Fill(canvas, new SKRect(x - radius, y - radius, x + radius, y + radius), color, radius);
        }
    }

    /// <summary>
    /// Asks for the two panels to be arranged again around the splitter.
    /// </summary>
    /// <remarks>
    /// A drag asks this on every pointer move, and a pointer reports several times per frame.
    /// Arranging here would lay the message-sized subtree in one of the panels out once per report,
    /// for frames nobody sees, so the request is left to the frame that will be drawn. Reading the
    /// panels' bounds before that frame answers with the arrangement they had, which is the same
    /// bargain described on the host's request.
    /// </remarks>
    private void RequestArrange()
    {
        if (Host is { } host)
        {
            host.RequestLayout(this);
            return;
        }

        // Nothing to defer to without a window to paint.
        PerformLayout();
        Invalidate();
    }

    private void SetRange(ref float field, float value)
    {
        var normalized = MathF.Max(0f, value);
        if (MathF.Abs(normalized - field) < 0.01f)
        {
            return;
        }

        field = normalized;
        PerformLayout();
        Invalidate();
    }

    private void UpdateCursor() =>
        Cursor = _isSplitterFixed
            ? Cursor.Default
            : _orientation == Orientation.Vertical ? Cursor.SizeWE : Cursor.SizeNS;

    /// <summary>Clamps a requested distance to the range allowed for the given available space.</summary>
    private float ClampDistance(float value, float available)
    {
        if (available <= 0f)
        {
            return MathF.Max(0f, value);
        }

        var min = MathF.Max(0f, _panel1MinSize);
        var max = MathF.Min(_panel1MaxSize, available - _splitterWidth - MathF.Max(0f, _panel2MinSize));
        if (max < min)
        {
            max = min;
        }

        return Math.Clamp(value, min, max);
    }

    private float AxisLength(SKRect rect) => _orientation == Orientation.Vertical ? rect.Width : rect.Height;

    private float AxisCoordinate(SKPoint point) => _orientation == Orientation.Vertical ? point.X : point.Y;
}
