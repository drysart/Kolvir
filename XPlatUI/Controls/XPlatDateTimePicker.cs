using System.Globalization;
using SkiaSharp;

namespace XPlatUI;

/// <summary>How an <see cref="XPlatDateTimePicker"/> writes its value.</summary>
public enum XPlatDateTimePickerFormat
{
    /// <summary>The culture's short date pattern (<c>11/4/2026</c> in en-US).</summary>
    Short = 0,

    /// <summary>The culture's long date pattern (<c>Wednesday, November 4, 2026</c>).</summary>
    Long = 1,

    /// <summary>The culture's short time pattern (<c>8:19 PM</c>).</summary>
    Time = 2,

    /// <summary>Whatever <see cref="XPlatDateTimePicker.CustomFormat"/> says.</summary>
    CustomFormat = 3,
}

/// <summary>
/// A date and time field with a calendar in a popup: the text can be typed, stepped segment by
/// segment from the keyboard, or picked from a month grid that hangs below the field.
/// </summary>
/// <remarks>
/// <para>
/// The field is an <see cref="XPlatTextBox"/>, so it has the caret, selection, clipboard and
/// editing keys every text box has. Typed text is parsed against the current culture and
/// <see cref="Format"/> when the edit is committed - on Enter, when the focus leaves, or when the
/// popup opens - and an entry that cannot be parsed reverts to the last good value instead of
/// throwing. Nothing is parsed while the user is still typing, so a half-typed date is never
/// rewritten under the caret.
/// </para>
/// <para>
/// The calendar is a control drawn inside an <see cref="XPlatPopup"/>, so it can leave the window
/// when the platform layer provides popup windows. As with <see cref="XPlatComboBox"/>, the field
/// keeps the keyboard while the popup is open: the arrow keys move the calendar's highlight,
/// Enter takes it, and Escape closes the popup.
/// </para>
/// </remarks>
public class XPlatDateTimePicker : XPlatTextBox
{
    private const float ButtonWidth = 22f;
    private const float ButtonGap = 2f;

    private readonly CalendarView _calendar;
    private readonly XPlatPopup _popup;
    private Action<object>? _valueChanged;
    private DateTime? _value;
    private DateTime _minDate = DateTime.MinValue;
    private DateTime _maxDate = DateTime.MaxValue;
    private XPlatDateTimePickerFormat _format = XPlatDateTimePickerFormat.Short;
    private string _customFormat = string.Empty;
    private bool _syncing;
    private bool _dropDownOpen;
    private bool _hotButton;

    public XPlatDateTimePicker()
    {
        TabStop = true;
        Cursor = XPlatCursor.IBeam;
        BorderWidth = 1f;
        CornerRadius = 6f;
        Padding = new XPlatThickness(8f, 6f, 8f, 6f);
        MinimumSize = new SKSize(120f, 30f);
        Size = new SKSize(180f, 30f);

        _calendar = Own(new CalendarView(this)
        {
            DateSelected = OnCalendarDateSelected,
            TodayRequested = OnCalendarTodayRequested,
            ClearRequested = OnCalendarClearRequested,
        });

        _popup = Own(new XPlatPopup(this) { Content = _calendar });
        Own(_popup.AddClosedHandler(OnPopupClosed));
    }

    /// <summary>Creates a picker showing <paramref name="value"/>.</summary>
    public XPlatDateTimePicker(DateTime value)
        : this()
    {
        Value = value;
    }

    /// <summary>Creates a picker whose field starts out with <paramref name="text"/>, parsed the way typed text is.</summary>
    public XPlatDateTimePicker(string text)
        : this()
    {
        Text = text;
        CommitText();
    }

    /// <summary>
    /// The value, or null when the field is empty. Assigning a value outside
    /// <see cref="MinDate"/>..<see cref="MaxDate"/> clamps it, and the text follows.
    /// </summary>
    public DateTime? Value
    {
        get => _value;
        set => SetValue(value, reformat: true);
    }

    /// <summary>The earliest date the value may take. Defaults to <see cref="DateTime.MinValue"/>.</summary>
    /// <remarks>
    /// A minimum later than <see cref="MaxDate"/> is not rejected: the two are ordered when a value
    /// is clamped, so assigning them in either order works.
    /// </remarks>
    public DateTime MinDate
    {
        get => _minDate;
        set
        {
            if (_minDate == value)
            {
                return;
            }

            _minDate = value;
            SetValue(_value, reformat: true);
        }
    }

    /// <summary>The latest date the value may take. Defaults to <see cref="DateTime.MaxValue"/>.</summary>
    public DateTime MaxDate
    {
        get => _maxDate;
        set
        {
            if (_maxDate == value)
            {
                return;
            }

            _maxDate = value;
            SetValue(_value, reformat: true);
        }
    }

    /// <summary>How the value is written and parsed. Defaults to <see cref="XPlatDateTimePickerFormat.Short"/>.</summary>
    public XPlatDateTimePickerFormat Format
    {
        get => _format;
        set
        {
            if (_format == value)
            {
                return;
            }

            _format = value;
            SyncText();
        }
    }

    /// <summary>The pattern used when <see cref="Format"/> is <see cref="XPlatDateTimePickerFormat.CustomFormat"/>.</summary>
    /// <remarks>An empty pattern falls back to the culture's short date pattern.</remarks>
    public string CustomFormat
    {
        get => _customFormat;
        set
        {
            value ??= string.Empty;

            if (string.Equals(_customFormat, value, StringComparison.Ordinal))
            {
                return;
            }

            _customFormat = value;

            if (_format == XPlatDateTimePickerFormat.CustomFormat)
            {
                SyncText();
            }
        }
    }

    /// <summary>Whether the calendar is open.</summary>
    public bool IsDropDownOpen => _dropDownOpen;

    /// <summary>Adds a handler that runs after <see cref="Value"/> has changed.</summary>
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

    /// <summary>Opens the calendar, if the picker is enabled and it is not already open.</summary>
    public void OpenDropDown()
    {
        if (_dropDownOpen || !Enabled || Host is not { } host)
        {
            return;
        }

        // A half-typed date is committed before the calendar takes over, so the grid opens on the
        // date the field is actually showing.
        CommitText();

        _calendar.Show(_value, DateTime.Today);

        var area = PlacementArea(host);
        var size = _calendar.Measure(new SKSize(area.Width, area.Height));
        var origin = GetPositionInHost();

        float x = Math.Clamp(origin.X, area.Left, MathF.Max(area.Left, area.Right - size.Width));
        float below = origin.Y + Height;
        float above = origin.Y - size.Height;

        // Like a drop-down list, the calendar opens downwards and flips above the field when there
        // is no room below it. The platform layer still clamps a window popup to the display.
        float y = below + size.Height <= area.Bottom || above < area.Top ? below : above;
        y = Math.Clamp(y, area.Top, MathF.Max(area.Top, area.Bottom - size.Height));

        _popup.Size = size;
        _popup.Show(new SKPoint(x, y));
        _dropDownOpen = true;
        Invalidate();
    }

    /// <summary>Closes the calendar, if it is open.</summary>
    public void CloseDropDown()
    {
        if (!_dropDownOpen)
        {
            return;
        }

        _popup.Close();
    }

    /// <summary>Reserves the calendar button's width at the right-hand end of the field.</summary>
    protected override XPlatThickness ContentPadding =>
        new(Padding.Left, Padding.Top, Padding.Right + ButtonWidth + ButtonGap, Padding.Bottom);

    /// <summary>Raises <see cref="AddValueChangedHandler"/>.</summary>
    protected virtual void OnValueChanged() => _valueChanged?.Invoke(this);

    protected override SKSize MeasureContent(SKSize availableSize)
    {
        var size = base.MeasureContent(availableSize);
        return new SKSize(size.Width + ButtonWidth + ButtonGap, size.Height);
    }

    /// <summary>The calendar button is chrome rather than text, so the pointer stays an arrow over it.</summary>
    public override XPlatCursor GetCursorAt(SKPoint pointInLocal) =>
        XPlatGeometry.Contains(ButtonBounds, pointInLocal) ? XPlatCursor.Default : base.GetCursorAt(pointInLocal);

    protected override void OnPaintBackground(SKCanvas canvas, SKRect rect)
    {
        base.OnPaintBackground(canvas, rect);

        if (IsFocused && Enabled)
        {
            XPlatPainter.DrawFocusRing(canvas, rect, XPlatTheme.FocusRing, 1.5f, CornerRadius);
        }
    }

    protected override void OnPaint(SKCanvas canvas, SKRect rect)
    {
        base.OnPaint(canvas, rect);
        PaintDropButton(canvas);
    }

    protected override void OnMouseDown(XPlatMouseEventArgs e)
    {
        if (e.Button == XPlatMouseButtons.Left && Enabled && XPlatGeometry.Contains(ButtonBounds, e.Location))
        {
            // A press on the button is not a press in the text: the caret and the selection stay
            // where they were, which is what makes the calendar usable while a date is half-typed.
            Focus();
            CommitText();

            if (_dropDownOpen)
            {
                CloseDropDown();
            }
            else
            {
                OpenDropDown();
            }

            Invalidate();
            return;
        }

        base.OnMouseDown(e);
    }

    protected override void OnMouseMove(XPlatMouseEventArgs e)
    {
        bool hot = Enabled && XPlatGeometry.Contains(ButtonBounds, e.Location);
        if (hot != _hotButton)
        {
            _hotButton = hot;
            Invalidate();
        }

        base.OnMouseMove(e);
    }

    protected override void OnMouseLeave()
    {
        if (_hotButton)
        {
            _hotButton = false;
            Invalidate();
        }

        base.OnMouseLeave();
    }

    protected override void OnKeyDown(XPlatKeyEventArgs e)
    {
        // Keys the picker owns are marked handled before the text box sees them, so its own
        // left/right character movement does not run. Calling base afterwards still notifies the
        // public key handlers, and the text box skips its editing switch because Handled is set.
        if (!e.Handled && Enabled && HandleKey(e))
        {
            e.Handled = true;
            base.OnKeyDown(e);
            return;
        }

        base.OnKeyDown(e);
    }

    protected override void OnTextInput(XPlatTextInputEventArgs e)
    {
        base.OnTextInput(e);

        // Typing is the field's business, so an open calendar gets out of the way.
        if (e.Handled && _dropDownOpen)
        {
            CloseDropDown();
        }
    }

    protected override void OnGotFocus()
    {
        base.OnGotFocus();

        // A field whose whole content is one date is easier to replace than to edit.
        SelectAll();
    }

    protected override void OnLostFocus()
    {
        base.OnLostFocus();
        CommitText();
        CloseDropDown();
    }

    protected override void OnEnabledChanged()
    {
        if (!Enabled)
        {
            _hotButton = false;
            CloseDropDown();
        }

        base.OnEnabledChanged();
    }

    protected override void OnVisibleChanged()
    {
        if (!Visible)
        {
            CloseDropDown();
        }

        base.OnVisibleChanged();
    }

    private SKRect ButtonBounds
    {
        get
        {
            var client = ClientRect;
            float right = client.Right - 3f;
            return new SKRect(right - ButtonWidth, client.Top + 3f, right, client.Bottom - 3f);
        }
    }

    private string Pattern
    {
        get
        {
            var format = CultureInfo.CurrentCulture.DateTimeFormat;

            return _format switch
            {
                XPlatDateTimePickerFormat.Long => format.LongDatePattern,
                XPlatDateTimePickerFormat.Time => format.ShortTimePattern,
                XPlatDateTimePickerFormat.CustomFormat => string.IsNullOrWhiteSpace(_customFormat)
                    ? format.ShortDatePattern
                    : _customFormat,
                _ => format.ShortDatePattern,
            };
        }
    }

    private bool HandleKey(XPlatKeyEventArgs e) => _dropDownOpen ? HandleOpenKey(e) : HandleClosedKey(e);

    private bool HandleOpenKey(XPlatKeyEventArgs e)
    {
        switch (e.Key)
        {
            case XPlatKey.Left:
                _calendar.MoveCursor(-1);
                return true;

            case XPlatKey.Right:
                _calendar.MoveCursor(1);
                return true;

            case XPlatKey.Up:
                _calendar.MoveCursor(-7);
                return true;

            case XPlatKey.Down:
                _calendar.MoveCursor(7);
                return true;

            case XPlatKey.PageUp:
                _calendar.MoveCursorMonth(-1);
                return true;

            case XPlatKey.PageDown:
                _calendar.MoveCursorMonth(1);
                return true;

            case XPlatKey.Enter:
                _calendar.SelectCursor();
                return true;

            case XPlatKey.Escape:
                CloseDropDown();
                return true;

            case XPlatKey.Tab:
                // Tab belongs to the host, which moves the focus; the calendar closes on the way.
                CloseDropDown();
                return false;

            default:
                return false;
        }
    }

    private bool HandleClosedKey(XPlatKeyEventArgs e)
    {
        bool control = (e.Modifiers & XPlatModifiers.Control) != 0;
        bool shift = (e.Modifiers & XPlatModifiers.Shift) != 0;
        bool alt = (e.Modifiers & XPlatModifiers.Alt) != 0;

        switch (e.Key)
        {
            case XPlatKey.Down when alt:
                OpenDropDown();
                return true;

            case XPlatKey.Up when alt:
                CloseDropDown();
                return true;

            // Left and right move between the date's segments rather than between characters, so
            // the caret always lands on something a step can change. Shift and Control keep the
            // text box's own selection and word movement.
            case XPlatKey.Left when !control && !shift:
                MoveSegment(-1);
                return true;

            case XPlatKey.Right when !control && !shift:
                MoveSegment(1);
                return true;

            case XPlatKey.Up:
                StepSegment(1);
                return true;

            case XPlatKey.Down:
                StepSegment(-1);
                return true;

            case XPlatKey.PageUp:
                StepMonths(-1);
                return true;

            case XPlatKey.PageDown:
                StepMonths(1);
                return true;

            case XPlatKey.Enter:
                CommitText();
                return true;

            case XPlatKey.Escape:
                // Escape gives up the edit and puts the last good value back.
                SyncText();
                return true;

            default:
                return false;
        }
    }

    private void StepSegment(int direction)
    {
        // A step is a commit, the way an arrow key in a native date field takes the text as it
        // stands: a valid entry is adopted and an invalid one reverts before the step.
        CommitText();

        var segments = CurrentSegments(out int index);
        DateTime basis = _value ?? DefaultValue();

        if (index < 0 || index >= segments.Count)
        {
            SetValue(basis.AddDays(direction), reformat: true);
            return;
        }

        DateTime next = segments[index].Kind switch
        {
            SegmentKind.Day => AddDaysSafe(basis, direction),
            SegmentKind.Month => AddMonthsSafe(basis, direction),
            SegmentKind.Year => AddYearsSafe(basis, direction),
            SegmentKind.Hour => AddTicksSafe(basis, direction * TimeSpan.TicksPerHour),
            SegmentKind.Minute => AddTicksSafe(basis, direction * TimeSpan.TicksPerMinute),
            SegmentKind.Second => AddTicksSafe(basis, direction * TimeSpan.TicksPerSecond),
            SegmentKind.AmPm => AddTicksSafe(basis, direction * 12 * TimeSpan.TicksPerHour),
            _ => basis,
        };

        SetValue(next, reformat: true);
        SelectSegment(index);
    }

    private void StepMonths(int direction)
    {
        CommitText();

        DateTime basis = _value ?? DefaultValue();
        SetValue(AddMonthsSafe(basis, direction), reformat: true);
    }

    private void MoveSegment(int delta)
    {
        CommitText();

        var segments = CurrentSegments(out int index);
        if (segments.Count == 0 || index < 0)
        {
            return;
        }

        index = Math.Clamp(index + delta, 0, segments.Count - 1);
        SelectSegment(index);
    }

    private void SelectSegment(int index)
    {
        var segments = CurrentSegments(out _);
        if (segments.Count == 0)
        {
            return;
        }

        index = Math.Clamp(index, 0, segments.Count - 1);
        var segment = segments[index];
        Select(segment.Start, segment.Length);
    }

    /// <summary>
    /// The segments of the text as it stands. When the field is mid-edit and its text no longer
    /// matches the value, the canonical text is used instead, so a step still has something to
    /// step even though the caret cannot be mapped onto it.
    /// </summary>
    private List<Segment> CurrentSegments(out int index)
    {
        DateTime value = _value ?? DefaultValue();
        string canonical = FormatValue(value);
        bool matches = string.Equals(Text, canonical, StringComparison.Ordinal);
        var segments = BuildSegments(matches ? Text : canonical, value, Pattern, CultureInfo.CurrentCulture);

        index = segments.Count == 0 ? -1 : matches ? IndexForCaret(segments, CaretIndex) : 0;
        return segments;
    }

    private DateTime DefaultValue() => Coerce(DateTime.Today);

    /// <summary>
    /// Date arithmetic that stops at the ends of <see cref="DateTime"/> instead of throwing, so a
    /// field with the default bounds can be stepped all the way to year 1 or 9999.
    /// </summary>
    private static DateTime AddDaysSafe(DateTime value, int days) =>
        AddTicksSafe(value, (long)days * TimeSpan.TicksPerDay);

    private static DateTime AddTicksSafe(DateTime value, long ticks)
    {
        long total = value.Ticks + ticks;

        return total <= DateTime.MinValue.Ticks
            ? DateTime.MinValue
            : total >= DateTime.MaxValue.Ticks ? DateTime.MaxValue : new DateTime(total, value.Kind);
    }

    private static DateTime AddMonthsSafe(DateTime value, int months)
    {
        // Year 1 is month index 12, not 0: the index is one-based by year.
        long totalMonths = ((long)value.Year * 12) + (value.Month - 1) + months;
        totalMonths = Math.Clamp(totalMonths, 12, (9999L * 12) + 11);

        int year = (int)(totalMonths / 12);
        int month = (int)(totalMonths % 12) + 1;
        int day = Math.Min(value.Day, DateTime.DaysInMonth(year, month));

        return new DateTime(year, month, day, 0, 0, 0, value.Kind) + value.TimeOfDay;
    }

    private static DateTime AddYearsSafe(DateTime value, int years) => AddMonthsSafe(value, years * 12);

    private DateTime Coerce(DateTime value)
    {
        // Min and Max are ordered rather than rejected, so assigning them in either order works.
        DateTime floor = (_minDate <= _maxDate ? _minDate : _maxDate).Date;
        DateTime ceiling = (_minDate <= _maxDate ? _maxDate : _minDate).Date;
        DateTime date = value.Date;

        // The bounds are dates, so a clamped value keeps its time of day: a time field at the last
        // allowed day does not jump to midnight just because the day cannot move further.
        if (date < floor)
        {
            date = floor;
        }
        else if (date > ceiling)
        {
            date = ceiling;
        }

        return date + value.TimeOfDay;
    }

    private void SetValue(DateTime? value, bool reformat)
    {
        DateTime? coerced = value is { } date ? Coerce(date) : null;
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

    private string FormatValue(DateTime value)
    {
        try
        {
            return value.ToString(Pattern, CultureInfo.CurrentCulture);
        }
        catch (FormatException)
        {
            // A hand-written CustomFormat can be invalid; showing the culture's own form beats
            // throwing out of a paint pass.
            return value.ToString(CultureInfo.CurrentCulture);
        }
    }

    private void SyncText()
    {
        string text = _value is { } value ? FormatValue(value) : string.Empty;
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
    }

    /// <summary>
    /// Turns the typed text into a value: an empty field clears it, a parseable entry becomes the
    /// value, and anything else is replaced by the last good value. Called on Enter, on focus loss
    /// and when the calendar opens.
    /// </summary>
    private void CommitText()
    {
        if (_syncing)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(Text))
        {
            SetValue(null, reformat: true);
            return;
        }

        if (TryParseValue(Text, out DateTime parsed))
        {
            SetValue(parsed, reformat: true);
        }
        else
        {
            SyncText();
        }
    }

    private bool TryParseValue(string text, out DateTime value)
    {
        var culture = CultureInfo.CurrentCulture;
        var format = culture.DateTimeFormat;
        const DateTimeStyles styles = DateTimeStyles.AllowWhiteSpaces;

        string primary = Pattern;

        if (TryParseExact(text, primary, culture, styles, out value))
        {
            return true;
        }

        foreach (string candidate in ParsePatterns(format))
        {
            if (!string.Equals(candidate, primary, StringComparison.Ordinal) &&
                TryParseExact(text, candidate, culture, styles, out value))
            {
                return true;
            }
        }

        // A culture's own parser is the last resort, so "11/4" or "3:30 PM" works even though it
        // is only part of the pattern the field writes.
        if (DateTime.TryParse(text, culture, styles, out var general))
        {
            value = general;
            return true;
        }

        value = default;
        return false;
    }

    private bool TryParseExact(string text, string pattern, CultureInfo culture, DateTimeStyles styles, out DateTime value)
    {
        value = default;

        if (string.IsNullOrEmpty(pattern))
        {
            return false;
        }

        DateTime parsed;
        try
        {
            if (!DateTime.TryParseExact(text, pattern, culture, styles, out parsed))
            {
                return false;
            }
        }
        catch (FormatException)
        {
            // A hand-written CustomFormat can be invalid; it is not a reason to throw out of a commit.
            return false;
        }

        var (hasDate, hasTime) = ClassifyPattern(pattern);

        // A date-only format keeps the time the field already held, and a time-only format keeps
        // its date, so editing one half never silently resets the other.
        if (hasDate && !hasTime && _value is { } oldDate)
        {
            parsed = parsed.Date + oldDate.TimeOfDay;
        }
        else if (hasTime && !hasDate)
        {
            parsed = (_value?.Date ?? DateTime.Today) + parsed.TimeOfDay;
        }

        value = parsed;
        return true;
    }

    private static IEnumerable<string> ParsePatterns(DateTimeFormatInfo format)
    {
        string shortDate = format.ShortDatePattern;
        string longDate = format.LongDatePattern;
        string shortTime = format.ShortTimePattern;

        yield return shortDate;
        yield return longDate;
        yield return shortTime;
        yield return shortDate + " " + shortTime;
        yield return longDate + " " + shortTime;
    }

    /// <summary>Whether a pattern writes a date, a time, or both, skipping quoted literals and escapes.</summary>
    private static (bool HasDate, bool HasTime) ClassifyPattern(string pattern)
    {
        bool hasDate = false;
        bool hasTime = false;

        for (int i = 0; i < pattern.Length; i++)
        {
            char c = pattern[i];

            if (c == '\\')
            {
                i++;
                continue;
            }

            if (c is '\'' or '"')
            {
                char quote = c;
                i++;
                while (i < pattern.Length && pattern[i] != quote)
                {
                    i++;
                }

                continue;
            }

            switch (c)
            {
                case 'd':
                case 'M':
                case 'y':
                    hasDate = true;
                    break;

                case 'h':
                case 'H':
                case 'm':
                case 's':
                case 't':
                case 'f':
                case 'F':
                    hasTime = true;
                    break;
            }
        }

        return (hasDate, hasTime);
    }

    /// <summary>
    /// Maps each editable token of the pattern onto the run of characters it produced, so the
    /// caret can be told which part of the date it is sitting on.
    /// </summary>
    private static List<Segment> BuildSegments(string text, DateTime value, string pattern, CultureInfo culture)
    {
        var segments = new List<Segment>(8);

        if (string.IsNullOrEmpty(text) || string.IsNullOrEmpty(pattern))
        {
            return segments;
        }

        int cursor = 0;

        for (int i = 0; i < pattern.Length; i++)
        {
            char c = pattern[i];

            if (c == '\\')
            {
                i++;
                continue;
            }

            if (c is '\'' or '"')
            {
                char quote = c;
                i++;
                while (i < pattern.Length && pattern[i] != quote)
                {
                    i++;
                }

                continue;
            }

            SegmentKind? kind = c switch
            {
                'd' => SegmentKind.Day,
                'M' => SegmentKind.Month,
                'y' => SegmentKind.Year,
                'h' or 'H' => SegmentKind.Hour,
                'm' => SegmentKind.Minute,
                's' => SegmentKind.Second,
                't' => SegmentKind.AmPm,
                _ => null,
            };

            if (kind is null)
            {
                continue;
            }

            int start = i;
            while (i + 1 < pattern.Length && pattern[i + 1] == c)
            {
                i++;
            }

            string token = pattern[start..(i + 1)];
            string rendered;

            try
            {
                // A single-character token would be read as a standard format - "d" is the whole
                // short date - so the percent prefix forces the custom specifier this is.
                rendered = value.ToString(token.Length == 1 ? "%" + token : token, culture);
            }
            catch (FormatException)
            {
                continue;
            }

            if (rendered.Length == 0)
            {
                continue;
            }

            cursor = Math.Min(cursor, text.Length);
            int found = text.IndexOf(rendered, cursor, StringComparison.Ordinal);
            if (found < 0)
            {
                continue;
            }

            segments.Add(new Segment(found, rendered.Length, kind.Value));
            cursor = found + rendered.Length;
        }

        return segments;
    }

    private static int IndexForCaret(List<Segment> segments, int caret)
    {
        int index = 0;

        for (int i = 0; i < segments.Count; i++)
        {
            if (segments[i].Start <= caret)
            {
                index = i;
            }
        }

        return index;
    }

    private void PaintDropButton(SKCanvas canvas)
    {
        var bounds = ButtonBounds;
        if (bounds.Width < 8f || bounds.Height < 10f)
        {
            return;
        }

        if (_dropDownOpen || _hotButton)
        {
            var fill = _dropDownOpen ? XPlatTheme.SecondaryPressed : XPlatTheme.SecondaryHover;
            XPlatPainter.Fill(canvas, XPlatGeometry.Deflate(bounds, 2f), fill, 4f);
        }

        var color = !Enabled
            ? XPlatTheme.DisabledForeground
            : _dropDownOpen ? XPlatTheme.Accent : XPlatTheme.MutedForeground;

        PaintCalendarGlyph(canvas, bounds, color);
    }

    private static void PaintCalendarGlyph(SKCanvas canvas, SKRect area, SKColor color)
    {
        const float size = 13f;

        var icon = new SKRect(
            area.MidX - (size / 2f),
            area.MidY - (size / 2f) + 1.5f,
            area.MidX + (size / 2f),
            area.MidY + (size / 2f) + 1.5f);

        XPlatPainter.Fill(canvas, new SKRect(icon.Left, icon.Top, icon.Right, icon.Top + 3.5f), color, 1f);
        XPlatPainter.DrawBorder(canvas, icon, color, 1f, 2f);

        // The two rings above the body, which is what makes the glyph read as a calendar rather
        // than a plain rectangle.
        float legTop = icon.Top - 2f;
        XPlatPainter.Fill(canvas, new SKRect(icon.Left + 3f, legTop, icon.Left + 4.2f, icon.Top + 1.5f), color);
        XPlatPainter.Fill(canvas, new SKRect(icon.Right - 4.2f, legTop, icon.Right - 3f, icon.Top + 1.5f), color);
    }

    private static SKRect PlacementArea(XPlatUiHost host)
    {
        var service = host.GetService<IXPlatPopupService>() ?? XPlatPopupService.Current;

        return service?.GetPlacementArea(host) ?? new SKRect(
            0f,
            0f,
            MathF.Max(1f, host.ClientSize.Width),
            MathF.Max(1f, host.ClientSize.Height));
    }

    private void OnCalendarDateSelected(DateTime date)
    {
        // The grid chooses a day, not a time: picking one keeps the time of day the field held,
        // the way a native calendar does.
        DateTime merged = date.Date + (_value?.TimeOfDay ?? TimeSpan.Zero);
        SetValue(merged, reformat: true);
        CloseDropDown();
    }

    private void OnCalendarTodayRequested()
    {
        DateTime today = DateTime.Today;

        // Picking "today" keeps the time of day the field already had, so a time field does not
        // jump to midnight.
        if (_value is { } value)
        {
            today = today.Date + value.TimeOfDay;
        }

        SetValue(today, reformat: true);
        CloseDropDown();
    }

    private void OnCalendarClearRequested()
    {
        SetValue(null, reformat: true);
        CloseDropDown();
    }

    private void OnPopupClosed(object sender)
    {
        _dropDownOpen = false;
        Invalidate();

        var focused = Host?.FocusedControl;
        if (Enabled && Visible && (focused is null || ReferenceEquals(focused, this)))
        {
            Focus();
        }
    }

    private enum SegmentKind
    {
        Day,
        Month,
        Year,
        Hour,
        Minute,
        Second,
        AmPm,
    }

    private readonly struct Segment
    {
        public Segment(int start, int length, SegmentKind kind)
        {
            Start = start;
            Length = length;
            Kind = kind;
        }

        public int Start { get; }

        public int Length { get; }

        public SegmentKind Kind { get; }
    }

    /// <summary>
    /// The month grid shown in the popup. It paints its own chrome and takes the pointer, but it
    /// never takes the keyboard focus: the field owns the keyboard and routes the keys here.
    /// </summary>
    private sealed class CalendarView : XPlatControl
    {
        private const float CellWidth = 34f;
        private const float CellHeight = 28f;
        private const float HeaderHeight = 34f;
        private const float WeekdayHeight = 22f;
        private const float FooterHeight = 34f;
        private const float Inset = 10f;
        private const int Columns = 7;
        private const int Rows = 6;
        private const int Cells = Columns * Rows;

        private const int TargetPrev = 1;
        private const int TargetNext = 2;
        private const int TargetToday = 3;
        private const int TargetClear = 4;
        private const int TargetDayBase = 100;

        private readonly XPlatDateTimePicker _owner;
        private DateTime _displayMonth;
        private DateTime _cursor;
        private DateTime? _selected;
        private int _hot = -1;
        private int _pressed = -1;

        internal CalendarView(XPlatDateTimePicker owner)
        {
            _owner = owner;
            TabStop = false;
            Focusable = false;
            Cursor = XPlatCursor.Default;
            CornerRadius = 10f;
        }

        internal Action<DateTime>? DateSelected { get; set; }

        internal Action? TodayRequested { get; set; }

        internal Action? ClearRequested { get; set; }

        internal void Show(DateTime? value, DateTime fallback)
        {
            _selected = value?.Date;
            _cursor = ClampToRange((value ?? fallback).Date);
            _displayMonth = FirstOfMonth(_cursor);
            _hot = -1;
            _pressed = -1;
            Invalidate();
        }

        internal void MoveCursor(int days)
        {
            _cursor = ClampToRange(AddDaysSafe(_cursor, days));
            _displayMonth = FirstOfMonth(_cursor);
            Invalidate();
        }

        internal void MoveCursorMonth(int months)
        {
            _cursor = ClampToRange(AddMonthsSafe(_cursor, months));
            _displayMonth = FirstOfMonth(_cursor);
            Invalidate();
        }

        internal void SelectCursor()
        {
            if (IsSelectable(_cursor))
            {
                DateSelected?.Invoke(_cursor);
            }
        }

        protected override SKSize MeasureContent(SKSize availableSize) => new(
            (Columns * CellWidth) + (Inset * 2f),
            Inset + HeaderHeight + WeekdayHeight + 2f + (Rows * CellHeight) + 4f + FooterHeight + Inset);

        protected override void OnPaintBackground(SKCanvas canvas, SKRect rect)
        {
            XPlatPainter.Fill(canvas, rect, XPlatTheme.Surface, CornerRadius);
            XPlatPainter.DrawBorder(canvas, rect, XPlatTheme.Border, 1f, CornerRadius);
        }

        protected override void OnPaint(SKCanvas canvas, SKRect rect)
        {
            PaintHeader(canvas);
            PaintWeekdays(canvas);
            PaintDays(canvas);
            PaintFooter(canvas);
        }

        protected override void OnMouseDown(XPlatMouseEventArgs e)
        {
            if (e.Button != XPlatMouseButtons.Left || !Enabled)
            {
                return;
            }

            _pressed = TargetFromPoint(e.Location);
            if (_pressed >= 0)
            {
                CaptureMouse();
                Invalidate();
            }
        }

        protected override void OnMouseMove(XPlatMouseEventArgs e)
        {
            int hot = TargetFromPoint(e.Location);
            if (hot != _hot)
            {
                _hot = hot;
                Invalidate();
            }
        }

        protected override void OnMouseUp(XPlatMouseEventArgs e)
        {
            if (_pressed < 0)
            {
                return;
            }

            int pressed = _pressed;
            _pressed = -1;
            ReleaseMouseCapture();
            Invalidate();

            // Press inside and release inside is a click; a press that slid off is cancelled.
            if (TargetFromPoint(e.Location) == pressed)
            {
                Activate(pressed);
            }
        }

        protected override void OnMouseLeave()
        {
            if (_hot >= 0)
            {
                _hot = -1;
                Invalidate();
            }
        }

        protected override void OnEnabledChanged()
        {
            if (!Enabled)
            {
                _hot = -1;
                _pressed = -1;
                ReleaseMouseCapture();
            }

            base.OnEnabledChanged();
        }

        private SKRect HeaderRect => new(Inset, Inset, ClientRect.Right - Inset, Inset + HeaderHeight);

        private SKRect WeekdayRect => new(
            Inset,
            HeaderRect.Bottom,
            ClientRect.Right - Inset,
            HeaderRect.Bottom + WeekdayHeight);

        private SKRect GridRect => new(
            Inset,
            WeekdayRect.Bottom + 2f,
            ClientRect.Right - Inset,
            WeekdayRect.Bottom + 2f + (Rows * CellHeight));

        private SKRect FooterRect => new(
            Inset,
            GridRect.Bottom + 4f,
            ClientRect.Right - Inset,
            GridRect.Bottom + 4f + FooterHeight);

        private SKRect PrevRect => new(
            HeaderRect.Left,
            HeaderRect.MidY - 14f,
            HeaderRect.Left + 28f,
            HeaderRect.MidY + 14f);

        private SKRect NextRect => new(
            HeaderRect.Right - 28f,
            HeaderRect.MidY - 14f,
            HeaderRect.Right,
            HeaderRect.MidY + 14f);

        private SKRect TodayRect => FooterButtonRect(FooterRect.Left, "Today", fromRight: false);

        private SKRect ClearRect => FooterButtonRect(FooterRect.Right, "Clear", fromRight: true);

        private SKRect FooterButtonRect(float edge, string text, bool fromRight)
        {
            float width = _owner.Font.MeasureWidth(text) + 20f;
            float height = MathF.Min(FooterHeight, 28f);
            float top = FooterRect.MidY - (height / 2f);

            return fromRight
                ? new SKRect(edge - width, top, edge, top + height)
                : new SKRect(edge, top, edge + width, top + height);
        }

        private SKRect CellRect(int index)
        {
            int row = index / Columns;
            int column = index % Columns;

            return new SKRect(
                GridRect.Left + (column * CellWidth) + 2f,
                GridRect.Top + (row * CellHeight) + 1f,
                GridRect.Left + ((column + 1) * CellWidth) - 2f,
                GridRect.Top + ((row + 1) * CellHeight) - 1f);
        }

        private void PaintHeader(SKCanvas canvas)
        {
            PaintChevronButton(canvas, PrevRect, pointingLeft: true, TargetPrev);
            PaintChevronButton(canvas, NextRect, pointingLeft: false, TargetNext);

            string title = _displayMonth.ToString(
                CultureInfo.CurrentCulture.DateTimeFormat.YearMonthPattern,
                CultureInfo.CurrentCulture);

            var inner = new SKRect(
                PrevRect.Right + 4f,
                HeaderRect.Top,
                NextRect.Left - 4f,
                HeaderRect.Bottom);

            canvas.Save();
            canvas.ClipRect(inner);
            XPlatTextRenderer.Draw(
                canvas,
                title,
                XPlatTheme.EmphasizedFont,
                XPlatTheme.Foreground,
                inner,
                XPlatContentAlignment.MiddleCenter);
            canvas.Restore();
        }

        private void PaintChevronButton(SKCanvas canvas, SKRect bounds, bool pointingLeft, int target)
        {
            if (_pressed == target)
            {
                XPlatPainter.Fill(canvas, bounds, XPlatTheme.SecondaryPressed, 6f);
            }
            else if (_hot == target)
            {
                XPlatPainter.Fill(canvas, bounds, XPlatTheme.SecondaryHover, 6f);
            }

            float centerX = bounds.MidX;
            float centerY = bounds.MidY;
            const float half = 3.5f;

            using var builder = new SKPathBuilder();
            if (pointingLeft)
            {
                builder.MoveTo(centerX + (half * 0.5f), centerY - half);
                builder.LineTo(centerX - (half * 0.5f), centerY);
                builder.LineTo(centerX + (half * 0.5f), centerY + half);
            }
            else
            {
                builder.MoveTo(centerX - (half * 0.5f), centerY - half);
                builder.LineTo(centerX + (half * 0.5f), centerY);
                builder.LineTo(centerX - (half * 0.5f), centerY + half);
            }

            using var path = builder.Snapshot();
            using var paint = new SKPaint
            {
                Color = XPlatTheme.MutedForeground,
                IsAntialias = true,
                Style = SKPaintStyle.Stroke,
                StrokeWidth = 1.6f,
                StrokeCap = SKStrokeCap.Round,
                StrokeJoin = SKStrokeJoin.Round,
            };

            canvas.DrawPath(path, paint);
        }

        private void PaintWeekdays(SKCanvas canvas)
        {
            var format = CultureInfo.CurrentCulture.DateTimeFormat;
            int first = (int)format.FirstDayOfWeek;
            var area = WeekdayRect;

            for (int column = 0; column < Columns; column++)
            {
                var cell = new SKRect(
                    area.Left + (column * CellWidth),
                    area.Top,
                    area.Left + ((column + 1) * CellWidth),
                    area.Bottom);

                string text = format.AbbreviatedDayNames[(first + column) % 7];
                XPlatTextRenderer.Draw(
                    canvas,
                    text,
                    XPlatTheme.CaptionFont,
                    XPlatTheme.MutedForeground,
                    cell,
                    XPlatContentAlignment.MiddleCenter);
            }
        }

        private void PaintDays(SKCanvas canvas)
        {
            for (int index = 0; index < Cells; index++)
            {
                DateTime date = DateAt(index);
                var cell = CellRect(index);
                var inner = XPlatGeometry.Deflate(cell, 1f);

                bool inMonth = date.Month == _displayMonth.Month && date.Year == _displayMonth.Year;
                bool selectable = IsSelectable(date);
                bool selected = _selected is { } selectedDate && selectedDate.Date == date.Date;
                bool today = date.Date == DateTime.Today;
                bool cursor = date.Date == _cursor.Date;
                int target = TargetDayBase + index;

                if (selected)
                {
                    XPlatPainter.Fill(canvas, inner, XPlatTheme.Accent, 6f);
                }
                else if (selectable && _pressed == target)
                {
                    XPlatPainter.Fill(canvas, inner, XPlatTheme.SecondaryPressed, 6f);
                }
                else if (selectable && _hot == target)
                {
                    XPlatPainter.Fill(canvas, inner, XPlatTheme.SecondaryHover, 6f);
                }
                else if (cursor)
                {
                    XPlatPainter.Fill(canvas, inner, XPlatTheme.Secondary, 6f);
                }

                if (today && !selected)
                {
                    XPlatPainter.DrawBorder(
                        canvas,
                        XPlatGeometry.Deflate(cell, 2f),
                        XPlatTheme.Accent,
                        1f,
                        5f);
                }

                var color = !selectable
                    ? XPlatTheme.DisabledForeground
                    : selected
                        ? XPlatTheme.AccentForeground
                        : inMonth ? XPlatTheme.Foreground : XPlatTheme.MutedForeground;

                XPlatTextRenderer.Draw(
                    canvas,
                    date.Day.ToString(CultureInfo.CurrentCulture),
                    _owner.Font,
                    color,
                    cell,
                    XPlatContentAlignment.MiddleCenter);
            }
        }

        private void PaintFooter(SKCanvas canvas)
        {
            PaintTextButton(canvas, TodayRect, "Today", TargetToday, enabled: true);
            PaintTextButton(canvas, ClearRect, "Clear", TargetClear, enabled: _selected.HasValue);
        }

        private void PaintTextButton(SKCanvas canvas, SKRect bounds, string text, int target, bool enabled)
        {
            if (enabled && _pressed == target)
            {
                XPlatPainter.Fill(canvas, bounds, XPlatTheme.SecondaryPressed, 5f);
            }
            else if (enabled && _hot == target)
            {
                XPlatPainter.Fill(canvas, bounds, XPlatTheme.SecondaryHover, 5f);
            }

            var color = !enabled
                ? XPlatTheme.DisabledForeground
                : target == TargetClear ? XPlatTheme.MutedForeground : XPlatTheme.Foreground;

            XPlatTextRenderer.Draw(
                canvas,
                text,
                _owner.Font,
                color,
                bounds,
                XPlatContentAlignment.MiddleCenter);
        }

        private int TargetFromPoint(SKPoint point)
        {
            if (XPlatGeometry.Contains(PrevRect, point))
            {
                return TargetPrev;
            }

            if (XPlatGeometry.Contains(NextRect, point))
            {
                return TargetNext;
            }

            if (XPlatGeometry.Contains(TodayRect, point))
            {
                return TargetToday;
            }

            if (_selected.HasValue && XPlatGeometry.Contains(ClearRect, point))
            {
                return TargetClear;
            }

            for (int index = 0; index < Cells; index++)
            {
                if (XPlatGeometry.Contains(CellRect(index), point))
                {
                    return IsSelectable(DateAt(index)) ? TargetDayBase + index : -1;
                }
            }

            return -1;
        }

        private void Activate(int target)
        {
            switch (target)
            {
                case TargetPrev:
                    MoveDisplayMonth(-1);
                    break;

                case TargetNext:
                    MoveDisplayMonth(1);
                    break;

                case TargetToday:
                    TodayRequested?.Invoke();
                    break;

                case TargetClear:
                    ClearRequested?.Invoke();
                    break;

                default:
                    if (target >= TargetDayBase)
                    {
                        DateTime date = DateAt(target - TargetDayBase);
                        if (IsSelectable(date))
                        {
                            DateSelected?.Invoke(date);
                        }
                    }

                    break;
            }
        }

        private void MoveDisplayMonth(int months)
        {
            DateTime previous = _displayMonth;
            _displayMonth = FirstOfMonth(AddMonthsSafe(previous, months));

            // The highlight follows the month being looked at, but only when it was in the month
            // that was on screen, so browsing does not drag a highlight in from somewhere else.
            if (_cursor.Year == previous.Year && _cursor.Month == previous.Month)
            {
                int day = Math.Min(_cursor.Day, DateTime.DaysInMonth(_displayMonth.Year, _displayMonth.Month));
                _cursor = ClampToRange(new DateTime(_displayMonth.Year, _displayMonth.Month, day));
            }

            Invalidate();
        }

        private DateTime FirstCellDate()
        {
            var first = new DateTime(_displayMonth.Year, _displayMonth.Month, 1);
            int firstDay = (int)CultureInfo.CurrentCulture.DateTimeFormat.FirstDayOfWeek;
            int offset = ((int)first.DayOfWeek - firstDay + 7) % 7;
            long ticks = first.Ticks - ((long)offset * TimeSpan.TicksPerDay);

            return ticks < DateTime.MinValue.Ticks ? DateTime.MinValue : new DateTime(ticks);
        }

        private DateTime DateAt(int index)
        {
            long ticks = FirstCellDate().Ticks + ((long)index * TimeSpan.TicksPerDay);

            return ticks > DateTime.MaxValue.Ticks ? DateTime.MaxValue.Date : new DateTime(ticks);
        }

        private static DateTime FirstOfMonth(DateTime value) => new(value.Year, value.Month, 1);

        private bool IsSelectable(DateTime value)
        {
            DateTime min = _owner.MinDate.Date;
            DateTime max = _owner.MaxDate.Date;

            if (min > max)
            {
                (min, max) = (max, min);
            }

            return value.Date >= min && value.Date <= max;
        }

        private DateTime ClampToRange(DateTime value)
        {
            DateTime min = _owner.MinDate.Date;
            DateTime max = _owner.MaxDate.Date;

            if (min > max)
            {
                (min, max) = (max, min);
            }

            value = value.Date;

            return value < min ? min : value > max ? max : value;
        }
    }
}
