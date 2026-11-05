using SkiaSharp;

namespace XPlatUI;

/// <summary>
/// A drop-down list: shows the selected item in a field and opens a list of the items below it when
/// the user asks for it. The open list is a popup, so it can hang past the edges of the window when
/// the platform layer provides popup windows, and is painted inside the window when it does not.
/// </summary>
/// <remarks>
/// The list itself is an <see cref="XPlatListBox"/>, reachable through <see cref="DropDownList"/>
/// for the things the combo box does not forward - item drawing, item height, scrollbars. The combo
/// box keeps the keyboard, as a native one does: while the list is open the arrow keys, Home, End,
/// Page Up and Page Down move the selection without the list taking focus.
/// </remarks>
public class XPlatComboBox : XPlatControl
{
    private const long TypeAheadTimeout = 800;

    private readonly XPlatListBox _list;
    private readonly XPlatPopup _popup;
    private Action<object>? _selectedIndexChanged;
    private bool _dropDownOpen;
    private string _typeAhead = string.Empty;
    private long _typeAheadAt;

    public XPlatComboBox()
    {
        TabStop = true;
        Cursor = XPlatCursor.Hand;
        BorderWidth = 1f;
        CornerRadius = 6f;
        Padding = new XPlatThickness(8f, 6f, 26f, 6f);
        MinimumSize = new SKSize(64f, 30f);
        Size = new SKSize(180f, 30f);

        // The list is not a child control - it lives in a popup, in a tree of its own - so it cannot
        // inherit this combo box's font through the parent chain. It is handed the effective font
        // when the list is shown instead, which also keeps it current without a snapshot.
        _list = new XPlatListBox
        {
            SelectionMode = XPlatSelectionMode.One,
            HorizontalScroll = XPlatScrollStyle.None,
        };

        _list.AddSelectedIndexChangedHandler(OnListSelectedIndexChanged);
        _list.AddMouseUpHandler(OnListMouseUp);
        _list.AddItemActivatedHandler(OnListItemActivated);

        _popup = new XPlatPopup(this) { Content = _list };
        _popup.AddClosedHandler(OnPopupClosed);
    }

    /// <summary>A combo box is a field, so it paints on the input fill until <see cref="XPlatControl.Background"/> is assigned.</summary>
    protected override XPlatBackground DefaultBackground => XPlatBackground.FromThemeColor(XPlatThemeColor.InputBackground);

    /// <summary>The items the user can pick from.</summary>
    public XPlatListBox.ItemCollection Items => _list.Items;

    /// <summary>Number of items, which is the same as <c>Items.Count</c>.</summary>
    public int ItemCount => _list.ItemCount;

    /// <summary>The list shown while the combo box is open.</summary>
    public XPlatListBox DropDownList => _list;

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
        return new XPlatHandlerToken<Action<object>>(RemoveSelectedIndexChangedHandler, handler);
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
        var origin = GetPositionInHost();
        var location = new SKPoint(origin.X, origin.Y + Height);

        // Flipping above the field is what a combo box near the bottom of the window does; the
        // platform layer places the popup inside the screen as well, but it has no idea which
        // control the popup belongs to.
        if (location.Y + size.Height > host.ClientSize.Height && origin.Y - size.Height >= 0f)
        {
            location = new SKPoint(origin.X, origin.Y - size.Height);
        }

        _dropDownOpen = true;
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

    protected override SKSize MeasureContent(SKSize availableSize)
    {
        float widest = 0f;

        for (int i = 0; i < _list.ItemCount; i++)
        {
            widest = MathF.Max(widest, XPlatTextRenderer.Measure(_list.GetItemText(i), Font, float.PositiveInfinity, false).Width);
        }

        return new SKSize(
            MathF.Max(MinimumSize.Width, widest + Padding.Horizontal),
            MathF.Max(MinimumSize.Height, Font.LineHeight + Padding.Vertical));
    }

    protected override void OnPaintBackground(SKCanvas canvas, SKRect rect)
    {
        var fill = Enabled ? BackColor ?? XPlatTheme.InputBackground : XPlatTheme.DisabledBackground;
        XPlatPainter.Fill(canvas, rect, fill, CornerRadius);

        var border = !Enabled
            ? XPlatTheme.DisabledBackground
            : _dropDownOpen || IsFocused
                ? XPlatTheme.Accent
                : IsMouseOver ? XPlatTheme.SplitterHover : BorderColor;

        XPlatPainter.DrawBorder(canvas, rect, border, BorderWidth > 0f ? BorderWidth : 1f, CornerRadius);
    }

    protected override void OnPaint(SKCanvas canvas, SKRect rect)
    {
        var content = XPlatGeometry.Deflate(rect, Padding);
        if (content.Width <= 0f || content.Height <= 0f)
        {
            return;
        }

        var text = SelectedText;
        var color = !Enabled
            ? XPlatTheme.DisabledForeground
            : text.Length > 0 ? ForeColor : XPlatTheme.Placeholder;

        canvas.Save();
        canvas.ClipRect(content);
        XPlatTextRenderer.Draw(canvas, text.Length > 0 ? text : EmptyText, Font, color, content);
        canvas.Restore();

        PaintChevron(canvas, rect);
    }

    protected override void OnPaintBorder(SKCanvas canvas, SKRect rect)
    {
        // The border is drawn in OnPaintBackground so it stays behind the text and the chevron.
    }

    protected override void OnMouseDown(XPlatMouseEventArgs e)
    {
        base.OnMouseDown(e);

        if (e.Button != XPlatMouseButtons.Left || !Enabled)
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

    protected override void OnMouseEnter()
    {
        base.OnMouseEnter();
        Invalidate();
    }

    protected override void OnMouseLeave()
    {
        base.OnMouseLeave();
        Invalidate();
    }

    protected override void OnKeyDown(XPlatKeyEventArgs e)
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

    protected override void OnTextInput(XPlatTextInputEventArgs e)
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

    protected override void OnLostFocus()
    {
        base.OnLostFocus();
        CloseDropDown();
    }

    protected override void OnEnabledChanged()
    {
        base.OnEnabledChanged();

        if (!Enabled)
        {
            CloseDropDown();
        }

        Invalidate();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _popup.RemoveClosedHandler(OnPopupClosed);
            _popup.Dispose();
        }

        base.Dispose(disposing);
    }

    private void HandleClosedKey(XPlatKeyEventArgs e)
    {
        switch (e.Key)
        {
            case XPlatKey.Down when (e.Modifiers & XPlatModifiers.Alt) != 0:
                OpenDropDown();
                e.Handled = true;
                break;

            case XPlatKey.Down:
                MoveSelection(1);
                e.Handled = true;
                break;

            case XPlatKey.Up:
                MoveSelection(-1);
                e.Handled = true;
                break;

            case XPlatKey.Home:
                MoveSelectionTo(0);
                e.Handled = true;
                break;

            case XPlatKey.End:
                MoveSelectionTo(_list.ItemCount - 1);
                e.Handled = true;
                break;

            case XPlatKey.Enter:
            case XPlatKey.Space:
                OpenDropDown();
                e.Handled = true;
                break;
        }
    }

    private void HandleOpenKey(XPlatKeyEventArgs e)
    {
        switch (e.Key)
        {
            case XPlatKey.Down:
                MoveSelection(1);
                e.Handled = true;
                break;

            case XPlatKey.Up:
                MoveSelection(-1);
                e.Handled = true;
                break;

            case XPlatKey.Home:
                MoveSelectionTo(0);
                e.Handled = true;
                break;

            case XPlatKey.End:
                MoveSelectionTo(_list.ItemCount - 1);
                e.Handled = true;
                break;

            case XPlatKey.PageDown:
                MoveSelection(PageStep());
                e.Handled = true;
                break;

            case XPlatKey.PageUp:
                MoveSelection(-PageStep());
                e.Handled = true;
                break;

            case XPlatKey.Escape when (e.Modifiers & XPlatModifiers.Alt) != 0:
            case XPlatKey.Escape:
            case XPlatKey.Enter:
            case XPlatKey.Tab:
                CloseDropDown();
                e.Handled = e.Key != XPlatKey.Tab;
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
            Color = Enabled ? XPlatTheme.MutedForeground : XPlatTheme.DisabledForeground,
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

    private void OnListMouseUp(object? sender, XPlatMouseEventArgs e)
    {
        if (e.Button == XPlatMouseButtons.Left)
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
