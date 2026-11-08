using SkiaSharp;

namespace XPlatUI;

/// <summary>What a toast is reporting, which is what its accent bar is coloured for.</summary>
public enum XPlatToastSeverity
{
    /// <summary>Something happened that the user may want to know about.</summary>
    Info = 0,

    /// <summary>An operation finished the way it was supposed to.</summary>
    Success = 1,

    /// <summary>Something finished, but not entirely as asked.</summary>
    Warning = 2,

    /// <summary>An operation failed.</summary>
    Error = 3,
}

/// <summary>
/// A transient message bar: an accent bar, a line of text and a close glyph, which dismisses itself
/// after <see cref="Duration"/> so a success or an error does not need a dialog.
/// </summary>
/// <remarks>
/// <para>
/// It is an ordinary control rather than a window, so the application decides where transient
/// messages belong - docked across the top of a page, in a corner of a panel, inside a card - and
/// the message moves with the layout instead of floating over it. The layer that floats is a popup,
/// which is a different job.
/// </para>
/// <para>
/// The countdown is a timer and follows the rule the marquee bar set: it runs only while the toast is
/// open and <see cref="XPlatControl.IsDisplayed"/>, and there is an application to deliver its
/// ticks. A toast on a page the user is not looking at therefore stops counting rather than expiring
/// unseen, and a dismissed toast runs no timer at all.
/// </para>
/// <para>
/// Opening a toast with <see cref="Show"/> makes it visible and closing it with
/// <see cref="Dismiss"/> hides it, so the control can be built once and reused. The fade and slide
/// this kind of message usually has belongs to <c>XPlatUI.Feature.Animations</c>; this control does
/// not reference that package, so an application that wants the transition opts into it.
/// </para>
/// </remarks>
public class XPlatToast : XPlatControl
{
    /// <summary>What a toast waits before dismissing itself when the application asks for nothing else.</summary>
    public static readonly TimeSpan DefaultDuration = TimeSpan.FromSeconds(4);

    private const float BarWidth = 4f;

    // The accent bar's colours, chosen to read on both the light and the dark surface. They are bar
    // colours rather than text colours, so they do not have to carry a contrast ratio against the
    // surface the way a foreground would; the message itself is always the theme's foreground.
    private static readonly SKColor InfoColor = new(0xFF2F6FED);
    private static readonly SKColor SuccessColor = new(0xFF2E9E5B);
    private static readonly SKColor WarningColor = new(0xFFD08B0A);
    private static readonly SKColor ErrorColor = new(0xFFC93C37);

    private Action<object>? _dismissed;
    private XPlatToastSeverity _severity = XPlatToastSeverity.Info;
    private TimeSpan? _duration = DefaultDuration;
    private SKColor? _barColor;
    private bool _open;
    private bool _closeHot;
    private bool _closePressed;
    private XPlatTimer? _timer;

    public XPlatToast()
    {
        TabStop = true;
        Focusable = true;
        AutoSize = true;
        Cursor = XPlatCursor.Hand;
        CornerRadius = 8f;
        BorderWidth = 1f;
        Padding = new XPlatThickness(22f, 10f, 40f, 10f);
        MinimumSize = new SKSize(160f, 40f);

        // A message nobody has asked for yet takes no room in the layout; Show puts it up.
        Visible = false;
    }

    /// <summary>Creates a closed toast whose message is <paramref name="text"/>.</summary>
    public XPlatToast(string text)
        : this()
    {
        Text = text;
    }

    /// <summary>A toast is a raised message, so it paints on the surface until a background is assigned.</summary>
    protected override XPlatBackground DefaultBackground => XPlatBackground.FromThemeColor(XPlatThemeColor.Surface);

    /// <summary>Raised after the toast dismissed itself or was dismissed.</summary>
    public IDisposable AddDismissedHandler(Action<object> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        _dismissed += handler;
        return new XPlatHandlerToken<Action<object>>(RemoveDismissedHandler, handler);
    }

    /// <summary>Removes a handler that <see cref="AddDismissedHandler"/> added.</summary>
    public void RemoveDismissedHandler(Action<object> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        _dismissed -= handler;
    }

    /// <summary>What the message is reporting. Defaults to <see cref="XPlatToastSeverity.Info"/>.</summary>
    public XPlatToastSeverity Severity
    {
        get => _severity;
        set
        {
            if (_severity == value)
            {
                return;
            }

            _severity = value;
            Invalidate();
        }
    }

    /// <summary>
    /// How long the toast shows before dismissing itself. <see cref="DefaultDuration"/> by default;
    /// null keeps it up until <see cref="Dismiss"/> is called, and <see cref="TimeSpan.Zero"/> means
    /// the same.
    /// </summary>
    public TimeSpan? Duration
    {
        get => _duration;
        set
        {
            if (value is { } duration && duration < TimeSpan.Zero)
            {
                throw new ArgumentOutOfRangeException(nameof(value), value, "The duration cannot be negative.");
            }

            if (_duration == value)
            {
                return;
            }

            _duration = value;

            if (_open && _timer is { Enabled: true } timer && value is { } newDuration && newDuration > TimeSpan.Zero)
            {
                timer.Stop();
                timer.Interval = newDuration;
                timer.Start();
            }
            else
            {
                UpdateTimer();
            }
        }
    }

    /// <summary>
    /// The accent bar's colour, or null to use the one <see cref="Severity"/> implies. Assigning a
    /// colour is how an application brands its own messages.
    /// </summary>
    public SKColor? BarColor
    {
        get => _barColor;
        set
        {
            if (Nullable.Equals(_barColor, value))
            {
                return;
            }

            _barColor = value;
            Invalidate();
        }
    }

    /// <summary>True while the message is up.</summary>
    public bool IsOpen => _open;

    /// <summary>Shows the message and starts the countdown, if it has one and there is something on screen.</summary>
    public void Show()
    {
        if (_open)
        {
            return;
        }

        _open = true;
        Visible = true;

        // Showing or hiding a docked control changes how much room its container has to share out,
        // and only the container can move the siblings.
        Parent?.PerformLayout();
        UpdateTimer();
        Invalidate();
    }

    /// <summary>Hides the message, stops its countdown and raises <see cref="AddDismissedHandler"/>.</summary>
    public void Dismiss()
    {
        if (!_open)
        {
            return;
        }

        _open = false;
        _closePressed = false;
        _closeHot = false;
        ReleaseMouseCapture();
        _timer?.Stop();
        Visible = false;
        Parent?.PerformLayout();
        Invalidate();
        _dismissed?.Invoke(this);
    }

    /// <summary>The rectangle the close glyph is drawn in, in client coordinates.</summary>
    protected SKRect CloseBounds
    {
        get
        {
            var client = ClientRect;
            float size = MathF.Min(24f, client.Height - 8f);
            float top = client.MidY - (size / 2f);
            return new SKRect(client.Right - 10f - size, top, client.Right - 10f, top + size);
        }
    }

    protected override SKSize MeasureContent(SKSize availableSize)
    {
        var measured = XPlatTextRenderer.Measure(Text, Font, MathF.Max(0f, availableSize.Width - Padding.Horizontal), wrap: true);
        return new SKSize(MathF.Ceiling(measured.Width), MathF.Ceiling(measured.Height));
    }

    protected override void OnPaintBackground(SKCanvas canvas, SKRect rect)
    {
        base.OnPaintBackground(canvas, rect);

        XPlatPainter.DrawBorder(canvas, rect, BorderColor, BorderWidth <= 0f ? 1f : BorderWidth, CornerRadius);

        if (IsFocused && Enabled)
        {
            XPlatPainter.DrawFocusRing(canvas, rect, XPlatTheme.FocusRing, 2f, CornerRadius);
        }

        // The accent bar is inset inside the rounded corner rather than drawn over it, so it reads as
        // part of the surface instead of a stripe pasted on.
        var bar = new SKRect(
            rect.Left + 10f,
            rect.Top + 10f,
            rect.Left + 10f + BarWidth,
            rect.Bottom - 10f);

        if (bar.Height > 0f)
        {
            XPlatPainter.Fill(canvas, bar, BarColorOf(_severity), BarWidth / 2f);
        }

        var close = CloseBounds;
        if (close.Width > 0f && (_closeHot || _closePressed))
        {
            var fill = _closePressed ? XPlatTheme.SecondaryPressed : XPlatTheme.SecondaryHover;
            XPlatPainter.Fill(canvas, close, fill, 4f);
        }
    }

    protected override void OnPaint(SKCanvas canvas, SKRect rect)
    {
        var color = Enabled ? ForeColor : XPlatTheme.DisabledForeground;
        var content = XPlatGeometry.Deflate(rect, Padding);

        if (content.Width > 0f && content.Height > 0f && !string.IsNullOrEmpty(Text))
        {
            canvas.Save();
            canvas.ClipRect(content);
            XPlatTextRenderer.Draw(
                canvas,
                Text,
                Font,
                color,
                content,
                XPlatContentAlignment.MiddleLeft,
                wrap: false);
            canvas.Restore();
        }

        PaintCloseGlyph(canvas, CloseBounds, Enabled ? XPlatTheme.MutedForeground : XPlatTheme.DisabledForeground);
    }

    protected override void OnPaintBorder(SKCanvas canvas, SKRect rect)
    {
        // The surface, the border and the accent bar are drawn in OnPaintBackground so they stay
        // behind the message.
    }

    public override XPlatCursor GetCursorAt(SKPoint pointInLocal) =>
        XPlatGeometry.Contains(CloseBounds, pointInLocal) ? XPlatCursor.Hand : XPlatCursor.Default;

    protected override void OnMouseDown(XPlatMouseEventArgs e)
    {
        base.OnMouseDown(e);

        if (e.Button != XPlatMouseButtons.Left || !Enabled)
        {
            return;
        }

        IsPressed = true;

        if (XPlatGeometry.Contains(CloseBounds, e.Location))
        {
            _closePressed = true;
            CaptureMouse();
            Invalidate();
        }
    }

    protected override void OnMouseUp(XPlatMouseEventArgs e)
    {
        base.OnMouseUp(e);

        if (e.Button != XPlatMouseButtons.Left)
        {
            return;
        }

        bool pressedOnClose = _closePressed;
        _closePressed = false;
        IsPressed = false;
        ReleaseMouseCapture();
        Invalidate();

        if (pressedOnClose && XPlatGeometry.Contains(CloseBounds, e.Location))
        {
            Dismiss();
        }
    }

    protected override void OnMouseMove(XPlatMouseEventArgs e)
    {
        bool hot = Enabled && XPlatGeometry.Contains(CloseBounds, e.Location);
        if (hot != _closeHot)
        {
            _closeHot = hot;
            Invalidate();
        }

        // Dragging off the close glyph cancels the press, the way it does on a button.
        if (_closePressed && !hot)
        {
            _closePressed = false;
            ReleaseMouseCapture();
            Invalidate();
        }

        base.OnMouseMove(e);
    }

    protected override void OnMouseLeave()
    {
        if (_closeHot || _closePressed)
        {
            _closeHot = false;
            _closePressed = false;
            ReleaseMouseCapture();
            Invalidate();
        }

        base.OnMouseLeave();
    }

    protected override void OnKeyDown(XPlatKeyEventArgs e)
    {
        base.OnKeyDown(e);

        if (e.Handled || !Enabled || !_open)
        {
            return;
        }

        if (e.Key is XPlatKey.Escape or XPlatKey.Enter or XPlatKey.Space)
        {
            Dismiss();
            e.Handled = true;
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
        Invalidate();
    }

    protected override void OnDisplayedChanged()
    {
        base.OnDisplayedChanged();
        UpdateTimer();
    }

    protected override void OnEnabledChanged()
    {
        if (!Enabled)
        {
            _closeHot = false;
            _closePressed = false;
            ReleaseMouseCapture();
        }

        base.OnEnabledChanged();
        UpdateTimer();
    }

    private SKColor BarColorOf(XPlatToastSeverity severity) => _barColor ?? severity switch
    {
        XPlatToastSeverity.Success => SuccessColor,
        XPlatToastSeverity.Warning => WarningColor,
        XPlatToastSeverity.Error => ErrorColor,
        _ => InfoColor,
    };

    /// <summary>
    /// Runs the countdown only while the message is open and something can see it. A toast on a page
    /// the user has switched away from stops rather than expiring behind the scenes.
    /// </summary>
    private void UpdateTimer()
    {
        bool run = _open
            && IsDisplayed
            && Enabled
            && _duration is { } duration
            && duration > TimeSpan.Zero
            && XPlatApplication.Current is not null;

        if (!run)
        {
            _timer?.Stop();
            return;
        }

        var wait = _duration!.Value;

        if (_timer is null)
        {
            _timer = CreateTimer(wait, OnTick);
        }
        else if (_timer.Enabled)
        {
            if (_timer.Interval != wait)
            {
                _timer.Stop();
                _timer.Interval = wait;
                _timer.Start();
            }

            return;
        }
        else
        {
            _timer.Interval = wait;
        }

        _timer.Start();
    }

    private void OnTick(object sender)
    {
        // The control tree belongs to the caller, so the timer can outlive the application; once it
        // has, the timer ticks inline on a pool thread, which must not touch the UI.
        if (XPlatApplication.Current is null || !IsDisplayed || !_open)
        {
            _timer?.Stop();
            return;
        }

        Dismiss();
    }

    private static void PaintCloseGlyph(SKCanvas canvas, SKRect cell, SKColor color)
    {
        if (cell.Width <= 0f || cell.Height <= 0f)
        {
            return;
        }

        float half = 4f;
        var center = XPlatGeometry.Center(cell);

        using var builder = new SKPathBuilder();
        builder.MoveTo(center.X - half, center.Y - half);
        builder.LineTo(center.X + half, center.Y + half);
        builder.MoveTo(center.X + half, center.Y - half);
        builder.LineTo(center.X - half, center.Y + half);

        using var path = builder.Snapshot();
        using var paint = new SKPaint
        {
            Color = color,
            IsAntialias = true,
            Style = SKPaintStyle.Stroke,
            StrokeWidth = 1.6f,
            StrokeCap = SKStrokeCap.Round,
        };

        canvas.DrawPath(path, paint);
    }
}
