using SkiaSharp;

namespace Kolvir;

/// <summary>
/// A collapsible section: a header with a chevron and a caption, and the content that folds away
/// under it. It is the shape a settings page is made of, where <see cref="GroupBox"/> is the
/// static version of the same idea.
/// </summary>
/// <remarks>
/// <para>
/// It is an <see cref="Panel"/>, so the content keeps the docking, scrolling and child
/// management of one. The header band is taken out of the content area by overriding
/// <see cref="Panel.ContentBounds"/> rather than by adding to the padding, which is what keeps
/// a scrolled child from ever being arranged or painted under the caption.
/// </para>
/// <para>
/// Collapsing folds the control itself rather than only hiding the content: it remembers the height
/// it had and shrinks to its header, and expanding puts that height back. An
/// <see cref="Control.AutoSize"/> expander needs none of that - it measures as its header when
/// collapsed - so the two agree without the control having to guess which one it is.
/// </para>
/// <para>
/// <b>Two states, one of them the user's.</b> <see cref="IsExpanded"/> is the target - what the
/// caller asked for, what the header toggles, what the keyboard folds and unfolds.
/// <see cref="ExpandProgress"/> is the visual state: 0 folded, 1 fully open, anything between a
/// section that is half open. With nothing animating the two always agree, because changing
/// <see cref="IsExpanded"/> moves the progress itself; a caller never assigns
/// <see cref="ExpandProgress"/> and never has to. They disagree only while something is driving the
/// progress, which is why the number exists at all: a section that is half open is expanded by
/// <see cref="IsExpanded"/> - that is where it is going - and 0.5 by
/// <see cref="ExpandProgress"/> - that is where it is.
/// </para>
/// <para>
/// The progress drives everything visual, the content height and the chevron's rotation both, so
/// there is one number to animate rather than two that can drift apart. The arrow is one chevron
/// turned by <see cref="ArrowRotationDegrees"/>: pointing right at 0 and exactly a quarter turn
/// clockwise - pointing down - at 1, which is why the folded and open arrows cannot end up being
/// drawn as different shapes.
/// </para>
/// <para>
/// The expand and collapse is a jump, deliberately. Animating it is
/// <c>Kolvir.Feature.Animations</c> work, and the control does not reference that package: an
/// application that wants the slide opts into it the way it opts into any other feature.
/// <c>ExpandDuration</c> there animates this same progress, so the two never disagree about what
/// the section is doing.
/// </para>
/// <para>
/// <b>The keyboard follows the target, not the fold.</b> The header is a tab stop and stays where
/// it is in the order whether the section is open or shut. The content is not: folded, nothing
/// inside the section is a tab stop and nothing inside it can be focused, because
/// <see cref="Control.IsContentReachable"/> is false there. Which answer a collapsing section
/// gives is settled by <see cref="IsExpanded"/> the moment it is set, so a fold in progress takes
/// no new focus and does not change its mind at the last frame. Collapsing while a control inside
/// the content holds the focus hands that focus to the header - the nearest thing the user was
/// looking at - rather than leaving it on a control that is no longer on screen.
/// </para>
/// </remarks>
public class Expander : Panel
{
    private const float ChevronWidth = 18f;

    private Action<object>? _expandedChanged;
    private Action<object>? _progressChanged;
    private bool _expanded = true;
    private bool _headerHot;
    private bool _headerPressed;
    private bool _progressSteered;
    private double _progress = 1d;
    private float _expandedHeight;

    /// <summary>
    /// Creates an expanded section with no caption and the border, corner radius and padding the
    /// control is drawn with.
    /// </summary>
    public Expander()
    {
        BorderWidth = 1f;
        CornerRadius = 8f;
        Padding = new Thickness(12f, 0f, 12f, 12f);
        TabStop = true;
        Cursor = Cursor.Hand;
    }

    /// <summary>An expander is a card: a surface with a border, which is what it was drawn as.</summary>
    protected override Background DefaultBackground => Background.FromThemeColor(ThemeColor.Surface);

    /// <summary>Creates an expander whose header reads <paramref name="header"/>.</summary>
    public Expander(string header)
        : this()
    {
        Text = header;
    }

    /// <summary>
    /// Raised when <see cref="IsExpanded"/> changed, before <see cref="ExpandProgress"/> follows it.
    /// </summary>
    /// <remarks>
    /// The order is the contract: a handler that assigns <see cref="ExpandProgress"/> while it runs
    /// takes the transition over, and the control then leaves the progress where that handler put
    /// it instead of jumping it to the new target. That is the one case where the progress is
    /// allowed to disagree with <see cref="IsExpanded"/>, and it is how an animation starts from
    /// wherever the section happens to be - including from halfway through a fold that has not
    /// finished - rather than from the target it is heading for.
    /// </remarks>
    public IDisposable AddExpandedChangedHandler(Action<object> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        _expandedChanged += handler;
        return new HandlerToken<Action<object>>(RemoveExpandedChangedHandler, handler);
    }

    /// <summary>Removes a handler that <see cref="AddExpandedChangedHandler"/> added.</summary>
    public void RemoveExpandedChangedHandler(Action<object> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        _expandedChanged -= handler;
    }

    /// <summary>Raised after <see cref="ExpandProgress"/> changed by a worthwhile amount.</summary>
    public IDisposable AddExpandProgressChangedHandler(Action<object> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        _progressChanged += handler;
        return new HandlerToken<Action<object>>(RemoveExpandProgressChangedHandler, handler);
    }

    /// <summary>Removes a handler that <see cref="AddExpandProgressChangedHandler"/> added.</summary>
    public void RemoveExpandProgressChangedHandler(Action<object> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        _progressChanged -= handler;
    }

    /// <summary>
    /// Whether the content is showing: the state the section is heading for. Collapsing remembers
    /// the height it had; expanding restores it.
    /// </summary>
    /// <remarks>
    /// Changing it moves <see cref="ExpandProgress"/> to match, one way or the other, unless a
    /// handler of <see cref="AddExpandedChangedHandler"/> drives the progress itself. So a half
    /// open section is expanded here and 0.5 there.
    /// </remarks>
    public bool IsExpanded
    {
        get => _expanded;
        set
        {
            if (_expanded == value)
            {
                return;
            }

            _expanded = value;

            // Raised before the progress follows, so a handler can take the transition over. The
            // flag it leaves behind is what tells the snap below to stand down.
            _progressSteered = false;
            _expandedChanged?.Invoke(this);

            if (!_progressSteered)
            {
                ExpandProgress = value ? 1d : 0d;
            }

            if (!value)
            {
                // The target is what the section is doing, so it is authoritative from the moment
                // it is set rather than from the moment the fold finishes: a section on its way
                // shut is out of the Tab order already, and focus inside it has nowhere to be.
                ReleaseFocusFromSubtree();
            }
        }
    }

    /// <summary>
    /// Whether the content can be reached, which it can only be while the section is expanded.
    /// </summary>
    /// <remarks>
    /// Folded, the children stay in the tree - that is what lets the section show them again - but
    /// the control arranges them to nothing and paints none of them, so they are not reachable and
    /// the Tab order steps over them. The question is answered from
    /// <see cref="IsExpanded"/> rather than from <see cref="ExpandProgress"/> so a section that is
    /// folding is already shut to the keyboard: it does not take the focus back halfway through,
    /// and it does not decide at the last frame either.
    /// </remarks>
    protected internal override bool IsContentReachable => _expanded;

    /// <summary>
    /// How far open the section is drawn, from 0 to 1: the content's height and the chevron's
    /// rotation both come from it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// It follows <see cref="IsExpanded"/> on its own, so an application that never animates never
    /// assigns it. It is public because something else - <c>ExpandDuration</c> in
    /// <c>Kolvir.Feature.Animations</c> - drives it a frame at a time, and because a value that a
    /// control is drawn from is worth being able to read.
    /// </para>
    /// <para>
    /// Out of range values are clamped: below 0 folds it, above 1 opens it fully. A section driven
    /// by an animation that overshoots its ease therefore lands on its target instead of drawing
    /// past it. NaN is not a progress and is rejected.
    /// </para>
    /// </remarks>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="value"/> is NaN.</exception>
    public double ExpandProgress
    {
        get => _progress;
        set
        {
            if (double.IsNaN(value))
            {
                throw new ArgumentOutOfRangeException(nameof(value), value, "The progress must be a number.");
            }

            // Set before the early-out below: a driver that wants the transition held where it is -
            // a fold starting from halfway - assigns the value it already has, and that has to count
            // as taking the transition over just as much as a move would.
            _progressSteered = true;

            double clamped = Math.Clamp(value, 0d, 1d);
            if (Math.Abs(clamped - _progress) < 0.000001d)
            {
                return;
            }

            // Leaving full expansion: the height it is at now is the one to come back to. Taken
            // before the height changes, and only off the top of the range, so a fold that is
            // reversed halfway still remembers the height a whole section had.
            if (_progress >= 1d && !AutoSize && Height > HeaderHeight)
            {
                _expandedHeight = Height;
            }

            _progress = clamped;
            ApplyProgress();
            _progressChanged?.Invoke(this);
        }
    }

    /// <summary>
    /// How far the chevron is turned, in degrees clockwise: 0 points right for a folded section and
    /// 90 points down for an open one.
    /// </summary>
    /// <remarks>
    /// The arrow is painted at exactly this angle, so a test that pins it is pinning what the
    /// screen shows rather than a number that happens to sit beside it.
    /// </remarks>
    public double ArrowRotationDegrees => _progress * 90d;

    /// <summary>The height the content is showing at, which collapsing remembers and expanding restores.</summary>
    public float ExpandedHeight => _expandedHeight > HeaderHeight ? _expandedHeight : Height;

    /// <summary>The height of the header band: the caption's line plus its vertical padding.</summary>
    protected float HeaderHeight => MathF.Ceiling(Font.LineHeight) + 18f;

    /// <summary>The area the content is laid out in, which starts below the header.</summary>
    protected override SKRect ContentBounds =>
        new(ClientRect.Left, ClientRect.Top + HeaderHeight, ClientRect.Right, ClientRect.Bottom);

    /// <summary>An expander's own header is emphasized until the theme or an ancestor says otherwise.</summary>
    protected override Font DefaultFont => Theme.EmphasizedFont;

    /// <inheritdoc />
    /// <remarks>
    /// The content is measured as a whole section and then scaled by <see cref="ExpandProgress"/>,
    /// so an <see cref="Control.AutoSize"/> expander folds its height along with everything else.
    /// </remarks>
    protected override SKSize MeasureContent(SKSize availableSize)
    {
        float header = HeaderHeight;
        float headerWidth = ChevronWidth + 2f +
                            TextRenderer.Measure(Text, Font, float.PositiveInfinity, wrap: false).Width;

        if (_progress <= 0d)
        {
            return new SKSize(headerWidth, header);
        }

        // Measured as a whole section and then folded to the progress, so an AutoSize expander
        // animates its height too instead of jumping to the fully open measurement on the first
        // frame.
        var content = base.MeasureContent(new SKSize(availableSize.Width, MathF.Max(0f, availableSize.Height - header)));
        return new SKSize(
            MathF.Max(headerWidth, content.Width),
            (content.Height * (float)_progress) + header);
    }

    /// <inheritdoc />
    /// <remarks>
    /// Folded, the children are still arranged - to nothing, at the top of the content area - so
    /// they keep a rectangle that shows again when the section opens.
    /// </remarks>
    protected override SKSize ArrangeChildren(SKRect area)
    {
        if (_progress > 0d)
        {
            // Half open the children are laid out in the room that is left; the panel's viewport
            // clips whatever hangs past the folded edge, so a fold in progress shows the top of the
            // content rather than nothing at all.
            return base.ArrangeChildren(area);
        }

        // Folded, the content still exists - so it shows again on expand - but it occupies nothing:
        // zeroing the rectangles is what stops a child reporting the position it had the last time
        // the section was open. Painting and hit testing skip it separately, so nothing is walked.
        foreach (var child in Controls)
        {
            child.Bounds = new SKRect(area.Left, area.Top, area.Right, area.Top);
        }

        return SKSize.Empty;
    }

    /// <summary>A perfectly folded expander paints nothing of its content, so the walk is skipped entirely.</summary>
    protected override void RenderScrolledChildren(SKCanvas canvas)
    {
        if (_progress > 0d)
        {
            base.RenderScrolledChildren(canvas);
        }
    }

    /// <summary>While folded the only thing under the pointer is the header, which is this control.</summary>
    protected override Control? HitTestChildren(SKPoint local) =>
        _progress > 0d ? base.HitTestChildren(local) : null;

    /// <summary>Fills the header band while the pointer is over it or the header is held down.</summary>
    protected override void OnPaintBackground(SKCanvas canvas, SKRect rect)
    {
        base.OnPaintBackground(canvas, rect);

        if (!_headerHot && !_headerPressed)
        {
            return;
        }

        var header = new SKRect(rect.Left, rect.Top, rect.Right, rect.Top + HeaderHeight);
        var fill = _headerPressed ? Theme.SecondaryPressed : Theme.SecondaryHover;
        Painter.Fill(canvas, header, fill);
    }

    /// <summary>
    /// Paints the chevron and the caption, and the hairline under the header while the section is
    /// showing any of its content.
    /// </summary>
    protected override void OnPaint(SKCanvas canvas, SKRect rect)
    {
        var color = Enabled ? ForeColor : Theme.DisabledForeground;
        float headerBottom = rect.Top + HeaderHeight;
        var chevron = new SKRect(rect.Left + Padding.Left, rect.Top, rect.Left + Padding.Left + ChevronWidth, headerBottom);

        PaintChevron(canvas, chevron, color);

        var caption = new SKRect(
            chevron.Right + 2f,
            rect.Top,
            rect.Right - Padding.Right,
            headerBottom);

        if (!string.IsNullOrEmpty(Text) && caption.Width > 0f)
        {
            TextRenderer.Draw(canvas, Text, Font, color, caption, ContentAlignment.MiddleLeft);
        }

        if (_progress > 0d)
        {
            // The hairline separates the header from the content; folded, the panel's own border is
            // already the bottom edge and a second line would read as a gap.
            Painter.Fill(
                canvas,
                new SKRect(rect.Left + 1f, headerBottom - 0.5f, rect.Right - 1f, headerBottom + 0.5f),
                Theme.Border);
        }
    }

    /// <summary>
    /// Arms a press on the header, which the matching mouse-up turns into a toggle.
    /// </summary>
    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);

        if (e.Button != MouseButtons.Left || !IsInHeader(e.Location))
        {
            return;
        }

        _headerPressed = true;
        CaptureMouse();
        Invalidate();
    }

    /// <summary>
    /// Toggles <see cref="IsExpanded"/> when the press was armed on the header and the button is
    /// released over it, the way a button takes a click.
    /// </summary>
    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);

        if (e.Button != MouseButtons.Left || !_headerPressed)
        {
            return;
        }

        _headerPressed = false;
        ReleaseMouseCapture();
        Invalidate();

        if (IsInHeader(e.Location))
        {
            IsExpanded = !IsExpanded;
        }
    }

    /// <summary>
    /// Tracks whether the pointer is over the header, and cancels a header press that drags off it.
    /// </summary>
    protected override void OnMouseMove(MouseEventArgs e)
    {
        bool hot = IsInHeader(e.Location);
        if (hot != _headerHot)
        {
            _headerHot = hot;
            Invalidate();
        }

        // Dragging off the header cancels the press, the way it does on a button.
        if (_headerPressed && !hot)
        {
            _headerPressed = false;
            ReleaseMouseCapture();
            Invalidate();
        }

        base.OnMouseMove(e);
    }

    /// <summary>Clears the header's hover and press state when the pointer leaves the control.</summary>
    protected override void OnMouseLeave()
    {
        _headerHot = false;

        if (_headerPressed)
        {
            _headerPressed = false;
            ReleaseMouseCapture();
        }

        base.OnMouseLeave();
    }

    /// <summary>
    /// Enter and Space toggle the section, Right opens it when it is shut and Left shuts it when it
    /// is open.
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
            case Key.Enter:
            case Key.Space:
                IsExpanded = !IsExpanded;
                e.Handled = true;
                break;

            case Key.Right when !IsExpanded:
                IsExpanded = true;
                e.Handled = true;
                break;

            case Key.Left when IsExpanded:
                IsExpanded = false;
                e.Handled = true;
                break;
        }
    }

    /// <summary>Repaints, because the header draws a focus ring only while the section has focus.</summary>
    protected override void OnGotFocus()
    {
        base.OnGotFocus();
        Invalidate();
    }

    /// <summary>
    /// Drops a header press when the focus leaves, so coming back does not toggle the section, and
    /// repaints the focus ring away.
    /// </summary>
    protected override void OnLostFocus()
    {
        base.OnLostFocus();

        if (_headerPressed)
        {
            _headerPressed = false;
            ReleaseMouseCapture();
        }

        Invalidate();
    }

    /// <summary>Clears the header's hover and press state when the control is disabled.</summary>
    protected override void OnEnabledChanged()
    {
        if (!Enabled)
        {
            _headerHot = false;
            _headerPressed = false;
            ReleaseMouseCapture();
        }

        base.OnEnabledChanged();
    }

    /// <summary>Draws the focus ring around the header band while the section has focus.</summary>
    protected override void OnPaintBorder(SKCanvas canvas, SKRect rect)
    {
        base.OnPaintBorder(canvas, rect);

        if (IsFocused && Enabled)
        {
            var header = new SKRect(rect.Left, rect.Top, rect.Right, rect.Top + HeaderHeight);
            Painter.DrawFocusRing(canvas, header, Theme.FocusRing, 2f, CornerRadius);
        }
    }

    private bool IsInHeader(SKPoint point) => point.Y < HeaderHeight;

    /// <summary>
    /// Puts the section where <see cref="ExpandProgress"/> says: its own height first, then whatever
    /// is docked under it, which has to move with the fold.
    /// </summary>
    private void ApplyProgress()
    {
        if (!AutoSize)
        {
            float open = ExpandedHeight;
            Height = HeaderHeight + (MathF.Max(0f, open - HeaderHeight) * (float)_progress);

            // Fully open is the height worth remembering: it is the one a later fold has to come back
            // to, and reading it here is what keeps a fold reversed halfway from remembering the
            // height it happened to be at.
            if (_progress >= 1d && Height > HeaderHeight)
            {
                _expandedHeight = Height;
            }
        }

        // The section just changed how much room it needs, so whatever is docked below it has to
        // move: a control that changes its own height is the only one that knows a layout pass is
        // due outside the one that placed it.
        Parent?.PerformLayout();
        PerformLayout();
        Invalidate();
    }

    private void PaintChevron(SKCanvas canvas, SKRect cell, SKColor color)
    {
        float centerX = cell.MidX;
        float centerY = cell.MidY;
        float half = 3.5f;

        // One chevron, pointing right, turned by the progress. Drawing a second shape for the open
        // state is what let the two arrows drift apart; there is nothing left to drift.
        using var builder = new SKPathBuilder();
        builder.MoveTo(centerX - (half * 0.5f), centerY - half);
        builder.LineTo(centerX + (half * 0.7f), centerY);
        builder.LineTo(centerX - (half * 0.5f), centerY + half);

        using var path = builder.Snapshot();
        using var paint = new SKPaint
        {
            Color = color,
            IsAntialias = true,
            Style = SKPaintStyle.Stroke,
            StrokeWidth = 1.6f,
            StrokeCap = SKStrokeCap.Round,
            StrokeJoin = SKStrokeJoin.Round,
        };

        if (ArrowRotationDegrees == 0d)
        {
            canvas.DrawPath(path, paint);
            return;
        }

        canvas.Save();
        canvas.RotateDegrees((float)ArrowRotationDegrees, centerX, centerY);
        canvas.DrawPath(path, paint);
        canvas.Restore();
    }
}
