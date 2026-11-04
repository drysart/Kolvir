using System.Collections.ObjectModel;
using SkiaSharp;

namespace XPlatUI;

/// <summary>
/// The shared body of the controls that paint a vertical list of rows: <see cref="XPlatListBox"/>,
/// <see cref="XPlatListView"/> and <see cref="XPlatTreeView"/>.
/// </summary>
/// <remarks>
/// <para>
/// A derived control supplies a row source - how many rows there are, what text a row carries, how
/// wide the widest row is and how to paint one - and inherits everything else: the scrollbars, the
/// visible band, the four selection modes, the focus rectangle, hover tracking, type-ahead and the
/// keyboard.
/// </para>
/// <para>
/// Only the rows that are on screen are measured, painted or invalidated, so the row count is only
/// bounded by memory. A row is assumed to be <see cref="ItemHeight"/> tall and to start at the top
/// of the content, which is what makes the mapping between an index and a position arithmetic
/// rather than a walk.
/// </para>
/// <para>
/// Selection here is the control's own notion of "which rows are picked" and has nothing to do
/// with the host text selection that <see cref="XPlatControl.Selectable"/> opts into.
/// </para>
/// </remarks>
public abstract class XPlatVirtualList : XPlatControl
{
    /// <summary>The gap between a row's edge and its text.</summary>
    protected const float TextInset = 8f;

    private const float ScrollEpsilon = 0.5f;
    private const long TypeAheadTimeoutMs = 700;

    private readonly XPlatScrollBars _scroll;
    private readonly List<int> _selected = [];
    private readonly ReadOnlyCollection<int> _selectedView;

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

    protected XPlatVirtualList()
    {
        // The scroll helper has to exist before its own properties are assigned: setting one asks
        // the owner to lay out, and the owner lays out through the helper.
        _scroll = new XPlatScrollBars(this);
        _selectedView = _selected.AsReadOnly();

        Focusable = true;
        TabStop = true;
        BorderWidth = 1f;
        CornerRadius = 4f;
        Padding = new XPlatThickness(2f);
        MinimumSize = new SKSize(0f, 32f);
        _scroll.Vertical = XPlatScrollStyle.Automatic;
    }

    /// <summary>Number of rows the control currently has.</summary>
    public abstract int ItemCount { get; }

    /// <summary>The text a row carries, which is what type-ahead matches against.</summary>
    /// <exception cref="ArgumentOutOfRangeException">The index is outside the list.</exception>
    public abstract string GetItemText(int index);

    /// <summary>The row's own object, or null when the row is not a single object.</summary>
    /// <exception cref="ArgumentOutOfRangeException">The index is outside the list.</exception>
    public abstract object? GetItem(int index);

    /// <summary>The width of the widest row, which is the horizontal extent the rows are drawn at.</summary>
    protected abstract float MeasureItemWidth();

    /// <summary>
    /// Paints one row. The band behind the row - selection, hover - has already been filled, so
    /// this is the row's content.
    /// </summary>
    /// <param name="canvas">Already clipped to the viewport and translated to the control's own coordinates.</param>
    /// <param name="index">The row's index.</param>
    /// <param name="bounds">The whole row, which may be wider than the viewport.</param>
    /// <param name="state">What the row is: selected, focused, hot, disabled.</param>
    /// <param name="textColor">The colour the default drawing would have used for the row's text.</param>
    protected abstract void OnDrawItem(SKCanvas canvas, int index, SKRect bounds, XPlatDrawItemState state, SKColor textColor);

    /// <summary>A list paints on the input fill, like the other fields, until <see cref="XPlatControl.BackColor"/> is assigned.</summary>
    protected override SKColor? DefaultBackColor => XPlatTheme.InputBackground;

    /// <summary>Font used to draw the rows. The control does not take ownership of it.</summary>
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

    /// <summary>How many rows may be selected at once. Defaults to <see cref="XPlatSelectionMode.One"/>.</summary>
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
    /// The lowest selected index, or -1 when nothing is selected. Setting it selects that row and
    /// nothing else.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">The index is outside the list.</exception>
    public int SelectedIndex
    {
        get => _selected.Count > 0 ? _selected[0] : -1;
        set => SelectSingle(value);
    }

    /// <summary>The row at <see cref="SelectedIndex"/>, or null when nothing is selected.</summary>
    public object? SelectedItem => SelectedIndex >= 0 ? GetItem(SelectedIndex) : null;

    /// <summary>The selected indices, in ascending order. A live, read-only view.</summary>
    public IReadOnlyList<int> SelectedIndices => _selectedView;

    /// <summary>The selected rows, in list order. Built on demand.</summary>
    public IReadOnlyList<object?> SelectedItems
    {
        get
        {
            var items = new object?[_selected.Count];
            for (int i = 0; i < _selected.Count; i++)
            {
                items[i] = GetItem(_selected[i]);
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
        get => Math.Clamp((int)(_scroll.VerticalOffset / ItemHeight), 0, Math.Max(0, ItemCount - 1));
        set => _scroll.ScrollTo(
            _scroll.HorizontalOffset,
            Math.Clamp(value, 0, Math.Max(0, ItemCount - 1)) * ItemHeight);
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

    /// <summary>The vertical scrollbar, or null until the control first needs one.</summary>
    public XPlatScrollBar? VerticalScrollBar => _scroll.VerticalBar;

    /// <summary>The horizontal scrollbar, or null until the control first needs one.</summary>
    public XPlatScrollBar? HorizontalScrollBar => _scroll.HorizontalBar;

    /// <summary>The current vertical scroll offset, in pixels. Zero when not scrolled.</summary>
    public float VerticalOffset => _scroll.VerticalOffset;

    /// <summary>The current horizontal scroll offset, in pixels. Zero when not scrolled.</summary>
    public float HorizontalOffset => _scroll.HorizontalOffset;

    /// <summary>
    /// The area the rows are painted into: <see cref="ScrollClient"/> less the gutters of whichever
    /// scrollbars are showing. Before the first layout it is the whole of <see cref="ScrollClient"/>.
    /// </summary>
    public SKRect ViewportRect => _scroll.EffectiveViewport(ScrollClient);

    /// <summary>
    /// The area the rows are laid out in: the viewport without the scroll padding and without the
    /// scrollbars' gutter. A control that draws something above its rows lines it up with this.
    /// </summary>
    public SKRect RowsRect => _scroll.ContentArea;

    /// <summary>Adds a handler that runs whenever the set of selected rows changes.</summary>
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
    /// Adds a handler that runs when the user activates a row: a double click, or Enter on the row
    /// the keyboard is on. The activated row is the <see cref="SelectedIndex"/>.
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

    /// <summary>True when the row at <paramref name="index"/> is selected.</summary>
    /// <exception cref="ArgumentOutOfRangeException">The index is outside the list.</exception>
    public bool GetSelected(int index)
    {
        ValidateIndex(index);
        return IsRowSelected(index);
    }

    /// <summary>Selects or deselects one row, honouring <see cref="SelectionMode"/>.</summary>
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

    /// <summary>Selects every row. Does nothing in the modes that allow at most one.</summary>
    public void SelectAll()
    {
        if (_selectionMode is XPlatSelectionMode.None or XPlatSelectionMode.One || ItemCount == 0)
        {
            return;
        }

        if (_selected.Count == ItemCount)
        {
            return;
        }

        var previous = _selected.ToArray();
        _selected.Clear();
        for (int i = 0; i < ItemCount; i++)
        {
            _selected.Add(i);
        }

        InvalidateSelectionChange(previous);
        RaiseSelectedIndexChanged();
    }

    /// <summary>The whole row for a row index, in this control's coordinates. It may be scrolled out of sight.</summary>
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

    /// <summary>The row under a point in this control's coordinates, or -1 when there is none.</summary>
    public int IndexFromPoint(SKPoint point)
    {
        if (ItemCount == 0 || !XPlatGeometry.Contains(_scroll.EffectiveViewport(ScrollClient), point))
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
        return index < ItemCount ? index : -1;
    }

    /// <summary>Scrolls so that the row is inside the viewport.</summary>
    public void EnsureVisible(int index)
    {
        if (index < 0 || index >= ItemCount || _scroll.ContentArea.Height <= 0f)
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
    /// Re-measures and repaints the rows. Call it after changing something in place, since the
    /// control cannot tell that a row it already knows about now reads differently.
    /// </summary>
    public void RefreshItems() => NotifyItemsChanged();

    /// <summary>Stops the control from laying out or repainting until the matching <see cref="EndUpdate"/>.</summary>
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

    /// <summary>
    /// The area the scrollbars are laid out in and the rows are clipped to. A control with a fixed
    /// band above the rows - a list view's column headers - takes that band out here.
    /// </summary>
    protected virtual SKRect ScrollClient => ClientRect;

    /// <summary>The padding applied inside the viewport, which is <see cref="XPlatControl.Padding"/> unless a derived control says otherwise.</summary>
    protected virtual XPlatThickness ScrollPadding => Padding;

    /// <summary>Rows an auto-sized control shows when nothing constrains it.</summary>
    protected virtual int DefaultVisibleRows => 8;

    /// <summary>The row height used until <see cref="ItemHeight"/> is assigned.</summary>
    protected virtual float DefaultItemHeight => MathF.Ceiling(_font.LineHeight + 10f);

    /// <summary>The row the focus rectangle is on, or -1. It is not the selection.</summary>
    protected int LeadIndex => _lead;

    /// <summary>The row the pointer is over, or -1.</summary>
    protected int HotIndex => _hotIndex;

    /// <summary>True when the row at <paramref name="index"/> is part of the selection.</summary>
    protected bool IsRowSelected(int index) => _selected.BinarySearch(index) >= 0;

    /// <summary>The rectangle a row's text occupies, which is also where the focus rectangle goes.</summary>
    protected SKRect TextBoundsOf(SKRect bounds) =>
        new(bounds.Left + TextInset, bounds.Top, bounds.Right - TextInset, bounds.Bottom);

    /// <summary>Draws a row's text with the control's font, at <see cref="TextBoundsOf"/>.</summary>
    protected void DrawItemText(SKCanvas canvas, SKRect bounds, string text, SKColor color) =>
        XPlatTextRenderer.Draw(canvas, text, _font, color, TextBoundsOf(bounds), XPlatContentAlignment.MiddleLeft);

    /// <summary>
    /// Draws the thin rectangle that marks the row the keyboard is on, so it stays findable when
    /// the selection is somewhere else - the same idea as WinForms' focus rectangle.
    /// </summary>
    protected void DrawFocusRectangle(SKCanvas canvas, SKRect bounds, SKColor color) =>
        XPlatPainter.DrawBorder(canvas, XPlatGeometry.Deflate(TextBoundsOf(bounds), 1f), color.WithAlpha(120), 1f, 2f);

    /// <summary>
    /// Moves the focus rectangle to a row and repaints the row it leaves behind.
    /// </summary>
    /// <remarks>
    /// The lead is what the rectangle follows, and it is not the selection: in
    /// <see cref="XPlatSelectionMode.None"/> it is the only thing a click moves. A row draws its
    /// rectangle from the lead, so changing the lead without repainting the old row leaves a
    /// rectangle behind on a row that no longer has one.
    /// </remarks>
    protected void SetLead(int index)
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

    /// <summary>Selects one row and nothing else, then brings it into view.</summary>
    protected void SelectOnly(int index) => SelectSingle(index);

    /// <summary>Raises <see cref="AddItemActivatedHandler"/> for the selected row.</summary>
    protected virtual void OnItemActivated()
    {
        if (SelectedIndex >= 0)
        {
            _itemActivated?.Invoke(this);
        }
    }

    protected override void OnLayout()
    {
        if (_layingOut)
        {
            return;
        }

        _layingOut = true;
        try
        {
            _scroll.Layout(ScrollClient, ScrollPadding, MeasureExtent);
        }
        finally
        {
            _layingOut = false;
        }
    }

    protected override SKSize MeasureContent(SKSize availableSize)
    {
        EnsureContentMeasured();

        float height = Math.Max(1, Math.Min(ItemCount, DefaultVisibleRows)) * ItemHeight;
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

        // Rows are clipped to the viewport, so a point in the bar gutter - or in a band above the
        // rows - belongs to the control itself rather than to an item.
        if (!XPlatGeometry.Contains(_scroll.EffectiveViewport(ScrollClient), local))
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
        var viewport = _scroll.EffectiveViewport(ScrollClient);
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
            OnItemActivated();
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

        if (e.Handled || ItemCount == 0)
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
                MoveLeadTo(ItemCount - 1, shift);
                break;

            case XPlatKey.Space:
                ToggleLead();
                break;

            case XPlatKey.Enter:
                OnItemActivated();
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
    /// Type-ahead: typing a few characters moves the keyboard to the first row that starts with
    /// them. The buffer forgets itself after a moment of quiet.
    /// </summary>
    protected override void OnTextInput(XPlatTextInputEventArgs e)
    {
        base.OnTextInput(e);

        if (e.Handled || _selectionMode == XPlatSelectionMode.None || ItemCount == 0)
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

        // A single new character searches from the row after the current one so that repeatedly
        // pressing the same letter cycles through the rows that start with it; a longer prefix
        // refines the match, so it may stay where it is.
        int start = appended.Length > text.Length ? _lead : _lead + 1;
        int match = FindRow(start, appended);

        // Typing a character that extends the buffer into a dead end starts a new search instead of
        // swallowing the keystroke, so "Delta" is still reachable after "Ch".
        if (match < 0 && !fresh)
        {
            appended = text;
            match = FindRow(_lead + 1, text);
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

    /// <summary>Called after the row source changed, by the derived control or its collection.</summary>
    protected internal void NotifyItemsChanged()
    {
        _measureDirty = true;
        _hotIndex = -1;

        bool selectionChanged = false;
        while (_selected.Count > 0 && _selected[^1] >= ItemCount)
        {
            _selected.RemoveAt(_selected.Count - 1);
            selectionChanged = true;
        }

        if (_lead >= ItemCount)
        {
            _lead = ItemCount - 1;
        }

        if (_anchor >= ItemCount)
        {
            _anchor = ItemCount - 1;
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

    private int PageSize => Math.Max(1, (int)(_scroll.ContentArea.Height / MathF.Max(1f, ItemHeight)));

    private void ValidateIndex(int index)
    {
        if (index < 0 || index >= ItemCount)
        {
            throw new ArgumentOutOfRangeException(nameof(index), index, "The index is outside the list.");
        }
    }

    private void EnsureContentMeasured()
    {
        if (!_measureDirty)
        {
            return;
        }

        _contentWidth = MeasureItemWidth();
        _measureDirty = false;
    }

    /// <summary>
    /// Reports the size of the rows to the scroll helper. Called once per visibility pass, so the
    /// rows are measured once and reused.
    /// </summary>
    private SKSize MeasureExtent(SKRect area)
    {
        EnsureContentMeasured();
        return new SKSize(_contentWidth, ItemCount * ItemHeight);
    }

    private (int First, int Last) VisibleRows()
    {
        var area = _scroll.ContentArea;
        float height = ItemHeight;

        if (ItemCount == 0 || area.Height <= 0f || height <= 0f)
        {
            return (0, -1);
        }

        float offset = _scroll.VerticalOffset;
        int first = Math.Max(0, (int)MathF.Floor(offset / height));
        int last = Math.Min(ItemCount - 1, (int)MathF.Floor((offset + area.Height - 0.01f) / height));

        return (first, last);
    }

    /// <summary>Repaints one row, clipped to the part of it that is on screen.</summary>
    private void InvalidateRow(int index)
    {
        if (index < 0 || index >= ItemCount)
        {
            return;
        }

        var viewport = _scroll.EffectiveViewport(ScrollClient);
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

    private void RaiseSelectedIndexChanged()
    {
        // Inside BeginUpdate/EndUpdate the change is reported once, when the batch closes, so that
        // restoring a selection item by item does not raise the event item by item.
        if (_updateCount > 0)
        {
            _pendingSelectionChanged = true;
            return;
        }

        _selectedIndexChanged?.Invoke(this);
    }

    /// <summary>Replaces the selection with a single row and repaints only what changed.</summary>
    private void SelectSingle(int index)
    {
        if (index < -1 || index >= ItemCount)
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
        if (ItemCount == 0)
        {
            return;
        }

        int low = Math.Clamp(Math.Min(from, to), 0, ItemCount - 1);
        int high = Math.Clamp(Math.Max(from, to), 0, ItemCount - 1);
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

        int from = _lead < 0 ? (delta > 0 ? -1 : ItemCount) : _lead;
        MoveLeadTo(Math.Clamp(from + delta, 0, ItemCount - 1), shift, cursorOnly);
    }

    private void MoveLeadTo(int index, bool shift, bool cursorOnly = false)
    {
        if (_selectionMode == XPlatSelectionMode.None || ItemCount == 0)
        {
            return;
        }

        index = Math.Clamp(index, 0, ItemCount - 1);

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
        if (_selectionMode == XPlatSelectionMode.None || ItemCount == 0)
        {
            return;
        }

        int index = Math.Clamp(_lead, 0, ItemCount - 1);

        if (_selectionMode == XPlatSelectionMode.One)
        {
            SelectSingle(index);
            return;
        }

        ToggleAt(index);
    }

    private int FindRow(int start, string prefix)
    {
        int count = ItemCount;
        if (count == 0)
        {
            return -1;
        }

        for (int step = 0; step < count; step++)
        {
            int index = (((start + step) % count) + count) % count;
            if (GetItemText(index).StartsWith(prefix, StringComparison.CurrentCultureIgnoreCase))
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

        OnDrawItem(canvas, index, bounds, state, textColor);
    }
}
