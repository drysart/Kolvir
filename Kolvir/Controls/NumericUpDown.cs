using System.Globalization;
using SkiaSharp;

namespace Kolvir;

/// <summary>
/// A number field: an <see cref="TextBox"/> with a spinner pair in its right-hand edge, so the
/// value can be typed or stepped with the arrow keys, the mouse wheel or the buttons.
/// </summary>
/// <remarks>
/// <para>
/// The value is a <see cref="decimal"/> rather than a float, so stepping a money amount a hundred
/// times lands on the amount and not on a rounding artefact. <see cref="DecimalPlaces"/> decides
/// how many of those decimals are shown, and every value that comes in - typed, stepped or
/// assigned - is rounded to it and clamped to <see cref="Minimum"/>..<see cref="Maximum"/>.
/// </para>
/// <para>
/// Typing is free-form within the characters a number can be made of: the text is parsed as it
/// changes and <see cref="Value"/> follows, but the text is only rewritten from the value when the
/// edit is committed - on <see cref="Key.Enter"/>, when the focus leaves, or on any step - so
/// a half-typed "-" or "1." is never taken away from the user. Parsing and formatting use the
/// current culture, so the decimal separator is the one the user's keyboard produces.
/// </para>
/// </remarks>
public class NumericUpDown : TextBox
{
    private const float SpinnerWidth = 17f;
    private const float SpinnerInset = 3f;
    private const float SpinnerGap = 2f;

    /// <summary>How long a held button waits before it starts repeating.</summary>
    private static readonly TimeSpan RepeatDelay = TimeSpan.FromMilliseconds(400);

    /// <summary>How often a held button repeats once it has started.</summary>
    private static readonly TimeSpan RepeatInterval = TimeSpan.FromMilliseconds(60);

    private Action<object>? _valueChanged;
    private decimal _minimum;
    private decimal _maximum = 100m;
    private decimal _increment = 1m;
    private decimal _value;
    private int _decimalPlaces;
    private bool _syncing;

    // 0 when no spinner button is involved, 1 for up, 2 for down.
    private int _pressedButton;
    private int _hotButton;
    private Timer? _repeat;

    /// <summary>
    /// Creates a field showing 0, with a range of 0 to 100, whole-number increments of 1 and no
    /// decimal places.
    /// </summary>
    public NumericUpDown()
    {
        SyncText();
    }

    /// <summary>
    /// Creates a field showing <paramref name="value"/>, clamped to the default range and rounded to
    /// the default number of decimals the way any assignment to <see cref="Value"/> is.
    /// </summary>
    public NumericUpDown(decimal value)
        : this()
    {
        Value = value;
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

    /// <summary>The value that <see cref="Value"/> cannot go below. Defaults to 0.</summary>
    /// <remarks>A minimum above <see cref="Maximum"/> is not rejected: the two are ordered when a value is clamped.</remarks>
    public decimal Minimum
    {
        get => _minimum;
        set
        {
            if (_minimum == value)
            {
                return;
            }

            _minimum = value;
            SetValue(_value, reformat: true);
        }
    }

    /// <summary>The value that <see cref="Value"/> cannot go above. Defaults to 100.</summary>
    public decimal Maximum
    {
        get => _maximum;
        set
        {
            if (_maximum == value)
            {
                return;
            }

            _maximum = value;
            SetValue(_value, reformat: true);
        }
    }

    /// <summary>The current value, rounded to <see cref="DecimalPlaces"/> and clamped to the range.</summary>
    public decimal Value
    {
        get => _value;
        set => SetValue(value, reformat: true);
    }

    /// <summary>How far one step - a button press, an arrow key or a wheel detent - moves the value. Defaults to 1.</summary>
    /// <exception cref="ArgumentOutOfRangeException">The value is not positive.</exception>
    public decimal Increment
    {
        get => _increment;
        set
        {
            if (value <= 0m)
            {
                throw new ArgumentOutOfRangeException(nameof(value), value, "The increment must be positive.");
            }

            _increment = value;
        }
    }

    /// <summary>How many decimals the value is shown and held with. Defaults to 0.</summary>
    /// <exception cref="ArgumentOutOfRangeException">The value is outside 0..28, the range a decimal can hold.</exception>
    public int DecimalPlaces
    {
        get => _decimalPlaces;
        set
        {
            if (value is < 0 or > 28)
            {
                throw new ArgumentOutOfRangeException(nameof(value), value, "The number of decimals must be between 0 and 28.");
            }

            if (_decimalPlaces == value)
            {
                return;
            }

            _decimalPlaces = value;
            SetValue(_value, reformat: true);
        }
    }

    /// <summary>Moves <see cref="Value"/> on by one <see cref="Increment"/>.</summary>
    public void Up() => Step(1m);

    /// <summary>Moves <see cref="Value"/> back by one <see cref="Increment"/>.</summary>
    public void Down() => Step(-1m);

    /// <summary>
    /// Reserves the spinner's width at the right-hand end of the field, so the text and the caret
    /// stop short of the buttons instead of running underneath them.
    /// </summary>
    protected override Thickness ContentPadding
    {
        get
        {
            var content = base.ContentPadding;
            return new(content.Left, content.Top, content.Right + SpinnerWidth + SpinnerInset + SpinnerGap, content.Bottom);
        }
    }

    /// <summary>Raises <see cref="AddValueChangedHandler"/>.</summary>
    protected virtual void OnValueChanged() => _valueChanged?.Invoke(this);

    /// <inheritdoc />
    /// <remarks>The spinner's width is added to what the text box measures, since it sits beside
    /// the text rather than over it.</remarks>
    protected override SKSize MeasureContent(SKSize availableSize)
    {
        var size = base.MeasureContent(availableSize);
        return new SKSize(size.Width + SpinnerWidth + SpinnerInset + SpinnerGap, size.Height);
    }

    /// <summary>The spinner is chrome rather than text, so the pointer stays an arrow over it.</summary>
    public override Cursor GetCursorAt(SKPoint pointInLocal) =>
        ButtonFromPoint(pointInLocal) != 0 ? Cursor.Default : base.GetCursorAt(pointInLocal);

    /// <summary>Paints the text field as a text box does, then the spinner over its right-hand edge.</summary>
    protected override void OnPaint(SKCanvas canvas, SKRect rect)
    {
        base.OnPaint(canvas, rect);
        PaintSpinner(canvas);
    }

    /// <summary>
    /// Steps the value once for a press on either spinner button and starts the repeat that keeps
    /// stepping while the button is held. A press anywhere else is the text box's, so the caret and
    /// the selection stay where they were.
    /// </summary>
    protected override void OnMouseDown(MouseEventArgs e)
    {
        if (e.Button != MouseButtons.Left || !Enabled)
        {
            base.OnMouseDown(e);
            return;
        }

        int button = ButtonFromPoint(e.Location);
        if (button == 0)
        {
            _pressedButton = 0;
            StopRepeat();
            base.OnMouseDown(e);
            return;
        }

        // A press on a button is not a press in the text: the caret and the selection stay where
        // they were, which is what makes the buttons usable while a value is half-typed.
        _pressedButton = button;
        _hotButton = button;
        Focus();
        CaptureMouse();
        Step(button == 1 ? 1m : -1m);
        StartRepeat();
        Invalidate();
    }

    /// <summary>
    /// Tracks which spinner button the pointer is over, and cancels a press that slides off the
    /// button it started on, repeat included.
    /// </summary>
    protected override void OnMouseMove(MouseEventArgs e)
    {
        int button = ButtonFromPoint(e.Location);
        if (button != _hotButton)
        {
            _hotButton = button;
            Invalidate();
        }

        if (_pressedButton != 0 && button != _pressedButton)
        {
            // Dragging off a button cancels it, the way a native spinner does, and the repeat goes
            // with it so a press that slid away cannot keep counting.
            _pressedButton = 0;
            StopRepeat();
            Invalidate();
        }

        base.OnMouseMove(e);
    }

    /// <summary>Releases a spinner press, which stops the repeat and the capture it took.</summary>
    protected override void OnMouseUp(MouseEventArgs e)
    {
        if (_pressedButton != 0)
        {
            _pressedButton = 0;
            StopRepeat();
            ReleaseMouseCapture();
            Invalidate();
        }

        base.OnMouseUp(e);
    }

    /// <summary>Clears the hot button and any press, stopping the repeat, when the pointer leaves.</summary>
    protected override void OnMouseLeave()
    {
        base.OnMouseLeave();

        if (_hotButton != 0)
        {
            _hotButton = 0;
            Invalidate();
        }

        if (_pressedButton != 0)
        {
            _pressedButton = 0;
            StopRepeat();
            Invalidate();
        }
    }

    /// <summary>
    /// Steps the value one <see cref="Increment"/> per wheel detent - up for a turn away from the
    /// user - and claims the turn so it does not also scroll a panel above the field.
    /// </summary>
    protected override void OnMouseWheel(MouseEventArgs e)
    {
        base.OnMouseWheel(e);

        if (e.Handled || !Enabled || e.Delta == 0f)
        {
            return;
        }

        Step(e.Delta > 0f ? 1m : -1m);
        e.Handled = true;
    }

    /// <summary>
    /// Up and Down step by one increment, Page Up and Page Down by ten, and Enter commits the text,
    /// rewriting it from the value so an out-of-range entry or stray leading zeros come back tidy.
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
            case Key.Up:
                Step(1m);
                e.Handled = true;
                break;

            case Key.Down:
                Step(-1m);
                e.Handled = true;
                break;

            case Key.PageUp:
                Step(10m);
                e.Handled = true;
                break;

            case Key.PageDown:
                Step(-10m);
                e.Handled = true;
                break;

            case Key.Enter:
                // Commit: the value is already following the text, so this only rewrites the text
                // from it - which is how "007" becomes "7" and an out-of-range number comes back.
                SyncText();
                e.Handled = true;
                break;
        }
    }

    /// <summary>
    /// Drops a character a number cannot contain before the text box inserts it, and a second
    /// decimal separator, so an ordinary typo never has to be undone by the commit that follows.
    /// </summary>
    protected override void OnTextInput(TextInputEventArgs e)
    {
        if (!e.Handled && !ReadOnly && !IsNumericInput(e.Text))
        {
            e.Handled = true;
            return;
        }

        base.OnTextInput(e);
    }

    /// <summary>
    /// Parses the text as it changes and moves <see cref="Value"/> with it, leaving the text exactly
    /// as typed so a half-written number is never rewritten under the caret.
    /// </summary>
    protected override void OnTextChanged()
    {
        base.OnTextChanged();

        if (_syncing || !TryParse(Text, out var parsed))
        {
            return;
        }

        // While the user is typing the text is left exactly as it is - only the value follows it.
        var coerced = Coerce(parsed);
        if (coerced != _value)
        {
            _value = coerced;
            OnValueChanged();
        }
    }

    /// <summary>Selects the whole number, since a field that holds one value is easier to replace
    /// than to edit.</summary>
    protected override void OnGotFocus()
    {
        base.OnGotFocus();

        // A field whose whole content is one number is easier to replace than to edit.
        SelectAll();
    }

    /// <summary>
    /// Ends a spinner press and commits the edit by rewriting the text from the value.
    /// </summary>
    protected override void OnLostFocus()
    {
        base.OnLostFocus();

        _pressedButton = 0;
        StopRepeat();
        SyncText();
    }

    /// <summary>Clears the spinner's hover and press state, and stops the repeat, when disabled.</summary>
    protected override void OnEnabledChanged()
    {
        if (!Enabled)
        {
            _pressedButton = 0;
            _hotButton = 0;
            StopRepeat();
        }

        base.OnEnabledChanged();
    }

    private SKRect SpinnerBounds
    {
        get
        {
            var client = ClientRect;
            return new SKRect(
                client.Right - SpinnerInset - SpinnerWidth,
                client.Top + SpinnerInset,
                client.Right - SpinnerInset,
                client.Bottom - SpinnerInset);
        }
    }

    /// <summary>The button a point falls on: 1 for up, 2 for down, 0 for neither.</summary>
    private int ButtonFromPoint(SKPoint point)
    {
        var spinner = SpinnerBounds;
        if (spinner.Width <= 0f || spinner.Height <= 0f || !Geometry.Contains(spinner, point))
        {
            return 0;
        }

        return point.Y < spinner.MidY ? 1 : 2;
    }

    private void Step(decimal direction) => SetValue(_value + (_increment * direction), reformat: true);

    /// <summary>
    /// The one path every value change goes through: round, clamp, rewrite the text when the change
    /// came from somewhere other than the keyboard, and tell the handlers if it really moved.
    /// </summary>
    private void SetValue(decimal value, bool reformat)
    {
        var coerced = Coerce(value);
        bool changed = coerced != _value;
        _value = coerced;

        if (reformat)
        {
            SyncText();
        }

        if (changed)
        {
            OnValueChanged();
        }
    }

    private decimal Coerce(decimal value)
    {
        // Min and Max are ordered rather than rejected, so assigning them in either order works.
        decimal floor = _minimum <= _maximum ? _minimum : _maximum;
        decimal ceiling = _minimum <= _maximum ? _maximum : _minimum;

        value = Math.Clamp(value, floor, ceiling);

        if (_decimalPlaces <= 0)
        {
            return Math.Clamp(Math.Round(value, 0, MidpointRounding.AwayFromZero), floor, ceiling);
        }

        // Rounding a value that sat on a bound can move it past the bound, so clamp again.
        return Math.Clamp(Math.Round(value, _decimalPlaces, MidpointRounding.AwayFromZero), floor, ceiling);
    }

    private string Format(decimal value) => value.ToString("F" + _decimalPlaces.ToString(CultureInfo.InvariantCulture), CultureInfo.CurrentCulture);

    private void SyncText()
    {
        string text = Format(_value);
        if (string.Equals(Text, text, StringComparison.Ordinal))
        {
            return;
        }

        _syncing = true;
        try
        {
            Text = text;
        }
        finally
        {
            _syncing = false;
        }

        if (IsFocused)
        {
            // A step that rewrote the text leaves the caret somewhere in the middle of a number
            // the user is about to replace anyway.
            SelectAll();
        }
    }

    private static bool TryParse(string text, out decimal value) =>
        decimal.TryParse(text, NumberStyles.Number, CultureInfo.CurrentCulture, out value);

    /// <summary>
    /// True when every character of <paramref name="text"/> can appear in a number, and a second
    /// decimal separator is not being added to one that already has a separator outside the
    /// selection.
    /// </summary>
    private bool IsNumericInput(string text)
    {
        if (text.Length == 0)
        {
            return false;
        }

        var format = CultureInfo.CurrentCulture.NumberFormat;
        string separator = format.NumberDecimalSeparator;

        bool addsSeparator = false;
        foreach (char c in text)
        {
            if (char.IsDigit(c) || c == format.NegativeSign[0] || c == format.NumberGroupSeparator[0])
            {
                continue;
            }

            if (separator.Length == 1 && c == separator[0])
            {
                addsSeparator = true;
                continue;
            }

            return false;
        }

        if (!addsSeparator || separator.Length != 1)
        {
            return true;
        }

        string remaining = Text.Remove(SelectionStart, SelectionLength);
        return !remaining.Contains(separator, StringComparison.Ordinal);
    }

    private void StartRepeat()
    {
        _repeat ??= CreateTimer(RepeatDelay, OnRepeatTick);
        _repeat.Interval = RepeatDelay;
        _repeat.Start();
    }

    private void StopRepeat()
    {
        if (_repeat is { Enabled: true } timer)
        {
            timer.Stop();
        }
    }

    private void OnRepeatTick(object sender)
    {
        if (_pressedButton == 0)
        {
            StopRepeat();
            return;
        }

        if (_repeat is { } timer && timer.Interval != RepeatInterval)
        {
            // The first tick ends the wait and starts the run.
            timer.Interval = RepeatInterval;
        }

        Step(_pressedButton == 1 ? 1m : -1m);
    }

    private void PaintSpinner(SKCanvas canvas)
    {
        var spinner = SpinnerBounds;
        if (spinner.Width < 6f || spinner.Height < 10f)
        {
            return;
        }

        Painter.Fill(canvas, spinner, Enabled ? Theme.Secondary : Theme.DisabledBackground, 3f);

        float mid = spinner.MidY;
        var up = new SKRect(spinner.Left, spinner.Top, spinner.Right, mid);
        var down = new SKRect(spinner.Left, mid, spinner.Right, spinner.Bottom);

        PaintButton(canvas, up, _pressedButton == 1, _hotButton == 1);
        PaintButton(canvas, down, _pressedButton == 2, _hotButton == 2);

        var glyph = Enabled ? Theme.MutedForeground : Theme.DisabledForeground;
        PaintChevron(canvas, up, pointingUp: true, glyph);
        PaintChevron(canvas, down, pointingUp: false, glyph);
    }

    private void PaintButton(SKCanvas canvas, SKRect rect, bool pressed, bool hot)
    {
        if (!Enabled || (!pressed && !hot))
        {
            return;
        }

        var fill = pressed ? Theme.SecondaryPressed : Theme.SecondaryHover;
        Painter.Fill(canvas, Geometry.Deflate(rect, 1f), fill, 2f);
    }

    private static void PaintChevron(SKCanvas canvas, SKRect cell, bool pointingUp, SKColor color)
    {
        float centerX = cell.MidX;
        float centerY = cell.MidY;
        float half = 4f;
        float height = 4f;

        using var builder = new SKPathBuilder();
        if (pointingUp)
        {
            builder.MoveTo(centerX - half, centerY + (height * 0.5f));
            builder.LineTo(centerX + half, centerY + (height * 0.5f));
            builder.LineTo(centerX, centerY - (height * 0.5f));
        }
        else
        {
            builder.MoveTo(centerX - half, centerY - (height * 0.5f));
            builder.LineTo(centerX + half, centerY - (height * 0.5f));
            builder.LineTo(centerX, centerY + (height * 0.5f));
        }

        builder.Close();

        using var path = builder.Snapshot();
        using var paint = new SKPaint
        {
            Color = color,
            IsAntialias = true,
            Style = SKPaintStyle.Fill,
        };

        canvas.DrawPath(path, paint);
    }
}
