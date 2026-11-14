using SkiaSharp;

namespace Kolvir;

/// <summary>
/// A drop-down list: shows the selected item in a field and opens a list of the items below it when
/// the user asks for it. The open list is a popup, so it can hang past the edges of the window when
/// the platform layer provides popup windows, and is painted inside the window when it does not.
/// </summary>
/// <remarks>
/// The list itself is an <see cref="ListBox"/>, reachable through <see cref="DropDownList"/>
/// for the things the combo box does not forward - item drawing, item height, scrollbars. The combo
/// box keeps the keyboard, as a native one does: while the list is open the arrow keys, Home, End,
/// Page Up and Page Down move the selection without the list taking focus.
/// </remarks>
public class ComboBox : Control
{
    private const long TypeAheadTimeout = 800;

    private readonly ListBox _list;
    private readonly Popup _popup;
    private Action<object>? _selectedIndexChanged;
    private bool _dropDownOpen;
    private string _typeAhead = string.Empty;
    private long _typeAheadAt;

    /// <summary>
    /// Creates an empty combo box 180 by 30, together with the <see cref="ListBox"/> it shows and
    /// the popup that holds it.
    /// </summary>
    public ComboBox()
    {
        TabStop = true;
        Cursor = Cursor.Hand;
        BorderWidth = 1f;
        CornerRadius = 6f;
        Padding = new Thickness(8f, 6f, 26f, 6f);
        MinimumSize = new SKSize(64f, 30f);
        Size = new SKSize(180f, 30f);

        // The list is not a child control - it lives in a popup, in a tree of its own - so it cannot
        // inherit this combo box's font through the parent chain. It is handed the effective font
        // when the list is shown instead, which also keeps it current without a snapshot.
        _list = new ListBox
        {
            SelectionMode = SelectionMode.One,
            HorizontalScroll = ScrollStyle.None,
        };

        _list.AddSelectedIndexChangedHandler(OnListSelectedIndexChanged);
        _list.AddMouseUpHandler(OnListMouseUp);
        _list.AddItemActivatedHandler(OnListItemActivated);

        _popup = new Popup(this) { Content = _list };
        _popup.AddClosedHandler(OnPopupClosed);
    }

    /// <summary>A combo box is a field, so it paints on the input fill until <see cref="Control.Background"/> is assigned.</summary>
    protected override Background DefaultBackground => Background.FromThemeColor(ThemeColor.InputBackground);

    /// <summary>The items the user can pick from.</summary>
    public ListBox.ItemCollection Items => _list.Items;

    /// <summary>Number of items, which is the same as <c>Items.Count</c>.</summary>
    public int ItemCount => _list.ItemCount;

    /// <summary>The list shown while the combo box is open.</summary>
    public ListBox DropDownList => _list;

    /// <summary>Index of the selected item, or -1 when nothing is selected.</summary>
    public int SelectedIndex
    {
        get => _list.SelectedIndex;
        set
        {
            int index = value < -1 || value >= _list.ItemCount ? -1 : value;

            if (_list.SelectedIndex == index)
            {
                return;
            }

            _list.SelectedIndex = index;

            if (index >= 0)
            {
                _list.EnsureVisible(index);
            }
        }
    }

    /// <summary>The selected item, or null when nothing is selected.</summary>
    public object? SelectedItem
    {
        get => _list.SelectedItem;
        set
        {
            int index = value is null ? -1 : _list.Items.IndexOf(value);
            SelectedIndex = index;
        }
    }

    /// <summary>The text of the selected item, or an empty string when nothing is selected.</summary>
    public string SelectedText => _list.SelectedIndex >= 0 ? _list.GetItemText(_list.SelectedIndex) : string.Empty;

    /// <summary>
    /// The selected text. Assigning it selects the first item whose text matches, and clears the
    /// selection when none does.
    /// </summary>
    public override string Text
    {
        get => SelectedText;
        set => SelectItemByText(value);
    }

    /// <summary>Text painted in the field while nothing is selected.</summary>
    public string EmptyText { get; set; } = string.Empty;

    /// <summary>Tallest the open list may get, in logical pixels, before it scrolls. Defaults to 240.</summary>
    public float MaxDropDownHeight { get; set; } = 240f;

    /// <summary>Whether the list is open.</summary>
    public bool IsDropDownOpen => _dropDownOpen;

    /// <summary>Raised when <see cref="SelectedIndex"/> changed.</summary>
    public IDisposable AddSelectedIndexChangedHandler(Action<object> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        _selectedIndexChanged += handler;
        return new HandlerToken<Action<object>>(RemoveSelectedIndexChangedHandler, handler);
    }

    /// <summary>Removes a handler that <see cref="AddSelectedIndexChangedHandler"/> added.</summary>
    public void RemoveSelectedIndexChangedHandler(Action<object> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        _selectedIndexChanged -= handler;
    }

    /// <summary>Opens the list, if the combo box is enabled and the list is not already open.</summary>
    public void OpenDropDown()
    {
        if (_dropDownOpen || !Enabled || Host is not { } host)
        {
            return;
        }

        // The popup list has no parent to inherit from, and the font may have changed since the last
        // time it was shown. Assigning the same font again is a no-op.
        _list.Font = Font;

        var size = MeasureDropDown();
        var scale = EffectiveScaleMultiplier;
        var onScreen = new SKSize(size.Width * scale, size.Height * scale);
        var origin = GetPositionInHost();
        var location = new SKPoint(origin.X, origin.Y + (Height * scale));

        // Flipping above the field is what a combo box near the bottom of the window does; the
        // platform layer places the popup inside the screen as well, but it has no idea which
        // control the popup belongs to. The flip decision uses the size the popup will occupy on
        // screen, not the content size it was measured at.
        if (location.Y + onScreen.Height > host.ClientSize.Height && origin.Y - onScreen.Height >= 0f)
        {
            location = new SKPoint(origin.X, origin.Y - onScreen.Height);
        }

        _dropDownOpen = true;
        _popup.ScaleFactor = scale;
        _popup.Size = size;
        _popup.Show(location);

        if (SelectedIndex >= 0)
        {
            _list.EnsureVisible(SelectedIndex);
        }

        Invalidate();
    }

    /// <summary>Closes the list, if it is open.</summary>
    public void CloseDropDown()
    {
        if (!_dropDownOpen)
        {
            return;
        }

        _popup.Close();
    }

    /// <inheritdoc />
    /// <remarks>
    /// The field is as wide as its widest item plus the padding, and one line of the inherited font
    /// tall, but never smaller than <see cref="Control.MinimumSize"/>.
    /// </remarks>
    protected override SKSize MeasureContent(SKSize availableSize)
    {
        float widest = 0f;

        for (int i = 0; i < _list.ItemCount; i++)
        {
            widest = MathF.Max(widest, TextRenderer.Measure(_list.GetItemText(i), Font, float.PositiveInfinity, false).Width);
        }

        return new SKSize(
            MathF.Max(MinimumSize.Width, widest + Padding.Horizontal),
            MathF.Max(MinimumSize.Height, Font.LineHeight + Padding.Vertical));
    }

    /// <summary>
    /// Fills the field and draws its border, which takes the accent colour while the list is open
    /// or the combo box has the focus and the hover colour while the pointer is over it.
    /// </summary>
    protected override void OnPaintBackground(SKCanvas canvas, SKRect rect)
    {
        var fill = Enabled ? BackColor ?? Theme.InputBackground : Theme.DisabledBackground;
        Painter.Fill(canvas, rect, fill, CornerRadius);

        var border = !Enabled
            ? Theme.DisabledBackground
            : _dropDownOpen || IsFocused
                ? Theme.Accent
                : IsMouseOver ? Theme.SplitterHover : BorderColor;

        Painter.DrawBorder(canvas, rect, border, BorderWidth > 0f ? BorderWidth : 1f, CornerRadius);
    }

    /// <summary>
    /// Paints the selected text, or <see cref="EmptyText"/> in the placeholder colour when nothing
    /// is selected, clipped to the content area, and then the chevron in the right-hand margin.
    /// </summary>
    protected override void OnPaint(SKCanvas canvas, SKRect rect)
    {
        var content = Geometry.Deflate(rect, Padding);
        if (content.Width <= 0f || content.Height <= 0f)
        {
            return;
        }

        var text = SelectedText;
        var color = !Enabled
            ? Theme.DisabledForeground
            : text.Length > 0 ? ForeColor : Theme.Placeholder;

        canvas.Save();
        canvas.ClipRect(content);
        TextRenderer.Draw(canvas, text.Length > 0 ? text : EmptyText, Font, color, content);
        canvas.Restore();

        PaintChevron(canvas, rect);
    }

    /// <summary>
    /// Draws nothing: the border is painted in <see cref="OnPaintBackground"/> so that the text and
    /// the chevron sit over it rather than under it.
    /// </summary>
    protected override void OnPaintBorder(SKCanvas canvas, SKRect rect)
    {
        // The border is drawn in OnPaintBackground so it stays behind the text and the chevron.
    }

    /// <summary>
    /// Takes the focus and toggles the list: opens it when it is shut, closes it when it is open.
    /// </summary>
    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);

        if (e.Button != MouseButtons.Left || !Enabled)
        {
            return;
        }

        Focus();

        if (_dropDownOpen)
        {
            CloseDropDown();
        }
        else
        {
            OpenDropDown();
        }
    }

    /// <summary>Repaints, because the field's border follows whether the pointer is over it.</summary>
    protected override void OnMouseEnter()
    {
        base.OnMouseEnter();
        Invalidate();
    }

    /// <summary>Repaints, because the field's border follows whether the pointer is over it.</summary>
    protected override void OnMouseLeave()
    {
        base.OnMouseLeave();
        Invalidate();
    }

    /// <summary>
    /// Keeps the keyboard while the list is open: the arrows, Home, End, Page Up and Page Down move
    /// the selection, and Enter, Escape or Tab close the list. Shut, Alt+Down, Enter or Space open
    /// it and the arrows move the selection without opening it.
    /// </summary>
    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);

        if (!Enabled || e.Handled)
        {
            return;
        }

        if (_dropDownOpen)
        {
            HandleOpenKey(e);
        }
        else
        {
            HandleClosedKey(e);
        }
    }

    /// <summary>
    /// Type-ahead: the characters typed so far are remembered for a moment and select the next item
    /// whose text starts with them, starting from the current selection and wrapping round.
    /// </summary>
    protected override void OnTextInput(TextInputEventArgs e)
    {
        base.OnTextInput(e);

        if (!Enabled || string.IsNullOrEmpty(e.Text))
        {
            return;
        }

        // Typing letters picks the next item that starts with what was typed, the way a combo box
        // does without an editable field. The prefix is forgotten after a short pause so a later
        // keystroke starts a new search.
        long now = Environment.TickCount64;
        _typeAhead = now - _typeAheadAt > TypeAheadTimeout ? e.Text : _typeAhead + e.Text;
        _typeAheadAt = now;

        int match = FindItemStartingWith(_typeAhead, SelectedIndex);
        if (match >= 0)
        {
            SelectedIndex = match;
            _list.EnsureVisible(match);
        }

        e.Handled = true;
    }

    /// <summary>Closes the list when the focus moves on, as a native combo box does.</summary>
    protected override void OnLostFocus()
    {
        base.OnLostFocus();
        CloseDropDown();
    }

    /// <summary>Closes the list when the combo box is disabled, and repaints either way.</summary>
    protected override void OnEnabledChanged()
    {
        base.OnEnabledChanged();

        if (!Enabled)
        {
            CloseDropDown();
        }

        Invalidate();
    }

    /// <summary>
    /// Drops the popup's close handler - so tearing the popup down cannot call back into this
    /// control - and disposes the popup the combo box owns.
    /// </summary>
    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _popup.RemoveClosedHandler(OnPopupClosed);
            _popup.Dispose();
        }

        base.Dispose(disposing);
    }

    private void HandleClosedKey(KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Down when (e.Modifiers & Modifiers.Alt) != 0:
                OpenDropDown();
                e.Handled = true;
                break;

            case Key.Down:
                MoveSelection(1);
                e.Handled = true;
                break;

            case Key.Up:
                MoveSelection(-1);
                e.Handled = true;
                break;

            case Key.Home:
                MoveSelectionTo(0);
                e.Handled = true;
                break;

            case Key.End:
                MoveSelectionTo(_list.ItemCount - 1);
                e.Handled = true;
                break;

            case Key.Enter:
            case Key.Space:
                OpenDropDown();
                e.Handled = true;
                break;
        }
    }

    private void HandleOpenKey(KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Down:
                MoveSelection(1);
                e.Handled = true;
                break;

            case Key.Up:
                MoveSelection(-1);
                e.Handled = true;
                break;

            case Key.Home:
                MoveSelectionTo(0);
                e.Handled = true;
                break;

            case Key.End:
                MoveSelectionTo(_list.ItemCount - 1);
                e.Handled = true;
                break;

            case Key.PageDown:
                MoveSelection(PageStep());
                e.Handled = true;
                break;

            case Key.PageUp:
                MoveSelection(-PageStep());
                e.Handled = true;
                break;

            case Key.Escape when (e.Modifiers & Modifiers.Alt) != 0:
            case Key.Escape:
            case Key.Enter:
            case Key.Tab:
                CloseDropDown();
                e.Handled = e.Key != Key.Tab;
                break;
        }
    }

    private int PageStep()
    {
        float viewport = _list.ViewportRect.Height;
        int visible = Math.Max(1, (int)(viewport / MathF.Max(1f, _list.ItemHeight)));
        return visible;
    }

    private void MoveSelection(int delta)
    {
        if (_list.ItemCount == 0)
        {
            return;
        }

        int current = SelectedIndex;

        if (current < 0)
        {
            MoveSelectionTo(delta < 0 ? _list.ItemCount - 1 : 0);
            return;
        }

        MoveSelectionTo(current + delta);
    }

    private void MoveSelectionTo(int index)
    {
        if (_list.ItemCount == 0)
        {
            return;
        }

        index = Math.Clamp(index, 0, _list.ItemCount - 1);
        SelectedIndex = index;
        _list.EnsureVisible(index);
    }

    private int FindItemStartingWith(string prefix, int from)
    {
        int count = _list.ItemCount;
        if (count == 0)
        {
            return -1;
        }

        for (int step = 0; step < count; step++)
        {
            int index = ((from + step) % count + count) % count;
            string text = _list.GetItemText(index);

            if (text.StartsWith(prefix, StringComparison.CurrentCultureIgnoreCase))
            {
                return index;
            }
        }

        return -1;
    }

    private void SelectItemByText(string? value)
    {
        value ??= string.Empty;

        for (int i = 0; i < _list.ItemCount; i++)
        {
            if (string.Equals(_list.GetItemText(i), value, StringComparison.CurrentCulture))
            {
                SelectedIndex = i;
                return;
            }
        }

        SelectedIndex = -1;
    }

    private SKSize MeasureDropDown()
    {
        // The list draws its own border inside its bounds, so the border and the padding are part
        // of the height the popup has to make room for.
        float chrome = (_list.BorderWidth * 2f) + _list.Padding.Vertical;
        float rows = MathF.Max(1, _list.ItemCount) * _list.ItemHeight;
        float height = MathF.Min(rows + chrome, MathF.Max(MaxDropDownHeight, chrome));

        return new SKSize(Width, height);
    }

    private void PaintChevron(SKCanvas canvas, SKRect rect)
    {
        float centerX = rect.Right - Padding.Right * 0.5f - 4f;
        float centerY = rect.MidY;
        float half = 4f;

        using var builder = new SKPathBuilder();
        builder.MoveTo(centerX - half, centerY - half * 0.5f);
        builder.LineTo(centerX + half, centerY - half * 0.5f);
        builder.LineTo(centerX, centerY + half * 0.6f);
        builder.Close();

        using var path = builder.Snapshot();
        using var paint = new SKPaint
        {
            Color = Enabled ? Theme.MutedForeground : Theme.DisabledForeground,
            IsAntialias = true,
            Style = SKPaintStyle.Fill,
        };

        canvas.DrawPath(path, paint);
    }

    private void OnListSelectedIndexChanged(object? sender)
    {
        // The list is the model: its selection change is the combo box's selection change, whether
        // it came from the drop-down, from typing, or from the SelectedIndex property. The handler
        // is only reached for a real change, because the list raises its own event that way.
        Invalidate();
        _selectedIndexChanged?.Invoke(this);
    }

    private void OnListMouseUp(object? sender, MouseEventArgs e)
    {
        if (e.Button == MouseButtons.Left)
        {
            CloseDropDown();
        }
    }

    private void OnListItemActivated(object? sender) => CloseDropDown();

    private void OnPopupClosed(object? sender)
    {
        _dropDownOpen = false;
        Invalidate();

        if (Enabled && Visible)
        {
            Focus();
        }
    }
}
