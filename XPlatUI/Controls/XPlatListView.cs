using System.Collections;
using SkiaSharp;

namespace XPlatUI;

/// <summary>
/// One column of an <see cref="XPlatListView"/> in <see cref="XPlatView.Details"/> view: the text
/// in its header, how wide it is and how its cells are aligned.
/// </summary>
public sealed class XPlatListViewColumn
{
    private string _text = string.Empty;
    private float _width = 120f;
    private XPlatContentAlignment _alignment = XPlatContentAlignment.MiddleLeft;
    private Comparison<XPlatListViewItem>? _comparer;

    public XPlatListViewColumn()
    {
    }

    public XPlatListViewColumn(string text, float width, XPlatContentAlignment alignment = XPlatContentAlignment.MiddleLeft)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(width);

        _text = text ?? string.Empty;
        _width = width;
        _alignment = alignment;
    }

    /// <summary>The column's header text.</summary>
    public string Text
    {
        get => _text;
        set
        {
            var text = value ?? string.Empty;
            if (_text == text)
            {
                return;
            }

            _text = text;
            Owner?.OnColumnChanged();
        }
    }

    /// <summary>
    /// The column's width in logical pixels. The last column also grows to fill the control when
    /// the columns together are narrower than it is.
    /// </summary>
    public float Width
    {
        get => _width;
        set
        {
            ArgumentOutOfRangeException.ThrowIfNegative(value);

            if (_width == value)
            {
                return;
            }

            _width = value;
            Owner?.OnColumnChanged();
        }
    }

    /// <summary>How the header and the cells are aligned. Only the horizontal half is used.</summary>
    public XPlatContentAlignment Alignment
    {
        get => _alignment;
        set
        {
            if (_alignment == value)
            {
                return;
            }

            _alignment = value;
            Owner?.OnColumnChanged();
        }
    }

    /// <summary>
    /// How two rows are ordered when this column is the one being sorted, or <see langword="null"/> to
    /// compare the cell text with the current culture. The comparison is always written ascending: the
    /// control turns it around for <see cref="XPlatSortOrder.Descending"/>, and rows that compare equal
    /// keep the order they were added in.
    /// </summary>
    /// <remarks>
    /// Use this when the cell text is not the value being ordered, such as a right-aligned "812 KB"
    /// that has to sort above "4 KB". The items are handed over whole, so a row can keep its real
    /// value in <see cref="XPlatListViewItem.Tag"/> and sort on that instead of parsing the text.
    /// </remarks>
    public Comparison<XPlatListViewItem>? Comparer
    {
        get => _comparer;
        set
        {
            if (ReferenceEquals(_comparer, value))
            {
                return;
            }

            _comparer = value;
            Owner?.OnColumnComparerChanged(this);
        }
    }

    /// <summary>The list this column belongs to, or <see langword="null"/> when it is in none.</summary>
    public XPlatListView? Owner { get; internal set; }

    /// <summary>The column's position in <see cref="XPlatListView.Columns"/>, or -1.</summary>
    public int Index => Owner is { } owner ? owner.Columns.IndexOf(this) : -1;
}

/// <summary>One row of an <see cref="XPlatListView"/>: a text for the first column plus cells.</summary>
public sealed class XPlatListViewItem
{
    private readonly List<string> _cells;

    public XPlatListViewItem()
        : this(string.Empty)
    {
    }

    public XPlatListViewItem(string text, params string[] cells)
    {
        _cells = new List<string>(1 + (cells?.Length ?? 0)) { text ?? string.Empty };

        if (cells is not null)
        {
            foreach (var cell in cells)
            {
                _cells.Add(cell ?? string.Empty);
            }
        }
    }

    /// <summary>The first column's text.</summary>
    public string Text
    {
        get => _cells[0];
        set => SetCell(0, value);
    }

    /// <summary>Whatever the application wants to hang off the row.</summary>
    public object? Tag { get; set; }

    /// <summary>The list this item belongs to, or <see langword="null"/> when it is in none.</summary>
    public XPlatListView? Owner { get; internal set; }

    /// <summary>The item's position in <see cref="XPlatListView.Items"/>, or -1.</summary>
    public int Index => Owner is { } owner ? owner.Items.IndexOf(this) : -1;

    /// <summary>True when the item is part of the list's selection.</summary>
    public bool Selected
    {
        get
        {
            int index = Index;
            return index >= 0 && Owner is { } owner && owner.GetSelected(index);
        }
        set
        {
            int index = Index;
            if (index >= 0 && Owner is { } owner)
            {
                owner.SetSelected(index, value);
            }
        }
    }

    /// <summary>How many cells the row has, which is at least one.</summary>
    public int CellCount => _cells.Count;

    /// <summary>The text in a column, or an empty string when the row has nothing for it.</summary>
    public string GetCell(int index) => index >= 0 && index < _cells.Count ? _cells[index] : string.Empty;

    /// <summary>
    /// Sets the text in a column, growing the row when the column is past the cells it already has.
    /// </summary>
    public void SetCell(int index, string? text)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(index);

        var value = text ?? string.Empty;
        if (index >= _cells.Count)
        {
            if (value.Length == 0)
            {
                return;
            }

            while (_cells.Count < index)
            {
                _cells.Add(string.Empty);
            }

            _cells.Add(value);
        }
        else
        {
            if (_cells[index] == value)
            {
                return;
            }

            _cells[index] = value;
        }

        Owner?.NotifyItemsChanged();
    }
}

/// <summary>What a <see cref="XPlatListView"/> column-click handler is given.</summary>
public class XPlatListViewColumnClickEventArgs
{
    public XPlatListViewColumnClickEventArgs(XPlatListViewColumn column, int columnIndex)
    {
        Column = column;
        ColumnIndex = columnIndex;
    }

    /// <summary>The column whose header was clicked.</summary>
    public XPlatListViewColumn Column { get; }

    /// <summary>The column's position in <see cref="XPlatListView.Columns"/>.</summary>
    public int ColumnIndex { get; }

    /// <summary>
    /// Set to <see langword="true"/> to stop the control from sorting the column itself, which is
    /// what an application does when it wants to sort on something other than the cell text.
    /// </summary>
    public bool Cancel { get; set; }
}

/// <summary>
/// A list of items shown as rows of columns, with an optional header band that sorts them. It is
/// the closest thing this library has to <c>System.Windows.Forms.ListView</c>.
/// </summary>
/// <remarks>
/// <para>
/// Both views are virtualized: only the rows inside the viewport are painted and measured, so the
/// item count is limited by memory rather than by paint time. The scrolling, selection, type-ahead
/// and keyboard behaviour all come from <see cref="XPlatVirtualList"/>.
/// </para>
/// <para>
/// Selection is always the whole row, so there is no <c>FullRowSelect</c> to turn off, and the
/// cells are plain strings drawn with the control's font - the icon views of WinForms
/// (<c>LargeIcon</c>, <c>SmallIcon</c>, <c>Tile</c>) are not implemented.
/// </para>
/// </remarks>
public class XPlatListView : XPlatVirtualList
{
    private readonly List<XPlatListViewItem> _items = [];
    private readonly List<XPlatListViewColumn> _columns = [];
    private readonly XPlatListViewItemCollection _itemCollection;
    private readonly XPlatListViewColumnCollection _columnCollection;

    /// <summary>How close to a column's right edge the pointer has to be to grab it.</summary>
    private const float ResizeGrabSize = 4f;

    /// <summary>The narrowest a column can be dragged, so that it stays big enough to grab again.</summary>
    private const float MinColumnWidth = 24f;

    private XPlatView _view = XPlatView.Details;
    private XPlatSortOrder _sortOrder;
    private int _sortColumn = -1;
    private int _hotColumn = -1;
    private float? _headerHeight;
    private bool _gridLines;
    private bool _allowColumnResize = true;
    private int _resizeColumn = -1;
    private float _resizeOrigin;
    private float _resizeOriginWidth;

    private Action<object, XPlatListViewColumnClickEventArgs>? _columnClick;

    public XPlatListView()
    {
        _itemCollection = new XPlatListViewItemCollection(this, _items);
        _columnCollection = new XPlatListViewColumnCollection(this, _columns);
    }

    /// <summary>The rows of the list, in the order they are painted.</summary>
    public XPlatListViewItemCollection Items => _itemCollection;

    /// <summary>The columns of the list, which only <see cref="XPlatView.Details"/> draws.</summary>
    public XPlatListViewColumnCollection Columns => _columnCollection;

    /// <summary>How the items are arranged. Changing it re-measures the rows.</summary>
    public XPlatView View
    {
        get => _view;
        set
        {
            if (_view == value)
            {
                return;
            }

            _view = value;
            _hotColumn = -1;
            EndColumnResize();
            NotifyItemsChanged();
        }
    }

    /// <summary>
    /// The height of the header band in <see cref="XPlatView.Details"/>. It defaults to the font's
    /// line height plus a little padding.
    /// </summary>
    public float HeaderHeight
    {
        get => _headerHeight ?? MathF.Ceiling(Font.LineHeight + 8f);
        set
        {
            ArgumentOutOfRangeException.ThrowIfNegative(value);

            if (HeaderHeight == value)
            {
                return;
            }

            _headerHeight = value;
            NotifyItemsChanged();
        }
    }

    /// <summary>True to draw a hairline between rows and between columns, as WinForms' <c>GridLines</c> does.</summary>
    public bool GridLines
    {
        get => _gridLines;
        set
        {
            if (_gridLines == value)
            {
                return;
            }

            _gridLines = value;
            Invalidate();
        }
    }

    /// <summary>
    /// True to let the width of a column be changed by dragging its right edge in the header.
    /// Defaults to true.
    /// </summary>
    /// <remarks>
    /// Only <see cref="XPlatView.Details"/> has a header to drag. The last column is not a drag
    /// target while it is stretched to fill the control, because its width is then the space that
    /// is left over rather than a width of its own; set <see cref="XPlatListViewColumn.Width"/> to
    /// size it. Columns are never dragged narrower than 24 pixels.
    /// </remarks>
    public bool AllowColumnResize
    {
        get => _allowColumnResize;
        set
        {
            if (_allowColumnResize == value)
            {
                return;
            }

            _allowColumnResize = value;
            EndColumnResize();
        }
    }

    /// <summary>The column the items are sorted by, or -1 for none.</summary>
    public int SortColumn
    {
        get => _sortColumn;
        set
        {
            if (_sortColumn == value)
            {
                return;
            }

            _sortColumn = value;
            Invalidate();

            if (_sortOrder != XPlatSortOrder.None)
            {
                Sort();
            }
        }
    }

    /// <summary>
    /// Which way <see cref="SortColumn"/> sorts. Setting it to anything but
    /// <see cref="XPlatSortOrder.None"/> re-sorts straight away; setting it to
    /// <see cref="XPlatSortOrder.None"/> stops sorting and leaves the items in the order they are
    /// in, as WinForms does.
    /// </summary>
    public XPlatSortOrder SortOrder
    {
        get => _sortOrder;
        set
        {
            if (_sortOrder == value)
            {
                return;
            }

            _sortOrder = value;
            Invalidate();

            if (value != XPlatSortOrder.None)
            {
                Sort();
            }
        }
    }

    /// <summary>Number of items, which is the same as <c>Items.Count</c>.</summary>
    public override int ItemCount => _items.Count;

    /// <summary>Adds a handler that is called when a column's header is clicked.</summary>
    public IDisposable AddColumnClickHandler(Action<object, XPlatListViewColumnClickEventArgs> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        _columnClick += handler;
        return new XPlatHandlerToken<Action<object, XPlatListViewColumnClickEventArgs>>(RemoveColumnClickHandler, handler);
    }

    /// <summary>Removes a handler that <see cref="AddColumnClickHandler"/> added.</summary>
    public void RemoveColumnClickHandler(Action<object, XPlatListViewColumnClickEventArgs> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);

        if (_columnClick is not null)
        {
            _columnClick -= handler;
        }
    }

    /// <summary>
    /// Re-orders the items by <see cref="SortColumn"/> and <see cref="SortOrder"/>. The sort is
    /// stable, orders the cells with the column's <see cref="XPlatListViewColumn.Comparer"/> when it has
    /// one and their text with the current culture when it does not, and keeps the selection on the
    /// items that had it.
    /// </summary>
    public void Sort()
    {
        if (_sortOrder == XPlatSortOrder.None || _sortColumn < 0 || _sortColumn >= _columns.Count || _items.Count < 2)
        {
            return;
        }

        int count = _items.Count;
        var order = new int[count];
        for (int i = 0; i < count; i++)
        {
            order[i] = i;
        }

        int column = _sortColumn;
        var comparer = _columns[column].Comparer;
        bool descending = _sortOrder == XPlatSortOrder.Descending;
        Array.Sort(order, (left, right) =>
        {
            // Swapping the rows rather than negating the result turns the order around without the
            // overflow that a comparer returning int.MinValue would hit.
            int first = descending ? right : left;
            int second = descending ? left : right;

            int result = comparer is null
                ? string.Compare(
                    _items[first].GetCell(column),
                    _items[second].GetCell(column),
                    StringComparison.CurrentCultureIgnoreCase)
                : comparer(_items[first], _items[second]);

            // Falling back to the original position keeps equal rows in the order they were added.
            return result == 0 ? left.CompareTo(right) : result;
        });

        var sorted = new XPlatListViewItem[count];
        var movedTo = new int[count];
        for (int i = 0; i < count; i++)
        {
            sorted[i] = _items[order[i]];
            movedTo[order[i]] = i;
        }

        var selected = new int[SelectedIndices.Count];
        for (int i = 0; i < selected.Length; i++)
        {
            selected[i] = SelectedIndices[i];
        }

        int lead = LeadIndex;

        BeginUpdate();
        try
        {
            _items.Clear();
            _items.AddRange(sorted);

            ClearSelected();
            foreach (var index in selected)
            {
                SetSelected(movedTo[index], true);
            }

            SetLead(lead >= 0 ? movedTo[lead] : -1);
            ScrollTo(HorizontalOffset, 0f);
        }
        finally
        {
            EndUpdate();
        }
    }

    /// <summary>The column under a point in this control's coordinates, or -1 when there is none.</summary>
    public int ColumnFromPoint(SKPoint point)
    {
        if (_view != XPlatView.Details || !XPlatGeometry.Contains(HeaderBand, point))
        {
            return -1;
        }

        for (int index = 0; index < _columns.Count; index++)
        {
            if (point.X < CellRect(index).Right)
            {
                return index;
            }
        }

        return -1;
    }

    /// <summary>
    /// The column whose right edge is under a point and can be dragged, or -1 when there is none.
    /// </summary>
    private int ResizeColumnFromPoint(SKPoint point)
    {
        if (!_allowColumnResize || !Enabled || _view != XPlatView.Details || !XPlatGeometry.Contains(HeaderBand, point))
        {
            return -1;
        }

        // A stretched last column has no edge of its own to move: it takes whatever space is left.
        int count = _columns.Count;
        if (count > 0 && CellRect(count - 1).Width > _columns[count - 1].Width)
        {
            count--;
        }

        for (int index = 0; index < count; index++)
        {
            if (MathF.Abs(point.X - CellRect(index).Right) <= ResizeGrabSize)
            {
                return index;
            }
        }

        return -1;
    }

    /// <summary>Shows the left-right arrow over a column edge that can be dragged.</summary>
    public override XPlatCursor GetCursorAt(SKPoint pointInLocal)
    {
        if (_resizeColumn >= 0 || ResizeColumnFromPoint(pointInLocal) >= 0)
        {
            return XPlatCursor.SizeWE;
        }

        return base.GetCursorAt(pointInLocal);
    }

    private bool TryBeginColumnResize(SKPoint point)
    {
        int column = ResizeColumnFromPoint(point);
        if (column < 0)
        {
            return false;
        }

        _resizeColumn = column;
        _resizeOrigin = point.X;
        _resizeOriginWidth = _columns[column].Width;
        _hotColumn = -1;
        CaptureMouse();
        return true;
    }

    private void ResizeColumnTo(float x)
    {
        if (_resizeColumn < 0 || _resizeColumn >= _columns.Count)
        {
            return;
        }

        _columns[_resizeColumn].Width = MathF.Max(MinColumnWidth, _resizeOriginWidth + (x - _resizeOrigin));
    }

    private void EndColumnResize()
    {
        if (_resizeColumn < 0)
        {
            return;
        }

        _resizeColumn = -1;
        ReleaseMouseCapture();
    }

    public override string GetItemText(int index) => _items[index].Text;

    public override object? GetItem(int index) => _items[index];

    /// <summary>The area the columns are laid out in, which the header and the rows share.</summary>
    /// <remarks>
    /// Both have to agree on where a column starts, so neither works it out from its own rectangle:
    /// the rows live inside the scroll padding and the header does not. <see cref="RowsRect"/> is
    /// exactly the rows' area.
    /// </remarks>
    private SKRect ColumnsRect => RowsRect;

    /// <summary>The gap between the border and the rows, so that rows never paint over the border.</summary>
    private float Inset => BorderWidth > 0f || BorderColor.Alpha > 0 ? 1f : 0f;

    private float TotalColumnWidth
    {
        get
        {
            float width = 0f;
            foreach (var column in _columns)
            {
                width += column.Width;
            }

            return width;
        }
    }

    private SKRect HeaderBand
    {
        get
        {
            var client = ClientRect;
            float top = client.Top + Inset;
            float bottom = MathF.Min(top + HeaderHeight, client.Bottom);

            // The horizontal extent comes from the rows' own area, so that a column starts in the
            // same place in the header as it does in a row.
            return new SKRect(ColumnsRect.Left, top, ColumnsRect.Right, bottom);
        }
    }

    /// <summary>The header band is outside the scrolling area, so the rows start below it.</summary>
    /// <remarks>
    /// It is worked out from the client rectangle rather than from <see cref="HeaderBand"/>, because
    /// the header takes its width from <see cref="RowsRect"/> and that is measured against this.
    /// </remarks>
    protected override SKRect ScrollClient
    {
        get
        {
            var client = ClientRect;
            if (_view != XPlatView.Details)
            {
                return client;
            }

            float top = MathF.Min(client.Top + Inset + HeaderHeight, client.Bottom);
            return new SKRect(client.Left, top, client.Right, client.Bottom);
        }
    }

    /// <summary>Rows are inset by the border so that they do not paint over it.</summary>
    protected override XPlatThickness ScrollPadding => Inset > 0f ? new XPlatThickness(Inset) : XPlatThickness.Empty;

    protected override float MeasureItemWidth()
    {
        if (_view == XPlatView.Details)
        {
            return TotalColumnWidth;
        }

        float width = 0f;
        foreach (var item in _items)
        {
            width = MathF.Max(width, Font.MeasureWidth(item.Text));
        }

        return width + (TextInset * 2f);
    }

    protected override void OnPaint(SKCanvas canvas, SKRect rect)
    {
        base.OnPaint(canvas, rect);

        if (_view == XPlatView.Details)
        {
            DrawHeader(canvas);
        }
    }

    protected override void OnDrawItem(SKCanvas canvas, int index, SKRect bounds, XPlatDrawItemState state, SKColor textColor)
    {
        var item = _items[index];

        if (_view == XPlatView.List || _columns.Count == 0)
        {
            DrawItemText(canvas, bounds, item.Text, textColor);

            if ((state & XPlatDrawItemState.Focused) != 0)
            {
                DrawFocusRectangle(canvas, bounds, textColor);
            }

            return;
        }

        for (int column = 0; column < _columns.Count; column++)
        {
            var cell = CellRect(column, bounds.Top, bounds.Bottom);
            DrawCellText(canvas, cell, item.GetCell(column), _columns[column].Alignment, textColor);

            if (_gridLines && column > 0)
            {
                XPlatPainter.Fill(canvas, new SKRect(cell.Left, cell.Top + 2f, cell.Left + 1f, cell.Bottom - 2f), XPlatTheme.Border);
            }
        }

        if (_gridLines)
        {
            XPlatPainter.Fill(canvas, new SKRect(bounds.Left, bounds.Bottom - 1f, bounds.Right, bounds.Bottom), XPlatTheme.Border);
        }

        if ((state & XPlatDrawItemState.Focused) != 0)
        {
            DrawFocusRectangle(canvas, bounds, textColor);
        }
    }

    protected override void OnMouseDown(XPlatMouseEventArgs e)
    {
        base.OnMouseDown(e);

        if (e.Button != XPlatMouseButtons.Left)
        {
            return;
        }

        if (TryBeginColumnResize(e.Location))
        {
            Focus();
            return;
        }

        int column = ColumnFromPoint(e.Location);
        if (column < 0)
        {
            return;
        }

        Focus();
        OnColumnClick(column);
    }

    protected override void OnMouseMove(XPlatMouseEventArgs e)
    {
        base.OnMouseMove(e);

        if (_resizeColumn >= 0)
        {
            ResizeColumnTo(e.Location.X);
            return;
        }

        int column = ColumnFromPoint(e.Location);
        if (column == _hotColumn)
        {
            return;
        }

        _hotColumn = column;
        Invalidate(HeaderBand);
    }

    protected override void OnMouseUp(XPlatMouseEventArgs e)
    {
        base.OnMouseUp(e);

        if (e.Button == XPlatMouseButtons.Left)
        {
            EndColumnResize();
        }
    }

    protected override void OnMouseLeave()
    {
        base.OnMouseLeave();

        if (_hotColumn < 0)
        {
            return;
        }

        _hotColumn = -1;
        Invalidate(HeaderBand);
    }

    protected override void OnLostFocus()
    {
        base.OnLostFocus();
        EndColumnResize();
    }

    /// <summary>
    /// Sorts the clicked column, unless a handler cancels it. Clicking the column that is already
    /// sorted turns it around.
    /// </summary>
    protected virtual void OnColumnClick(int columnIndex)
    {
        var args = new XPlatListViewColumnClickEventArgs(_columns[columnIndex], columnIndex);
        _columnClick?.Invoke(this, args);

        if (args.Cancel)
        {
            return;
        }

        if (_sortColumn == columnIndex)
        {
            SortOrder = _sortOrder == XPlatSortOrder.Ascending
                ? XPlatSortOrder.Descending
                : XPlatSortOrder.Ascending;
            return;
        }

        SortColumn = columnIndex;
        SortOrder = XPlatSortOrder.Ascending;
    }

    /// <summary>Called when a column changed, so that the rows can be measured again.</summary>
    internal void OnColumnChanged() => NotifyItemsChanged();

    /// <summary>Re-sorts when the column that just got a new comparer is the one being sorted by.</summary>
    internal void OnColumnComparerChanged(XPlatListViewColumn column)
    {
        if (_sortOrder == XPlatSortOrder.None || _sortColumn < 0 || _sortColumn >= _columns.Count)
        {
            return;
        }

        if (ReferenceEquals(_columns[_sortColumn], column))
        {
            Sort();
        }
    }

    private SKRect CellRect(int index) => CellRect(index, HeaderBand.Top, HeaderBand.Bottom);

    private SKRect CellRect(int index, float top, float bottom)
    {
        float left = ColumnsRect.Left - HorizontalOffset;
        float right = ColumnsRect.Left + MathF.Max(ColumnsRect.Width, TotalColumnWidth);

        for (int column = 0; column < index; column++)
        {
            left += _columns[column].Width;
        }

        float width = _columns[index].Width;
        if (index == _columns.Count - 1)
        {
            width = MathF.Max(width, right - left);
        }

        return new SKRect(left, top, left + width, bottom);
    }

    private void DrawHeader(SKCanvas canvas)
    {
        var band = HeaderBand;
        if (band.Width <= 0f || band.Height <= 0f)
        {
            return;
        }

        var back = !Enabled
            ? XPlatTheme.DisabledBackground
            : _hotColumn >= 0 ? XPlatTheme.SecondaryHover : XPlatTheme.Secondary;

        XPlatPainter.Fill(canvas, band, back);

        canvas.Save();
        canvas.ClipRect(band);

        for (int index = 0; index < _columns.Count; index++)
        {
            var column = _columns[index];
            var cell = CellRect(index, band.Top, band.Bottom);

            if (index == _hotColumn)
            {
                XPlatPainter.Fill(canvas, cell, XPlatTheme.SecondaryPressed);
            }

            bool sorted = index == _sortColumn && _sortOrder != XPlatSortOrder.None;
            var color = !Enabled
                ? XPlatTheme.DisabledForeground
                : sorted ? XPlatTheme.Foreground : XPlatTheme.MutedForeground;

            DrawCellText(canvas, cell, column.Text, column.Alignment, color, sorted ? 12f : 0f);

            if (sorted)
            {
                DrawSortGlyph(canvas, cell, _sortOrder == XPlatSortOrder.Ascending, color);
            }

            if (index > 0)
            {
                XPlatPainter.Fill(canvas, new SKRect(cell.Left, band.Top + 4f, cell.Left + 1f, band.Bottom - 4f), XPlatTheme.Border);
            }
        }

        canvas.Restore();

        XPlatPainter.Fill(canvas, new SKRect(band.Left, band.Bottom - 1f, band.Right, band.Bottom), XPlatTheme.Border);
    }

    /// <summary>
    /// Draws a cell's text, trimmed with an ellipsis when it is wider than the cell so that it
    /// cannot run into the next column.
    /// </summary>
    private void DrawCellText(
        SKCanvas canvas,
        SKRect cell,
        string text,
        XPlatContentAlignment alignment,
        SKColor color,
        float reservedRight = 0f)
    {
        if (text.Length == 0)
        {
            return;
        }

        var bounds = new SKRect(cell.Left + TextInset, cell.Top, cell.Right - TextInset - reservedRight, cell.Bottom);
        if (bounds.Width <= 0f)
        {
            return;
        }

        XPlatTextRenderer.Draw(canvas, Trim(text, bounds.Width), Font, color, bounds, alignment);
    }

    private string Trim(string text, float maxWidth)
    {
        if (Font.MeasureWidth(text) <= maxWidth)
        {
            return text;
        }

        const string Ellipsis = "\u2026";
        float ellipsisWidth = Font.MeasureWidth(Ellipsis);
        if (ellipsisWidth > maxWidth)
        {
            return string.Empty;
        }

        int low = 0;
        int high = text.Length;
        while (low < high)
        {
            int middle = (low + high + 1) / 2;
            if (Font.MeasureWidth(text[..middle]) + ellipsisWidth <= maxWidth)
            {
                low = middle;
            }
            else
            {
                high = middle - 1;
            }
        }

        return text[..low] + Ellipsis;
    }

    private static void DrawSortGlyph(SKCanvas canvas, SKRect cell, bool ascending, SKColor color)
    {
        const float Size = 5f;
        float centreX = cell.Right - TextInset - Size;
        float centreY = (cell.Top + cell.Bottom) / 2f;

        using var builder = new SKPathBuilder();
        if (ascending)
        {
            builder.MoveTo(centreX - Size, centreY + (Size / 2f));
            builder.LineTo(centreX + Size, centreY + (Size / 2f));
            builder.LineTo(centreX, centreY - (Size / 2f));
        }
        else
        {
            builder.MoveTo(centreX - Size, centreY - (Size / 2f));
            builder.LineTo(centreX + Size, centreY - (Size / 2f));
            builder.LineTo(centreX, centreY + (Size / 2f));
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

    /// <summary>The rows of an <see cref="XPlatListView"/>, in the order they are painted.</summary>
    public sealed class XPlatListViewItemCollection : IList<XPlatListViewItem>
    {
        private readonly XPlatListView _owner;
        private readonly List<XPlatListViewItem> _items;

        internal XPlatListViewItemCollection(XPlatListView owner, List<XPlatListViewItem> items)
        {
            _owner = owner;
            _items = items;
        }

        public XPlatListViewItem this[int index]
        {
            get => _items[index];
            set
            {
                ArgumentNullException.ThrowIfNull(value);

                var previous = _items[index];
                if (ReferenceEquals(previous, value))
                {
                    return;
                }

                previous.Owner = null;
                value.Owner = _owner;
                _items[index] = value;
                _owner.NotifyItemsChanged();
            }
        }

        public int Count => _items.Count;

        public bool IsReadOnly => false;

        public void Add(XPlatListViewItem item)
        {
            ArgumentNullException.ThrowIfNull(item);
            item.Owner = _owner;
            _items.Add(item);
            _owner.NotifyItemsChanged();
        }

        /// <summary>Adds several items with a single layout and repaint.</summary>
        public void AddRange(IEnumerable<XPlatListViewItem> items)
        {
            ArgumentNullException.ThrowIfNull(items);

            _owner.BeginUpdate();
            try
            {
                foreach (var item in items)
                {
                    ArgumentNullException.ThrowIfNull(item);
                    item.Owner = _owner;
                    _items.Add(item);
                }
            }
            finally
            {
                _owner.EndUpdate();
            }
        }

        public void Insert(int index, XPlatListViewItem item)
        {
            ArgumentNullException.ThrowIfNull(item);
            item.Owner = _owner;
            _items.Insert(index, item);
            _owner.NotifyItemsChanged();
        }

        public bool Remove(XPlatListViewItem item)
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
            _items[index].Owner = null;
            _items.RemoveAt(index);
            _owner.NotifyItemsChanged();
        }

        public void Clear()
        {
            if (_items.Count == 0)
            {
                return;
            }

            foreach (var item in _items)
            {
                item.Owner = null;
            }

            _items.Clear();
            _owner.NotifyItemsChanged();
        }

        public bool Contains(XPlatListViewItem item) => _items.Contains(item);

        public void CopyTo(XPlatListViewItem[] array, int arrayIndex) => _items.CopyTo(array, arrayIndex);

        public int IndexOf(XPlatListViewItem item) => _items.IndexOf(item);

        public IEnumerator<XPlatListViewItem> GetEnumerator() => _items.GetEnumerator();

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }

    /// <summary>The columns of an <see cref="XPlatListView"/>.</summary>
    public sealed class XPlatListViewColumnCollection : IList<XPlatListViewColumn>
    {
        private readonly XPlatListView _owner;
        private readonly List<XPlatListViewColumn> _columns;

        internal XPlatListViewColumnCollection(XPlatListView owner, List<XPlatListViewColumn> columns)
        {
            _owner = owner;
            _columns = columns;
        }

        public XPlatListViewColumn this[int index]
        {
            get => _columns[index];
            set
            {
                ArgumentNullException.ThrowIfNull(value);

                var previous = _columns[index];
                if (ReferenceEquals(previous, value))
                {
                    return;
                }

                previous.Owner = null;
                value.Owner = _owner;
                _columns[index] = value;
                _owner.OnColumnChanged();
            }
        }

        public int Count => _columns.Count;

        public bool IsReadOnly => false;

        public void Add(XPlatListViewColumn column)
        {
            ArgumentNullException.ThrowIfNull(column);
            column.Owner = _owner;
            _columns.Add(column);
            _owner.OnColumnChanged();
        }

        public void Insert(int index, XPlatListViewColumn column)
        {
            ArgumentNullException.ThrowIfNull(column);
            column.Owner = _owner;
            _columns.Insert(index, column);
            _owner.OnColumnChanged();
        }

        public bool Remove(XPlatListViewColumn column)
        {
            int index = _columns.IndexOf(column);
            if (index < 0)
            {
                return false;
            }

            RemoveAt(index);
            return true;
        }

        public void RemoveAt(int index)
        {
            _columns[index].Owner = null;
            _columns.RemoveAt(index);
            _owner.OnColumnChanged();
        }

        public void Clear()
        {
            if (_columns.Count == 0)
            {
                return;
            }

            foreach (var column in _columns)
            {
                column.Owner = null;
            }

            _columns.Clear();
            _owner.OnColumnChanged();
        }

        public bool Contains(XPlatListViewColumn column) => _columns.Contains(column);

        public void CopyTo(XPlatListViewColumn[] array, int arrayIndex) => _columns.CopyTo(array, arrayIndex);

        public int IndexOf(XPlatListViewColumn column) => _columns.IndexOf(column);

        public IEnumerator<XPlatListViewColumn> GetEnumerator() => _columns.GetEnumerator();

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }
}
