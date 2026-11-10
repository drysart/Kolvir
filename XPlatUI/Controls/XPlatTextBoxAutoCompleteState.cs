using System.Runtime.CompilerServices;
using SkiaSharp;

namespace XPlatUI.AutoComplete;

/// <summary>
/// The autocomplete state of one text box: its mode and source, the popup list, the bounded history,
/// and the text the user actually typed while the rest of a match has been appended after it.
/// </summary>
/// <remarks>
/// <para>
/// The state sits beside the control in a weak table, so a text box that never turns the feature on
/// has no entry and carries no fields for it. It wires itself to the control's own handlers when it
/// is created, and the popup and its list are built the first time a suggestion is needed.
/// </para>
/// <para>
/// In <see cref="XPlatAutoCompleteMode.SuggestAppend"/> the box holds two strings: the text the user
/// typed, and the suffix that was appended to it. The filter always runs against the former, because
/// matching the appended text would narrow the list to the one item that was just completed and the
/// user could never type past it.
/// </para>
/// </remarks>
internal sealed class XPlatTextBoxAutoCompleteState
{
    private const int MaxHistory = 64;
    private const float MaxListHeight = 240f;

    private static readonly ConditionalWeakTable<XPlatTextBox, XPlatTextBoxAutoCompleteState> States = new();

    private readonly XPlatTextBox _textBox;
    private readonly List<string> _history = [];
    private readonly List<string> _customSource = [];

    private XPlatAutoCompleteMode _mode;
    private XPlatAutoCompleteSource _source;
    private XPlatListBox? _list;
    private XPlatPopup? _popup;
    private bool _open;

    /// <summary>The text as the user typed it, without a completion appended after it.</summary>
    private string _typed = string.Empty;

    /// <summary>The suffix the box appended, or null when no completion is showing.</summary>
    private string? _appended;

    /// <summary>Set while this feature itself assigns <see cref="XPlatControl.Text"/>.</summary>
    private bool _previewing;

    /// <summary>Set while the list is being rebuilt, so selecting its first row does not complete the text again.</summary>
    private bool _suppressCompletion;

    /// <summary>
    /// Set when a key or a text input is about to change the text, so the change is known to come
    /// from the user rather than from an assignment. A key up clears it again if the edit turned
    /// out to change nothing.
    /// </summary>
    private bool _userEdit;

    private XPlatTextBoxAutoCompleteState(XPlatTextBox textBox)
    {
        _textBox = textBox;
        _typed = textBox.Text;

        textBox.AddKeyDownHandler(OnKeyDown);
        textBox.AddKeyUpHandler(OnKeyUp);
        textBox.AddTextInputHandler(OnTextInput);
        textBox.AddTextChangedHandler(OnTextChanged);
        textBox.AddMouseDownHandler(OnMouseDown);
        textBox.AddLostFocusHandler(OnLostFocus);
        textBox.AddEnabledChangedHandler(OnEnabledChanged);
        textBox.AddVisibleChangedHandler(OnVisibleChanged);
    }

    /// <summary>Returns the state for <paramref name="textBox"/>, creating and wiring it on first use.</summary>
    public static XPlatTextBoxAutoCompleteState Get(XPlatTextBox textBox) =>
        States.GetValue(textBox, static box => new XPlatTextBoxAutoCompleteState(box));

    /// <summary>Returns the state for <paramref name="textBox"/>, or null when it never opted in.</summary>
    public static XPlatTextBoxAutoCompleteState? Find(XPlatTextBox textBox) =>
        States.TryGetValue(textBox, out var state) ? state : null;

    /// <summary>What the box does with its suggestions as the user types.</summary>
    public XPlatAutoCompleteMode Mode
    {
        get => _mode;
        set
        {
            if (_mode == value)
            {
                return;
            }

            _mode = value;
            Close();
            _typed = CurrentPrefix();
            _appended = null;
        }
    }

    /// <summary>Where the suggestions come from.</summary>
    public XPlatAutoCompleteSource Source
    {
        get => _source;
        set
        {
            if (_source == value)
            {
                return;
            }

            _source = value;

            // The open list holds values from the old source, so it goes away; the next keystroke
            // builds one from the new source.
            Close();
        }
    }

    /// <summary>The values suggested when <see cref="Source"/> is <see cref="XPlatAutoCompleteSource.CustomSource"/>.</summary>
    public IList<string> CustomSource => _customSource;

    private bool HasSuggest =>
        _mode is XPlatAutoCompleteMode.Suggest or XPlatAutoCompleteMode.SuggestAppend;

    // Inline completion appends the rest of a single-line value to what was typed. A suffix cannot
    // span a line break, and completing a whole multi-line document from a single-line suggestion
    // would throw away every other line, so append is off in a multi-line box: Append and
    // SuggestAppend both behave as Suggest there.
    private bool HasAppend =>
        !_textBox.IsMultiline &&
        _mode is XPlatAutoCompleteMode.Append or XPlatAutoCompleteMode.SuggestAppend;

    private void OnKeyDown(object? sender, XPlatKeyEventArgs e)
    {
        if (e.Handled || _mode == XPlatAutoCompleteMode.None)
        {
            return;
        }

        bool alt = (e.Modifiers & XPlatModifiers.Alt) != 0;
        bool control = (e.Modifiers & XPlatModifiers.Control) != 0;

        switch (e.Key)
        {
            case XPlatKey.Down when alt && !_open:
                OpenFromKeyboard();
                e.Handled = _open;
                break;

            case XPlatKey.Down:
                if (_open)
                {
                    MoveSelection(1);
                    e.Handled = true;
                }
                else if (!_textBox.ReadOnly)
                {
                    OpenFromKeyboard();
                    e.Handled = _open;
                }

                break;

            case XPlatKey.Up:
                if (_open)
                {
                    MoveSelection(-1);
                    e.Handled = true;
                }

                break;

            case XPlatKey.Enter:
                if (_open)
                {
                    AcceptSelected();
                    e.Handled = true;
                }
                else
                {
                    // Enter is the text box's own accept gesture, so it is recorded but left
                    // unhandled for the form the field sits in.
                    CommitCompletion();
                    RecordHistory();
                }

                break;

            case XPlatKey.Escape:
                if (_open)
                {
                    // Escape takes the list away, not the text.
                    Close();
                    e.Handled = true;
                }

                break;

            case XPlatKey.Tab:
                // Tab belongs to the host, which moves the focus; the list closes on the way.
                Close();
                break;

            case XPlatKey.Left:
            case XPlatKey.Right:
            case XPlatKey.Home:
            case XPlatKey.End:
                // Moving the caret takes the completion as ordinary text. The text box moves the
                // caret after this handler returns, so the state is settled first.
                CommitCompletion();
                break;

            case XPlatKey.Backspace:
            case XPlatKey.Delete:
                _userEdit = true;
                break;

            case XPlatKey.V when control:
            case XPlatKey.X when control:
                _userEdit = true;
                break;
        }
    }

    private void OnKeyUp(object? sender, XPlatKeyEventArgs e) => _userEdit = false;

    private void OnTextInput(object? sender, XPlatTextInputEventArgs e)
    {
        if (!e.Handled && _mode != XPlatAutoCompleteMode.None)
        {
            // The text box inserts the text after this handler returns, and the change it makes is
            // therefore the user's.
            _userEdit = true;
        }
    }

    private void OnTextChanged(object? sender)
    {
        if (_previewing || _mode == XPlatAutoCompleteMode.None)
        {
            return;
        }

        if (!_userEdit)
        {
            // An assignment is not typing: adopt the text as it stands, drop any completion and get
            // out of the way.
            _typed = CurrentPrefix();
            _appended = null;
            Close();
            return;
        }

        _userEdit = false;
        UpdateForEdit();
    }

    private void OnMouseDown(object? sender, XPlatMouseEventArgs e) => CommitCompletion();

    private void OnLostFocus(object? sender)
    {
        _userEdit = false;
        Close();
        RecordHistory();
    }

    private void OnEnabledChanged(object? sender)
    {
        if (!_textBox.Enabled)
        {
            Close();
        }
    }

    private void OnVisibleChanged(object? sender)
    {
        if (!_textBox.Visible)
        {
            Close();
        }
    }

    /// <summary>
    /// The text a suggestion is matched against for the editing position. A single-line field
    /// matches its whole value. A multi-line one matches the logical line the caret is on, up to the
    /// caret, because suggestions are single-line values: taking one must not rewrite - or match
    /// against - any other line of the document.
    /// </summary>
    private string CurrentPrefix()
    {
        if (!_textBox.IsMultiline)
        {
            return _textBox.Text;
        }

        var text = _textBox.Text;
        int caret = CaretIndex();
        return text[LineStart()..caret];
    }

    /// <summary>Index of the caret, clamped to the text, for a prefix that is always a real slice.</summary>
    private int CaretIndex() => Math.Clamp(_textBox.CaretIndex, 0, _textBox.Text.Length);

    /// <summary>Index of the first character of the logical line the caret is on.</summary>
    private int LineStart()
    {
        var text = _textBox.Text;
        int index = CaretIndex();
        while (index > 0 && text[index - 1] != '\n')
        {
            index--;
        }

        return index;
    }

    /// <summary>Re-filters after an edit the user made.</summary>
    private void UpdateForEdit()
    {
        if (_textBox.IsMultiline)
        {
            // Only the line being edited is offered: a deletion never re-completes and an insertion
            // into an empty line closes the list, exactly as an empty single-line field does.
            _typed = CurrentPrefix();
            _appended = null;
            Refresh(_typed, allowComplete: false);
            return;
        }

        string text = _textBox.Text;

        if (_appended is not null && string.Equals(text, _typed, StringComparison.Ordinal))
        {
            // Backspace or Delete took the appended suffix itself. Keep what the user typed and do
            // not put the same completion straight back, or the key would appear to do nothing.
            _appended = null;
            Refresh(_typed, allowComplete: false);
            return;
        }

        // Only an insertion completes: a deletion that re-completed the text would make Backspace
        // and Delete appear to do nothing once the match had been accepted.
        bool inserted = text.Length >= _typed.Length;

        _typed = text;
        _appended = null;
        Refresh(text, allowComplete: inserted);
    }

    private void Refresh(string typed, bool allowComplete)
    {
        if (typed.Length == 0)
        {
            Close();
            return;
        }

        var matches = Filter(typed);

        if (allowComplete && HasAppend && matches.Count > 0)
        {
            CompleteTo(typed, matches[0]);
        }

        if (!HasSuggest || matches.Count == 0)
        {
            Close();
            return;
        }

        // The first match was already completed above, so selecting its row must not complete it
        // a second time.
        Show(matches, previewFirst: false);
    }

    private List<string> Filter(string typed)
    {
        var source = _source == XPlatAutoCompleteSource.HistoryList ? _history : _customSource;
        var matches = new List<string>();

        foreach (string candidate in source)
        {
            if (string.IsNullOrEmpty(candidate))
            {
                continue;
            }

            if (typed.Length == 0 ||
                candidate.StartsWith(typed, StringComparison.CurrentCultureIgnoreCase))
            {
                matches.Add(candidate);
            }
        }

        return matches;
    }

    /// <summary>Opens the list on what the user typed, which is what Down and Alt+Down do.</summary>
    private void OpenFromKeyboard()
    {
        if (!_textBox.IsFocused || _textBox.Host is null || _textBox.ReadOnly)
        {
            return;
        }

        string typed = _textBox.IsMultiline
            ? CurrentPrefix()
            : _appended is not null ? _typed : _textBox.Text;
        _typed = typed;
        _appended = null;

        var matches = Filter(typed);
        if (matches.Count == 0)
        {
            Close();
            return;
        }

        Show(matches, previewFirst: true);
    }

    private void Show(List<string> matches, bool previewFirst)
    {
        if (!_textBox.IsFocused || !_textBox.Enabled || !_textBox.Visible || _textBox.Host is not { } host)
        {
            Close();
            return;
        }

        EnsurePopup();

        bool wasOpen = _open;

        _suppressCompletion = !previewFirst;
        _list!.BeginUpdate();
        try
        {
            _list.Items.Clear();
            _list.Items.AddRange(matches);
            _list.SelectedIndex = 0;
            _list.EnsureVisible(0);
        }
        finally
        {
            _list.EndUpdate();
            _suppressCompletion = false;
        }

        _popup!.ScaleFactor = _textBox.EffectiveScaleMultiplier;
        _popup.Size = MeasureList(host);
        _open = true;

        if (wasOpen)
        {
            // The list can grow or shrink as the filter changes; the popup re-measures in place so
            // it does not slide around under the pointer. A multi-line box also re-anchors it,
            // because the caret it hangs from can now be on a different line.
            _popup.MoveTo(_textBox.IsMultiline ? Placement(host) : _popup.Location);
        }
        else
        {
            _popup.Show(Placement(host));
        }
    }

    /// <summary>Closes the list, if it is open.</summary>
    private void Close()
    {
        if (!_open)
        {
            return;
        }

        _open = false;
        _popup?.Close();
    }

    /// <summary>Appends the rest of <paramref name="item"/> after <paramref name="typed"/> and selects it.</summary>
    private void CompleteTo(string typed, string item)
    {
        if (_textBox.ReadOnly ||
            !item.StartsWith(typed, StringComparison.CurrentCultureIgnoreCase) ||
            (_textBox.MaxLength > 0 && item.Length > _textBox.MaxLength))
        {
            return;
        }

        _previewing = true;
        try
        {
            _textBox.Text = item;
            _textBox.Select(typed.Length, item.Length - typed.Length);
        }
        finally
        {
            _previewing = false;
        }

        _typed = typed;
        _appended = item.Length > typed.Length ? item[typed.Length..] : null;
    }

    /// <summary>Takes the appended suffix as ordinary text, so a later edit no longer replaces it.</summary>
    private void CommitCompletion()
    {
        if (_appended is null)
        {
            return;
        }

        _appended = null;
        _typed = _textBox.Text;
    }

    private void MoveSelection(int delta)
    {
        if (_list is null || _list.ItemCount == 0)
        {
            return;
        }

        int current = _list.SelectedIndex;
        int next = current < 0 ? (delta > 0 ? 0 : _list.ItemCount - 1) : current + delta;
        next = Math.Clamp(next, 0, _list.ItemCount - 1);

        _list.SelectedIndex = next;
        _list.EnsureVisible(next);
    }

    /// <summary>Takes the highlighted suggestion, closes the list and records the value.</summary>
    private void AcceptSelected()
    {
        if (_list is null || _list.SelectedIndex < 0)
        {
            Close();
            return;
        }

        string item = _list.GetItemText(_list.SelectedIndex);

        if (!_textBox.ReadOnly)
        {
            _previewing = true;
            try
            {
                if (_textBox.IsMultiline)
                {
                    // Replace what was typed on the caret's line and leave every other line alone.
                    int start = LineStart();
                    _textBox.Select(start, CaretIndex() - start);
                    _textBox.InsertText(item);
                    _textBox.Select(start + item.Length, 0);
                }
                else
                {
                    _textBox.Text = item;
                    _textBox.Select(item.Length, 0);
                }
            }
            finally
            {
                _previewing = false;
            }

            _typed = item;
            _appended = null;
        }

        Close();
        RecordHistory();
    }

    /// <summary>
    /// Remembers a value the user committed. Only the history source uses the list, so it is the
    /// only source that can grow; it is deduplicated and capped so it cannot grow without bound.
    /// </summary>
    private void RecordHistory()
    {
        if (_mode == XPlatAutoCompleteMode.None || _source != XPlatAutoCompleteSource.HistoryList)
        {
            return;
        }

        // History holds single-line values. A multi-line document is not one, and recording it
        // would make the next suggestion replace the whole document with one of its lines.
        if (_textBox.IsMultiline)
        {
            return;
        }

        string value = _textBox.Text.Trim();
        if (value.Length == 0)
        {
            return;
        }

        _history.RemoveAll(v => string.Equals(v, value, StringComparison.CurrentCultureIgnoreCase));
        _history.Insert(0, value);

        if (_history.Count > MaxHistory)
        {
            _history.RemoveRange(MaxHistory, _history.Count - MaxHistory);
        }
    }

    private void OnSuggestionSelected(object? sender)
    {
        if (_suppressCompletion || !HasAppend || _list is null || _list.SelectedIndex < 0)
        {
            return;
        }

        // Arrowing through the list previews the highlighted item in the box, still filtered on
        // what the user typed.
        CompleteTo(_typed, _list.GetItemText(_list.SelectedIndex));
    }

    private void OnSuggestionMouseUp(object? sender, XPlatMouseEventArgs e)
    {
        if (e.Button == XPlatMouseButtons.Left)
        {
            AcceptSelected();
        }
    }

    private void OnPopupClosed(object? sender)
    {
        _open = false;

        var focused = _textBox.Host?.FocusedControl;
        if (_textBox.Enabled && _textBox.Visible && (focused is null || ReferenceEquals(focused, _textBox)))
        {
            _textBox.Focus();
        }
    }

    private void EnsurePopup()
    {
        if (_popup is not null)
        {
            // The box's font can change while it is in use, and the rows follow it.
            _list!.Font = _textBox.Font;
            return;
        }

        _list = new XPlatListBox
        {
            SelectionMode = XPlatSelectionMode.One,
            HorizontalScroll = XPlatScrollStyle.None,
            Font = _textBox.Font,

            // The list takes the pointer but never the keyboard: the text box keeps the caret and
            // forwards the keys, which is what lets the user keep typing with the list open.
            Focusable = false,
            TabStop = false,
        };

        _list.AddSelectedIndexChangedHandler(OnSuggestionSelected);
        _list.AddMouseUpHandler(OnSuggestionMouseUp);

        _popup = _textBox.Own(new XPlatPopup(_textBox) { Content = _list });
        _popup.AddClosedHandler(OnPopupClosed);
    }

    private SKSize MeasureList(XPlatUiHost host)
    {
        var area = PlacementArea(host);
        var scale = _textBox.EffectiveScaleMultiplier;

        // The list is measured in the content space it will be drawn in, so the room is the
        // host-space placement area divided by the zoom.
        var room = new SKSize(area.Width / scale, area.Height / scale);
        var measured = _list!.Measure(new SKSize(room.Width, float.PositiveInfinity));

        // The list draws its own border and padding inside its bounds, so both are part of the
        // height the popup has to make room for, as they are for a combo box.
        float chrome = (_list.BorderWidth * 2f) + _list.Padding.Vertical;
        float rows = MathF.Max(1, _list.ItemCount) * _list.ItemHeight;
        float height = MathF.Min(rows + chrome, MathF.Min(MaxListHeight, room.Height));

        // As wide as its own rows, unlike a combo box's list: it hangs from the caret rather than
        // filling the field, so a short suggestion does not put a wide blank panel on screen.
        float width = MathF.Min(measured.Width, room.Width);

        return new SKSize(MathF.Max(1f, width), MathF.Max(1f, height));
    }

    private SKPoint Placement(XPlatUiHost host)
    {
        var area = PlacementArea(host);
        var scale = _textBox.EffectiveScaleMultiplier;
        var origin = _textBox.GetPositionInHost();
        var caret = _textBox.CaretBounds;
        _popup!.ScaleFactor = scale;
        var size = _popup.Size ?? SKSize.Empty;
        var onScreen = new SKSize(size.Width * scale, size.Height * scale);

        // Under the caret: the list starts where the insertion point is, so it reads as a
        // continuation of what the user is typing rather than as a drop-down of the whole field.
        // The caret and the field's height are the box's own metrics, so they are zoomed too.
        float x = origin.X + (caret.Left * scale);
        x = Math.Clamp(x, area.Left, MathF.Max(area.Left, area.Right - onScreen.Width));

        // Like a drop-down list, it opens downwards and flips above the field when there is no room
        // below it. In a field that spans lines it hangs from the caret's own line instead, which is
        // the line being edited and may be many lines away from the top or bottom of a tall box. The
        // platform layer still clamps a window popup to the display.
        float top = origin.Y + (_textBox.IsMultiline ? caret.Top * scale : 0f);
        float bottom = origin.Y + (_textBox.IsMultiline ? caret.Bottom * scale : _textBox.Height * scale);
        float below = bottom;
        float above = top - onScreen.Height;
        float y = below + onScreen.Height <= area.Bottom || above < area.Top ? below : above;
        y = Math.Clamp(y, area.Top, MathF.Max(area.Top, area.Bottom - onScreen.Height));

        return new SKPoint(x, y);
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
}
