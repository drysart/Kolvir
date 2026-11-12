using SkiaSharp;

namespace Kolvir;

/// <summary>What a toast is reporting, which is what its accent bar is coloured for.</summary>
public enum ToastSeverity
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
/// open and <see cref="Control.IsDisplayed"/>, and there is an application to deliver its
/// ticks. A toast on a page the user is not looking at therefore stops counting rather than expiring
/// unseen, and a dismissed toast runs no timer at all.
/// </para>
/// <para>
/// Opening a toast with <see cref="Show"/> makes it visible and closing it with
/// <see cref="Dismiss"/> hides it, so the control can be built once and reused. The fade and slide
/// this kind of message usually has belongs to <c>Kolvir.Feature.Animations</c>; this control does
/// not reference that package, so an application that wants the transition opts into it.
/// </para>
/// <para>
/// <b>The seam an animation uses.</b> <see cref="ShowProgress"/> is the one number the message is
/// drawn from - the opacity, and how far it has come in from its docked edge - and it follows
/// <see cref="IsOpen"/> on its own, so an application that never animates never assigns it. A
/// handler of <see cref="AddShowingHandler"/> or <see cref="AddDismissingHandler"/> that assigns it
/// while the change is being raised has taken the transition over, which is how the feature package
/// drives it a frame at a time. A dismissal is over when the progress reaches 0 rather than when
/// <see cref="Dismiss"/> is called, so the message is taken off the screen by whatever brings the
/// progress down - and a progress that is put on 0 by anything else removes it just the same.
/// </para>
/// </remarks>
public class Toast : Control
{
    /// <summary>What a toast waits before dismissing itself when the application asks for nothing else.</summary>
    public static readonly TimeSpan DefaultDuration = TimeSpan.FromSeconds(4);

    private const float BarWidth = 4f;

    /// <summary>How far the message travels at most as it comes in, in logical pixels.</summary>
    private const float SlideReach = 20f;

    // The accent bar's colours, chosen to read on both the light and the dark surface. They are bar
    // colours rather than text colours, so they do not have to carry a contrast ratio against the
    // surface the way a foreground would; the message itself is always the theme's foreground.
    private static readonly SKColor InfoColor = new(0xFF2F6FED);
    private static readonly SKColor SuccessColor = new(0xFF2E9E5B);
    private static readonly SKColor WarningColor = new(0xFFD08B0A);
    private static readonly SKColor ErrorColor = new(0xFFC93C37);

    private Action<object>? _dismissed;
    private Action<object>? _showing;
    private Action<object>? _dismissing;
    private ToastSeverity _severity = ToastSeverity.Info;
    private TimeSpan? _duration = DefaultDuration;
    private SKColor? _barColor;
    private bool _open;
    private bool _closeHot;
    private bool _closePressed;
    private Timer? _timer;
    private double _progress;
    private bool _progressSteered;
    private bool _removalPending;
    private SKPoint _slideDirection = new(0f, -1f);
    private SKPoint _slide;
    private bool _keyboardTaken;
    private bool _tabStopBeforeDismiss = true;
    private bool _focusableBeforeDismiss = true;

    public Toast()
    {
        TabStop = true;
        Focusable = true;
        AutoSize = true;
        Cursor = Cursor.Hand;
        CornerRadius = 8f;
        BorderWidth = 1f;
        Padding = new Thickness(22f, 10f, 40f, 10f);
        MinimumSize = new SKSize(160f, 40f);

        // A message nobody has asked for yet takes no room in the layout; Show puts it up.
        Visible = false;
    }

    /// <summary>Creates a closed toast whose message is <paramref name="text"/>.</summary>
    public Toast(string text)
        : this()
    {
        Text = text;
    }

    /// <summary>A toast is a raised message, so it paints on the surface until a background is assigned.</summary>
    protected override Background DefaultBackground => Background.FromThemeColor(ThemeColor.Surface);

    /// <summary>Raised after the toast dismissed itself or was dismissed.</summary>
    public IDisposable AddDismissedHandler(Action<object> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        _dismissed += handler;
        return new HandlerToken<Action<object>>(RemoveDismissedHandler, handler);
    }

    /// <summary>Removes a handler that <see cref="AddDismissedHandler"/> added.</summary>
    public void RemoveDismissedHandler(Action<object> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        _dismissed -= handler;
    }

    /// <summary>
    /// Adds a handler that runs when the message is about to come in, before
    /// <see cref="ShowProgress"/> follows.
    /// </summary>
    /// <remarks>
    /// The order is the contract, the way it is for <see cref="Expander.AddExpandedChangedHandler"/>:
    /// a handler that assigns <see cref="ShowProgress"/> while it runs has taken the transition
    /// over, and the control does not snap the progress to 1. Assigning the value it already has
    /// counts, so an animation can hold the message where it is and drive it from there.
    /// </remarks>
    public IDisposable AddShowingHandler(Action<object> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        _showing += handler;
        return new HandlerToken<Action<object>>(RemoveShowingHandler, handler);
    }

    /// <summary>Removes a handler that <see cref="AddShowingHandler"/> added.</summary>
    public void RemoveShowingHandler(Action<object> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        _showing -= handler;
    }

    /// <summary>
    /// Adds a handler that runs when the message is about to go, before <see cref="ShowProgress"/>
    /// follows it down.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The same contract as <see cref="AddShowingHandler"/>: a handler that assigns
    /// <see cref="ShowProgress"/> has taken the transition over, and the message stays on screen
    /// until something brings the progress to 0.
    /// </para>
    /// <para>
    /// From the moment this is raised the message is no longer open, so it has already left the Tab
    /// order and given up the focus - it must not be reachable on its way out. Coming back with
    /// <see cref="Show"/> puts both back.
    /// </para>
    /// </remarks>
    public IDisposable AddDismissingHandler(Action<object> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        _dismissing += handler;
        return new HandlerToken<Action<object>>(RemoveDismissingHandler, handler);
    }

    /// <summary>Removes a handler that <see cref="AddDismissingHandler"/> added.</summary>
    public void RemoveDismissingHandler(Action<object> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        _dismissing -= handler;
    }

    /// <summary>
    /// How far the message has come in, from 0 to 1: 0 is fully gone and 1 is fully in place.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The one number the message is drawn from. At anything below 1 it is faded - its
    /// <see cref="Control.Opacity"/> is this value, so the whole message composites as one -
    /// and it is drawn short of where the layout put it, out of the edge of its container that it
    /// is docked against. The step is a single translate, so nothing about the control is stretched
    /// on the way: the accent bar stays a bar and the close glyph stays square.
    /// </para>
    /// <para>
    /// It follows <see cref="IsOpen"/> on its own, so an application that never animates never
    /// assigns it: a message that has not been shown is at 0, and <see cref="Show"/> puts it at 1 in
    /// the same call. It is public because something else - <c>ShowDuration</c> and
    /// <c>DismissDuration</c> in <c>Kolvir.Feature.Animations</c> - drives it a frame at a time,
    /// and because the value a control is drawn from is worth being able to read.
    /// </para>
    /// <para>
    /// Out of range values are clamped, so an animation that overshoots its ease lands on its end
    /// state rather than drawing past it. A message that is no longer open leaves the screen the
    /// moment the progress reaches 0, whatever brought it there.
    /// </para>
    /// </remarks>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="value"/> is NaN.</exception>
    public double ShowProgress
    {
        get => _progress;
        set
        {
            if (double.IsNaN(value))
            {
                throw new ArgumentOutOfRangeException(nameof(value), value, "The progress must be a number.");
            }

            // Set before the early-out below, for the same reason ExpandProgress does: holding the
            // progress where it is counts as taking the transition over just as much as moving it.
            _progressSteered = true;

            double clamped = Math.Clamp(value, 0d, 1d);
            bool moved = Math.Abs(clamped - _progress) >= 0.000001d;

            _progress = clamped;

            // Kept in step even when the value did not move. A message that has never been shown
            // reads 0 here and is still a plain opaque control, and the hold that an animation
            // starts with is the assignment that puts the two back together - before the first
            // frame, so the message never paints at full strength on its way in.
            Opacity = (float)clamped;

            if (moved)
            {
                UpdateSlide();
                Invalidate();
            }

            // Checked on every assignment rather than only on a change, because the message can be
            // dismissed with the progress already at 0 - shown and dismissed in the same turn, for
            // instance - and that still has to take it off the screen.
            RemoveIfDismissed();
        }
    }

    /// <summary>What the message is reporting. Defaults to <see cref="ToastSeverity.Info"/>.</summary>
    public ToastSeverity Severity
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

        // A show that arrives while the message is still fading out turns round: the removal the
        // dismissal was heading for is off, and the message has its keyboard flags back.
        _removalPending = false;
        GiveKeyboardBack();

        Visible = true;

        // Showing or hiding a docked control changes how much room its container has to share out,
        // and only the container can move the siblings. Done before the edge is worked out, so the
        // message comes in from where the layout has just put it.
        Parent?.PerformLayout();
        _slideDirection = SlideDirection();
        UpdateTimer();

        MoveProgress(1d, dismissing: false);
        Invalidate();
    }

    /// <summary>Hides the message, stops its countdown and raises <see cref="AddDismissedHandler"/>.</summary>
    /// <remarks>
    /// With nothing animating the message goes at once. With a handler of
    /// <see cref="AddDismissingHandler"/> driving <see cref="ShowProgress"/> it stays on the screen,
    /// out of reach of the keyboard, until that progress reaches 0 - which is what removes it and
    /// raises <see cref="AddDismissedHandler"/>, once.
    /// </remarks>
    public void Dismiss()
    {
        if (!_open)
        {
            return;
        }

        _open = false;
        _removalPending = true;
        _closePressed = false;
        _closeHot = false;
        ReleaseMouseCapture();
        _timer?.Stop();
        TakeKeyboardOutOfReach();
        Invalidate();

        MoveProgress(0d, dismissing: true);
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
        var measured = TextRenderer.Measure(Text, Font, MathF.Max(0f, availableSize.Width - Padding.Horizontal), wrap: true);
        return new SKSize(MathF.Ceiling(measured.Width), MathF.Ceiling(measured.Height));
    }

    protected override void OnPaintBackground(SKCanvas canvas, SKRect rect)
    {
        bool shifted = BeginSlide(canvas);

        base.OnPaintBackground(canvas, rect);

        Painter.DrawBorder(canvas, rect, BorderColor, BorderWidth <= 0f ? 1f : BorderWidth, CornerRadius);

        if (IsFocused && Enabled)
        {
            Painter.DrawFocusRing(canvas, rect, Theme.FocusRing, 2f, CornerRadius);
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
            Painter.Fill(canvas, bar, BarColorOf(_severity), BarWidth / 2f);
        }

        var close = CloseBounds;
        if (close.Width > 0f && (_closeHot || _closePressed))
        {
            var fill = _closePressed ? Theme.SecondaryPressed : Theme.SecondaryHover;
            Painter.Fill(canvas, close, fill, 4f);
        }

        if (shifted)
        {
            canvas.Restore();
        }
    }

    protected override void OnPaint(SKCanvas canvas, SKRect rect)
    {
        bool shifted = BeginSlide(canvas);

        var color = Enabled ? ForeColor : Theme.DisabledForeground;
        var content = Geometry.Deflate(rect, Padding);

        if (content.Width > 0f && content.Height > 0f && !string.IsNullOrEmpty(Text))
        {
            canvas.Save();
            canvas.ClipRect(content);
            TextRenderer.Draw(
                canvas,
                Text,
                Font,
                color,
                content,
                ContentAlignment.MiddleLeft,
                wrap: false);
            canvas.Restore();
        }

        PaintCloseGlyph(canvas, CloseBounds, Enabled ? Theme.MutedForeground : Theme.DisabledForeground);

        if (shifted)
        {
            canvas.Restore();
        }
    }

    protected override void OnPaintBorder(SKCanvas canvas, SKRect rect)
    {
        // The surface, the border and the accent bar are drawn in OnPaintBackground so they stay
        // behind the message.
    }

    public override Cursor GetCursorAt(SKPoint pointInLocal) =>
        Geometry.Contains(CloseBounds, pointInLocal) ? Cursor.Hand : Cursor.Default;

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);

        if (e.Button != MouseButtons.Left || !Enabled)
        {
            return;
        }

        IsPressed = true;

        if (Geometry.Contains(CloseBounds, e.Location))
        {
            _closePressed = true;
            CaptureMouse();
            Invalidate();
        }
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);

        if (e.Button != MouseButtons.Left)
        {
            return;
        }

        bool pressedOnClose = _closePressed;
        _closePressed = false;
        IsPressed = false;
        ReleaseMouseCapture();
        Invalidate();

        if (pressedOnClose && Geometry.Contains(CloseBounds, e.Location))
        {
            Dismiss();
        }
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        bool hot = Enabled && Geometry.Contains(CloseBounds, e.Location);
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

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);

        if (e.Handled || !Enabled || !_open)
        {
            return;
        }

        if (e.Key is Key.Escape or Key.Enter or Key.Space)
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

    private SKColor BarColorOf(ToastSeverity severity) => _barColor ?? severity switch
    {
        ToastSeverity.Success => SuccessColor,
        ToastSeverity.Warning => WarningColor,
        ToastSeverity.Error => ErrorColor,
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
            && Application.Current is not null;

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
        if (Application.Current is null || !IsDisplayed || !_open)
        {
            _timer?.Stop();
            return;
        }

        Dismiss();
    }

    /// <summary>
    /// Moves the message to <paramref name="target"/>, unless a handler takes the transition over.
    /// </summary>
    private void MoveProgress(double target, bool dismissing)
    {
        // The contract is the expander's: a handler that assigns ShowProgress while the change is
        // being raised has taken the transition over, and the snap below stands down. Assigning the
        // value the progress already has counts, which is how an animation holds the message where
        // it is before driving it.
        _progressSteered = false;

        if (dismissing)
        {
            _dismissing?.Invoke(this);
        }
        else
        {
            _showing?.Invoke(this);
        }

        if (!_progressSteered)
        {
            ShowProgress = target;
        }
    }

    /// <summary>
    /// Takes the message off the screen once it has finished going: it was told to dismiss, and its
    /// progress has reached 0.
    /// </summary>
    /// <remarks>
    /// Every assignment of <see cref="ShowProgress"/> ends here, so no route can leave a toast
    /// half faded and still on the screen. The pending flag is what keeps it to one removal, which
    /// matters because a dismissal that lands exactly on 0 and then settles there assigns the same
    /// value twice.
    /// </remarks>
    private void RemoveIfDismissed()
    {
        if (!_removalPending || _progress > 0d)
        {
            return;
        }

        _removalPending = false;
        _closePressed = false;
        _closeHot = false;
        ReleaseMouseCapture();
        _timer?.Stop();
        Visible = false;
        Parent?.PerformLayout();
        Invalidate();
        _dismissed?.Invoke(this);
    }

    /// <summary>
    /// Stops a message that is on its way out from taking the keyboard: it leaves the Tab order,
    /// gives up the focus, and the focus moves to whatever above it can still take it.
    /// </summary>
    /// <remarks>
    /// The two flags are put back when the message comes back, so an application that never
    /// dismissed it never sees a change, and one that set either of them keeps its choice.
    /// </remarks>
    private void TakeKeyboardOutOfReach()
    {
        if (_keyboardTaken)
        {
            return;
        }

        _keyboardTaken = true;
        _tabStopBeforeDismiss = TabStop;
        _focusableBeforeDismiss = Focusable;
        TabStop = false;
        Focusable = false;

        // The toast is not focusable any more, so this moves the focus up to the nearest ancestor
        // that can take it rather than leaving it on the control that is leaving.
        ReleaseFocusFromSubtree();
    }

    /// <summary>Puts back the Tab stop and focus flags <see cref="TakeKeyboardOutOfReach"/> took away.</summary>
    private void GiveKeyboardBack()
    {
        if (!_keyboardTaken)
        {
            return;
        }

        _keyboardTaken = false;
        TabStop = _tabStopBeforeDismiss;
        Focusable = _focusableBeforeDismiss;
    }

    /// <summary>
    /// Which way the message travels as it comes in: one unit step out of the edge of the space it
    /// sits in that it is docked against, so a message along the top of a page slides down and one
    /// at the bottom slides up.
    /// </summary>
    /// <remarks>
    /// A message that spans the width of that space is a band, and it is docked to the top or the
    /// bottom even though a band flush with the top is also flush with both sides; one that spans
    /// the height is docked to a side. Anything smaller takes whichever edge it is nearest.
    /// </remarks>
    private SKPoint SlideDirection()
    {
        if (Parent is not { } parent)
        {
            return new SKPoint(0f, -1f);
        }

        var area = parent.ClientRect;
        var bounds = Bounds;

        float left = bounds.Left - area.Left;
        float right = area.Right - bounds.Right;
        float top = bounds.Top - area.Top;
        float bottom = area.Bottom - bounds.Bottom;

        bool spansWidth = bounds.Width >= area.Width - 1f;
        bool spansHeight = bounds.Height >= area.Height - 1f;

        if (spansWidth && !spansHeight)
        {
            return top <= bottom ? new SKPoint(0f, -1f) : new SKPoint(0f, 1f);
        }

        if (spansHeight && !spansWidth)
        {
            return left <= right ? new SKPoint(-1f, 0f) : new SKPoint(1f, 0f);
        }

        float nearest = MathF.Min(MathF.Min(top, bottom), MathF.Min(left, right));

        if (nearest == top)
        {
            return new SKPoint(0f, -1f);
        }

        if (nearest == bottom)
        {
            return new SKPoint(0f, 1f);
        }

        return nearest == left ? new SKPoint(-1f, 0f) : new SKPoint(1f, 0f);
    }

    /// <summary>
    /// Puts the message where its progress says: exactly on its layout slot at 1, and a short way
    /// out of its docked edge at 0.
    /// </summary>
    /// <remarks>
    /// The step is one translate of the whole control, never a change to its geometry, so the
    /// accent bar, the close glyph and the text keep their shape all the way in - a slide that
    /// scaled the control would turn the square glyph into a rectangle.
    /// </remarks>
    private void UpdateSlide()
    {
        float height = Bounds.Height > 0f ? Bounds.Height : MinimumSize.Height;
        float reach = MathF.Min(SlideReach, MathF.Max(4f, height * 0.5f));
        float away = (float)(1d - _progress) * reach;

        _slide = new SKPoint(_slideDirection.X * away, _slideDirection.Y * away);
    }

    /// <summary>
    /// Moves the canvas to where the message is while it is on its way in or out, and reports
    /// whether it did - false is the case for every toast that never animates.
    /// </summary>
    private bool BeginSlide(SKCanvas canvas)
    {
        if (_slide.X == 0f && _slide.Y == 0f)
        {
            return false;
        }

        canvas.Save();
        canvas.Translate(_slide.X, _slide.Y);
        return true;
    }

    private static void PaintCloseGlyph(SKCanvas canvas, SKRect cell, SKColor color)
    {
        if (cell.Width <= 0f || cell.Height <= 0f)
        {
            return;
        }

        float half = 4f;
        var center = Geometry.Center(cell);

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
