using System.Collections;
using System.Collections.ObjectModel;
using SkiaSharp;

namespace XPlatUI;

/// <summary>
/// What an owner-draw handler is given for the item it is asked to paint.
/// </summary>
public class XPlatListBoxDrawItemEventArgs
{
    public XPlatListBoxDrawItemEventArgs(
        SKCanvas canvas,
        int index,
        object? item,
        SKRect bounds,
        XPlatDrawItemState state,
        XPlatFont font,
        SKColor textColor)
    {
        Canvas = canvas;
        Index = index;
        Item = item;
        Bounds = bounds;
        State = state;
        Font = font;
        TextColor = textColor;
    }

    /// <summary>
    /// The canvas to draw on. It is already clipped to the viewport and translated to the list's
    /// own coordinates, so <see cref="Bounds"/> can be used as is.
    /// </summary>
    public SKCanvas Canvas { get; }

    /// <summary>The item's position in <see cref="XPlatListBox.Items"/>.</summary>
    public int Index { get; }

    /// <summary>The item itself.</summary>
    public object? Item { get; }

    /// <summary>The whole row, in the list's coordinates. It may be wider than the viewport.</summary>
    public SKRect Bounds { get; }

    /// <summary>What the row is: selected, focused, hot or disabled.</summary>
    public XPlatDrawItemState State { get; }

    /// <summary>The font the list would have used for the item's text.</summary>
    public XPlatFont Font { get; }

    /// <summary>
    /// The colour the default drawing would have used for the text, which is the right one for a
    /// selected, disabled or plain row. A handler is free to ignore it.
    /// </summary>
    public SKColor TextColor { get; }
}

/// <summary>
/// A vertical list of items, one row each, that the user can select from. It is the closest
/// thing this library has to <c>System.Windows.Forms.ListBox</c>.
/// </summary>
/// <remarks>
/// <para>
/// Items are any object; the row shows <see cref="object.ToString"/>. An
/// <see cref="AddDrawItemHandler"/> replaces that with whatever the application wants to paint.
/// </para>
/// <para>
/// The four WinForms selection modes are supported through <see cref="SelectionMode"/>. Selection
/// here is the list's own notion of "which items are picked" and has nothing to do with the host
/// text selection that <see cref="XPlatControl.Selectable"/> opts into.
/// </para>
/// <para>
/// Scrolling is the same machinery an <see cref="XPlatPanel"/> uses: a visible scrollbar takes a
/// gutter out of the client area and the rest of the control clips the rows. Only the rows that
/// are actually on screen are painted, measured or invalidated, so a list can hold far more items
/// than fit.
/// </para>
/// </remarks>
public class XPlatListBox : XPlatControl
{
    private const float TextInset = 8f;
    private const float ScrollEpsilon = 0.5f;
    private const int DefaultVisibleRows = 8;
    private const long TypeAheadTimeoutMs = 700;

    private readonly XPlatScrollBars _scroll;
    private readonly List<object?> _items = [];
    private readonly List<int> _selected = [];
    private readonly ReadOnlyCollection<int> _selectedView;
    private readonly ItemCollection _itemCollection;

    private XPlatFont _font = XPlatTheme.DefaultFont;
    private XPlatSelectionMode _selectionMode = XPlatSelectionMode.One;
    private string _emptyText = string.Empty;

    private float _itemHeight;
    private float _contentWidth;
    private bool _measureDirty = true;

    private int _anchor = -1;
    private int _lead = -1;
    private int _hotIndex = -1;
    private bool _dragging;
    private bool _layingOut;
    private bool _pendingSelectionChanged;
    private int _updateCount;

    private string _typeAhead = string.Empty;
    private long _typeAheadStamp;

    private Action<object>? _selectedIndexChanged;
    private Action<object>? _itemActivated;
    private Action<object, XPlatListBoxDrawItemEventArgs>? _drawItem;

    public XPlatListBox()
    {
        // The scroll helper has to exist before its own properties are assigned: setting one asks
        // the owner to lay out, and the owner lays out through the helper.
        _scroll = new XPlatScrollBars(this);
        _selectedView = _selected.AsReadOnly();
        _itemCollection = new ItemCollection(this, _items);

        Focusable = true;
        TabStop = true;
        BorderWidth = 1f;
        CornerRadius = 4f;
        Padding = new XPlatThickness(2f);
        MinimumSize = new SKSize(0f, 32f);
        _scroll.Vertical = XPlatScrollStyle.Automatic;
    }

    /// <summary>A list paints on the input fill, like the other fields, until <see cref="XPlatControl.BackColor"/> is assigned.</summary>
    protected override SKColor? DefaultBackColor => XPlatTheme.InputBackground;

    /// <summary>The items in the list, in the order they are painted.</summary>
    public ItemCollection Items => _itemCollection;

    /// <summary>Number of items, which is the same as <c>Items.Count</c>.</summary>
    public int ItemCount => _items.Count;

    /// <summary>Font used to draw the item text. The list does not take ownership of it.</summary>
    public XPlatFont Font
    {
        get => _font;
        set
        {
            ArgumentNullException.ThrowIfNull(value);

            if (ReferenceEquals(_font, value))
            {
                return;
            }

            _font = value;
            _measureDirty = true;
            PerformLayout();
            Invalidate();
        }
    }

    /// <summary>How many items may be selected at once. Defaults to <see cref="XPlatSelectionMode.One"/>.</summary>
    public XPlatSelectionMode SelectionMode
    {
        get => _selectionMode;
        set
        {
            if (_selectionMode == value)
            {
                return;
            }

            _selectionMode = value;

            if (value == XPlatSelectionMode.None)
            {
                ClearSelected();
            }
            else if (value == XPlatSelectionMode.One)
            {
                CollapseToSingle();
            }
        }
    }

    /// <summary>
    /// The lowest selected index, or -1 when nothing is selected. Setting it selects that item and
    /// nothing else.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">The index is outside the list.</exception>
    public int SelectedIndex
    {
        get => _selected.Count > 0 ? _selected[0] : -1;
        set => SelectSingle(value);
    }

    /// <summary>The item at <see cref="SelectedIndex"/>, or null when nothing is selected.</summary>
    public object? SelectedItem
    {
        get
        {
            int index = SelectedIndex;
            return index >= 0 ? _items[index] : null;
        }
    }

    /// <summary>The selected indices, in ascending order. A live, read-only view.</summary>
    public IReadOnlyList<int> SelectedIndices => _selectedView;

    /// <summary>The selected items, in list order. Built on demand.</summary>
    public IReadOnlyList<object?> SelectedItems
    {
        get
        {
            var items = new object?[_selected.Count];
            for (int i = 0; i < _selected.Count; i++)
            {
                items[i] = _items[_selected[i]];
            }

            return items;
        }
    }

    /// <summary>
    /// The height of one row. Defaults to the font's line height plus room for the selection band,
    /// and follows the font until it is assigned.
    /// </summary>
    public float ItemHeight
    {
        get => _itemHeight > 0f ? _itemHeight : DefaultItemHeight;
        set
        {
            var clamped = MathF.Max(1f, value);
            if (MathF.Abs(ItemHeight - clamped) < 0.01f)
            {
                return;
            }

            _itemHeight = clamped;
            PerformLayout();
            Invalidate();
        }
    }

    /// <summary>The index of the first row at the top of the viewport. Setting it scrolls there.</summary>
    public int TopIndex
    {
        get => Math.Clamp((int)(_scroll.VerticalOffset / ItemHeight), 0, Math.Max(0, _items.Count - 1));
        set => _scroll.ScrollTo(
            _scroll.HorizontalOffset,
            Math.Clamp(value, 0, Math.Max(0, _items.Count - 1)) * ItemHeight);
    }

    /// <summary>Text painted in place of the rows while the list is empty. Empty by default.</summary>
    public string EmptyText
    {
        get => _emptyText;
        set
        {
            var text = value ?? string.Empty;
            if (_emptyText == text)
            {
                return;
            }

            _emptyText = text;
            Invalidate();
        }
    }

    /// <summary>How the vertical axis scrolls. Defaults to <see cref="XPlatScrollStyle.Automatic"/>.</summary>
    public XPlatScrollStyle VerticalScroll
    {
        get => _scroll.Vertical;
        set => _scroll.Vertical = value;
    }

    /// <summary>How the horizontal axis scrolls. Defaults to <see cref="XPlatScrollStyle.None"/>.</summary>
    public XPlatScrollStyle HorizontalScroll
    {
        get => _scroll.Horizontal;
        set => _scroll.Horizontal = value;
    }

    /// <summary>The thickness reserved for a visible scrollbar, in device independent pixels.</summary>
    public float ScrollBarThickness
    {
        get => _scroll.Thickness;
        set => _scroll.Thickness = value;
    }

    /// <summary>The vertical scrollbar, or null until the list first needs one.</summary>
    public XPlatScrollBar? VerticalScrollBar => _scroll.VerticalBar;

    /// <summary>The horizontal scrollbar, or null until the list first needs one.</summary>
    public XPlatScrollBar? HorizontalScrollBar => _scroll.HorizontalBar;

    /// <summary>The current vertical scroll offset, in pixels. Zero when not scrolled.</summary>
    public float VerticalOffset => _scroll.VerticalOffset;

    /// <summary>The current horizontal scroll offset, in pixels. Zero when not scrolled.</summary>
    public float HorizontalOffset => _scroll.HorizontalOffset;

    /// <summary>
    /// The area the rows are painted into: the client area less the gutters of whichever
    /// scrollbars are showing. Before the first layout it is the whole client area.
    /// </summary>
    public SKRect ViewportRect => _scroll.EffectiveViewport(ClientRect);

    /// <summary>Adds a handler that runs whenever the set of selected items changes.</summary>
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

    /// <summary>
    /// Adds a handler that runs when the user activates an item: a double click, or Enter on the
    /// item the keyboard is on. The activated item is the <see cref="SelectedIndex"/>.
    /// </summary>
    public IDisposable AddItemActivatedHandler(Action<object> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        _itemActivated += handler;
        return new XPlatHandlerToken<Action<object>>(RemoveItemActivatedHandler, handler);
    }

    /// <summary>Removes a handler that <see cref="AddItemActivatedHandler"/> added.</summary>
    public void RemoveItemActivatedHandler(Action<object> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        _itemActivated -= handler;
    }

    /// <summary>
    /// Adds a handler that paints the items instead of the default text. Adding one replaces the
    /// default row drawing entirely, background included.
    /// </summary>
    public IDisposable AddDrawItemHandler(Action<object, XPlatListBoxDrawItemEventArgs> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        _drawItem += handler;
        Invalidate();
        return new XPlatHandlerToken<Action<object, XPlatListBoxDrawItemEventArgs>>(RemoveDrawItemHandler, handler);
    }

    /// <summary>Removes a handler that <see cref="AddDrawItemHandler"/> added.</summary>
    public void RemoveDrawItemHandler(Action<object, XPlatListBoxDrawItemEventArgs> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);

        if (_drawItem is not null)
        {
            _drawItem -= handler;
        }

        Invalidate();
    }

    /// <summary>The text a row shows, which is the item's own <see cref="object.ToString"/>.</summary>
    public string GetItemText(int index) => GetItemText(_items[index]);

    /// <summary>True when the item at <paramref name="index"/> is selected.</summary>
    /// <exception cref="ArgumentOutOfRangeException">The index is outside the list.</exception>
    public bool GetSelected(int index)
    {
        ValidateIndex(index);
        return IsRowSelected(index);
    }

    /// <summary>Selects or deselects one item, honouring <see cref="SelectionMode"/>.</summary>
    /// <exception cref="ArgumentOutOfRangeException">The index is outside the list.</exception>
    public void SetSelected(int index, bool selected)
    {
        ValidateIndex(index);

        if (_selectionMode == XPlatSelectionMode.None)
        {
            return;
        }

        if (_selectionMode == XPlatSelectionMode.One)
        {
            if (selected)
            {
                SelectSingle(index);
            }
            else if (IsRowSelected(index))
            {
                ClearSelected();
            }

            return;
        }

        if (selected)
        {
            AddSelected(index);
        }
        else
        {
            RemoveSelected(index);
        }
    }

    /// <summary>Deselects everything.</summary>
    public void ClearSelected()
    {
        if (_selected.Count == 0)
        {
            return;
        }

        var previous = _selected.ToArray();
        _selected.Clear();
        _anchor = -1;
        SetLead(-1);
        InvalidateSelectionChange(previous);
        RaiseSelectedIndexChanged();
    }

    /// <summary>Selects every item. Does nothing in the modes that allow at most one.</summary>
    public void SelectAll()
    {
        if (_selectionMode is XPlatSelectionMode.None or XPlatSelectionMode.One || _items.Count == 0)
        {
            return;
        }

        if (_selected.Count == _items.Count)
        {
            return;
        }

        var previous = _selected.ToArray();
        _selected.Clear();
        for (int i = 0; i < _items.Count; i++)
        {
            _selected.Add(i);
        }

        InvalidateSelectionChange(previous);
        RaiseSelectedIndexChanged();
    }

    /// <summary>The whole row for an item, in this control's coordinates. It may be scrolled out of sight.</summary>
    /// <exception cref="ArgumentOutOfRangeException">The index is outside the list.</exception>
    public SKRect GetItemRect(int index)
    {
        ValidateIndex(index);

        var area = _scroll.ContentArea;
        float height = ItemHeight;
        float top = area.Top - _scroll.VerticalOffset + (index * height);
        float width = MathF.Max(area.Width, _scroll.Extent.Width);

        return new SKRect(area.Left, top, area.Left + width, top + height);
    }

    /// <summary>The item under a point in this control's coordinates, or -1 when there is none.</summary>
    public int IndexFromPoint(SKPoint point)
    {
        if (_items.Count == 0 || !XPlatGeometry.Contains(_scroll.EffectiveViewport(ClientRect), point))
        {
            return -1;
        }

        var area = _scroll.ContentArea;
        if (area.Height <= 0f)
        {
            return -1;
        }

        float y = point.Y - area.Top + _scroll.VerticalOffset;
        if (y < 0f)
        {
            return -1;
        }

        int index = (int)(y / ItemHeight);
        return index < _items.Count ? index : -1;
    }

    /// <summary>Scrolls so that the item is inside the viewport.</summary>
    public void EnsureVisible(int index)
    {
        if (index < 0 || index >= _items.Count || _scroll.ContentArea.Height <= 0f)
        {
            return;
        }

        // GetItemRect reports client coordinates with the scroll offset already taken out, so the
        // row has to be rebuilt in content coordinates here; the scrolling helper expects those.
        float height = ItemHeight;
        float top = index * height;
        _scroll.ScrollVerticalIntoView(top, top + height);
    }

    /// <summary>Scrolls to the given offsets, clamped to the scrollable range.</summary>
    public void ScrollTo(float horizontalOffset, float verticalOffset) =>
        _scroll.ScrollTo(horizontalOffset, verticalOffset);

    /// <summary>Scrolls by a relative amount, clamped to the scrollable range.</summary>
    public void ScrollBy(float horizontalDelta, float verticalDelta) =>
        _scroll.ScrollBy(horizontalDelta, verticalDelta);

    /// <summary>
    /// Re-measures and repaints the list. Call it after changing an item in place, since the list
    /// cannot tell that an object it already holds now reads differently.
    /// </summary>
    public void RefreshItems() => NotifyItemsChanged();

    /// <summary>Stops the list from laying out or repainting until the matching <see cref="EndUpdate"/>.</summary>
    public void BeginUpdate() => _updateCount++;

    /// <summary>Ends an update started by <see cref="BeginUpdate"/> and lays out and repaints once.</summary>
    public void EndUpdate()
    {
        if (_updateCount == 0)
        {
            return;
        }

        _updateCount--;

        if (_updateCount == 0)
        {
            NotifyItemsChanged();
        }
    }

    public override void PerformLayout()
    {
        if (_layingOut)
        {
            return;
        }

        _layingOut = true;
        try
        {
            _scroll.Layout(ClientRect, Padding, MeasureExtent);
        }
        finally
        {
            _layingOut = false;
        }
    }

    protected override SKSize MeasureContent(SKSize availableSize)
    {
        EnsureContentMeasured();

        float height = Math.Max(1, Math.Min(_items.Count, DefaultVisibleRows)) * ItemHeight;
        float width = _contentWidth;
        if (VerticalScroll != XPlatScrollStyle.None)
        {
            width += ScrollBarThickness;
        }

        return new SKSize(width, height);
    }

    public override XPlatControl? HitTest(SKPoint pointInParent)
    {
        if (!Visible)
        {
            return null;
        }

        var local = new SKPoint(pointInParent.X - Left, pointInParent.Y - Top);
        if (!XPlatGeometry.Contains(ClientRect, local))
        {
            return null;
        }

        if (_scroll.VerticalBar?.HitTest(local) is { } verticalHit)
        {
            return verticalHit;
        }

        if (_scroll.HorizontalBar?.HitTest(local) is { } horizontalHit)
        {
            return horizontalHit;
        }

        // Rows are clipped to the viewport, so a point in the bar gutter belongs to the list
        // itself rather than to an item.
        if (!XPlatGeometry.Contains(_scroll.EffectiveViewport(ClientRect), local))
        {
            return Enabled ? this : null;
        }

        return base.HitTest(pointInParent);
    }

    /// <summary>The scrollbars sit in the gutter and never scroll with the rows.</summary>
    protected internal override SKPoint GetChildOffset(XPlatControl child) =>
        _scroll.IsBar(child) ? SKPoint.Empty : _scroll.ChildOffset;

    protected override void RenderChildren(SKCanvas canvas)
    {
        base.RenderChildren(canvas);
        _scroll.RenderBars(canvas);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _scroll.Dispose();
        }

        base.Dispose(disposing);
    }

    protected override void OnPaintBackground(SKCanvas canvas, SKRect rect)
    {
        var fill = Enabled ? BackColor ?? XPlatTheme.InputBackground : XPlatTheme.DisabledBackground;
        XPlatPainter.Fill(canvas, rect, fill, CornerRadius);
    }

    protected override void OnPaint(SKCanvas canvas, SKRect rect)
    {
        var viewport = _scroll.EffectiveViewport(ClientRect);
        if (viewport.Width <= 0f || viewport.Height <= 0f)
        {
            return;
        }

        var (first, last) = VisibleRows();
        if (last < first)
        {
            DrawEmptyText(canvas, viewport);
            return;
        }

        canvas.Save();
        canvas.ClipRect(viewport);

        for (int index = first; index <= last; index++)
        {
            DrawRow(canvas, index);
        }

        canvas.Restore();
    }

    protected override void OnPaintBorder(SKCanvas canvas, SKRect rect)
    {
        var border = !Enabled
            ? XPlatTheme.DisabledBackground
            : IsFocused ? XPlatTheme.Accent : BorderColor;

        XPlatPainter.DrawBorder(canvas, rect, border, BorderWidth > 0f ? BorderWidth : 1f, CornerRadius);
    }

    protected override void OnMouseDown(XPlatMouseEventArgs e)
    {
        base.OnMouseDown(e);

        if (e.Button != XPlatMouseButtons.Left)
        {
            return;
        }

        int index = IndexFromPoint(e.Location);
        if (index < 0)
        {
            return;
        }

        Focus();

        if (e.ClickCount >= 2)
        {
            RaiseItemActivated();
            return;
        }

        _dragging = true;
        CaptureMouse();
        SelectFromPointer(index, e.Modifiers);
    }

    protected override void OnMouseMove(XPlatMouseEventArgs e)
    {
        base.OnMouseMove(e);

        if (_dragging)
        {
            DragTo(e.Location);
            return;
        }

        int index = IndexFromPoint(e.Location);
        if (index == _hotIndex)
        {
            return;
        }

        int previous = _hotIndex;
        _hotIndex = index;
        InvalidateRow(previous);
        InvalidateRow(index);
    }

    protected override void OnMouseUp(XPlatMouseEventArgs e)
    {
        base.OnMouseUp(e);

        if (!_dragging)
        {
            return;
        }

        _dragging = false;
        ReleaseMouseCapture();
    }

    protected override void OnMouseLeave()
    {
        base.OnMouseLeave();

        int previous = _hotIndex;
        _hotIndex = -1;
        InvalidateRow(previous);
    }

    /// <summary>
    /// Scrolls the list, vertically by default and horizontally when Shift is held or the vertical
    /// axis has nothing left to give. The list claims the turn only when it actually moved.
    /// </summary>
    protected override void OnMouseWheel(XPlatMouseEventArgs e)
    {
        base.OnMouseWheel(e);

        if (e.Handled)
        {
            return;
        }

        bool canScrollVertically = _scroll.MaxVerticalOffset > ScrollEpsilon;
        bool canScrollHorizontally = _scroll.MaxHorizontalOffset > ScrollEpsilon;
        bool shift = (e.Modifiers & XPlatModifiers.Shift) != 0;

        if (canScrollVertically && !(shift && canScrollHorizontally))
        {
            var before = _scroll.VerticalOffset;
            ScrollBy(0f, -e.Delta);
            e.Handled = MathF.Abs(_scroll.VerticalOffset - before) > ScrollEpsilon;
            return;
        }

        if (canScrollHorizontally)
        {
            var before = _scroll.HorizontalOffset;
            ScrollBy(-e.Delta, 0f);
            e.Handled = MathF.Abs(_scroll.HorizontalOffset - before) > ScrollEpsilon;
        }
    }

    protected override void OnKeyDown(XPlatKeyEventArgs e)
    {
        base.OnKeyDown(e);

        if (e.Handled || _items.Count == 0)
        {
            return;
        }

        bool shift = (e.Modifiers & XPlatModifiers.Shift) != 0;
        bool control = (e.Modifiers & XPlatModifiers.Control) != 0;

        // In MultiExtended, Ctrl+arrow walks the focus rectangle without touching the selection,
        // which is how a user inspects the list before committing to another range.
        bool cursorOnly = control && !shift && _selectionMode == XPlatSelectionMode.MultiExtended;

        switch (e.Key)
        {
            case XPlatKey.Down:
                MoveLead(1, shift, cursorOnly);
                break;

            case XPlatKey.Up:
                MoveLead(-1, shift, cursorOnly);
                break;

            case XPlatKey.PageDown:
                MoveLead(PageSize, shift, cursorOnly);
                break;

            case XPlatKey.PageUp:
                MoveLead(-PageSize, shift, cursorOnly);
                break;

            case XPlatKey.Home:
                MoveLeadTo(0, shift);
                break;

            case XPlatKey.End:
                MoveLeadTo(_items.Count - 1, shift);
                break;

            case XPlatKey.Space:
                ToggleLead();
                break;

            case XPlatKey.Enter:
                RaiseItemActivated();
                break;

            case XPlatKey.A when control:
                SelectAll();
                break;

            default:
                return;
        }

        e.Handled = true;
    }

    /// <summary>
    /// Type-ahead: typing a few characters moves the keyboard to the first item that starts with
    /// them. The buffer forgets itself after <see cref="TypeAheadTimeoutMs"/> of quiet.
    /// </summary>
    protected override void OnTextInput(XPlatTextInputEventArgs e)
    {
        base.OnTextInput(e);

        if (e.Handled || _selectionMode == XPlatSelectionMode.None || _items.Count == 0)
        {
            return;
        }

        var text = e.Text;
        if (string.IsNullOrEmpty(text) || char.IsControl(text[0]))
        {
            return;
        }

        long now = Environment.TickCount64;
        bool fresh = now - _typeAheadStamp > TypeAheadTimeoutMs;
        var appended = fresh ? text : _typeAhead + text;
        _typeAheadStamp = now;

        // A single new character searches from the item after the current one so that repeatedly
        // pressing the same letter cycles through the items that start with it; a longer prefix
        // refines the match, so it may stay where it is.
        int start = appended.Length > text.Length ? _lead : _lead + 1;
        int match = FindItem(start, appended);

        // Typing a character that extends the buffer into a dead end starts a new search instead of
        // swallowing the keystroke, so "Delta" is still reachable after "Ch".
        if (match < 0 && !fresh)
        {
            appended = text;
            match = FindItem(_lead + 1, text);
        }

        _typeAhead = appended;

        if (match >= 0)
        {
            MoveLeadTo(match, shift: false);
            e.Handled = true;
        }
    }

    /// <summary>Clips a scrollbar's invalidation to the gutter it occupies.</summary>
    protected internal override void ChildInvalidate(XPlatControl child, SKRegion region)
    {
        _scroll.ClipInvalidate(child, region, ClientRect);
        base.ChildInvalidate(child, region);
    }

    protected virtual string GetItemText(object? item) => item?.ToString() ?? string.Empty;

    private static bool ContainsSorted(ReadOnlySpan<int> values, int value)
    {
        int low = 0;
        int high = values.Length - 1;

        while (low <= high)
        {
            int middle = (low + high) >> 1;
            int candidate = values[middle];

            if (candidate == value)
            {
                return true;
            }

            if (candidate < value)
            {
                low = middle + 1;
            }
            else
            {
                high = middle - 1;
            }
        }

        return false;
    }

    private float DefaultItemHeight => MathF.Ceiling(_font.LineHeight + 10f);

    private int PageSize => Math.Max(1, (int)(_scroll.ContentArea.Height / MathF.Max(1f, ItemHeight)));

    private void ValidateIndex(int index)
    {
        if (index < 0 || index >= _items.Count)
        {
            throw new ArgumentOutOfRangeException(nameof(index), index, "The index is outside the list.");
        }
    }

    private bool IsRowSelected(int index) => _selected.BinarySearch(index) >= 0;

    private void EnsureContentMeasured()
    {
        if (!_measureDirty)
        {
            return;
        }

        float width = 0f;
        foreach (var item in _items)
        {
            width = MathF.Max(width, _font.MeasureWidth(GetItemText(item)));
        }

        _contentWidth = width + (TextInset * 2f);
        _measureDirty = false;
    }

    /// <summary>
    /// Reports the size of the rows to the scroll helper. Called once per visibility pass, so the
    /// text is measured once and reused.
    /// </summary>
    private SKSize MeasureExtent(SKRect area)
    {
        EnsureContentMeasured();
        return new SKSize(_contentWidth, _items.Count * ItemHeight);
    }

    private (int First, int Last) VisibleRows()
    {
        var area = _scroll.ContentArea;
        float height = ItemHeight;

        if (_items.Count == 0 || area.Height <= 0f || height <= 0f)
        {
            return (0, -1);
        }

        float offset = _scroll.VerticalOffset;
        int first = Math.Max(0, (int)MathF.Floor(offset / height));
        int last = Math.Min(_items.Count - 1, (int)MathF.Floor((offset + area.Height - 0.01f) / height));

        return (first, last);
    }

    /// <summary>
    /// Moves the focus rectangle to a row and repaints the row it leaves behind.
    /// </summary>
    /// <remarks>
    /// The lead is what the rectangle follows, and it is not the selection: in
    /// <see cref="XPlatSelectionMode.None"/> it is the only thing a click moves. A row draws its
    /// rectangle from the lead, so changing the lead without repainting the old row leaves a
    /// rectangle behind on a row that no longer has one.
    /// </remarks>
    private void SetLead(int index)
    {
        if (_lead == index)
        {
            return;
        }

        int previous = _lead;
        _lead = index;
        InvalidateRow(previous);
        InvalidateRow(index);
    }

    /// <summary>Repaints one row, clipped to the part of it that is on screen.</summary>
    private void InvalidateRow(int index)
    {
        if (index < 0 || index >= _items.Count)
        {
            return;
        }

        var viewport = _scroll.EffectiveViewport(ClientRect);
        var bounds = GetItemRect(index);

        if (bounds.Bottom <= viewport.Top || bounds.Top >= viewport.Bottom)
        {
            return;
        }

        Invalidate(
            new SKRect(
                viewport.Left,
                MathF.Max(bounds.Top, viewport.Top),
                viewport.Right,
                MathF.Min(bounds.Bottom, viewport.Bottom)),
            invalidateChildren: false);
    }

    /// <summary>
    /// Repaints exactly the rows whose selected state differs between the old selection and the
    /// current one, and only those of them that are on screen.
    /// </summary>
    private void InvalidateSelectionChange(ReadOnlySpan<int> previous)
    {
        var (first, last) = VisibleRows();
        if (last < first)
        {
            return;
        }

        for (int index = first; index <= last; index++)
        {
            if (ContainsSorted(previous, index) != IsRowSelected(index))
            {
                InvalidateRow(index);
            }
        }
    }

    private void RaiseSelectedIndexChanged() => _selectedIndexChanged?.Invoke(this);

    private void RaiseItemActivated()
    {
        if (SelectedIndex >= 0)
        {
            _itemActivated?.Invoke(this);
        }
    }

    /// <summary>Replaces the selection with a single item and repaints only what changed.</summary>
    private void SelectSingle(int index)
    {
        if (index < -1 || index >= _items.Count)
        {
            throw new ArgumentOutOfRangeException(nameof(index), index, "The index is outside the list.");
        }

        if (_selectionMode == XPlatSelectionMode.None)
        {
            return;
        }

        if (index < 0)
        {
            ClearSelected();
            return;
        }

        if (_selected.Count == 1 && _selected[0] == index)
        {
            _anchor = index;
            SetLead(index);
            EnsureVisible(index);
            return;
        }

        var previous = _selected.ToArray();
        _selected.Clear();
        _selected.Add(index);
        _anchor = index;
        SetLead(index);

        InvalidateSelectionChange(previous);
        RaiseSelectedIndexChanged();
        EnsureVisible(index);
    }

    private void CollapseToSingle()
    {
        if (_selected.Count <= 1)
        {
            return;
        }

        var previous = _selected.ToArray();
        int keep = _selected[0];
        _selected.Clear();
        _selected.Add(keep);

        InvalidateSelectionChange(previous);
        RaiseSelectedIndexChanged();
    }

    private void AddSelected(int index)
    {
        int at = _selected.BinarySearch(index);
        if (at >= 0)
        {
            SetLead(index);
            return;
        }

        _selected.Insert(~at, index);
        SetLead(index);
        InvalidateRow(index);
        RaiseSelectedIndexChanged();
    }

    private void RemoveSelected(int index)
    {
        int at = _selected.BinarySearch(index);
        if (at < 0)
        {
            return;
        }

        _selected.RemoveAt(at);
        InvalidateRow(index);
        RaiseSelectedIndexChanged();
    }

    private void SelectRange(int from, int to)
    {
        if (_items.Count == 0)
        {
            return;
        }

        int low = Math.Clamp(Math.Min(from, to), 0, _items.Count - 1);
        int high = Math.Clamp(Math.Max(from, to), 0, _items.Count - 1);
        int count = high - low + 1;

        if (count == _selected.Count)
        {
            bool same = true;
            for (int i = 0; i < count; i++)
            {
                if (_selected[i] != low + i)
                {
                    same = false;
                    break;
                }
            }

            if (same)
            {
                return;
            }
        }

        var previous = _selected.ToArray();
        _selected.Clear();
        for (int i = low; i <= high; i++)
        {
            _selected.Add(i);
        }

        InvalidateSelectionChange(previous);
        RaiseSelectedIndexChanged();
    }

    private void SelectFromPointer(int index, XPlatModifiers modifiers)
    {
        if (_selectionMode == XPlatSelectionMode.None)
        {
            SetLead(index);
            return;
        }

        bool shift = (modifiers & XPlatModifiers.Shift) != 0;
        bool control = (modifiers & XPlatModifiers.Control) != 0;

        if (_selectionMode == XPlatSelectionMode.MultiExtended && shift && _anchor >= 0)
        {
            SelectRange(_anchor, index);
            SetLead(index);
            return;
        }

        if (_selectionMode == XPlatSelectionMode.MultiExtended && control)
        {
            ToggleAt(index);
            return;
        }

        if (_selectionMode == XPlatSelectionMode.MultiSimple)
        {
            // MultiSimple toggles on Ctrl only; a plain click is an ordinary single selection.
            if (control)
            {
                ToggleAt(index);
            }
            else
            {
                SelectSingle(index);
            }

            return;
        }

        SelectSingle(index);
    }

    private void ToggleAt(int index)
    {
        if (IsRowSelected(index))
        {
            RemoveSelected(index);
        }
        else
        {
            AddSelected(index);
        }

        _anchor = index;
        SetLead(index);
    }

    private void DragTo(SKPoint point)
    {
        if (_selectionMode is XPlatSelectionMode.None or XPlatSelectionMode.MultiSimple)
        {
            return;
        }

        int index = IndexFromPoint(point);
        if (index < 0 || index == _lead)
        {
            return;
        }

        if (_selectionMode == XPlatSelectionMode.MultiExtended && _anchor >= 0)
        {
            SelectRange(_anchor, index);
            SetLead(index);
        }
        else
        {
            SelectSingle(index);
        }

        EnsureVisible(index);
    }

    private void MoveLead(int delta, bool shift, bool cursorOnly)
    {
        if (_selectionMode == XPlatSelectionMode.None)
        {
            return;
        }

        int from = _lead < 0 ? (delta > 0 ? -1 : _items.Count) : _lead;
        MoveLeadTo(Math.Clamp(from + delta, 0, _items.Count - 1), shift, cursorOnly);
    }

    private void MoveLeadTo(int index, bool shift, bool cursorOnly = false)
    {
        if (_selectionMode == XPlatSelectionMode.None || _items.Count == 0)
        {
            return;
        }

        index = Math.Clamp(index, 0, _items.Count - 1);

        if (cursorOnly)
        {
            SetLead(index);
            EnsureVisible(index);
            return;
        }

        switch (_selectionMode)
        {
            case XPlatSelectionMode.One:
                SelectSingle(index);
                break;

            case XPlatSelectionMode.MultiSimple:
                // The keyboard still selects the row it lands on; Space is what adds or removes one
                // without disturbing the rest.
                SelectSingle(index);
                break;

            default:
                if (shift && _anchor >= 0)
                {
                    SelectRange(_anchor, index);
                    SetLead(index);
                }
                else
                {
                    SelectSingle(index);
                }

                break;
        }

        EnsureVisible(index);
    }

    private void ToggleLead()
    {
        if (_selectionMode == XPlatSelectionMode.None || _items.Count == 0)
        {
            return;
        }

        int index = Math.Clamp(_lead, 0, _items.Count - 1);

        if (_selectionMode == XPlatSelectionMode.One)
        {
            SelectSingle(index);
            return;
        }

        ToggleAt(index);
    }

    private int FindItem(int start, string prefix)
    {
        int count = _items.Count;
        if (count == 0)
        {
            return -1;
        }

        for (int step = 0; step < count; step++)
        {
            int index = (((start + step) % count) + count) % count;
            if (GetItemText(_items[index]).StartsWith(prefix, StringComparison.CurrentCultureIgnoreCase))
            {
                return index;
            }
        }

        return -1;
    }

    private void DrawEmptyText(SKCanvas canvas, SKRect viewport)
    {
        if (_emptyText.Length == 0)
        {
            return;
        }

        var area = _scroll.ContentArea;
        var bounds = area.Height > 0f ? area : viewport;

        XPlatTextRenderer.Draw(
            canvas,
            _emptyText,
            _font,
            Enabled ? XPlatTheme.Placeholder : XPlatTheme.DisabledForeground,
            new SKRect(bounds.Left + TextInset, bounds.Top, bounds.Right - TextInset, bounds.Top + ItemHeight),
            XPlatContentAlignment.MiddleLeft);
    }

    private void DrawRow(SKCanvas canvas, int index)
    {
        var bounds = GetItemRect(index);
        bool selected = IsRowSelected(index);
        bool hot = !selected && index == _hotIndex;
        bool focused = IsFocused && index == _lead;

        if (selected)
        {
            XPlatPainter.Fill(canvas, bounds, IsFocused ? XPlatTheme.Accent : XPlatTheme.Selection);
        }
        else if (hot)
        {
            XPlatPainter.Fill(canvas, bounds, XPlatTheme.SecondaryHover);
        }

        var state = XPlatDrawItemState.None;
        if (selected)
        {
            state |= XPlatDrawItemState.Selected;
        }

        if (focused)
        {
            state |= XPlatDrawItemState.Focused;
        }

        if (hot)
        {
            state |= XPlatDrawItemState.Hot;
        }

        if (!Enabled)
        {
            state |= XPlatDrawItemState.Disabled;
        }

        var textColor = !Enabled
            ? XPlatTheme.DisabledForeground
            : selected
                ? IsFocused ? XPlatTheme.AccentForeground : XPlatTheme.Foreground
                : XPlatTheme.Foreground;

        if (_drawItem is { } drawItem)
        {
            drawItem(this, new XPlatListBoxDrawItemEventArgs(canvas, index, _items[index], bounds, state, _font, textColor));
            return;
        }

        var textBounds = new SKRect(bounds.Left + TextInset, bounds.Top, bounds.Right - TextInset, bounds.Bottom);
        XPlatTextRenderer.Draw(canvas, GetItemText(_items[index]), _font, textColor, textBounds, XPlatContentAlignment.MiddleLeft);

        if (focused)
        {
            // A thin rectangle on the row the keyboard is on, so it stays findable when the
            // selection is somewhere else - the same idea as WinForms' focus rectangle.
            XPlatPainter.DrawBorder(
                canvas,
                XPlatGeometry.Deflate(textBounds, 1f),
                textColor.WithAlpha(120),
                1f,
                2f);
        }
    }

    /// <summary>Called by <see cref="ItemCollection"/> after the items changed.</summary>
    internal void NotifyItemsChanged()
    {
        _measureDirty = true;
        _hotIndex = -1;

        bool selectionChanged = false;
        while (_selected.Count > 0 && _selected[^1] >= _items.Count)
        {
            _selected.RemoveAt(_selected.Count - 1);
            selectionChanged = true;
        }

        if (_lead >= _items.Count)
        {
            _lead = _items.Count - 1;
        }

        if (_anchor >= _items.Count)
        {
            _anchor = _items.Count - 1;
        }

        if (_updateCount > 0)
        {
            // Whatever was clamped is reported once, when the batch closes.
            _pendingSelectionChanged |= selectionChanged;
            return;
        }

        PerformLayout();
        Invalidate();

        if (selectionChanged || _pendingSelectionChanged)
        {
            _pendingSelectionChanged = false;
            RaiseSelectedIndexChanged();
        }
    }

    /// <summary>The items of an <see cref="XPlatListBox"/>, in the order they are painted.</summary>
    public sealed class ItemCollection : IList<object?>
    {
        private readonly XPlatListBox _owner;
        private readonly List<object?> _items;

        internal ItemCollection(XPlatListBox owner, List<object?> items)
        {
            _owner = owner;
            _items = items;
        }

        public object? this[int index]
        {
            get => _items[index];
            set
            {
                if (Equals(_items[index], value))
                {
                    return;
                }

                _items[index] = value;
                _owner.NotifyItemsChanged();
            }
        }

        public int Count => _items.Count;

        public bool IsReadOnly => false;

        public void Add(object? item)
        {
            _items.Add(item);
            _owner.NotifyItemsChanged();
        }

        /// <summary>Adds several items with a single layout and repaint.</summary>
        public void AddRange(IEnumerable<object?> items)
        {
            ArgumentNullException.ThrowIfNull(items);

            _owner.BeginUpdate();
            try
            {
                foreach (var item in items)
                {
                    _items.Add(item);
                }
            }
            finally
            {
                _owner.EndUpdate();
            }
        }

        public void Insert(int index, object? item)
        {
            _items.Insert(index, item);
            _owner.NotifyItemsChanged();
        }

        public bool Remove(object? item)
        {
            int index = _items.IndexOf(item);
            if (index < 0)
            {
                return false;
            }

            RemoveAt(index);
            return true;
        }

        public void RemoveAt(int index)
        {
            _items.RemoveAt(index);
            _owner.NotifyItemsChanged();
        }

        public void Clear()
        {
            if (_items.Count == 0)
            {
                return;
            }

            _items.Clear();
            _owner.NotifyItemsChanged();
        }

        public bool Contains(object? item) => _items.Contains(item);

        public int IndexOf(object? item) => _items.IndexOf(item);

        public void CopyTo(object?[] array, int arrayIndex) => _items.CopyTo(array, arrayIndex);

        public IEnumerator<object?> GetEnumerator() => _items.GetEnumerator();

        IEnumerator IEnumerable.GetEnumerator() => _items.GetEnumerator();
    }
}
